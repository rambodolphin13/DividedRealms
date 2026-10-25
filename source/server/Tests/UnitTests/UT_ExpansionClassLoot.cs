using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>ROG loot for the expansion classes uses their own weapons, armor, shields and lines.</summary>
    [TestFixture]
    public class UT_ExpansionClassLoot
    {
        [Test]
        public void ExpansionClassesGetTheirOwnWeaponsAndArmor()
        {
            for (int i = 0; i < 50; i++)
            {
                Assert.That(GeneratedUniqueItem.GetAlbionWeapon(eCharacterClass.MaulerAlb), Is.AnyOf(eObjectType.FistWraps, eObjectType.MaulerStaff));
                Assert.That(GeneratedUniqueItem.GetMidgardWeapon(eCharacterClass.Valkyrie), Is.AnyOf(eObjectType.Sword, eObjectType.Spear, eObjectType.Staff, eObjectType.Shield));
                Assert.That(GeneratedUniqueItem.GetAlbionWeapon(eCharacterClass.Heretic), Is.AnyOf(eObjectType.CrushingWeapon, eObjectType.Flexible, eObjectType.Staff, eObjectType.Shield));
                Assert.That(GeneratedUniqueItem.GetHiberniaWeapon(eCharacterClass.Vampiir), Is.EqualTo(eObjectType.Piercing));
            }
            Assert.That(GeneratedUniqueItem.GetMidgardArmorType(eCharacterClass.Valkyrie, 50), Is.EqualTo(eObjectType.Chain));
            Assert.That(GeneratedUniqueItem.GetHiberniaArmorType(eCharacterClass.MaulerHib, 50), Is.EqualTo(eObjectType.Leather));
            Assert.That(GeneratedUniqueItem.GetHiberniaArmorType(eCharacterClass.Bainshee, 50), Is.EqualTo(eObjectType.Cloth));
            Assert.That(GeneratedUniqueItem.GetMidgardWeapon(eCharacterClass.Warlock), Is.EqualTo(eObjectType.Staff));
        }

        [Test]
        public void SkillsAndStatsFollowEachClassesLines()
        {
            Assert.That(ExpansionClassLoot.SkillValid(eCharacterClass.Warlock, eProperty.Skill_Cursing), Is.True);
            Assert.That(ExpansionClassLoot.SkillValid(eCharacterClass.Warlock, eProperty.Skill_Sword), Is.False);
            Assert.That(ExpansionClassLoot.SkillValid(eCharacterClass.MaulerMid, eProperty.Skill_Power_Strikes), Is.True);
            Assert.That(ExpansionClassLoot.SkillValid(eCharacterClass.Vampiir, eProperty.Skill_Dementia), Is.True);
            Assert.That(ExpansionClassLoot.StatTemplate(eCharacterClass.Bainshee), Is.EqualTo(eCharacterClass.Eldritch));
            Assert.That(ExpansionClassLoot.StatTemplate(eCharacterClass.Armsman), Is.EqualTo(eCharacterClass.Armsman), "classic classes unchanged");
            Assert.That(ExpansionClassLoot.TryShieldSize(eCharacterClass.Valkyrie, out int size) && size == 3, Is.True);
            Assert.That(ClassItemRules.ItemClass(eCharacterClass.DeathKnight), Is.EqualTo(eCharacterClass.Necromancer));
        }
    }
}
