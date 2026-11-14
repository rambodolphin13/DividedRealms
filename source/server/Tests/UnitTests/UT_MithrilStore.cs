using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_MithrilStore
{
    private static GameInventoryItem Item(string id, int model, eInventorySlot slot, int objectType = 35)
    {
        return new GameInventoryItem(new DbItemTemplate { Id_nb = id, Name = id, Model = model, Item_Type = (int) slot, Object_Type = objectType, MaxCount = 1, PackSize = 1 });
    }

    [Test]
    public void MoneyRowShowsTheBalanceAndFoldsCoinMithrilIntoPlatinum()
    {
        Assert.That(MithrilBalance.DisplayValue(0), Is.EqualTo(0));
        Assert.That(MithrilBalance.DisplayValue(1385), Is.EqualTo(1385));
        Assert.That(MithrilBalance.DisplayValue(10_000_000), Is.EqualTo(ushort.MaxValue));
        Assert.That(MithrilBalance.DisplayValue(-5), Is.EqualTo(0));
        Assert.That(MithrilBalance.DisplayPlatinum(2, 15), Is.EqualTo(2015));
        Assert.That(MithrilBalance.DisplayPlatinum(70, 0), Is.EqualTo(ushort.MaxValue));
    }

    [Test]
    public void QuestMithrilGrowsWithQuestLevel()
    {
        int baseAmount = DOL.GS.ServerProperties.Properties.MITHRIL_QUEST;
        Assert.That(MithrilBalance.ForQuest(5), Is.EqualTo(baseAmount <= 0 ? 0 : baseAmount));
        Assert.That(MithrilBalance.ForQuest(48), Is.EqualTo(baseAmount <= 0 ? 0 : baseAmount + 4));
    }

    [Test]
    public void PatternRecordsThenAppliesToTheSameSlotOnly()
    {
        var pattern = Item("mithril_pattern_blank", 3000, eInventorySlot.FirstBackpack, 0);
        var fancyHelm = Item("fancy_helm", 1234, eInventorySlot.HeadArmor);
        var plainHelm = Item("plain_helm", 62, eInventorySlot.HeadArmor);
        var boots = Item("boots", 84, eInventorySlot.FeetArmor);

        Assert.That(Transmog.Use(pattern, fancyHelm, out _), Is.EqualTo(Transmog.Result.Recorded));
        Assert.That(pattern.LookModel, Is.EqualTo(1234));
        Assert.That(Transmog.Use(pattern, boots, out _), Is.EqualTo(Transmog.Result.Refused));
        Assert.That(boots.Model, Is.EqualTo(84));
        Assert.That(Transmog.Use(pattern, plainHelm, out _), Is.EqualTo(Transmog.Result.Applied));
        Assert.That(plainHelm.Model, Is.EqualTo(1234), "the client is sent the look model");
        Assert.That(plainHelm.Template.Model, Is.EqualTo(62), "the template is untouched");
    }

    [Test]
    public void WeaponsMatchByHandAndEraserRestores()
    {
        var pattern = Item("mithril_pattern_blank", 3000, eInventorySlot.FirstBackpack, 0);
        var sword = Item("sword", 4, eInventorySlot.RightHandWeapon, 3);
        var mace = Item("mace", 13, eInventorySlot.RightHandWeapon, 2);
        var bow = Item("bow", 132, eInventorySlot.DistanceWeapon, 9);
        var eraser = Item("mithril_pattern_eraser", 3001, eInventorySlot.FirstBackpack, 0);

        Transmog.Use(pattern, sword, out _);
        Assert.That(Transmog.Use(pattern, bow, out _), Is.EqualTo(Transmog.Result.Refused));
        Assert.That(Transmog.Use(pattern, mace, out _), Is.EqualTo(Transmog.Result.Applied), "owner rule: same hand, any weapon type");
        Assert.That(mace.Model, Is.EqualTo(4));
        Assert.That(Transmog.Use(eraser, mace, out _), Is.EqualTo(Transmog.Result.Erased));
        Assert.That(mace.Model, Is.EqualTo(13));
    }

    [Test]
    public void MasksOnlyGoOnHelmsAndJewelryCannotBePatterned()
    {
        var mask = Item("mithril_mask_plague", 1650, eInventorySlot.FirstBackpack, 0);
        var helm = Item("helm", 62, eInventorySlot.HeadArmor);
        var gloves = Item("gloves", 85, eInventorySlot.HandsArmor);
        var ring = Item("ring", 103, eInventorySlot.RightRing, 41);

        Assert.That(Transmog.Use(mask, gloves, out _), Is.EqualTo(Transmog.Result.Refused));
        Assert.That(Transmog.Use(mask, helm, out _), Is.EqualTo(Transmog.Result.Applied));
        Assert.That(helm.Model, Is.EqualTo(1650));
        Assert.That(Transmog.Use(mask, ring, out _), Is.EqualTo(Transmog.Result.Refused));
        Assert.That(Transmog.Use(Item("dagger", 21, eInventorySlot.RightHandWeapon), helm, out _), Is.EqualTo(Transmog.Result.NotATool));
    }

    [Test]
    public void MithrilRespecCopiesWorkLikeTheStones()
    {
        Assert.That(MithrilBalance.BaseItemId("mithril_respec_full"), Is.EqualTo("respec_full"));
        Assert.That(MithrilBalance.BaseItemId("mithril_respec_cl"), Is.EqualTo("respec_cl"));
        Assert.That(MithrilBalance.BaseItemId("respec_single"), Is.EqualTo("respec_single"));
        Assert.That(MithrilBalance.BaseItemId("mithril_xp_potion"), Is.EqualTo("mithril_xp_potion"));
    }

    [Test]
    public void BodyModelKeepsSizeAndFaceBits()
    {
        int creation = (2 << 11) | 32;
        Assert.That(MithrilCharacterChanges.WithBody(creation, 41), Is.EqualTo((2 << 11) | 41));
    }

    [Test]
    public void OmniDyesAreRecognisedByTheirId()
    {
        Assert.That(GamePlayerInventory.IsOmniDye(Item("mithril_omni_dye_fuschia", 229, eInventorySlot.FirstBackpack, 0)), Is.True);
        Assert.That(GamePlayerInventory.IsOmniDye(Item("cloth_dye_red", 229, eInventorySlot.FirstBackpack, 0)), Is.False);
    }
}
