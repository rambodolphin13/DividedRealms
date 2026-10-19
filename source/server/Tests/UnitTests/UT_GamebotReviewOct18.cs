using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Autonomous gamebot review, 2026-10-18.</summary>
[TestFixture]
public sealed class UT_GamebotReviewOct18
{
    [Test]
    public void BattlegroundKeepDefendersHoldingTheirPostAreNotMovementStalls()
    {
        eAutonomousProgressKind hold = AutonomousStuckWatchdog.DefensivePostProgress(eAutonomousObjectiveKind.Battleground);
        Assert.That(AutonomousStuckWatchdog.RefreshesMovementClock(hold), Is.True);
        // Holding a post is not an outcome: a forty-five-minute quiet hold is still recovered.
        Assert.That(AutonomousStuckWatchdog.CountsAsGoalProgress(hold), Is.False);
    }

    [TestCase(eAutonomousObjectiveKind.RvR)]
    [TestCase(eAutonomousObjectiveKind.SoloPve)]
    [TestCase(eAutonomousObjectiveKind.GroupPve)]
    public void OtherDefendersKeepTheirExistingWatchdogRules(eAutonomousObjectiveKind kind)
    {
        eAutonomousProgressKind hold = AutonomousStuckWatchdog.DefensivePostProgress(kind);
        Assert.That(hold, Is.EqualTo(eAutonomousProgressKind.Objective));
        Assert.That(AutonomousStuckWatchdog.RefreshesMovementClock(hold), Is.False);
    }

    [Test]
    public void MovementClockKindsAreUnchanged()
    {
        Assert.That(AutonomousStuckWatchdog.RefreshesMovementClock(eAutonomousProgressKind.Movement), Is.True);
        Assert.That(AutonomousStuckWatchdog.RefreshesMovementClock(eAutonomousProgressKind.StableTravel), Is.True);
        Assert.That(AutonomousStuckWatchdog.RefreshesMovementClock(eAutonomousProgressKind.Recovery), Is.True);
        Assert.That(AutonomousStuckWatchdog.RefreshesMovementClock(eAutonomousProgressKind.SiegeParticipation), Is.True);
        Assert.That(AutonomousStuckWatchdog.RefreshesMovementClock(eAutonomousProgressKind.Combat), Is.False);
        Assert.That(AutonomousStuckWatchdog.RefreshesMovementClock(eAutonomousProgressKind.Objective), Is.False);
    }

    [Test]
    public void MedallionMerchantWaitsOnlyWhileASaleMadeRoom()
    {
        Assert.That(AutonomousFrontierTransport.KeepWaitingForMedallionRoom(backpackFull: true, soldTrash: true), Is.True);
        // Nothing sellable: fall through to the walking itinerary instead of holding until the watchdog.
        Assert.That(AutonomousFrontierTransport.KeepWaitingForMedallionRoom(backpackFull: true, soldTrash: false), Is.False);
        Assert.That(AutonomousFrontierTransport.KeepWaitingForMedallionRoom(backpackFull: false, soldTrash: false), Is.False);
    }

    [TestCase(eAutonomousObjectiveKind.SoloPve, false, true)]
    [TestCase(eAutonomousObjectiveKind.GroupPve, false, true)]
    [TestCase(eAutonomousObjectiveKind.GroupPve, true, false)]
    [TestCase(eAutonomousObjectiveKind.SoloPve, true, true)]
    [TestCase(eAutonomousObjectiveKind.RvR, false, false)]
    [TestCase(eAutonomousObjectiveKind.Battleground, false, false)]
    public void FirstPveAssignmentKeepsTheCampTheBotIsAlreadyWalkingTo(eAutonomousObjectiveKind kind, bool dungeon, bool keeps)
    {
        Assert.That(AutonomousObjectiveAssignments.KeepsCampOnFirstAssignment(string.Empty, kind,
            betweenTasks: false, grouped: false, hasCamp: true, campIsDungeon: dungeon), Is.EqualTo(keeps));
    }

    [Test]
    public void AnUnreachableFlyerIsLetGoAfterThirtySecondsWithoutContact()
    {
        const long start = 1_000_000;
        long limit = AutonomousPveTargetPolicy.UnreachableFlyerAggroDropMilliseconds;
        Assert.That(AutonomousPveTargetPolicy.ShouldDropUnreachableFlyer(true, false, start, start + limit - 1), Is.False);
        Assert.That(AutonomousPveTargetPolicy.ShouldDropUnreachableFlyer(true, false, start, start + limit), Is.True);
        // A landed or received blow is a real fight; a reachable target is never dropped.
        Assert.That(AutonomousPveTargetPolicy.ShouldDropUnreachableFlyer(true, true, start, start + 10 * limit), Is.False);
        Assert.That(AutonomousPveTargetPolicy.ShouldDropUnreachableFlyer(false, false, start, start + 10 * limit), Is.False);
    }

