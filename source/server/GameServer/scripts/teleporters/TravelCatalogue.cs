using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.Database;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS.Scripts
{
    /// <summary>
    /// The travel catalogue every realm teleporter offers under [anywhere else] (owner 2026-11-15, after Gaheris's
    /// "one teleporter, the whole world on it"). The places are teleport rows, one per realm:
    /// <list type="bullet">
    /// <item><c>Travel</c> rows: places in the player's own lands, neutral places, and that realm's own side of a
    /// realm-war area (its New Frontiers border keeps and town, its Darkness Falls, Passage of Conflict and
    /// Labyrinth entrances). An Albion player never has a row into a Midgard or Hibernia side.</item>
    /// <item><c>TravelCoop</c> rows: another realm's lands. Offered only while the co-op world is on.</item>
    /// </list>
    /// New Frontiers keeps are added live: every keep the player's realm holds that has not been attacked in the
    /// last five minutes. Battlegrounds stay on the existing [Battlegrounds] choice (level brackets).
    /// </summary>
    public static class TravelCatalogue
    {
        public const string OwnType = "Travel";
        public const string CoopType = "TravelCoop";
        public const string MenuWord = "anywhere else";
        public const string MenuLine = "\n\nOr I can open the way to [anywhere else] in the realms.";
        public const int NewFrontiersRegion = 163;

        private static readonly string[] Families =
        {
            "Cities", "Albion", "Midgard", "Hibernia", "Shrouded Isles", "Atlantis", "Catacombs",
            "New Frontiers", "Darkness Falls", "Dungeons", "Elsewhere",
        };

        private static readonly HashSet<ushort> Cities = [10, 101, 201];
        private static readonly HashSet<ushort> ShroudedIsles = [50, 51, 150, 151, 180, 181];
        private static readonly HashSet<ushort> Atlantis = [30, 45, 46, 47, 70, 71, 72, 73, 88, 89, 90, 130, 145, 146, 147];
        private static readonly HashSet<ushort> Catacombs =
        [
            58, 59, 63, 65, 66, 67, 68, 69, 92, 93, 94, 95, 96, 97, 98, 99, 109, 148, 149, 162, 188, 189, 195, 196, 197,
            226, 227, 228, 229, 243, 335, 339, 341, 376, 390, 429, 430, 433, 435, 436,
        ];
        private static readonly HashSet<ushort> RealmWar = [163, 233, 244, 245];
        private static readonly HashSet<ushort> Dungeons =
        [
            20, 21, 22, 23, 24, 60, 61, 120, 121, 122, 125, 126, 127, 128, 129, 160, 161, 190, 191, 220, 221, 222, 223,
            224, 246, 247, 248, 276, 277,
        ];

        /// <summary>Which heading a place is listed under, by region (the zones table has no realm to go on).</summary>
        public static string FamilyOf(ushort region)
        {
            if (Cities.Contains(region)) return "Cities";
            if (ShroudedIsles.Contains(region)) return "Shrouded Isles";
            if (Atlantis.Contains(region)) return "Atlantis";
            if (Catacombs.Contains(region)) return "Catacombs";
            if (RealmWar.Contains(region)) return "New Frontiers";
            if (region == 249) return "Darkness Falls";
            if (Dungeons.Contains(region)) return "Dungeons";
            if (region < 100) return "Albion";
            if (region < 200) return "Midgard";
            if (region < 300) return "Hibernia";
            return "Elsewhere";
        }

        /// <summary>One place a player may be sent, with the heading it is listed under.</summary>
        public sealed record Stop(string Family, DbTeleport Destination);

        /// <summary>
        /// Every place offered to <paramref name="realm"/> now: its own rows, other realms' lands while the co-op world
        /// is on, and the New Frontiers keeps it holds.
        /// </summary>
        public static List<Stop> StopsFor(eRealm realm, bool liveKeeps = true)
        {
            var stops = new List<Stop>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(DbTeleport row)
            {
                if (row == null || row.RegionID <= 0 || string.IsNullOrWhiteSpace(row.TeleportID) || !names.Add(row.TeleportID)) return;
                stops.Add(new Stop(FamilyOf((ushort)row.RegionID), row));
            }
            foreach (DbTeleport row in WorldMgr.GetTeleportLocations(realm, OwnType))
                Add(row);
            if (HybridCoopRules.Enabled)
                foreach (DbTeleport row in WorldMgr.GetTeleportLocations(realm, CoopType))
                    Add(row);
            if (liveKeeps)
                foreach (DbTeleport keep in HeldKeeps(realm))
                    Add(keep);
            return stops;
        }

        /// <summary>The teleport rows a gamebot of <paramref name="realm"/> may plan with (no live keeps).</summary>
        public static IEnumerable<DbTeleport> PlannableFor(eRealm realm) => StopsFor(realm, false).Select(stop => stop.Destination);

        /// <summary>New Frontiers keeps (not towers) the realm holds and that are not under attack.</summary>
        public static IEnumerable<DbTeleport> HeldKeeps(eRealm realm)
        {
            ICollection<AbstractGameKeep> keeps = GameServer.KeepManager?.GetKeepsOfRegion(NewFrontiersRegion);
            if (keeps == null) yield break;
            foreach (AbstractGameKeep keep in keeps.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (keep == null || keep.KeepID >= 256 || keep.IsPortalKeep || keep.Realm != realm || keep.InCombat) continue;
                yield return new DbTeleport
                {
                    Type = OwnType, TeleportID = keep.Name, Realm = (int)realm, RegionID = keep.Region,
                    X = keep.X, Y = keep.Y, Z = keep.Z, Heading = keep.Heading,
                };
            }
        }

        /// <summary>
        /// Handles a word said to a teleporter: the [anywhere else] menu, a heading, or a place. True when the word
        /// was ours; <paramref name="destination"/> is set when the player should be sent.
        /// </summary>
        public static bool TryHandle(GameNPC teleporter, GamePlayer player, string text, out DbTeleport destination)
        {
            destination = null;
            if (teleporter == null || player == null || string.IsNullOrWhiteSpace(text)) return false;
            string said = text.Trim();
            List<Stop> stops = StopsFor(player.Realm);

            if (said.Equals(MenuWord, StringComparison.OrdinalIgnoreCase) || said.Equals("travel", StringComparison.OrdinalIgnoreCase))
            {
                var menu = new StringBuilder("Name a land and I will tell you where in it I can send you:\n");
                foreach (string family in Families)
                {
                    int count = Listed(teleporter, stops, family).Count();
                    if (count > 0) menu.Append($"\n  [{family}] ({count})");
                }
                Speak(teleporter, player, menu.ToString());
                return true;
            }

            string heading = Families.FirstOrDefault(f => f.Equals(said, StringComparison.OrdinalIgnoreCase));
            if (heading != null)
            {
                var places = Listed(teleporter, stops, heading).ToList();
                if (places.Count == 0)
                {
                    teleporter.SayTo(player, $"There is nowhere in {heading} I can send you.");
                    return true;
                }
                var list = new StringBuilder($"{heading}: name a place and I will send you.\n");
                foreach (Stop stop in places)
                    list.Append($"\n  [{stop.Destination.TeleportID}]");
                if (heading == "New Frontiers")
                    list.Append("\n\nI can reach the frontier keeps your realm holds, unless they are under attack.");
                list.Append($"\n\n  [{MenuWord}]");
                Speak(teleporter, player, list.ToString());
                return true;
            }

            Stop picked = stops.FirstOrDefault(stop => stop.Destination.TeleportID.Equals(said, StringComparison.OrdinalIgnoreCase));
            if (picked == null) return false;
            if (picked.Destination.RegionID == 249 && player.Client?.Account?.PrivLevel <= 1 &&
                !DFEnterJumpPoint.CanRealmEnter(player.Realm))
            {
                teleporter.SayTo(player, "Your realm does not hold the frontier keeps that open Darkness Falls.");
                return true;
            }
            destination = picked.Destination;
            return true;
        }

        /// <summary>The places under one heading, less the one the teleporter already stands at.</summary>
        private static IEnumerable<Stop> Listed(GameNPC teleporter, IEnumerable<Stop> stops, string family) =>
            stops.Where(stop => stop.Family == family &&
                                !(stop.Destination.RegionID == teleporter.CurrentRegionID &&
                                  Math.Abs(stop.Destination.X - teleporter.X) < 2000 && Math.Abs(stop.Destination.Y - teleporter.Y) < 2000))
                .OrderBy(stop => stop.Destination.TeleportID, StringComparer.OrdinalIgnoreCase);

        /// <summary>A popup split so no message passes the client's packet limit, however long a list grows.</summary>
        private static void Speak(GameNPC teleporter, GamePlayer player, string text)
        {
            const int Limit = 1400;
            var chunk = new StringBuilder();
            foreach (string line in text.Split('\n'))
            {
                if (chunk.Length > 0 && chunk.Length + line.Length + 1 > Limit)
                {
                    teleporter.SayTo(player, chunk.ToString());
                    chunk.Clear();
                }
                chunk.Append(line).Append('\n');
            }
            if (chunk.Length > 0) teleporter.SayTo(player, chunk.ToString());
        }
    }
}
