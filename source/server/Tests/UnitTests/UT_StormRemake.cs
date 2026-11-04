using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Storm Sovereign remake: the style table, the remake spell IDs and Tempest charges.</summary>
    [TestFixture, NonParallelizable]
    public class UT_StormRemake
    {
        private GameServer _previous;
        private readonly List<GameLiving> _actors = new();

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
        }

        private sealed class Pool : GameNPC
        {
            public override byte Level { get; set; } = 50;
            public override int MaxHealth => 1000;
            public override bool IsAlive => true;
            public override int GetModified(eProperty p) => 0;
        }

        private sealed record StyleRow(int index, string line, string name, int level, string open, string after, double growth, string description);

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var actor in _actors)
                StormTempest.Reset(actor);
            _actors.Clear();
            GameServer.LoadTestDouble(_previous);
        }

        private Pool NewPool()
        {
            var pool = new Pool();
            _actors.Add(pool);
            return pool;
        }

        private static List<StyleRow> Styles()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "source", "tools", "sovereign", "storm_remake_styles.json")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "storm_remake_styles.json not found above the test folder");
            string json = File.ReadAllText(Path.Combine(dir.FullName, "source", "tools", "sovereign", "storm_remake_styles.json"));
            return JsonSerializer.Deserialize<List<StyleRow>>(json);
        }

        [Test]
        public void StyleTable_HasTwentyStylesPerLineAndAnEffectForEach()
        {
            var styles = Styles();
            Assert.That(styles, Has.Count.EqualTo(StormRemakeStyles.Count));
            Assert.That(styles.GroupBy(s => s.line).ToDictionary(g => g.Key, g => g.Count()),
                Is.EquivalentTo(new Dictionary<string, int> { ["Hammer"] = 20, ["Axe"] = 20, ["Shields"] = 20 }));
            Assert.That(styles.Select(s => s.index), Is.EqualTo(Enumerable.Range(0, StormRemakeStyles.Count)));
            foreach (var style in styles)
            {
                MLAbility ability = MasterLevelAbilities.For(StormRemakeStyles.FirstEffectId + style.index);
                Assert.That(ability?.Run, Is.Not.Null, style.name);
            }
        }

        [Test]
        public void StyleTable_EveryChainFollowsAnEarlierStyleOfItsOwnLine()
        {
            var styles = Styles();
            var byName = styles.ToDictionary(s => s.name);
            foreach (var style in styles.Where(s => s.open == "chain"))
            {
                Assert.That(byName.TryGetValue(style.after, out var before), Is.True, style.name);
                Assert.That(before.line, Is.EqualTo(style.line), style.name);
                Assert.That(before.level, Is.LessThan(style.level), style.name);
            }
            Assert.That(styles.Where(s => s.open != "chain").Select(s => s.after), Is.All.Null);
        }

        [Test]
        public void StyleTable_EachLineHasOffensiveAndDefensiveChainsAndASignatureFinisher()
        {
            foreach (var line in Styles().GroupBy(s => s.line))
            {
                Assert.That(line.Count(s => s.open == "anytime"), Is.GreaterThanOrEqualTo(3), line.Key);
                Assert.That(line.Count(s => s.open is "block" or "parry"), Is.GreaterThanOrEqualTo(2), line.Key);
                Assert.That(line.Count(s => s.open == "stealth"), Is.EqualTo(1), line.Key);
                Assert.That(line.Max(s => s.level), Is.EqualTo(48), line.Key);
            }
        }

        [Test]
        public void RemakeSpellIds_DoNotOverlapAnyOtherAbility()
        {
            var ids = StormRemakeSpells.Build().Select(entry => entry.Id).ToList();
            Assert.That(ids, Has.Count.EqualTo(StormSovereignAbilities.Levels.Length * StormRemakeSpells.Keys));
            foreach (int id in ids)
            {
                Assert.That(StormRemakeSpells.Owns(id), Is.True);
                Assert.That(PendragonRules.IsRoyalRank(id), Is.False, $"{id} is a royal rank");
                Assert.That(StormRemakeStyles.OwnsEffect(id), Is.False);
                Assert.That(id, Is.Not.InRange(63000, 63144), "Master Level abilities");
                Assert.That(StormSovereignAbilities.Owns(id), Is.True, "royal damage, Storm Crown and siphon rules apply");
            }
            for (int i = 0; i < StormRemakeStyles.Count; i++)
                Assert.That(StormSovereignAbilities.Owns(StormRemakeStyles.FirstEffectId + i), Is.True);
            Assert.That(StormRemakeSpells.Owns(StormRemakeSpells.FirstId + StormRemakeSpells.Keys), Is.False, "unused key slot");
        }

        [Test]
        public void RemakeSpells_OneAbilityKeyAcrossEveryRank()
        {
            for (int key = 0; key < StormRemakeSpells.Keys; key++)
            {
                var keys = Enumerable.Range(0, StormSovereignAbilities.Levels.Length)
                    .Select(tier => StormSovereignAbilities.AbilityKey(StormRemakeSpells.FirstId + tier * 16 + key)).Distinct().ToList();
                Assert.That(keys, Is.EqualTo(new[] { 100 + key }));
            }
            Assert.That(StormSovereignAbilities.AbilityKey(62400 + 3 * 32 + 7), Is.EqualTo(7), "old royal keys are unchanged");
        }

        [Test]
        public void RemakeSpells_OnlyRunicArmamentIsAToggle()
        {
            for (int key = 0; key < StormRemakeSpells.Keys; key++)
                Assert.That(StormSovereignAbilities.Toggle(StormRemakeSpells.FirstId + key), Is.EqualTo(key == StormRemakeSpells.RunicArmament), $"key {key}");
        }

        [Test]
        public void Tempest_BuildsToFiveAndFinishersSpendItAll()
        {
            var owner = NewPool();
            StormTempest.Add(owner, 3);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(3));
            StormTempest.Add(owner, 4);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(5), "capped at the five orbs");
            Assert.That(StormTempest.Spend(owner), Is.EqualTo(5));
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(0));
            Assert.That(StormTempest.Spend(owner), Is.EqualTo(0), "nothing left to spend");
        }

        [Test]
        public void Tempest_EachChargeAddsTwentyPercent()
        {
            Assert.That(StormTempest.Bonus(0), Is.EqualTo(1.0));
            Assert.That(StormTempest.Bonus(3), Is.EqualTo(1.6).Within(1e-9));
            Assert.That(StormTempest.Bonus(5), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(StormTempest.Bonus(9), Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void Tempest_SetFillsAndClearEmpties()
        {
            var owner = NewPool();
            StormTempest.Set(owner, StormTempest.MaxCharges);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(5));
            StormTempest.Clear(owner);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(0));
        }
    }
}
