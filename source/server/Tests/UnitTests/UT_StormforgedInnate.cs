using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.PropertyCalc;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_StormforgedInnate
{
    private class Player : GamePlayer
    {
        private Player() : base(null, null) { }
        public ICharacterClass Class;
        public override ICharacterClass CharacterClass => Class;
        public override byte Level { get; set; }
        public override int TotalConstitutionLostAtDeath { get; set; }
        public override int GetBaseStat(eStat stat) => 60;
        public override int CalculateMaxHealth(int level, int constitution) => 1000 + constitution * 2;
        public override int CalculateMaxMana(int level, int stat) => 100 + stat;
        public override bool HasAbility(string key) => false;
        public override int GetModified(eProperty property) => property >= eProperty.Stat_First && property <= eProperty.Stat_Last
            ? new StatCalculator().CalcValue(this, property) : 0;
    }
    private static Player NewPlayer(bool stormforged, byte level)
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        player.Class = stormforged ? new ClassStormforged() : new ClassThane();
        player.Level = level;
        foreach (string property in new[] { "ItemBonus", "AbilityBonus", "BaseBuffBonusCategory", "SpecBuffBonusCategory", "OtherBonus", "DebuffCategory", "SpecDebuffCategory", "BuffBonusMultCategory1" })
        {
            var field = typeof(GameLiving).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(player, Activator.CreateInstance(field.FieldType));
        }
        return player;
    }

    [Test]
    public void HumanPlayersUsePrivateTurretAllowanceOnlyForTheirOwnClassAndBomb()
    {
        var storm = NewPlayer(true, 50);
        var stock = NewPlayer(false, 50);
        var bomb = new Spell(new DOL.Database.DbSpell { Type = "SummonAnimistFnF", Target = "Self", LifeDrainReturn = 60171002 }, 10);
        Assert.That(StormforgedTurretRules.IsStormOwner(storm), Is.True);
        Assert.That(StormforgedTurretRules.IsStormSummon(storm, bomb), Is.True);
        Assert.That(StormforgedTurretRules.IsStormOwner(stock), Is.False);
        Assert.That(StormforgedTurretRules.IsStormSummon(stock, bomb), Is.False);
        Assert.That(StormforgedInnate.Bonus(null, eProperty.Strength), Is.Zero);
    }

    [TestCase((byte)1)] [TestCase((byte)5)] [TestCase((byte)20)] [TestCase((byte)40)] [TestCase((byte)50)]
    public void AllEightStatsScaleWithoutEquipmentOrBuffs(byte level)
    {
        var storm = NewPlayer(true, level);
        var thane = NewPlayer(false, level);
        for (int property = (int)eProperty.Stat_First; property <= (int)eProperty.Stat_Last; property++)
        {
            Assert.That(storm.GetModified((eProperty)property), Is.EqualTo(60 + level * 2));
            Assert.That(thane.GetModified((eProperty)property), Is.EqualTo(60));
        }
    }

    [Test]
    public void IntrinsicStatsStackOutsideWardAndItemCapsAndUpdateWithoutRecasting()
    {
        var player = NewPlayer(true, 40);
        player.BaseBuffBonusCategory[eProperty.Strength] = 10000;
        player.SpecBuffBonusCategory[eProperty.Strength] = 10000;
        player.ItemBonus[eProperty.Strength] = 10000;
        Assert.That(player.GetModified(eProperty.Strength), Is.EqualTo(60 + 80 + 50 + 75 + 60));
        player.Level = 50;
        Assert.That(player.GetModified(eProperty.Strength), Is.EqualTo(60 + 100 + 62 + 93 + 75));
        player.TotalConstitutionLostAtDeath = 5;
        Assert.That(player.GetModified(eProperty.Constitution), Is.EqualTo(155));
    }

    [TestCase((byte)1)] [TestCase((byte)25)] [TestCase((byte)50)]
    public void HealthAndPowerIncludeBothStatBenefitsAndFlatInnateBonuses(byte level)
    {
        var player = NewPlayer(true, level);
        Assert.That(new MaxHealthCalculator().CalcValue(player, eProperty.MaxHealth),
            Is.EqualTo(1000 + (60 + level * 2) * 2 + level * 10));
        Assert.That(new MaxManaCalculator().CalcValue(player, eProperty.MaxMana),
            Is.EqualTo(100 + 60 + level * 2 + level * 2));
        var stock = NewPlayer(false, level);
        Assert.That(new MaxHealthCalculator().CalcValue(stock, eProperty.MaxHealth), Is.EqualTo(1120));
        Assert.That(new MaxManaCalculator().CalcValue(stock, eProperty.MaxMana), Is.EqualTo(160));
    }
}
