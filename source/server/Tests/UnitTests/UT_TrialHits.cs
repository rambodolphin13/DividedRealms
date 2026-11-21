using DOL.GS;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TrialHits
    {
        // DR-397: trial damage outside a normal attack names what hit and for how much.
        [Test]
        public void ATrialHitNamesItsSource()
        {
            Assert.That(TrialHits.HitLine("Aithos", 240, eDamageType.Heat), Is.EqualTo("Aithos hits you for 240 heat damage!"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void AHitWithNoMonsterBehindItIsTheTrials(string source)
        {
            Assert.That(TrialHits.HitLine(source, 50, eDamageType.Crush), Is.EqualTo("The trial hits you for 50 crush damage!"));
        }
    }
}
