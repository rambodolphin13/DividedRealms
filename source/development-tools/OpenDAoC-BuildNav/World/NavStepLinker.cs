using CEM.Utils;

namespace CEM.World
{
    /// <summary>
    /// Joins large navmesh pieces that only touch at a step Recast cannot climb, with two-way off-mesh links.
    ///
    /// Recast's step limit is 0.9 units (28.8 world). In the 1.130 Labyrinth (zone 245) the bottom flooded hall
    /// (38 spawns) hangs off a ramp whose top meets the next floor at a ~74-unit lip, so it came out as its own
    /// island. After pass 1 this reads the built .nav (DT_POLYREF64 tile set, the format the server loads), groups
    /// polygons into connected pieces (internal neighbours, tile-border portals, existing off-mesh links) and, for
    /// every pair of pieces of at least <see cref="MinPiecePolys"/> polygons, finds polygon vertices of the two
    /// pieces that are at most 48 world units apart sideways and 16-96 apart in height. One link per 300 world units
    /// of contact is written to the .gset; pass 2 builds them in. Small pieces (tops of statues, pedestals, crates)
    /// are left alone. Opt-in with --step-links (2026-11-15).
    /// </summary>
    internal static class NavStepLinker
    {
        private const float Scale = 32f;
        private const int MinPiecePolys = 150;
        private const float MaxSide = 48f;
        private const float MinRise = 16f;
        private const float MaxRise = 96f;
        private const float Spacing = 300f;
        private const float Inset = 12f;

        private sealed class Poly
        {
            public int Tile;
            public float[][] Verts = [];
            public ushort[] Neis = [];
            public bool OffMesh;
        }

        private sealed record Tile(int X, int Y, int Base, int Count, float Climb);

        public static List<(OpenTK.Vector3 Start, OpenTK.Vector3 End)> FindLinks(string navPath)
        {
            var links = new List<(OpenTK.Vector3, OpenTK.Vector3)>();
            if (!File.Exists(navPath))
                return links;

            var (tiles, polys, offMesh) = Read(navPath);
            var parent = Enumerable.Range(0, polys.Count).ToArray();
            int Find(int a)
            {
                while (parent[a] != a)
                {
                    parent[a] = parent[parent[a]];
                    a = parent[a];
                }
                return a;
            }
            void Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a != b)
                    parent[a] = b;
            }

            // internal neighbours + tile-border portals
            var border = new Dictionary<(int, int, int), List<(int Poly, float[] A, float[] B)>>();
            for (int i = 0; i < polys.Count; i++)
            {
                var p = polys[i];
                if (p.OffMesh)
                    continue;
                var t = tiles[p.Tile];
                for (int j = 0; j < p.Neis.Length; j++)
                {
                    ushort ne = p.Neis[j];
                    if (ne == 0)
                        continue;
                    if ((ne & 0x8000) != 0)
                    {
                        var key = (t.X, t.Y, ne & 0xff);
                        if (!border.TryGetValue(key, out var list))
                            border[key] = list = new();
                        list.Add((i, p.Verts[j], p.Verts[(j + 1) % p.Verts.Length]));
                    }
                    else
                        Union(i, t.Base + ne - 1);
                }
            }
            foreach (var ((tx, ty, d), edges) in border)
            {
                (int dx, int dy) = d switch { 0 => (1, 0), 2 => (0, 1), 4 => (-1, 0), 6 => (0, -1), _ => (0, 0) };
                if (!border.TryGetValue((tx + dx, ty + dy, (d + 4) & 7), out var other))
                    continue;
                int ax = d is 0 or 4 ? 2 : 0;
                foreach (var (pi, a, b) in edges)
                {
                    float lo = Math.Min(a[ax], b[ax]), hi = Math.Max(a[ax], b[ax]), ya = (a[1] + b[1]) / 2;
                    float climb = tiles[polys[pi].Tile].Climb;
                    foreach (var (pj, e, f) in other)
                    {
                        float lo2 = Math.Min(e[ax], f[ax]), hi2 = Math.Max(e[ax], f[ax]);
                        if (Math.Min(hi, hi2) - Math.Max(lo, lo2) > 0.01f && Math.Abs((e[1] + f[1]) / 2 - ya) < 4 * climb + 1)
                            Union(pi, pj);
                    }
                }
            }
            // existing off-mesh links (ladders): join the polygons nearest to each end
            var centroids = new List<(int Poly, float X, float Y, float Z)>();
            for (int i = 0; i < polys.Count; i++)
                if (!polys[i].OffMesh)
                {
                    var v = polys[i].Verts;
                    centroids.Add((i, v.Average(q => q[0]), v.Average(q => q[1]), v.Average(q => q[2])));
                }
            foreach (var (poly, pos, rad) in offMesh)
                for (int end = 0; end < 2; end++)
                {
                    float x = pos[end * 3], y = pos[end * 3 + 1], z = pos[end * 3 + 2];
                    int nearest = -1;
                    float bestD = (rad + 1) * (rad + 1);
                    foreach (var c in centroids)
                    {
                        if (Math.Abs(c.Y - y) >= 200 / Scale)
                            continue;
                        float dd = (c.X - x) * (c.X - x) + (c.Z - z) * (c.Z - z);
                        if (dd < bestD)
                        {
                            bestD = dd;
                            nearest = c.Poly;
                        }
                    }
                    if (nearest >= 0)
                        Union(poly, nearest);
                }

