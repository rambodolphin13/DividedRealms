using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Code fixes from the 2026-11-17 crafting pass (Research\Audits\Crafting-Pass-2026-11-17).</summary>
[TestFixture]
public class UT_CraftingPass1117
{
    [TestCase(51, 32)]
    [TestCase(49, 31)]
    [TestCase(45, 28)]
    [TestCase(44, 27)]
    [TestCase(60, 32)]
    [TestCase(0, 0)]
    public void ImbuePointsFollowTheLiveTableWhateverTheQuality(int level, int expected)
    {
        Assert.That(SpellCrafting.MaxImbuePointsForLevel(level), Is.EqualTo(expected));
    }

    [TestCase(2, 1)]
    [TestCase(5, 2)]
    [TestCase(14, 8)]
    [TestCase(26, 15)]
    [TestCase(29, 16)]
    public void StatGemsCostThe118Points(int value, int cost)
    {
        Assert.That(SpellCrafting.GemImbuePoints((int)eProperty.Strength, value), Is.EqualTo(cost));
    }

    [Test]
    public void OldStatGemValuesKeepTheirOldCost()
    {
        Assert.That(SpellCrafting.GemImbuePoints((int)eProperty.Strength, 28), Is.EqualTo(19));
        Assert.That(SpellCrafting.GemImbuePoints((int)eProperty.Strength, 1), Is.EqualTo(1));
    }

    [Test]
    public void ResistGemCostIsUnchanged()
    {
        Assert.That(SpellCrafting.GemImbuePoints((int)eProperty.Resist_Heat, 7), Is.EqualTo(12));
    }

    [TestCase(2605, 0)]
    [TestCase(2600, 1)]
    [TestCase(2601, 2)]
    [TestCase(2602, 3)]
    [TestCase(1, 1)]
    public void CraftedRamSizeComesFromItsModel(int model, int level)
    {
        Assert.That(SiegeCrafting.RamLevelFromModel(model), Is.EqualTo((byte)level));
    }

    [Test]
    public void OnlyDeployedEnginesHavePlaceRules()
    {
        Assert.That(SiegeCrafting.IsDeployedEngine(eObjectType.SiegeCatapult), Is.True);
        Assert.That(SiegeCrafting.IsDeployedEngine(eObjectType.SiegeRam), Is.True);
        Assert.That(SiegeCrafting.IsDeployedEngine(eObjectType.SiegeCauldron), Is.False);
        Assert.That(SiegeCrafting.IsDeployedEngine(eObjectType.GenericItem), Is.False);
    }

    [Test]
    public void TokenGivesAPrimaryOnlyToCharactersWithoutOne()
    {
        Assert.That(MithrilStoreItem.PrimaryAfterToken(eCraftingSkill.BasicCrafting, eCraftingSkill.Alchemy), Is.EqualTo(eCraftingSkill.Alchemy));
        Assert.That(MithrilStoreItem.PrimaryAfterToken(eCraftingSkill.NoCrafting, eCraftingSkill.ArmorCrafting), Is.EqualTo(eCraftingSkill.ArmorCrafting));
        Assert.That(MithrilStoreItem.PrimaryAfterToken(eCraftingSkill.Tailoring, eCraftingSkill.Alchemy), Is.EqualTo(eCraftingSkill.Tailoring));
        Assert.That(MithrilStoreItem.PrimaryAfterToken(eCraftingSkill.BasicCrafting, null), Is.EqualTo(eCraftingSkill.BasicCrafting));
    }
}
