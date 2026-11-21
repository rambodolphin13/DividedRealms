using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// DR-395: where a player's casters and healers stand in a Master Level trial fight. They used to stand in their follow
    /// slots around the player, who stands on the boss, and went back there every time the player's target died, so every
    /// area attack hit the whole party (ML 2.10: the spikes for four or more near Runihura fired every six seconds of the
    /// fight; ML 1.1: four dead in one fireball). Each now takes its own spot on a ring around the trial's boss: outside
    /// the point blank pulses, apart from the others, and still in spell, heal and resurrection range of the fight.
    /// Melee companions stay on the monster. The rules here are pure; BotBrain.TrialStandOff applies them.
    /// </summary>
    public static class TrialStandOff
    {
        /// <summary>Ring radius: trial pulses reach 350 to 700 from their monster, Aithos' fireball 400 from its victim.</summary>
        public const int Radius = 750;

        /// <summary>The ground stopped the spot nearer than this to the boss (a wall, a ledge): not worth the walk.</summary>
        public const int MinimumRadius = 450;

        /// <summary>The ring radii tried in turn until a spot has ground and a line of sight to the boss.</summary>
        public static readonly int[] Radii = { Radius, 600, MinimumRadius };

        /// <summary>Most degrees between two neighbours; 750 out, that is 750 apart.</summary>
        public const double MaximumSpacingDegrees = 60;

        /// <summary>Nearer than this to its spot the companion stands and fights.</summary>
        public const int ArrivalTolerance = 150;

        /// <summary>The boss moved this far from where the ring was laid out: lay it out again.</summary>
        public const int ReanchorDistance = 500;

        /// <summary>Trial monsters, and companions, farther than this from the player are not in this fight.</summary>
        public const int FightRadius = 2000;

        /// <summary>The spots are held only while the player is fighting. Pure.</summary>
        public static bool LeaderFights(bool inCombat, bool attacking) => inCombat || attacking;

        public readonly record struct Mob(string Name, int MaxHealth);

        /// <summary>
        /// The monster the fight is spread around: among the living trial monsters the one with the most health whose name
        /// no other shares (the boss; packs of cobras, statues or vipers share theirs), or -1 when there is none. Pure.
        /// </summary>
        public static int AnchorIndex(IReadOnlyList<Mob> mobs)
        {
            int best = -1;
            for (int i = 0; i < mobs.Count; i++)
            {
                bool shared = false;
                for (int j = 0; j < mobs.Count && !shared; j++)
                    shared = j != i && string.Equals(mobs[j].Name, mobs[i].Name, StringComparison.OrdinalIgnoreCase);
                if (!shared && (best < 0 || mobs[i].MaxHealth > mobs[best].MaxHealth))
                    best = i;
            }
            return best;
        }

        /// <summary>
        /// The bearing from the boss of slot <paramref name="slot"/> of <paramref name="slots"/>: fanned evenly either
        /// side of <paramref name="approachDegrees"/> (the side the party came from), at most
        /// <see cref="MaximumSpacingDegrees"/> apart, the whole ring when there are more than six. Pure.
        /// </summary>
        public static double BearingDegrees(double approachDegrees, int slot, int slots)
        {
            slots = Math.Max(1, slots);
            double spacing = Math.Min(MaximumSpacingDegrees, 360d / slots);
            double bearing = approachDegrees + (Math.Clamp(slot, 0, slots - 1) - (slots - 1) / 2d) * spacing;
            bearing %= 360;
            return bearing < 0 ? bearing + 360 : bearing;
        }

        /// <summary>Two companions' spots are at least this far apart: Aithos' fireball reaches 400 from its victim.</summary>
        public const int MinimumSeparation = 400;

        /// <summary>
        /// The bearings tried for a spot, nearest the companion's own first: its own, then 30 degrees to either side, 60,
        /// and so on round to the far side of the boss (twelve in all). Walls, ledges and a blocked line of sight rule
        /// most of a temple room out, so one bearing alone often had no spot (ML 2.10 sandbox run, 2026-11-21). Pure.
        /// </summary>
        public static IEnumerable<double> CandidateBearings(double own)
        {
            yield return Normalize(own);
            for (int step = 30; step < 180; step += 30)
            {
                yield return Normalize(own + step);
                yield return Normalize(own - step);
            }
            yield return Normalize(own + 180);
        }

        private static double Normalize(double degrees)
        {
            degrees %= 360;
            return degrees < 0 ? degrees + 360 : degrees;
        }

        /// <summary>Whether a spot is clear of the spots other companions already hold. Pure.</summary>
        public static bool ClearOfOthers(Vector2 spot, IEnumerable<Vector2> taken)
        {
            foreach (Vector2 other in taken)
                if (Vector2.Distance(spot, other) < MinimumSeparation)
                    return false;
            return true;
        }

        /// <summary>The point <paramref name="radius"/> from <paramref name="centre"/> on that bearing. Pure.</summary>
        public static Vector2 Spot(Vector2 centre, double bearingDegrees, int radius)
        {
            double radians = bearingDegrees * Math.PI / 180d;
            return new(centre.X + (float)(Math.Cos(radians) * radius), centre.Y + (float)(Math.Sin(radians) * radius));
        }

        /// <summary>The bearing of <paramref name="to"/> seen from <paramref name="from"/>, in degrees (0 when they coincide). Pure.</summary>
        public static double Bearing(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            if (delta.LengthSquared() < 1)
                return 0;
            double degrees = Math.Atan2(delta.Y, delta.X) * 180d / Math.PI;
            return degrees < 0 ? degrees + 360 : degrees;
        }

        /// <summary>
        /// Who takes a spot: a player's own companion in the player's group that fights from range or is the party's
        /// healer, unless the player told it to stay somewhere (/bots stay). Gamebots and melee companions never. Pure.
        /// </summary>
        public static bool Applies(bool playerCompanion, bool playerLed, bool alive, bool fightsAtRange, bool support, bool heldByStayOrder) =>
            playerCompanion && playerLed && alive && (fightsAtRange || support) && !heldByStayOrder;

        /// <summary>
        /// Whether the companion walks to its spot now: not while casting, not when it is as good as there, and not when
        /// the monster it is attacking would be out of its spell range or out of view from there (it would only walk
        /// back). Pure.
        /// </summary>
        public static bool WalksToSpot(bool casting, double distanceToSpot, double spotToAttackTarget, int spellRange, bool targetInViewFromSpot = true) =>
            !casting && distanceToSpot > ArrivalTolerance && (spellRange <= 0 || spotToAttackTarget <= spellRange - 50 && targetInViewFromSpot);

        /// <summary>A companion without a spot looks again once the boss has moved this far.</summary>
        public const int RetryDistance = 150;

        /// <summary>
        /// The ring is laid out again for a new boss, for one that has moved <see cref="ReanchorDistance"/>, and for a
        /// companion whose spot the boss has come within <see cref="MinimumRadius"/> of (a tank drags a boss about: on
        /// the 2026-11-21 sandbox run Runihura moved 400 and stood 190 from a Wizard's spot). A boss that is after this
        /// very companion does not move its spot: it would only be chased from spot to spot. Without a spot it looks
        /// again when the boss has moved <see cref="RetryDistance"/>. Pure.
        /// </summary>
        public static bool NeedsNewLayout(bool sameBoss, double bossMovedFromCentre, bool hasSpot = true,
            double spotToBoss = double.MaxValue, bool bossIsAfterMe = false) =>
            !sameBoss || bossMovedFromCentre > ReanchorDistance ||
            (hasSpot ? spotToBoss < MinimumRadius && !bossIsAfterMe : bossMovedFromCentre > RetryDistance);
    }
}
