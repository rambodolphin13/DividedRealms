using System.Linq;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public class UT_DiscordFixes1117
{
    [Test]
    public void RollNowAtLeastOne_AlwaysDropsOneEvenWhenEveryChanceIsTiny()
    {
        for (int kill = 0; kill < 500; kill++)
        {
            var loot = new LootList(1);
            loot.AddRandom(1, new DbItemTemplate { Id_nb = "a" });
            loot.AddRandom(1, new DbItemTemplate { Id_nb = "b" });
            loot.AddRandom(1, new DbItemTemplate { Id_nb = "c" });
            loot.RollNowAtLeastOne();
            Assert.That(loot.GetDrops().Length, Is.EqualTo(1));
        }
    }

    [Test]
    public void RollNowAtLeastOne_KeepsDropCountAndLeavesNoRandomEntries()
    {
        var loot = new LootList(2);
        loot.AddRandom(100, new DbItemTemplate { Id_nb = "a" });
        loot.AddRandom(100, new DbItemTemplate { Id_nb = "b" });
        loot.AddRandom(100, new DbItemTemplate { Id_nb = "c" });
        loot.RollNowAtLeastOne();
        DbItemTemplate[] first = loot.GetLoot();
        Assert.That(first.Length, Is.EqualTo(2));
        Assert.That(first.Select(i => i.Id_nb).Distinct().Count(), Is.EqualTo(2));
        Assert.That(loot.GetLoot().Select(i => i.Id_nb), Is.EqualTo(first.Select(i => i.Id_nb)), "rolled once, now fixed");
    }

    [Test]
    public void RollNowAtLeastOne_EmptyListStaysEmpty()
    {
        var loot = new LootList(1);
        loot.RollNowAtLeastOne();
        Assert.That(loot.GetDrops(), Is.Empty);
    }

    [Test]
    public void DarknessFallsPrincesAlwaysDropNamedLoot()
    {
        foreach (string name in new[] { "Prince Abdin", "prince asmoien", "Prince Ba'alorien", "Princess Nahemah" })
            Assert.That(LootGeneratorTemplate.AlwaysDropsNamedLoot.Contains(name), name);
        Assert.That(LootGeneratorTemplate.AlwaysDropsNamedLoot.Contains("Prince Oar"), Is.False);
    }

    [TestCase(0, 1000, 0)]
    [TestCase(1000, 1000, SummonedMerchantCamp.ReuseMs)]
    [TestCase(1000, 1000 + SummonedMerchantCamp.ReuseMs - 1, 1)]
    [TestCase(1000, 1000 + SummonedMerchantCamp.ReuseMs, 0)]
    public void MerchantGemReuseWait(long lastUse, long now, long expected)
    {
        Assert.That(SummonedMerchantCamp.ReuseWaitMs(lastUse, now), Is.EqualTo(expected));
    }

    [Test]
    public void AllTradesTokenSkipsBountycraftAndBasicCrafting()
    {
        Assert.That(MithrilStoreItem.IsLegendaryTrade(eCraftingSkill.BountyCrafting), Is.False);
        Assert.That(MithrilStoreItem.IsLegendaryTrade(eCraftingSkill.BasicCrafting), Is.False);
        Assert.That(MithrilStoreItem.IsLegendaryTrade(eCraftingSkill.NoCrafting), Is.False);
        Assert.That(MithrilStoreItem.IsLegendaryTrade(eCraftingSkill.WeaponCrafting), Is.True);
        Assert.That(MithrilStoreItem.IsLegendaryTrade(eCraftingSkill.SpellCrafting), Is.True);
    }

    [Test]
    public void MerchantGemCampLastsTwoMinutes()
    {
        Assert.That(SummonedMerchantCamp.DurationMs, Is.EqualTo(120_000));
    }
}
