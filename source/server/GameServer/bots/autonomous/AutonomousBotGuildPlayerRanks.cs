using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>A real player's standing in the bot guild they are in (AutonomousBotGuildPlayerRanks).</summary>
[DataTable(TableName = "offline_bot_guild_players")]
public sealed class OfflineBotGuildPlayerRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotGuildPlayerRecord)MemberwiseClone();

    /// <summary>The character's database id (DbCoreCharacter.ObjectId).</summary>
    [DataElement(AllowDbNull = false, Unique = true, Varchar = 100)] public string PlayerId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 64)] public string PlayerName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Index = true, Varchar = 64)] public string GuildId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string JoinedUtc { get; set; } = string.Empty;
    /// <summary>Minutes online while in the guild.</summary>
    [DataElement(AllowDbNull = false)] public int Minutes { get; set; }
    /// <summary>Minutes online in a group with at least one guildmate (bot or player).</summary>
    [DataElement(AllowDbNull = false)] public int GroupedMinutes { get; set; }
    /// <summary>The player's lifetime deed counts when the row was made, so only deeds done in the guild count.</summary>
    [DataElement(AllowDbNull = false)] public int BaseRvrKills { get; set; }
    [DataElement(AllowDbNull = false)] public int BaseNamedKills { get; set; }
}

