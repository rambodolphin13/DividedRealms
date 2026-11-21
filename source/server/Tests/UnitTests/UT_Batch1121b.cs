using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_Batch1121b
    {
        // DR-422: a step of a Master Level already reached by experience is recorded and announced, once.
        [TestCase(8, 8, false, true)]
        [TestCase(9, 8, false, true)]
        [TestCase(8, 8, true, false)]
        [TestCase(7, 8, false, false)]
        public void AStepOfAMasterLevelAlreadyHeldIsStillRecorded(int ml, int stepMl, bool finished, bool expected)
        {
            Assert.That(MasterLevelRules.IsPastStep(ml, stepMl, finished), Is.EqualTo(expected));
            if (expected)
                Assert.That(MasterLevelRules.CanEarnStep(ml, stepMl, finished), Is.False);
        }

        // DR-393: only trial monsters that take no damage are skipped.
        [Test]
        public void OrdinaryTargetsCanBeHurt()
        {
            Assert.That(BotBrain.CannotBeHurt(null), Is.False);
        }

        // DR-289: one refusal line for every teleport a relic carrier tries.
        [Test]
        public void RelicCarriersGetOneRefusalLine()
        {
            Assert.That(GameRelic.CarrierPortRefusal, Does.Contain("relic"));
            Assert.That(GameRelic.IsPlayerCarryingRelic(null), Is.False);
        }
    }
}
