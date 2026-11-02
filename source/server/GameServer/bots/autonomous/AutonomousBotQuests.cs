using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.Quests;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>One classic quest of one gamebot: the step it is on, or how often it has finished it.</summary>
[DataTable(TableName = "offline_bot_quests")]
public sealed class OfflineBotQuestRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotQuestRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Index = true)] public long BotId { get; set; }
    [DataElement(AllowDbNull = false)] public int QuestId { get; set; }
    /// <summary>True while the bot holds the quest.</summary>
    [DataElement(AllowDbNull = false)] public bool Active { get; set; }
    /// <summary>0 while the bot walks to the giver; then the DataQuest step.</summary>
    [DataElement(AllowDbNull = false)] public int Step { get; set; }
    [DataElement(AllowDbNull = false)] public int Finished { get; set; }
    [DataElement(AllowDbNull = false)] public int Abandoned { get; set; }
    /// <summary>A quest given up is not taken again before this time.</summary>
    [DataElement(AllowDbNull = false)] public string RetryUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string UpdatedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Gamebots run the classic quests. Bots are GameNPCs and DataQuest runs on GamePlayer, so a quest is compiled into a
/// <see cref="BotQuestPlan"/> and run here by the same rules: the bot walks to the giver, walks the steps, fights and
/// collects, and gets the row's real XP, coin and items. Nothing is shared with a player's copy of the quest: a bot's
/// progress lives in offline_bot_quests, and it only touches a quest monster on its own step (<see cref="MayHunt"/>).
/// The world controller owns movement; this class owns state and rewards.
/// </summary>
public static class AutonomousBotQuests
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotQuests));

    [ServerProperty("autonomous", "bot_quests", "Levelling gamebots take and finish classic quests.", true)]
    public static bool Enabled = true;

    /// <summary>Chance, when a free bot looks for its next goal, that the goal is a quest.</summary>
    public const double QuestChance = 0.35;
    /// <summary>Levelling only: at the cap there is no experience left to earn from a quest.</summary>
    public const int MaximumBotLevel = 49;
    public const long PickIntervalMilliseconds = 60_000;
    /// <summary>Longest a bot spends on one step before it gives the quest up.</summary>
    public const long StepTimeoutMilliseconds = 25 * 60_000;
    /// <summary>Failed trips to a step before the quest is given up.</summary>
    public const int MaximumStepFailures = 3;
    public static readonly TimeSpan RetryAfterAbandon = TimeSpan.FromHours(4);
    /// <summary>Free backpack slots a bot wants before taking a quest: its items must fit.</summary>
    public const int FreeSlotsToAccept = 4;

    internal static readonly LivingServerTable<(long BotId, int QuestId), OfflineBotQuestRecord> Quests =
        new("bot quest", row => (row.BotId, row.QuestId));

    public sealed record NpcSpot(string Name, ushort Region, int X, int Y, int Z, eRealm Realm, int Level, bool Hostile);

    /// <summary>A quest with what the live world says about it.</summary>
    public sealed record Entry(BotQuestPlan Plan, eRealm Realm, int HardestLevel);

    /// <summary>The quest a bot holds, with the transient state of the step it is on.</summary>
    public sealed class Run
    {
        public BotQuestPlan Plan { get; init; }
        public OfflineBotQuestRecord Record { get; init; }
        public int Step => Record.Step;
        public BotQuestStep Current => Plan.Step(Record.Step);
        public long StepStartedTick;
        public int StepFailures;
        public long WaitUntilTick;
        public bool Spawned;
        public string LastKillId;
    }

    /// <summary>Where the current step sends the bot.</summary>
    public sealed record Goal(string Name, ushort Region, int X, int Y, int Z, int Radius, bool Hunts);

    private sealed class Catalog
    {
        public Entry[] Entries = [];
        public Dictionary<int, Entry> ById = new();
        public Dictionary<string, List<NpcSpot>> Spots = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Places = new(StringComparer.OrdinalIgnoreCase);
    }

    private static volatile Catalog _catalog;
    private static int _loading;
    private static readonly ConcurrentDictionary<long, Run> Active = new();
    private static readonly ConcurrentDictionary<long, HashSet<int>> FinishedByBot = new();
    private static readonly ConcurrentDictionary<string, (bool Busy, long Until)> PlayerTargets = new(StringComparer.OrdinalIgnoreCase);

    public static int CatalogSize => _catalog?.Entries.Length ?? 0;

    #region Server lifecycle

    /// <summary>Fills the in-memory state from the saved rows (start-up, and tests).</summary>
    public static void Load(IEnumerable<OfflineBotQuestRecord> rows)
    {
        OfflineBotQuestRecord[] all = rows.ToArray();
        Quests.Load(all);
        Active.Clear();
        FinishedByBot.Clear();
        foreach (OfflineBotQuestRecord row in all.Where(row => row.Finished > 0))
            FinishedByBot.GetOrAdd(row.BotId, _ => new HashSet<int>()).Add(row.QuestId);
    }

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        GameEventMgr.AddHandler(GameLivingEvent.Dying, new DOLEventHandler(OnDying));
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        GameEventMgr.RemoveHandler(GameLivingEvent.Dying, new DOLEventHandler(OnDying));
    }

    /// <summary>A pet's kill counts for the bot that owns it; its own kills arrive through GameBot.EnemyKilled.</summary>
    private static void OnDying(DOLEvent e, object sender, EventArgs args)
    {
        if (sender is GameNPC mob && args is DyingEventArgs dying && dying.Killer is GameNPC pet &&
            pet.Brain is IControlledBrain { Owner: GameBot owner })
            OnKilled(owner, mob);
    }

    #endregion

    #region Catalog

    /// <summary>Builds the quest catalog in the background once the world stands; until then no bot takes a quest.</summary>
    private static void BeginLoad()
    {
        if (Interlocked.Exchange(ref _loading, 1) == 1)
            return;
        Task.Run(() =>
        {
            try
            {
                _catalog = BuildCatalog(GameServer.Database.SelectAllObjects<DbDataQuest>(), ClassicQuests.Current, WorldSpots());
            }
            catch (Exception exception)
            {
                Log.Error("Gamebot quest catalog failed; bots run no quests", exception);
                _catalog = new Catalog();
            }
        });
    }

    private static List<NpcSpot> WorldSpots()
    {
        var spots = new List<NpcSpot>();
        foreach (Region region in WorldMgr.GetAllRegions())
        {
            if (region == null || region.IsHousing)
                continue;
            foreach (GameObject item in region.Objects)
            {
                if (item == null || string.IsNullOrEmpty(item.Name))
                    continue;
                if (item is GameNPC npc)
                {
                    if (npc is GameBot)
                        continue;
                    bool hostile = npc.Realm == eRealm.None && (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0;
                    Point3D home = npc.SpawnPoint ?? new Point3D(npc.X, npc.Y, npc.Z);
                    spots.Add(new NpcSpot(npc.Name, region.ID, home.X, home.Y, home.Z, npc.Realm, npc.Level, hostile));
                }
                else if (item is GameStaticItem)
                    spots.Add(new NpcSpot(item.Name, region.ID, item.X, item.Y, item.Z, item.Realm, 0, false));
            }
        }
        return spots;
    }

    /// <summary>
    /// The quests a bot can run, and a line saying what share of the classic quests that is. Pure but for the log.
    /// </summary>
    private static Catalog BuildCatalog(IEnumerable<DbDataQuest> rows, ClassicQuests.Config config, IEnumerable<NpcSpot> world)
    {
        var catalog = new Catalog();
        foreach (NpcSpot spot in world)
        {
            if (!catalog.Spots.TryGetValue(spot.Name, out List<NpcSpot> list))
                catalog.Spots[spot.Name] = list = new List<NpcSpot>();
            list.Add(spot);
        }
        var skips = new Dictionary<string, int>();
        void Skip(string reason) => skips[reason] = skips.GetValueOrDefault(reason) + 1;
        var plans = new List<BotQuestPlan>();
        int total = 0;
        foreach (DbDataQuest row in rows)
        {
            if (row.StartType != (byte)DataQuest.eStartType.Standard || !config.Quests.TryGetValue(row.ID, out ClassicQuests.QuestInfo info))
                continue;
            total++;
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(row, info, out string skip);
            if (plan == null)
                Skip(skip);
            else if (MissingWorldReason(plan, catalog) is { } missing)
                Skip(missing);
            else
                plans.Add(plan);
        }
        plans = AutonomousBotQuestPlanner.KeepRunnableChains(plans, out int chains);
        if (chains > 0)
            skips["depends on a quest bots cannot run"] = chains;
        var entries = new List<Entry>(plans.Count);
        foreach (BotQuestPlan plan in plans)
        {
            NpcSpot giver = FindSpots(catalog, plan.GiverName, plan.GiverRegion).First();
            int hardest = plan.KillTargets().SelectMany(target => FindSpots(catalog, target.Name, target.Region))
                .Select(spot => spot.Level).DefaultIfEmpty(0).Max();
            entries.Add(new Entry(plan, giver.Realm, hardest));
        }
        catalog.Entries = entries.ToArray();
        catalog.ById = entries.ToDictionary(entry => entry.Plan.Id);
        if (Log.IsInfoEnabled)
            Log.Info($"AUTONOMOUS_QUEST_CATALOG classic={total} runnable={entries.Count} " +
                     $"coverage={(total == 0 ? 0 : 100.0 * entries.Count / total):F1}% " +
                     $"skipped=\"{string.Join("; ", skips.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}: {pair.Value}"))}\"");
        return catalog;
    }

    private static IEnumerable<NpcSpot> FindSpots(Catalog catalog, string name, ushort region)
    {
        if (string.IsNullOrEmpty(name) || !catalog.Spots.TryGetValue(name, out List<NpcSpot> spots))
            return [];
        return region == 0 ? spots : spots.Where(spot => spot.Region == region);
    }

    private static string MissingWorldReason(BotQuestPlan plan, Catalog catalog)
    {
        if (!FindSpots(catalog, plan.GiverName, plan.GiverRegion).Any())
            return "giver not in the world";
        foreach (BotQuestStep step in plan.Steps)
        {
            bool spawned = step.Spawn != null && QuestNames.Same(step.Spawn.Name, step.TargetName);
            if (step.Action is eBotQuestAction.Talk or eBotQuestAction.HandIn or eBotQuestAction.Choose or eBotQuestAction.Kill &&
                !spawned && !FindSpots(catalog, step.TargetName, step.TargetRegion).Any())
                return step.Action == eBotQuestAction.Kill ? "monster not in the world" : "step character not in the world";
            if (step.Action == eBotQuestAction.Kill && !spawned &&
                !FindSpots(catalog, step.TargetName, step.TargetRegion).Any(spot => spot.Hostile))
                return "kill target is not a monster";
            foreach (BotQuestDrop drop in step.Drops)
                if (!catalog.Spots.ContainsKey(drop.Mob))
                    return "monster not in the world";
        }
        return null;
    }

    #endregion

    #region Choosing a quest

    public static bool WantsQuest(int level, double roll) => level <= MaximumBotLevel && roll < QuestChance;

    /// <summary>Can a lone bot of this level take on this monster: a quest asks more than a grind, so up to orange.</summary>
    public static bool CanFight(int level, int monsterLevel) =>
        monsterLevel <= 0 || ConLevels.GetConColor(ConLevels.GetConLevel(Math.Clamp(level, 0, 127), Math.Clamp(monsterLevel, 0, 127))) <= ConColor.ORANGE;

    /// <summary>The rules of offering a quest to a bot, without the world. Pure.</summary>
    public static bool Offers(Entry entry, eRealm realm, int level, int classId, int raceId, ICollection<int> finished,
        ICollection<string> finishedNames, ICollection<int> active)
    {
        BotQuestPlan plan = entry.Plan;
        return (entry.Realm == eRealm.None || entry.Realm == realm) &&
            AutonomousBotQuestPlanner.InLevelWindow(level, plan.MinLevel, plan.MaxLevel) &&
            (plan.AllowedClasses.Count == 0 || plan.AllowedClasses.Contains(classId)) &&
            ClassicQuests.RaceAllowed(plan.Info, raceId) &&
            !finished.Contains(plan.Id) &&
            QuestDependencies.AreMet(plan.Dependencies, finishedNames, finished, active) &&
            CanFight(level, entry.HardestLevel);
    }

    /// <summary>Picks a quest for this bot: of a few that fit and that it can reach, the one nearest it.</summary>
    public static BotQuestPlan Pick(GameBot bot, Func<ushort, bool> reachable, Func<ushort, int, int, double> travelMinutes)
    {
        Catalog catalog = _catalog;
        if (catalog == null)
        {
            BeginLoad();
            return null;
        }
        if (bot?.PersistentRecord == null || bot.Inventory == null || !HasFreeSlots(bot, FreeSlotsToAccept))
            return null;
        HashSet<int> finished = FinishedByBot.TryGetValue(bot.DatabaseID, out HashSet<int> done) ? new HashSet<int>(done) : new HashSet<int>();
        var finishedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int id in finished)
            if (catalog.ById.TryGetValue(id, out Entry known))
                finishedNames.Add(known.Plan.Name);
        var active = new HashSet<int>();
        DateTime now = DateTime.UtcNow;
        List<Entry> fits = catalog.Entries.Where(entry =>
                Offers(entry, bot.Realm, bot.Level, bot.CharacterClass?.ID ?? 0, bot.PersistentRecord.RaceId, finished, finishedNames, active) &&
                MayRetry(bot.DatabaseID, entry.Plan.Id, now) &&
                PlaceReachable(catalog, entry.Plan, reachable))
            .ToList();
        if (fits.Count == 0)
            return null;
        Entry best = null;
        double bestMinutes = double.MaxValue;
        for (int i = 0; i < 6; i++)
        {
            Entry entry = fits[Random.Shared.Next(fits.Count)];
            NpcSpot giver = NearestSpot(catalog, entry.Plan.GiverName, entry.Plan.GiverRegion, bot);
            double minutes = giver == null ? double.MaxValue : travelMinutes(giver.Region, giver.X, giver.Y);
            if (minutes < bestMinutes)
            {
                bestMinutes = minutes;
                best = entry;
            }
        }
        return best?.Plan;
    }

    private static bool MayRetry(long botId, int questId, DateTime now) =>
        !Quests.TryGet((botId, questId), out OfflineBotQuestRecord row) || !DateTime.TryParse(row.RetryUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime retry) || retry.ToUniversalTime() <= now;

    private static bool PlaceReachable(Catalog catalog, BotQuestPlan plan, Func<ushort, bool> reachable)
    {
        if (!FindSpots(catalog, plan.GiverName, plan.GiverRegion).Any(spot => reachable(spot.Region)))
            return false;
        foreach (BotQuestStep step in plan.Steps)
        {
            if (step.TargetRegion != 0 && !reachable(step.TargetRegion))
                return false;
            if (step.Point != null && step.Point.Region != 0 && !reachable(step.Point.Region))
                return false;
            if (step.Spawn != null && step.Spawn.TriggerRadius > 0 && !reachable(step.Spawn.Region))
                return false;
            if (step.Action is eBotQuestAction.Talk or eBotQuestAction.HandIn or eBotQuestAction.Choose or eBotQuestAction.Kill &&
                step.Spawn == null && !FindSpots(catalog, step.TargetName, step.TargetRegion).Any(spot => reachable(spot.Region)))
                return false;
        }
        return true;
    }

    private static bool HasFreeSlots(GameBot bot, int slots) =>
        bot.Inventory.IsSlotsFree(slots, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);

    private static NpcSpot NearestSpot(Catalog catalog, string name, ushort region, GameBot bot, BotQuestPoint near = null)
    {
        NpcSpot best = null;
        long bestDistance = long.MaxValue;
        foreach (NpcSpot spot in FindSpots(catalog, name, region))
        {
            bool sameRegion = near != null ? spot.Region == near.Region : spot.Region == bot.CurrentRegionID;
            long dx = spot.X - (near?.X ?? bot.X), dy = spot.Y - (near?.Y ?? bot.Y);
            long distance = (sameRegion ? 0 : 1L << 50) + dx * dx / 2 + dy * dy / 2;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = spot;
            }
        }
        return best;
    }

    #endregion

    #region Running a quest

    /// <summary>The quest this bot holds, restored from its saved row after a restart.</summary>
    public static Run Current(GameBot bot)
    {
        if (bot == null || bot.DatabaseID <= 0)
            return null;
        if (Active.TryGetValue(bot.DatabaseID, out Run run))
            return run;
        Catalog catalog = _catalog;
        if (catalog == null)
            return null;
        OfflineBotQuestRecord row = Quests.Where(record => record.BotId == bot.DatabaseID && record.Active).FirstOrDefault();
        if (row == null)
            return null;
        if (!catalog.ById.TryGetValue(row.QuestId, out Entry entry))
        {
            Quests.Change((row.BotId, row.QuestId), () => row, saved => saved.Active = false);
            return null;
        }
        run = new Run { Plan = entry.Plan, Record = row, StepStartedTick = GameLoop.GameLoopTime };
        return Active.GetOrAdd(bot.DatabaseID, run);
    }

    /// <summary>The bot sets out for the giver; it holds the quest once it has spoken to them.</summary>
    public static Run Begin(GameBot bot, BotQuestPlan plan)
    {
        OfflineBotQuestRecord row = Quests.Change((bot.DatabaseID, plan.Id),
            () => new OfflineBotQuestRecord { BotId = bot.DatabaseID, QuestId = plan.Id },
            saved =>
            {
                saved.Active = true;
                saved.Step = 0;
                saved.RetryUtc = string.Empty;
                saved.UpdatedUtc = DateTime.UtcNow.ToString("O");
            });
        var run = new Run { Plan = plan, Record = row, StepStartedTick = GameLoop.GameLoopTime };
        Active[bot.DatabaseID] = run;
        Log.Info($"AUTONOMOUS_QUEST_CHOSEN bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} quest={plan.Id} name=\"{plan.Name}\" giver=\"{plan.GiverName}\"");
        return run;
    }

    /// <summary>Where the bot must go now, or null when the place cannot be found.</summary>
    public static Goal GoalFor(GameBot bot, Run run)
    {
        Catalog catalog = _catalog;
        if (catalog == null)
            return null;
        BotQuestPlan plan = run.Plan;
        if (run.Step == 0)
            return SpotGoal(NearestSpot(catalog, plan.GiverName, plan.GiverRegion, bot), false);
        BotQuestStep step = run.Current;
        if (step == null)
            return null;
        string hunted = step.HuntForItem(item => Carried(bot, item));
        if (hunted != null)
            return SpotGoal(NearestSpot(catalog, hunted, 0, bot, step.Point), true);
        switch (step.Action)
        {
            case eBotQuestAction.Kill:
                if (step.Spawn != null && QuestNames.Same(step.Spawn.Name, step.TargetName))
                    return step.Spawn.TriggerRadius > 0
                        ? new Goal(step.TargetName, step.Spawn.Region, step.Spawn.X, step.Spawn.Y, step.Spawn.Z, step.Spawn.TriggerRadius, true)
                        : new Goal(step.TargetName, bot.CurrentRegionID, bot.X, bot.Y, bot.Z, 0, true);
                return SpotGoal(NearestSpot(catalog, step.TargetName, step.TargetRegion, bot, step.Point), true);
            case eBotQuestAction.Talk:
            case eBotQuestAction.HandIn:
            case eBotQuestAction.Choose:
                if (step.Spawn is { Peaceful: true } && QuestNames.Same(step.Spawn.Name, step.TargetName))
                    return new Goal(step.TargetName, step.Spawn.Region, step.Spawn.X, step.Spawn.Y, step.Spawn.Z, 0, false);
                return SpotGoal(NearestSpot(catalog, step.TargetName, step.TargetRegion, bot, step.Point), false);
            default:
                // A step with no place, or anywhere in the region, is done where the bot stands.
                return step.Point == null || step.Point.Region == 0
                    ? new Goal(plan.Name, bot.CurrentRegionID, bot.X, bot.Y, bot.Z, 0, false)
                    : new Goal(plan.Name, step.Point.Region, step.Point.X, step.Point.Y, step.Point.Z, Math.Max(step.Radius, 0), false);
        }
    }

    private static Goal SpotGoal(NpcSpot spot, bool hunts) =>
        spot == null ? null : new Goal(spot.Name, spot.Region, spot.X, spot.Y, spot.Z, 0, hunts);

    public static int Carried(GameBot bot, string item) =>
        bot?.Inventory == null || string.IsNullOrEmpty(item) ? 0
            : bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                .Where(held => QuestDeliveryItems.IsCollectItem(held?.Id_nb, item)).Sum(held => Math.Max(1, held.Count));

    private static bool GiveItem(GameBot bot, string id)
    {
        DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(id);
        if (template == null)
        {
            Log.Warn($"AUTONOMOUS_QUEST_ITEM_MISSING id=\"{id}\"");
            return true; // nothing to hand: a missing template must not hold the quest
        }
        return GiveTemplate(bot, template, template.Name) != null;
    }

    private static DbInventoryItem GiveTemplate(GameBot bot, DbItemTemplate template, string source)
    {
        DbInventoryItem item = GameInventoryItem.Create(template);
        if (item == null || !bot.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item))
            return null;
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        return item;
    }

    /// <summary>The bot has spoken to the giver and holds the quest.</summary>
    public static void Accept(GameBot bot, Run run)
    {
        SetStep(bot, run, 1);
        Log.Info($"AUTONOMOUS_QUEST_ACCEPTED bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} quest={run.Plan.Id} name=\"{run.Plan.Name}\"");
        Progress(bot);
    }

    private static void SetStep(GameBot bot, Run run, int number)
    {
        Quests.Change((run.Record.BotId, run.Record.QuestId), () => run.Record, saved =>
        {
            saved.Step = number;
            saved.UpdatedUtc = DateTime.UtcNow.ToString("O");
        });
        run.StepStartedTick = GameLoop.GameLoopTime;
        run.StepFailures = 0;
        run.WaitUntilTick = 0;
        run.Spawned = false;
        BotQuestStep step = run.Current;
        if (step == null)
            return;
        foreach (string item in step.GivenAtStart)
            if (Carried(bot, item) == 0)
                GiveItem(bot, item);
    }

    private static void Progress(GameBot bot)
    {
        AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Objective);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
    }

    /// <summary>
    /// The current step is done: its item is taken, its reward paid, and the quest moves on or finishes. False when the
    /// backpack cannot take what comes next; the step then stays as it is, as a player's would.
    /// </summary>
    public static bool CompleteStep(GameBot bot, Run run, string chosenReward = null)
    {
        BotQuestStep step = run.Current;
        if (step == null)
            return false;
        int slotsNeeded = (string.IsNullOrEmpty(step.GivenAtEnd) ? 0 : 1) + (chosenReward == null ? 0 : 1) +
                          (step.Finishes ? run.Plan.FinalRewards.Length : 0) + (run.Plan.Step(step.Number + 1)?.GivenAtStart.Length ?? 0);
        if (slotsNeeded > 0 && !HasFreeSlots(bot, slotsNeeded))
            return false;

        string traded = TakeNeededItem(bot, step);
        PayStep(bot, step);
        if (!string.IsNullOrEmpty(step.GivenAtEnd))
            GiveItem(bot, step.GivenAtEnd);
        if (!string.IsNullOrEmpty(traded))
            GiveReward(bot, traded);
        if (chosenReward != null)
            GiveReward(bot, chosenReward);
        Log.Info($"AUTONOMOUS_QUEST_STEP bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} quest={run.Plan.Id} step={step.Number} action={step.Action} target=\"{step.TargetName}\"");
        if (step.Finishes || step.Number >= run.Plan.Steps.Length)
            Finish(bot, run);
        else
            SetStep(bot, run, step.Number + 1);
        Progress(bot);
        return true;
    }

    /// <summary>Takes what the step wants; a trade step returns the item it pays with.</summary>
    private static string TakeNeededItem(GameBot bot, BotQuestStep step)
    {
        if (step.Trades != null)
        {
            foreach ((string wanted, string output) in step.Trades)
                if (Carried(bot, wanted) > 0)
                {
                    RemoveItems(bot, wanted, 1);
                    return output;
                }
            return null;
        }
        if (step.NeededItem.Length > 0 && (step.Action == eBotQuestAction.HandIn || step.ConsumesItem))
            RemoveItems(bot, step.NeededItem, step.NeededCount);
        return null;
    }

    private static void RemoveItems(GameBot bot, string id, int count)
    {
        foreach (DbInventoryItem held in bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).ToList())
        {
            if (count <= 0 || !QuestDeliveryItems.IsCollectItem(held?.Id_nb, id))
                continue;
            int take = Math.Min(count, Math.Max(1, held.Count));
            if (held.Count > take ? bot.Inventory.RemoveCountFromStack(held, take) : bot.Inventory.RemoveItem(held))
                count -= take;
        }
        AutonomousBotEconomy.MarkInventoryChanged(bot);
    }

    private static void PayStep(GameBot bot, BotQuestStep step)
    {
        long experience = ClassicQuests.ScaleXP(step.XpShare, LevelSpan(bot.Level), step.RewardXp);
        if (experience > 0)
            bot.GainExperience(new GainedExperienceEventArgs(experience, 0, 0, 0, 0, 0, false, false, eXPSource.Quest));
        if (step.RewardRp > 0)
            bot.GainRealmPoints(step.RewardRp);
        if (step.RewardMoney > 0)
            AutonomousBotEconomy.AddMoney(bot.DatabaseID, step.RewardMoney);
    }

    /// <summary>Experience from this level to the next; GamePlayer.GetExperienceAmountForLevel(n) is the total for n + 1.</summary>
    public static long LevelSpan(int level) =>
        level >= GamePlayer.MAX_LEVEL ? 0 : GamePlayer.GetExperienceAmountForLevel(level) - GamePlayer.GetExperienceAmountForLevel(level - 1);

    private static void GiveReward(GameBot bot, string id)
    {
        DbItemTemplate template = ClassicQuests.RewardFor(bot.CharacterClass?.ID ?? 0, GameServer.Database.FindObjectByKey<DbItemTemplate>(id));
        if (template == null)
            return;
        DbInventoryItem item = GiveTemplate(bot, template, "quest");
        if (item != null)
            AutonomousBotEconomy.TryEquipOwnedUpgrade(bot, item, "quest");
    }

    /// <summary>The reward a bot names at a Choose step: the one that betters what it wears, else the first it can use.</summary>
    public static string ChooseReward(GameBot bot, BotQuestStep step)
    {
        string best = null;
        int bestGain = int.MinValue;
        foreach (string id in step.Choices?.Values ?? [])
        {
            DbItemTemplate template = ClassicQuests.RewardFor(bot.CharacterClass?.ID ?? 0, GameServer.Database.FindObjectByKey<DbItemTemplate>(id));
            if (template == null || GameInventoryItem.Create(template) is not DbInventoryItem item)
                continue;
            int gain = AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, item, out _) ? AutonomousBotEconomy.EquipmentValue(item) : 0;
            if (gain > bestGain)
            {
                bestGain = gain;
                best = id;
            }
        }
        return best;
    }

    private static void Finish(GameBot bot, Run run)
    {
        foreach (string reward in run.Plan.FinalRewards)
            GiveReward(bot, reward);
        ClearQuestItems(bot, run.Plan);
        Quests.Change((run.Record.BotId, run.Record.QuestId), () => run.Record, saved =>
        {
            saved.Active = false;
            saved.Step = 0;
            saved.Finished++;
            saved.UpdatedUtc = DateTime.UtcNow.ToString("O");
        });
        HashSet<int> finished = FinishedByBot.GetOrAdd(bot.DatabaseID, _ => new HashSet<int>());
        lock (finished)
            finished.Add(run.Plan.Id);
        Active.TryRemove(bot.DatabaseID, out _);
        Log.Info($"AUTONOMOUS_QUEST_DONE bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} quest={run.Plan.Id} name=\"{run.Plan.Name}\" " +
                 $"xp={run.Plan.Steps.Sum(step => step.RewardXp)} copper={run.Plan.Steps.Sum(step => step.RewardMoney)} rewards={run.Plan.FinalRewards.Length}");
    }

    /// <summary>Gives the quest up: it is not taken again for a while, and its errand items go with it.</summary>
    public static void Abandon(GameBot bot, Run run, string reason)
    {
        int step = run.Step;
        Quests.Change((run.Record.BotId, run.Record.QuestId), () => run.Record, saved =>
        {
            saved.Active = false;
            saved.Step = 0;
            saved.Abandoned++;
            saved.RetryUtc = DateTime.UtcNow.Add(RetryAfterAbandon).ToString("O");
            saved.UpdatedUtc = DateTime.UtcNow.ToString("O");
        });
        Active.TryRemove(bot.DatabaseID, out _);
        ClearQuestItems(bot, run.Plan);
        Log.Info($"AUTONOMOUS_QUEST_ABANDONED bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} quest={run.Plan.Id} step={step} reason=\"{reason}\"");
        bot.MarkAutonomousStateDirty();
    }

    /// <summary>A trip to the step failed (a route, an empty spot, a death). Enough of them end the quest.</summary>
    public static void NoteStepFailure(GameBot bot, string reason)
    {
        if (Current(bot) is not Run run)
            return;
        if (++run.StepFailures >= MaximumStepFailures)
            Abandon(bot, run, reason);
    }

    public static bool StepTimedOut(Run run, long now) => now - run.StepStartedTick >= StepTimeoutMilliseconds;

    private static IEnumerable<string> QuestItemIds(BotQuestPlan plan) => plan.Steps.SelectMany(step =>
        step.GivenAtStart.Append(step.GivenAtEnd).Append(step.NeededItem)
            .Concat(step.Drops.Select(drop => drop.Item)).Concat(step.Trades?.Keys ?? []))
        .Where(id => !string.IsNullOrEmpty(id));

    private static void ClearQuestItems(GameBot bot, BotQuestPlan plan)
    {
        var rewards = new HashSet<string>(plan.FinalRewards, StringComparer.OrdinalIgnoreCase);
        foreach (string id in QuestItemIds(plan).Distinct(StringComparer.OrdinalIgnoreCase).Where(id => !rewards.Contains(id)))
            if (Carried(bot, id) > 0)
                RemoveItems(bot, id, int.MaxValue);
    }

    /// <summary>An item the bot's current quest needs: it is neither sold nor posted while the quest runs.</summary>
    public static bool IsHeldForQuest(GameBot bot, DbInventoryItem item) =>
        item != null && bot != null && Active.TryGetValue(bot.DatabaseID, out Run run) &&
        QuestItemIds(run.Plan).Any(id => string.Equals(id, item.Id_nb, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bot killed something: its Kill step ends, or a monster its step wants drops the item (one kill counts once).</summary>
    public static void OnKilled(GameBot bot, GameLiving enemy)
    {
        if (bot == null || enemy == null || !Active.TryGetValue(bot.DatabaseID, out Run run) || run.Current is not BotQuestStep step)
            return;
        string killId = $"{enemy.ObjectID}:{enemy.InternalID}:{GameLoop.GameLoopTime / 1000}";
        lock (run)
        {
            if (run.LastKillId == killId)
                return;
            run.LastKillId = killId;
        }
        foreach (BotQuestDrop drop in step.Drops)
        {
            if (QuestNames.Same(drop.Mob, enemy.Name) && Util.Chance(drop.Chance) && HasFreeSlots(bot, 1))
            {
                GiveItem(bot, drop.Item);
                AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Objective);
                return;
            }
        }
        if (step.Action != eBotQuestAction.Kill || !QuestNames.Same(step.TargetName, enemy.Name) ||
            step.TargetRegion != 0 && step.TargetRegion != enemy.CurrentRegionID)
            return;
        if (step.GivenChance < 100 && !Util.Chance(step.GivenChance))
            return; // the step's item did not drop, so the step stays, as DataQuest's does
        CompleteStep(bot, run);
    }

    #endregion

    #region Quest monsters

    /// <summary>
    /// The only way past the rule that gamebots leave quest monsters alone: the monster is what this bot's own current
    /// step asks for, and no real player is on a step that wants it. An event monster must be the one that came for this
    /// bot. Everything else about the rule stands for every other bot.
    /// </summary>
    public static bool MayHunt(GameBot bot, GameNPC npc)
    {
        if (bot == null || npc == null || !Active.TryGetValue(bot.DatabaseID, out Run run) || run.Current is not BotQuestStep step ||
            !StepWants(step, npc.Name))
            return false;
        if (npc.TempProperties.GetProperty<object>(ClassicQuests.EventSpawnProperty) != null)
            return ClassicQuests.IsEventSpawnOf(npc, bot);
        return !PlayerNeeds(npc.Name);
    }

    public static bool StepWants(BotQuestStep step, string name) =>
        step.Action == eBotQuestAction.Kill && QuestNames.Same(step.TargetName, name) ||
        step.Drops.Any(drop => QuestNames.Same(drop.Mob, name));

    /// <summary>A real player online is on a step that wants this monster (checked at most every 30 seconds per name).</summary>
    private static bool PlayerNeeds(string name)
    {
        long now = GameLoop.GameLoopTime;
        if (PlayerTargets.TryGetValue(name, out var known) && now < known.Until)
            return known.Busy;
        bool busy = false;
        foreach (GamePlayer player in ClientService.Instance.GetPlayers())
            if (player?.QuestList != null && player.QuestList.Keys.OfType<DataQuest>().Any(quest => QuestNames.Same(quest.TargetName, name)))
            {
                busy = true;
                break;
            }
        PlayerTargets[name] = (busy, now + 30_000);
        return busy;
    }

    #endregion

    /// <summary>Tests only.</summary>
    public static void SetCatalogForTests(IEnumerable<Entry> entries, IEnumerable<NpcSpot> spots)
    {
        var catalog = new Catalog { Entries = entries.ToArray() };
        catalog.ById = catalog.Entries.ToDictionary(entry => entry.Plan.Id);
        foreach (NpcSpot spot in spots)
        {
            if (!catalog.Spots.TryGetValue(spot.Name, out List<NpcSpot> list))
                catalog.Spots[spot.Name] = list = new List<NpcSpot>();
            list.Add(spot);
        }
        _catalog = catalog;
        Interlocked.Exchange(ref _loading, 1);
        Active.Clear();
        FinishedByBot.Clear();
    }
}
