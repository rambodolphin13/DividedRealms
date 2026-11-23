using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>DR-197: the zone coin bonus (live 1.87 double coin, 1.116 Darkness Falls +150%).</summary>
    [TestFixture]
    public class UT_ZoneCoinBonus
    {
        [Test]
        public void CoinBonusAddsTheZonePercentOnlyWhenZoneBonusesAreOn()
        {
            Assert.Multiple(() =>
            {
                Assert.That(LootGeneratorMoney.WithZoneCoinBonus(1000, 100, true), Is.EqualTo(2000), "classic / SI / RvR double coin");
                Assert.That(LootGeneratorMoney.WithZoneCoinBonus(1000, 150, true), Is.EqualTo(2500), "Darkness Falls +150%");
                Assert.That(LootGeneratorMoney.WithZoneCoinBonus(1000, 0, true), Is.EqualTo(1000), "zone without a bonus");
                Assert.That(LootGeneratorMoney.WithZoneCoinBonus(1000, 100, false), Is.EqualTo(1000), "enable_zone_bonuses off");
            });
        }
    }
}
