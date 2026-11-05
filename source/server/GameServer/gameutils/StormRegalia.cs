using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// The Storm Regalia: the Storm Sovereign's own armor and cloak (the Stormforged models 4929-4933, winged helm 4942 and
    /// wolf cloak 4937), with a set bonus at 2, 4, 6 and 7 pieces worn. Item rows come from
    /// source/tools/sovereign/storm_regalia_edits.py; the Crownkeeper hands out missing pieces at level 50.
    /// </summary>
    public static class StormRegalia
    {
        public const string Prefix = "storm_regalia_";
        public static readonly string[] Pieces =
            { Prefix + "helm", Prefix + "hauberk", Prefix + "sleeves", Prefix + "gauntlets", Prefix + "leggings", Prefix + "boots", Prefix + "cloak" };
        public const int GrantLevel = 50;
        public const string GrantKeyword = "Storm Regalia";
        private const string AppliedKey = "StormRegalia.Applied";

        /// <summary>Bonus held at each tier, cumulative: 2, 4, 6 and the full set of 7.</summary>
        public static IReadOnlyList<(int Pieces, string Text, MLMod[] Mods)> Tiers { get; } = new (int, string, MLMod[])[]
        {
            (2, "+5% to all resistances", MLMod.Resists(MLCategory.Ability, 5, physical: true)),
            (4, "+10% melee and style damage", new[] { new MLMod(eProperty.MeleeDamage, MLCategory.Ability, 10), new MLMod(eProperty.StyleDamage, MLCategory.Ability, 10) }),
            (6, "+10% armor absorption and spell damage", new[] { new MLMod(eProperty.ArmorAbsorption, MLCategory.Ability, 10), new MLMod(eProperty.SpellDamage, MLCategory.Ability, 10) }),
            (7, "+10% maximum health, and every block or parry builds 2 Tempest", new[] { new MLMod(eProperty.Of_Toughness, MLCategory.Ability, 10) }),
        };

        public static bool IsPiece(DbInventoryItem item) => item?.Id_nb?.StartsWith(Prefix, StringComparison.Ordinal) == true;

        /// <summary>Distinct pieces worn, counting <paramref name="arriving"/> and not <paramref name="leaving"/> (the hooks run mid-move).</summary>
        public static int Worn(GamePlayer player, DbInventoryItem arriving = null, DbInventoryItem leaving = null)
        {
            if (player?.Inventory == null)
                return 0;
            var worn = player.Inventory.EquippedItems.Where(item => IsPiece(item) && !ReferenceEquals(item, leaving)).Select(item => item.Id_nb).ToHashSet();
            if (IsPiece(arriving))
                worn.Add(arriving.Id_nb);
            return worn.Count;
        }

        public static bool HasFullSet(GamePlayer player) => Applied(player) >= Pieces.Length;

        /// <summary>Tempest for a block or parry: 1, or 2 with the full set.</summary>
        public static int DefenseTempest(GamePlayer player) => HasFullSet(player) ? 2 : 1;

        /// <summary>Re-reads the worn pieces and swaps the held tier bonuses. Called on every equip change, login included.</summary>
        public static void Refresh(GamePlayer player, DbInventoryItem arriving = null, DbInventoryItem leaving = null)
        {
            int before = Applied(player);
            int worn = Worn(player, arriving, leaving);
            if (worn == before)
                return;
            foreach (var tier in Tiers)
            {
                bool had = before >= tier.Pieces, has = worn >= tier.Pieces;
                if (had == has)
                    continue;
                foreach (MLMod mod in tier.Mods)
                    mod.Apply(player, null, has);
                if (player.Client?.ClientState == GameClient.eClientState.Playing)
                    player.Out.SendMessage($"Storm Regalia ({tier.Pieces} pieces): {tier.Text} {(has ? "is yours" : "fades")}.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
            }
            player.TempProperties.SetProperty(AppliedKey, worn);
            MLMod.Refresh(player);
        }

        private static int Applied(GamePlayer player) => player?.TempProperties.GetProperty<int>(AppliedKey) ?? 0;

        /// <summary>Gives a level 50 Storm Sovereign every Regalia piece they do not carry (bags or worn).</summary>
        public static void Grant(GamePlayer player, GameNPC giver)
        {
            if (!StormforgedRules.IsStormforged(player.CharacterClass.ID))
                return;
            if (player.Level < GrantLevel)
            {
                player.Out.SendMessage($"{giver.GetName(0, true)} says, \"The Regalia waits for a sovereign of level {GrantLevel}.\"", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
                return;
            }
            var owned = new HashSet<string>(player.Inventory.AllItems.Where(IsPiece).Select(item => item.Id_nb));
            int given = 0;
            foreach (string id in Pieces.Where(id => !owned.Contains(id)))
                if (player.ReceiveItem(giver, id))
                    given++;
            player.Out.SendMessage(given > 0
                ? $"{giver.GetName(0, true)} says, \"Wear the storm, sovereign. All seven pieces together hold its full strength.\""
                : $"{giver.GetName(0, true)} says, \"You already carry the whole Regalia.\"", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
        }
    }
}
