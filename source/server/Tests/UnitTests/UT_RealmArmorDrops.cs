using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>DR-486: realm-0 armor in drop tables must be wearable by the killer's realm.</summary>
    [TestFixture]
    public class UT_RealmArmorDrops
    {
        [TestCase(eRealm.Albion, eObjectType.Plate, true)]
        [TestCase(eRealm.Midgard, eObjectType.Plate, false)]    // Breastplate of Demonic Dreams from Adremal
        [TestCase(eRealm.Hibernia, eObjectType.Plate, false)]
        [TestCase(eRealm.Midgard, eObjectType.Chain, true)]
        [TestCase(eRealm.Hibernia, eObjectType.Chain, false)]
        [TestCase(eRealm.Albion, eObjectType.Studded, true)]
        [TestCase(eRealm.Hibernia, eObjectType.Studded, false)]
        [TestCase(eRealm.Hibernia, eObjectType.Reinforced, true)]
        [TestCase(eRealm.Albion, eObjectType.Reinforced, false)]
        [TestCase(eRealm.Hibernia, eObjectType.Scale, true)]
        [TestCase(eRealm.Midgard, eObjectType.Scale, false)]
        [TestCase(eRealm.Midgard, eObjectType.Cloth, true)]
        [TestCase(eRealm.Hibernia, eObjectType.Leather, true)]
        [TestCase(eRealm.Midgard, eObjectType.Sword, true)]
        [TestCase(eRealm.Midgard, eObjectType.Magical, true)]
        public void RealmZeroArmorOnlyDropsWhereTheRealmCanWearIt(eRealm realm, eObjectType type, bool expected) =>
            Assert.That(LootGeneratorBase.RealmCanWearArmor(realm, (int)type), Is.EqualTo(expected));
    }
}