/// <summary>
/// Real players in a bot guild climb its ranks (owner request 2026-11-18: "up to 0"). Players join at rank 8
/// (AutonomousBotRealmGuilds.PlayerJoinRank) and bots only ever hold 9, 1 and 0, so a player's ladder runs through
/// the ranks bots leave empty and can end at rank 0 beside the bot leader. A minute timer counts time in the guild
/// and time grouped with guildmates; deeds come from the realm's reputation record (AutonomousPlayerDeeds), counted
/// from the day the player joined. Promotion only: nobody is demoted, and a rank set by hand (/gc promote) stays.
/// Leaving the guild and joining another starts over; rejoining the same guild with nothing in between keeps the row.
/// </summary>
public static class AutonomousBotGuildPlayerRanks
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>One rung: the rank and what it needs (hours are before BOT_GUILD_PLAYER_RANK_PACE).</summary>
    public readonly record struct Step(int Rank, double Hours, double GroupedHours, int MinLevel, int Deeds);

    /// <summary>Where a player stands now.</summary>
    public readonly record struct Standing(int Rank, int Level, int Minutes, int GroupedMinutes, int Deeds);

    /// <summary>
    /// The ladder, lowest step first. Rank rights (GuildMgr.CreateRank): 7 emblem, 5 alliance chat, 3 officer chat,
    /// 2 invite and promote, 1 claim and upgrade keeps, 0 everything (bank, dues, alliances, remove).
    /// </summary>
    public static readonly Step[] Ladder =
    [
        new(7, 2, 0, 1, 0),
        new(5, 8, 2, 20, 0),
        new(3, 20, 6, 40, 1),
        new(2, 40, 12, 50, 2),
        new(1, 80, 25, 50, 3),
        new(0, 150, 50, 50, 4),
    ];

    public const int TickMs = 60_000;
    public const int FlushIntervalMs = 60_000;
    /// <summary>Grouped hours that count as one deed in any guild, so a player can climb even with deeds off.</summary>
    public const double GroupedHoursPerDeed = 15;

    internal static readonly LivingServerTable<string, OfflineBotGuildPlayerRecord> Rows =
        new("bot guild players", row => row.PlayerId, StringComparer.Ordinal);

    private static Timer _tickTimer;
    private static Timer _flushTimer;

    public static bool Enabled => AutonomousBotRealmGuilds.Enabled && Properties.BOT_GUILD_PLAYER_PROMOTION;

    private static double Pace => Properties.BOT_GUILD_PLAYER_RANK_PACE > 0 ? Properties.BOT_GUILD_PLAYER_RANK_PACE : 1.0;

    #region Rules (pure, tested)

    /// <summary>
    /// The deeds a player has for their guild's charter: RvR kills (25 each) in an RvR guild, named kills (2 each)
    /// in a raiding guild, the primary trade skill (400/600/800/1000) in a crafting guild, and in every guild one
    /// per 15 hours grouped with guildmates. The larger of the charter count and the grouped count is used.
    /// </summary>
    public static int Deeds(BotGuildCharter charter, int rvrKills, int namedKills, int craftSkill, int groupedMinutes)
    {
        int grouped = (int)(groupedMinutes / 60.0 / GroupedHoursPerDeed);
        int charterDeeds = charter switch
        {
            BotGuildCharter.Rvr => Math.Max(0, rvrKills) / 25,
            BotGuildCharter.Raiding => Math.Max(0, namedKills) / 2,
            BotGuildCharter.Crafting => craftSkill >= 1000 ? 4 : craftSkill >= 800 ? 3 : craftSkill >= 600 ? 2 : craftSkill >= 400 ? 1 : 0,
            _ => 0,
        };
        return Math.Max(charterDeeds, grouped);
    }

    public static bool Meets(Standing standing, Step step, double pace) =>
        standing.Minutes >= step.Hours * pace * 60 &&
        standing.GroupedMinutes >= step.GroupedHours * pace * 60 &&
        standing.Level >= step.MinLevel &&
        standing.Deeds >= step.Deeds;

    /// <summary>
    /// The rank the player should hold: the highest step met in a row from the bottom (a player can't skip a
    /// step it fails), but never lower than the rank it already has. Equal to the current rank when nothing changes.
    /// </summary>
    public static int RankFor(Standing standing, double pace)
    {
        int rank = standing.Rank;
        foreach (Step step in Ladder)
        {
            if (!Meets(standing, step, pace))
                break;
            rank = Math.Min(rank, step.Rank);
        }
        return rank;
    }

    /// <summary>The next step above the player's rank, or null at the top.</summary>
    public static Step? NextStep(int rank)
    {
        foreach (Step step in Ladder)
            if (step.Rank < rank)
                return step;
        return null;
    }

    #endregion

    #region Server lifecycle

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            Rows.Load(GameServer.Database.SelectAllObjects<OfflineBotGuildPlayerRecord>());
        }
        catch (Exception exception)
        {
            Log.Warn("Bot guild player ranks: could not read saved standings", exception);
        }
        _tickTimer = new Timer(_ => OnTimer(), null, TickMs, TickMs);
        _flushTimer = new Timer(_ => Rows.Flush(), null, FlushIntervalMs, FlushIntervalMs);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _tickTimer?.Dispose();
        _tickTimer = null;
        _flushTimer?.Dispose();
        _flushTimer = null;
        Rows.Flush(wait: true);
    }

    private static void OnTimer()
    {
        if (!Enabled)
            return;
        // Guild ranks and groups belong to the game loop.
        ClientService.Instance.Post(static _ => TickOnGameLoop(), 0);
    }

    #endregion

    #region Tick

    private static void TickOnGameLoop()
    {
        try
        {
            foreach (GamePlayer player in ClientService.Instance.GetPlayers())
                Tick(player);
        }
        catch (Exception exception)
        {
            Log.Error("Bot guild player ranks: tick failed", exception);
        }
    }

    private static void Tick(GamePlayer player)
    {
        Guild guild = player?.Guild;
        if (guild == null || player.Client?.Account == null || player.GuildRank == null ||
            !AutonomousBotRealmGuilds.TryGetBotGuild(guild.GuildID, out var info))
            return;

        bool grouped = GroupedWithGuildmate(player, guild);
        OfflineBotGuildPlayerRecord row = Rows.Change(player.InternalID, () => NewRow(player, guild), r =>
        {
            if (!string.Equals(r.GuildId, guild.GuildID, StringComparison.Ordinal))
                Reset(r, player, guild);
            r.PlayerName = player.Name;
            r.Minutes++;
            if (grouped)
                r.GroupedMinutes++;
        });

        Standing standing = StandingOf(player, row, info.Charter);
        int rank = RankFor(standing, Pace);
        if (rank < standing.Rank)
            Promote(player, guild, rank);
    }

    public static Standing StandingOf(GamePlayer player, OfflineBotGuildPlayerRecord row, BotGuildCharter charter)
    {
        int rvr = 0, named = 0;
        if (AutonomousPlayerDeeds.Reputations.TryGet(AutonomousPlayerDeeds.KeyOf(player.Realm, player.Name), out var reputation))
        {
            rvr = reputation.RvrKills - row.BaseRvrKills;
            named = reputation.NamedKills - row.BaseNamedKills;
        }
        int craft = player.CraftingPrimarySkill == eCraftingSkill.NoCrafting ? 0 : player.GetCraftingSkillValue(player.CraftingPrimarySkill);
        int deeds = Deeds(charter, rvr, named, craft, row.GroupedMinutes);
        return new Standing(player.GuildRank?.RankLevel ?? AutonomousBotGuildPlanner.LowestRank, player.Level, row.Minutes, row.GroupedMinutes, deeds);
    }

    private static bool GroupedWithGuildmate(GamePlayer player, Guild guild)
    {
        Group group = player.Group;
        if (group == null)
            return false;
        foreach (GameLiving member in group.GetMembersInTheGroup())
        {
            Guild memberGuild = member switch
            {
                GamePlayer other => other.Guild,
                GameBot bot => bot.Guild,
                _ => null,
            };
            if (member != player && ReferenceEquals(memberGuild, guild))
                return true;
        }
        return false;
    }

    private static OfflineBotGuildPlayerRecord NewRow(GamePlayer player, Guild guild)
    {
        var row = new OfflineBotGuildPlayerRecord { PlayerId = player.InternalID };
        Reset(row, player, guild);
        return row;
    }

    private static void Reset(OfflineBotGuildPlayerRecord row, GamePlayer player, Guild guild)
    {
        row.GuildId = guild.GuildID;
        row.JoinedUtc = DateTime.UtcNow.ToString("O");
        row.Minutes = 0;
        row.GroupedMinutes = 0;
        AutonomousPlayerDeeds.Reputations.TryGet(AutonomousPlayerDeeds.KeyOf(player.Realm, player.Name), out var reputation);
        row.BaseRvrKills = reputation?.RvrKills ?? 0;
        row.BaseNamedKills = reputation?.NamedKills ?? 0;
    }

    private static void Promote(GamePlayer player, Guild guild, int rankLevel)
    {
        DbGuildRank rank = guild.GetRankByID(rankLevel);
        if (rank == null)
            return;
        player.GuildRank = rank;
        player.SaveIntoDatabase();
        string title = string.IsNullOrWhiteSpace(rank.Title) ? $"rank {rankLevel}" : rank.Title;
        player.Out.SendMessage($"You have been promoted to {title} (rank {rankLevel}) in {guild.Name}.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
        guild.SendMessageToGuildMembers($"{player.Name} has been promoted to {title}.", eChatType.CT_Guild, eChatLoc.CL_ChatWindow);
        AutonomousBotGuildSocial.OnPlayerPromoted(player, guild, title);
        if (Log.IsInfoEnabled)
            Log.Info($"Bot guild player ranks: {player.Name} promoted to rank {rankLevel} in \"{guild.Name}\"");
    }

    #endregion

    #region /gc standing

    /// <summary>Lines for /gc standing: the player's rank, time and deeds, and what the next rank needs.</summary>
    public static List<string> StandingLines(GamePlayer player)
    {
        var lines = new List<string>();
        Guild guild = player?.Guild;
        if (guild == null || !AutonomousBotRealmGuilds.TryGetBotGuild(guild.GuildID, out var info))
        {
            lines.Add("Only bot guilds promote by standing; your guild's leaders promote you by hand.");
            return lines;
        }
        if (!Enabled)
        {
            lines.Add("Promotion in bot guilds is switched off on this server.");
            return lines;
        }
        Rows.TryGet(player.InternalID, out var row);
        if (row == null || !string.Equals(row.GuildId, guild.GuildID, StringComparison.Ordinal))
            row = new OfflineBotGuildPlayerRecord { GuildId = guild.GuildID };
        Standing standing = StandingOf(player, row, info.Charter);
        double pace = Pace;
        lines.Add($"{guild.Name} ({info.Charter}): you are {player.GuildRank?.Title} (rank {standing.Rank}).");
        lines.Add($"In the guild {Hours(standing.Minutes)}, grouped with guildmates {Hours(standing.GroupedMinutes)}, deeds {standing.Deeds}.");
        if (NextStep(standing.Rank) is not { } next)
        {
            lines.Add("You hold the guild's highest rank.");
            return lines;
        }
        string nextTitle = guild.GetRankByID(next.Rank)?.Title ?? $"rank {next.Rank}";
        var needs = new List<string>();
        if (standing.Minutes < next.Hours * pace * 60)
            needs.Add($"{Hours((int)Math.Ceiling(next.Hours * pace * 60))} in the guild");
        if (standing.GroupedMinutes < next.GroupedHours * pace * 60)
            needs.Add($"{Hours((int)Math.Ceiling(next.GroupedHours * pace * 60))} grouped with guildmates");
        if (standing.Level < next.MinLevel)
            needs.Add($"level {next.MinLevel}");
        if (standing.Deeds < next.Deeds)
            needs.Add($"{next.Deeds} deeds ({DeedHint(info.Charter)})");
        lines.Add(needs.Count == 0
            ? $"Next: {nextTitle} (rank {next.Rank}) - you qualify; it comes within a minute."
            : $"Next: {nextTitle} (rank {next.Rank}) needs {string.Join(", ", needs)}.");
        return lines;
    }

    private static string Hours(int minutes) => minutes < 60 ? $"{minutes}m" : $"{minutes / 60}h {minutes % 60}m";

    private static string DeedHint(BotGuildCharter charter) => charter switch
    {
        BotGuildCharter.Rvr => "one per 25 RvR kills, or per 15h grouped with guildmates",
        BotGuildCharter.Raiding => "one per 2 named kills, or per 15h grouped with guildmates",
        BotGuildCharter.Crafting => "trade skill 400/600/800/1000, or one per 15h grouped with guildmates",
        _ => "one per 15h grouped with guildmates",
    };

    #endregion
}
