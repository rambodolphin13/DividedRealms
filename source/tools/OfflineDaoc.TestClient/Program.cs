using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OfflineDaoc.TestClient
{
    // Plays Offline DAoC with no window: as MCP tools for Claude and Codex (the default), or a saved scenario.
    //   OfflineDaoc.TestClient --runtime <folder> [--account aiclaude] [--shared-log <file>]
    //   OfflineDaoc.TestClient --runtime <folder> --scenario <file.json> [--verbose]
//   OfflineDaoc.TestClient --runtime <folder> --check      (no server needed)
    public static class Program
    {
        static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        static readonly object Output = new();

        public static int Main(string[] args)
        {
            string runtime = Option(args, "--runtime") ?? Environment.GetEnvironmentVariable("DAOC_TEST_RUNTIME");
            if (runtime == null)
            {
                Console.Error.WriteLine("Give --runtime <the install's runtime folder> or set DAOC_TEST_RUNTIME.");
                return 2;
            }
            var config = new TestConfig(runtime, Option(args, "--account") ?? "aitester", Option(args, "--shared-log"));
            var tools = new TestTools(config);
            if (args.Contains("--check"))
                return Check(config, tools.Nav);
            if (Option(args, "--world-audit") is string audit)
                return WorldAudit(config, tools.Nav, audit);
            if (Option(args, "--path-audit") is string pathAudit)
                return PathAudit(config, tools.Nav, pathAudit);
            string scenario = Option(args, "--scenario");
            int code = 0;
            if (scenario != null)
                code = RunScenario(tools, scenario, args.Contains("--verbose"));
            else
                Serve(tools);
            tools.LogoutAll();
            return code;
        }

        // Everything that works with the server off: the zone table, Detour and a walk across Camelot Hills.
        static int Check(TestConfig config, NavMesh nav)
        {
            ZoneInfo zone = config.ZoneAt(1, 560000, 512000);
            Console.WriteLine($"Zones: {config.Zones.Count}. Server port {config.Port}. 560000, 512000 in region 1 is {zone?.Name}.");
            var from = nav.Ground(zone.Id, new Vector3(560000, 512000, 2000));
            var to = nav.Ground(zone.Id, new Vector3(565000, 515000, 2000));
            Console.WriteLine($"Ground: {from} and {to}");
            List<Vector3> path = from == null || to == null ? null : nav.FindPath(zone.Id, from.Value, to.Value);
            Console.WriteLine(path == null ? "No path." : $"Path of {path.Count} points ending at {path[^1]}");
            return path == null ? 1 : 0;
        }

        // Every spawn checked against the zones and navmeshes, with the server off: mobs nothing can reach, and how well
        // each zone's mesh joins up, tried between random pairs of its own mobs.
        static int WorldAudit(TestConfig config, NavMesh nav, string file)
        {
            var random = new Random(1);
            var zones = new List<object>();
            int[] regions = { 1, 100, 200, 30, 73, 130, 70, 71, 72, 35, 36, 37, 40, 45, 46, 51, 61, 151, 181 };
            foreach (ZoneInfo zone in config.Zones.Where(z => regions.Contains(z.Region)))
            {
                var mobs = config.Query($"select Name, Level, X, Y, Z, Flags, RoamingRange from Mob where Region = {zone.Region} "
                    + $"and X >= {zone.X * 8192} and X < {(zone.X + zone.Width) * 8192} and Y >= {zone.Y * 8192} and Y < {(zone.Y + zone.Height) * 8192}", 100000)
                    .Where(m => Convert.ToInt32(m["Level"]) > 0).ToList();
                if (mobs.Count == 0)
                    continue;
                int off = 0, above = 0, below = 0, noMesh = 0;
                var points = new List<Vector3>();
                var worst = new Dictionary<string, int>();
                foreach (var m in mobs)
                {
                    var at = new Vector3(Convert.ToSingle(m["X"]), Convert.ToSingle(m["Y"]), Convert.ToSingle(m["Z"]));
                    if (nav.Ground(zone.Id, at) is not Vector3 ground)
                    {
                        noMesh++;
                        continue;
                    }
                    points.Add(ground);
                    bool flying = (Convert.ToInt32(m["Flags"]) & 1) != 0 || (Convert.ToInt32(m["Flags"]) & 4) != 0;
                    float flat = new Vector2(ground.X - at.X, ground.Y - at.Y).Length();
                    if (flat > 200)
                        off++;
                    else if (!flying && at.Z - ground.Z > 200)
                        above++;
                    else if (at.Z - ground.Z < -200)
                        below++;
                    else
                        continue;
                    string name = (string)m["Name"];
                    worst[name] = worst.GetValueOrDefault(name) + 1;
                }
                int tried = 0, joined = 0, partial = 0, tooLong = 0;
                for (int i = 0; i < 40 && points.Count > 1; i++)
                {
                    Vector3 from = points[random.Next(points.Count)], to = points[random.Next(points.Count)];
                    tried++;
                    List<Vector3> path = nav.FindPath(zone.Id, from, to);
                    if (path != null && Vector3.Distance(path[^1], to) < 150)
                        joined++;
                    else if (path != null && path.Count >= 256)
                        tooLong++;                                 // Detour's point limit: a walker replans from there
                    else if (path != null)
                        partial++;
                }
                zones.Add(new
                {
                    zone = zone.Id, name = zone.Name, region = zone.Region, mobs = mobs.Count, no_mesh = noMesh, off_mesh = off, in_air = above, underground = below,
                    paths_tried = tried, paths_joined = joined, paths_partial = partial, paths_too_long = tooLong,
                    worst = worst.OrderByDescending(p => p.Value).Take(5).Select(p => $"{p.Key} x{p.Value}"),
                });
            }
            File.WriteAllText(file, JsonSerializer.Serialize(zones));
            Console.WriteLine($"{zones.Count} zones");
            return 0;
        }

        /// <summary>
        /// Why paths in a zone fail: walks each pair the way a bot does, replanning from where a partial path stops, and groups
        /// the spawns that can reach each other. Spawns outside the largest group sit on a piece of mesh of their own.
        /// </summary>
        static int PathAudit(TestConfig config, NavMesh nav, string file)
        {
            var random = new Random(2);
            var zones = new List<object>();
            foreach (ZoneInfo zone in config.Zones)
            {
                var mobs = config.Query($"select Name, X, Y, Z from Mob where Region = {zone.Region} and X >= {zone.X * 8192} and X < {(zone.X + zone.Width) * 8192} "
                    + $"and Y >= {zone.Y * 8192} and Y < {(zone.Y + zone.Height) * 8192} and Level > 0", 600).ToList();
                var points = new List<(string Name, Vector3 At)>();
                foreach (var m in mobs)
                    if (nav.Ground(zone.Id, new Vector3(Convert.ToSingle(m["X"]), Convert.ToSingle(m["Y"]), Convert.ToSingle(m["Z"]))) is Vector3 ground)
                        points.Add(((string)m["Name"], ground));
                if (points.Count < 4)
                    continue;
                int firstTry = 0, replanned = 0, failed = 0;
                int[] group = Enumerable.Range(0, points.Count).ToArray();
                int Root(int i)
                {
                    while (group[i] != i)
                        i = group[i] = group[group[i]];
                    return i;
                }
                bool Walk(Vector3 from, Vector3 to, out int legs)
                {
                    legs = 0;
                    for (; legs < 8; legs++)
                    {
                        List<Vector3> path = nav.FindPath(zone.Id, from, to);
                        if (path == null || path.Count == 0)
                            return false;
                        if (Vector3.Distance(path[^1], to) < 150)
                            return true;
                        if (Vector3.Distance(path[^1], from) < 100)
                            return false;
                        from = path[^1];
                    }
                    return false;
                }
                for (int i = 0; i < 60; i++)
                {
                    int a = random.Next(points.Count), b = random.Next(points.Count);
                    if (!Walk(points[a].At, points[b].At, out int legs))
                    {
                        failed++;
                        continue;
                    }
                    if (legs == 0)
                        firstTry++;
                    else
                        replanned++;
                    group[Root(a)] = Root(b);
                }
                // Tie the rest in: each spawn tries the biggest group so far.
                for (int i = 0; i < points.Count; i++)
                {
                    int biggest = Enumerable.Range(0, points.Count).GroupBy(Root).OrderByDescending(g => g.Count()).First().Key;
                    if (Root(i) != biggest && Walk(points[i].At, points[biggest].At, out _))
                        group[Root(i)] = biggest;
                }
                var islands = Enumerable.Range(0, points.Count).GroupBy(Root).OrderByDescending(g => g.Count()).ToList();
                zones.Add(new
                {
                    zone = zone.Id, name = zone.Name, region = zone.Region, spawns = points.Count, first_try = firstTry, replanned, failed,
                    main_share = 100 * islands[0].Count() / points.Count, islands = islands.Count,
                    cut_off = islands.Skip(1).Take(6).Select(g => new
                    {
                        count = g.Count(), name = points[g.First()].Name, x = (int)points[g.First()].At.X, y = (int)points[g.First()].At.Y, z = (int)points[g.First()].At.Z,
                    }),
                });
            }
            File.WriteAllText(file, JsonSerializer.Serialize(zones));
            Console.WriteLine($"{zones.Count} zones");
            return 0;
        }

        static string Option(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // ---------------------------------------------------------------- MCP: JSON-RPC, a message a line on stdio

        static void Serve(TestTools tools)
        {
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                JsonObject request = JsonNode.Parse(line).AsObject();
                JsonNode id = request["id"]?.DeepClone();
                if (id == null)
                    continue;                           // a notification
                string method = (string)request["method"];
                JsonObject parameters = request["params"]?.AsObject();
                // Apart from the reader: a fight in one session must not hold up the other.
                Task.Run(() => Reply(stdout, id, Answer(tools, method, parameters, out string error), error));
            }
        }

        static void Reply(StreamWriter stdout, JsonNode id, JsonNode result, string error)
        {
            var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id };
            if (error == null)
                reply["result"] = result;
            else
                reply["error"] = new JsonObject { ["code"] = -32601, ["message"] = error };
            lock (Output)
                stdout.WriteLine(reply.ToJsonString());
        }

        static JsonNode Answer(TestTools tools, string method, JsonObject parameters, out string error)
        {
            error = null;
            switch (method)
            {
                case "initialize":
                    return new JsonObject
                    {
                        ["protocolVersion"] = parameters?["protocolVersion"]?.DeepClone() ?? "2025-06-18",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "daoc-tester", ["version"] = "1.0.0" },
                    };
                case "ping":
                    return new JsonObject();
                case "tools/list":
                    return new JsonObject
                    {
                        ["tools"] = new JsonArray(tools.All.Select(t => (JsonNode)new JsonObject
                        {
                            ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.Schema.DeepClone(),
                        }).ToArray()),
                    };
                case "tools/call":
                    return Call(tools, (string)parameters["name"], parameters["arguments"]?.AsObject() ?? new JsonObject());
                default:
                    error = "Unknown method " + method;
                    return null;
            }
        }

        static JsonNode Call(TestTools tools, string name, JsonObject arguments)
        {
            bool failed = !TryRun(tools, name, arguments, out string text);
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                ["isError"] = failed,
            };
        }

        static bool TryRun(TestTools tools, string name, JsonObject arguments, out string text)
        {
            try
            {
                Tool tool = tools.All.FirstOrDefault(t => t.Name == name) ?? throw new InvalidOperationException("Unknown tool " + name);
                object result = tool.Run(arguments);
                text = result as string ?? JsonSerializer.Serialize(result, Json);
                return true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                text = e.Message;
                return false;
            }
        }

        // ---------------------------------------------------------------- saved scenarios

        // { "name": ..., "steps": [ { "tool": "command", "args": { ... }, "expect": ["text"], "forbid": ["text"] } ] }
        static int RunScenario(TestTools tools, string file, bool verbose)
        {
            JsonObject scenario = JsonNode.Parse(File.ReadAllText(file)).AsObject();
            Console.WriteLine($"Scenario: {scenario["name"] ?? file}");
            int failures = 0, number = 0;
            foreach (JsonNode node in scenario["steps"].AsArray())
            {
                JsonObject step = node.AsObject();
                number++;
                bool ok = TryRun(tools, (string)step["tool"], step["args"]?.DeepClone().AsObject() ?? new JsonObject(), out string text);
                var problems = new List<string>();
                if (!ok)
                    problems.Add(text);
                foreach (JsonNode want in step["expect"]?.AsArray() ?? new JsonArray())
                {
                    if (!text.Contains((string)want, StringComparison.OrdinalIgnoreCase))
                        problems.Add("missing: " + want);
                }
                foreach (JsonNode bad in step["forbid"]?.AsArray() ?? new JsonArray())
                {
                    if (text.Contains((string)bad, StringComparison.OrdinalIgnoreCase))
                        problems.Add("found: " + bad);
                }
                string about = (string)step["about"] ?? (string)step["tool"];
                Console.WriteLine($"  {(problems.Count == 0 ? "PASS" : "FAIL")} {number}. {about}");
                if (verbose && problems.Count == 0)
                    Console.WriteLine("       " + text);
                if (problems.Count > 0)
                {
                    failures++;
                    problems.ForEach(p => Console.WriteLine("       " + p));
                    Console.WriteLine("       result: " + (text.Length > 1500 ? text[..1500] + " ..." : text));
                    if ((bool?)step["required"] == true)
                        break;
                }
            }
            Console.WriteLine(failures == 0 ? "All steps passed." : $"{failures} step(s) failed.");
            return failures == 0 ? 0 : 1;
        }
    }
}
