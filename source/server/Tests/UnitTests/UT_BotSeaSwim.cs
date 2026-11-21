using System.Numerics;
using DOL.GS;
using DOL.GS.Movement;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// DR-423: the Atlantis sea is meshed as a flat sheet on its surface. A companion tank on top of the water got a
    /// path that ended on the surface above a sea-floor boss, never moved, and /pull ended "could not make contact".
    /// </summary>
    [TestFixture]
    public class UT_BotSeaSwim
    {
        [Test]
        public void ABotOnTopOfTheWaterDivesStraightToASeaFloorTarget()
        {
            Assert.That(BotSeaSwim.Choose(fromUnderSea: false, fromInWater: true, toUnderSea: true), Is.EqualTo(BotSeaSwim.Route.Straight));
        }

        [Test]
        public void ABotUnderTheSeaSwimsStraightWhereverItGoes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotSeaSwim.Choose(true, true, true), Is.EqualTo(BotSeaSwim.Route.Straight), "to another place under the sea");
                Assert.That(BotSeaSwim.Choose(true, true, false), Is.EqualTo(BotSeaSwim.Route.Straight), "back up to its leader");
            });
        }

        [Test]
        public void ABotOnLandWalksToTheWaterAboveASeaFloorTargetFirst()
        {
            Assert.That(BotSeaSwim.Choose(fromUnderSea: false, fromInWater: false, toUnderSea: true), Is.EqualTo(BotSeaSwim.Route.SurfaceAbove));
        }

        [Test]
        public void OnTopOfTheWaterAndOnLandBotsKeepToTheMesh()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotSeaSwim.Choose(false, true, false), Is.EqualTo(BotSeaSwim.Route.Mesh), "swimming across the surface");
                Assert.That(BotSeaSwim.Choose(false, false, false), Is.EqualTo(BotSeaSwim.Route.Mesh), "on an island");
            });
        }

        [TestCase((ushort) 76, 5406f, true, true, Description = "Fool's Bow argo in its pit, Oceanus Notos")]
        [TestCase((ushort) 31, 3616f, true, true, Description = "Naxos Tidal Lord, Midgard's Mesothalassa")]
        [TestCase((ushort) 76, 7996f, false, false, Description = "A bot on the surface sheet")]
        [TestCase((ushort) 76, 7960f, true, false, Description = "Just under the surface: still within reach of the sheet")]
        [TestCase((ushort) 76, 7000f, false, false, Description = "A dry hollow below sea level is not sea")]
        [TestCase((ushort) 82, 7112f, true, false, Description = "Land of Atum's rivers have a real bed on the mesh")]
        [TestCase((ushort) 1, 1000f, true, false, Description = "A classic lake")]
        [TestCase((ushort) 164, 6431f, true, false, Description = "The Irish Sea keeps its own rule (DR-432)")]
        public void UnderTheSeaMeansDrawnWaterBelowTheSheetInAnAtlantisSeaZone(ushort zone, float z, bool underwater, bool expected)
        {
            Assert.That(BotSeaSwim.IsUnderSea(zone, z, underwater), Is.EqualTo(expected));
        }

        [Test]
        public void TheSurfacePointAboveASeaFloorTargetSnapsOntoTheSheet()
        {
            Vector3 surface = BotSeaSwim.SurfaceAbove(new Vector3(358680, 623324, 5406));
            Assert.Multiple(() =>
            {
                Assert.That(surface.X, Is.EqualTo(358680f));
                Assert.That(surface.Y, Is.EqualTo(623324f));
                Assert.That(surface.Z, Is.GreaterThan(Pathfinder.ATLANTIS_SEA_LEVEL));
                Assert.That(System.Math.Abs(surface.Z - 7997f), Is.LessThan(BotSeaSwim.SurfaceSnapDistance));
            });
        }

        [TestCase(7996f, true, Description = "On the surface sheet: sandbox 2026-11-21, the tank stayed here above the argo")]
        [TestCase(7977f, true, Description = "A little way down")]
        [TestCase(8040f, false, Description = "On an island beach")]
        public void ABotOnTheSurfaceSheetCountsAsInTheWater(float z, bool expected)
        {
            // Drawn water lies under 7970 (surface 8010 less ZoneWater.SurfaceMargin) and the sheet above 7950.
            float probe = BotSeaSwim.FeetProbe(new Vector3(359190, 621312, z)).Z;
            bool drawnWater = probe < 8010 - ZoneWater.SurfaceMargin;
            Assert.That(BotSeaSwim.IsUnderSea(76, probe, drawnWater), Is.EqualTo(expected));
        }

        [TestCase((ushort) 30, true)]
        [TestCase((ushort) 72, true)]
        [TestCase((ushort) 77, true)]
        [TestCase((ushort) 134, true)]
        [TestCase((ushort) 81, false, Description = "Stygian Delta")]
        [TestCase((ushort) 164, false, Description = "Irish Sea")]
        public void AtlantisSeaZones(ushort zone, bool expected)
        {
            Assert.That(Pathfinder.IsAtlantisSeaZone(zone), Is.EqualTo(expected));
        }
    }
}
