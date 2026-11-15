using System.Linq;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TrialsMl4
    {
        private static readonly string[] Names =
        {
            "Sting", "Storms", "Roar", "Lost Goddess", "Masters of the Desert", "Ghillan", "Portal", "Blood", "Oasis", "The Rising Pyramid",
        };

        private static readonly TrialScope[] Scopes =
        {
            TrialScope.Group, TrialScope.Raid, TrialScope.Group, TrialScope.Raid, TrialScope.Raid,
            TrialScope.Group, TrialScope.Group, TrialScope.Raid, TrialScope.Raid, TrialScope.Raid,
        };

        [Test]
        public void EveryStepExistsInOrder()
        {
            Assert.That(Ml4.Trials().Select(trial => trial.Step), Is.EqualTo(Enumerable.Range(1, 10)));
            Assert.That(Ml4.Trials().Select(trial => trial.Name), Is.EqualTo(Names));
            Assert.That(Ml4.Trials().Select(trial => trial.ML).Distinct(), Is.EqualTo(new[] { 4 }));
        }

        [Test]
        public void ScopesMatchTheWindow()
        {
            Assert.That(Ml4.Trials().Select(trial => trial.Scope), Is.EqualTo(Scopes));
        }

        [Test]
        public void TokensBecomeStepRequirements()
        {
            TrialDefinition[] trials = Ml4.Trials().ToArray();
            Assert.That(TrialManager.Required(trials[6]), Is.EqualTo(Enumerable.Range(1, 6)));
            Assert.That(TrialManager.Required(trials[7]), Is.EqualTo(new[] { 7 }));
            Assert.That(TrialManager.Required(trials[8]), Is.EqualTo(new[] { 8 }));
            Assert.That(trials[9].RequiresSteps, Is.Null);
            Assert.That(TrialManager.Required(trials[9]), Is.EqualTo(Enumerable.Range(1, 9)));
        }

        [Test]
        public void OnlyTheFinaleIsInThePyramid()
        {
            foreach (TrialDefinition trial in Ml4.Trials())
                Assert.That(trial.Zone, Is.EqualTo(trial.Step == 10 ? TrialZone.Pyramid : TrialZone.Oceanus), trial.Key);
        }

        [Test]
        public void OverlandAnchorsLieInAtumOrTheDelta()
        {
            foreach (TrialDefinition trial in Ml4.Trials().Where(trial => trial.Zone == TrialZone.Oceanus))
            {
                Assert.That(trial.Anchor.X, Is.InRange(28 * 8192, 44 * 8192), trial.Key);
                Assert.That(trial.Anchor.Y, Is.InRange(48 * 8192, 56 * 8192), trial.Key);
            }
        }

        [Test]
        public void LeashesReachTheFarthestStop()
        {
            TrialDefinition portal = Ml4.Trials().Single(trial => trial.Step == 7);
            foreach (TrialPoint statue in Ml4.Statues)
                Assert.That(Ml34Kit.Distance(portal.Anchor, statue), Is.LessThan(portal.LeashRange));

            TrialDefinition blood = Ml4.Trials().Single(trial => trial.Step == 8);
            Assert.That(Ml34Kit.Distance(blood.Anchor, new TrialPoint(353613, 406725, 8824)), Is.LessThan(blood.LeashRange));
        }

        [Test]
        public void Portal_HasAStopAndAStoneForEachStatue()
        {
            TrialDefinition portal = Ml4.Trials().Single(trial => trial.Step == 7);
            Assert.That(Ml4.Stones, Has.Length.EqualTo(6));
            Assert.That(portal.Phases, Has.Count.EqualTo(Ml4.Statues.Length));
            Assert.That(Ml4.Statues.Select(statue => statue.X), Is.Ordered);
        }

        [Test]
        public void Obelisk_HurtsFarMoreWhenItLandsOnHim()
        {
            Assert.That(Ml4.ObeliskShare(300), Is.EqualTo(Ml4.ObeliskHitShare));
            Assert.That(Ml4.ObeliskShare(Ml4.ObeliskReach + 1), Is.EqualTo(Ml4.ObeliskMissShare));
            Assert.That(Ml4.ObeliskHitShare, Is.GreaterThan(Ml4.ObeliskMissShare));
        }

        [Test]
        public void SplitMeter_ArrowsNeverSplit()
        {
            SplitMeter meter = new(2);
            Assert.That(Enumerable.Range(0, 10).Select(_ => meter.Hit(ranged: true)), Is.All.False);
        }

        [Test]
        public void SplitMeter_SplitsEveryFewBlows()
        {
            SplitMeter meter = new(3);
            Assert.That(new[] { meter.Hit(false), meter.Hit(false), meter.Hit(false), meter.Hit(false) }, Is.EqualTo(new[] { false, false, true, false }));
        }

        [Test]
        public void Along_RunsFromStartToEnd()
        {
            TrialPoint a = new(0, 0, 100);
            TrialPoint b = new(1000, 0, 200);
            Assert.That(Ml34Kit.Along(a, b, 0.5, 0), Is.EqualTo(new TrialPoint(500, 0, 150)));
            Assert.That(Ml34Kit.Along(a, b, 0.5, 100).Y, Is.EqualTo(100));
        }

        [Test]
        public void Sting_IsAtTheAtumArch()
        {
            TrialDefinition sting = Ml4.Trials().Single(trial => trial.Step == 1);
            Assert.That(sting.Anchor, Is.EqualTo(Ml4.StingCamp));
            Assert.That(sting.Anchor.X - 28 * 8192, Is.InRange(4000, 6000), "5k Land of Atum");
            Assert.That(sting.Anchor.Y - 48 * 8192, Is.InRange(24000, 26000), "25k Land of Atum");
            Assert.That(sting.Phases, Has.Count.EqualTo(3));
        }

        [Test]
        public void Sting_GiantsAreYellowOrangeAndRed()
        {
            Assert.That(Ml4.GiantLevels, Has.Length.EqualTo(3));
            Assert.That(Ml4.GiantLevels, Is.Ordered.Ascending);
            Assert.That(Ml4.GiantLevels[0], Is.InRange(50, 51));
            Assert.That(Ml4.GiantLevels[2], Is.InRange(54, 57));
        }

        [Test]
        public void Storms_EveryTownAndCampIsInsideTheLeash()
        {
            TrialDefinition storms = Ml4.Trials().Single(trial => trial.Step == 2);
            TrialPoint[] stops = Ml4.StormsTowns.Concat(Ml4.StormsCamps.SelectMany(camps => camps)).Append(Ml4.StormsGate).Append(Ml4.SetiThrone).ToArray();
            Assert.That(stops, Has.Length.EqualTo(14));
            // A party fights a few hundred units past a camp's centre: leave room for the chase.
            foreach (TrialPoint stop in stops)
                Assert.That(Ml34Kit.Distance(storms.Anchor, stop) + 2000, Is.LessThan(storms.LeashRange), stop.ToString());
        }

        [Test]
        public void Blood_ReturnsHomeBeforeTheSweepGivesUp()
        {
            Assert.That(Ml4.HomeSeconds, Is.LessThan(6));
            TrialDefinition blood = Ml4.Trials().Single(trial => trial.Step == 8);
            Assert.That(blood.OnEnd, Is.Not.Null, "a failed run must bring the party out of the valley");
        }

        [Test]
        public void Blood_ValleyIsFarFromThePeak()
        {
            Assert.That(Ml4.InValley(Ml4.Valley), Is.True);
            Assert.That(Ml4.InValley(Ml4.DaehienLair), Is.True);
            Assert.That(Ml4.InValley(Ml4.Peak), Is.False);
            Assert.That(Ml4.InValley(Ml4.StingCamp), Is.False);
            foreach (TrialPoint statue in Ml4.Statues)
                Assert.That(Ml4.InValley(statue), Is.False, statue.ToString());
        }

        [Test]
        public void EveryCreditStepNeedsOnlyEarlierSteps()
        {
            foreach (TrialDefinition trial in Ml4.Trials())
                Assert.That(TrialManager.Required(trial), Is.All.LessThan(trial.Step), trial.Key);
        }

        [Test]
        public void Ghillan_SplitsEverySixthBlowOfEachMember()
        {
            Assert.That(Ml4.GhillanBlowsPerClone(1), Is.EqualTo(6), "a lone player splits him as before");
            Assert.That(Ml4.GhillanBlowsPerClone(0), Is.EqualTo(6));
            Assert.That(Ml4.GhillanBlowsPerClone(8), Is.EqualTo(48), "a full group must not raise all ten copies in the first minute");
        }

        [Test]
        public void Storms_SetsAsideTheWorldGarrisonEverywhereItFights()
        {
            TrialPoint[] hushed = Ml4.StormsHushPoints().ToArray();
            Assert.That(hushed, Has.Length.EqualTo(Ml4.StormsTowns.Length + Ml4.StormsCamps.Sum(camps => camps.Length) + 2));
            Assert.That(hushed, Does.Contain(Ml4.StormsGate));
            Assert.That(hushed, Does.Contain(Ml4.SetiThrone));
            Assert.That(Ml4.Trials().First(trial => trial.Step == 2).OnEnd, Is.Not.Null, "the world garrison must come back when a run ends");
        }
    }
}
