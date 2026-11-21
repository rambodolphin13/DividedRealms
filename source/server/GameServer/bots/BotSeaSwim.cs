using System.Numerics;
using DOL.GS.Movement;

namespace DOL.GS
{
    /// <summary>
    /// How a bot moves in the Atlantis sea zones (DR-423). Their navmesh is a flat sheet on the water surface with no
    /// sea bed, so a path to a monster on the sea floor ended on the surface above it: a companion tank summoned on top
    /// never moved, and its /pull ended "could not make contact". A bot in the water swims straight to a place under
    /// it, and a bot under water swims straight to wherever it is going; everything else stays on the mesh.
    /// </summary>
    public static class BotSeaSwim
    {
        public enum Route
        {
            /// <summary>Path on the navmesh as anywhere else.</summary>
            Mesh,
            /// <summary>Swim in a straight line.</summary>
            Straight,
            /// <summary>On land with the goal under water: path to the water surface above the goal first.</summary>
            SurfaceAbove
        }

        /// <summary>The surface sheet lies near 7997; a point this far from it still snaps onto it.</summary>
        public const float SurfaceSnapDistance = 128f;
        private const float SurfaceHeight = Pathfinder.ATLANTIS_SEA_LEVEL + 47;

        /// <summary>Pure. <paramref name="fromInWater"/>: the bot swims, on top of the water or under it.</summary>
        public static Route Choose(bool fromUnderSea, bool fromInWater, bool toUnderSea)
        {
            if (fromUnderSea)
                return Route.Straight;
            if (!toUnderSea)
                return Route.Mesh;
            return fromInWater ? Route.Straight : Route.SurfaceAbove;
        }

        public static Route Choose(Zone zone, GameBot bot, Vector3 from, Vector3 to)
        {
            if (zone == null || !Pathfinder.IsAtlantisSeaZone(zone.ID) || !zone.IsPathfindingEnabled ||
                (bot.Flags & (GameNPC.eFlags.FLYING | GameNPC.eFlags.SWIMMING)) != 0)
                return Route.Mesh;
            bool fromUnderSea = IsUnderSea(zone, from);
            bool toUnderSea = IsUnderSea(zone, to);
            if (!fromUnderSea && !toUnderSea)
                return Route.Mesh;
            return Choose(fromUnderSea, fromUnderSea || BotPlayerPresentation.Swimming(bot) || IsUnderSea(zone, FeetProbe(from)), toUnderSea);
        }

        /// <summary>
        /// A bot on the surface sheet (near 7997) stands above the drawn water (under 7970) and the sheet carries no
        /// swim polygons, so neither swim test sees it: it is in the water when the sea is this close under its feet.
        /// </summary>
        public const float WadeDepth = 60f;

        /// <summary>The point under a bot's feet that tells whether it floats on the sea. Pure.</summary>
        public static Vector3 FeetProbe(Vector3 position) => new(position.X, position.Y, position.Z - WadeDepth);

        /// <summary>Under the surface sheet, in water the client draws (not a dry hollow below sea level).</summary>
        public static bool IsUnderSea(Zone zone, Vector3 point) =>
            IsUnderSea(zone.ID, point.Z, zone.IsUnderwater((int)point.X, (int)point.Y, (int)point.Z));

        /// <summary>Pure.</summary>
        public static bool IsUnderSea(ushort zoneId, float z, bool underwater) =>
            underwater && Pathfinder.IsAtlantisSeaZone(zoneId) && z < Pathfinder.ATLANTIS_SEA_LEVEL;

        /// <summary>The point on the water surface straight above a place under the sea. Pure.</summary>
        public static Vector3 SurfaceAbove(Vector3 underSea) => new(underSea.X, underSea.Y, SurfaceHeight);
    }
}
