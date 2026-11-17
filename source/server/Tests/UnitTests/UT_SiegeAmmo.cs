using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Crafted siege ammunition and fortified engines (crafting pass 2026-11-17, owner design).</summary>
[TestFixture]
public class UT_SiegeAmmo
{
    private static DbItemTemplate Ammo(string name, int model, int damage, int area, int type) => new()
    {
        Id_nb = name.Replace(' ', '_'), Name = name, Model = model, Object_Type = (int)eObjectType.GenericItem,
        DPS_AF = damage, SPD_ABS = area, Type_Damage = type
    };

    [Test]
    public void BoltsFitBallistasAndStonesFitCatapults()
    {
        Assert.That(SiegeAmmo.KindOf(Ammo("field bolt", 1682, 100, 100, 3)), Is.EqualTo(eSiegeAmmoKind.Bolt));
        Assert.That(SiegeAmmo.KindOf(Ammo("boulder", 1681, 110, 100, 1)), Is.EqualTo(eSiegeAmmoKind.Stone));
        Assert.That(SiegeAmmo.KindOf(Ammo("arctic ice ball", 2621, 120, 100, 11)), Is.EqualTo(eSiegeAmmoKind.Stone));
    }

    [Test]
    public void ItemsWithoutAmmoNumbersAreNotAmmo()
    {
        Assert.That(SiegeAmmo.KindOf(Ammo("Image of Nefastus", 1681, 0, 0, 0)), Is.EqualTo(eSiegeAmmoKind.None));
        DbItemTemplate weapon = Ammo("rock club", 1681, 120, 37, 1);
        weapon.Object_Type = (int)eObjectType.CrushingWeapon;
        Assert.That(SiegeAmmo.KindOf(weapon), Is.EqualTo(eSiegeAmmoKind.None));
        Assert.That(SiegeAmmo.KindOf(null), Is.EqualTo(eSiegeAmmoKind.None));
    }

    [Test]
    public void ShotTakesItsNumbersFromTheItem()
    {
        SiegeShot shot = SiegeAmmo.ShotOf(Ammo("dense stone shot", 1681, 125, 75, 1), eDamageType.Crush);
        Assert.That(shot.Damage, Is.EqualTo(1.25));
        Assert.That(shot.Area, Is.EqualTo(0.75));
        Assert.That(shot.DamageType, Is.EqualTo(eDamageType.Crush));
        Assert.That(shot.IsAmmo, Is.True);
        Assert.That(SiegeAmmo.ShotOf(Ammo("lava stone", 1681, 100, 100, 13), eDamageType.Crush).DamageType, Is.EqualTo(eDamageType.Heat));
    }

    [Test]
    public void PlainShotKeepsTodaysDamage()
    {
        SiegeShot plain = SiegeShot.Plain(eDamageType.Crush);
        Assert.That(SiegeAmmo.Damage(200, plain, fortified: false, targetResistPercent: 26), Is.EqualTo(200));
        Assert.That(plain.IsAmmo, Is.False);
    }

    [Test]
    public void AmmoDamageUsesFortifiedBonusAndTargetResist()
    {
        SiegeShot lethal = new(1.5, 1.0, eDamageType.Body, "lethal bolt");
        Assert.That(SiegeAmmo.Damage(100, lethal, fortified: false, targetResistPercent: 0), Is.EqualTo(150));
        Assert.That(SiegeAmmo.Damage(100, lethal, fortified: true, targetResistPercent: 0), Is.EqualTo(187));
        Assert.That(SiegeAmmo.Damage(100, lethal, fortified: false, targetResistPercent: 20), Is.EqualTo(120));
    }

    [Test]
    public void FortifiedEnginesHaveMoreHealth()
    {
        Assert.That(SiegeAmmo.Health(fortified: false), Is.EqualTo(10000));
        Assert.That(SiegeAmmo.Health(fortified: true), Is.EqualTo(15000));
        Assert.That(SiegeCrafting.IsFortifiedProduct("fortified trebuchet"), Is.True);
        Assert.That(SiegeCrafting.IsFortifiedProduct("field ballista"), Is.False);
    }
}
