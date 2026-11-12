using System.Numerics;
using System.Runtime.CompilerServices;

using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Discord 2026-11-08: a raid inside a just-taken keep could not follow the player out (CompanionKeepDoors).</summary>
    public class UT_CompanionKeepDoors
    {
        private static readonly Vector3 Inside = new(1000, 1000, 500);
        private static readonly Vector3 Outside = new(1000, 1600, 500);

        [Test]
        public void PicksTheReachableDoorTowardTheLeader()
        {
            var doors = new[]
            {
                new CompanionKeepDoors.Candidate(new(1000, 700, 500), true, false),  // back side, away from the leader
                new CompanionKeepDoors.Candidate(new(1000, 1300, 500), true, false), // the gate toward the leader
            };
            Assert.That(CompanionKeepDoors.ChooseDoor(Inside, Outside, doors), Is.EqualTo(1));
        }

        [Test]
        public void NeverGoesBackThroughTheDoorJustPassed()
        {
            var doors = new[] { new CompanionKeepDoors.Candidate(new(1000, 1300, 500), true, true) };
            Assert.That(CompanionKeepDoors.ChooseDoor(Inside, Outside, doors), Is.EqualTo(-1));
        }

        [Test]
        public void SkipsUnreachableAndFarAboveDoors()
        {
            var doors = new[]
            {
                new CompanionKeepDoors.Candidate(new(1000, 1300, 500), false, false),
                new CompanionKeepDoors.Candidate(new(1000, 1300, 1200), true, false),
            };
            Assert.That(CompanionKeepDoors.ChooseDoor(Inside, Outside, doors), Is.EqualTo(-1));
        }

        [Test]
        public void InnerDoorFirstThenOuterGate()
        {
            // Lord room door at 1150, outer gate at 1400: the nearer one toward the leader comes first.
            var doors = new[]
            {
                new CompanionKeepDoors.Candidate(new(1000, 1400, 500), true, false),
                new CompanionKeepDoors.Candidate(new(1000, 1150, 500), true, false),
            };
            Assert.That(CompanionKeepDoors.ChooseDoor(Inside, Outside, doors), Is.EqualTo(1));
        }

        /// <summary>A floor at one point; found only inside the asked box.</summary>
        private sealed class Floor : PathfindingMgrBase
        {
            private readonly Vector3? _floor;
            public Floor(Vector3? floor) => _floor = floor;
            public override bool IsAvailable => true;
            public override bool HasNavmesh(Zone zone) => true;
            public override Vector3? GetClosestPointInBounds(Zone zone, Vector3 origin, Vector3 min, Vector3 max, EDtPolyFlags[] filters) =>
                _floor is { } f && f.X >= origin.X + min.X && f.X <= origin.X + max.X && f.Y >= origin.Y + min.Y &&
                f.Y <= origin.Y + max.Y && f.Z >= origin.Z + min.Z && f.Z <= origin.Z + max.Z ? f : null;
        }

        private static Zone AZone() => (Zone)RuntimeHelpers.GetUninitializedObject(typeof(Zone));

        [Test]
        public void ABotPassingADoorLandsOnTheGroundBelowIt()
        {
            // Discord 2026-11-12: the raid came out of the keep hanging in the air at the door's height.
            Assert.That(CompanionKeepDoors.FloorZ(new Floor(new(1000, 1600, 380)), AZone(), 1000, 1600, 500), Is.EqualTo(380));
        }

        [Test]
        public void NoFloorInReachKeepsTheDoorHeight()
        {
            Assert.That(CompanionKeepDoors.FloorZ(new Floor(null), AZone(), 1000, 1600, 500), Is.EqualTo(500));
            Assert.That(CompanionKeepDoors.FloorZ(new Floor(new(1000, 1600, -400)), AZone(), 1000, 1600, 500), Is.EqualTo(500),
                "a floor far below (a cellar, the moat) is not taken");
            Assert.That(CompanionKeepDoors.FloorZ(new Floor(new(1000, 1600, 900)), AZone(), 1000, 1600, 500), Is.EqualTo(500),
                "never up onto a wall walk");
        }
    }
}
