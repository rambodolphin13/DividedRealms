using System;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Overnight run 2026-11-04/05 (build 616190a): the Marfach Caverns raid timed out after 91 pulls on an Ancient Black
    /// Oak that never entered combat, and the night's sieges were logged without the army they could draw on
    /// (docs/NIGHT-SIEGES-AND-MARFACH.md).
    /// </summary>
    [TestFixture]
    public class UT_MarfachAndNightSieges
    {
        [TestCase(0, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(91, true)]
        public void ThreePullsThatReachNobodySetTheEncounterAside(int pulls, bool setAside) =>
            Assert.That(RealmRaidDungeonRoute.PullsNeverReached(pulls), Is.EqualTo(setAside));

        [Test]
        public void TheMarfachLoopEndsOnTheThirdEmptyPull()
        {
            // The run: every pull went out, waited the two-minute grace, and ended with the Oak out of combat and no wipe.
            var pulls = new RealmRaidDungeonRoute.PullContact();
            int setAsideAt = 0;
            for (int pull = 1; pull <= 91 && setAsideAt == 0; pull++)
            {
                pulls.Begin();
                if (pulls.Ended(wiped: false)) setAsideAt = pull;
            }
            Assert.That(setAsideAt, Is.EqualTo(RealmRaidDungeonRoute.MaximumPullsWithoutContact));
        }

        [Test]
        public void APullThatReachesTheEncounterStartsTheCountAgain() => Assert.Multiple(() =>
        {
            var pulls = new RealmRaidDungeonRoute.PullContact();
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False);
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False);
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(2));
            // The third pull gets a fight going and the raid breaks off: an ordinary attempt, not an unreachable encounter.
            pulls.Begin();
            pulls.SawCombat();
            Assert.That(pulls.Ended(false), Is.False);
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(0));
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False, "one empty pull after a fight is not three in a row");
        });

        [Test]
        public void AWipeIsAFailedAttemptNotAnUnreachableEncounter() => Assert.Multiple(() =>
        {
            var pulls = new RealmRaidDungeonRoute.PullContact();
            for (int i = 0; i < 5; i++)
            {
                pulls.Begin();
                Assert.That(pulls.Ended(wiped: true), Is.False);
            }
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(0));
        });

        [Test]
        public void ANewEncounterStartsWithNoEmptyPulls()
        {
            var pulls = new RealmRaidDungeonRoute.PullContact();
            pulls.Begin(); pulls.Ended(false);
            pulls.Begin(); pulls.Ended(false);
            pulls.Reset();
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False);
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(1));
        }

        private static long At(string hhmmss) => (long)TimeSpan.Parse(hhmmss).TotalMilliseconds;

        [Test]
        public void KrondonsBeranMusterIsSetAsideWhenItStopsGaining()
        {
            // Build 8d7baa3, 2026-11-05: REALM_EXPEDITION_FINAL_MUSTER for Beran the Supply Master (needed 50), with the raid
            // turning on Scurceol Hyrde from 14:42:32 to 14:48:07 in between (the muster clock restarted at each return).
            var muster = new RealmRaidDungeonRoute.MusterProgress();
            (string At, int Mustered)[] samples =
            [
                ("14:27:55", 0), ("14:29:56", 2), ("14:31:57", 2), ("14:33:58", 2), ("14:35:59", 2), ("14:38:00", 2),
                ("14:40:01", 3), ("14:42:02", 5),
                ("14:48:07", 2), ("14:50:08", 2), ("14:52:09", 2), ("14:54:10", 2), ("14:56:12", 2), ("14:58:14", 2),
                ("15:00:15", 2), ("15:02:16", 2),
            ];
            string stalledAt = null;
            foreach (var (time, mustered) in samples)
                if (muster.Observe(mustered, 50, At(time))) { stalledAt = time; break; }
            Assert.Multiple(() =>
            {
                Assert.That(stalledAt, Is.EqualTo("15:02:16"), "20 minutes after its best (5 at 14:42), not never");
                Assert.That(muster.Best, Is.EqualTo(5));
            });
        }

        [Test]
        public void AMusterThatKeepsGainingOrIsFullIsNeverStalled() => Assert.Multiple(() =>
        {
            var slow = new RealmRaidDungeonRoute.MusterProgress();
            // A long walk in: one more raider every five minutes for an hour.
            bool stalled = false;
            for (int minute = 0, count = 1; minute <= 60; minute += 5, count++)
                stalled |= slow.Observe(count, 50, minute * 60_000L);
            Assert.That(stalled, Is.False, "still gaining");
            var full = new RealmRaidDungeonRoute.MusterProgress();
            full.Observe(60, 50, 0);
            Assert.That(full.Observe(55, 50, 30 * 60_000L), Is.False, "enough there: the raid goes in, it is not stalled");
            var empty = new RealmRaidDungeonRoute.MusterProgress();
            Assert.That(empty.Observe(0, 50, 0), Is.False);
            Assert.That(empty.Observe(0, 50, RealmRaidDungeonRoute.MusterStalledMilliseconds - 1), Is.False);
            Assert.That(empty.Observe(0, 50, RealmRaidDungeonRoute.MusterStalledMilliseconds), Is.True,
                "an empty muster stalls too (MUSTER_UNREACHABLE usually takes it first)");
        });

        [Test]
        public void TheSiegePoolCountsTheAttackersLevelFiftiesAndTheirRvrDuty()
        {
            var bots = new (eRealm, int, bool)[]
            {
                (eRealm.Albion, 50, true), (eRealm.Albion, 50, false), (eRealm.Albion, 49, true),
                (eRealm.Midgard, 50, true), (eRealm.Albion, 50, true),
            };
            Assert.That(AutonomousRvrEventLayer.SiegePool(bots, eRealm.Albion), Is.EqualTo((3, 2)));
            Assert.That(AutonomousRvrEventLayer.SiegePool(bots, eRealm.Hibernia), Is.EqualTo((0, 0)));
            Assert.That(AutonomousRvrEventLayer.SiegePool(null, eRealm.Albion), Is.EqualTo((0, 0)));
        }

        [Test]
        public void TheMarchLineNamesTheArmyAndItsPool() =>
            Assert.That(AutonomousRvrEventLayer.SiegeArmyFields(72, 594, 180, 16, 0.5),
                Is.EqualTo("assigned=72 realmLevel50=594 realmRvr=180 inOtherSieges=16 defenseShare=0.5"));
    }
}
