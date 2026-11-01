using System.Numerics;
using NUnit.Framework;
using OfflineDaoc.TestClient;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TestClientRules
    {
        [Test]
        public void AWalkThatLeadsAwayFromTheGoalIsNotStalled()
        {
            // Walking 100 units a second, for far longer than the 8 seconds; the goal plays no part.
            var watch = new WalkWatch(Vector3.Zero, 0);

            for (int second = 1; second <= 60; second++)
                Assert.That(watch.Stalled(new Vector3(second * 100, 0, 0), second * 1000), Is.False);
        }

        [Test]
        public void StandingStillStallsAfterEightSeconds()
        {
            var watch = new WalkWatch(new Vector3(500, 500, 0), 0);

            Assert.Multiple(() =>
            {
                Assert.That(watch.Stalled(new Vector3(500, 500, 0), WalkWatch.StallMs), Is.False);
                Assert.That(watch.Stalled(new Vector3(500, 500, 0), WalkWatch.StallMs + 1000), Is.True);
            });
        }

        [Test]
        public void JitterInPlaceStillStalls()
        {
            var watch = new WalkWatch(Vector3.Zero, 0);

            Assert.That(watch.Stalled(new Vector3(WalkWatch.MinStep - 1, 0, 0), 9000), Is.True);
        }

        [Test]
        public void WalkingAgainRestartsTheClock()
        {
            var watch = new WalkWatch(Vector3.Zero, 0);

            Assert.Multiple(() =>
            {
                Assert.That(watch.Stalled(new Vector3(50, 0, 0), 7000), Is.False);
                Assert.That(watch.Stalled(new Vector3(50, 0, 0), 14000), Is.False);
                Assert.That(watch.Stalled(new Vector3(50, 0, 0), 16000), Is.True);
            });
        }

        [Test]
        public void TheWeaponFollowsTheTrainedLine()
        {
            Assert.Multiple(() =>
            {
                Assert.That(KitRules.WeaponForSpecs(new[] { "Crush", "Slash", "Thrust" }), Is.EqualTo(2), "a Paladin in Crush");
                Assert.That(KitRules.WeaponForSpecs(new[] { "Chants", "Two Handed", "Parry" }), Is.EqualTo(6), "a line that isn't a weapon is skipped");
                Assert.That(KitRules.WeaponForSpecs(new[] { "Void", "Light", "Mana" }), Is.Zero, "a pure caster has none");
            });
        }
    }
}
