using System;
using System.Linq;
using DOL.GS;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_MasterLevelTrials
    {
        [Test]
        public void Catalog_HasNoMistakes()
        {
            Assert.That(TrialCatalog.Validate(), Is.Empty);
        }

        [Test]
        public void Scaling_ShrinksForSmallPartiesWithinLimits()
        {
            Assert.That(TrialScaling.HealthScale(TrialScope.Group, 8), Is.EqualTo(1.0));
            Assert.That(TrialScaling.HealthScale(TrialScope.Group, 4), Is.EqualTo(0.5));
            Assert.That(TrialScaling.HealthScale(TrialScope.Raid, 1), Is.EqualTo(TrialScaling.MinimumScale));
            Assert.That(TrialScaling.HealthScale(TrialScope.Raid, 80), Is.EqualTo(TrialScaling.MaximumScale));
        }

        [Test]
        public void Scaling_LeavesSoloTrialsAlone()
        {
            Assert.That(TrialScaling.HealthScale(TrialScope.Solo, 8), Is.EqualTo(1.0));
            Assert.That(TrialScaling.DamageScale(TrialScope.Solo, 8), Is.EqualTo(1.0));
        }

        [Test]
        public void Scaling_DamageFallsSlowerThanHealth()
        {
            Assert.That(TrialScaling.DamageScale(TrialScope.Group, 2), Is.GreaterThan(TrialScaling.HealthScale(TrialScope.Group, 2)));
        }

        [Test]
        public void Finale_NeedsTheEarlierSteps()
        {
            TrialDefinition finale = new() { ML = 1, Step = 10 };
            TrialDefinition first = new() { ML = 1, Step = 1 };
            Assert.That(TrialManager.Required(finale), Is.EqualTo(Enumerable.Range(1, 9)));
            Assert.That(TrialManager.Required(first), Is.Empty);
        }

        [Test]
        public void Finale_RequirementsCanBeSetByHand()
        {
            TrialDefinition trial = new() { ML = 1, Step = 6, RequiresSteps = new[] { 1, 4 } };
            Assert.That(TrialManager.Required(trial), Is.EqualTo(new[] { 1, 4 }));
        }

        [Test]
        public void Regions_EachZoneHasThreeRealmCopies()
        {
            foreach (TrialZone zone in Enum.GetValues<TrialZone>())
                Assert.That(TrialManager.RegionsOf(zone).Distinct().Count(), Is.EqualTo(3), zone.ToString());
        }
    }
}
