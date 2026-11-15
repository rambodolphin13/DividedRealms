using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// What a piece of gear is worth to one bot (owner 2026-11-15, "Gear stat weighting"). The old
    /// <see cref="AutonomousBotEconomy.EquipmentValue(DbInventoryItem)"/> added every bonus up whatever it was, so +15 Piety
    /// counted the same for a Warrior as +15 Strength, +1 to a skill the same as +1 to a stat, and points past the item caps
    /// as much as the first ones. Here each bonus is weighed by:
    /// <list type="bullet">
    /// <item>its item budget (the usual imbue costs: a stat point 1, 4 hit points 1, a resist % 2, a skill level 5, a 10%-cap
    /// Atlantis bonus 5 per %);</item>
    /// <item>how much this class uses it (primary/secondary/tertiary/power stat, its own build's skill lines, melee or
    /// caster or archer or healer bonuses);</item>
    /// <item>how much room is left under this bot's item cap for it once the piece it would replace is taken off (a point
    /// past the cap keeps a tenth of its worth, in case a cap increase comes later).</item>
    /// </list>
    /// The base (item level, quality, armour factor or weapon DPS) is the old formula's, so a much higher-level piece still
    /// wins over a low one with the right stat. <see cref="Value"/> is pure; <see cref="Profile.For"/> reads the bot.
    /// </summary>
    public static class BotGearValue
    {
        /// <summary>The old formula's multiplier on bonus points; weights below are in item-budget units.</summary>
        public const int BonusScale = 3;
        /// <summary>The share of a bonus point past the bot's cap that still counts.</summary>
        public const double OverCapShare = 0.1;

        public sealed class Profile
        {
            public int Level = 1;
            public eStat Primary = eStat.UNDEFINED, Secondary = eStat.UNDEFINED, Tertiary = eStat.UNDEFINED, Mana = eStat.UNDEFINED;
            public eClassType ClassType = eClassType.PureTank;
            public bool Melee, Archer, DualWield, Healer, Buffer;
            /// <summary>Skill properties of the bot's build (its lifetime plan and the lines it has trained).</summary>
            public HashSet<eProperty> Skills = new();
            /// <summary>Focus properties of the bot's caster lines.</summary>
            public HashSet<eProperty> Foci = new();
            /// <summary>Item bonus totals of everything the bot wears now.</summary>
            public Dictionary<eProperty, int> Worn = new();

            public bool UsesPower => Mana != eStat.UNDEFINED && ClassType != eClassType.PureTank;
            public bool Caster => UsesPower && !Melee;

            public int WornBonus(eProperty property) => Worn.TryGetValue(property, out int value) ? value : 0;

            public static Profile For(GameBot bot)
            {
                var profile = new Profile { Level = Math.Max(1, (int)bot.Level) };
                if (bot.CharacterClass is ICharacterClass cls)
                {
                    profile.Primary = cls.PrimaryStat;
                    profile.Secondary = cls.SecondaryStat;
                    profile.Tertiary = cls.TertiaryStat;
                    profile.Mana = cls.ManaStat;
                    profile.ClassType = cls.ClassType;
                }
                var lines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (bot.BotSpec?.SpecLines != null)
                    foreach (BotSpecLine line in bot.BotSpec.SpecLines)
                        if (!string.IsNullOrEmpty(line.Spec))
                            lines.Add(line.Spec);
                IList<Specialization> trained;
                try { trained = bot.GetSpecList(); }
                catch (NullReferenceException) { trained = Array.Empty<Specialization>(); } // a bot not fully set up (tests)
                foreach (Specialization spec in trained)
                    if (spec != null && spec.Level > 1)
                        lines.Add(spec.KeyName);
                foreach (string line in lines)
                {
                    eProperty skill = SkillBase.SpecToSkill(line);
                    if (skill != eProperty.Undefined)
                        profile.Skills.Add(skill);
                    eProperty focus = SkillBase.SpecToFocus(line);
                    if (focus != eProperty.Undefined)
                        profile.Foci.Add(focus);
                }
                profile.FillRoles();
                if (bot.Inventory != null)
                    foreach (DbInventoryItem item in bot.Inventory.EquippedItems)
                        foreach ((eProperty property, int value) in Bonuses(item))
                            profile.Worn[property] = profile.WornBonus(property) + value;
                return profile;
            }

            /// <summary>Melee, archer, dual wield, healer and buffer from the class type and the build's skills.</summary>
            public void FillRoles()
            {
                Archer = Skills.Overlaps(ArcherySkills);
                DualWield = Skills.Overlaps(DualWieldSkills);
                Melee = ClassType != eClassType.ListCaster && (Skills.Overlaps(MeleeSkills) || ClassType == eClassType.PureTank);
                Healer = Skills.Overlaps(HealingSkills);
                Buffer = Skills.Overlaps(BuffSkills);
            }
        }

        private static readonly HashSet<eProperty> MeleeSkills =
        [
            eProperty.Skill_Two_Handed, eProperty.Skill_Critical_Strike, eProperty.Skill_Crushing, eProperty.Skill_Dual_Wield,
            eProperty.Skill_Flexible_Weapon, eProperty.Skill_Polearms, eProperty.Skill_Slashing, eProperty.Skill_Staff,
            eProperty.Skill_Thrusting, eProperty.Skill_Sword, eProperty.Skill_Hammer, eProperty.Skill_Axe, eProperty.Skill_Left_Axe,
            eProperty.Skill_Spear, eProperty.Skill_Blades, eProperty.Skill_Blunt, eProperty.Skill_Piercing,
            eProperty.Skill_Large_Weapon, eProperty.Skill_Celtic_Dual, eProperty.Skill_Celtic_Spear, eProperty.Skill_Scythe,
            eProperty.Skill_HandToHand, eProperty.Skill_MaulerStaff, eProperty.Skill_FistWraps, eProperty.Skill_Thrown_Weapons,
            eProperty.Skill_Shields, eProperty.Skill_Parry,
        ];
        private static readonly HashSet<eProperty> DualWieldSkills =
            [eProperty.Skill_Dual_Wield, eProperty.Skill_Celtic_Dual, eProperty.Skill_Left_Axe, eProperty.Skill_HandToHand, eProperty.Skill_FistWraps];
        private static readonly HashSet<eProperty> ArcherySkills =
        [
            eProperty.Skill_Cross_Bows, eProperty.Skill_Long_bows, eProperty.Skill_Composite, eProperty.Skill_RecurveBow,
            eProperty.Skill_ShortBow, eProperty.Skill_Archery,
        ];
        private static readonly HashSet<eProperty> HealingSkills =
            [eProperty.Skill_Rejuvenation, eProperty.Skill_Mending, eProperty.Skill_Regrowth];
        private static readonly HashSet<eProperty> BuffSkills =
        [
            eProperty.Skill_Enhancement, eProperty.Skill_Augmentation, eProperty.Skill_Nurture, eProperty.Skill_Nature,
            eProperty.Skill_Battlesongs,
        ];
        private static readonly HashSet<eProperty> TenPercentBonuses =
        [
            eProperty.SpellDamage, eProperty.CastingSpeed, eProperty.ResistPierce, eProperty.MeleeDamage, eProperty.MeleeSpeed,
            eProperty.StyleDamage, eProperty.ArcherySpeed, eProperty.RangedDamage, eProperty.SpellRange, eProperty.ArcheryRange,
        ];
        private static readonly HashSet<eProperty> TwentyFivePercentBonuses =
        [
            eProperty.BuffEffectiveness, eProperty.DebuffEffectiveness, eProperty.HealingEffectiveness, eProperty.SpellDuration,
            eProperty.ArcaneSyphon, eProperty.Fatigue,
        ];

        /// <summary>Every (property, amount) bonus an item gives (the ten bonus slots and the extra bonus).</summary>
        public static IEnumerable<(eProperty Property, int Value)> Bonuses(DbInventoryItem item)
        {
            if (item == null)
                yield break;
            (int Type, int Value)[] pairs =
            [
                (item.Bonus1Type, item.Bonus1), (item.Bonus2Type, item.Bonus2), (item.Bonus3Type, item.Bonus3),
                (item.Bonus4Type, item.Bonus4), (item.Bonus5Type, item.Bonus5), (item.Bonus6Type, item.Bonus6),
                (item.Bonus7Type, item.Bonus7), (item.Bonus8Type, item.Bonus8), (item.Bonus9Type, item.Bonus9),
                (item.Bonus10Type, item.Bonus10), (item.ExtraBonusType, item.ExtraBonus),
            ];
            foreach ((int type, int value) in pairs)
                if (type >= 0 && value != 0)
                    yield return ((eProperty)type, value); // type 0: a bonus with no type, counted as before
        }

        /// <summary>How much this class gets from one point of a stat (0.15 to 1).</summary>
        public static double StatRelevance(Profile profile, eStat stat)
        {
            double relevance = 0.15;
            if (stat == profile.Primary) relevance = 1.0;
            else if (stat == profile.Secondary) relevance = 0.8;
            else if (stat == profile.Tertiary) relevance = 0.6;
            if (profile.UsesPower && stat == profile.Mana)
                relevance = Math.Max(relevance, profile.ClassType == eClassType.ListCaster ? 1.0 : 0.8);
            if (stat == eStat.CON)
                relevance = Math.Max(relevance, profile.Melee ? 0.7 : 0.6);
            if (profile.Melee && stat is eStat.STR or eStat.DEX or eStat.QUI)
                relevance = Math.Max(relevance, 0.5);
            if (profile.Archer && stat == eStat.DEX)
                relevance = Math.Max(relevance, 0.8);
            if (profile.Caster && stat == eStat.DEX)
                relevance = Math.Max(relevance, 0.5);
            return relevance;
        }

        private static eStat StatOf(eProperty property) => property switch
        {
            eProperty.Strength or eProperty.StrCapBonus or eProperty.MythicalStrCapBonus => eStat.STR,
            eProperty.Dexterity or eProperty.DexCapBonus or eProperty.MythicalDexCapBonus => eStat.DEX,
            eProperty.Constitution or eProperty.ConCapBonus or eProperty.MythicalConCapBonus => eStat.CON,
            eProperty.Quickness or eProperty.QuiCapBonus or eProperty.MythicalQuiCapBonus => eStat.QUI,
            eProperty.Intelligence or eProperty.IntCapBonus or eProperty.MythicalIntCapBonus => eStat.INT,
            eProperty.Piety or eProperty.PieCapBonus or eProperty.MythicalPieCapBonus => eStat.PIE,
            eProperty.Empathy or eProperty.EmpCapBonus or eProperty.MythicalEmpCapBonus => eStat.EMP,
            eProperty.Charisma or eProperty.ChaCapBonus or eProperty.MythicalChaCapBonus => eStat.CHR,
            _ => eStat.UNDEFINED,
        };

        private static bool IsAcuity(eProperty property) =>
            property is eProperty.Acuity or eProperty.AcuCapBonus or eProperty.MythicalAcuCapBonus;

        /// <summary>Item-budget worth of one point of a bonus to this bot, before caps.</summary>
        public static double Weight(Profile profile, eProperty property)
        {
            eStat stat = StatOf(property);
            double acuity = profile.UsesPower ? StatRelevance(profile, profile.Mana) : 0;
            switch (property)
            {
                case >= eProperty.Strength and <= eProperty.Charisma:
                    return StatRelevance(profile, stat);
                case eProperty.Acuity:
                    return acuity;
                case > eProperty.StatCapBonus_First and < eProperty.StatCapBonus_Last:
                case > eProperty.MythicalStatCapBonus_First and < eProperty.MythicalStatCapBonus_Last:
                    return 0.5 * (IsAcuity(property) ? acuity :
                        property is eProperty.MaxHealthCapBonus ? 0.25 :
                        property is eProperty.PowerPoolCapBonus ? (profile.UsesPower ? 2 : 0) :
                        StatRelevance(profile, stat));
                case eProperty.MaxHealth:
                    return profile.Melee ? 0.3 : 0.25;
                case > eProperty.Resist_First and < eProperty.Resist_Last:
                case eProperty.Resist_Natural:
                    return 2;
                case > eProperty.ResCapBonus_First and < eProperty.ResCapBonus_Last:
                    return 1;
                case > eProperty.Skill_First and < eProperty.Skill_Last:
                    return profile.Skills.Contains(property) ? 5 : 0.3;
                case eProperty.AllMagicSkills:
                    return profile.UsesPower ? 5 : 0;
                case eProperty.AllMeleeWeaponSkills:
                    return profile.Melee ? 5 : 0;
                case eProperty.AllDualWieldingSkills:
                    return profile.DualWield ? 5 : 0;
                case eProperty.AllArcherySkills:
                    return profile.Archer ? 5 : 0;
                case eProperty.AllSkills:
                    return 5;
                case eProperty.AllFocusLevels:
                    return profile.Caster ? 0.5 : 0;
                case >= eProperty.Focus_Darkness and <= eProperty.Focus_Arboreal:
                case >= eProperty.Focus_EtherealShriek and <= eProperty.Focus_Witchcraft:
                    return profile.Foci.Contains(property) ? 0.5 : 0;
                case eProperty.PowerPool:
                    return profile.UsesPower ? 2 : 0;
                case eProperty.MaxMana:
                    return profile.UsesPower ? 1 : 0;
                case eProperty.SpellDamage or eProperty.CastingSpeed or eProperty.ResistPierce:
                    return profile.Caster ? 5 : profile.UsesPower ? 2 : 0;
                case eProperty.SpellRange:
                    return profile.Caster ? 2 : 0;
                case eProperty.MeleeDamage or eProperty.MeleeSpeed or eProperty.StyleDamage:
                    return profile.Melee ? 5 : 0;
                case eProperty.ArcherySpeed or eProperty.RangedDamage:
                    return profile.Archer ? 5 : 0;
                case eProperty.ArcheryRange:
                    return profile.Archer ? 2 : 0;
                case eProperty.HealingEffectiveness:
                    return profile.Healer ? 2 : 0;
                case eProperty.BuffEffectiveness:
                    return profile.Buffer ? 2 : 0;
                case eProperty.DebuffEffectiveness:
                    return profile.Caster ? 1.5 : 0;
                case eProperty.SpellDuration or eProperty.ArcaneSyphon:
                    return profile.UsesPower ? 1 : 0;
                case eProperty.Fatigue:
                    return profile.Melee ? 2 : 0;
                case eProperty.ArmorFactor:
                    return 1;
                case eProperty.CriticalMeleeHitChance:
                    return profile.Melee ? 3 : 0;
                case eProperty.CriticalSpellHitChance:
                    return profile.Caster ? 3 : 0;
                case eProperty.CriticalArcheryHitChance:
                    return profile.Archer ? 3 : 0;
                case eProperty.CriticalHealHitChance:
                    return profile.Healer ? 2 : 0;
                case eProperty.EvadeChance or eProperty.BlockChance or eProperty.ParryChance:
                    return 3;
                case eProperty.Undefined:
                    return 1; // the old formula's weight for a bonus it could not name
                default:
                    return 0.5;
            }
        }

        /// <summary>This bot's item cap for a bonus (int.MaxValue for the uncapped ones).</summary>
        public static int Cap(Profile profile, eProperty property)
        {
            int level = profile.Level;
            switch (property)
            {
                case >= eProperty.Strength and <= eProperty.Charisma:
                    return (int)(level * 1.5) + Math.Min(Math.Max(0, profile.WornBonus(CapBonusOf(property))), level / 2 + 1);
                case eProperty.Acuity:
                    return (int)(level * 1.5) + Math.Min(Math.Max(0, profile.WornBonus(eProperty.AcuCapBonus)), level / 2 + 1);
                case > eProperty.StatCapBonus_First and < eProperty.StatCapBonus_Last:
                    return property switch
                    {
                        eProperty.MaxHealthCapBonus => level * 4,
                        eProperty.PowerPoolCapBonus => level / 2,
                        _ => level / 2 + 1,
                    };
                case eProperty.MaxHealth:
                    return level * 4 + Math.Min(Math.Max(0, profile.WornBonus(eProperty.MaxHealthCapBonus)), level * 4);
                case > eProperty.Resist_First and < eProperty.Resist_Last:
                    return level / 2 + 1 + Math.Min(Math.Max(0, profile.WornBonus(property - eProperty.Resist_First + eProperty.ResCapBonus_First)), 15);
                case > eProperty.ResCapBonus_First and < eProperty.ResCapBonus_Last:
                    return 15;
                case > eProperty.Skill_First and < eProperty.Skill_Last:
                    return level / 5 + 1;
                case eProperty.PowerPool:
                    return level / 2 + Math.Min(Math.Max(0, profile.WornBonus(eProperty.PowerPoolCapBonus)), level / 2);
                default:
                    if (TenPercentBonuses.Contains(property))
                        return Math.Clamp(level / 5, 1, 10);
                    if (TwentyFivePercentBonuses.Contains(property))
                        return Math.Clamp(level / 2, 1, 25);
                    return int.MaxValue;
            }
        }

        private static eProperty CapBonusOf(eProperty stat) => stat switch
        {
            eProperty.Strength => eProperty.StrCapBonus,
            eProperty.Dexterity => eProperty.DexCapBonus,
            eProperty.Constitution => eProperty.ConCapBonus,
            eProperty.Quickness => eProperty.QuiCapBonus,
            eProperty.Intelligence => eProperty.IntCapBonus,
            eProperty.Piety => eProperty.PieCapBonus,
            eProperty.Empathy => eProperty.EmpCapBonus,
            eProperty.Charisma => eProperty.ChaCapBonus,
            _ => eProperty.Undefined,
        };

        /// <summary>
        /// What <paramref name="item"/> is worth to this bot in the slot now holding <paramref name="occupant"/> (null for an
        /// empty slot or a spare). The occupant's own bonuses are taken off the worn totals first, so the item and the
        /// occupant are measured against the same room under the caps. Pure.
        /// </summary>
        public static int Value(Profile profile, DbInventoryItem item, DbInventoryItem occupant)
        {
            if (item == null || !BotWeaponStats.HasFunctionalMeleeStats(item))
                return 0;
            int baseValue = Math.Max(1, item.Level) * 10 + Math.Max(1, item.Quality) + Math.Max(0, item.DPS_AF) * 2 +
                            Math.Abs(item.Bonus) * BonusScale;
            if (profile == null)
                return baseValue;

            var occupantBonus = new Dictionary<eProperty, int>();
            foreach ((eProperty property, int value) in Bonuses(occupant))
                occupantBonus[property] = occupantBonus.GetValueOrDefault(property) + value;
            var itemBonus = new Dictionary<eProperty, int>();
            foreach ((eProperty property, int value) in Bonuses(item))
                itemBonus[property] = itemBonus.GetValueOrDefault(property) + value;

            double worth = 0;
            foreach ((eProperty property, int value) in itemBonus)
            {
                double weight = Weight(profile, property);
                if (weight <= 0)
                    continue;
                if (value < 0)
                {
                    worth += weight * value;
                    continue;
                }
                int cap = Cap(profile, property);
                double counted = value;
                if (cap != int.MaxValue)
                {
                    int room = Math.Max(0, cap - (profile.WornBonus(property) - occupantBonus.GetValueOrDefault(property)));
                    counted = Math.Min(value, room) + OverCapShare * Math.Max(0, value - room);
                }
                worth += weight * counted;
            }
            return baseValue + (int)Math.Round(worth * BonusScale);
        }
    }
}
