using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Rare drop of a CL15 Darkness Falls weapon (the Blood Seal weapons) from Darkness Falls mobs. Not live
    /// behaviour: live only dropped them from Legion and Lilith; the owner chose a rare drop from every DF mob
    /// (2026-11-17). The weapon comes from the loot owner's realm list (`cl15_blood_weapons_alb/mid/hib`, the
    /// Diamond Seal merchants' stock) and, when possible, is one the loot owner's class can use.
    /// A world only uses it when it has a LootGenerator row for `DOL.GS.LootGeneratorDarknessFallsWeapons`.
    /// </summary>
    public class LootGeneratorDarknessFallsWeapons : LootGeneratorBase
    {
        public const ushort DarknessFallsRegion = 249;

        private static readonly Lazy<Dictionary<eRealm, DbItemTemplate[]>> _weapons = new(LoadWeapons);

        public override LootList GenerateLoot(GameNPC mob, GameObject killer)
        {
            return GenerateLoot(LootContext.For(mob, killer));
        }

        public override LootList GenerateLoot(LootContext context)
        {
            GameNPC mob = context.Mob;
            LootList loot = base.GenerateLoot(mob, context.Killer);

            try
            {
                GameLiving owner = context.Owner;

                if (owner == null || mob.CurrentRegionID != DarknessFallsRegion || mob.Realm != eRealm.None ||
                    mob.Level < ServerProperties.Properties.LOOTGENERATOR_DF_WEAPON_MIN_LEVEL || owner.GetConLevel(mob) <= -3)
                    return loot;

                if (!Util.Chance(DropChance(mob) / 100.0))
                    return loot;

                DbItemTemplate weapon = PickWeapon(owner.Realm, ClassIdOf(owner));

                if (weapon != null)
                    loot.AddFixed(weapon, 1);
            }
            catch
            {
                return loot;
            }

            return loot;
        }

        /// <summary>Drop chance in percent. Named mobs (capitalized names) and epic NPCs get the named multiplier.</summary>
        public static double DropChance(GameNPC mob)
        {
            double chance = ServerProperties.Properties.LOOTGENERATOR_DF_WEAPON_BASE_CHANCE;

            if (LootContext.IsNamedMob(mob))
                chance *= ServerProperties.Properties.LOOTGENERATOR_DF_WEAPON_NAMED_MULTIPLIER;

            return Math.Min(100, chance);
        }

        /// <summary>A random weapon from the realm's list, limited to the class's weapons when it has any.</summary>
        public static DbItemTemplate PickWeapon(eRealm realm, int classId)
        {
            if (!_weapons.Value.TryGetValue(realm, out DbItemTemplate[] stock) || stock.Length == 0)
                return null;

            return Pick(stock, classId);
        }

        public static DbItemTemplate Pick(IReadOnlyList<DbItemTemplate> stock, int classId)
        {
            if (stock == null || stock.Count == 0)
                return null;

            if (classId > 0)
            {
                DbItemTemplate[] usable = stock.Where(item => !string.IsNullOrWhiteSpace(item.AllowedClasses) &&
                                                              ClassItemRules.Allows(item.AllowedClasses, classId)).ToArray();
                if (usable.Length > 0)
                    return usable[Util.Random(usable.Length - 1)];
            }

            return stock[Util.Random(stock.Count - 1)];
        }

        private static int ClassIdOf(GameLiving owner)
        {
            return owner switch
            {
                GamePlayer player => player.CharacterClass?.ID ?? 0,
                IGamePlayer bot => bot.CharacterClass?.ID ?? 0,
                _ => 0
            };
        }

        private static Dictionary<eRealm, DbItemTemplate[]> LoadWeapons()
        {
            return new Dictionary<eRealm, DbItemTemplate[]>
            {
                [eRealm.Albion] = LoadList("cl15_blood_weapons_alb"),
                [eRealm.Midgard] = LoadList("cl15_blood_weapons_mid"),
                [eRealm.Hibernia] = LoadList("cl15_blood_weapons_hib"),
            };
        }

        private static DbItemTemplate[] LoadList(string listId)
        {
            IList<DbMerchantItem> stock = DOLDB<DbMerchantItem>.SelectObjects(DB.Column("ItemListID").IsEqualTo(listId));
            return stock.Select(item => GameServer.Database.FindObjectByKey<DbItemTemplate>(item.ItemTemplateID))
                .Where(template => template != null)
                .GroupBy(template => template.Id_nb)
                .Select(group => group.First())
                .ToArray();
        }
    }
}
