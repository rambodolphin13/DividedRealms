using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_CraftingToolBonus
{
    [Test]
    public void RenvekHatsMatchLive()
    {
        Assert.That(CraftingToolBonus.TryGet("lv15893", out var armor), Is.True);
        Assert.That(armor, Is.EqualTo(new CraftingToolBonus.Bonus(eCraftingSkill.ArmorCrafting, 100, 25, 2)));
        Assert.That(CraftingToolBonus.TryGet("LV15899", out var siege), Is.True);
        Assert.That(siege.Skill, Is.EqualTo(eCraftingSkill.SiegeCrafting));
        Assert.That(siege.MinQuality, Is.EqualTo(0));
        Assert.That(CraftingToolBonus.TryGet("some_helm", out _), Is.False);
    }

    [Test]
    public void GainDoublesUpTo100()
    {
        var hat = new CraftingToolBonus.Bonus(eCraftingSkill.Tailoring, 100, 25, 2);
        Assert.That(CraftingToolBonus.ApplyGain(30, hat), Is.EqualTo(60));
        Assert.That(CraftingToolBonus.ApplyGain(65, hat), Is.EqualTo(100));
        Assert.That(CraftingToolBonus.ApplyGain(0, hat), Is.EqualTo(0));
        Assert.That(CraftingToolBonus.ApplyGain(30, default), Is.EqualTo(30));
    }

    [Test]
    public void SpeedAndQuality()
    {
        var hat = new CraftingToolBonus.Bonus(eCraftingSkill.Tailoring, 100, 25, 2);
        Assert.That(CraftingToolBonus.ApplySpeed(100, hat), Is.EqualTo(80));
        Assert.That(CraftingToolBonus.ApplySpeed(1, hat), Is.EqualTo(1));
        Assert.That(CraftingToolBonus.ApplyQuality(94, hat), Is.EqualTo(98));
        Assert.That(CraftingToolBonus.ApplyQuality(99, hat), Is.EqualTo(99));
        Assert.That(CraftingToolBonus.ApplyQuality(100, hat), Is.EqualTo(100));
        Assert.That(CraftingToolBonus.ApplyQuality(94, hat with { MinQuality = 0 }), Is.EqualTo(94));
    }
}
