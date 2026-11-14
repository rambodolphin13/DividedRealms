using System.Collections.Generic;
using System.Linq;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TrialsMl8
    {
        private static readonly string[] Names =
        {
            "Agne's Crystal", "Agne's Shield", "Agne's Sword", "Centaur's Hunt", "Treasure of the Cyclops",
            "Agne's Will", "Agne's Might", "Gorgon's Secret", "Agne's Spare Arm", "Agne's Great Might",
        };

        private static List<TrialDefinition> Steps() => Ml8.Trials().ToList();

        [Test]
        public void Steps_AreAllThereInOrder()
        {
            Assert.That(Steps().Select(trial => trial.Step), Is.EqualTo(Enumerable.Range(1, 10)));
            Assert.That(Steps().Select(trial => trial.Name), Is.EqualTo(Names));
            Assert.That(Steps().All(trial => trial.ML == 8 && trial.Zone == TrialZone.Oceanus));
        }

        [Test]
        public void Scopes_MatchTheWindowText()
        {
            int[] groups = { 4, 5, 7, 9 };
            foreach (TrialDefinition trial in Steps())
                Assert.That(trial.Scope, Is.EqualTo(groups.Contains(trial.Step) ? TrialScope.Group : TrialScope.Raid), trial.Key);
        }

        [Test]
        public void Finale_NeedsEveryPieceOfAgne()
        {
            Assert.That(TrialManager.Required(Steps()[9]), Is.EqualTo(Enumerable.Range(1, 9)));
            Assert.That(Steps().Take(9).SelectMany(TrialManager.Required), Is.Empty);
        }

        [Test]
        public void Anchors_LieInGreenGladesOrArborGlen()
        {
            foreach (TrialDefinition trial in Steps())
            {
                Assert.That(trial.Anchor.X, Is.InRange(44 * 8192, 60 * 8192), trial.Key);
                Assert.That(trial.Anchor.Y, Is.InRange(80 * 8192, 88 * 8192), trial.Key);
            }
        }

        [Test]
        public void Tuning_StaysInsideTheBrief()
        {
            foreach (TrialDefinition trial in Steps())
            {
                Assert.That(trial.TimeLimitSeconds, Is.InRange(900, 2400), trial.Key);
                foreach (TrialMobSpec mob in trial.Phases.SelectMany(phase => phase.Mobs).Where(mob => !mob.Objective))
                {
                    Assert.That(mob.Level, Is.InRange(55, 70), mob.Name);
                    Assert.That(mob.Health, Is.AtMost(150000), mob.Name);
                }
            }
        }

        [Test]
        public void Replaces_NeverTakesACommonCreature()
        {
            string[] common = { "cobra guardian", "self-maintaining statue", "centaur guard" };
            Assert.That(Steps().SelectMany(trial => trial.Replaces).Intersect(common), Is.Empty);
        }

        [Test]
        public void Replaces_NoNameTwice()
        {
            List<string> names = Steps().SelectMany(trial => trial.Replaces).ToList();
            Assert.That(names, Is.Unique);
        }

        [TestCase(100, 0)]
        [TestCase(75, 1)]
        [TestCase(40, 2)]
        [TestCase(10, 3)]
        public void Ibn_StormsAtEachMark(int health, int storms)
        {
            Assert.That(Ml8.IbnStorms(health), Is.EqualTo(storms));
        }

        [TestCase(100, 0)]
        [TestCase(66, 1)]
        [TestCase(20, 2)]
        public void Dawar_FleesAtEachMark(int health, int flights)
        {
            Assert.That(Ml8.DawarFlights(health), Is.EqualTo(flights));
        }

        [Test]
        public void Torso_AlwaysTurnsUp()
        {
            Assert.That(Ml8.TorsoFound(1, 0.9), Is.False);
            Assert.That(Ml8.TorsoFound(1, 0.1), Is.True);
            Assert.That(Ml8.TorsoFound(12, 0.99), Is.True);
        }

        [Test]
        public void Talos_CallsForHealingOnce()
        {
            Assert.That(Ml8.TalosCallsForHealing(26, false), Is.False);
            Assert.That(Ml8.TalosCallsForHealing(25, false), Is.True);
            Assert.That(Ml8.TalosCallsForHealing(10, true), Is.False);
        }

        [Test]
        public void Statues_MendOnlyBelowNinetyPercent()
        {
            Assert.That(Ml8.StatueMends(95), Is.False);
            Assert.That(Ml8.StatueMends(90), Is.False);
            Assert.That(Ml8.StatueMends(89), Is.True);
            Assert.That(Ml8.StatueMends(0), Is.False);
        }

        [TestCase(1, 75)]
        [TestCase(2, 50)]
        [TestCase(3, 25)]
        [TestCase(4, 0)]
        public void Agne_HoldsAtEachWavesFloor(int wave, int floor)
        {
            Assert.That(Ml8.AgneFloor(wave), Is.EqualTo(floor));
        }

        [Test]
        public void Talos_RetryWindowIsFortyFiveMinutes()
        {
            const long lost = 1_000_000;
            Assert.That(Ml8.TalosRetryOpen(0, lost), Is.False);
            Assert.That(Ml8.TalosRetryOpen(lost, lost + 60_000), Is.True);
            Assert.That(Ml8.TalosRetryOpen(lost, lost + 45 * 60_000L), Is.True);
            Assert.That(Ml8.TalosRetryOpen(lost, lost + 45 * 60_000L + 1), Is.False);
            Assert.That(Ml8.TalosRetryOpen(lost, lost - 1), Is.False);
        }

        [Test]
        public void Shield_FightsEveryStatueInOrderWithLiveSoldierCounts()
        {
            TrialDefinition shield = Steps()[1];
            List<List<string>> fights = shield.Phases.Select(phase => phase.Mobs.Where(mob => !mob.Objective).Select(mob => mob.Name).ToList()).Where(names => names.Count > 0).ToList();
            Assert.That(fights.Select(names => names[0]), Is.EqualTo(new[] { "Tholos at the Military Academy", "Tholos Tournament Champion", "Commander Tholos", "General Tholos", "Lord Tholos" }));
            Assert.That(fights.Select(names => names.Count - 1), Is.EqualTo(new[] { 3, 0, 3, 8, 0 }), "Prima: 3 trainees, Champion alone, 3 soldiers, 8 soldiers, Lord alone");
        }

        [Test]
        public void Crystal_HasFourSetsOfTheThreeStatueKinds()
        {
            TrialDefinition crystal = Steps()[0];
            List<TrialMobSpec> statues = crystal.Phases.SelectMany(phase => phase.Mobs).Where(mob => mob.Tag == "bisul.statue").ToList();
            Assert.That(statues, Has.Count.EqualTo(12));
            Assert.That(statues.GroupBy(mob => mob.Name).Select(group => group.Count()), Is.All.EqualTo(4));
        }

        [Test]
        public void Gorgons_IncludeEverySisterAndOnlyKynthiaCounts()
        {
            TrialDefinition gorgons = Steps()[7];
            List<string> names = gorgons.Phases.SelectMany(phase => phase.Mobs).Select(mob => mob.Name).ToList();
            Assert.That(names, Is.SupersetOf(new[] { "Kaeriand", "Lomandeara", "Giathna", "Quaria", "Minarak", "Kynthia's Guide", "Kynthia" }));
            Assert.That(gorgons.Phases.SelectMany(phase => phase.Mobs).Where(mob => mob.Name == "Kynthia").All(mob => !mob.Immune), "Prima: Kynthia can be fought while her Guide lives");
        }

        [Test]
        public void CentaursHunt_NeedsOneLieutenantOnly()
        {
            TrialDefinition hunt = Steps()[3];
            Assert.That(hunt.Phases.SelectMany(phase => phase.Mobs).Where(mob => mob.Required).Select(mob => mob.Name), Is.EqualTo(new[] { "Chief Oighrig" }));
        }

        [Test]
        public void Will_BothDawarAndHisStatueMustDie()
        {
            TrialDefinition will = Steps()[5];
            List<TrialMobSpec> mobs = will.Phases.SelectMany(phase => phase.Mobs).ToList();
            Assert.That(mobs.Where(mob => mob.Required).Select(mob => mob.Name), Is.EquivalentTo(new[] { "Dawar", "controlled statue" }));
            Assert.That(mobs.Any(mob => mob.Immune), Is.False, "Prima: neither is shielded by the other");
        }
    }
}
