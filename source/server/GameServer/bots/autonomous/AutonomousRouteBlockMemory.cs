using System.Collections.Concurrent;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// The monsters a bot or its party gave up a camp for, and how far around them to stay: for
    /// <see cref="CooldownMilliseconds"/> the camp picker skips any camp whose straight line from the bot passes inside
    /// that reach. The give-up memory (AutonomousFailedGoalMemory) only blocks 200 units around the old camp, and a
    /// party that gave up a giant spider camp for a goblin pack picked the next giant spider spawn 1,000 units on and
    /// walked back into the same goblins: on three 14 minute private copy runs (2026-11-02) 13, 57 and 45 low level
    /// party members died within 2,500 units of a camp their party had given up in the last five minutes.
    /// </summary>
    public static class AutonomousRouteBlockMemory
    {
        public const long CooldownMilliseconds = 15 * 60_000;
        /// <summary>The reach kept around a blocker at least: about one aggro range plus the retreat clearance.</summary>
        public const float MinimumRadius = 800f;
        private const int SlotsPerOwner = 4;
        private const int SoftOwnerCap = 2_048;

        private struct Entry
        {
            public ushort Region;
            public Vector2 At;
            public float Radius;
            public long Until;
        }

        private static readonly ConcurrentDictionary<long, Entry[]> Owners = new();

        public static void Note(long ownerKey, ushort region, Vector2 at, float radius, long now)
        {
            if (ownerKey == 0 || region == 0)
                return;
            if (Owners.Count > SoftOwnerCap)
                Prune(now);
            Entry[] entries = Owners.GetOrAdd(ownerKey, _ => new Entry[SlotsPerOwner]);
            radius = System.Math.Max(radius, MinimumRadius);
            lock (entries)
            {
                int slot = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Until > now && entries[i].Region == region &&
                        Vector2.Distance(entries[i].At, at) <= entries[i].Radius)
                    {
                        slot = i; // the same blocker again: refresh it
                        break;
                    }
                    if (entries[i].Until < entries[slot].Until)
                        slot = i; // else replace the oldest
                }
                entries[slot] = new Entry { Region = region, At = at, Radius = radius, Until = now + CooldownMilliseconds };
            }
        }

        /// <summary>Whether the straight way from <paramref name="from"/> to <paramref name="to"/> passes a remembered blocker.</summary>
        public static bool Crosses(long ownerKey, ushort region, Vector2 from, Vector2 to, long now)
        {
            if (ownerKey == 0 || !Owners.TryGetValue(ownerKey, out Entry[] entries))
                return false;
            lock (entries)
            {
                foreach (Entry entry in entries)
                    if (entry.Until > now && entry.Region == region &&
                        AutonomousRouteThreatPolicy.DistanceToSegment(from, to, entry.At) < entry.Radius)
                        return true;
            }
            return false;
        }

        public static bool HasAny(long ownerKey, long now)
        {
            if (ownerKey == 0 || !Owners.TryGetValue(ownerKey, out Entry[] entries))
                return false;
            lock (entries)
            {
                foreach (Entry entry in entries)
                    if (entry.Until > now)
                        return true;
            }
            return false;
        }

        public static void Clear() => Owners.Clear();

        private static void Prune(long now)
        {
            foreach (var pair in Owners)
            {
                bool live = false;
                lock (pair.Value)
                    foreach (Entry entry in pair.Value)
                        live |= entry.Until > now;
                if (!live)
                    Owners.TryRemove(pair.Key, out _);
            }
        }
    }
}
