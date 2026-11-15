using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CEM.Utils;
using MNL;
using OpenTK;

namespace CEM.Client.ZoneExporter
{
  /// <summary>
  /// Dungeon
  /// </summary>
  partial class Zone2Obj
  {
    private void ExtractDungeon(Dictionary<string, string> proxyValues)
    {
      // Is offset internally (ExtractMeshesFromPlace)

      NiFile[] models = ParseChunk("dungeon.chunk", proxyValues);
      _skippedPlaces = FindHeightOutliers("dungeon.place");
      Matrix4[] matrices = ExtractMeshesFromPlace("dungeon.place", models, null, true, both: false);
      // hack: the place file looks identical to the prop file, atleast the first entries that I use
      ExtractMeshesFromPlace("dungeon.prop", models, matrices, true, both: false);
    }

    // Placement indexes (into the matrices list) left out of the mesh, and the props hanging on them.
    private HashSet<int> _skippedPlaces = new();

    // Recast keeps span heights in 13 bits, so one mesh can only be ~8191 cells tall (about 26,000 world units at
    // the demo's cell height). The 1.130 Labyrinth (zone 245) has 15 stray "collapsed" caps at the map edges
    // ~20,000 below its 1,159 real pieces; they stretch the height range and every floor above ~26,000 was cut
    // off. When a dungeon is that tall, leave out small clusters (under 2% of the pieces) that sit far apart from
    // the rest. A scan of every 1.130 dungeon (2026-11-15) found only zone 245 matching.
    private const float OutlierGap = 4096f;
    private const float OutlierSpan = 20000f;
    private const float OutlierFraction = 0.02f;

    private HashSet<int> FindHeightOutliers(string dungeonPlacementFile)
    {
      var skipped = new HashSet<int>();
      var stream = ClientData.FindCSV(Zone, dungeonPlacementFile);
      if (stream == null)
        return skipped;

      var heights = new List<(int index, float z)>();
      using (TextReader reader = new StreamReader(stream))
      {
        string input;
        while ((input = reader.ReadLine()) != null)
        {
          if (input.Trim() == string.Empty) continue;
          float z = float.Parse(input.Split(',')[3], CultureInfo.InvariantCulture);
          if (z < -65536) continue; // same skip as ExtractMeshesFromPlace, so indexes line up
          heights.Add((heights.Count, z));
        }
      }
      if (heights.Count == 0)
        return skipped;

      heights.Sort((a, b) => a.z.CompareTo(b.z));
      if (heights[^1].z - heights[0].z <= OutlierSpan)
        return skipped;

      var clusters = new List<List<(int index, float z)>> { new() { heights[0] } };
      for (int i = 1; i < heights.Count; i++)
      {
        if (heights[i].z - heights[i - 1].z > OutlierGap)
          clusters.Add(new());
        clusters[^1].Add(heights[i]);
      }
      if (clusters.Count < 2)
        return skipped;

      foreach (var cluster in clusters)
      {
        if (cluster.Count >= OutlierFraction * heights.Count)
          continue;
        foreach (var piece in cluster)
          skipped.Add(piece.index);
        Log.Warn("{0}: leaving {1} stray piece(s) at z {2:0}..{3:0} out of the mesh (dungeon is too tall for Recast)",
          Zone, cluster.Count, cluster[0].z, cluster[^1].z);
      }
      return skipped;
    }

    private NiFile[] ParseChunk(string name, Dictionary<string, string> proxyValues)
    {
      var models = new List<NiFile>();
      using (TextReader reader = new StreamReader(ClientData.FindCSV(Zone, name)))
      {
        string input;
        while ((input = reader.ReadLine()) != null)
        {
          // Locate the file
          string file = input;
          if (proxyValues.ContainsKey(file))
          {
            file = proxyValues[file];
          }
          Stream stream = ClientData.FindDNIF(Zone, file);
          if (stream == null)
          {
            Log.Error("Dungeon Model not found: {0}", file);
            models.Add(null);
            continue;
          }

          // Load the model
          NiFile model;
          using (var br = new BinaryReader(stream))
            model = new NiFile(br, file);

          if (!model.Loaded)
            continue;

          models.Add(model);
        }
      }
      return models.ToArray();
    }

    private Matrix4[] ExtractMeshesFromPlace(string dungeonPlacementFile, NiFile[] models, Matrix4[] oldWorldMatrices, bool invertTris = false, bool both = false)
    {
      //hack: return if there is no prop file (it happens)
      var stream = ClientData.FindCSV(Zone, dungeonPlacementFile);
      if (dungeonPlacementFile.EndsWith(".prop") && stream == null)
      {
        return new Matrix4[0];
      }

      var worldMatrices = new List<Matrix4>();
      using (TextReader reader = new StreamReader(stream))
      {
        // find the file
        string input;
        int nifIndex = 0;
        while ((input = reader.ReadLine()) != null)
        {
          if (input.Trim() == string.Empty) continue;

          string[] split = input.Split(',');
          int modelid = int.Parse(split[0], CultureInfo.InvariantCulture);
          float x = float.Parse(split[1], CultureInfo.InvariantCulture);
          float y = float.Parse(split[2], CultureInfo.InvariantCulture);
          float z = float.Parse(split[3], CultureInfo.InvariantCulture);

          if (z < -65536) continue; // Trollheim has a broken piece at -72k that screws up recast
          float angle = float.Parse(split[4], CultureInfo.InvariantCulture);
          float ax = float.Parse(split[5], CultureInfo.InvariantCulture);
          float ay = float.Parse(split[6], CultureInfo.InvariantCulture);
          float az = float.Parse(split[7], CultureInfo.InvariantCulture);
          //unkown?
          int parentid = 0;
          if (dungeonPlacementFile.EndsWith(".prop"))
          {
            parentid = int.Parse(split[9], CultureInfo.InvariantCulture);
          }

          NiFile model = models[modelid];

          if (model == null)
          {
            Log.Warn("Model with id " + modelid + " is missing!");
          }

          Matrix4 worldMatrix = Matrix4.Identity;
          worldMatrix *= Matrix4.CreateFromAxisAngle(new Vector3(ax, ay, -az), angle);
          worldMatrix *= Matrix4.CreateTranslation(x, y, z);

          bool skip;
          if (!dungeonPlacementFile.EndsWith(".prop"))
          {
            worldMatrix *= Matrix4.CreateScale(-1, 1, 1);
            worldMatrix *= Matrix4.CreateTranslation(8192 * 4 - 768, 8192 * 4 - 768, 16000);
            skip = _skippedPlaces.Contains(worldMatrices.Count);
            worldMatrices.Add(worldMatrix);
          }
          else
          {
            worldMatrix = worldMatrix * oldWorldMatrices[parentid];
            skip = _skippedPlaces.Contains(parentid);
          }

          if (skip)
          {
            nifIndex++;
            continue;
          }

          // pickee is not good enough for recast (since it generates it's own optimized navmesh from the collision layer)
          AddModelToObj(model, worldMatrix, /*new[] {"pickee"} */ new[] { "collide", "collidee", "collision" }, invertTris, both: both);
          ExtractDoor(model, worldMatrix, nifIndex);
          ExtractLadder(model, worldMatrix);

          nifIndex++;
        }
      }

      return worldMatrices.ToArray();
    }
  }
}
