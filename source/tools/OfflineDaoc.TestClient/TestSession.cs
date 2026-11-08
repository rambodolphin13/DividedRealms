using System.Numerics;
using Godot.Collections;
using MMOEngine;

namespace OfflineDaoc.TestClient
{
    public sealed record ClassRow(string name, int class_id, int realm, int race, int model, int gender, int[] stats);

    public sealed class Entity
    {
        public int Oid;
        public string Kind;                 // npc, player, object
        public string Name;
        public string Guild;
        public int Level;
        public int Realm;
        public int Model;
        public double X, Y, Z;
        public int Health = 100;
        public long SeenAt;

        public Vector3 Position => new((float)X, (float)Y, (float)Z);
    }

    public sealed record GameEvent(long Seq, long AtMs, string Kind, object Data);

    public sealed record CharacterChoice(int Realm, int Slot, string Name, int Level, string ClassName);

    // ArmorType and WeaponType are eObjectType; Specs are trained in that order, as far as the points go.
    public sealed record ClassPreset(int ClassId, int Realm, int Race, int Model, int[] Stats, int ArmorType, int WeaponType, params string[] Specs)
    {
        public int Gender { get; init; }
    }

    // One logged-in character: the packets coming in kept as state and events, and what a real client keeps sending.
    public sealed class TestSession : IDisposable
    {
        const int RunSpeed = 191;           // the speed of 100%
        const float MeleeReach = 120f;
        const int TickMs = 50;
        public const string Retry = "reconnecting";
        const int MaxEvents = 5000;
        const int StateSwimming = 0x04;     // PlayerPositionUpdateHandler.StateFlags

        // Kept as state only: at this volume they would drown what a test wants to read.
        static readonly HashSet<string> QuietKinds = new()
        {
            "move", "time", "equipment", "npc", "player", "object", "remove", "map_marker", "map_marker_remove",
            "visual_effect", "warmap_keeps", "warmap_fights", "sound_effect", "status", "you", "inventory", "skills",
            "spell_line", "stats", "resists", "weapon_armor", "craft_skills", "encumbrance", "los", "group_update",
        };

        // eCharacterClass and the races' GlobalConstants.STARTING_STATS_DICT, packet order STR DEX CON QUI INT PIE EMP CHA,
        // with the 30 creation points spent 10 / 10 / 10 as the new client does.
        public static readonly System.Collections.Generic.Dictionary<string, ClassPreset> Presets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["armsman"] = new(2, 1, 1, 32, new[] { 70, 70, 70, 60, 60, 60, 60, 60 }, 36, 3, "Slash", "Shields", "Parry"),
            ["cleric"] = new(6, 1, 1, 32, new[] { 60, 60, 70, 70, 60, 70, 60, 60 }, 35, 2, "Rejuvenation", "Enhancement", "Smite"),
            ["wizard"] = new(7, 1, 1, 32, new[] { 60, 70, 70, 60, 70, 60, 60, 60 }, 32, 8, "Fire Magic", "Earth Magic", "Cold Magic"),
            ["warrior"] = new(22, 2, 5, 503, new[] { 80, 60, 80, 50, 60, 60, 60, 60 }, 35, 11, "Sword", "Shields", "Parry"),
            ["healer"] = new(26, 2, 5, 503, new[] { 70, 60, 80, 50, 60, 70, 60, 60 }, 35, 12, "Mending", "Augmentation", "Pacification"),
            ["runemaster"] = new(29, 2, 5, 503, new[] { 70, 60, 80, 50, 60, 70, 60, 60 }, 32, 8, "Darkness", "Suppression", "Runecarving"),
            ["hero"] = new(44, 3, 9, 302, new[] { 70, 70, 70, 60, 60, 60, 60, 60 }, 38, 19, "Blades", "Shields", "Parry"),
            ["druid"] = new(47, 3, 9, 302, new[] { 60, 60, 70, 70, 60, 60, 70, 60 }, 38, 20, "Nurture", "Nature", "Regrowth"),
            ["eldritch"] = new(40, 3, 11, 334, new[] { 40, 85, 50, 75, 80, 60, 60, 60 }, 32, 8, "Void", "Light", "Mana"),
        };

