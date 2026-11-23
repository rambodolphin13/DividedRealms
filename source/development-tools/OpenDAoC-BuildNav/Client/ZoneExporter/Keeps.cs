using System.Text.Json;
using CEM.Utils;
using MNL;
using OpenTK;

namespace CEM.Client.ZoneExporter
{
    /// <summary>
    /// Server-placed keep components (DR-035). New Frontiers keeps and towers are not client zone fixtures: the server
    /// sends each component (skin, grid offset, quarter-turn heading) and the client draws a frontiers/NIFS piece there.
    /// Without them the frontier meshes had no keep walls, gates or door polys, so bots planned straight through closed
    /// keeps and DoorMgr could not register a single New Frontiers keep door (369 errors every boot).
    /// The placement file comes from tools/export_keep_components.py (the server's own position formula); each piece
    /// is added like a colliding fixture: its collision geometry, and its numbered doors as Door areas.
    /// </summary>
    partial class Zone2Obj
    {
        private sealed record KeepPiece(int region, int keep, int component, int skin, string model, float x, float y, float z, float heading);

        private sealed record KeepFile(List<KeepPiece> components);

        private static List<KeepPiece> _keepPieces;

        private static List<KeepPiece> KeepPieces()
        {
            if (_keepPieces != null) return _keepPieces;
            string path = Program.Arguments.KeepsFile;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return _keepPieces = new List<KeepPiece>();
            var file = JsonSerializer.Deserialize<KeepFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return _keepPieces = file?.components ?? new List<KeepPiece>();
        }

        private void ExportKeeps()
        {
            var pieces = KeepPieces().Where(p => p.region == Zone.Region.ID &&
                p.x >= Zone.XOffset && p.x < Zone.XOffset + Zone.Width && p.y >= Zone.YOffset && p.y < Zone.YOffset + Zone.Height).ToList();
            if (pieces.Count == 0) return;
            var models = new Dictionary<string, NiFile>(StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (KeepPiece piece in pieces)
            {
                if (!models.TryGetValue(piece.model, out NiFile model))
                {
                    using Stream stream = ClientData.OpenExactly(piece.model);
                    if (stream != null)
                        using (var reader = new BinaryReader(stream))
                            model = new NiFile(reader, Path.GetFileName(piece.model));
                    models[piece.model] = model is { Loaded: true } ? model : null;
                    model = models[piece.model];
                }
                if (model == null)
                {
                    Log.Warn("Keep {0} component {1}: model {2} not found", piece.keep, piece.component, piece.model);
                    continue;
                }
                // Same convention as a zone fixture (ExportNifs): mirrored Y, heading in degrees about Z, world position.
                float angle = (float)(piece.heading / 180f * Math.PI) * KeepHeadingSign;
                Matrix4 world = Matrix4.CreateScale(new Vector3(1, -1, 1)) * Matrix4.CreateRotationZ(angle) *
                                Matrix4.CreateTranslation(new Vector3(piece.x, piece.y, piece.z));
                int fixtureId = 800000 + piece.keep * 100 + piece.component; // unique per component, apart from client fixture ids
                AddModelToObj(model, world, ["collide", "collidee", "collision"], true, fixtureId: fixtureId, openDoorLeaves: true);
                ExtractDoor(model, world, fixtureId);
                added++;
            }
            Log.Normal("Zone {0}: added {1} keep component(s) from {2}", Zone.ID, added, Program.Arguments.KeepsFile);
        }

        /// <summary>Heading direction of keep pieces; +1 like fixtures. KEEP_HEADING_SIGN=-1 flips it (placement check).</summary>
        private static float KeepHeadingSign =>
            Environment.GetEnvironmentVariable("KEEP_HEADING_SIGN") == "-1" ? -1f : 1f;
    }
}
