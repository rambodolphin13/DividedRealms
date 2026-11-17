using System;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Mithril store consumables (ItemTemplate.ClassType = "DOL.GS.MithrilStoreItem"), chosen by Id_nb:
    /// <list type="bullet">
    /// <item>mithril_xp_potion / mithril_rp_potion / mithril_bp_potion: experience, realm or bounty points now.</item>
    /// <item>mithril_xp_off / mithril_rp_off: switch experience or realm point gain off or on again.</item>
    /// <item>mithril_cl15_token / mithril_ml10_token / mithril_boost_token (level 50 + CL15 + ML10).</item>
    /// <item>mithril_lgm_&lt;skill&gt; / mithril_lgm_all: Legendary Grandmaster (1100) in one trade (and its secondaries) or all.</item>
    /// <item>mithril_repair_kit: drag onto an item to restore its condition and durability.</item>
    /// <item>mithril_name_change / mithril_gender_change / mithril_race_change: used through /mithril.</item>
    /// </list>
    /// </summary>
    public class MithrilStoreItem : GameInventoryItem
    {
        private MithrilStoreItem() { }

        public MithrilStoreItem(DbItemTemplate template) : base(template) { }

        public MithrilStoreItem(DbItemUnique template) : base(template) { }

        /// <summary>Used when a saved item is loaded from the database.</summary>
        public MithrilStoreItem(DbInventoryItem item) : base(item) { }

        public override bool Use(GamePlayer player)
        {
            string id = Id_nb?.ToLowerInvariant() ?? string.Empty;
            string done = id switch
            {
                "mithril_xp_potion" => XpPotion(player),
                "mithril_rp_potion" => Points(player, rp: 25000),
                "mithril_bp_potion" => Points(player, bp: 500),
                "mithril_xp_off" => ToggleXp(player),
                "mithril_rp_off" => ToggleRp(player),
                "mithril_cl15_token" => ChampionTo15(player),
                "mithril_ml10_token" => MasterTo10(player),
                "mithril_boost_token" => Boost(player),
                "mithril_lgm_all" => Legendary(player, null),
                "mithril_merchant_gem" => Tell(player, SummonedMerchantCamp.Summon(player)),   // reusable: never used up
                "mithril_name_change" or "mithril_gender_change" or "mithril_race_change" => Tell(player,
                    "Keep this token in your backpack and type /mithril rename <name>, /mithril gender or /mithril race <race>."),
                _ when id.StartsWith("mithril_lgm_") => Legendary(player, ParseSkill(id["mithril_lgm_".Length..])),
                _ => null,
            };

            if (done == null)
                return false;

            if (done.Length > 0)
            {
                player.Out.SendMessage(done, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                player.Inventory.RemoveCountFromStack(this, 1);
            }

            return true;
        }

        public override bool Combine(GamePlayer player, DbInventoryItem targetItem)
        {
            if (!string.Equals(Id_nb, "mithril_repair_kit", StringComparison.OrdinalIgnoreCase) || targetItem == null)
                return false;

            if (targetItem.MaxCondition <= 0 && targetItem.MaxDurability <= 0)
            {
                player.Out.SendMessage("That item can't be repaired.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return true;
            }

            targetItem.Condition = targetItem.MaxCondition;
            targetItem.Durability = targetItem.MaxDurability;
            player.Inventory.RemoveCountFromStack(this, 1);
            player.Out.SendMessage($"Your {targetItem.Name} is as good as new.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            return true;
        }

        /// <summary>A message that does not use the item up.</summary>
        private static string Tell(GamePlayer player, string message)
        {
            player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return string.Empty;
        }

        private static string XpPotion(GamePlayer player)
        {
            if (player.Level >= 50)
                return Tell(player, "You are already level 50.");

            long level = player.GetExperienceNeededForLevel(player.Level) - player.GetExperienceNeededForLevel(player.Level - 1);
            player.GainExperience(new GainedExperienceEventArgs(Math.Max(1, level / 4), 0, 0, 0, 0, 0, true, false, eXPSource.Other));
            return "The potion fills you with a quarter of a level of experience.";
        }

        private static string Points(GamePlayer player, long rp = 0, long bp = 0)
        {
            if (rp > 0)
                player.GainRealmPoints(rp, false);

            if (bp > 0)
                player.GainBountyPoints(bp, false);

            return rp > 0 ? $"You gain {rp} realm points." : $"You gain {bp} bounty points.";
        }

        private static string ToggleXp(GamePlayer player)
        {
            player.GainXP = !player.GainXP;
            return Tell(player, player.GainXP ? "You gain experience again." : "You no longer gain experience. Use this again to switch it back on.");
        }

        private static string ToggleRp(GamePlayer player)
        {
            player.GainRP = !player.GainRP;
            return Tell(player, player.GainRP ? "You gain realm points again." : "You no longer gain realm points. Use this again to switch it back on.");
        }

        private static string ChampionTo15(GamePlayer player)
        {
            if (player.Level < 50)
                return Tell(player, "You must be level 50 to use this.");

            if (player.ChampionLevel >= 15)
                return Tell(player, "You are already Champion Level 15.");

            player.Champion = true;

            while (player.ChampionLevel < Math.Min(15, player.ChampionMaxLevel))
            {
                player.GainChampionExperience(Math.Max(1, player.ChampionExperienceForNextLevel - player.ChampionExperience), eXPSource.Other);

                while (player.ChampionLevel < player.ChampionMaxLevel && player.ChampionExperience >= player.ChampionExperienceForNextLevel)
                    player.ChampionLevelUp();

                if (player.ChampionMaxLevel <= player.ChampionLevel)
                    break;
            }

            player.SaveIntoDatabase();
            return $"You are now Champion Level {player.ChampionLevel}.";
        }

        private static string MasterTo10(GamePlayer player)
        {
            if (player.Level < 50)
                return Tell(player, "You must be level 50 to use this.");

            if (player.MLLevel >= 10)
                return Tell(player, "You are already Master Level 10.");

            player.MLLevel = Math.Min(10, GamePlayer.ML_MAX_LEVEL);
            player.MLExperience = 0;
            player.MLGranted = true;
            MasterLevelRules.Refresh(player);
            player.Out.SendMasterLevelWindow((byte) player.MLLevel);
            player.SaveIntoDatabase();
            return "You are now Master Level 10. A master level trainer can set your path if you have none.";
        }

        private static string Boost(GamePlayer player)
        {
            if (player.Level < 50)
                player.SetLevelDirect(50);

            string cl = ChampionTo15(player);
            string ml = MasterTo10(player);
            player.SaveIntoDatabase();
            return $"You are level 50, Champion Level {player.ChampionLevel} and Master Level {player.MLLevel}.";
        }

        private static eCraftingSkill? ParseSkill(string name)
        {
            return Enum.TryParse(name.Replace("_", string.Empty), true, out eCraftingSkill skill) && skill is not eCraftingSkill.NoCrafting
                ? skill : null;
        }

        /// <summary>
        /// Trades the all-trades token raises. Bountycraft (1.125) stays at 1 on every character and is reset to 1 at login,
        /// so the token leaves it alone (DR-460).
        /// </summary>
        public static bool IsLegendaryTrade(eCraftingSkill skill)
        {
            return skill is not eCraftingSkill.NoCrafting and not eCraftingSkill._Last and not eCraftingSkill.BasicCrafting
                and not eCraftingSkill.BountyCrafting;
        }

        /// <summary>
        /// Skill the tokens set. Owner decision 2026-11-17: 1100, the Legendary Grandmaster title rung
        /// (AbstractProfession), so recipes up to 1115 con orange instead of purple (0%) at 1000 (DR-464).
        /// </summary>
        public const int LegendarySkill = 1100;

        /// <summary>
        /// Secondary trades whose skill the main trade's recipes require (CheckSecondCraftingSkillRequirement:
        /// recipe level - 60 / - 30 / - 20), so a single-trade token raises them too.
        /// </summary>
        public static eCraftingSkill[] SecondariesOf(eCraftingSkill trade)
        {
            return trade switch
            {
                eCraftingSkill.ArmorCrafting => [eCraftingSkill.MetalWorking, eCraftingSkill.LeatherCrafting, eCraftingSkill.ClothWorking],
                eCraftingSkill.WeaponCrafting => [eCraftingSkill.MetalWorking, eCraftingSkill.WoodWorking, eCraftingSkill.LeatherCrafting, eCraftingSkill.ClothWorking],
                eCraftingSkill.Tailoring => [eCraftingSkill.ClothWorking, eCraftingSkill.LeatherCrafting, eCraftingSkill.MetalWorking],
                eCraftingSkill.Fletching => [eCraftingSkill.WoodWorking, eCraftingSkill.MetalWorking, eCraftingSkill.ClothWorking, eCraftingSkill.LeatherCrafting],
                eCraftingSkill.SiegeCrafting => [eCraftingSkill.WoodWorking, eCraftingSkill.MetalWorking],
                eCraftingSkill.SpellCrafting => [eCraftingSkill.GemCutting],
                eCraftingSkill.Alchemy => [eCraftingSkill.HerbalCrafting],
                _ => []
            };
        }

        /// <summary>The trades a token raises: one trade plus its secondaries, or every legendary trade.</summary>
        public static eCraftingSkill[] TradesRaisedBy(eCraftingSkill? only)
        {
            return only.HasValue
                ? [only.Value, .. SecondariesOf(only.Value)]
                : Enum.GetValues<eCraftingSkill>().Where(IsLegendaryTrade).Distinct().ToArray();
        }

        private static string Legendary(GamePlayer player, eCraftingSkill? only)
        {
            eCraftingSkill[] trades = TradesRaisedBy(only);
            int raised = 0;

            foreach (eCraftingSkill trade in trades)
            {
                if (CraftingMgr.getSkillbyEnum(trade) == null)
                    continue;

                player.AddCraftingSkill(trade, 1);
                int now = player.GetCraftingSkillValue(trade);

                if (now < LegendarySkill && player.GainCraftingSkill(trade, LegendarySkill - now, announce: false))
                    raised++;
            }

            if (raised == 0)
                return Tell(player, "You are already a Legendary Grandmaster there.");

            player.CraftingPrimarySkill = PrimaryAfterToken(player.CraftingPrimarySkill, only);
            player.Out.SendUpdateCraftingSkills();
            player.SaveIntoDatabase();
            string done = raised == 1 ? "You are now a Legendary Grandmaster." : $"You are now a Legendary Grandmaster in {raised} trades.";

            if (player.CraftingPrimarySkill is eCraftingSkill.BasicCrafting or eCraftingSkill.NoCrafting)
                done += " Speak to a crafting master to choose the trade your title shows.";

            return done;
        }

        /// <summary>
        /// A character who never chose a trade keeps Basic Crafting (title "Basic Crafter's Helper"). A single-trade token
        /// makes that trade the primary; a chosen primary is never changed.
        /// </summary>
        public static eCraftingSkill PrimaryAfterToken(eCraftingSkill current, eCraftingSkill? only)
        {
            if (current is not (eCraftingSkill.BasicCrafting or eCraftingSkill.NoCrafting))
                return current;

            return only is eCraftingSkill.WeaponCrafting or eCraftingSkill.ArmorCrafting or eCraftingSkill.Tailoring
                or eCraftingSkill.Fletching or eCraftingSkill.SpellCrafting or eCraftingSkill.Alchemy or eCraftingSkill.SiegeCrafting
                ? only.Value
                : current;
        }
    }
}
