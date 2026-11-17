using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_DarknessFallsWeaponDrops
{
    private class Server : GameServer
    {
        protected override IObjectDatabase DataBaseImpl => DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    }

    private GameServer _previousServer;

    [OneTimeSetUp]
    public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

    [SetUp]
    public void UseTestServer()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((Server) RuntimeHelpers.GetUninitializedObject(typeof(Server)));
    }

    [TearDown]
    public void RestoreServer() => GameServer.LoadTestDouble(_previousServer);

    private static GameNPC Mob(string name)
    {
        var mob = (GameNPC) RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mob, new ArrayList(1));
        mob.Name = name;
        return mob;
    }

    [Test]
    public void DropChanceUsesTheNamedMultiplier()
    {
        double baseChance = Properties.LOOTGENERATOR_DF_WEAPON_BASE_CHANCE;
        double multiplier = Properties.LOOTGENERATOR_DF_WEAPON_NAMED_MULTIPLIER;
        try
        {
            Properties.LOOTGENERATOR_DF_WEAPON_BASE_CHANCE = 1.0;
            Properties.LOOTGENERATOR_DF_WEAPON_NAMED_MULTIPLIER = 5.0;

            Assert.That(LootGeneratorDarknessFallsWeapons.DropChance(Mob("demonic minion")), Is.EqualTo(1.0));
            Assert.That(LootGeneratorDarknessFallsWeapons.DropChance(Mob("Baron Kellan")), Is.EqualTo(5.0));

            Properties.LOOTGENERATOR_DF_WEAPON_BASE_CHANCE = 50.0;
            Assert.That(LootGeneratorDarknessFallsWeapons.DropChance(Mob("Baron Kellan")), Is.EqualTo(100.0));
        }
        finally
        {
            Properties.LOOTGENERATOR_DF_WEAPON_BASE_CHANCE = baseChance;
            Properties.LOOTGENERATOR_DF_WEAPON_NAMED_MULTIPLIER = multiplier;
        }
    }

    [Test]
    public void PickPrefersWeaponsTheClassCanUse()
    {
        var armsman = new DbItemTemplate { Id_nb = "arms", AllowedClasses = "2" };
        var reaver = new DbItemTemplate { Id_nb = "reaver", AllowedClasses = "19" };
        var stock = new[] { armsman, reaver };

        for (int i = 0; i < 50; i++)
            Assert.That(LootGeneratorDarknessFallsWeapons.Pick(stock, 2), Is.SameAs(armsman));
    }

    [Test]
    public void PickFallsBackToTheWholeListWhenNoWeaponFitsTheClass()
    {
        var armsman = new DbItemTemplate { Id_nb = "arms", AllowedClasses = "2" };
        var stock = new[] { armsman };

        Assert.That(LootGeneratorDarknessFallsWeapons.Pick(stock, 5), Is.SameAs(armsman));
        Assert.That(LootGeneratorDarknessFallsWeapons.Pick(stock, 0), Is.SameAs(armsman));
        Assert.That(LootGeneratorDarknessFallsWeapons.Pick(new DbItemTemplate[0], 2), Is.Null);
    }
}