    [Test]
    public void OnlyRealDamageCountsAsContactWithAFlyer()
    {
        // A bow draw or a pet's swing stamps LastAttackTick without reaching anything; it is not an input.
        Assert.That(AutonomousPveTargetPolicy.FlyerFightContact(100, 100, 5000, 5000), Is.False);
        Assert.That(AutonomousPveTargetPolicy.FlyerFightContact(100, 101, 5000, 5000), Is.True);
        Assert.That(AutonomousPveTargetPolicy.FlyerFightContact(100, 100, 5000, 4999), Is.True);
    }

    [Test]
    public void AFightWithNoDamageEitherWayIsDroppedAfterAMinute()
    {
        const long start = 1_000_000;
        long minute = AutonomousPveTargetPolicy.StaleFightDropMilliseconds;
        long flyer = AutonomousPveTargetPolicy.UnreachableFlyerAggroDropMilliseconds;
        Assert.That(AutonomousPveTargetPolicy.ShouldDropStaleFight(true, false, false, start, start + minute - 1), Is.False);
        Assert.That(AutonomousPveTargetPolicy.ShouldDropStaleFight(true, false, false, start, start + minute), Is.True);
        Assert.That(AutonomousPveTargetPolicy.ShouldDropStaleFight(true, true, false, start, start + flyer), Is.True);
        // Damage either way, or an RvR target, is never judged stale.
        Assert.That(AutonomousPveTargetPolicy.ShouldDropStaleFight(true, false, true, start, start + 10 * minute), Is.False);
        Assert.That(AutonomousPveTargetPolicy.ShouldDropStaleFight(false, false, false, start, start + 10 * minute), Is.False);
    }

    [Test]
    public void APartyLeaderThatMayNotPullPassesARouteMonsterAfterAShortHold()
    {
        const long start = 1_000_000;
        long hold = AutonomousRouteThreatPolicy.PartyHoldBeforePassMilliseconds;
        Assert.That(AutonomousRouteThreatPolicy.ShouldPassHeldThreat(start, start + hold - 1), Is.False);
        Assert.That(AutonomousRouteThreatPolicy.ShouldPassHeldThreat(start, start + hold), Is.True);
        Assert.That(hold, Is.LessThan(15 * 60_000), "must end well before the fifteen-minute watchdog");
    }

    [Test]
    public void GriffonGlidersHoveringOverTheCampFloorAreUnreachable()
    {
        // Gripklosa: a glider at Z 7482 over a Hunter rescued at Z 6677.
        Assert.That(AutonomousPveTargetPolicy.IsUnreachableFlyer(GameNPC.eFlags.FLYING, 7482, 6677), Is.True);
        Assert.That(AutonomousPveTargetPolicy.IsUnreachableFlyer(GameNPC.eFlags.FLYING, 6900, 6677), Is.False);
        Assert.That(AutonomousPveTargetPolicy.IsUnreachableFlyer(0, 7482, 6677), Is.False);
    }

    [Test]
    public void LongWaitingPartySeekersRecruitFromFurtherAway()
    {
        long early = AutonomousBotGroupCoordinator.ExtendedPveRecruitmentAfterMilliseconds - 1;
        long late = AutonomousBotGroupCoordinator.ExtendedPveRecruitmentAfterMilliseconds;
        Assert.That(AutonomousBotGroupCoordinator.PveRecruitmentDistance(0, early), Is.EqualTo(30_000));
        Assert.That(AutonomousBotGroupCoordinator.PveRecruitmentDistance(late, 0), Is.EqualTo(60_000));
        Assert.That(AutonomousBotGroupCoordinator.PveRecruitmentDistance(0, late), Is.EqualTo(60_000));
        Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(false, false, false, false, false, 45_000), Is.False);
        Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(false, false, false, false, false, 45_000,
            AutonomousBotGroupCoordinator.ExtendedPveRecruitmentDistance), Is.True);
        // Combat still blocks recruitment at any distance.
        Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(false, false, true, false, false, 0,
            AutonomousBotGroupCoordinator.ExtendedPveRecruitmentDistance), Is.False);
    }

    [Test]
    public void RealReassignmentsStillDropTheRoute()
    {
        // A change from one assignment to another is a real reassignment.
        Assert.That(AutonomousObjectiveAssignments.KeepsCampOnFirstAssignment("albion-0-4-SoloPve",
            eAutonomousObjectiveKind.SoloPve, false, false, true, false), Is.False);
        // Between-task services, party members and bots with no live camp replan as before.
        Assert.That(AutonomousObjectiveAssignments.KeepsCampOnFirstAssignment(string.Empty,
            eAutonomousObjectiveKind.SoloPve, betweenTasks: true, grouped: false, hasCamp: true, campIsDungeon: false), Is.False);
        Assert.That(AutonomousObjectiveAssignments.KeepsCampOnFirstAssignment(string.Empty,
            eAutonomousObjectiveKind.SoloPve, betweenTasks: false, grouped: true, hasCamp: true, campIsDungeon: false), Is.False);
        Assert.That(AutonomousObjectiveAssignments.KeepsCampOnFirstAssignment(null,
            eAutonomousObjectiveKind.SoloPve, betweenTasks: false, grouped: false, hasCamp: false, campIsDungeon: false), Is.False);
    }
}
