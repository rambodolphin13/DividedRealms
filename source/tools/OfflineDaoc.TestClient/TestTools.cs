using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot.Collections;

namespace OfflineDaoc.TestClient
{
    public sealed record Tool(string Name, string Description, JsonObject Schema, Func<JsonObject, object> Run);

    // What an AI (over MCP) or a saved scenario can do with a character. Each takes "session" for a second character.
    public sealed class TestTools
    {
        readonly TestConfig _config;
        readonly NavMesh _nav;
        readonly System.Collections.Generic.Dictionary<string, TestSession> _sessions = new();

        readonly System.Collections.Generic.Dictionary<string, FileStream> _claims = new();

        public List<Tool> All { get; } = new();
        public NavMesh Nav => _nav;

        public TestTools(TestConfig config)
        {
            _config = config;
            _nav = new NavMesh(config);
            Define();
        }

        Tool Make(string name, string description, Func<JsonObject, object> run, string[] required, params (string name, string type, string about)[] args)
        {
            var properties = new JsonObject { ["session"] = Arg("string", "Which character (default main). Each session is its own account.") };
            foreach (var (argName, type, about) in args)
                properties[argName] = Arg(type, about);
            var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
            if (required.Length > 0)
                schema["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
            return new Tool(name, description, schema, run);
        }

        static JsonObject Arg(string type, string about)
        {
            return new JsonObject { ["type"] = type, ["description"] = about };
        }

        void Define()
        {
            string[] none = System.Array.Empty<string>();

            All.Add(Make("server_status", "Whether the test server is listening. The tools never start or stop it.", ServerStatus, none));
            All.Add(Make("login", "Log a character in, creating it on first use. Classes: " + string.Join(", ", TestSession.Presets.Keys)
                + ". The account is a normal player that mobs can hit, with GM commands such as /jump, /player, /mob and /item granted.",
                Login, none, ("name", "string", "Character name, letters only"), ("class", "string", "Class, to create the character")));
            All.Add(Make("logout", "Disconnect a character.", Logout, none));
            All.Add(Make("status", "Position, zone, health, power, level, target and whether walking.", Status, none));
            All.Add(Make("nearby", "What the character can see, nearest first.", Nearby, none,
                ("kind", "string", "npc, player or object"), ("name", "string", "Part of the name"),
                ("radius", "number", "Default 3000"), ("limit", "integer", "Default 30")));
            All.Add(Make("events", "Chat, combat, spells and every other game event after a cursor. Each tool result gives its cursor.", Events, none,
                ("since", "integer", "Cursor, default 0"), ("kinds", "string", "Comma list: message, combat, cast, spell_effect, death, dialog ..."),
                ("limit", "integer", "Default 200")));
            All.Add(Make("command", "Run a slash command, or say text without the slash. Returns the events it caused.", Command,
                new[] { "text" }, ("text", "string", "For example /jump to 560000 512000 8000 1 or /player level 50"),
                ("wait_ms", "integer", "How long to collect the reply, default 800")));
            All.Add(Make("teleport", "Teleport with /jump.", Teleport, new[] { "x", "y" }, ("x", "integer", ""), ("y", "integer", ""),
                ("z", "integer", "Default: the ground, from the zone's navmesh"), ("region", "integer", "Default: this region")));
            All.Add(Make("target", "Target by name or object id, or clear with an empty string.", Target, new[] { "who" }, ("who", "string", "")));
            All.Add(Make("ground_target", "Set the ground target on a living, for area spells.", GroundTarget, new[] { "who" }, ("who", "string", "")));
            All.Add(Make("move_to", "Walk on the zone's navmesh to a point or a living. Result says whether it arrived and whether a mesh was used.", MoveTo, none,
                ("who", "string", "Name or object id, instead of a point"), ("x", "number", ""), ("y", "number", ""), ("z", "number", ""),
                ("stop_within", "number", "Default 50"), ("timeout_s", "number", "Default 120")));
            All.Add(Make("stop", "Stop walking and stop attacking.", Stop, none));
            All.Add(Make("swim", "Say you are in the water, for the sea, boats and breath. Atlantis zones have no water level to detect it from.", Swim,
                new[] { "on" }, ("on", "boolean", "True in the water, false on land")));
            All.Add(Make("release", "Release a dead character and wait until it stands at its bind point. Don't send /release yourself: "
                + "during the countdown it toggles auto-release off.", Release, none));
            All.Add(Make("prepare", "Make a character fit for a test: level, trained specs, and the best armor and weapon the world has for its class. "
                + "Without this a level 50 test character dies in seconds. Every class works: see the login tool.",
                Prepare, new[] { "class" }, ("class", "string", "The character's class"), ("level", "integer", "Default 50")));
            All.Add(Make("fight", "Target a living, close to melee and fight until it dies, you die or the time runs out.", Fight, new[] { "who" },
                ("who", "string", "Name, object id, or @mob for the nearest hostile mob"), ("timeout_s", "number", "Default 60")));
            All.Add(Make("abilities", "Specs, abilities, styles and spells, with the names cast and use_ability take.", Abilities, none));
            All.Add(Make("cast", "Cast a spell by name at the target or at who, facing it first.", Cast, new[] { "spell" },
                ("spell", "string", "Name, or part of it"), ("who", "string", "Name or object id"), ("wait_ms", "integer", "Default 3500")));
            All.Add(Make("use_ability", "Use an ability or style by name.", UseAbility, new[] { "name" }, ("name", "string", ""),
                ("wait_ms", "integer", "Default 1000")));
            All.Add(Make("inventory", "Carried and worn items by slot.", Inventory, none));
            All.Add(Make("use_item", "Use the item in a slot.", UseItem, new[] { "slot" }, ("slot", "integer", ""), ("type", "integer", "1 use (default), 0 click")));
            All.Add(Make("move_item", "Move, swap or equip an item.", MoveItem, new[] { "from", "to" }, ("from", "integer", ""), ("to", "integer", ""),
                ("count", "integer", "Default 1")));
            All.Add(Make("interact", "Right-click a living or object.", Interact, new[] { "who" }, ("who", "string", ""), ("wait_ms", "integer", "Default 800")));
            All.Add(Make("pick_up", "Pick up an object on the ground.", PickUp, new[] { "who" }, ("who", "string", "")));
            All.Add(Make("craft", "Craft a recipe by its client recipe id (tdl.crf), as the crafting window does; waits for the result.", Craft,
                new[] { "recipe" }, ("recipe", "integer", "Recipe id (CraftedItemID)"), ("wait_ms", "integer", "How long to collect messages (default 15000)")));
            All.Add(Make("house_enter", "Enter (or leave) a house through its door, as the client does: stand within 1000 of it.", HouseEnter,
                new[] { "house" }, ("house", "integer", "House number"), ("enter", "boolean", "Default true; false leaves")));
            All.Add(Make("house_place", "Drop a backpack item on a house hookpoint or floor, as dragging it in decorate mode does.", HousePlace,
                new[] { "slot", "house", "position" }, ("slot", "integer", "Backpack slot of the item"), ("house", "integer", "House number"),
                ("position", "integer", "Hookpoint id (or garden tile)"), ("method", "integer", "Client placement mode, default 5 (hookpoint)"),
                ("x", "integer", "Indoor x, default 0"), ("y", "integer", "Indoor y, default 0")));
            All.Add(Make("buy", "Buy from the open merchant window.", Buy, new[] { "item_slot" }, ("item_slot", "integer", ""),
                ("count", "integer", "Default 1"), ("menu", "integer", "Default 0")));
            All.Add(Make("answer_dialog", "Answer the open yes / no dialog.", AnswerDialog, none, ("response", "integer", "1 yes (default), 0 no")));
            All.Add(Make("wait", "Let time pass and return the events.", Wait, new[] { "seconds" }, ("seconds", "number", "")));
            All.Add(Make("db_query", "Read the world database with one SELECT, to find what to test: Mob, Zones, ItemTemplate, Spell, DOLCharacters ...", DbQuery,
                new[] { "sql" }, ("sql", "string", "Read only"), ("limit", "integer", "Default 200")));
            All.Add(Make("report_finding", "Add to the log every tester shares, Claude and Codex alike. Quote the game's own text as evidence.", ReportFinding,
                new[] { "what" }, ("what", "string", "What is wrong, or what you are claiming"),
                ("kind", "string", "bug (default), works, claim (an area you are taking, so others stay out), note"),
                ("severity", "string", "blocker, major, minor"), ("area", "string", "mobs, camps, areas, items, travel, artifacts ..."),
                ("evidence", "string", "Game messages, ids, the steps to repeat it")));
            All.Add(Make("read_findings", "Read the shared log, from a line onwards. Read it before you start and when you change area.", ReadFindings, none,
                ("since", "integer", "Lines already read, default 0"), ("kind", "string", "Only this kind")));
        }

        // ---------------------------------------------------------------- arguments

        TestSession Session(JsonObject a)
        {
            string name = Text(a, "session") ?? "main";
            lock (_sessions)
            {
                if (_sessions.TryGetValue(name, out TestSession s) && s.IsConnected)
                    return s;
            }
            throw new InvalidOperationException($"Session {name} is not logged in.");
        }

        static string Text(JsonObject a, string key)
        {
            return a[key]?.ToString();
        }

        static double Number(JsonObject a, string key, double fallback)
        {
            return a[key] == null ? fallback : double.Parse(a[key].ToString());
        }

        static int Whole(JsonObject a, string key, int fallback)
        {
            return (int)Number(a, key, fallback);
        }

        static Entity Living(TestSession s, string who)
        {
            return s.Find(who) ?? throw new InvalidOperationException($"Nothing called {who} in sight.");
        }

        // ---------------------------------------------------------------- the tools

        object ServerStatus(JsonObject a)
        {
            bool listening;
            try
            {
                using var probe = new System.Net.Sockets.TcpClient();
                listening = probe.ConnectAsync(_config.Host, _config.Port).Wait(1000) && probe.Connected;
            }
            catch (AggregateException)
            {
                listening = false;
            }
            return new { listening, host = _config.Host, port = _config.Port, runtime = _config.Runtime };
        }

        object Login(JsonObject a)
        {
            string name = Text(a, "session") ?? "main";
            if (!name.All(char.IsAsciiLetter))
                throw new InvalidOperationException("Session names are letters only: they become account names.");
            lock (_sessions)
            {
                if (_sessions.TryGetValue(name, out TestSession old) && old.IsConnected)
                    return Describe(old);
                if (!_claims.ContainsKey(name))
                    _claims[name] = _config.ClaimAccount(_config.AccountPrefix + name);
            }
            // A client gone without quitting stays linkdead for 60 seconds, and the server refuses new logins meanwhile
            // (eLoginError.AccountAlreadyLoggedIn, or the connection closed).
            long end = Environment.TickCount64 + 75000;
            while (true)
            {
                var session = new TestSession(_config, _nav, name);
                try
                {
                    session.LogIn(Text(a, "name"), Text(a, "class"));
                    lock (_sessions)
                        _sessions[name] = session;
                    TestSession.WaitUntil(() => session.Character.Count > 0, 3000);
                    return Describe(session);
                }
                catch (InvalidOperationException e) when ((e.Message == TestSession.Retry || e.Message.Contains("closed the connection") || e.Message.EndsWith("eLoginError 17")) && Environment.TickCount64 < end)
                {
                    session.Dispose();
                    Thread.Sleep(5000);
                }
            }
        }

        object Logout(JsonObject a)
        {
            string name = Text(a, "session") ?? "main";
            Close(name);
            return new { logged_out = name };
        }

        public void LogoutAll()
        {
            string[] names;
            lock (_sessions)
                names = _sessions.Keys.ToArray();
            foreach (string name in names)
                Close(name);
        }

        void Close(string name)
        {
            TestSession session;
            lock (_sessions)
            {
                if (_claims.Remove(name, out FileStream claim))
                    claim.Dispose();
                if (!_sessions.Remove(name, out session))
                    return;
            }
            session.Command("/quit");
            TestSession.WaitUntil(() => !session.IsConnected, 1000);
            session.Dispose();
        }

        object Status(JsonObject a)
        {
            return Describe(Session(a));
        }

        static object Describe(TestSession s)
        {
            Entity target = s.TargetOid == 0 ? null : s.Find(s.TargetOid.ToString());
            return new
            {
                session = s.SessionName, account = s.Account, oid = s.Oid,
                name = s.Character.GetValueOrDefault("name"), level = s.Character.GetValueOrDefault("level"),
                @class = s.Character.GetValueOrDefault("class_name"),
                region = s.Region, zone = s.CurrentZone?.Id, zone_name = s.CurrentZone?.Name,
                x = (int)s.X, y = (int)s.Y, z = (int)s.Z, heading = s.Heading,
                health_pct = s.Status.GetValueOrDefault("health_pct"), mana_pct = s.Status.GetValueOrDefault("mana_pct"),
                endurance_pct = s.Status.GetValueOrDefault("endurance_pct"), health = s.Status.GetValueOrDefault("health"),
                max_health = s.Status.GetValueOrDefault("max_health"),
                dead = s.Dead, walking = s.Walking, swimming = s.Swimming, max_speed_pct = s.MaxSpeedPercent,
                target = target == null ? null : Show(s, target),
                unknown_packets = UnknownPackets(s),
                cursor = s.LastSeq,
            };
        }

        // Packets DaocClient.cs doesn't decode yet: a test gap, not a failure.
        static string UnknownPackets(TestSession s)
        {
            lock (s.UnknownPackets)
                return string.Join(" ", s.UnknownPackets.OrderBy(p => p.Key).Select(p => $"0x{p.Key:X2}:{p.Value}"));
        }

        static object Show(TestSession s, Entity e)
        {
            return new { oid = e.Oid, kind = e.Kind, name = e.Name, level = e.Level, realm = e.Realm, health_pct = e.Health, model = e.Model,
                x = (int)e.X, y = (int)e.Y, z = (int)e.Z, distance = (int)s.Distance(e), guild = e.Guild };
        }

        object Nearby(JsonObject a)
        {
            TestSession s = Session(a);
            return s.Nearby(Text(a, "kind"), Text(a, "name"), Number(a, "radius", 3000), Whole(a, "limit", 30)).Select(e => Show(s, e));
        }

        object Events(JsonObject a)
        {
            TestSession s = Session(a);
            ISet<string> kinds = Text(a, "kinds")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            return new { cursor = s.LastSeq, events = s.EventsSince((long)Number(a, "since", 0), kinds, Whole(a, "limit", 200)) };
        }

        static object Collect(TestSession s, long since, int waitMs, object extra = null)
        {
            Thread.Sleep(waitMs);
            return new { result = extra, cursor = s.LastSeq, events = s.TakeEvents() };
        }

        object Command(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            s.Command(Text(a, "text"));
            return Collect(s, since, Whole(a, "wait_ms", 800));
        }

        object Teleport(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            int wanted = Whole(a, "region", s.Region);
            int x = Whole(a, "x", 0), y = Whole(a, "y", 0), z = Whole(a, "z", -1);
            if (z < 0)
                z = (int)(_config.ZoneAt(wanted, x, y) is ZoneInfo zone && _nav.Ground(zone.Id, new Vector3(x, y, 2000)) is Vector3 ground ? ground.Z : s.Z);
            s.Command($"/jump to {x} {y} {z} {wanted}");
            bool arrived = TestSession.WaitUntil(() => s.InWorld && s.Region == wanted && Math.Abs(s.X - x) < 50 && Math.Abs(s.Y - y) < 50, 15000);
            return Collect(s, since, 500, new { arrived, position = Describe(s) });
        }

        object Target(JsonObject a)
        {
            TestSession s = Session(a);
            string who = Text(a, "who");
            Entity e = string.IsNullOrEmpty(who) ? null : Living(s, who);
            s.SetTarget(e?.Oid ?? 0);
            return e == null ? "target cleared" : Show(s, e);
        }

        object GroundTarget(JsonObject a)
        {
            TestSession s = Session(a);
            Entity e = Living(s, Text(a, "who"));
            s.SetGroundTarget(e);
            return Show(s, e);
        }

        object MoveTo(JsonObject a)
        {
            TestSession s = Session(a);
            string who = Text(a, "who");
            Entity e = who == null ? null : Living(s, who);
            Vector3 Goal() => e?.Position ?? new Vector3((float)Number(a, "x", s.X), (float)Number(a, "y", s.Y), (float)Number(a, "z", s.Z));
            bool arrived = GoTo(s, Goal, (float)Number(a, "stop_within", 50), Number(a, "timeout_s", 120), out bool onMesh);
            return new { arrived, on_navmesh = onMesh, distance_left = (int)Vector3.Distance(s.Position, Goal()), position = Describe(s) };
        }

        // Replans as it goes: each mesh stops at its zone's edge, and a living keeps moving.
        static bool GoTo(TestSession s, Func<Vector3> goal, float within, double timeoutS, out bool onMesh)
        {
            long end = Environment.TickCount64 + (long)(timeoutS * 1000);
            onMesh = true;
            var watch = new WalkWatch(s.Position, Environment.TickCount64);
            while (Environment.TickCount64 < end && !s.Dead)
            {
                if (Vector3.Distance(s.Position, goal()) <= within)
                {
                    s.Halt();
                    return true;
                }
                if (watch.Stalled(s.Position, Environment.TickCount64))
                    break;
                onMesh &= s.Walk(goal());
                Thread.Sleep(1000);
            }
            s.Halt();
            return false;
        }

        object Stop(JsonObject a)
        {
            TestSession s = Session(a);
            s.Halt();
            s.Attack(false);
            return Describe(s);
        }

        object Swim(JsonObject a)
        {
            TestSession s = Session(a);
            s.InSea = (bool)a["on"];
            return Collect(s, s.LastSeq, 1500, new { swimming = s.Swimming });
        }

        object Release(JsonObject a)
        {
            TestSession s = Session(a);
            if (!s.Dead)
                return new { released = false, why = "not dead", you = Describe(s) };
            long since = s.LastSeq;
            s.Command("/release");
            bool released = TestSession.WaitUntil(() => s.InWorld && !s.Dead, 30000);
            return Collect(s, since, 500, new { released, you = Describe(s) });
        }

        object Prepare(JsonObject a)
        {
            TestSession s = Session(a);
            string wanted = new string((Text(a, "class") ?? "").Where(char.IsAsciiLetter).ToArray());
            if (!TestSession.Presets.TryGetValue(wanted, out ClassPreset preset))
                throw new InvalidOperationException("Classes: " + string.Join(", ", TestSession.Presets.Keys));
            long since = s.LastSeq;
            s.Command($"/player level {Whole(a, "level", 50)}");
            Thread.Sleep(1500);
            ClassPreset kitFor = Kit(s, preset);
            int line = 0;
            foreach (string spec in kitFor.Specs)
            {
                int target = line++ switch { 0 => 50, 1 => 35, _ => 25 };
                int points = SpecLevels(s);
                for (int attempt = 0; attempt < 4 && SpecLevels(s) == points; attempt++)    // /train ignores a call too soon after the last
                {
                    s.Command($"/train {spec} {target}");
                    TestSession.WaitUntil(() => SpecLevels(s) != points, 1500);
                }
            }
            var specs = new List<object>();
            specs.AddRange(s.Skills().Where(k => (int)k["page"] == 0 && (int)k["level"] > 1).Select(k => new { spec = k["name"], level = k["level"] }));

            var kit = new List<object>();
            foreach (int slot in new[] { 21, 22, 23, 25, 27, 28 })      // head, hands, feet, torso, legs, arms
                kit.Add(Equip(s, slot, kitFor, $"Item_Type = {slot} and Object_Type = {kitFor.ArmorType}", out _));
            int hand = 10;
            foreach (int slot in new[] { 10, 12 })                      // one-handed, else two-handed (a staff)
            {
                kit.Add(Equip(s, slot, kitFor, $"Item_Type = {slot} and Object_Type = {kitFor.WeaponType}", out bool worn));
                if (worn)                                               // slot 10 is never empty: the starter weapon sits there
                {
                    hand = slot;
                    break;
                }
            }
            s.UseSlot(hand, 0);                                         // ready that weapon set
            s.Command("/heal");                                         // levels raise max health, not health
            return Collect(s, since, 500, new { specs, kit, you = Describe(s) });
        }

        // What the character can really use, for classes without a hand-tuned kit.
        static readonly (string Name, int Type)[] Armors =
            { ("Plate", 36), ("Scale", 38), ("Chain", 35), ("Reinforced", 37), ("Studded", 34), ("Leather", 33), ("Cloth", 32) };
        static readonly (string Name, int Type)[] Weapons =
        {
            ("Hand to Hand", 25), ("Fist", 27), ("Mauler", 28), ("Scythe", 26), ("Celtic Spear", 23), ("Large Weapons", 22), ("Blades", 19), ("Blunt", 20),
            ("Piercing", 21), ("Swords", 11), ("Hammers", 12), ("Axes", 13), ("Spears", 14), ("Flexible", 24), ("Polearm", 7), ("Two Handed", 6),
            ("Slashing", 3), ("Crushing", 2), ("Thrusting", 4), ("Staves", 8),
        };
        static readonly HashSet<string> SupportLines = new(StringComparer.OrdinalIgnoreCase)
            { "Parry", "Shield", "Shields", "Stealth", "Envenom", "Critical Strike", "Dual Wield", "Left Axe", "Celtic Dual", "Thrown Weapons", "Instruments" };

        static ClassPreset Kit(TestSession s, ClassPreset preset)
        {
            if (preset.ArmorType != 0)
                return preset;
            var names = s.Skills().Where(k => (int)k["page"] == 1).Select(k => (string)k["name"]).ToList();
            int armor = Armors.FirstOrDefault(a => names.Any(n => n.StartsWith("Armor") && n.Contains(a.Name, StringComparison.OrdinalIgnoreCase))).Type;
            var lines = s.Skills().Where(k => (int)k["page"] == 0).Select(k => (string)k["name"]).ToList();
            string[] specs = lines.Where(l => !SupportLines.Contains(l)).Concat(lines.Where(SupportLines.Contains)).Take(3).ToArray();
            int weapon = KitRules.WeaponForSpecs(specs);
            if (weapon == 0)
                weapon = Weapons.FirstOrDefault(w => names.Any(n => n.StartsWith("Weaponry") && n.Contains(w.Name, StringComparison.OrdinalIgnoreCase))).Type;
            return preset with { ArmorType = armor == 0 ? 32 : armor, WeaponType = weapon == 0 ? 8 : weapon, Specs = specs };
        }

        static int SpecLevels(TestSession s)
        {
            return s.Skills().Where(k => (int)k["page"] == 0).Sum(k => (int)k["level"]);
        }

        // The best item the class can wear, created in the backpack and put on. Some are refused (a class's own item, a
        // minimum level), so it works down a short list. Artifacts stay out: they are test subjects.
        object Equip(TestSession s, int slot, ClassPreset preset, string what, out bool worn)
        {
            worn = false;
            var rows = _config.Query($"select Id_nb, Name from ItemTemplate where {what} and Realm in (0, {preset.Realm}) and Level between 40 and 51 "
                + $"and (AllowedClasses in ('', '0') or AllowedClasses is null or ';' || AllowedClasses || ';' like '%;{preset.ClassId};%') "
                + "and Id_nb not in (select ItemID from ArtifactXItem) and Id_nb not like '% %' "
                + "order by DPS_AF desc, Bonus1 + Bonus2 + Bonus3 + Bonus4 desc, Level desc limit 6", 6);
            foreach (var row in rows)
            {
                int free = 40;
                var used = s.Inventory().Select(i => (int)i["slot"]).ToHashSet();
                while (used.Contains(free))
                    free++;
                s.Command("/item create " + row["Id_nb"]);
                if (!TestSession.WaitUntil(() => s.Inventory().Any(i => (int)i["slot"] == free), 2000))
                    continue;
                object made = s.Inventory().First(i => (int)i["slot"] == free)["name"];
                s.MoveItem(free, slot, 1);
                if (TestSession.WaitUntil(() => s.Inventory().Any(i => (int)i["slot"] == slot && Equals(i["name"], made)), 1000))
                {
                    worn = true;
                    return new { slot, item = row["Name"], worn = true };
                }
            }
            return new { slot, item = (string)null, worn = false };
        }

        object Fight(JsonObject a)
        {
            TestSession s = Session(a);
            Entity foe = Living(s, Text(a, "who"));
            long since = s.LastSeq;
            long end = Environment.TickCount64 + (long)(Number(a, "timeout_s", 60) * 1000);
            int oid = foe.Oid;
            s.SetTarget(oid);
            bool swinging = false;
            string outcome = "timed out";
            while (Environment.TickCount64 < end)
            {
                if (s.Dead)
                {
                    outcome = "you died";
                    break;
                }
                foe = s.Find(oid.ToString());
                if (foe == null || foe.Health == 0)
                {
                    outcome = "it died or left";
                    break;
                }
                if (s.Distance(foe) > TestSession.Reach)
                {
                    s.Walk(foe.Position);
                }
                else
                {
                    s.Halt();
                    s.Face(foe);
                    if (!swinging)
                    {
                        s.Attack(true);
                        swinging = true;
                    }
                }
                Thread.Sleep(400);
            }
            s.Halt();
            s.Attack(false);
            return Collect(s, since, 500, new { outcome, foe = foe == null ? null : Show(s, foe), you = Describe(s) });
        }

        object Abilities(JsonObject a)
        {
            TestSession s = Session(a);
            var skills = s.Skills().Select(k => new { name = k["name"], level = k["level"], page = (int)k["page"] }).ToList();
            return new
            {
                specs = skills.Where(k => k.page == 0),
                abilities = skills.Where(k => k.page == 1),
                styles = skills.Where(k => k.page == 2),
                spells = skills.Where(k => k.page >= 3),
                spell_lines = s.SpellLines().Select(l => new
                {
                    l.line,
                    name = l.data.GetValueOrDefault("name"),
                    spells = ((Godot.Collections.Array)l.data["spells"]).Cast<Dictionary>()
                        .Select(p => new { name = p["name"], level = p["level"], kind = p["kind"] }),
                }),
            };
        }

        // Spells come two ways on 1.127: a list caster's in spell lines (UseSpell), the others' as skill entries, page 3.
        object Cast(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            if (Text(a, "who") is string who)
                s.SetTarget(Equals(who, s.Character.GetValueOrDefault("name")) ? s.Oid : Living(s, who).Oid);
            Thread.Sleep(150);
            string wanted = Text(a, "spell");
            var fromLine = s.SpellLines()
                .SelectMany(l => ((Godot.Collections.Array)l.data["spells"]).Cast<Dictionary>().Select(p => (l.line, spell: p)))
                .Where(p => (string)p.spell["kind"] == "spell" && ((string)p.spell["name"]).Contains(wanted, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => ((string)p.spell["name"]).Equals(wanted, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => (int)p.spell["level"])
                .ToList();
            if (fromLine.Count == 0)
                return Use(s, since, wanted, k => (int)k["page"] >= 3, Whole(a, "wait_ms", 3500));   // (page 4 is songs)
            if (s.TargetOid != 0 && s.Find(s.TargetOid.ToString()) is Entity target)
                s.Face(target);
            s.CastFromLine((int)fromLine[0].spell["level"], fromLine[0].line);
            return Collect(s, since, Whole(a, "wait_ms", 3500), new { used = fromLine[0].spell["name"] });
        }

        object UseAbility(JsonObject a)
        {
            TestSession s = Session(a);
            return Use(s, s.LastSeq, Text(a, "name"), k => true, Whole(a, "wait_ms", 1000));
        }

        static object Use(TestSession s, long since, string wanted, Func<Dictionary, bool> kind, int waitMs)
        {
            var usable = s.Skills().Where(k => (int)k["page"] != 0).ToList();   // type 1 counts from the first that isn't a spec
            bool Exact(Dictionary k) => kind(k) && ((string)k["name"]).Equals(wanted, StringComparison.OrdinalIgnoreCase);
            bool Like(Dictionary k) => kind(k) && ((string)k["name"]).Contains(wanted, StringComparison.OrdinalIgnoreCase);
            int index = usable.FindIndex(Exact);
            if (index < 0)
                index = usable.FindIndex(Like);
            if (index < 0)
                throw new InvalidOperationException($"Nothing usable like {wanted}: see abilities.");
            if (s.TargetOid != 0 && s.Find(s.TargetOid.ToString()) is Entity target)
                s.Face(target);
            s.UseAbility(index);
            return Collect(s, since, waitMs, new { used = usable[index]["name"] });
        }

        object Inventory(JsonObject a)
        {
            return Session(a).Inventory().Select(i => new { slot = i["slot"], name = i["name"], level = i["level"], condition = i["condition"] });
        }

        object UseItem(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            s.UseSlot(Whole(a, "slot", 0), Whole(a, "type", 1));
            return Collect(s, since, 1000);
        }

        object MoveItem(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            s.MoveItem(Whole(a, "from", 0), Whole(a, "to", 0), Whole(a, "count", 1));
            return Collect(s, since, 500);
        }

        object Interact(JsonObject a)
        {
            TestSession s = Session(a);
            Entity e = Living(s, Text(a, "who"));
            long since = s.LastSeq;
            s.SetTarget(e.Oid);
            s.Face(e);
            s.Interact(e.Oid);
            return Collect(s, since, Whole(a, "wait_ms", 800), Show(s, e));
        }

        object PickUp(JsonObject a)
        {
            TestSession s = Session(a);
            Entity e = Living(s, Text(a, "who"));
            long since = s.LastSeq;
            s.PickUp(e.Oid);
            return Collect(s, since, 800);
        }

        object Craft(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            s.Craft(Whole(a, "recipe", 0));
            return Collect(s, since, Whole(a, "wait_ms", 15000));
        }

        object HouseEnter(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            s.HouseEnter(Whole(a, "house", 0), a["enter"] == null || bool.Parse(a["enter"].ToString()));
            return Collect(s, since, 1200);
        }

        object HousePlace(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            s.HousePlace(Whole(a, "slot", 0), Whole(a, "house", 0), Whole(a, "position", 0), Whole(a, "method", 5),
                Whole(a, "x", 0), Whole(a, "y", 0));
            return Collect(s, since, 1200);
        }

        object Buy(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            s.Buy(Whole(a, "item_slot", 0), Whole(a, "count", 1), Whole(a, "menu", 0));
            return Collect(s, since, 800);
        }

        object AnswerDialog(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            s.AnswerDialog(Whole(a, "response", 1));
            return Collect(s, since, 800);
        }

        object DbQuery(JsonObject a)
        {
            return _config.Query(Text(a, "sql"), Whole(a, "limit", 200));
        }

        object ReportFinding(JsonObject a)
        {
            string name = Text(a, "session") ?? "main";
            TestSession s;
            lock (_sessions)
                s = _sessions.GetValueOrDefault(name);
            _config.AppendShared(new
            {
                at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), by = _config.AccountPrefix + name,
                kind = Text(a, "kind") ?? "bug", severity = Text(a, "severity"), area = Text(a, "area"),
                what = Text(a, "what"), evidence = Text(a, "evidence"),
                region = s?.Region, zone = s?.CurrentZone?.Name, x = (int?)s?.X, y = (int?)s?.Y, z = (int?)s?.Z,
            });
            return new { logged_to = _config.SharedLog };
        }

        object ReadFindings(JsonObject a)
        {
            List<string> lines = _config.ReadShared();
            int since = Math.Min(Whole(a, "since", 0), lines.Count);
            string kind = Text(a, "kind");
            return new
            {
                lines = lines.Count,
                findings = lines.Skip(since).Select(l => JsonNode.Parse(l)).Where(n => kind == null || (string)n["kind"] == kind),
            };
        }

        object Wait(JsonObject a)
        {
            TestSession s = Session(a);
            return Collect(s, s.LastSeq, (int)(Number(a, "seconds", 1) * 1000));
        }
    }
}
