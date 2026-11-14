using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TrialsMl9
    {
        private static readonly string[] Names =
        {
            "Wings of Aerus", "Lachlen's Explorers", "Atlantis Warlords", "Kings of Aerus", "Scholars of Aerus",
            "Patrolling the City", "Uncontrolled Host", "Guardians of the Phoenix", "Centaur's Patrol", "Rise of the Phoenix",
        };

        private static List<TrialDefinition> Trials => Ml9.Trials().ToList();

        [Test]
        public void Steps_ExistInOrder()
        {
            Assert.That(Trials.Select(trial => trial.Step), Is.EqualTo(Enumerable.Range(1, 10)));
            Assert.That(Trials.Select(trial => trial.Name), Is.EqualTo(Names));
            Assert.That(Trials.All(trial => trial.ML == 9 && trial.Zone == TrialZone.Aerus));
        }

        [Test]
        public void OnlyLachlenIsAGroupTrial()
        {
            Assert.That(Trials.Where(trial => trial.Scope == TrialScope.Group).Select(trial => trial.Step), Is.EqualTo(new[] { 2 }));
            Assert.That(Trials.Where(trial => trial.Step != 2).All(trial => trial.Scope == TrialScope.Raid));
        }

        [Test]
        public void Finale_NeedsTheFirstNine()
        {
            Assert.That(TrialManager.Required(Trials[9]), Is.EqualTo(Enumerable.Range(1, 9)));
        }

        [Test]
        public void Anchors_LieInsideTheCityOfAerus()
        {
            foreach (TrialDefinition trial in Trials)
            {
                Assert.That(trial.Anchor.X, Is.InRange(8192, 73728), trial.Key);
                Assert.That(trial.Anchor.Y, Is.InRange(8192, 73728), trial.Key);
            }
        }

        [Test]
        public void Tuning_StaysInsideTheBrief()
        {
            foreach (TrialDefinition trial in Trials)
            {
                Assert.That(trial.TimeLimitSeconds, Is.InRange(900, 2400), trial.Key);
                foreach (TrialMobSpec mob in trial.Phases.SelectMany(phase => phase.Mobs).Where(mob => !mob.Objective))
                    Assert.That((int)mob.Level, Is.InRange(55, 70), mob.Name);
            }
        }

        [Test]
        public void Replaces_NeverTakeACommonCreature()
        {
            string[] common = { "avriel", "iaculus", "enraged iaculus", "elusive jinni", "avriel young" };
            Assert.That(Trials.SelectMany(trial => trial.Replaces).Intersect(common), Is.Empty);
        }

        [TestCase(eRealm.Albion, false, Ml9.KyrosShape.Armsman)]
        [TestCase(eRealm.Albion, true, Ml9.KyrosShape.Wizard)]
        [TestCase(eRealm.Hibernia, false, Ml9.KyrosShape.Champion)]
        [TestCase(eRealm.Hibernia, true, Ml9.KyrosShape.Enchanter)]
        [TestCase(eRealm.Midgard, false, Ml9.KyrosShape.Warrior)]
        [TestCase(eRealm.Midgard, true, Ml9.KyrosShape.Runemaster)]
        public void Kyros_BecomesTheRealmAndKindOfWhoeverHitHim(eRealm realm, bool caster, Ml9.KyrosShape shape)
        {
            Assert.That(Ml9.KyrosShapeFor(realm, caster), Is.EqualTo(shape));
        }

        [Test]
        public void Lachlen_RunsOutOfHeals()
        {
            Assert.That(Ml9.LachlenMayHeal(Ml9.LachlenHeals - 1), Is.True);
            Assert.That(Ml9.LachlenMayHeal(Ml9.LachlenHeals), Is.False);
        }

        [Test]
        public void Lachlen_IsSpokenToBeforeHisExplorersTurn()
        {
            List<TrialPhase> phases = Trials[1].Phases;
            Assert.That(phases[0].Mobs.Single().Objective, Is.True);
            Assert.That(phases[0].Mobs.Single().Name, Is.EqualTo("Lachlen"));
            Assert.That(phases[1].Mobs.Count(mob => mob.Name == "Weary Explorer"), Is.EqualTo(6));
            Assert.That(phases[2].Mobs.Single().Name, Is.EqualTo("Lachlen"));
        }

        [Test]
        public void Lethos_AndAllFourAdvisorsMustFall()
        {
            TrialPhase phase = Trials[2].Phases.Single();
            Assert.That(phase.Mobs.Where(mob => mob.Required).Select(mob => mob.Name), Is.EquivalentTo(new[]
            {
                "Lethos the Unifier", "Advisor Anzelm", "Advisor Timanthes", "Advisor Zarlanthus", "Advisor Gyges",
            }));
            Assert.That(phase.Mobs.Where(mob => mob.Name.StartsWith("Advisor")).All(mob => mob.Immune && mob.AggroRange == 0), "advisors sleep until Lethos is struck");
        }

        [Test]
        public void Lethos_TakesAnAdvisorsGiftWhileHeStands_ThenPassesHisOwnToTheRest()
        {
            Assert.That(Ml9.Heirs("gyges", true, new[] { "lethos", "anzelm" }), Is.EqualTo(new[] { "lethos" }));
            Assert.That(Ml9.Heirs("lethos", false, new[] { "anzelm", "gyges" }), Is.EqualTo(new[] { "anzelm", "gyges" }));
            Assert.That(Ml9.Heirs("gyges", false, new[] { "anzelm" }), Is.EqualTo(new[] { "anzelm" }));
        }

        [Test]
        public void Kings_WakeOnlyWithTheControllersCrystal_InAnyOrder()
        {
            TrialPhase phase = Trials[3].Phases.Single();
            Assert.That(Trials[3].Phases, Has.Count.EqualTo(1), "one phase, so the kings can be taken in any order");
            Assert.That(phase.Mobs.Count(mob => mob.Objective), Is.EqualTo(4));
            Assert.That(phase.Mobs.Single(mob => !mob.Objective).Name, Is.EqualTo("avriel controller"));
            Assert.That(phase.Done, Is.Not.Null);
        }

        [Test]
        public void Ghosts_ResistMatterAndFearHeat()
        {
            Assert.That(Ml9.GhostDamage(eDamageType.Matter, 100), Is.LessThan(100));
            Assert.That(Ml9.GhostDamage(eDamageType.Heat, 100), Is.GreaterThan(Ml9.GhostDamage(eDamageType.Crush, 100)));
            Assert.That(Ml9.GhostDamage(eDamageType.Crush, 100), Is.GreaterThan(Ml9.GhostDamage(eDamageType.Slash, 100)));
            Assert.That(Ml9.GhostDamage(eDamageType.Slash, 100), Is.GreaterThan(Ml9.GhostDamage(eDamageType.Thrust, 100)));
            Assert.That(Ml9.GhostDamage(eDamageType.Thrust, 100), Is.GreaterThan(100));
        }

        [Test]
        public void Nelos_TeachesALessonAfterFiveUses()
        {
            Assert.That(Ml9.StudentsLearn(Ml9.NelosLessons - 1), Is.False);
            Assert.That(Ml9.StudentsLearn(Ml9.NelosLessons), Is.True);
            Assert.That(Ml9.NelosAbilities, Is.EquivalentTo(new[] { "pbaoe", "heal", "stun" }));
        }

        [Test]
        public void Katri_IsTheOnlyKillThatCounts_AndHoldsAtThreeQuarters()
        {
            TrialDefinition patrol = Trials[5];
            Assert.That(patrol.Phases.SelectMany(phase => phase.Mobs).Where(mob => mob.Required).Select(mob => mob.Name), Is.EqualTo(new[] { "Katri" }));
            Assert.That(Ml9.KatriFloor, Is.EqualTo(75));
            Assert.That(Ml9.KatriGuardians, Is.EqualTo(5));
        }

        [Test]
        public void Iaculus_ResistsBodyAndFearsCold()
        {
            Assert.That(Ml9.IaculusDamage(eDamageType.Body, 100), Is.LessThan(100));
            Assert.That(Ml9.IaculusDamage(eDamageType.Cold, 100), Is.GreaterThan(100));
            Assert.That(Ml9.IaculusDamage(eDamageType.Slash, 100), Is.EqualTo(100));
        }

        [Test]
        public void Sphinxes_AnyOneOfThreeIsEnough()
        {
            TrialPhase phase = Trials[7].Phases.Single();
            Assert.That(phase.Mobs.Select(mob => mob.Name), Is.EquivalentTo(new[] { "Chan", "Kiran", "Roshan" }));
            Assert.That(phase.Mobs.All(mob => mob.AggroRange == 0 && !mob.Required), "neutral until struck");
            Assert.That(phase.Done, Is.Not.Null);
        }

        [Test]
        public void Chan_NoticesOnlyThoseOnHisRoad()
        {
            Assert.That(Ml9.PastChan(Ml9.BypassX + 1, 12049), Is.True);
            Assert.That(Ml9.PastChan(Ml9.BypassX, 12049), Is.False);
            Assert.That(Ml9.PastChan(Ml9.BypassX + 1, 15900), Is.False);
        }

        [Test]
        public void Neola_ComesAfterThirtySisters()
        {
            Assert.That(Ml9.NeolaSisters, Is.EqualTo(30));
            Assert.That(Trials[8].Phases, Has.Count.EqualTo(2));
        }

        [Test]
        public void Aerus_AdmitsOnlyMasterLevelEight()
        {
            Assert.That(Trials.All(trial => trial.Refuse != null));
            Assert.That(Ml9.AerusMasterLevel, Is.EqualTo(8));
        }

        [TestCase(3, 75)]
        [TestCase(2, 50)]
        [TestCase(1, 25)]
        [TestCase(0, 0)]
        public void Phoenix_LosesAQuarterForEachGenerator(int generators, int floor)
        {
            Assert.That(Ml9.PhoenixFloor(generators), Is.EqualTo(floor));
        }

        [Test]
        public void Phoenix_WeakensAsGeneratorsFall()
        {
            Assert.That(Ml9.PhoenixFireball(3), Is.GreaterThan(Ml9.PhoenixFireball(2)));
            Assert.That(Ml9.PhoenixFireball(3), Is.LessThanOrEqualTo(300));
            Assert.That(Ml9.PhoenixFireball(0), Is.Zero);
            Assert.That(Ml9.PhoenixFlare(0), Is.Zero);
            Assert.That(Ml9.PhoenixFireballReach(3), Is.LessThan(Ml9.PhoenixFireballReach(2)));
            Assert.That(Ml9.PhoenixFireballReach(2), Is.LessThan(Ml9.PhoenixFireballReach(1)));
        }

        [Test]
        public void Phoenix_LeashCoversTheGenerators()
        {
            Assert.That(Trials[9].LeashRange, Is.GreaterThan(10500));
            Assert.That(Trials[9].Phases.SelectMany(phase => phase.Mobs).Count(mob => mob.Name == "Phoenix Generator"), Is.EqualTo(3));
        }
    }
}
