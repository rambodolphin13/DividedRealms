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