        // Every other class comes from classes.json (gen_classes.py): armor, weapon and specs are worked out by prepare.
        static TestSession()
        {
            string file = Path.Combine(AppContext.BaseDirectory, "classes.json");
            if (!File.Exists(file))
                return;
            foreach (var (key, row) in System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, ClassRow>>(File.ReadAllText(file)))
            {
                if (!Presets.ContainsKey(key))
                    Presets[key] = new ClassPreset(row.class_id, row.realm, row.race, row.model, row.stats, 0, 0) { Gender = row.gender };
            }
        }

        readonly TestConfig _config;
        readonly NavMesh _nav;
        readonly DaocClient _client = new();
        readonly object _lock = new();
        readonly Thread _pump;
        readonly System.Collections.Generic.Dictionary<int, Entity> _entities = new();
        readonly HashSet<int> _askedFor = new();
        readonly LinkedList<GameEvent> _events = new();
        readonly List<CharacterChoice> _characters = new();
        readonly System.Collections.Generic.Dictionary<int, Dictionary> _inventory = new();
        readonly List<Dictionary> _skills = new();
        readonly SortedDictionary<int, Dictionary> _spellLines = new();
        readonly long _started = Environment.TickCount64;

        volatile bool _running = true;
        long _seq, _delivered;
        bool _loggedIn, _realmKnown, _inWorld;
        int _charactersSeen;
        string _failure;
        Queue<Vector3> _path;
        float _speedNow;
        long _nextPing, _nextPosition;

        public System.Collections.Generic.Dictionary<int, int> UnknownPackets { get; } = new();
        public string SessionName { get; }
        public string Account { get; }
        public int Oid { get; private set; }
        public int Region { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public int Heading { get; private set; }
        public int TargetOid { get; private set; }
        public bool Dead { get; private set; }
        public int MaxSpeedPercent { get; private set; } = 100;
        public Dictionary Status { get; private set; } = new();
        public Dictionary Character { get; private set; } = new();
        public Dictionary Points { get; private set; } = new();
        public Dictionary Money { get; private set; } = new();
        public Dictionary LastDialog { get; private set; }
        public bool IsConnected => _client.IsOpen;
        public bool InWorld => _inWorld;
        public long LastSeq => Interlocked.Read(ref _seq);

        // Everything since the last tool call: an AI thinks for seconds between calls, and what lands then still counts.
        public List<GameEvent> TakeEvents()
        {
            return EventsSince(Interlocked.Exchange(ref _delivered, LastSeq));
        }
        public bool Walking
        {
            get
            {
                lock (_lock)
                    return _path != null;
            }
        }
        public Vector3 Position => new((float)X, (float)Y, (float)Z);
        int HealthPercent => Status.TryGetValue("health_pct", out object v) ? (int)v : 100;

        public TestSession(TestConfig config, NavMesh nav, string sessionName)
        {
            _config = config;
            _nav = nav;
            SessionName = sessionName;
            Account = config.AccountPrefix + sessionName;
            _client.ServerPort = config.Port;
            _client.Signalled += OnSignal;
            _pump = new Thread(Pump) { IsBackground = true, Name = "client " + sessionName };
            _pump.Start();
        }

        public void Dispose()
        {
            _running = false;
            _client.Close();
        }

        // ---------------------------------------------------------------- logging in

        public void LogIn(string name, string className)
        {
            _config.EnsureAccount(Account);
            _config.GrantCommands(Account);
            if (!_client.Open(_config.Host))
                throw new InvalidOperationException(_failure ?? "could not connect");
            _client.Login(Account, _config.PasswordFor(Account));
            Require(() => _loggedIn, 15000, "the server did not grant the login");

            _client.RequestCharacters(0);
            Require(() => _realmKnown, 10000, "the server did not answer with the account's realm");
            for (int realm = 1; realm <= 3; realm++)
                AskCharacters(realm);

            CharacterChoice choice = Choose(name);
            if (choice == null)
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(className))
                    throw new InvalidOperationException("The account has no such character: give a name and a class to create it. Classes: "
                        + string.Join(", ", Presets.Keys));
                Create(name, className);
                choice = Choose(name) ?? throw new InvalidOperationException(
                    $"The server refused the character {name} (names are letters only and must be unused)");
            }

