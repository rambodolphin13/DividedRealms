using DOL.GS.PropertyCalc;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_RunSpeedProperty
    {
        // DR-450: player_run_speed_percent scales the base run speed; a missing or broken value changes nothing.
        [TestCase(100, 1.0)]
        [TestCase(150, 1.5)]
        [TestCase(50, 0.5)]
        [TestCase(0, 1.0)]
        [TestCase(-20, 1.0)]
        public void ThePropertyIsAFactor(int percent, double factor)
        {
            Assert.That(MaxSpeedCalculator.RunSpeedFactor(percent), Is.EqualTo(factor));
        }
    }
}
