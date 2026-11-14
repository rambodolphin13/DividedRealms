using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Live crafting tools worn on the head (Renvek's hats, live ids 15893-15899): while worn they raise the chance of a
    /// skill gain, the crafting speed and (some) the minimum quality, but only for their own trade.
    /// </summary>
    public static class CraftingToolBonus
    {
        public readonly record struct Bonus(eCraftingSkill Skill, int SkillGainPercent, int SpeedPercent, int MinQuality);

        private static readonly Dictionary<string, Bonus> _byItem = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["lv15893"] = new(eCraftingSkill.ArmorCrafting, 100, 25, 2),
            ["lv15894"] = new(eCraftingSkill.WeaponCrafting, 100, 25, 2),
            ["lv15895"] = new(eCraftingSkill.Tailoring, 100, 25, 2),
            ["lv15896"] = new(eCraftingSkill.Fletching, 100, 25, 2),
            ["lv15897"] = new(eCraftingSkill.SpellCrafting, 100, 25, 0),
            ["lv15898"] = new(eCraftingSkill.Alchemy, 100, 25, 0),
            ["lv15899"] = new(eCraftingSkill.SiegeCrafting, 100, 25, 0),
        };

        public static bool TryGet(string itemId, out Bonus bonus)
        {
            bonus = default;
            return itemId != null && _byItem.TryGetValue(itemId, out bonus);
        }

        /// <summary>The bonus of the tool on the player's head, when it is for this trade.</summary>
        public static Bonus For(GamePlayer player, eCraftingSkill skill)
        {
            DbInventoryItem helm = player?.Inventory?.GetItem(eInventorySlot.HeadArmor);

            return helm != null && TryGet(helm.Id_nb, out Bonus bonus) && bonus.Skill == skill ? bonus : default;
        }

        /// <summary>Skill gain chance with a tool: +100% frequency doubles it, up to 100.</summary>
        public static int ApplyGain(int chance, Bonus bonus)
        {
            if (chance <= 0 || bonus.SkillGainPercent <= 0)
                return chance;

            return System.Math.Min(100, chance * (100 + bonus.SkillGainPercent) / 100);
        }

        public static int ApplySpeed(int seconds, Bonus bonus)
        {
            return bonus.SpeedPercent <= 0 ? seconds : System.Math.Max(1, seconds * 100 / (100 + bonus.SpeedPercent));
        }

        /// <summary>Raises a non-masterpiece quality to at least 96 + the tool's minimum quality bonus.</summary>
        public static int ApplyQuality(int quality, Bonus bonus)
        {
            if (bonus.MinQuality <= 0 || quality >= 100)
                return quality;

            return System.Math.Max(quality, System.Math.Min(99, 96 + bonus.MinQuality));
        }
    }
}
