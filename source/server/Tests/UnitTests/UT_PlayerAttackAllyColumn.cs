using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// DR-492: the player's own realm, called to a player's keep or relic keep attack, gathers at its portal outpost and
    /// marches as one column instead of walking in one warband at a time.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_PlayerAttackAllyColumn
    {
        [SetUp] public void SetUp() { RvrEventTestState.Clear(); }
        [TearDown] public void TearDown() { RvrEventTestState.Clear(); }

        private const string Helpers = "albion-helpers";
        private const string Defenders = "midgard-defenders";

        private static AutonomousRvrEventLayer.LiveObjective Relic(string id = "rvr-keep-9001") =>
            new(id, "relic keep Test Faste", AutonomousRvrEventLayer.Intent.AssaultRelicKeep, eRealm.Midgard, 100, 100000, 100000, 100, true, 0, 0, 0, 0);

        /// <summary>Recruitment itself needs live bots (PulseDefense); the tests register the warbands it would have added.</summary>
        private static void Register(string targetId, string side, string force, int members)
        {
            object events = typeof(AutonomousRvrEventLayer).GetField("Events", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            object active = events.GetType().GetProperty("Item").GetValue(events, new object[] { targetId });
            var forces = (Dictionary<string, int>)active.GetType().GetField(side).GetValue(active);
            forces[force] = members;
        }

        private static long Open(out string id)
        {
            long now = GameLoop.GameLoopTime + 100;
            id = Relic().Id;
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Relic(), eRealm.Albion, "offline", now,
                AutonomousRvrEventLayer.DefenseScale.Small), Is.True);
            Register(id, "Attackers", Helpers, 3);
            Register(id, "Defenders", Defenders, 4);
            return now;
        }

        [Test]
        public void HelpersGatherBeforeTheyMarch()
        {
            long now = Open(out string id);
            // Before the first helper is noted the old rule holds: no order, every warband walks in alone.
            Assert.That(AutonomousRvrEventLayer.HasAllyColumn(id), Is.False);
            Assert.That(AutonomousRvrEventLayer.GetRallyOrder(Helpers, eRealm.Albion, now), Is.Null);

            Assert.That(AutonomousRvrEventLayer.NoteAllyJoined(id, eRealm.Midgard, now), Is.False, "defenders open no helper muster");
            Assert.That(AutonomousRvrEventLayer.NoteAllyJoined(id, eRealm.Hibernia, now), Is.False, "the third realm stays chaotic");
            Assert.That(AutonomousRvrEventLayer.NoteAllyJoined(id, eRealm.Albion, now), Is.True);
            Assert.That(AutonomousRvrEventLayer.NoteAllyJoined(id, eRealm.Albion, now + 60_000), Is.False, "one muster per attack");
            Assert.That(AutonomousRvrEventLayer.HasAllyColumn(id), Is.True);

            var order = AutonomousRvrEventLayer.GetRallyOrder(Helpers, eRealm.Albion, now + 1000);
            Assert.That(order, Is.Not.Null);
            Assert.That(order.TargetId, Is.EqualTo(id));
            Assert.That(order.HomeMuster, Is.True, "gathering at the portal outpost");
            Assert.That(order.AllyColumn, Is.True);
            Assert.That(order.Slots, Has.Length.EqualTo(3));
            // The defenders are untouched: no defense muster was opened here, so they have no order.
            Assert.That(AutonomousRvrEventLayer.GetRallyOrder(Defenders, eRealm.Midgard, now + 1000), Is.Null);
            // The battle is still on for everyone (a player attack never waits for the helpers).
            Assert.That(AutonomousRvrEventLayer.IsBattleForce(Helpers, now + 1000), Is.True);
            Assert.That(AutonomousRvrEventLayer.IsRallying(Helpers, now + 1000), Is.False);
        }

        [Test]
        public void HelpersMarchAtTheDeadlineAfterOneExtensionWhenNobodyHasGathered()
        {
            long now = Open(out string id);
            AutonomousRvrEventLayer.NoteAllyJoined(id, eRealm.Albion, now);
            long muster = AutonomousRvrEventLayer.PlayerAllyMusterMilliseconds;
            Assert.That(muster, Is.EqualTo(5 * 60_000));

            Assert.That(AutonomousRvrEventLayer.GetRallyOrder(Helpers, eRealm.Albion, now + muster - 1000).HomeMuster, Is.True);
            // Deadline with nobody at the outpost: one more muster.
            Assert.That(AutonomousRvrEventLayer.GetRallyOrder(Helpers, eRealm.Albion, now + muster + 1000).HomeMuster, Is.True);
            // Second deadline: they go, relic keep or not (no ten-minute relic extensions for a player attack).
            var marching = AutonomousRvrEventLayer.GetRallyOrder(Helpers, eRealm.Albion, now + 2 * muster + 2000);
            Assert.That(marching, Is.Not.Null);
            Assert.That(marching.HomeMuster, Is.False);
            Assert.That(marching.AllyColumn, Is.True);
            Assert.That(AutonomousRvrEventLayer.HasAllyColumn(id), Is.True);
        }

        [Test]
        public void ColumnEndsWhenItsLeaderReachesTheKeep()
        {
            long now = Open(out string id);
            AutonomousRvrEventLayer.NoteAllyJoined(id, eRealm.Albion, now);
            long muster = AutonomousRvrEventLayer.PlayerAllyMusterMilliseconds;

            // Still gathering: an arrival report changes nothing.
            AutonomousRvrEventLayer.ReportArmyArrived(id, now + 1000);
            Assert.That(AutonomousRvrEventLayer.HasAllyColumn(id), Is.True);

            AutonomousRvrEventLayer.GetRallyOrder(Helpers, eRealm.Albion, now + muster + 1000);
            long marched = now + 2 * muster + 2000;
            Assert.That(AutonomousRvrEventLayer.GetRallyOrder(Helpers, eRealm.Albion, marched).HomeMuster, Is.False);
            AutonomousRvrEventLayer.ReportArmyArrived(id, marched + 60_000);
            Assert.That(AutonomousRvrEventLayer.HasAllyColumn(id), Is.False);
            Assert.That(AutonomousRvrEventLayer.GetRallyOrder(Helpers, eRealm.Albion, marched + 61_000), Is.Null,
                "after the column has arrived, helpers fight as battle participants");
            Assert.That(AutonomousRvrEventLayer.NoteAllyJoined(id, eRealm.Albion, marched + 62_000), Is.False,
                "late helpers do not start a second muster");
            Assert.That(AutonomousRvrEventLayer.IsBattleForce(Helpers, marched + 62_000), Is.True);
        }

        [Test]
        public void PlannedSiegeOrdersAreNotAllyColumns()
        {
            long now = GameLoop.GameLoopTime + 100;
            AutonomousRvrEventLayer.StagedAssault = true;
            var keep = Relic("rvr-keep-9002") with { IsRelicKeep = false, Kind = AutonomousRvrEventLayer.Intent.AssaultKeep };
            Assert.That(AutonomousRvrEventLayer.ForceStart(keep, eRealm.Albion, now, out _), Is.True);
            Register(keep.Id, "Attackers", Helpers, 3);
            var order = AutonomousRvrEventLayer.GetRallyOrder(Helpers, eRealm.Albion, now + 1000);
            Assert.That(order, Is.Not.Null);
            Assert.That(order.HomeMuster, Is.True);
            Assert.That(order.AllyColumn, Is.False);
            Assert.That(AutonomousRvrEventLayer.HasAllyColumn(keep.Id), Is.False);
            Assert.That(AutonomousRvrEventLayer.NoteAllyJoined(keep.Id, eRealm.Albion, now), Is.False);
        }

        [TestCase(8, 6)]
        [TestCase(16, 12)]
        [TestCase(48, 38)]
        [TestCase(96, 76)]
        public void ColumnMarchesOnceMostOfTheHelpersCalledHaveGathered(int called, int needed)
        {
            Assert.That(AutonomousRvrEventLayer.AllyWaveSize(called), Is.EqualTo(needed));
        }

        [Test]
        public void HelperCapsAreUnchanged()
        {
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false, AutonomousRvrEventLayer.DefenseScale.Small), Is.EqualTo(8));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false, AutonomousRvrEventLayer.DefenseScale.Standard), Is.EqualTo(16));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false, AutonomousRvrEventLayer.DefenseScale.Large), Is.EqualTo(48));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false, AutonomousRvrEventLayer.DefenseScale.Massive), Is.EqualTo(96));
        }

        // inKeepRegion, toKeep, toMuster, marching => straight in
        [TestCase(true, 5_000d, 130_000d, false, true, TestName = "AllyNearTheKeepWhileOthersGatherGoesStraightIn")]
        [TestCase(true, 12_000d, 130_000d, true, true, TestName = "AllyAtTheDirectRadiusGoesStraightIn")]
        [TestCase(true, 12_001d, 130_000d, true, false, TestName = "AllyMarchingAndStillFarStaysInTheColumn")]
        [TestCase(true, 40_000d, 90_000d, false, true, TestName = "AllyGatheringNearerTheKeepThanTheMusterGoesStraightIn")]
        [TestCase(true, 90_000d, 40_000d, false, false, TestName = "AllyGatheringNearerTheMusterGoesToTheMuster")]
        [TestCase(true, 40_000d, 90_000d, true, false, TestName = "AllyMarchingOnlyTheDirectRadiusCounts")]
        [TestCase(false, 0d, 0d, false, false, TestName = "AllyInAnotherRegionTravelsToTheMuster")]
        [TestCase(true, 30_000d, double.PositiveInfinity, false, true, TestName = "AllyInTheKeepRegionWithMusterElsewhereGoesStraightIn")]
        public void HelperNearTheKeepSkipsTheColumn(bool inKeepRegion, double toKeep, double toMuster, bool marching, bool expected)
        {
            Assert.That(AutonomousRvrEventLayer.AllyDirectRadius, Is.EqualTo(12_000));
            Assert.That(AutonomousRvrEventLayer.AllyGoesStraightIn(inKeepRegion, toKeep, toMuster, marching), Is.EqualTo(expected));
        }
    }
}
