using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.Database;

namespace DOL.GS.Scripts
{
    /// <summary>
    /// The realm porter's greeting, laid out like the co-op server porter the owner showed on 2026-11-15: one flowing
    /// paragraph that names the main choices ([Border Keeps], [Battlegrounds], [Frontier Regions], the capital, the
    /// Shrouded Isles harbour, the realm's wild lands, Oceanus, the catacombs, [towns] and [housing]) and a closing line
    /// for the Labyrinth and the full [anywhere else] list. While the co-op world is on (the porter welcomes visitors)
    /// each choice lists all three realms' places, own realm first; otherwise only the porter's own realm.
    /// A place is named only when a teleport row answers it, so the menu never offers a dead link.
    /// </summary>
    public static class PorterMenu
    {
        public const string BorderKeepsWord = "Border Keeps";
        public const string FrontierRegionsWord = "Frontier Regions";

        /// <summary>One realm's places, in the order the greeting names them.</summary>
        public sealed record RealmPlaces(
            eRealm Realm, string Capital, string Harbour, string Wilds, string Catacombs, string[] BorderKeeps);

        public static readonly RealmPlaces[] Realms =
        {
            new(eRealm.Albion, "Camelot", "Gothwaite Harbor", "Avalon Marsh", "Inconnu Crypt",
                new[] { "Castle Sauvage", "Snowdonia Fortress" }),
            new(eRealm.Midgard, "Jordheim", "Aegirhamn", "Gotar", "Kobold Undercity",
                new[] { "Svasud Faste", "Vindsaul Faste" }),
            new(eRealm.Hibernia, "Tir na Nog", "Grove of Domnann", "Shannon Estuary", "Shar Labyrinth",
                new[] { "Druim Ligen", "Druim Cain" }),
        };

        public static RealmPlaces For(eRealm realm) => Realms.FirstOrDefault(r => r.Realm == realm);

        /// <summary>The realms a porter of <paramref name="porterRealm"/> lists: its own first, the others in co-op.</summary>
        public static IEnumerable<RealmPlaces> Shown(eRealm porterRealm, bool coop) =>
            Realms.Where(r => r.Realm == porterRealm).Concat(coop ? Realms.Where(r => r.Realm != porterRealm) : []);

        /// <summary>"[A]", "[A] or [B]", "[A], [B], or [C]".</summary>
        public static string Links(IEnumerable<string> names)
        {
            var list = names.Where(n => !string.IsNullOrEmpty(n)).Select(n => $"[{n}]").ToList();
            return list.Count switch
            {
                0 => string.Empty,
                1 => list[0],
                2 => $"{list[0]} or {list[1]}",
                _ => string.Join(", ", list.Take(list.Count - 1)) + ", or " + list[^1],
            };
        }

        /// <summary>
        /// The greeting. <paramref name="resolves"/> says whether a name sent back to this porter reaches a place.
        /// </summary>
        public static string Greeting(eRealm porterRealm, bool coop, Func<string, bool> resolves)
        {
            var shown = Shown(porterRealm, coop).ToList();
            IEnumerable<string> Pick(Func<RealmPlaces, string> field) =>
                shown.Select(field).Where(name => name != null && resolves(name));

            var text = new StringBuilder();
            text.Append("Greetings. I can channel the energies of this place to send you to far away lands. ");
            text.Append($"Do you wish to go to any of the [{BorderKeepsWord}], [Battlegrounds] or [{FrontierRegionsWord}]? ");

            var capitals = Pick(r => r.Capital).ToList();
            if (capitals.Count > 1)
                text.Append($"Perhaps the great cities are more to your interest; simply choose between {Links(capitals)} and I shall send you on your way. ");
            else if (capitals.Count == 1)
                text.Append($"Perhaps our great city of [{capitals[0]}] is more to your interest. ");

            var harbours = Pick(r => r.Harbour).ToList();
            var wilds = Pick(r => r.Wilds).ToList();
            if (harbours.Count > 0)
            {
                text.Append($"You could explore the area around {Links(harbours)} and discover the majesty of the [Shrouded Isles]");
                text.Append(wilds.Count > 0 ? $", or head out to the distant lands of {TheLinks(wilds)}. " : ". ");
            }
            else if (wilds.Count > 0)
                text.Append($"You could head out to the distant lands of {TheLinks(wilds)}. ");

            bool oceanus = resolves("Oceanus");
            var crypts = Pick(r => r.Catacombs).ToList();
            if (oceanus || crypts.Count > 0)
            {
                string trials = oceanus ? "The trials of Atlantis within [Oceanus]" : string.Empty;
                string catacombs = crypts.Count > 0 ? $"the catacombs of {TheLinks(crypts)}" : string.Empty;
                string both = oceanus && crypts.Count > 0 ? $"{trials} or {catacombs}" : oceanus ? trials : Capitalise(catacombs);
                text.Append($"{both} seek to test your mettle if you are interested in a challenge. ");
            }

            text.Append("You could explore other areas of the realm by asking about other [towns] or [housing] destinations, ");
            text.Append("or face the [Epic Dungeon] of our realm.");

            text.Append("\n\nFinally, I can transport you to the center of the great ");
            text.Append(resolves("Labyrinth") ? "[Labyrinth], or " : string.Empty);
            text.Append($"open the way to [{TravelCatalogue.MenuWord}] in the realms.");
            return text.ToString();
        }

        /// <summary>The border keeps a porter offers: the Old Frontiers pair, then the New Frontiers pair.</summary>
        public static string BorderKeeps(eRealm porterRealm, bool coop, Func<string, bool> resolves)
        {
            var text = new StringBuilder();
            foreach (RealmPlaces realm in Shown(porterRealm, coop))
            {
                var old = realm.BorderKeeps.Where(resolves).ToList();
                var nf = realm.BorderKeeps.Select(k => $"{k} (New Frontiers)").Where(resolves).ToList();
                if (old.Count == 0 && nf.Count == 0) continue;
                if (text.Length > 0) text.Append("\n\n");
                text.Append(realm.Realm == porterRealm ? "Our border keeps" : $"The border keeps of {GlobalConstants.RealmToName(realm.Realm)}");
                if (old.Count > 0) text.Append($" guard the Old Frontiers at {Links(old)}");
                if (nf.Count > 0) text.Append(old.Count > 0 ? $", and the New Frontiers at {Links(nf)}" : $" stand in the New Frontiers at {Links(nf)}");
                text.Append('.');
            }
            return text.Length == 0 ? "I cannot reach any border keep right now." : text.ToString();
        }

        /// <summary>The opening line of the [Frontier Regions] answer; the New Frontiers list follows it.</summary>
        public static string FrontierRegionsIntro(Func<string, bool> resolves)
        {
            var text = new StringBuilder("The Old Frontiers lie beyond our [Border Keeps]. ");
            if (resolves("Darkness Falls"))
                text.Append("The way down into [Darkness Falls] opens to the realm that holds the most frontier keeps. ");
            text.Append("In the New Frontiers I can send you to these places:");
            return text.ToString();
        }

        private static string TheLinks(List<string> names) => "the " + Links(names);

        private static string Capitalise(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
