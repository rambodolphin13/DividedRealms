using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>DR-196: rest experience rules from the 1.94 patch notes.</summary>
    [TestFixture]
    public class UT_RestExperience
    {
        [Test]
        public void HourlyRateFallsFromFivePercentToOne()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RestExperience.HourlyPercent(1), Is.EqualTo(5.0));
                Assert.That(RestExperience.HourlyPercent(25), Is.EqualTo(3.0));
                Assert.That(RestExperience.HourlyPercent(49), Is.EqualTo(1.0));
            });
        }

        [Test]
        public void NothingUnderAnHourThenEachFullHourCounts()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RestExperience.PoolAfterRest(0, 1, TimeSpan.FromMinutes(59), 1000, 1_000_000, true, 0.5), Is.EqualTo(0));
                // level 1, 5% of a 1,000 span per hour, 3.5 hours -> 3 full hours -> 150
                Assert.That(RestExperience.PoolAfterRest(0, 1, TimeSpan.FromHours(3.5), 1000, 1_000_000, true, 0.5), Is.EqualTo(150));
                Assert.That(RestExperience.PoolAfterRest(0, 1, TimeSpan.FromHours(3.5), 1000, 1_000_000, false, 0.5), Is.EqualTo(75), "outside a capital");
                Assert.That(RestExperience.PoolAfterRest(40, 1, TimeSpan.FromHours(3), 1000, 1_000_000, true, 0.5), Is.EqualTo(190), "adds to the pool");
            });
        }

        [Test]
        public void PoolIsCappedAndNeverLowered()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RestExperience.PoolAfterRest(0, 1, TimeSpan.FromDays(30), 1000, 500, true, 0.5), Is.EqualTo(500));
                Assert.That(RestExperience.PoolAfterRest(800, 1, TimeSpan.FromDays(30), 1000, 500, true, 0.5), Is.EqualTo(800));
            });
        }

        [Test]
        public void KillBonusIsTheBaseUpToThePool()
        {
            // the two examples from the 1.94 notes
            Assert.That(RestExperience.BonusFor(16_000, 10_000), Is.EqualTo(10_000));
            Assert.That(RestExperience.BonusFor(16_000, 30_000), Is.EqualTo(16_000));
        }

        [Test]
        public void RestedWordingFollowsLive()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RestExperience.RestedText(0, 900), Is.EqualTo("You feel tired."));
                Assert.That(RestExperience.RestedText(100, 900), Is.EqualTo("You feel rested."));
                Assert.That(RestExperience.RestedText(300, 900), Is.EqualTo("You feel well rested."));
                Assert.That(RestExperience.RestedText(900, 900), Is.EqualTo("You feel very well rested."));
            });
        }
    }
}
