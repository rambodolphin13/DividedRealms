using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Owner 2026-11-15: random item drops follow the XP multiplier (RandomItems.XpRate, off by default), and solo drops
    // take their quality from the real con as group drops do.
    [TestFixture, NonParallelizable]
    public class UT_LootXpRate
    {
        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ICharacterClass CharacterClass => new ClassEnchanter();
        }

        private sealed class Mob : GameNPC
        {
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        private sealed class Scripted : ROGMobGenerator
        {
            public readonly Dictionary<GameLiving, double> Rates = new();
            public readonly List<double> Chances = [];
            public readonly List<bool> AdjustedQuality = [];
            public readonly List<int> Cons = [];
            public bool Otherwise;

            protected override bool RollDropChance(double chance)
            {
                Chances.Add(chance);
                return Otherwise;
            }

            protected override double XpRateOf(GameLiving roller) => Rates.TryGetValue(roller, out double rate) ? rate : 1.0;

            public double RealXpRateOf(GameLiving roller) => base.XpRateOf(roller);

            protected override DbItemTemplate GenerateItemTemplate(GameLiving owner, eCharacterClass characterClass, byte level, int con, bool adjustQuality = true)
            {
                AdjustedQuality.Add(adjustQuality);
                Cons.Add(con);
                return new DbItemTemplate { Id_nb = Guid.NewGuid().ToString(), Name = "made for " + owner.Name, Realm = 0, MaxCount = 1, PackSize = 1 };
            }
        }

        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Db;
        }

        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, UT_LootPhase5.EmptyDb>();
        private static long _nextId = 940000;
        private GameServer _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            LootRulesFile.TestOverride = LootRules.Legacy;
        }

        [TearDown]
        public void TearDown()
        {
            LootRulesFile.TestOverride = null;
            GameServer.LoadTestDouble(_previous);
        }

        private static void Field(Type type, object target, string name, object value)
        {
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static T Inert<T>() where T : GameLiving
        {
            T living = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            Field(typeof(GameLiving), living, "<TempProperties>k__BackingField", new PropertyCollection());
            if (living is GameNPC)
                Field(typeof(GameNPC), living, "m_brains", new ArrayList());
            return living;
        }

        private static Bot NewBot(string name, byte level = 10)
        {
            Bot bot = Inert<Bot>();
            bot.Level = level;
            bot.Realm = eRealm.Hibernia;
            bot.Name = name;
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            return bot;
        }

        private static Mob NewMob(byte level = 10)
        {
            Mob mob = Inert<Mob>();
            mob.Level = level;
            mob.Name = "large frog";
            return mob;
        }

        private static void GroupOf(params Bot[] bots)
        {
            Group group = new(bots[0]);
            var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group);
            foreach (Bot bot in bots)
            {
                bot.Group = group;
                members.Add(bot);
            }
        }

        private static LootRules Rules(bool enabled = true, double exponent = 0.4, double min = 0.5, double max = 3.0, double chanceMultiplier = 1.0) =>
            new()
            {
                RandomItems = LootRules.Legacy.RandomItems with
                {
                    ChanceMultiplier = chanceMultiplier,
                    XpRate = new RandomItemXpRateRules { Enabled = enabled, Exponent = exponent, MinMultiplier = min, MaxMultiplier = max },
                },
            };

        [Test]
        public void TheFactorFollowsTheRateOnACurveWithinItsLimits()
        {
            var rules = new RandomItemXpRateRules { Enabled = true, Exponent = 0.4, MinMultiplier = 0.5, MaxMultiplier = 3 };
            Assert.That(rules.Factor(1), Is.EqualTo(1.0));
            Assert.That(rules.Factor(10), Is.EqualTo(Math.Pow(10, 0.4)).Within(1e-9), "10x XP: about 2.5x drops");
            Assert.That(rules.Factor(2), Is.EqualTo(Math.Pow(2, 0.4)).Within(1e-9));
            Assert.That(rules.Factor(1000), Is.EqualTo(3.0), "capped");
            Assert.That(rules.Factor(0.01), Is.EqualTo(0.5), "a low rate lowers drops, down to the floor");
            Assert.That(rules.Factor(0), Is.EqualTo(1.0), "a zero or bad rate changes nothing");
            Assert.That(rules.Factor(double.NaN), Is.EqualTo(1.0));
            Assert.That(RandomItemXpRateRules.Off.Factor(10), Is.EqualTo(1.0), "switched off");
            Assert.That(new RandomItemXpRateRules().Factor(10), Is.EqualTo(Math.Pow(10, 0.4)).Within(1e-9), "on by default (owner 2026-11-15)");
            Assert.That(new RandomItemXpRateRules { Enabled = true, Exponent = 1, MaxMultiplier = 20 }.Factor(10), Is.EqualTo(10).Within(1e-9), "exponent 1 follows the rate exactly");
        }

        [Test]
        public void TheRulesFileReadsAndChecksTheXpRateBlock()
        {
            LootRules rules = LootRules.Parse("{ \"RandomItems\": { \"XpRate\": { \"Enabled\": true, \"Exponent\": 0.5, \"MaxMultiplier\": 4 } } }");
            Assert.That(rules.RandomItems.XpRate.Enabled, Is.True);
            Assert.That(rules.RandomItems.XpRate.Exponent, Is.EqualTo(0.5));
            Assert.That(rules.RandomItems.XpRate.MaxMultiplier, Is.EqualTo(4));
            Assert.That(rules.RandomItems.XpRate.MinMultiplier, Is.EqualTo(0.5), "unwritten values keep their defaults");
            Assert.Throws<InvalidDataException>(() => LootRules.Parse("{ \"RandomItems\": { \"XpRate\": { \"Exponent\": 5 } } }"));
            Assert.Throws<InvalidDataException>(() => LootRules.Parse("{ \"RandomItems\": { \"XpRate\": { \"MaxMultiplier\": 0.5 } } }"));
            Assert.Throws<InvalidDataException>(() => LootRules.Parse("{ \"RandomItems\": { \"XpRate\": null } }"));
        }

        [Test]
        public void TheExampleRulesFileParses()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            string path = null;
            for (DirectoryInfo d = new(dir); d != null; d = d.Parent)
            {
                string candidate = Path.Combine(d.FullName, "docs", "loot-rules.example.json");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
            }
            if (path == null)
                Assert.Ignore("docs/loot-rules.example.json not found above " + dir);
            Assert.DoesNotThrow(() => LootRules.Parse(File.ReadAllText(path)));
        }

        [Test]
        public void ASoloRollIsScaledByTheKillersRate()
        {
            LootRulesFile.TestOverride = Rules();
            Bot bot = NewBot("solo");
            var generator = new Scripted();
            generator.Rates[bot] = 10;
            generator.GenerateLoot(NewMob(), bot);
            Assert.That(generator.Chances.Single(), Is.EqualTo(24 * Math.Pow(10, 0.4)).Within(1e-9), "24% even-con solo, x2.51");
        }

        [Test]
        public void WithTheLinkOffTheChanceIsUnchanged()
        {
            LootRulesFile.TestOverride = Rules(enabled: false);
            Bot bot = NewBot("solo");
            var generator = new Scripted();
            generator.Rates[bot] = 10;
            generator.GenerateLoot(NewMob(), bot);
            Assert.That(generator.Chances.Single(), Is.EqualTo(24.0));
        }

        [Test]
        public void TheScaledChanceNeverPassesOneHundred()
        {
            LootRulesFile.TestOverride = Rules(exponent: 1, max: 20);
            Bot bot = NewBot("solo");
            var generator = new Scripted();
            generator.Rates[bot] = 10;
            generator.GenerateLoot(NewMob(), bot);
            Assert.That(generator.Chances.Single(), Is.EqualTo(100.0));
        }

        [Test]
        public void EachGroupMemberRollsWithTheirOwnRate()
        {
            LootRulesFile.TestOverride = Rules(exponent: 1, max: 20);
            Bot first = NewBot("first"), second = NewBot("second"), third = NewBot("third");
            GroupOf(first, second, third);
            var generator = new Scripted();
            generator.Rates[first] = 1;
            generator.Rates[second] = 2;
            generator.Rates[third] = 3;
            generator.GenerateLoot(NewMob(), first);
            // a level 10 group has no level penalty: 14% even con, times each member's own rate
            Assert.That(generator.Chances, Is.EquivalentTo(new[] { 14.0, 28.0, 42.0 }));
        }

        [Test]
        public void ASoloDropTakesItsQualityFromTheRealCon()
        {
            LootRulesFile.TestOverride = Rules(enabled: false);
            Bot bot = NewBot("solo");
            var generator = new Scripted { Otherwise = true };
            generator.GenerateLoot(NewMob(), bot);
            Assert.That(generator.AdjustedQuality, Is.EqualTo(new[] { true }));
        }

        [Test]
        public void AGamebotUsesTheBotXpRateAndACompanionTheServerRate()
        {
            double botRate = DOL.GS.ServerProperties.Properties.BOT_XP_RATE, xpRate = DOL.GS.ServerProperties.Properties.XP_RATE;
            string botByLevel = DOL.GS.ServerProperties.Properties.BOT_XP_RATE_BY_LEVEL;
            try
            {
                DOL.GS.ServerProperties.Properties.BOT_XP_RATE = 7;
                DOL.GS.ServerProperties.Properties.XP_RATE = 3;
                DOL.GS.ServerProperties.Properties.BOT_XP_RATE_BY_LEVEL = "";
                Bot gamebot = NewBot("gamebot");
                Bot companion = NewBot("companion");
                typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(companion, false);
                var generator = new Scripted();
                Assert.That(generator.RealXpRateOf(gamebot), Is.EqualTo(7));
                Assert.That(generator.RealXpRateOf(companion), Is.EqualTo(3));
                Assert.That(generator.RealXpRateOf(null), Is.EqualTo(1));
            }
            finally
            {
                DOL.GS.ServerProperties.Properties.BOT_XP_RATE = botRate;
                DOL.GS.ServerProperties.Properties.XP_RATE = xpRate;
                DOL.GS.ServerProperties.Properties.BOT_XP_RATE_BY_LEVEL = botByLevel;
            }
        }

        #region Second pass (owner 2026-11-15): named quality, ToA by quality, coin by XP rate, saved bad luck

        [Test]
        public void ANamedMobRollsQualityAsIfItConnedHigher()
        {
            LootRulesFile.TestOverride = new LootRules { RandomItems = LootRules.Legacy.RandomItems with { NamedQualityConBonus = 2 } };
            Bot bot = NewBot("solo");
            Mob named = NewMob();
            named.Name = "Grumoz";
            var generator = new Scripted { Otherwise = true };
            generator.GenerateLoot(named, bot);
            generator.GenerateLoot(NewMob(), bot);
            Assert.That(generator.Cons, Is.EqualTo(new[] { 2, 0 }), "an even-con named mob makes items as if it were 2 steps higher");
            Assert.That(generator.Chances, Is.EqualTo(new[] { 24.0, 24.0 }), "the drop chance keeps the real con");
        }

        [Test]
        public void ToaLineChanceRisesWithQuality()
        {
            Assert.That(GeneratedUniqueItem.ToaLineChance(15, 95, 95, 3), Is.EqualTo(15));
            Assert.That(GeneratedUniqueItem.ToaLineChance(15, 99, 95, 3), Is.EqualTo(27));
            Assert.That(GeneratedUniqueItem.ToaLineChance(15, 99, 95, 0), Is.EqualTo(15), "0 per point is the old flat chance");
            Assert.That(GeneratedUniqueItem.ToaLineChance(0, 99, 95, 3), Is.EqualTo(0), "ToA off stays off");
            Assert.That(GeneratedUniqueItem.ToaLineChance(15, 90, 95, 3), Is.EqualTo(15), "below the starting quality adds nothing");
            Assert.That(GeneratedUniqueItem.ToaLineChance(90, 99, 95, 25), Is.EqualTo(100));
        }

        [Test]
        public void CoinFollowsTheKillersXpRate()
        {
            double botRate = DOL.GS.ServerProperties.Properties.BOT_XP_RATE, moneyDrop = DOL.GS.ServerProperties.Properties.MONEY_DROP;
            string botByLevel = DOL.GS.ServerProperties.Properties.BOT_XP_RATE_BY_LEVEL;
            try
            {
                DOL.GS.ServerProperties.Properties.BOT_XP_RATE = 10;
                DOL.GS.ServerProperties.Properties.BOT_XP_RATE_BY_LEVEL = "";
                DOL.GS.ServerProperties.Properties.MONEY_DROP = 1;
                Bot bot = NewBot("coin", level: 50);
                Mob mob = NewMob(50);
                const long minLoot = 2 + ((51 * 51 * 51) >> 3); // the generator's base, plus up to half again

                long Coin() => new LootGeneratorMoney().GenerateLoot(mob, bot).GetDrops().Single().Item.Price;

                LootRulesFile.TestOverride = LootRules.Legacy;
                for (int i = 0; i < 20; i++)
                    Assert.That(Coin(), Is.InRange(minLoot, minLoot + (minLoot >> 1)), "off: as before");

                LootRulesFile.TestOverride = LootRules.Legacy with { Coin = LootRules.Legacy.Coin with { XpRate = new RandomItemXpRateRules { Enabled = true } } };
                double factor = Math.Pow(10, 0.4);
                for (int i = 0; i < 20; i++)
                    Assert.That(Coin(), Is.InRange((long)(minLoot * factor) - 1, (long)((minLoot + (minLoot >> 1)) * factor) + 1), "10x XP: about 2.5x coin");
            }
            finally
            {
                DOL.GS.ServerProperties.Properties.BOT_XP_RATE = botRate;
                DOL.GS.ServerProperties.Properties.BOT_XP_RATE_BY_LEVEL = botByLevel;
                DOL.GS.ServerProperties.Properties.MONEY_DROP = moneyDrop;
            }
        }

        // A database that keeps the rows added to it, enough for the saved bad-luck row.
        public class RecordingDb : DispatchProxy
        {
            public static readonly List<DataObject> Rows = [];

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == nameof(IObjectDatabase.AddObject) && args[0] is DataObject added)
                {
                    Rows.Add(added);
                    return true;
                }
                if (method.Name == nameof(IObjectDatabase.SaveObject))
                    return true;
                Type result = method.ReturnType;
                if (method.Name == nameof(IObjectDatabase.SelectObject))
                    return Rows.FirstOrDefault(result.IsInstanceOfType);
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return result.IsValueType ? Activator.CreateInstance(result) : null;
            }
        }

        private sealed class RecordingServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => RecordingDatabase;
        }

        private static readonly IObjectDatabase RecordingDatabase = DispatchProxy.Create<IObjectDatabase, RecordingDb>();

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
        }

        private static Player NewPlayer(string characterId)
        {
            Player player = Inert<Player>();
            Field(typeof(GamePlayer), player, "m_dbCharacter", new DbCoreCharacter { ObjectId = characterId });
            return player;
        }

        [Test]
        public void BadLuckIsKeptAcrossLoginsWhenSaved()
        {
            RecordingDb.Rows.Clear();
            GameServer.LoadTestDouble((RecordingServer)RuntimeHelpers.GetUninitializedObject(typeof(RecordingServer)));
            LootRulesFile.TestOverride = new LootRules { RandomItems = LootRules.Legacy.RandomItems with { SaveBadLuck = true, BadLuckStep = 1, BadLuckMax = 25 } };

            Player first = NewPlayer("char-1");
            Assert.That(ROGMobGenerator.BadLuck(first), Is.EqualTo(0), "nothing saved yet");
            first.TempProperties.SetProperty(ROGMobGenerator.BadLuckProperty, 7.0);
            LootBadLuckStore.Save(first);
            Assert.That(RecordingDb.Rows.OfType<DbCoreCharacterXCustomParam>().Single().Value, Is.EqualTo("7"));

            Player again = NewPlayer("char-1"); // a new session of the same character
            Assert.That(ROGMobGenerator.BadLuck(again), Is.EqualTo(7));

            LootBadLuckStore.Save(again);
            Assert.That(RecordingDb.Rows, Has.Count.EqualTo(1), "unchanged: nothing written");
        }

        [Test]
        public void ASimulationNeverMovesBadLuck()
        {
            LootRulesFile.TestOverride = new LootRules { RandomItems = LootRules.Legacy.RandomItems with { BadLuckStep = 1, BadLuckMax = 25 } };
            Bot bot = NewBot("gm");
            bot.TempProperties.SetProperty(ROGMobGenerator.BadLuckProperty, 4.0);
            var generator = new Scripted { Otherwise = false };
            LootSimulator.Run(() => generator.GenerateLoot(NewMob(), bot).GetDrops().Select(d => d.Item).ToArray(), 50);
            Assert.That(generator.Chances, Is.All.EqualTo(28.0), "the bonus still counts in the simulated rolls");
            Assert.That(ROGMobGenerator.BadLuck(bot), Is.EqualTo(4), "50 simulated misses leave it as it was");
            generator.Otherwise = true;
            LootSimulator.Run(() => generator.GenerateLoot(NewMob(), bot).GetDrops().Select(d => d.Item).ToArray(), 5);
            Assert.That(ROGMobGenerator.BadLuck(bot), Is.EqualTo(4), "simulated drops do not clear it either");
            generator.GenerateLoot(NewMob(), bot);
            Assert.That(ROGMobGenerator.BadLuck(bot), Is.EqualTo(0), "a real drop still clears it");
        }

        [Test]
        public void BadLuckIsNotSavedWhenTheRuleIsOff()
        {
            RecordingDb.Rows.Clear();
            GameServer.LoadTestDouble((RecordingServer)RuntimeHelpers.GetUninitializedObject(typeof(RecordingServer)));
            LootRulesFile.TestOverride = new LootRules { RandomItems = LootRules.Legacy.RandomItems with { BadLuckStep = 1 } };
            Player player = NewPlayer("char-2");
            ROGMobGenerator.BadLuck(player);
            player.TempProperties.SetProperty(ROGMobGenerator.BadLuckProperty, 7.0);
            LootBadLuckStore.Save(player);
            Assert.That(RecordingDb.Rows, Is.Empty);
        }

        #endregion
    }
}
