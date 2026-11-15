using System.Globalization;
using CEM.Utils;

namespace CEM.World
{
    /// <summary>
    /// Bridges narrow cracks between walkable floors in an exported zone .obj (recast units, y up) before Recast runs.
    ///
    /// Dungeon pieces are separate meshes; where two pieces meet, their floors can stop a few units short of each
    /// other. Recast's cells are 0.1 units (3.2 world) and it erodes the walkable area by the agent radius (3 cells), so a
    /// 5-world-unit crack becomes a ~29-unit unwalkable strip and splits the mesh. In the 1.130 Labyrinth (zone 245)
    /// this cut the dungeon into 7 large islands: the Albion and Hibernia entrances each reached about 120 of 2,655
    /// spawns. Bridging the cracks joins them (2,415 spawns in one piece, every entrance).
    ///
    /// A bridge is two triangles spanning the overlap of two boundary edges of walkable triangles that are near
    /// parallel, face each other, are 0.2-12 world units apart horizontally and within 16 world units of height.
    /// Walls in the crack still rasterize on top of a bridge, so a closed joint stays closed.
    /// Opt-in with --seal-cracks (2026-11-15).
    /// </summary>
    internal static class ObjCrackSealer
    {
        private const double MaxGap = 12.0 / 32.0;
        private const double MinGap = 0.2 / 32.0;
        private const double MaxDz = 16.0 / 32.0;
        private const double WalkableNormalY = 0.70;
        private const double Cell = 0.5;
        private const double MinEdge = 0.05;

        private readonly record struct V3(double X, double Y, double Z);
        private readonly record struct Edge(V3 P, V3 Q, double Nx, double Nz, double Length);

        public static int Seal(string objPath)
        {
            var verts = new List<V3>();
            var faces = new List<(int A, int B, int C)>();
            foreach (string line in File.ReadLines(objPath))
            {
                if (line.StartsWith("v "))
                {
                    string[] s = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    verts.Add(new V3(Parse(s[1]), Parse(s[2]), Parse(s[3])));
                }
                else if (line.StartsWith("f "))
                {
                    string[] s = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    faces.Add((Index(s[1]), Index(s[2]), Index(s[3])));
                }
            }

            var walkable = new List<(int A, int B, int C)>();
            var edgeUse = new Dictionary<(long, long, long, long, long, long), int>();
            foreach (var f in faces)
            {
                V3 a = verts[f.A], b = verts[f.B], c = verts[f.C];
                var n = Cross(Sub(b, a), Sub(c, a));
                double len = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
                if (len < 1e-9 || n.Y / len <= WalkableNormalY)
                    continue;
                walkable.Add(f);
                foreach (var (p, q) in TriEdges(verts, f))
                {
                    var k = EdgeKey(p, q);
                    edgeUse[k] = edgeUse.GetValueOrDefault(k) + 1;
                }
            }

            var bound = new List<Edge>();
            foreach (var f in walkable)
            {
                int[] idx = [f.A, f.B, f.C];
                for (int i = 0; i < 3; i++)
                {
                    V3 p = verts[idx[i]], q = verts[idx[(i + 1) % 3]], r = verts[idx[(i + 2) % 3]];
                    if (edgeUse[EdgeKey(p, q)] != 1)
                        continue;
                    double dx = q.X - p.X, dz = q.Z - p.Z, l = Math.Sqrt(dx * dx + dz * dz);
                    if (l < MinEdge)
                        continue;
                    double nx = -dz / l, nz = dx / l;
                    if (nx * (r.X - p.X) + nz * (r.Z - p.Z) > 0)
                    {
                        nx = -nx;
                        nz = -nz;
                    }
                    bound.Add(new Edge(p, q, nx, nz, l));
                }
            }

            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < bound.Count; i++)
            {
                var e = bound[i];
                for (int gx = Cl(Math.Min(e.P.X, e.Q.X)) - 1; gx <= Cl(Math.Max(e.P.X, e.Q.X)) + 1; gx++)
                    for (int gz = Cl(Math.Min(e.P.Z, e.Q.Z)) - 1; gz <= Cl(Math.Max(e.P.Z, e.Q.Z)) + 1; gz++)
                    {
                        if (!grid.TryGetValue((gx, gz), out var list))
                            grid[(gx, gz)] = list = new List<int>();
                        list.Add(i);
                    }
            }

