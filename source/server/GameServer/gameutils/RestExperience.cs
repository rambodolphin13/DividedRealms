using System;
using System.Globalization;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Rest experience (RXP), live 1.94 (DR-196). A character logged out for at least an hour gathers, for each full hour,
    /// a share of the experience its current level needs: 5% an hour at level 1 falling evenly to 1% near 50, at the full
    /// rate when it logged out in a capital city ("Restful Node") and at rest_experience_away_factor elsewhere. The pool is
    /// capped at half the experience still needed to reach level 50. Each monster or player kill then pays a bonus equal to
    /// the kill's base experience (before any bonus), up to the pool. /rested tells how rested the character is.
    /// The pool is kept in the character's DOLCharactersXCustomParam row ("DividedRealms.RestExperience").
    /// Not built: live's RXP towards champion levels (our champion levels come from realm points).
    /// </summary>
    public static class RestExperience
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(RestExperience));

        public const string Param = "DividedRealms.RestExperience";
        private const string PoolProperty = "REST_EXPERIENCE_POOL";
        private const string SavedProperty = "REST_EXPERIENCE_SAVED";
        private const string LoadedProperty = "REST_EXPERIENCE_LOADED";

        /// <summary>Capital cities, live's only "Restful Nodes": Camelot, Jordheim, Tir na Nog.</summary>
        public static bool IsRestfulRegion(ushort region) => region is 10 or 101 or 201;

        /// <summary>Percent of the current level's experience gathered per hour: 5 at level 1, 1 at level 49+. Pure.</summary>
        public static double HourlyPercent(int level)
        {
            level = Math.Clamp(level, 1, 49);
            return 5.0 - 4.0 * (level - 1) / 48.0;
        }

        /// <summary>
        /// The pool after a time offline. Nothing for under an hour; each full hour adds HourlyPercent of the level's span
        /// (times awayFactor outside a capital); the result never exceeds cap and never drops an existing pool. Pure.
        /// </summary>
        public static long PoolAfterRest(long pool, int level, TimeSpan offline, long levelSpan, long cap, bool restful, double awayFactor)
        {
            pool = Math.Max(0, pool);
            cap = Math.Max(0, cap);
            if (offline.TotalHours < 1 || levelSpan <= 0 || cap <= pool)
                return pool;
            double hours = Math.Floor(offline.TotalHours);
            double gained = hours * HourlyPercent(level) / 100.0 * levelSpan * (restful ? 1.0 : Math.Clamp(awayFactor, 0, 1));
            return (long)Math.Min(cap, pool + Math.Min(gained, cap));
        }

        /// <summary>The bonus one kill takes from the pool: its base experience, up to the pool. Pure.</summary>
        public static long BonusFor(long baseExperience, long pool) => Math.Max(0, Math.Min(baseExperience, pool));

        /// <summary>live's /rested wording, by the pool against the experience still needed for the next level. Pure.</summary>
        public static string RestedText(long pool, long toNextLevel)
        {
            if (pool <= 0)
                return "You feel tired.";
            if (toNextLevel > 0 && pool >= toNextLevel)
                return "You feel very well rested.";
            if (toNextLevel > 0 && pool * 3 >= toNextLevel)
                return "You feel well rested.";
            return "You feel rested.";
        }

        public static long Pool(GamePlayer player) => player?.TempProperties.GetProperty<long>(PoolProperty) ?? 0;

        /// <summary>Called from GamePlayer.GainExperience for a kill: takes the bonus from the pool and returns it.</summary>
        public static long TakeKillBonus(GamePlayer player, long baseExperience)
        {
            if (!ServerProperties.Properties.ENABLE_REST_EXPERIENCE || player == null || player.Level >= GamePlayer.MAX_LEVEL)
                return 0;
            long pool = Pool(player);
            long bonus = BonusFor(baseExperience, pool);
            if (bonus > 0)
                player.TempProperties.SetProperty(PoolProperty, pool - bonus);
            return bonus;
        }

        [GameServerStartedEvent]
        public static void OnServerStart(DOLEvent e, object sender, EventArgs arguments) =>
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs arguments)
        {
            try { GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered)); }
            catch (ArgumentNullException) { }
        }

        /// <summary>At login: read the saved pool, add the rest earned since the last save, tell the player.</summary>
        private static void OnPlayerEntered(DOLEvent e, object sender, EventArgs arguments)
        {
            if (sender is not GamePlayer player || !ServerProperties.Properties.ENABLE_REST_EXPERIENCE)
                return;
            if (player.TempProperties.GetProperty<bool>(LoadedProperty))
                return;
            player.TempProperties.SetProperty(LoadedProperty, true);

            string id = player.DBCharacter?.ObjectId;
            if (string.IsNullOrEmpty(id))
                return;
            try
            {
                DbCoreCharacterXCustomParam row = Find(id);
                long stored = row != null && long.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;
                player.TempProperties.SetProperty(SavedProperty, stored);

                long pool = stored;
                DateTime last = player.PreviousLoginDate;
                if (player.Level < GamePlayer.MAX_LEVEL && last > DateTime.MinValue && last < DateTime.Now)
                {
                    long span = player.ExperienceForNextLevel - player.ExperienceForCurrentLevel;
                    long cap = (player.GetExperienceNeededForLevel(GamePlayer.MAX_LEVEL) - player.Experience) / 2;
                    pool = PoolAfterRest(stored, player.Level, DateTime.Now - last, span, cap,
                        IsRestfulRegion(player.CurrentRegionID), ServerProperties.Properties.REST_EXPERIENCE_AWAY_FACTOR);
                }
                player.TempProperties.SetProperty(PoolProperty, pool);
                if (pool > stored)
                    player.Out.SendMessage($"You have rested and gathered {pool - stored:N0} rest experience. " +
                                           RestedText(pool, player.ExperienceForNextLevel - player.Experience),
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }
            catch (Exception exception)
            {
                if (Log.IsWarnEnabled)
                    Log.Warn($"REST_EXPERIENCE could not read the pool for {player.Name}", exception);
            }
        }

        /// <summary>Called from GamePlayer.SaveIntoDatabase: writes the pool when it changed this session.</summary>
        public static void Save(GamePlayer player)
        {
            if (player == null || !player.TempProperties.GetProperty<bool>(LoadedProperty))
                return;
            string id = player.DBCharacter?.ObjectId;
            if (string.IsNullOrEmpty(id))
                return;
            long pool = Pool(player);
            if (pool == player.TempProperties.GetProperty<long>(SavedProperty))
                return;
            try
            {
                string text = pool.ToString(CultureInfo.InvariantCulture);
                DbCoreCharacterXCustomParam row = Find(id);
                if (row == null)
                    GameServer.Database.AddObject(new DbCoreCharacterXCustomParam(id, Param, text));
                else
                {
                    row.Value = text;
                    GameServer.Database.SaveObject(row);
                }
                player.TempProperties.SetProperty(SavedProperty, pool);
            }
            catch (Exception exception)
            {
                if (Log.IsWarnEnabled)
                    Log.Warn($"REST_EXPERIENCE could not save the pool for {player.Name}", exception);
            }
        }

        private static DbCoreCharacterXCustomParam Find(string characterId) =>
            DOLDB<DbCoreCharacterXCustomParam>.SelectObject(
                DB.Column("DOLCharactersObjectId").IsEqualTo(characterId).And(DB.Column("KeyName").IsEqualTo(Param)));
    }
}
