using System;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS.Realm;

namespace DOL.GS
{
    /// <summary>
    /// Name, gender and race changes bought at the Mithril merchant (live sold all three). Each uses up one token from the
    /// backpack. The client shows the new look and name after the player logs out and back in.
    /// </summary>
    public static class MithrilCharacterChanges
    {
        public const string NameToken = "mithril_name_change";
        public const string GenderToken = "mithril_gender_change";
        public const string RaceToken = "mithril_race_change";

        private static readonly Regex ValidName = new("^[A-Za-z]{3,20}$");

        public static string Rename(GamePlayer player, string newName)
        {
            if (!HasToken(player, NameToken, out DbInventoryItem token))
                return "You need a Name Change token in your backpack.";

            if (string.IsNullOrWhiteSpace(newName) || !ValidName.IsMatch(newName))
                return "A name is 3 to 20 letters, with no spaces or numbers.";

            newName = char.ToUpperInvariant(newName[0]) + newName[1..].ToLowerInvariant();

            if (DOLDB<DbCoreCharacter>.SelectObject(DB.Column("Name").IsEqualTo(newName)) != null)
                return $"The name {newName} is already taken.";

            string oldName = player.Name;
            player.Name = newName;
            player.SaveIntoDatabase();
            player.Inventory.RemoveCountFromStack(token, 1);
            AuditMgr.AddAuditEntry(player.Client, AuditType.Character, AuditSubtype.CharacterRename, oldName, newName);
            return $"You are now {newName}. Log out and back in to finish the change.";
        }

        public static string ChangeGender(GamePlayer player)
        {
            if (!HasToken(player, GenderToken, out DbInventoryItem token))
                return "You need a Gender Change token in your backpack.";

            PlayerRace race = RaceOf(player.Race);
            eGender next = player.Gender == eGender.Male ? eGender.Female : eGender.Male;

            if (race == null || race.GetModel(next) == eLivingModel.None)
                return "Your race has no other gender.";

            player.DBCharacter.Gender = next == eGender.Male ? 0 : 1;
            SetBodyModel(player, (int) race.GetModel(next));
            player.Inventory.RemoveCountFromStack(token, 1);
            return $"You are now {next.ToString().ToLowerInvariant()}. Log out and back in to see your new look.";
        }

        public static string ChangeRace(GamePlayer player, string raceName)
        {
            if (!HasToken(player, RaceToken, out DbInventoryItem token))
                return "You need a Race Change token in your backpack.";

            var eligible = player.CharacterClass?.EligibleRaces?.Where(r => r.Realm == player.Realm).ToList() ?? [];
            PlayerRace race = eligible.FirstOrDefault(r => string.Equals(r.ID.ToString(), raceName?.Replace(" ", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase));

            if (race == null)
                return $"Your class can be: {string.Join(", ", eligible.Select(r => r.ID))}. Type /mithril race <race>.";

            if ((short) race.ID == player.Race)
                return "You are already that race.";

            eGender gender = player.Gender;

            if (race.GetModel(gender) == eLivingModel.None)
                gender = eGender.Male;

            // Move the base stats from the old race's starting stats to the new one's; levels and points spent stay.
            if (GlobalConstants.STARTING_STATS_DICT.TryGetValue((eRace) player.Race, out var oldStats)
                && GlobalConstants.STARTING_STATS_DICT.TryGetValue(race.ID, out var newStats))
            {
                foreach (eStat stat in newStats.Keys)
                {
                    int delta = newStats[stat] - (oldStats.TryGetValue(stat, out int old) ? old : newStats[stat]);

                    if (delta != 0)
                        player.ChangeBaseStat(stat, (short) delta);
                }
            }

            player.Race = (short) race.ID;
            player.DBCharacter.Gender = gender == eGender.Male ? 0 : 1;
            SetBodyModel(player, (int) race.GetModel(gender));
            player.Inventory.RemoveCountFromStack(token, 1);
            return $"You are now a {race.ID}. Log out and back in to see your new look.";
        }

        /// <summary>Keeps the size and face bits of the creation model and replaces its body model.</summary>
        public static int WithBody(int creationModel, int bodyModel)
        {
            return (creationModel & ~0x7FF) | (bodyModel & 0x7FF);
        }

        private static void SetBodyModel(GamePlayer player, int bodyModel)
        {
            DbCoreCharacter character = player.DBCharacter;
            bool unchanged = character.CurrentModel == character.CreationModel;
            character.CreationModel = WithBody(character.CreationModel, bodyModel);

            if (unchanged)
            {
                character.CurrentModel = character.CreationModel;
                player.Model = (ushort) character.CurrentModel;
            }

            player.SaveIntoDatabase();
        }

        private static PlayerRace RaceOf(short race)
        {
            return PlayerRace.AllRaces.FirstOrDefault(r => (short) r.ID == race);
        }

        private static bool HasToken(GamePlayer player, string id, out DbInventoryItem token)
        {
            token = player?.Inventory?.GetFirstItemByID(id, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            return token != null;
        }
    }
}
