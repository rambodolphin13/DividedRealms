using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TrialsMl10
    {
        private static List<TrialDefinition> Trials => Ml10.Trials().ToList();

        private static IEnumerable<TrialMobSpec> Specs(TrialDefinition trial) => trial.Phases.SelectMany(phase => phase.Mobs);

        private static TrialMobSpec Boss(TrialDefinition trial) => Specs(trial).Single(mob => mob.Tag == "boss");

        [Test]
        public void Steps_ExistInOrder()
        {
            Assert.That(Trials.Select(trial => trial.Step), Is.EqualTo(Enumerable.Range(1, 5)));
            Assert.That(Trials.Select(trial => trial.Name), Is.EqualTo(new[] { "Sagittarius", "Scorpius", "Centaurus", "Leo", "Draco" }));
            Assert.That(Trials.All(trial => trial.ML == 10 && trial.Scope == TrialScope.Raid));
        }

        [Test]
        public void MasterLevel10_HasFiveSteps()
        {
            // GamePlayer.MLStepsForLevel[10] and the catalog check agree with live's five Celestius kills.
            Assert.That(Trials.Count, Is.EqualTo(5));
            Assert.That(TrialCatalog.All.Count(trial => trial.ML == 10), Is.EqualTo(5));
        }

        [Test]
        public void EachStep_NeedsAllTheEarlierOnes()
        {
            foreach (TrialDefinition trial in Trials)
                Assert.That(TrialManager.Required(trial), Is.EqualTo(Enumerable.Range(1, trial.Step - 1)), trial.Key);
        }

        [Test]
        public void AllSteps_AreFoughtInCelestius()
        {
            Assert.That(Trials.All(trial => trial.Zone == TrialZone.Celestius));
            Assert.That(TrialManager.RegionsOf(TrialZone.Celestius).Distinct(), Is.EqualTo(new ushort[] { 91 }));
        }

        [Test]
        public void Stones_StandOnTheArenaFloor()
        {
            // The sunken floor of the platform (zone091 mesh, Z 15734) spans x 30000-33750, y 29650-30900.
            Assert.That(Trials.Select(trial => trial.Anchor).Distinct().Count(), Is.EqualTo(5));
            foreach (TrialDefinition trial in Trials)
            {
                Assert.That(trial.Anchor.X, Is.InRange(30100, 33650), trial.Key);
                Assert.That(trial.Anchor.Y, Is.InRange(29700, 30850), trial.Key);
                Assert.That(trial.Anchor.Z, Is.EqualTo(15734), trial.Key);
            }
        }

        [Test]
        public void Monsters_StandOnTheArenaFloor()
        {
            foreach (TrialMobSpec mob in Trials.SelectMany(Specs))
            {
                Assert.That(mob.At, Is.Not.Null, mob.Name);
                Assert.That(mob.At.Value.X, Is.InRange(30100, 33650), mob.Name);
                Assert.That(mob.At.Value.Y, Is.InRange(29700, 30850), mob.Name);
                Assert.That(mob.At.Value.Z, Is.EqualTo(15734), mob.Name);
            }
            for (int i = 0; i < 6; i++)
            {
                TrialPoint bow = Ml10.Ring(Ml10.ArenaCentre, i, 6, 600);
                Assert.That(bow.X, Is.InRange(30100, 33650));
                Assert.That(bow.Y, Is.InRange(29700, 30850));
            }
        }

        [Test]
        public void Party_StaysLeashedFromLandingToArena()
        {
            foreach (TrialDefinition trial in Trials)
            {
                double dx = trial.Anchor.X - Ml10.Landing.X, dy = trial.Anchor.Y - Ml10.Landing.Y;
                Assert.That(System.Math.Sqrt(dx * dx + dy * dy) + 500, Is.LessThan(trial.LeashRange), trial.Key);
            }
        }

        [Test]
        public void Bosses_UseTheConstellationModels()
        {
            Assert.That(Trials.Select(trial => Boss(trial).Model), Is.EqualTo(new[] { Ml10.StarryCentaur, Ml10.StarryScorpion, Ml10.StarryCentaur, Ml10.StarryLion, Ml10.DracoModel }));
        }

        [Test]
        public void Bosses_CarryTheNamesTheWorldLootRowsUse()
        {
            // MobXLootTemplate rows in the test world: sagittarius, centaurus, leo, draco; LootTemplate "scorpious".
            Assert.That(Trials.Select(trial => Boss(trial).Name.ToLowerInvariant()), Is.EqualTo(new[] { "sagittarius", "scorpious", "centaurus", "leo", "draco" }));
        }

        [Test]
        public void Sagittarius_IgnoresSpellsButNotSteel()
        {
            TrialMobSpec boss = Boss(Trials[0]);
            Assert.That(boss.Hurts.HasFlag(TrialDamageKind.Spell), Is.False);
            Assert.That(boss.Hurts.HasFlag(TrialDamageKind.Melee) && boss.Hurts.HasFlag(TrialDamageKind.Ranged));
            Assert.That(boss.Immune, Is.False);
        }

        [Test]
        public void Cheiron_IsMortalOnlyBriefly()
        {
            Assert.That(Enumerable.Range(0, Ml10.CheironCycle).Count(Ml10.CheironMortal), Is.EqualTo(Ml10.CheironSeconds));
        }

        [TestCase(100, 0)]
        [TestCase(76, 0)]
        [TestCase(75, 1)]
        [TestCase(50, 2)]
        [TestCase(26, 2)]
        [TestCase(25, 3)]
        [TestCase(1, 3)]
        public void Leo_SplitsLessAfter189(int health, int splits)
        {
            Assert.That(Ml10.LeoSplits(health), Is.EqualTo(splits));
        }

        [Test]
        public void Leo_IsNeverImmune_SoAMeleePartyCanFinish()
        {
            TrialMobSpec boss = Boss(Trials[3]);
            Assert.That(boss.Immune, Is.False);
            Assert.That(boss.Hurts, Is.EqualTo(TrialDamageKind.All));
        }

        [Test]
        public void Leo_FlingTakesHalf()
        {
            Assert.That(Ml10.FlingDamage(2000), Is.EqualTo(1000));
        }

        [Test]
        public void Draco_IsTheStrongestOfThem()
        {
            Assert.That(Boss(Trials[4]).Health, Is.EqualTo(Trials.Max(trial => Boss(trial).Health)));
            Assert.That(Boss(Trials[4]).Health, Is.LessThanOrEqualTo(150000));
        }

        [Test]
        public void Bosses_AreTheOnlyRequiredMonsters()
        {
            foreach (TrialDefinition trial in Trials)
                Assert.That(Specs(trial).Where(mob => mob.Required).Select(mob => mob.Tag), Is.EqualTo(new[] { "boss" }), trial.Key);
        }

        [Test]
        public void Unready_LeadersAreRefusedBelowMasterLevel9()
        {
            Assert.That(Ml10.RequiredMasterLevel, Is.EqualTo(9));
            Assert.That(Trials.All(trial => trial.Refuse != null));
        }

        [Test]
        public void Gates_LeadEachRealmHomeToItsOwnAerus()
        {
            Assert.That(Ml10.AerusFor(eRealm.Midgard), Is.EqualTo((ushort)47));
            Assert.That(Ml10.AerusFor(eRealm.Albion), Is.EqualTo((ushort)90));
            Assert.That(Ml10.AerusFor(eRealm.Hibernia), Is.EqualTo((ushort)147));
        }
    }
}
