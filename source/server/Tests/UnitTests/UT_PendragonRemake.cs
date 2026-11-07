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
    /// <summary>Pendragon Sovereign remake: the style table, Valor, the spell IDs and the Regalia of Camelot.</summary>
    [TestFixture, NonParallelizable]
    public class UT_PendragonRemake
    {
        private GameServer _previous;
        private PetTestLanguageScope _language;
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
            _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var actor in _actors)
                PendragonValor.Reset(actor);
            _actors.Clear();
            GameServer.LoadTestDouble(_previous);
            _language.Dispose();
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
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "source", "tools", "sovereign", "pendragon_remake_styles.json")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "pendragon_remake_styles.json not found above the test folder");
            return JsonSerializer.Deserialize<List<StyleRow>>(File.ReadAllText(Path.Combine(dir.FullName, "source", "tools", "sovereign", "pendragon_remake_styles.json")));
        }

        [Test]
        public void StyleTable_SwordPolearmShieldsAndAnEffectForEach()
        {
            var styles = Styles();
            Assert.That(styles, Has.Count.EqualTo(PendragonRemakeStyles.Count));
            Assert.That(styles.GroupBy(s => s.line).ToDictionary(g => g.Key, g => g.Count()),
                Is.EquivalentTo(new Dictionary<string, int> { ["Slash"] = 20, ["Polearm"] = 20, ["Shields"] = 21 }));
            Assert.That(styles.Select(s => s.index), Is.EqualTo(Enumerable.Range(0, PendragonRemakeStyles.Count)));
            foreach (var style in styles)
                Assert.That(MasterLevelAbilities.For(PendragonRemakeStyles.FirstEffectId + style.index)?.Run, Is.Not.Null, style.name);
        }

        [Test]
        public void StyleTable_ChainsFollowEarlierStyles_AndPridwensBashIsTheAnytimeNineSecondSlam()
        {
            var styles = Styles();
            var byName = styles.ToDictionary(s => s.name);
            foreach (var style in styles.Where(s => s.open == "chain"))
            {
                Assert.That(byName[style.after].line, Is.EqualTo(style.line), style.name);
                Assert.That(byName[style.after].level, Is.LessThan(style.level), style.name);
            }
            var slam = byName["Pridwen's Bash"];
            Assert.That((slam.line, slam.open, slam.index), Is.EqualTo(("Shields", "anytime", 49)));
            Assert.That(PendragonRemakeStyles.Builds(49), Is.EqualTo(1));
        }

        [Test]
        public void Valor_CapsAtFive_SpendsAll_AndAddsTwentyPercentAPoint()
        {
            var me = NewPool();
            PendragonValor.Add(me, 3);
            Assert.That(PendragonValor.Count(me), Is.EqualTo(3));
            PendragonValor.Add(me, 4);
            Assert.That(PendragonValor.Count(me), Is.EqualTo(PendragonValor.BaseMax), "five at most without Code of Chivalry");
            Assert.That(PendragonValor.Bonus(5), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(PendragonValor.Spend(me), Is.EqualTo(5));
            Assert.That(PendragonValor.Count(me), Is.Zero);
            Assert.That(PendragonValor.Spend(me), Is.Zero);
        }

        [Test]
        public void Valor_AHitTakenEarnsOnlyOnceEveryThreeSeconds()
        {
            var me = NewPool();
            PendragonValor.AddFromHit(me);
            PendragonValor.AddFromHit(me);
            Assert.That(PendragonValor.Count(me), Is.EqualTo(1));
        }

        [Test]
        public void DefensiveStyles_EarnValorWhenTheirEffectRuns()
        {
            var me = NewPool();
            var enemy = NewPool();
            var spell = new Spell(new DbSpell { SpellID = PendragonRemakeStyles.FirstEffectId + 15, Type = "StormSovereignAbility", Target = "Enemy", Duration = 10 }, 50);
            var handler = new StormSovereignHandler(me, spell, new SpellLine("Combat Style Effects", "Combat Style Effects", "", false));
            handler.Ability.Run(handler, enemy);   // Mists of Avalon: +2 Valor
            Assert.That(PendragonValor.Count(me), Is.EqualTo(2));
        }

        [Test]
        public void SpellIds_DoNotOverlapAnyOtherAbility()
        {
            foreach (var (id, _) in PendragonRemakeSpells.Build())
            {
                Assert.That(PendragonRemakeSpells.Owns(id) || PendragonRemakeSpells.IsValorIcon(id), Is.True);
                Assert.That(StormRemakeSpells.Owns(id) || TuathaRemakeSpells.Owns(id) || PendragonRules.IsRoyalRank(id) ||
                            StormRemakeStyles.OwnsEffect(id) || TuathaRemakeStyles.OwnsEffect(id), Is.False, id.ToString());
                Assert.That(StormSovereignAbilities.Owns(id), Is.True, "royal rules apply");
                Assert.That(DOL.GS.PacketHandler.PacketLib1110.UsesOwnTooltipIdentity(id), Is.True, "own tooltip, not the borrowed icon's");
            }
            for (int i = 0; i < PendragonRemakeStyles.Count; i++)
            {
                int id = PendragonRemakeStyles.FirstEffectId + i;
                Assert.That(StormSovereignAbilities.Owns(id) && DOL.GS.PacketHandler.PacketLib1110.UsesOwnTooltipIdentity(id), Is.True);
                Assert.That(TuathaRemakeStyles.OwnsEffect(id) || StormRemakeStyles.OwnsEffect(id), Is.False);
            }
            Assert.That(PendragonRemakeStyles.FirstEffectId, Is.GreaterThan(TuathaRemakeStyles.FirstEffectId + TuathaRemakeStyles.Count));
            Assert.That(Enumerable.Range(1, PendragonRemakeSpells.ValorIcons).Select(PendragonRemakeSpells.ValorIconId).Distinct().Count(), Is.EqualTo(7));
            Assert.That(PendragonRemakeSpells.Owns(PendragonRemakeSpells.ValorIconId(1)), Is.False, "the icons are not spellbook buttons");
        }

        [Test]
        public void Spells_OneAbilityKeyAcrossRanks_AndOnlyConsecratedArmamentToggles()
        {
            for (int key = 0; key < PendragonRemakeSpells.Keys; key++)
            {
                var keys = Enumerable.Range(0, StormSovereignAbilities.Levels.Length)
                    .Select(tier => StormSovereignAbilities.AbilityKey(PendragonRemakeSpells.FirstId + tier * 16 + key)).Distinct();
                Assert.That(keys, Is.EqualTo(new[] { 300 + key }));
                Assert.That(StormSovereignAbilities.Toggle(PendragonRemakeSpells.FirstId + key), Is.EqualTo(key == PendragonRemakeSpells.ConsecratedArmament), $"key {key}");
            }
        }

        [Test]
        public void RegaliaOfCamelot_IsItsOwnSet()
        {
            Assert.That(CamelotRegalia.Set.Pieces, Has.Length.EqualTo(7).And.All.StartWith(CamelotRegalia.Prefix));
            Assert.That(CamelotRegalia.Set.Tiers.Select(t => t.Pieces), Is.EqualTo(new[] { 2, 4, 6, 7 }));
            Assert.That(CamelotRegalia.Set.Pieces, Is.Not.EquivalentTo(TaraRegalia.Set.Pieces));
            Assert.That(CamelotRegalia.DefenseValor(null), Is.EqualTo(1));
            foreach (int id in new[] { CamelotRegaliaSpells.CrownOfCamelot, CamelotRegaliaSpells.MercyOfAvalon, CamelotRegaliaSpells.HolyBulwark, CamelotRegaliaSpells.SearingRebuke })
                Assert.That(MasterLevelAbilities.For(id)?.Run, Is.Not.Null, id.ToString());
        }

        [Test]
        public void Weapons_SwordPolearmShieldParryStealthOnly()
        {
            Assert.That(PendragonRules.IsRogSkill(eProperty.Skill_Slashing) && PendragonRules.IsRogSkill(eProperty.Skill_Polearms) &&
                        PendragonRules.IsRogSkill(eProperty.Skill_Shields), Is.True);
            Assert.That(PendragonRules.IsRogSkill(eProperty.Skill_Crushing) || PendragonRules.IsRogSkill(eProperty.Skill_Thrusting) ||
                        PendragonRules.IsRogSkill(eProperty.Skill_Two_Handed), Is.False);
        }
    }
}
