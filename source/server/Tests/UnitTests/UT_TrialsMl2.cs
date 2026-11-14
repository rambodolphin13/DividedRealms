using System.Linq;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TrialsMl2
    {
        private static readonly string[] Names =
        {
            "Death from Shadow", "Stalking Terror", "Triton Hoards", "Rrionne's Reflection", "Chath's Test of Strategy",
            "Kanahkt Test of Tactics", "Pick Your Poison", "Stand Fast", "Battle Masters", "Immortal Imposter",
        };

        private static TrialDefinition Step(int step) => Ml2.Trials().First(trial => trial.Step == step);

        [Test]
        public void EveryStep_ExistsInOrder()
        {
            var trials = Ml2.Trials().ToList();
            Assert.That(trials.Select(trial => trial.Step), Is.EqualTo(Enumerable.Range(1, 10)));
            Assert.That(trials.Select(trial => trial.Name), Is.EqualTo(Names));
            Assert.That(trials.All(trial => trial.ML == 2), Is.True);
        }

        [Test]
        public void EveryStep_HasItsScope()
        {
            Assert.That(Step(5).Scope, Is.EqualTo(TrialScope.Group));
            Assert.That(Ml2.Trials().Where(trial => trial.Step is not (5 or 8)).All(trial => trial.Scope == TrialScope.Raid), Is.True);
        }

        [Test]
        public void StandFast_IsSoloAsReportedNotGroupAsTheWindowSays()
        {
            Assert.That(Step(8).Scope, Is.EqualTo(TrialScope.Solo));
        }

        [Test]
        public void EveryStep_StandsInsideItsZone()
        {
            foreach (TrialDefinition trial in Ml2.Trials())
            {
                if (trial.Step == 10)
                {
                    Assert.That(trial.Zone, Is.EqualTo(TrialZone.Oceanus));
                    Assert.That(trial.Anchor.X, Is.InRange(327680, 393216));
                    Assert.That(trial.Anchor.Y, Is.InRange(458752, 524288));
                }
                else
                {
                    Assert.That(trial.Zone, Is.EqualTo(TrialZone.Sobekite), trial.Key);
                    Assert.That(trial.Anchor.X, Is.InRange(8192, 73728), trial.Key);
                    Assert.That(trial.Anchor.Y, Is.InRange(8192, 73728), trial.Key);
                }
            }
        }

        [Test]
        public void KeyedTrials_NeedTheStepThatGivesTheKey()
        {
            Assert.That(TrialManager.Required(Step(5)), Is.EqualTo(new[] { 4 }));
            Assert.That(TrialManager.Required(Step(6)), Is.EqualTo(new[] { 3 }));
            Assert.That(TrialManager.Required(Step(7)), Is.EqualTo(new[] { 3 }));
            Assert.That(TrialManager.Required(Step(9)), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void Finale_NeedsTheNineTrials()
        {
            Assert.That(TrialManager.Required(Step(10)), Is.EqualTo(Enumerable.Range(1, 9)));
        }

        [Test]
        public void Bait_SpringsOnSittingOrAfterTheWait()
        {
            Assert.That(Ml2.BaitSprung(2, 30), Is.False);
            Assert.That(Ml2.BaitSprung(3, 30), Is.True);
            Assert.That(Ml2.BaitSprung(0, 90), Is.True);
        }

        [Test]
        public void Reaver_VanishesTwice()
        {
            Assert.That(Ml2.ReaverVanishes(70, 0), Is.False);
            Assert.That(Ml2.ReaverVanishes(66, 0), Is.True);
            Assert.That(Ml2.ReaverVanishes(50, 1), Is.False);
            Assert.That(Ml2.ReaverVanishes(30, 1), Is.True);
            Assert.That(Ml2.ReaverVanishes(5, 2), Is.False);
        }

        [Test]
        public void Board_TakesTheQueenFirstAndTheKingLast()
        {
            Assert.That(Ml2.NextPiece(Ml2.ChessOrder.Reverse()), Is.EqualTo("Chaths's Queen"));
            Assert.That(Ml2.NextPiece(new[] { "Chaths's King", "Chaths's Pawn" }), Is.EqualTo("Chaths's Pawn"));
            Assert.That(Ml2.NextPiece(new[] { "Chaths's King" }), Is.EqualTo("Chaths's King"));
            Assert.That(Ml2.NextPiece(new string[0]), Is.Null);
        }

        [Test]
        public void Eidolons_GrowUpToALimit()
        {
            Assert.That(Ml2.EidolonDamage(1), Is.GreaterThan(Ml2.EidolonDamage(0)));
            Assert.That(Ml2.EidolonDamage(50), Is.LessThanOrEqualTo(300));
        }

        [Test]
        public void Spikes_FallOnTheFourth()
        {
            Assert.That(Ml2.SpikesFall(3), Is.False);
            Assert.That(Ml2.SpikesFall(4), Is.True);
        }

        [Test]
        public void Poisons_OfferAllThreeTests()
        {
            Assert.That(Ml2.Poisons, Is.EqualTo(new[] { "Fear", "Terror", "Horror" }));
        }

        [Test]
        public void DeathFromShadow_EndsOnOneAssassinEitherName()
        {
            Assert.That(Ml2.Assassin(0), Is.EqualTo("Ka'a"));
            Assert.That(Ml2.Assassin(1), Is.EqualTo("Ata"));
            Assert.That(Step(1).Phases, Has.Count.EqualTo(2));
        }

        [Test]
        public void Credit_DeadGetNothingOnlyWhereLiveSaidSo()
        {
            Assert.That(Step(3).Earns, Is.Not.Null);
            Assert.That(Step(9).Earns, Is.Not.Null);
            Assert.That(Step(5).Earns, Is.Not.Null);
            Assert.That(Step(7).Earns, Is.Not.Null);
            foreach (int step in new[] { 1, 2, 4, 6, 8, 10 })
                Assert.That(Step(step).Earns, Is.Null, $"2.{step}");
        }

        [Test]
        public void Chess_CreditsOnlyPlayersWhoSetAPiece()
        {
            string leader = "leader", friend = "friend", idle = "idle";
            var setters = new System.Collections.Generic.HashSet<string> { friend };
            Assert.That(Ml2.SetterEarns(setters, friend, leader), Is.True);
            Assert.That(Ml2.SetterEarns(setters, leader, leader), Is.False);
            Assert.That(Ml2.SetterEarns(setters, idle, leader), Is.False);
            Assert.That(Ml2.SetterEarns<string>(null, leader, leader), Is.True, "swept board credits the leader");
            Assert.That(Ml2.SetterEarns<string>(null, idle, leader), Is.False);
        }

        [Test]
        public void Chess_EnemyPiecesCannotBeFoughtByHand()
        {
            TrialPhase board = Step(5).Phases[1];
            Assert.That(board.Mobs.All(mob => mob.Immune && mob.AggroRange == 0), Is.True);
            Assert.That(Step(5).Phases[0].Mobs.All(mob => mob.Objective && mob.OnDeath != null), Is.True);
            Assert.That(Step(5).ResetSeconds, Is.LessThanOrEqualTo(60));
        }

        [Test]
        public void Poison_FearIsSoloTerrorAndHorrorCreditTheParty()
        {
            string leader = "leader", friend = "friend";
            Assert.That(Ml2.PoisonEarns("Fear", friend, friend, leader), Is.True);
            Assert.That(Ml2.PoisonEarns("Fear", friend, leader, leader), Is.False);
            Assert.That(Ml2.PoisonEarns<string>("Fear", null, leader, leader), Is.True);
            Assert.That(Ml2.PoisonEarns<string>("Fear", null, friend, leader), Is.False);
            Assert.That(Ml2.PoisonEarns("Terror", null, friend, leader), Is.True);
            Assert.That(Ml2.PoisonEarns("Horror", null, friend, leader), Is.True);
        }

        [Test]
        public void StandFast_FoeConsYellowAndResetsQuickly()
        {
            TrialMobSpec amenemhat = Step(8).Phases[0].Mobs.Single();
            Assert.That(amenemhat.Level, Is.EqualTo(50));
            Assert.That(Step(8).ResetSeconds, Is.LessThanOrEqualTo(30));
        }

        [Test]
        public void BattleMasters_ConAsReported()
        {
            var masters = Step(9).Phases[0].Mobs.ToDictionary(mob => mob.Name, mob => mob.Level);
            Assert.That(masters["Buliss"], Is.GreaterThanOrEqualTo(57), "heavy tank purple");
            Assert.That(masters["Dimitri"], Is.InRange(50, 51), "light tank yellow");
            Assert.That(masters["Kaletor"], Is.InRange(54, 56), "PBAoE red");
            Assert.That(masters["Thao"], Is.InRange(54, 56), "rooter red");
        }

        [Test]
        public void TestOfTactics_StatuesBringFiveToTenEidolons()
        {
            Assert.That(Ml2.EidolonsPerStatue, Is.InRange(5, 10));
            foreach (TrialPhase phase in Step(6).Phases.Where(phase => phase.Mobs.Any(mob => mob.Tag == "statue")))
                Assert.That(phase.Mobs.Count(mob => mob.Tag == "eidolon"), Is.EqualTo(Ml2.EidolonsPerStatue));
        }

        [Test]
        public void StandFast_FallNeverKillsTheWinner()
        {
            Assert.That(Ml2.FallDamage(2000, 2000), Is.EqualTo(250));
            Assert.That(Ml2.FallDamage(200, 2000), Is.EqualTo(199));
            Assert.That(Ml2.FallDamage(1, 2000), Is.EqualTo(0));
        }

        [Test]
        public void Board_ThreeMovesTakeAPiece()
        {
            Assert.That(3 * Ml2.BoardBlow, Is.GreaterThanOrEqualTo(1.0));
            Assert.That(2 * Ml2.BoardBlow, Is.LessThan(1.0));
        }
    }
}
