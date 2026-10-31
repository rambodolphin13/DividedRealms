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
            All.Add(Make("move_to", "Walk on the zone's navmesh to a point or a living. Result says whether it arrived and whether a mesh was used.", MoveTo, none,
                ("who", "string", "Name or object id, instead of a point"), ("x", "number", ""), ("y", "number", ""), ("z", "number", ""),
                ("stop_within", "number", "Default 50"), ("timeout_s", "number", "Default 120")));
            All.Add(Make("stop", "Stop walking and stop attacking.", Stop, none));
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
            All.Add(Make("buy", "Buy from the open merchant window.", Buy, new[] { "item_slot" }, ("item_slot", "integer", ""),
                ("count", "integer", "Default 1"), ("menu", "integer", "Default 0")));
            All.Add(Make("answer_dialog", "Answer the open yes / no dialog.", AnswerDialog, none, ("response", "integer", "1 yes (default), 0 no")));
            All.Add(Make("wait", "Let time pass and return the events.", Wait, new[] { "seconds" }, ("seconds", "number", "")));
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
            lock (_sessions)
            {
                if (_sessions.TryGetValue(name, out TestSession old) && old.IsConnected)
                    return Describe(old);
            }
            if (!_config.MarkTester(_config.AccountPrefix + name))
            {
                using (var first = new TestSession(_config, _nav, name))
                    first.Register();
                Thread.Sleep(1500);                     // past the server's save of the closing account
                if (!_config.MarkTester(_config.AccountPrefix + name))
                    throw new InvalidOperationException("The new account could not be flagged as a tester.");
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
                catch (InvalidOperationException e) when ((e.Message.Contains("closed the connection") || e.Message.EndsWith("eLoginError 17")) && Environment.TickCount64 < end)
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
                dead = s.Dead, walking = s.Walking, max_speed_pct = s.MaxSpeedPercent,
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
            return new { oid = e.Oid, kind = e.Kind, name = e.Name, level = e.Level, realm = e.Realm, health_pct = e.Health,
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
            return new { result = extra, cursor = s.LastSeq, events = s.EventsSince(since) };
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
            float best = float.MaxValue;
            long bestAt = Environment.TickCount64;
            while (Environment.TickCount64 < end && !s.Dead)
            {
                float left = Vector3.Distance(s.Position, goal());
                if (left <= within)
                {
                    s.Halt();
                    return true;
                }
                if (left < best - 10)
                {
                    best = left;
                    bestAt = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - bestAt > 8000)
                {
                    break;                              // no closer in 8 seconds: stuck, rooted or out of mesh
                }
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
            var skills = Session(a).Skills().Select(k => new { name = k["name"], level = k["level"], page = (int)k["page"] }).ToList();
            return new
            {
                specs = skills.Where(k => k.page == 0),
                abilities = skills.Where(k => k.page == 1),
                styles = skills.Where(k => k.page == 2),
                spells = skills.Where(k => k.page >= 3),
            };
        }

        // On 1.127 spells are skill-list entries (page 3): GetAllUsableSkills, used by place after the specs like any other.
        object Cast(JsonObject a)
        {
            TestSession s = Session(a);
            long since = s.LastSeq;
            if (Text(a, "who") is string who)
                s.SetTarget(Living(s, who).Oid);
            Thread.Sleep(150);
            return Use(s, since, Text(a, "spell"), k => (int)k["page"] == 3, Whole(a, "wait_ms", 3500));
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

        object Wait(JsonObject a)
        {
            TestSession s = Session(a);
            return Collect(s, s.LastSeq, (int)(Number(a, "seconds", 1) * 1000));
        }
    }
}