            if (_config.PlaceIfNowhere(choice.Name))    // (the server holds the row it loaded: reconnect to read the new one)
            {
                _client.Close();
                throw new InvalidOperationException(Retry);
            }

            AskCharacters(choice.Realm);                // (the server enters from the realm last asked)
            _client.EnterWorld(choice.Realm, choice.Slot, choice.Name);
            Require(() => _inWorld, 60000, "the character never reached the world");
        }

        void AskCharacters(int realm)
        {
            int seen = Volatile.Read(ref _charactersSeen);
            _client.RequestCharacters(realm);
            Require(() => Volatile.Read(ref _charactersSeen) > seen, 10000, "no character list for realm " + realm);
        }

        CharacterChoice Choose(string name)
        {
            lock (_lock)
                return string.IsNullOrEmpty(name)
                    ? _characters.FirstOrDefault()
                    : _characters.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        void Create(string name, string className)
        {
            if (!Presets.TryGetValue(className, out ClassPreset preset))
                throw new InvalidOperationException($"Unknown class {className}. Classes: " + string.Join(", ", Presets.Keys));
            int slot;
            lock (_lock)
            {
                var used = _characters.Where(c => c.Realm == preset.Realm).Select(c => c.Slot).ToHashSet();
                slot = Enumerable.Range(0, 10).First(i => !used.Contains(i));
            }
            var face = new Godot.Collections.Array();
            for (int i = 0; i < 7; i++)
                face.Add(0);
            var stats = new Godot.Collections.Array();
            foreach (int stat in preset.Stats)
                stats.Add(stat);
            int seen = Volatile.Read(ref _charactersSeen);
            _client.CreateCharacter(new Dictionary
            {
                ["realm"] = preset.Realm, ["slot"] = slot, ["name"] = name, ["face"] = face, ["class_id"] = preset.ClassId,
                ["race"] = preset.Race, ["gender"] = preset.Gender, ["model"] = preset.Model, ["stats"] = stats,
            });
            WaitUntil(() => Volatile.Read(ref _charactersSeen) > seen, 3000);
            AskCharacters(preset.Realm);
        }

        void Require(Func<bool> done, int timeoutMs, string failure)
        {
            if (!WaitUntil(() => done() || _failure != null, timeoutMs) || !done())
                throw new InvalidOperationException(_failure ?? failure);
        }

        public static bool WaitUntil(Func<bool> done, int timeoutMs)
        {
            long end = Environment.TickCount64 + timeoutMs;
            while (!done())
            {
                if (Environment.TickCount64 >= end)
                    return false;
                Thread.Sleep(25);
            }
            return true;
        }

        // ---------------------------------------------------------------- the client's own rhythm

        void Pump()
        {
            long last = Environment.TickCount64;
            while (_running)
            {
                long now = Environment.TickCount64;
                float seconds = (now - last) / 1000f;
                last = now;
                try
                {
                    _client._Process(seconds);
                    if (_loggedIn && now >= _nextPing)
                    {
                        _nextPing = now + 10000;
                        _client.Ping();
                    }
                    if (_inWorld)
                        Tick(now, seconds);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"[{SessionName}] {e}");
                }
                Thread.Sleep(TickMs);
            }
        }

        void Tick(long now, float seconds)
        {
            bool moving;
            lock (_lock)
            {
                moving = _path != null;
                if (moving)
                    Walk(seconds);
            }
            if (now < _nextPosition)
                return;
            _nextPosition = now + (moving ? 100 : 1000);
            SendPosition();
        }

        void Walk(float seconds)
        {
            if (Dead)
            {
                StopWalking();
                return;
            }
            float left = RunSpeed * MaxSpeedPercent / 100f * seconds;
            _speedNow = left <= 0 ? 0 : RunSpeed * MaxSpeedPercent / 100f;
            while (left > 0 && _path.Count > 0)
            {
                Vector3 next = _path.Peek();
                Vector3 me = Position;
                Vector3 way = next - me;
                float flat = new Vector2(way.X, way.Y).Length();
                if (flat > 1f)
                    Heading = HeadingTo(next.X, next.Y);
                if (way.Length() <= left)
                {
                    X = next.X;
                    Y = next.Y;
                    Z = next.Z;
                    left -= way.Length();
                    _path.Dequeue();
                    continue;
                }
                Vector3 step = me + way / way.Length() * left;
                X = step.X;
                Y = step.Y;
                Z = step.Z;
                left = 0;
            }
            if (_path.Count == 0)
                StopWalking();
        }

        void StopWalking()
        {
            _path = null;
            _speedNow = 0;
            _nextPosition = 0;
        }

        void SendPosition()
        {
            float x, y, z, speed;
            int heading, zone;
            bool swimming;
            lock (_lock)
            {
                x = (float)X;
                y = (float)Y;
                z = (float)Z;
                speed = _speedNow;
                heading = Heading;
                zone = CurrentZone?.Id ?? 0;
                swimming = Swimming;
            }
            // TARGET_IN_VIEW 0x30: left out, every update would clear the server's TargetInView (zone_viewer.gd)
            _client.Position(x, y, z, speed, 0, zone, swimming ? StateSwimming : 0, !swimming, heading, TargetOid != 0 ? 0x30 : 0, HealthPercent, Oid);
        }

        int HeadingTo(double x, double y)
        {
            return (int)(Math.Atan2(-(x - X), y - Y) * 651.8986469) & 0xFFF;  // GameObject.GetHeading
        }

        // Most Atlantis zones have no WaterLevel in the world, so a test says when it is in the sea (the swim tool).
        public bool InSea { get; set; }
        public bool Swimming => InSea || (CurrentZone is { WaterLevel: > 0 } zone && Z < zone.WaterLevel);
        public ZoneInfo CurrentZone => _config.ZoneAt(Region, X, Y);

        // ---------------------------------------------------------------- what the server sends

        void OnSignal(string signal, object[] args)
        {
            switch (signal)
            {
                case DaocClient.SignalName.LoggedIn:
                    _loggedIn = true;
                    break;
                case DaocClient.SignalName.LoginFailed:
                    _failure = "login refused, eLoginError " + args[0];
                    break;
                case DaocClient.SignalName.Realm:
                    _realmKnown = true;
                    break;
                case DaocClient.SignalName.Disconnected:
                    _failure = (string)args[0];
                    _inWorld = false;
                    Record("disconnected", new { why = args[0] });
                    break;
                case DaocClient.SignalName.Characters:
                    OnCharacters((int)args[0], (Godot.Collections.Array)args[1]);
                    break;
                case DaocClient.SignalName.Packet when (int)args[0] == 0xA9:
                    OnPlayerPosition((byte[])args[1]);
                    break;
                case DaocClient.SignalName.Packet:
                    lock (UnknownPackets)
                        UnknownPackets[(int)args[0]] = UnknownPackets.GetValueOrDefault((int)args[0]) + 1;
                    break;
                case DaocClient.SignalName.World:
                    OnWorld((string)args[0], (Dictionary)args[1]);
                    break;
            }
        }

        void OnCharacters(int realm, Godot.Collections.Array list)
        {
            lock (_lock)
            {
                _characters.RemoveAll(c => c.Realm == realm);
                foreach (Dictionary c in list.Cast<Dictionary>())
                    _characters.Add(new CharacterChoice(realm, (int)c["slot"], ((string)c["name"]).TrimEnd(' '), (int)c["level"], ((string)c["class_name"]).TrimEnd(' ')));
            }
            Interlocked.Increment(ref _charactersSeen);
        }

        void OnWorld(string kind, Dictionary d)
        {
            lock (_lock)
                Apply(kind, d);
            if (!QuietKinds.Contains(kind))
                Record(kind, Named(kind, d));
        }

        void Apply(string kind, Dictionary d)
        {
            switch (kind)
            {
                case "you":
                    Oid = (int)d["oid"];
                    Region = (int)d["region"];
                    SetPosition((float)d["x"], (float)d["y"], (float)d["z"], (int)d["heading"]);
                    _entities.Clear();
                    _askedFor.Clear();
                    if (!_inWorld)
                    {
                        _client.PlayerInit();
                        _client.GameOpen();
                        _inWorld = true;
                    }
                    break;
                case "region_changed":
                    _inWorld = false;
                    _path = null;
                    _client.WorldInitAgain();
                    break;
                case "jump":
                    if ((long)d["x"] != 0 || (long)d["y"] != 0)
                        SetPosition((long)d["x"], (long)d["y"], (int)d["z"], (int)d["heading"]);
                    break;
                case "npc":
                case "object":
                    Remember(kind, d, (long)d["x"], (long)d["y"], (int)d["z"]);
                    break;
                case "player":
                    Remember(kind, d, (float)d["x"], (float)d["y"], (float)d["z"]);
                    break;
                case "move":
                    OnMove(d);
                    break;
                case "remove":
                    _entities.Remove((int)d["oid"]);
                    _askedFor.Remove((int)d["oid"]);
                    break;
                case "status":
                    Status = d;
                    if ((int)d["health_pct"] == 0)          // (a self kill sends no death packet of your own)
                        Dead = true;
                    else if (d.ContainsKey("health") && (int)d["health"] > 0)
                        Dead = false;
                    break;
                case "character":
                    Character = d;
                    break;
                case "points":
                    Points = d;
                    break;
                case "money":
                    Money = d;
                    break;
                case "max_speed":
                    MaxSpeedPercent = (int)d["percent"];
                    break;
                case "death":
                    if ((int)d["oid"] == Oid)
                        Dead = true;
                    else if (_entities.TryGetValue((int)d["oid"], out Entity died))
                        died.Health = 0;
                    break;
                case "revive":
                    if ((int)d["oid"] == Oid)
                        Dead = false;
                    break;
                case "combat":
                    if (_entities.TryGetValue((int)d["defender"], out Entity hit))
                        hit.Health = (int)d["health"];
                    break;
                case "model_change":
                    if (_entities.TryGetValue((int)d["oid"], out Entity changed))
                        changed.Model = (int)d["model"];
                    break;
                case "change_target":
                    SetTarget((int)d["oid"]);
                    break;
                case "los":                                 // no walls here: everything answers as seen
                    _client.LosReply((int)d["source"], (int)d["target"], true);
                    break;
                case "dialog":
                    LastDialog = d;
                    break;
                case "inventory":
                    foreach (Dictionary item in ((Godot.Collections.Array)d["items"]).Cast<Dictionary>())
                    {
                        if (item.ContainsKey("name") && !string.IsNullOrEmpty((string)item["name"]))
                            _inventory[(int)item["slot"]] = item;
                        else
                            _inventory.Remove((int)item["slot"]);
                    }
                    break;
                case "spell_line":                          // a list caster's spells: they aren't skill entries
                    if (!(bool)d["done"])
                        _spellLines[(int)d["line"]] = d;
                    break;
                case "skills":
                    if ((int)d["first"] == 0)
                        _skills.Clear();
                    _skills.AddRange(((Godot.Collections.Array)d["list"]).Cast<Dictionary>());
                    break;
            }
        }

        void SetPosition(double x, double y, double z, int heading)
        {
            X = x;
            Y = y;
            Z = z;
            Heading = heading;
            _path = null;
            _speedNow = 0;
            _nextPosition = 0;
        }

        void Remember(string kind, Dictionary d, double x, double y, double z)
        {
            int oid = (int)d["oid"];
            if (!_entities.TryGetValue(oid, out Entity e))
                _entities[oid] = e = new Entity { Oid = oid };
            e.Kind = kind;
            e.Name = d.TryGetValue("name", out object name) ? (string)name : "";
            e.Guild = d.TryGetValue("guild", out object guild) ? (string)guild : "";
            e.Level = d.TryGetValue("level", out object level) ? (int)level : 0;
            e.Realm = kind == "npc" ? ((int)d["flags"] >> 6) & 3 : 0;
            e.Model = (int)d["model"];
            e.X = x;
            e.Y = y;
            e.Z = z;
            e.SeenAt = Environment.TickCount64;
            _askedFor.Remove(oid);
        }

        void OnMove(Dictionary d)
        {
            int oid = (int)d["oid"];
            if (oid == Oid)
                return;
            if (!_entities.TryGetValue(oid, out Entity e))
            {
                // A living updated before it was ever created: ask, as the original client did (DaocClient.RequestObject)
                if (_askedFor.Add(oid))
                    _client.RequestObject(oid);
                return;
            }
            ZoneInfo zone = _config.Zones.FirstOrDefault(z => z.Id == (int)d["zone"]);
            if (zone != null)
            {
                e.X = zone.X * 8192 + (int)d["x"];
                e.Y = zone.Y * 8192 + (int)d["y"];
                e.Z = (int)d["z"];
            }
            e.Health = (int)d["health"];
            e.SeenAt = Environment.TickCount64;
        }

        // PlayerPositionUpdateHandler.CreateOutPak1127: another player. DaocClient doesn't read it, and a player is only
        // created on request (PlayerCreationRequestHandler): the original client asked when a position came from a stranger.
        void OnPlayerPosition(byte[] data)
        {
            var r = new DaocClient.Reader(data);
            float x = r.FloatLE(), y = r.FloatLE(), z = r.FloatLE();
            r.FloatLE();
            r.FloatLE();
            int session = r.Short(), oid = r.Short();
            r.Skip(11);                                 // zone, flags, seat, heading, action, RP flag, a zero
            int health = r.Byte() & 0x7F;               // (0x80: in attack mode)
            lock (_lock)
            {
                if (_entities.TryGetValue(oid, out Entity e))
                {
                    e.X = x;
                    e.Y = y;
                    e.Z = z;
                    e.Health = health;
                    e.SeenAt = Environment.TickCount64;
                }
                else if (_askedFor.Add(oid))
                {
                    var w = new DaocClient.Writer();
                    w.ShortLE((ushort)session);
                    w.Short(0);
                    _client.Send(0xD5, w);
                }
            }
        }

        object Named(string kind, Dictionary d)
        {
            var named = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var (key, value) in d)
            {
                if (key == "kind" || value is byte[])
                    continue;
                named[key] = value;
            }
            lock (_lock)
            {
                foreach (string key in new[] { "attacker", "defender", "caster", "target", "killer", "oid" })
                {
                    if (named.TryGetValue(key, out object v) && v is int oid)
                        named[key + "_name"] = oid == Oid ? "(you)" : _entities.TryGetValue(oid, out Entity e) ? e.Name : null;
                }
            }
            return named;
        }

        void Record(string kind, object data)
        {
            var e = new GameEvent(Interlocked.Increment(ref _seq), Environment.TickCount64 - _started, kind, data);
            lock (_events)
            {
                _events.AddLast(e);
                if (_events.Count > MaxEvents)
                    _events.RemoveFirst();
            }
        }

        // ---------------------------------------------------------------- what the tools read

        public List<GameEvent> EventsSince(long seq, ISet<string> kinds = null, int limit = 200)
        {
            lock (_events)
                return _events.Where(e => e.Seq > seq && (kinds == null || kinds.Contains(e.Kind))).TakeLast(limit).ToList();
        }

        public List<Entity> Nearby(string kind, string name, double radius, int limit)
        {
            lock (_lock)
                return _entities.Values
                    .Where(e => (kind == null || e.Kind == kind)
                        && (name == null || e.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                        && Distance(e) <= radius)
                    .OrderBy(Distance).Take(limit).ToList();
        }

        public double Distance(Entity e)
        {
            return Vector3.Distance(Position, e.Position);
        }

        public Entity Find(string who)
        {
            if (who == "@mob")
                return Nearby("npc", null, double.MaxValue, 200).FirstOrDefault(e => e.Health > 0 && e.Realm == 0 && e.Level > 0 && e.Guild.Length == 0 && e.Name.Length > 0);
            if (int.TryParse(who, out int oid))
            {
                lock (_lock)
                    return _entities.GetValueOrDefault(oid);
            }
            return Nearby(null, who, double.MaxValue, 50).FirstOrDefault(e => e.Health > 0 && e.Kind != "object")
                ?? Nearby(null, who, double.MaxValue, 1).FirstOrDefault();
        }

        public List<Dictionary> Skills()
        {
            lock (_lock)
                return _skills.ToList();
        }

        public List<(int line, Dictionary data)> SpellLines()
        {
            lock (_lock)
                return _spellLines.Select(l => (l.Key, l.Value)).ToList();
        }

        public List<Dictionary> Inventory()
        {
            lock (_lock)
                return _inventory.OrderBy(i => i.Key).Select(i => i.Value).ToList();
        }

        // ---------------------------------------------------------------- what the tools do

        public void Command(string text)
        {
            text = text.Trim();
            _client.Command(text.StartsWith('/') ? "&" + text[1..] : "&say " + text);
        }

        public void SetTarget(int oid)
        {
            TargetOid = oid;
            _client.Target(oid, oid == 0 ? 0 : 0x6000);
        }

        public void SetGroundTarget(Entity e) => _client.GroundTarget((int)e.X, (int)e.Y, (int)e.Z, 0x100);

        public void Face(Entity e)
        {
            lock (_lock)
                Heading = HeadingTo(e.X, e.Y);
            _client.Heading(Heading, TargetOid, TargetOid != 0 ? 0x30 : 0, 0);
            _nextPosition = 0;
        }

        // A path over the zone's navmesh, or a straight line where the zone has none. False: no mesh was used.
        public bool Walk(Vector3 to)
        {
            ZoneInfo zone = CurrentZone;
            List<Vector3> path = zone == null ? null : _nav.FindPath(zone.Id, Position, to);
            lock (_lock)
            {
                _path = new Queue<Vector3>(path ?? new List<Vector3> { to });
                _speedNow = RunSpeed * MaxSpeedPercent / 100f;
            }
            return path != null;
        }

        public void Halt()
        {
            lock (_lock)
                StopWalking();
        }

        public void Attack(bool on)
        {
            _client.Attack(on);
        }

        public void Sit(bool on)
        {
            _client.Sit(on);
        }

        public void Interact(int oid)
        {
            _client.Interact(oid, (int)X, (int)Y);
        }

        public void CastFromLine(int level, int line)
        {
            _client.UseSpell((float)X, (float)Y, (float)Z, _speedNow, Heading, true, level, line);
        }

        public void UseAbility(int index)
        {
            _client.UseSkill((float)X, (float)Y, (float)Z, _speedNow, Heading, true, index, 1);
        }

        public void UseSlot(int slot, int type)
        {
            _client.UseSlot((float)X, (float)Y, (float)Z, _speedNow, Heading, true, slot, type);
        }

        public void MoveItem(int from, int to, int count)
        {
            _client.MoveItem(from, to, count);
        }

        public void PickUp(int oid)
        {
            _client.PickUp((int)X, (int)Y, oid);
        }

        /// <summary>HouseEnterLeaveHandler: session id, house number, 1 enter / 0 leave.</summary>
        public void HouseEnter(int house, bool enter)
        {
            var w = new DaocClient.Writer();
            w.Short((ushort)Oid); w.Short((ushort)house); w.Byte((byte)(enter ? 1 : 0));
            _client.Send(0x0B, w);
        }

        /// <summary>HousingPlaceItemHandler: item flag, slot, house, unknown, position, method, rotation, indoor x, indoor y.</summary>
        public void HousePlace(int slot, int house, int position, int method, int x, int y)
        {
            var w = new DaocClient.Writer();
            w.Byte(0); w.Byte((byte)slot); w.Short((ushort)house); w.Byte(0); w.Byte((byte)position);
            w.Byte((byte)method); w.Byte(0); w.Short((ushort)(short)x); w.Short((ushort)(short)y);
            _client.Send(0x0C, w);
        }

        public void Buy(int itemSlot, int count, int menu)
        {
            _client.BuyItem((int)X, (int)Y, itemSlot, count, menu);
        }

        public void AnswerDialog(int response)
        {
            Dictionary d = LastDialog ?? throw new InvalidOperationException("No dialog is open.");
            _client.DialogResponse((int)d["data1"], (int)d["data2"], (int)d["data3"], (int)d["code"], response);
            LastDialog = null;
        }

        public static float Reach => MeleeReach;
    }
}