            var size = new Dictionary<int, int>();
            for (int i = 0; i < polys.Count; i++)
                if (!polys[i].OffMesh)
                    size[Find(i)] = size.GetValueOrDefault(Find(i)) + 1;
            var big = size.Where(kv => kv.Value >= MinPiecePolys).Select(kv => kv.Key).ToHashSet();
            Log.Normal($"Step links: {tiles.Count} tile(s), {polys.Count} polygon(s), {size.Count} piece(s), {big.Count} of {MinPiecePolys}+ polygons");
            if (big.Count < 2)
                return links;

            // vertices of the big pieces in a 2D grid (world units), each moved a little toward its polygon's middle
            var grid = new Dictionary<(int, int), List<(int Piece, float X, float Y, float Z)>>();
            for (int i = 0; i < polys.Count; i++)
            {
                var p = polys[i];
                if (p.OffMesh || !big.Contains(Find(i)))
                    continue;
                float cx = p.Verts.Average(v => v[0]) * Scale, cz = p.Verts.Average(v => v[2]) * Scale;
                foreach (var v in p.Verts)
                {
                    float x = v[0] * Scale, y = v[2] * Scale, z = v[1] * Scale;
                    float dx = cx - x, dy = cz - y, l = MathF.Sqrt(dx * dx + dy * dy);
                    if (l > 1e-3f)
                    {
                        float k = Math.Min(Inset, l * 0.5f) / l;
                        x += dx * k;
                        y += dy * k;
                    }
                    var key = ((int)MathF.Floor(x / MaxSide), (int)MathF.Floor(y / MaxSide));
                    if (!grid.TryGetValue(key, out var list))
                        grid[key] = list = new();
                    list.Add((Find(i), x, y, z));
                }
            }

