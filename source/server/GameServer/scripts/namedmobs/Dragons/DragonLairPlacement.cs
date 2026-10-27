using System.Collections.Concurrent;

namespace DOL.GS
{
    /// <summary>Lair anchors verified against the client mound geometry (not terrain beneath it).
    /// Flight coordinates are transient, never a spawn/home location.</summary>
    public static class DragonLairPlacement
    {
        /// <summary>How far a dragon's saved row may sit from its anchor and still set the lair height. The retail
        /// 1.130 world's mounds stand about 170 units lower than the classic world's.</summary>
        public const int AdoptRadius = 300;
        public const int AdoptHeight = 600;

        private static readonly ConcurrentDictionary<eRealm, int> _height = new();

        public static Point3D Home(eRealm realm)
        {
            Point3D anchor = Anchor(realm);
            return _height.TryGetValue(realm, out int z) ? new Point3D(anchor.X, anchor.Y, z) : anchor;
        }

        private static Point3D Anchor(eRealm realm) => realm switch
        {
            eRealm.Albion => new Point3D(391326, 755351, 391),
            eRealm.Midgard => new Point3D(708811, 1021459, 3014),
            eRealm.Hibernia => new Point3D(408646, 706432, 2965),
            _ => throw new System.ArgumentOutOfRangeException(nameof(realm))
        };

        /// <summary>Takes the lair height from the dragon's database row when that row stands on its lair, so each
        /// world (classic or retail 1.130) keeps its own floor. Returns whether the height was taken.</summary>
        public static bool Adopt(eRealm realm, int x, int y, int z)
        {
            Point3D anchor = Anchor(realm);
            long dx = x - anchor.X, dy = y - anchor.Y;
            if (dx * dx + dy * dy > (long)AdoptRadius * AdoptRadius || System.Math.Abs(z - anchor.Z) > AdoptHeight)
                return false;
            _height[realm] = z;
            return true;
        }

        public static void Forget(eRealm realm) => _height.TryRemove(realm, out _);

        public static void Restore(GameNPC dragon, eRealm realm)
        {
            Point3D home = Home(realm);
            dragon.X = home.X;
            dragon.Y = home.Y;
            dragon.Z = home.Z;
            dragon.Flags = GroundFlags(dragon.Flags);
        }

        public static GameNPC.eFlags GroundFlags(GameNPC.eFlags flags) => flags & ~GameNPC.eFlags.FLYING;
        public static bool CanReceiveDamageFrom(GameObject source) => source != null && CanReceiveDamageType(source.GetType());
        public static bool CanReceiveDamageType(System.Type type) => type != null && typeof(GameLiving).IsAssignableFrom(type);
    }
}
