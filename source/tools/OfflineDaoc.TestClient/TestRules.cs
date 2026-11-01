using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OfflineDaoc.TestClient
{
    // Pure rules of the tools, linked into the server's Tests project.
    public sealed class WalkWatch
    {
        public const float MinStep = 10f;
        public const long StallMs = 8000;

        Vector3 _from;
        long _since;

        public WalkWatch(Vector3 start, long now)
        {
            _from = start;
            _since = now;
        }

        // True once the character has gone 8 seconds without walking anywhere: stuck, rooted or out of mesh. Distance
        // to the goal says nothing here, as a long route can lead away from it first (Fomor, 2026-10-31).
        public bool Stalled(Vector3 here, long now)
        {
            if (Vector3.Distance(here, _from) >= MinStep)
            {
                _from = here;
                _since = now;
                return false;
            }
            return now - _since > StallMs;
        }
    }

    public static class KitRules
    {
        // Spec lines and the eObjectType they use.
        static readonly Dictionary<string, int> SpecWeapons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Slash"] = 3, ["Crush"] = 2, ["Thrust"] = 4, ["Two Handed"] = 6, ["Polearm"] = 7, ["Staff"] = 8, ["Flexible"] = 24,
            ["Sword"] = 11, ["Hammer"] = 12, ["Axe"] = 13, ["Spear"] = 14,
            ["Blades"] = 19, ["Blunt"] = 20, ["Piercing"] = 21, ["Large Weapons"] = 22, ["Celtic Spear"] = 23, ["Scythe"] = 26,
            ["Hand to Hand"] = 25, ["Fist Wraps"] = 27, ["Mauler Staff"] = 28,
        };

        // The weapon for the first weapon line that gets trained, 0 if none is. A Paladin may use two-handed weapons, but
        // one trained in Crush fights with a crushing weapon.
        public static int WeaponForSpecs(IEnumerable<string> specs)
        {
            foreach (string spec in specs)
            {
                if (SpecWeapons.TryGetValue(spec, out int type))
                    return type;
            }
            return 0;
        }
    }
}
