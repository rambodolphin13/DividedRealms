using System.Collections.Generic;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>BotGearValue (owner 2026-11-15, "Gear stat weighting"): gear is worth what this class gets from it.</summary>
    [TestFixture]
    public class UT_BotGearValue
    {
        private static BotGearValue.Profile Warrior(int level = 50)
        {
            var profile = new BotGearValue.Profile
            {
                Level = level, Primary = eStat.STR, Secondary = eStat.CON, Tertiary = eStat.DEX, Mana = eStat.UNDEFINED,
                ClassType = eClassType.PureTank,
                Skills = new HashSet<eProperty> { eProperty.Skill_Sword, eProperty.Skill_Shields, eProperty.Skill_Parry },
            };
            profile.FillRoles();
            return profile;
        }

        private static BotGearValue.Profile Wizard(int level = 50)
        {
            var profile = new BotGearValue.Profile
            {
                Level = level, Primary = eStat.INT, Secondary = eStat.DEX, Tertiary = eStat.QUI, Mana = eStat.INT,
                ClassType = eClassType.ListCaster,
                Skills = new HashSet<eProperty> { eProperty.Skill_Fire, eProperty.Skill_Earth },
                Foci = new HashSet<eProperty> { eProperty.Focus_Fire, eProperty.Focus_Earth },
            };
            profile.FillRoles();
            return profile;
        }

        private static BotGearValue.Profile Cleric(int level = 50)
        {
            var profile = new BotGearValue.Profile
            {
                Level = level, Primary = eStat.PIE, Secondary = eStat.CON, Tertiary = eStat.STR, Mana = eStat.PIE,
                ClassType = eClassType.ListCaster,
                Skills = new HashSet<eProperty> { eProperty.Skill_Rejuvenation, eProperty.Skill_Enhancement, eProperty.Skill_Smiting },
            };
            profile.FillRoles();
            return profile;
        }

        private static DbInventoryItem Ring(int level, params (eProperty Property, int Value)[] bonuses)
        {
            var item = new DbInventoryItem { Level = level, Quality = 100, Object_Type = (int)eObjectType.Magical };
            for (int i = 0; i < bonuses.Length; i++)
            {
                int type = (int)bonuses[i].Property, value = bonuses[i].Value;
                switch (i)
                {
                    case 0: item.Bonus1Type = type; item.Bonus1 = value; break;
                    case 1: item.Bonus2Type = type; item.Bonus2 = value; break;
                    case 2: item.Bonus3Type = type; item.Bonus3 = value; break;
                    case 3: item.Bonus4Type = type; item.Bonus4 = value; break;
                }
            }
            return item;
        }

        [Test]
        public void AWarriorWantsStrengthNotPiety()
        {
            DbInventoryItem strength = Ring(45, (eProperty.Strength, 15));
            DbInventoryItem piety = Ring(45, (eProperty.Piety, 15));
            Assert.That(BotGearValue.Value(Warrior(), strength, null), Is.GreaterThan(BotGearValue.Value(Warrior(), piety, null)));
            Assert.That(BotGearValue.Value(Cleric(), piety, null), Is.GreaterThan(BotGearValue.Value(Cleric(), strength, null)));
        }

        [Test]
        public void AcuityCountsForCastersOnly()
        {
            DbInventoryItem acuity = Ring(45, (eProperty.Acuity, 15));
            DbInventoryItem plain = Ring(45);
            Assert.That(BotGearValue.Value(Wizard(), acuity, null), Is.GreaterThan(BotGearValue.Value(Wizard(), plain, null) + 30));
            Assert.That(BotGearValue.Value(Warrior(), acuity, null), Is.EqualTo(BotGearValue.Value(Warrior(), plain, null)));
        }

        [Test]
        public void ASkillOfTheBotsOwnBuildIsWorthFarMoreThanAnotherClasssSkill()
        {
            DbInventoryItem sword = Ring(45, (eProperty.Skill_Sword, 3));
            DbInventoryItem fire = Ring(45, (eProperty.Skill_Fire, 3));
            Assert.That(BotGearValue.Value(Warrior(), sword, null), Is.GreaterThan(BotGearValue.Value(Warrior(), fire, null) + 30));
            Assert.That(BotGearValue.Value(Wizard(), fire, null), Is.GreaterThan(BotGearValue.Value(Wizard(), sword, null) + 30));
            // One skill level is worth more than one stat point.
            Assert.That(BotGearValue.Weight(Warrior(), eProperty.Skill_Sword),
                Is.GreaterThan(BotGearValue.Weight(Warrior(), eProperty.Strength)));
        }

        [Test]
        public void MeleeAndSpellAtlantisBonusesGoToTheirOwnRoles()
        {
            Assert.That(BotGearValue.Weight(Warrior(), eProperty.MeleeDamage), Is.GreaterThan(0));
            Assert.That(BotGearValue.Weight(Warrior(), eProperty.SpellDamage), Is.Zero);
            Assert.That(BotGearValue.Weight(Wizard(), eProperty.SpellDamage), Is.GreaterThan(0));
            Assert.That(BotGearValue.Weight(Wizard(), eProperty.MeleeDamage), Is.Zero);
            Assert.That(BotGearValue.Weight(Cleric(), eProperty.HealingEffectiveness), Is.GreaterThan(0));
            Assert.That(BotGearValue.Weight(Wizard(), eProperty.HealingEffectiveness), Is.Zero);
        }

        [Test]
        public void StrengthPastTheCapBarelyCountsButResistsWithRoomDo()
        {
            BotGearValue.Profile capped = Warrior();
            capped.Worn[eProperty.Strength] = 75; // the level 50 item cap
            DbInventoryItem strength = Ring(45, (eProperty.Strength, 15));
            DbInventoryItem heat = Ring(45, (eProperty.Resist_Heat, 5));
            Assert.That(BotGearValue.Value(capped, heat, null), Is.GreaterThan(BotGearValue.Value(capped, strength, null)));
            // Uncapped, the strength ring wins.
            Assert.That(BotGearValue.Value(Warrior(), strength, null), Is.GreaterThan(BotGearValue.Value(Warrior(), heat, null)));
        }

        [Test]
        public void ThePieceBeingReplacedGivesItsRoomBack()
        {
            BotGearValue.Profile capped = Warrior();
            capped.Worn[eProperty.Strength] = 75;
            DbInventoryItem worn = Ring(45, (eProperty.Strength, 15));
            DbInventoryItem same = Ring(45, (eProperty.Strength, 15));
            Assert.That(BotGearValue.Value(capped, same, worn), Is.EqualTo(BotGearValue.Value(capped, worn, worn)));
            Assert.That(BotGearValue.Value(capped, same, worn), Is.GreaterThan(BotGearValue.Value(capped, same, null)));
        }

        [Test]
        public void AMuchHigherLevelPieceStillBeatsALowOneWithTheRightStat()
        {
            DbInventoryItem low = Ring(20, (eProperty.Strength, 6));
            DbInventoryItem high = Ring(50, (eProperty.Piety, 6));
            Assert.That(BotGearValue.Value(Warrior(), high, null), Is.GreaterThan(BotGearValue.Value(Warrior(), low, null)));
        }

        [Test]
        public void NoProfileGivesTheBaseOnlyAndANegativeBonusCostsWorth()
        {
            DbInventoryItem ring = Ring(30, (eProperty.Strength, 10));
            Assert.That(BotGearValue.Value(null, ring, null), Is.EqualTo(30 * 10 + 100));
            DbInventoryItem cursed = Ring(30, (eProperty.Strength, 10), (eProperty.Constitution, -10));
            Assert.That(BotGearValue.Value(Warrior(), cursed, null), Is.LessThan(BotGearValue.Value(Warrior(), ring, null)));
        }

        [Test]
        public void SkillCapsFollowLevel()
        {
            Assert.That(BotGearValue.Cap(Warrior(50), eProperty.Skill_Sword), Is.EqualTo(11));
            Assert.That(BotGearValue.Cap(Warrior(50), eProperty.Strength), Is.EqualTo(75));
            Assert.That(BotGearValue.Cap(Warrior(50), eProperty.Resist_Heat), Is.EqualTo(26));
            Assert.That(BotGearValue.Cap(Warrior(50), eProperty.MaxHealth), Is.EqualTo(200));
            Assert.That(BotGearValue.Cap(Wizard(50), eProperty.SpellDamage), Is.EqualTo(10));
            Assert.That(BotGearValue.Cap(Cleric(20), eProperty.HealingEffectiveness), Is.EqualTo(10));
        }
    }
}
