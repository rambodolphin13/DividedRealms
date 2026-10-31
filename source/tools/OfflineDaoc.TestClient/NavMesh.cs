using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OfflineDaoc.TestClient
{
    // The server's own Detour library and zone meshes (LocalPathfindingMgr), so walking follows the ground bots walk on.
    public sealed partial class NavMesh
    {
        const float Factor = 1f / 32f;
        const int MaxPoints = 256;
        const int AllCrossings = 0x02;              // DT_STRAIGHTPATH_ALL_CROSSINGS
        const ushort Walkable = 0xffff ^ 0x10;      // EDtPolyFlags.All without Disabled
        const uint Success = 0x40000000;            // DT_SUCCESS
        static readonly float[] HalfExtents = { 1f, 2f, 1f };
        static readonly float[] GroundSearch = { 10f, 700f, 10f };   // 320 units across, 22,400 up and down (recast order)
        static readonly ushort[] Filter = { Walkable, 0 };

        readonly TestConfig _config;
        readonly Dictionary<int, IntPtr> _queries = new();
        readonly object _lock = new();

        [LibraryImport("Detour", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool LoadNavMesh(string file, ref IntPtr meshPtr);

        [LibraryImport("Detour")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CreateNavMeshQuery(IntPtr meshPtr, ref IntPtr queryPtr);

        [LibraryImport("Detour")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static partial uint PathStraight(IntPtr query, ReadOnlySpan<float> start, ReadOnlySpan<float> end,
            ReadOnlySpan<float> extents, ReadOnlySpan<ushort> filter, int options, out int count,
            Span<float> points, Span<ushort> flags);

        [LibraryImport("Detour")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static partial uint FindClosestPoint(IntPtr query, ReadOnlySpan<float> center, ReadOnlySpan<float> extents,
            ReadOnlySpan<ushort> filter, Span<float> point);

        public NavMesh(TestConfig config)
        {
            _config = config;
            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (name, _, _) =>
                name == "Detour" ? NativeLibrary.Load(config.DetourDll) : IntPtr.Zero);
        }

        IntPtr QueryFor(int zone)
        {
            if (_queries.TryGetValue(zone, out IntPtr query))
                return query;
            string file = Path.Combine(_config.NavmeshFolder, $"zone{zone:D3}.nav");
            IntPtr mesh = IntPtr.Zero;
            if (File.Exists(file) && LoadNavMesh(file, ref mesh))
                CreateNavMeshQuery(mesh, ref query);
            return _queries[zone] = query;
        }

        // The ground under a point, searched far up and down: a test rarely knows the height it asks for.
        public Vector3? Ground(int zone, Vector3 near)
        {
            lock (_lock)
            {
                return Snap(QueryFor(zone), near);
            }
        }

        static Vector3? Snap(IntPtr query, Vector3 near)
        {
            if (query == IntPtr.Zero)
                return null;
            float[] point = new float[3];
            uint status = FindClosestPoint(query, Recast(near), GroundSearch, Filter, point);
            if ((status & Success) == 0)
                return null;
            return new Vector3(point[0] / Factor, point[2] / Factor, point[1] / Factor);
        }

        // The way from one region point towards another inside a zone's mesh; null without a mesh or a way.
        public List<Vector3> FindPath(int zone, Vector3 from, Vector3 to)
        {
            lock (_lock)
            {
                IntPtr query = QueryFor(zone);
                if (Snap(query, from) is not Vector3 start || Snap(query, to) is not Vector3 end)
                    return null;
                float[] points = new float[MaxPoints * 3];
                ushort[] flags = new ushort[MaxPoints];
                uint status = PathStraight(query, Recast(start), Recast(end), HalfExtents, Filter, AllCrossings, out int count, points, flags);
                if ((status & Success) == 0 || count == 0)
                    return null;
                var path = new List<Vector3>(count);
                for (int i = 0; i < count; i++)
                    path.Add(new Vector3(points[i * 3] / Factor, points[i * 3 + 2] / Factor, points[i * 3 + 1] / Factor));
                return path;
            }
        }

        static float[] Recast(Vector3 v)
        {
            return new[] { v.X * Factor, v.Z * Factor, v.Y * Factor };
        }
    }
}
