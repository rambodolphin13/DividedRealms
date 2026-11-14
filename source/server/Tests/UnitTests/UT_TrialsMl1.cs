using System.Linq;
using DOL.GS;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TrialsMl1
    {
        private static readonly string[] Names =
        {
            "Survivor", "Retrieval", "Greater Good", "Barrier", "Conflict", "Curses", "The Monument", "The Ruby", "The Crown", "Conquering the Sea",
        };

        private static readonly TrialScope[] Scopes =
        {
            TrialScope.Group, TrialScope.Group, TrialScope.Raid, TrialScope.Group, TrialScope.Solo,
            TrialScope.Raid, TrialScope.Raid, TrialScope.Raid, TrialScope.Raid, TrialScope.Raid,
        };

        [Test]
        public void EveryStep_ExistsInOrder()
        {
            var trials = Ml1.Trials().ToList();
            Assert.That(trials.Select(trial => trial.Step), Is.EqualTo(Enumerable.Range(1, 10)));
            Assert.That(trials.Select(trial => trial.Name), Is.EqualTo(Names));
            Assert.That(trials.All(trial => trial.ML == 1), Is.True);
        }

        [Test]
        public void EveryStep_HasTheWindowScope()
        {
            Assert.That(Ml1.Trials().Select(trial => trial.Scope), Is.EqualTo(Scopes));
        }

        [Test]
        public void EveryStep_StandsInsideItsZone()
        {
            foreach (TrialDefinition trial in Ml1.Trials())
            {
                if (trial.Zone == TrialZone.CetusPit)
                {
                    Assert.That(trial.Anchor.X, Is.InRange(8192, 73728), trial.Key);
                    Assert.That(trial.Anchor.Y, Is.InRange(8192, 73728), trial.Key);
                }
                else
                {
                    Assert.That(trial.Zone, Is.EqualTo(TrialZone.Oceanus), trial.Key);
                    Assert.That(trial.Anchor.X, Is.InRange(262144, 458752), trial.Key);
                    Assert.That(trial.Anchor.Y, Is.InRange(458752, 655360), trial.Key);
                }
            }
        }

        [Test]
        public void Curses_CanBeStartedWithoutTheGemOrBelt()
        {
            // Prima ToA p.45: the gem and belt make 1.6 easier, but it is possible without either.
            Assert.That(TrialManager.Required(Ml1.Trials().First(trial => trial.Step == 6)), Is.Empty);
        }

        [Test]
        public void Curses_TrapsAndKirkleisAreInsideTheLeash()
        {
            TrialDefinition curses = Ml1.Trials().First(trial => trial.Step == 6);
            TrialPoint[] spots = curses.Phases.SelectMany(phase => phase.Mobs).Select(mob => mob.At.Value).ToArray();
            Assert.That(spots, Has.Length.EqualTo(4));
            foreach (TrialPoint spot in spots)
                Assert.That(Distance(curses.Anchor, spot), Is.LessThan(curses.LeashRange - 1000));
            Assert.That(curses.Phases.Single().Done, Is.Not.Null, "the run ends when Kirkleis dies, live or dead traps");
        }

        [Test]
        public void Credit_FinaleNeedsTheNineForEachPlayer()
        {
            // TrialManager.Award (MayEarn) skips anyone missing Required(definition): only the finale may have any.
            TrialDefinition cetus = Ml1.Trials().Last();
            Assert.That(TrialManager.Required(cetus), Is.EqualTo(Enumerable.Range(1, 9)));
            foreach (TrialDefinition trial in Ml1.Trials().Where(trial => trial.Step < 10))
                Assert.That(TrialManager.Required(trial), Is.Empty, $"{trial.Key} must credit helpers who lack earlier steps");
        }

        [Test]
        public void Conflict_ChampionsMeetTheChallengersLevel()
        {
            Assert.That(Ml1.ChampionLevel, Is.InRange(51, 52));
            TrialDefinition conflict = Ml1.Trials().First(trial => trial.Step == 5);
            Assert.That(conflict.Phases[0].Mobs.Where(mob => mob.Tag == "seat").Select(mob => mob.Level), Is.All.EqualTo(Ml1.ChampionLevel));
        }

        [Test]
        public void Survivor_BeginsAMinuteAfterAccepting()
        {
            Assert.That(Ml1.Trials().First().Phases[0].SurviveSeconds, Is.EqualTo(60));
        }

        private static double Distance(TrialPoint a, TrialPoint b) => System.Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y));

        [Test]
        public void Finale_NeedsTheNineTrials()
        {
            TrialDefinition cetus = Ml1.Trials().Last();
            Assert.That(TrialManager.Required(cetus), Is.EqualTo(Enumerable.Range(1, 9)));
            Assert.That(cetus.Zone, Is.EqualTo(TrialZone.CetusPit));
        }

        [Test]
        public void ReplacedNames_AreNotAlsoAnotherTrialsStarter()
        {
            var trials = TrialCatalog.All.Where(trial => trial.Zone == TrialZone.Oceanus).ToList();
            foreach (TrialDefinition trial in trials)
            {
                foreach (string name in trial.Replaces)
                {
                    bool earlierStarter = trials.TakeWhile(other => other != trial).Any(other => other.StarterName == name);
                    Assert.That(earlierStarter, Is.False, $"{trial.Key} would delete the starter named {name}");
                }
            }
        }

        [Test]
        public void Hideouts_AreTheFourPrimaTerritories()
        {
            Assert.That(Ml1.Hideouts, Has.Length.EqualTo(4));
            for (int roll = -5; roll < 9; roll++)
                Assert.That(Ml1.PickHideout(roll), Is.InRange(0, Ml1.Hideouts.Length - 1));
            Assert.That(Enumerable.Range(0, 4).Select(Ml1.PickHideout).Distinct().Count(), Is.EqualTo(4));
        }

        [Test]
        public void Hideouts_AreInMesothalassaAndInsideTheLeash()
        {
            TrialDefinition retrieval = Ml1.Trials().First(trial => trial.Step == 2);
            Assert.That(retrieval.LeashRange, Is.EqualTo(Ml1.RetrievalLeash));
            foreach (var hideout in Ml1.Hideouts)
            {
                foreach (TrialPoint spot in new[] { hideout.Borjad, hideout.Borjan })
                {
                    // Mesothalassa is the zone at 327680, 524288 (Lornas stands at its 26.8k, 24.8k).
                    Assert.That(spot.X - 327680, Is.InRange(0, 65535));
                    Assert.That(spot.Y - 524288, Is.InRange(0, 65535));
                    Assert.That(Distance(retrieval.Anchor, spot), Is.LessThan(retrieval.LeashRange - 2000), hideout.Way);
                }
                Assert.That(Distance(hideout.Borjad, hideout.Borjan), Is.LessThan(3500), "the brothers share a territory");
            }
        }

        [Test]
        public void Purros_BurnZopureoDownInAFewMinutes()
        {
            // Live 2-3% a purros over 5-7 minutes; one purros every 8 seconds at most.
            Assert.That(Ml1.PurrosShare, Is.InRange(0.02, 0.05));
            double purros = (1 - Ml1.ZopureoSpentPercent / 100.0) / Ml1.PurrosShare;
            Assert.That(purros * 8, Is.LessThanOrEqualTo(300));
        }

        [Test]
        public void Cetus_FleesThreeTimesAtMost()
        {
            Assert.That(Ml1.CetusFlees(80, 0), Is.False);
            Assert.That(Ml1.CetusFlees(75, 0), Is.True);
            Assert.That(Ml1.CetusFlees(60, 1), Is.False);
            Assert.That(Ml1.CetusFlees(50, 1), Is.True);
            Assert.That(Ml1.CetusFlees(25, 2), Is.True);
            Assert.That(Ml1.CetusFlees(1, 3), Is.False);
        }
    }
}
