using System.Linq;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Master Level 5 checked against live (live pass 2026-11-14).</summary>
    [TestFixture]
    public class UT_TrialsMl5Live
    {
        private static TrialDefinition Step(int step) => TrialCatalog.Find($"5.{step}");

        [Test]
        public void Reflection_EitherCrocodileGivesTheMirror()
        {
            TrialDefinition reflection = Step(5);
            Assert.That(reflection.Phases, Has.Count.EqualTo(1));
            TrialPhase phase = reflection.Phases[0];
            Assert.That(phase.KillCount, Is.EqualTo(1));
            Assert.That(phase.Mobs.Select(mob => mob.At), Is.EquivalentTo(new TrialPoint?[] { Ml5.ReflectionNorth, Ml5.ReflectionSouth }));
            Assert.That(phase.Mobs.All(mob => mob.Name == "Cau Surae" && mob.Reflect == 0 && mob.Required));
        }

        [Test]
        public void Duamutef_HitWhileShrunkIsNotFed()
        {
            Assert.That(Ml5.SwarmHeal(6, 28000, struck: true), Is.EqualTo(0));
            Assert.That(Ml5.SwarmHeal(6, 28000, struck: false), Is.EqualTo(Ml5.ScarabHeal(6, 28000)));
            Assert.That(Ml5.SwarmHeal(0, 28000, struck: false), Is.EqualTo(0));
        }

        [Test]
        public void Duamutef_IsNeverImmune()
        {
            Assert.That(Step(2).Phases.SelectMany(phase => phase.Mobs).Any(mob => mob.Immune), Is.False);
        }

        [Test]
        public void HapyCopies_FadeOnceHurtALittle()
        {
            Assert.That(Ml5.CopyFades(100), Is.False);
            Assert.That(Ml5.CopyFades(Ml5.HapyCopyFadePercent + 1), Is.False);
            Assert.That(Ml5.CopyFades(Ml5.HapyCopyFadePercent), Is.True);
        }

        [Test]
        public void Imsety_LionsStrikeOneTargetNotAnArea()
        {
            Assert.That(Step(4).Phases.SelectMany(phase => phase.Pulses), Is.Empty);
            Assert.That(Ml5.LionStrikeDamage, Is.LessThanOrEqualTo(300));
        }

        [Test]
        public void Ammut_JudgesOnlyThoseNotInMummyForm()
        {
            Assert.That(Ml5.MummyFor(4, finishedToken: true), Is.True);
            Assert.That(Ml5.MummyFor(4, finishedToken: false), Is.False);
            Assert.That(Ml5.MummyFor(5, finishedToken: false), Is.True);
            Assert.That(Ml5.MummyFor(9, finishedToken: false), Is.True);
            Assert.That(Ml5.AddSeconds(true), Is.LessThan(Ml5.AddSeconds(false)));
        }

        [Test]
        public void BoundChambers_ClearTheImportedPack()
        {
            foreach (int step in new[] { 6, 7, 8 })
                Assert.That(Step(step).ClearRadius, Is.EqualTo(Ml5.ChamberClearRadius), $"5.{step}");
        }

        [Test]
        public void Credit_ChainsInLiveOrder()
        {
            Assert.That(Step(9).RequiresSteps, Is.EqualTo(new[] { 6, 7, 8 }));
            Assert.That(TrialManager.Required(Step(10)), Is.EqualTo(Enumerable.Range(1, 9)));
            Assert.That(Enumerable.Range(1, 8).All(step => TrialManager.Required(Step(step)).Any() == false));
            Assert.That(Step(10).Scope, Is.EqualTo(TrialScope.Raid));
        }
    }
}
