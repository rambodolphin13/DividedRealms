using DOL.GS;
using DOL.GS.Movement;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_AtlantisTravel
    {
        [TestCase((ushort) 73, 1049, true, Description = "Hesperos seabed")]
        [TestCase((ushort) 31, 7000, true, Description = "Midgard's copy of Mesothalassa")]
        [TestCase((ushort) 134, 3299, true, Description = "Hibernia's copy of Anatole")]
        [TestCase((ushort) 73, 8112, false, Description = "An island above the sea")]
        [TestCase((ushort) 82, 7112, false, Description = "Land of Atum lies below sea level but is land")]
        [TestCase((ushort) 81, 6418, false, Description = "Stygian Delta is land")]
        [TestCase((ushort) 1, 1000, false, Description = "Not Atlantis")]
        public void OnlySeaZoneSpawnsBelowTheSurfaceStayOffTheMesh(ushort zone, int z, bool expected)
        {
            Assert.That(Pathfinder.IsBelowAtlantisSea(zone, z), Is.EqualTo(expected));
        }

        [TestCase(444485, 551238, true, Description = "Dockmaster on the pier")]
        [TestCase(448007, 556030, true, Description = "Dockmistress set back from the pier")]
        [TestCase(460000, 551238, false, Description = "Too far away")]
        [TestCase(444485, 570000, false, Description = "Too far away")]
        public void RouteStartsWithinReachOfItsDockmaster(int x, int y, bool expected)
        {
            PathPoint start = new(442288, 552070, 7970, 1000, default);

            Assert.That(GameBoatStableMaster.IsRouteStartInReach(start, x, y), Is.EqualTo(expected));
        }

        [Test]
        public void NoRouteIsNeverInReach()
        {
            Assert.That(GameBoatStableMaster.IsRouteStartInReach(null, 0, 0), Is.False);
        }
    }
}
