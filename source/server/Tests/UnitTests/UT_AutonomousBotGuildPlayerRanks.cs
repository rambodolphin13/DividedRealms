using NUnit.Framework;
using Ranks = DOL.GS.AutonomousBotGuildPlayerRanks;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousBotGuildPlayerRanks
    {
        private static Ranks.Standing At(int rank, double hours, double groupedHours, int level, int deeds) =>
            new(rank, level, (int)(hours * 60), (int)(groupedHours * 60), deeds);

        [Test]
        public void NewMemberStaysAtJoinRank()
        {
            Assert.That(Ranks.RankFor(At(8, 1, 0, 50, 0), 1.0), Is.EqualTo(8));
        }

        [Test]
        public void TwoHoursInTheGuildGivesRankSeven()
        {
            Assert.That(Ranks.RankFor(At(8, 2, 0, 5, 0), 1.0), Is.EqualTo(7));
        }

        [Test]
        public void ClimbsEveryStepMetInOneGo()
        {
            Assert.That(Ranks.RankFor(At(8, 20, 6, 45, 1), 1.0), Is.EqualTo(3));
        }

        [Test]
        public void AFailedStepStopsTheClimbEvenWhenHigherNeedsAreMet()
        {
            // Plenty of hours and deeds but level 30: rank 5 needs 20, rank 3 needs 40.
            Assert.That(Ranks.RankFor(At(8, 200, 100, 30, 9), 1.0), Is.EqualTo(5));
        }

        [Test]
        public void TopOfTheLadderIsRankZero()
        {
            Assert.That(Ranks.RankFor(At(1, 150, 50, 50, 4), 1.0), Is.EqualTo(0));
            Assert.That(Ranks.NextStep(0), Is.Null);
        }

        [Test]
        public void NeverDemotesAHandSetRank()
        {
            Assert.That(Ranks.RankFor(At(2, 0, 0, 1, 0), 1.0), Is.EqualTo(2));
        }

        [Test]
        public void PaceScalesTheHours()
        {
            Assert.That(Ranks.RankFor(At(8, 1, 0, 1, 0), 1.0), Is.EqualTo(8));
            Assert.That(Ranks.RankFor(At(8, 1, 0, 1, 0), 0.5), Is.EqualTo(7));
        }

        [Test]
        public void NextStepIsTheRungAboveTheCurrentRank()
        {
            Assert.That(Ranks.NextStep(8)?.Rank, Is.EqualTo(7));
            Assert.That(Ranks.NextStep(7)?.Rank, Is.EqualTo(5));
            Assert.That(Ranks.NextStep(4)?.Rank, Is.EqualTo(3));
        }

        [TestCase(BotGuildCharter.Rvr, 50, 0, 0, 0, 2)]
        [TestCase(BotGuildCharter.Raiding, 0, 5, 0, 0, 2)]
        [TestCase(BotGuildCharter.Crafting, 0, 0, 800, 0, 3)]
        [TestCase(BotGuildCharter.Social, 100, 100, 1000, 0, 0)]
        [TestCase(BotGuildCharter.Social, 0, 0, 0, 30 * 60, 2)]
        [TestCase(BotGuildCharter.Rvr, 25, 0, 0, 45 * 60, 3)]
        public void DeedsFollowTheCharterOrGroupedTime(BotGuildCharter charter, int rvr, int named, int craft, int groupedMinutes, int expected)
        {
            Assert.That(Ranks.Deeds(charter, rvr, named, craft, groupedMinutes), Is.EqualTo(expected));
        }
    }
}
