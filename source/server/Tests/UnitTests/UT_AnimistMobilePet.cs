using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Patch 1.111 gave each Animist line a level 40 pet that walks and follows like any other class's pet.
    /// The server planted them as rooted turrets (Discord report 2026-11-12: "animist pets aren't following").
    /// A SummonAnimistPet spell with DamageType 2 is now such a movable pet; 0 and 1 stay the planted turrets.
    /// </summary>
    [TestFixture]
    public class UT_AnimistMobilePet
    {
        private static Spell Summon(eSpellType type, int damageType) => new(new DbSpell
        {
            SpellID = 77769, Name = "Guardian of the Forest", Type = type.ToString(), Target = "Self",
            DamageType = damageType, LifeDrainReturn = 77910, Damage = -100, Value = 50,
        }, 40);

        [Test]
        public void DamageTypeTwoMarksTheMovablePet() =>
            Assert.That(SummonAnimistMainPet.IsMobilePet(Summon(eSpellType.SummonAnimistPet, SummonAnimistMainPet.MobilePetDamageType)), Is.True);

        [TestCase(0)]
        [TestCase(1)]
        public void MainTurretsStayPlanted(int damageType) =>
            Assert.That(SummonAnimistMainPet.IsMobilePet(Summon(eSpellType.SummonAnimistPet, damageType)), Is.False);

        [TestCase(eSpellType.SummonAnimistFnF)]
        [TestCase(eSpellType.SummonAnimistFnFCustom)]
        [TestCase(eSpellType.SummonAnimistAmbusher)]
        public void FieldTurretsAreNeverMovable(eSpellType type) =>
            Assert.That(SummonAnimistMainPet.IsMobilePet(Summon(type, SummonAnimistMainPet.MobilePetDamageType)), Is.False);

        [Test]
        public void NoSpellIsNotMovable() => Assert.That(SummonAnimistMainPet.IsMobilePet(null), Is.False);
    }
}