            var bridges = new List<(V3, V3, V3)>();
            var candidates = new HashSet<int>();
            for (int i = 0; i < bound.Count; i++)
            {
                var e = bound[i];
                double ux = (e.Q.X - e.P.X) / e.Length, uz = (e.Q.Z - e.P.Z) / e.Length;
                candidates.Clear();
                for (int gx = Cl(Math.Min(e.P.X, e.Q.X)); gx <= Cl(Math.Max(e.P.X, e.Q.X)); gx++)
                    for (int gz = Cl(Math.Min(e.P.Z, e.Q.Z)); gz <= Cl(Math.Max(e.P.Z, e.Q.Z)); gz++)
                        if (grid.TryGetValue((gx, gz), out var list))
                            candidates.UnionWith(list);

                foreach (int j in candidates)
                {
                    if (j <= i)
                        continue;
                    var o = bound[j];
                    if (e.Nx * o.Nx + e.Nz * o.Nz > -0.97)
                        continue;
                    double s1 = (o.P.X - e.P.X) * e.Nx + (o.P.Z - e.P.Z) * e.Nz;
                    double s2 = (o.Q.X - e.P.X) * e.Nx + (o.Q.Z - e.P.Z) * e.Nz;
                    if (s1 < MinGap || s1 > MaxGap || s2 < MinGap || s2 > MaxGap)
                        continue;
                    double t1 = ((o.P.X - e.P.X) * ux + (o.P.Z - e.P.Z) * uz) / e.Length;
                    double t2 = ((o.Q.X - e.P.X) * ux + (o.Q.Z - e.P.Z) * uz) / e.Length;
                    double lo = Math.Max(0, Math.Min(t1, t2)), hi = Math.Min(1, Math.Max(t1, t2));
                    if ((hi - lo) * e.Length < MinEdge || Math.Abs(t2 - t1) < 1e-9)
                        continue;

                    V3 a = Lerp(e.P, e.Q, lo), b = Lerp(e.P, e.Q, hi);
                    V3 c = Lerp(o.P, o.Q, (hi - t1) / (t2 - t1)), d = Lerp(o.P, o.Q, (lo - t1) / (t2 - t1));
                    if (Math.Abs(a.Y - d.Y) > MaxDz || Math.Abs(b.Y - c.Y) > MaxDz)
                        continue;
                    bridges.Add(Upward(a, b, c));
                    bridges.Add(Upward(a, c, d));
                }
            }

            if (bridges.Count == 0)
                return 0;

            using (var w = new StreamWriter(objPath, append: true))
            {
                w.WriteLine("g crack_bridges");
                int baseIndex = verts.Count;
                for (int k = 0; k < bridges.Count; k++)
                {
                    var (a, b, c) = bridges[k];
                    foreach (var v in new[] { a, b, c })
                        w.WriteLine(string.Format(CultureInfo.InvariantCulture, "v {0:0.#####} {1:0.#####} {2:0.#####}", v.X, v.Y, v.Z));
                    w.WriteLine($"f {baseIndex + 3 * k + 1} {baseIndex + 3 * k + 2} {baseIndex + 3 * k + 3}");
                }
            }
            Log.Normal($"Crack sealer: {walkable.Count} walkable triangles, {bound.Count} open floor edges, {bridges.Count / 2} crack bridge(s) added");
            return bridges.Count / 2;
        }

        private static double Parse(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        private static int Index(string s) => int.Parse(s.Split('/')[0], CultureInfo.InvariantCulture) - 1;
        private static int Cl(double v) => (int)Math.Floor(v / Cell);
        private static V3 Sub(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        private static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        private static V3 Lerp(V3 a, V3 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        private static (V3, V3, V3) Upward(V3 a, V3 b, V3 c) => Cross(Sub(b, a), Sub(c, a)).Y > 0 ? (a, b, c) : (a, c, b);

        private static IEnumerable<(V3, V3)> TriEdges(List<V3> verts, (int A, int B, int C) f)
        {
            yield return (verts[f.A], verts[f.B]);
            yield return (verts[f.B], verts[f.C]);
            yield return (verts[f.C], verts[f.A]);
        }

        private static (long, long, long, long, long, long) EdgeKey(V3 p, V3 q)
        {
            (long, long, long) kp = (R(p.X), R(p.Y), R(p.Z)), kq = (R(q.X), R(q.Y), R(q.Z));
            if (kq.CompareTo(kp) < 0)
                (kp, kq) = (kq, kp);
            return (kp.Item1, kp.Item2, kp.Item3, kq.Item1, kq.Item2, kq.Item3);
        }

        private static long R(double v) => (long)Math.Round(v * 1000);
    }
}
