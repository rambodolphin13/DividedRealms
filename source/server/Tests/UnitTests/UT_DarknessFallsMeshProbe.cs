using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool: every certified Darkness Falls route on a real mesh, leg by leg, with the check the server's mover applies
    /// (issue #3). Read only. Env: OFFLINE_DAOC_NAV_ROOT (folder with navmesh/zone249.nav and lib/Detour.dll),
    /// OFFLINE_DAOC_PROOF_OUT.
    /// </summary>
    [TestFixture, Explicit("Darkness Falls mesh probe tool"), NonParallelizable]
    public class UT_DarknessFallsMeshProbe
    {
        [Test]
        public void ProbeCertifiedRoutes()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            Assume.That(root != null && output != null);
            Assembly assembly = typeof(AutonomousDarknessFallsNavigation).Assembly;
            string resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("darkness_falls_navigation_proofs.json", StringComparison.Ordinal));
            AutonomousDarknessFallsNavigation.Certificate certificate;
            using (Stream stream = assembly.GetManifestResourceStream(resource))
                certificate = JsonSerializer.Deserialize<AutonomousDarknessFallsNavigation.Certificate>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var zone = new Zone(null, 249, "zone249", 0, 0, 65536 * 8, 65536 * 8, 249, false, 0, false, 0, 0, 0, 0, 0);
            var lines = new List<string>();
            var failed = new Dictionary<(eRealm, string), int>();
            int legs = 0, routes = 0, whole = 0;
            WrappedPathfindingNode[] nodes = new WrappedPathfindingNode[256];
            try
            {
                LocalPathfindingMgr.LoadNavMesh(zone);
                foreach (var proof in certificate.Spawns)
                {
                    foreach (var route in proof.Routes)
                    {
                        routes++;
                        bool ok = true;
                        foreach (float[][] waypoints in new[] { route.InWaypoints, route.OutWaypoints })
                        {
                            for (int i = 1; i < waypoints.Length; i++)
                            {
                                legs++;
                                Vector3 start = new(waypoints[i - 1][0], waypoints[i - 1][1], waypoints[i - 1][2]);
                                Vector3 end = new(waypoints[i][0], waypoints[i][1], waypoints[i][2]);
                                PathfindingResult result = nav.GetPathStraight(zone, start, end, nav.DefaultFilters, nodes);
                                if (AutonomousDarknessFallsNavigation.MayQueuePath(true, AutonomousDarknessFallsPolicy.RegionId, true, result, nodes, start, end, certificate.TraversalLinks))
                                    continue;
                                ok = false;
                                var key = (route.Realm, $"{start.X:0},{start.Y:0},{start.Z:0}>{end.X:0},{end.Y:0},{end.Z:0} {result.Status}");
                                failed[key] = failed.GetValueOrDefault(key) + 1;
                            }
                        }
                        if (ok) whole++;
                    }
                }
            }
            finally
            {
                LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
            lines.Add($"routes {routes} complete {whole} legs {legs} failed leg kinds {failed.Count}");
            lines.AddRange(failed.OrderByDescending(pair => pair.Value).Take(30).Select(pair => $"{pair.Key.Item1} {pair.Key.Item2} x{pair.Value}"));
            File.WriteAllLines(output, lines);
        }
    }
}
