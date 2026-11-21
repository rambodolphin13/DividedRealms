using System.Collections.Generic;
using System.Numerics;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>DR-394, DR-395, DR-396: account companions in Master Level trial fights.</summary>
    [TestFixture]
    public class UT_TrialCompanions
    {
        private static Spell Spell(int id, eSpellType type, double damage, eDamageType damageType, int level, int power = 30,
            double castTime = 2.6, int duration = 0, int frequency = 0) => new(new DbSpell
        {
            SpellID = id, Name = "test " + id, Type = type.ToString(), Target = eSpellTarget.ENEMY.ToString(),
            Damage = damage, DamageType = (int)damageType, Power = power, CastTime = castTime, Duration = duration,
            Frequency = frequency, Range = 1500,
        }, level);

        // DR-394: spell choice

        [Test]
        public void FireWizardCastsItsSpecNukeNotTheHigherLevelBaselineOne()
        {
            Spell iceBlast = Spell(161, eSpellType.DirectDamage, 179, eDamageType.Cold, 50, power: 33);
            Spell conflagration = Spell(360, eSpellType.DirectDamageWithDebuff, 219, eDamageType.Heat, 47, power: 30, castTime: 2.8);
            Spell best = BotCasterPriority.HardestHitting(new[] { iceBlast, conflagration }, _ => 0, spell => spell.Power);
            Assert.That(best, Is.SameAs(conflagration));
            Assert.That(TemporaryCompanionBalance.HighestSpell(new[] { iceBlast, conflagration }), Is.SameAs(iceBlast),
                "the old choice, by level, is what picked the weaker spell");
        }

        [Test]
        public void ResistsDecideNotTheListedDamage()
        {
            Spell heat = Spell(1, eSpellType.DirectDamage, 219, eDamageType.Heat, 47);
            Spell cold = Spell(2, eSpellType.DirectDamage, 179, eDamageType.Cold, 50);
            Spell best = BotCasterPriority.HardestHitting(new[] { heat, cold },
                type => type == eDamageType.Heat ? 40 : 0, spell => spell.Power);
            Assert.That(best, Is.SameAs(cold), "219 heat at 40% resist is 131, under 179 cold");
        }

        [Test]
        public void EqualDamageGoesToTheCheaperThenTheQuickerSpell()
        {
            Spell dear = Spell(1, eSpellType.DirectDamage, 200, eDamageType.Heat, 50, power: 40);
            Spell cheap = Spell(2, eSpellType.DirectDamage, 200, eDamageType.Cold, 45, power: 30, castTime: 3.0);
            Spell quick = Spell(3, eSpellType.DirectDamage, 200, eDamageType.Matter, 44, power: 30, castTime: 2.5);
            Assert.That(BotCasterPriority.HardestHitting(new[] { dear, cheap }, _ => 0, spell => spell.Power), Is.SameAs(cheap));
            Assert.That(BotCasterPriority.HardestHitting(new[] { dear, cheap, quick }, _ => 0, spell => spell.Power), Is.SameAs(quick));
        }

        [Test]
        public void DamageOverTimeCountsEveryTick()
        {
            Spell nuke = Spell(1, eSpellType.DirectDamage, 150, eDamageType.Matter, 50);
            Spell dot = Spell(2, eSpellType.DamageOverTime, 60, eDamageType.Matter, 45, duration: 20_000, frequency: 4_000);
            Assert.That(BotCasterPriority.TotalDamage(60, 20_000, 4_000), Is.EqualTo(300));
            Assert.That(BotCasterPriority.HardestHitting(new[] { nuke, dot }, _ => 0, spell => spell.Power), Is.SameAs(dot));
        }

        [Test]
        public void NoDamageSpellGivesNoChoice()
        {
            Spell nearsight = Spell(1, eSpellType.Nearsight, 0, eDamageType.Heat, 19);
            Assert.That(BotCasterPriority.HardestHitting(new[] { nearsight }, _ => 0, spell => spell.Power), Is.Null);
            Assert.That(BotCasterPriority.HardestHitting(new Spell[0], _ => 0, spell => spell.Power), Is.Null);
        }

        [TestCase(219, 0, 219)]
        [TestCase(200, 25, 150)]
        [TestCase(200, -10, 220)]
        public void AfterResist(double damage, int resist, double expected) =>
            Assert.That(BotCasterPriority.AfterResist(damage, resist), Is.EqualTo(expected).Within(0.001));

        // rangedCaster, playerLed, targetIsMonster, outOfPowerForDamage
        [TestCase(true, true, true, true, true)]
        [TestCase(true, true, true, false, false, Description = "it can pay for a nuke: nothing to save for")]
        [TestCase(false, true, true, true, false, Description = "melee classes are not held")]
        [TestCase(true, false, true, true, false, Description = "gamebot parties are unchanged")]
        [TestCase(true, true, false, true, false, Description = "debuffs on enemy players stay")]
        public void OutOfPowerCasterSavesForItsNuke(bool ranged, bool playerLed, bool monster, bool outOfPower, bool expected) =>
            Assert.That(BotCasterPriority.SavesPowerForNuke(ranged, playerLed, monster, outOfPower), Is.EqualTo(expected));

        // DR-395: stand-off

        [Test]
        public void TheBossIsTheUniquelyNamedMonsterWithTheMostHealth()
        {
            var fight = new List<TrialStandOff.Mob>
            {
                new("bane viper", 2000), new("Runihura", 60000), new("bane viper", 2000), new("Ankh keeper", 9000),
            };
            Assert.That(TrialStandOff.AnchorIndex(fight), Is.EqualTo(1));
        }

        [Test]
        public void APackOfSameNamedMonstersHasNoBoss()
        {
            var cobras = new List<TrialStandOff.Mob> { new("spitting cobra", 4500), new("spitting cobra", 4500), new("Spitting Cobra", 4500) };
            Assert.That(TrialStandOff.AnchorIndex(cobras), Is.EqualTo(-1));
            Assert.That(TrialStandOff.AnchorIndex(new List<TrialStandOff.Mob>()), Is.EqualTo(-1));
        }

        [TestCase(2)]
        [TestCase(5)]
        [TestCase(7)]
        public void SpotsAreOutsideThePulsesAndApartFromEachOther(int casters)
        {
            Vector2 boss = new(335608, 465955);
            var spots = new List<Vector2>();
            for (int slot = 0; slot < casters; slot++)
                spots.Add(TrialStandOff.Spot(boss, TrialStandOff.BearingDegrees(200, slot, casters), TrialStandOff.Radius));
            foreach (Vector2 spot in spots)
                Assert.That(Vector2.Distance(spot, boss), Is.EqualTo(TrialStandOff.Radius).Within(1), "clear of the 450 and 600 pulses");
            for (int a = 0; a < spots.Count; a++)
                for (int b = a + 1; b < spots.Count; b++)
                    Assert.That(Vector2.Distance(spots[a], spots[b]), Is.GreaterThan(400), "one 400-radius fireball hits one of them");
        }

        [Test]
        public void SpotsFanEitherSideOfTheWayThePartyCame()
        {
            Assert.That(TrialStandOff.BearingDegrees(90, 0, 1), Is.EqualTo(90));
            Assert.That(TrialStandOff.BearingDegrees(90, 0, 3), Is.EqualTo(30));
            Assert.That(TrialStandOff.BearingDegrees(90, 1, 3), Is.EqualTo(90));
            Assert.That(TrialStandOff.BearingDegrees(90, 2, 3), Is.EqualTo(150));
            Assert.That(TrialStandOff.BearingDegrees(10, 0, 3), Is.EqualTo(310), "wraps round north");
            Assert.That(TrialStandOff.Bearing(new(0, 0), new(0, 100)), Is.EqualTo(90).Within(0.001));
            Assert.That(TrialStandOff.Bearing(new(0, 0), new(-100, 0)), Is.EqualTo(180).Within(0.001));
            Assert.That(TrialStandOff.Bearing(new(5, 5), new(5, 5)), Is.EqualTo(0));
        }

        // playerCompanion, playerLed, alive, fightsAtRange, support, heldByStayOrder
        [TestCase(true, true, true, true, false, false, true, Description = "Wizard, Sorcerer")]
        [TestCase(true, true, true, false, true, false, true, Description = "Cleric")]
        [TestCase(true, true, true, false, false, false, false, Description = "Paladin, Friar: melee stays on the boss")]
        [TestCase(false, true, true, true, false, false, false, Description = "a gamebot in the player's group")]
        [TestCase(true, false, true, true, false, false, false)]
        [TestCase(true, true, false, true, false, false, false)]
        [TestCase(true, true, true, true, false, true, false, Description = "/bots stay wins")]
        public void WhoTakesASpot(bool companion, bool playerLed, bool alive, bool ranged, bool support, bool stay, bool expected) =>
            Assert.That(TrialStandOff.Applies(companion, playerLed, alive, ranged, support, stay), Is.EqualTo(expected));

        [Test]
        public void ACompanionWalksToItsSpotOnlyWhenThatHelps()
        {
            Assert.That(TrialStandOff.WalksToSpot(false, 600, 750, 1400), Is.True);
            Assert.That(TrialStandOff.WalksToSpot(true, 600, 750, 1400), Is.False, "never out of a cast");
            Assert.That(TrialStandOff.WalksToSpot(false, TrialStandOff.ArrivalTolerance, 750, 1400), Is.False, "it is there");
            Assert.That(TrialStandOff.WalksToSpot(false, 600, 1500, 1400), Is.False, "its target would be out of range from the spot");
            Assert.That(TrialStandOff.WalksToSpot(false, 600, 0, 0), Is.True, "a healer has no attack target");
        }

        [Test]
        public void TheRingFollowsTheBossOnlyWhenItHasMovedFar()
        {
            Assert.That(TrialStandOff.NeedsNewLayout(true, 120), Is.False);
            Assert.That(TrialStandOff.NeedsNewLayout(true, TrialStandOff.ReanchorDistance + 1), Is.True);
            Assert.That(TrialStandOff.NeedsNewLayout(false, 0), Is.True);
            Assert.That(TrialStandOff.LeaderFights(false, false), Is.False, "out of combat the companions follow again");
            Assert.That(TrialStandOff.LeaderFights(true, false), Is.True);
        }

        // DR-396: resurrection

        [Test]
        public void ACorpseUnderAHeldBossIsRaisedInAPlayersParty()
        {
            Assert.That(BotGroupSupport.HardFoeStopsRaise(true, playerLedParty: true, foeHeldByLivingMember: true), Is.False);
            Assert.That(BotGroupSupport.HardFoeStopsRaise(true, playerLedParty: true, foeHeldByLivingMember: false), Is.True,
                "nobody holds the boss: the raised companion would only die again");
            Assert.That(BotGroupSupport.HardFoeStopsRaise(true, playerLedParty: false, foeHeldByLivingMember: true), Is.True,
                "gamebot raids keep the 2026-11-02 rule");
            Assert.That(BotGroupSupport.HardFoeStopsRaise(false, true, false), Is.False);
        }

        [Test]
        public void OneHealerSavesPowerForTheResurrection()
        {
            // Two Clerics short of the 176 a resurrection costs: 26 and 65 short. The nearer one saves, the other heals on.
            Assert.That(BotGroupSupport.SavesPowerForResurrection(26, 65, otherHealers: 2), Is.True);
            Assert.That(BotGroupSupport.SavesPowerForResurrection(65, 26, otherHealers: 2), Is.False);
        }

        [Test]
        public void NobodySavesWhenAResurrectorCanPayOrNoOneElseHeals()
        {
            Assert.That(BotGroupSupport.SavesPowerForResurrection(0, 65, 2), Is.False, "it can pay: it raises, no saving needed");
            Assert.That(BotGroupSupport.SavesPowerForResurrection(26, -40, 2), Is.False, "another resurrector can pay now");
            Assert.That(BotGroupSupport.SavesPowerForResurrection(26, 65, 0), Is.False, "the only healer keeps healing");
            Assert.That(BotGroupSupport.SavesPowerForResurrection(26, int.MaxValue, 1), Is.True, "the only resurrector, another healer heals");
        }
    }
}
