using System;
using System.Globalization;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Keeps a real player's random item bad-luck bonus across logins (owner 2026-11-15, loot-rules
    /// RandomItems.SaveBadLuck). The bonus lives in TempProperties while playing; it is read from the character's
    /// DOLCharactersXCustomParam row the first time it is needed in a session and written back when the character is
    /// saved, only if it changed. Gamebots keep the session-only bonus.
    /// </summary>
    public static class LootBadLuckStore
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(LootBadLuckStore));

        public const string Param = "DividedRealms.LootBadLuck";
        private const string LoadedProperty = "LOOT_RANDOM_ITEM_BAD_LUCK_LOADED";
        private const string SavedProperty = "LOOT_RANDOM_ITEM_BAD_LUCK_SAVED";

        public static void EnsureLoaded(GamePlayer player)
        {
            if (player == null || !LootRulesFile.Current.RandomItems.SaveBadLuck)
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
                if (row != null && double.TryParse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double stored)
                    && double.IsFinite(stored) && stored > 0)
                {
                    double max = LootRulesFile.Current.RandomItems.BadLuckMax;
                    double value = Math.Min(stored, max);
                    if (player.TempProperties.GetProperty<double>(ROGMobGenerator.BadLuckProperty) <= 0)
                        player.TempProperties.SetProperty(ROGMobGenerator.BadLuckProperty, value);
                    player.TempProperties.SetProperty(SavedProperty, stored);
                }
            }
            catch (Exception exception)
            {
                if (Log.IsWarnEnabled)
                    Log.Warn($"LOOT_BAD_LUCK could not read the saved bonus for {player.Name}", exception);
            }
        }

        /// <summary>Called from GamePlayer.SaveIntoDatabase: writes the bonus if it was used this session and changed.</summary>
        public static void Save(GamePlayer player)
        {
            if (player == null || !LootRulesFile.Current.RandomItems.SaveBadLuck)
                return;
            if (!player.TempProperties.GetProperty<bool>(LoadedProperty))
                return;
            string id = player.DBCharacter?.ObjectId;
            if (string.IsNullOrEmpty(id))
                return;

            double value = player.TempProperties.GetProperty<double>(ROGMobGenerator.BadLuckProperty);
            if (value == player.TempProperties.GetProperty<double>(SavedProperty))
                return;
            try
            {
                string text = value.ToString("0.###", CultureInfo.InvariantCulture);
                DbCoreCharacterXCustomParam row = Find(id);
                if (row == null)
                    GameServer.Database.AddObject(new DbCoreCharacterXCustomParam(id, Param, text));
                else
                {
                    row.Value = text;
                    GameServer.Database.SaveObject(row);
                }
                player.TempProperties.SetProperty(SavedProperty, value);
            }
            catch (Exception exception)
            {
                if (Log.IsWarnEnabled)
                    Log.Warn($"LOOT_BAD_LUCK could not save the bonus for {player.Name}", exception);
            }
        }

        private static DbCoreCharacterXCustomParam Find(string characterId) =>
            DOLDB<DbCoreCharacterXCustomParam>.SelectObject(
                DB.Column("DOLCharactersObjectId").IsEqualTo(characterId).And(DB.Column("KeyName").IsEqualTo(Param)));
    }
}
