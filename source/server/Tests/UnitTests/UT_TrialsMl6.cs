using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Master Level 6 against live (Prima's guide, the cairdeas/bkdaoc ML6 page, Allakhazam). Data and pure rules only.</summary>
    [TestFixture]
    public class UT_TrialsMl6
    {
        private static TrialDefinition Step(int step) => TrialCatalog.Find($"6.{step}");

        private static IEnumerable<TrialMobSpec> Mobs(int step) => Step(step).Phases.SelectMany(phase => phase.Mobs);

        [Test]
        public void Sacrifice_HasSevenEscorts()
        {
            Assert.That(Mobs(2).Where(mob => mob.Name == "siam-he sacrificial escort").Sum(mob => mob.Count), Is.EqualTo(7));
            Assert.That(Step(2).StarterName, Does.Contain("anubite"));
        }

        [Test]
        public void Lateef_TrioStaysTenMinutesUnlessEngaged()
        {
            Assert.That(Ml6.AmbushSeconds, Is.EqualTo(600));
            Assert.That(Ml6.AmbushLeaves(599, false), Is.False);
            Assert.That(Ml6.AmbushLeaves(600, false), Is.True);
            Assert.That(Ml6.AmbushLeaves(900, true), Is.False);
            Assert.That(Step(3).TimeLimitSeconds, Is.GreaterThanOrEqualTo(Ml6.LateefPatienceSeconds + Ml6.AmbushSeconds));
            Assert.That(Step(3).LeashRange, Is.GreaterThan(30000), "the bridge is about 29,400 from Lateef");
        }

        [Test]
        public void Lateef_SutekhIsTheKill()
        {
            Assert.That(Mobs(3).Where(mob => mob.Required).Select(mob => mob.Name), Is.EquivalentTo(new[] { "Fadil", "Udaji", "Sutekh" }));
            Assert.That(Step(3).Phases[1].Pulses.Single().SourceTag, Is.EqualTo("fadil"));
        }

        [Test]
        public void Infestation_SplitsAtHalfIntoOneRealAndTwoShades()
        {
            Assert.That(Ml6.PallidaSplitPercent, Is.EqualTo(50));
            Assert.That(Enumerable.Range(-3, 10).Select(Ml6.PallidaRealSplit).All(index => index is >= 0 and <= 2));
        }

        [Test]
        public void Flames_NeedsTwentyGlandsAndSixtySeconds()
        {
            Assert.That(Ml6.GlandsNeeded, Is.EqualTo(20));
            Assert.That(Ml6.FlameSeconds, Is.EqualTo(60));
            Assert.That(Step(5).Scope, Is.EqualTo(TrialScope.Solo), "only the incinerator's user gets credit");
            Assert.That(Step(5).Phases[1].Mobs.Count(mob => mob.Objective), Is.EqualTo(4));
        }

        [Test]
        public void Siraadi_ComesAfterSixChanters()
        {
            Assert.That(Mobs(6).Where(mob => mob.Name == "siam-he chanter").Sum(mob => mob.Count), Is.EqualTo(Ml6.ChanterCount));
            Assert.That(Step(6).Phases.Last().Mobs.Single().Name, Is.EqualTo("Siraadi"));
        }

        [Test]
        public void Ankhkare_TakesAnyTwoTolls()
        {
            Assert.That(Step(7).Refuse, Is.Not.Null);
            Assert.That(Step(7).RequiresSteps, Is.Null);
            Assert.That(Ml6.OfferingSteps, Is.EqualTo(new[] { 4, 5, 6 }));
            Assert.That(Ml6.OfferingsHeld(step => step != 5), Is.EqualTo(2));
            Assert.That(Ml6.OfferingsHeld(_ => false), Is.EqualTo(0));
            Assert.That(Ml6.AnkhkareHealth(3), Is.LessThan(Ml6.AnkhkareHealth(2)), "each toll weakens him");
            Assert.That(Step(7).Phases.Where(phase => !phase.Mobs.Any(mob => mob.Required)).All(phase => phase.Done != null || phase.SurviveSeconds > 0));
        }

        [Test]
        public void Efreet_EightKillableChallengersAndTwentyMinutes()
        {
            List<TrialMobSpec> challengers = Mobs(8).Where(mob => mob.Name == "efreeti challenger").ToList();
            Assert.That(challengers, Has.Count.EqualTo(Ml6.BrazierCount));
            Assert.That(Ml6.BrazierCount, Is.EqualTo(8));
            Assert.That(challengers.All(mob => !mob.Immune && !mob.Required && !mob.Hurts.HasFlag(TrialDamageKind.Melee)));
            Assert.That(Ml6.OrbSeconds, Is.EqualTo(1200));
            Assert.That(Step(8).TimeLimitSeconds, Is.GreaterThan(Ml6.OrbSeconds));
            TrialMobSpec orb = Mobs(8).Single(mob => mob.Name == "Orb of Apollo");
            Assert.That(orb.Required && orb.Reflect > 0, "the orb has a damage shield and must die");
        }

        [Test]
        public void Efreet_FallenChallengersStrengthenTheRest()
        {
            Assert.That(Ml6.ChallengerBoltDamage(0), Is.EqualTo(Ml6.ChallengerBolt));
            Assert.That(Ml6.ChallengerBoltDamage(4), Is.GreaterThan(Ml6.ChallengerBoltDamage(1)));
            Assert.That(Ml6.ChallengerBoltDamage(99), Is.EqualTo(Ml6.ChallengerBoltDamage(Ml6.BrazierCount)));
            Assert.That(Ml6.ChallengerBoltDamage(Ml6.BrazierCount), Is.LessThanOrEqualTo(300));
        }

        [Test]
        public void Fortress_DragonBreathIsTheLikeliestTrap()
        {
            Assert.That(Ml6.GateTrapFor(0), Is.EqualTo(Ml6.GateTrapKind.DragonBreath));
            Assert.That(Ml6.GateTrapFor(1), Is.EqualTo(Ml6.GateTrapKind.DragonBreath));
            Assert.That(Ml6.GateTrapFor(2), Is.EqualTo(Ml6.GateTrapKind.Battler));
            Assert.That(Ml6.GateTrapFor(3), Is.EqualTo(Ml6.GateTrapKind.Horde));
        }

        [Test]
        public void Chimera_BreathsCycleThroughThree()
        {
            Assert.That(Enumerable.Range(0, 6).Select(Ml6.ChimeraBreathIndex), Is.EqualTo(new[] { 0, 1, 2, 0, 1, 2 }));
            Assert.That(Mobs(10).Single(mob => mob.Name == "Chimera").Reflect, Is.GreaterThan(0));
        }

        [Test]
        public void Credit_EveryStepCanBeWonAndLevelsAtTen()
        {
            Assert.That(TrialCatalog.All.Count(trial => trial.ML == 6), Is.EqualTo(10));
            Assert.That(TrialCatalog.Validate().Where(error => error.StartsWith("6.")), Is.Empty);
        }
    }
}