            var contacts = new List<(float Score, int A, int B, (float X, float Y, float Z) P, (float X, float Y, float Z) Q)>();
            foreach (var ((gx, gy), pts) in grid)
                foreach (var a in pts)
                    for (int ox = -1; ox <= 1; ox++)
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            if (!grid.TryGetValue((gx + ox, gy + oy), out var near))
                                continue;
                            foreach (var b in near)
                            {
                                if (b.Piece <= a.Piece)
                                    continue;
                                float side = MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
                                float rise = Math.Abs(a.Z - b.Z);
                                if (side > MaxSide || rise < MinRise || rise > MaxRise)
                                    continue;
                                contacts.Add((side + rise * 0.5f, a.Piece, b.Piece, (a.X, a.Y, a.Z), (b.X, b.Y, b.Z)));
                            }
                        }

            var chosen = new Dictionary<(int, int), List<(float X, float Y, float Z)>>();
            foreach (var c in contacts.OrderBy(c => c.Score))
            {
                var key = (c.A, c.B);
                if (!chosen.TryGetValue(key, out var list))
                    chosen[key] = list = new();
                if (list.Any(s => (s.X - c.P.X) * (s.X - c.P.X) + (s.Y - c.P.Y) * (s.Y - c.P.Y) < Spacing * Spacing))
                    continue;
                list.Add(c.P);
                links.Add((new OpenTK.Vector3(c.P.X, c.P.Y, c.P.Z), new OpenTK.Vector3(c.Q.X, c.Q.Y, c.Q.Z)));
            }
            Log.Normal($"Step links: {big.Count} large piece(s), {chosen.Count} touching pair(s), {links.Count} link(s)");
            return links;
        }

        private static (List<Tile>, List<Poly>, List<(int Poly, float[] Pos, float Rad)>) Read(string path)
        {
            var tiles = new List<Tile>();
            var polys = new List<Poly>();
            var offMesh = new List<(int, float[], float)>();
            using var br = new BinaryReader(File.OpenRead(path));
            br.ReadInt32();
            br.ReadInt32();
            int tileCount = br.ReadInt32();
            br.ReadBytes(28); // dtNavMeshParams
            for (int n = 0; n < tileCount; n++)
            {
                ulong reference = br.ReadUInt64();
                int size = br.ReadInt32();
                br.ReadInt32(); // padding
                if (reference == 0 || size == 0)
                    break;
                byte[] data = br.ReadBytes(size);
                using var t = new BinaryReader(new MemoryStream(data));
                int[] h = new int[15];
                for (int i = 0; i < 15; i++)
                    h[i] = t.ReadInt32();
                float[] hf = new float[10];
                for (int i = 0; i < 10; i++)
                    hf[i] = t.ReadSingle();
                int polyCount = h[6], vertCount = h[7], maxLinks = h[8], detailMeshes = h[9], detailVerts = h[10],
                    detailTris = h[11], bvNodes = h[12], offMeshCount = h[13];
                var verts = new float[vertCount][];
                for (int i = 0; i < vertCount; i++)
                    verts[i] = [t.ReadSingle(), t.ReadSingle(), t.ReadSingle()];
                int tileIndex = tiles.Count, baseIndex = polys.Count;
                for (int i = 0; i < polyCount; i++)
                {
                    t.ReadUInt32();
                    var vs = new ushort[6];
                    var ns = new ushort[6];
                    for (int k = 0; k < 6; k++)
                        vs[k] = t.ReadUInt16();
                    for (int k = 0; k < 6; k++)
                        ns[k] = t.ReadUInt16();
                    t.ReadUInt16();
                    int count = t.ReadByte();
                    int type = t.ReadByte() >> 6;
                    polys.Add(new Poly
                    {
                        Tile = tileIndex,
                        Verts = vs.Take(count).Select(v => verts[v]).ToArray(),
                        Neis = ns.Take(count).ToArray(),
                        OffMesh = type == 1,
                    });
                }
                t.BaseStream.Seek(16L * maxLinks + 12L * detailMeshes + 12L * detailVerts + 4L * detailTris + 16L * bvNodes, SeekOrigin.Current);
                for (int i = 0; i < offMeshCount; i++)
                {
                    var pos = new float[6];
                    for (int k = 0; k < 6; k++)
                        pos[k] = t.ReadSingle();
                    float rad = t.ReadSingle();
                    int poly = t.ReadUInt16();
                    t.ReadBytes(6);
                    offMesh.Add((baseIndex + poly, pos, rad));
                }
                tiles.Add(new Tile(h[2], h[3], baseIndex, polyCount, hf[2]));
            }
            return (tiles, polys, offMesh);
        }
    }
}
