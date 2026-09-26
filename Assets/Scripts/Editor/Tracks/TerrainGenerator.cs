using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>A creek crossing under a bridge, carved into the terrain.</summary>
    public readonly struct Creek
    {
        public readonly Vector2 Center;

        /// <summary>Direction the creek flows (across the road).</summary>
        public readonly Vector2 Along;

        /// <summary>Direction of the road at the bridge.</summary>
        public readonly Vector2 Across;

        public readonly float HalfWidth;
        public readonly float BankWidth;
        public readonly float Reach;
        public readonly float BedHeight;

        public Creek(Vector2 center, Vector2 along, Vector2 across, float halfWidth, float bankWidth, float reach, float bedHeight)
        {
            Center = center;
            Along = along;
            Across = across;
            HalfWidth = halfWidth;
            BankWidth = bankWidth;
            Reach = reach;
            BedHeight = bedHeight;
        }

        /// <summary>True when a map position lies in the creek channel or its banks, plus a margin.</summary>
        public bool Contains(Vector2 point, float margin)
        {
            Vector2 offset = point - Center;
            return Mathf.Abs(Vector2.Dot(offset, Along)) <= Reach + margin
                   && Mathf.Abs(Vector2.Dot(offset, Across)) <= HalfWidth + BankWidth + margin;
        }
    }

    /// <summary>Terrain generated for a track, plus the data scenery placement needs.</summary>
    public sealed class GeneratedTerrain
    {
        public TerrainData Data;
        public List<Creek> Creeks;
        internal RoadField Field;
    }

    /// <summary>
    /// Builds the TerrainData for a track: valley-shaped hills, the road carved in with embankments,
    /// creeks under bridges, painted ground layers and a forest of terrain trees kept clear of the road.
    /// </summary>
    public static class TerrainGenerator
    {
        // Terrain under the road sits this far below the road surface so it never pokes through.
        private const float UnderRoadDepth = 0.35f;

        // Creek shape around each bridge.
        private const float CreekAbutment = 4f;
        private const float CreekBankWidth = 6f;
        private const float CreekReach = 90f;
        private const float CreekEndFade = 25f;

        private const float MapEdgeMargin = 12f;
        private const float MaxTreeSlope = 38f;

        public static GeneratedTerrain Generate(TrackDefinition definition, TrackLayout layout, EnvironmentAssets assets, string assetPath)
        {
            int resolution = definition.HeightmapResolution;
            float size = definition.TerrainSize;
            float cell = size / (resolution - 1);
            var roads = new List<RoadPath> { layout.Main };
            roads.AddRange(layout.Shortcuts);

            float influence = definition.RoadBevelWidth + definition.EmbankmentWidth;
            var field = RoadField.Compute(roads, resolution, cell, 0f, influence);
            var creeks = BuildCreeks(definition, layout);

            var data = new TerrainData { heightmapResolution = resolution };
            data.size = new Vector3(size, definition.TerrainMaxHeight, size);
            data.SetHeights(0, 0, BuildHeights(definition, field, creeks));

            data.terrainLayers = assets.TerrainLayers;
            data.alphamapResolution = definition.AlphamapResolution;
            data.SetAlphamaps(0, 0, BuildAlphamaps(definition, roads, data));

            data.treePrototypes = assets.TreePrefabs.Select(prefab => new TreePrototype { prefab = prefab }).ToArray();
            data.SetTreeInstances(PlaceTrees(definition, field, creeks, data), snapToHeightmap: true);

            if (AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
            EditorAssetUtility.EnsureFolder(System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            AssetDatabase.CreateAsset(data, assetPath);

            return new GeneratedTerrain { Data = data, Creeks = creeks, Field = field };
        }

        /// <summary>Natural ground height before the road is carved in: noise hills rising to a rim at the map edge.</summary>
        public static float BaseHeight(TrackDefinition definition, float x, float z)
        {
            float size = definition.TerrainSize;
            var offset = SeedOffset(definition.Seed, 1);
            float noise = 0f;
            float amplitude = 0.5f;
            float frequency = 1f / definition.NoiseScale;
            for (int octave = 0; octave < 4; octave++)
            {
                noise += amplitude * Mathf.PerlinNoise(x * frequency + offset.x, z * frequency + offset.y);
                amplitude *= 0.5f;
                frequency *= 2.1f;
            }

            float u = x / size * 2f - 1f;
            float v = z / size * 2f - 1f;
            float edge = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 4f) + Mathf.Pow(Mathf.Abs(v), 4f), 0.25f);
            float rim = definition.RimHeight * SmoothStep((edge - definition.RimStart) / (1f - definition.RimStart));
            return definition.BaseHeight + definition.NoiseAmplitude * noise / 0.9375f + rim;
        }

        /// <summary>0..1 forest density; below the clearing amount there are no trees.</summary>
        public static float ForestDensity(TrackDefinition definition, float x, float z)
        {
            var offset = SeedOffset(definition.Seed, 2);
            return Mathf.PerlinNoise(x / 70f + offset.x, z / 70f + offset.y);
        }

        private static float[,] BuildHeights(TrackDefinition definition, RoadField field, List<Creek> creeks)
        {
            int resolution = definition.HeightmapResolution;
            float maxHeight = definition.TerrainMaxHeight;
            float carveEdge = definition.RoadBevelWidth * 0.6f;
            var heights = new float[resolution, resolution];

            for (int iz = 0; iz < resolution; iz++)
            {
                float z = field.WorldCoordinate(iz);
                for (int ix = 0; ix < resolution; ix++)
                {
                    float x = field.WorldCoordinate(ix);
                    float height = BaseHeight(definition, x, z);
                    float distance = field.Distance[iz, ix];

                    if (!float.IsPositiveInfinity(distance))
                    {
                        float roadHeight = field.RoadHeight[iz, ix];
                        float beyondRoad = distance - field.HalfWidth[iz, ix] - carveEdge;

                        // Under a bridge the ground must stay well clear of the deck; the creek is carved afterwards.
                        float depth = field.OnBridge[iz, ix] ? UnderRoadDepth + 1f : UnderRoadDepth;
                        height = beyondRoad <= 0f
                            ? roadHeight - depth
                            : Mathf.Lerp(roadHeight - definition.RoadBevelDrop, height, SmoothStep(beyondRoad / definition.EmbankmentWidth));
                    }

                    foreach (var creek in creeks)
                        height = Mathf.Min(height, CarveCreek(creek, new Vector2(x, z), height));

                    heights[iz, ix] = Mathf.Clamp01(height / maxHeight);
                }
            }

            return heights;
        }

        private static float CarveCreek(Creek creek, Vector2 point, float height)
        {
            Vector2 offset = point - creek.Center;
            float along = Mathf.Abs(Vector2.Dot(offset, creek.Along));
            if (along > creek.Reach)
                return height;

            float across = Mathf.Abs(Vector2.Dot(offset, creek.Across));
            float bank = SmoothStep((across - creek.HalfWidth) / creek.BankWidth);
            float endFade = SmoothStep((along - (creek.Reach - CreekEndFade)) / CreekEndFade);
            return Mathf.Lerp(creek.BedHeight, height, Mathf.Max(bank, endFade));
        }

        private static List<Creek> BuildCreeks(TrackDefinition definition, TrackLayout layout)
        {
            var creeks = new List<Creek>();
            foreach (var bridge in definition.Bridges)
            {
                var center = layout.Main.Get(layout.SampleIndexAt(layout.AnchorDistance(bridge.center)));
                creeks.Add(new Creek(
                    TrackLayout.Flat(center.Position),
                    TrackLayout.Flat(center.Right).normalized,
                    TrackLayout.Flat(center.Forward).normalized,
                    Mathf.Max(2f, bridge.length * 0.5f - CreekAbutment),
                    CreekBankWidth,
                    CreekReach,
                    center.Position.y - bridge.creekDepth));
            }

            return creeks;
        }

        private static float[,,] BuildAlphamaps(TrackDefinition definition, List<RoadPath> roads, TerrainData data)
        {
            int resolution = definition.AlphamapResolution;
            float size = definition.TerrainSize;
            float cell = size / resolution;
            var field = RoadField.Compute(roads, resolution, cell, cell * 0.5f, 8f);
            var maps = new float[resolution, resolution, 4];

            for (int iz = 0; iz < resolution; iz++)
            {
                float z = field.WorldCoordinate(iz);
                for (int ix = 0; ix < resolution; ix++)
                {
                    float x = field.WorldCoordinate(ix);
                    float roadEdge = field.Distance[iz, ix] - field.HalfWidth[iz, ix];
                    float dirt = float.IsPositiveInfinity(roadEdge) ? 0f : 1f - SmoothStep((roadEdge - 0.5f) / 4f);
                    float rock = SmoothStep((data.GetSteepness(x / size, z / size) - 30f) / 12f) * (1f - dirt);
                    float forest = SmoothStep((ForestDensity(definition, x, z) - definition.ClearingAmount - 0.2f) / 0.15f)
                                   * (1f - dirt) * (1f - rock);
                    float grass = (1f - dirt) * (1f - rock) * (1f - forest);

                    float total = dirt + rock + forest + grass;
                    maps[iz, ix, EnvironmentAssets.GrassLayer] = grass / total;
                    maps[iz, ix, EnvironmentAssets.ForestLayer] = forest / total;
                    maps[iz, ix, EnvironmentAssets.DirtLayer] = dirt / total;
                    maps[iz, ix, EnvironmentAssets.RockLayer] = rock / total;
                }
            }

            return maps;
        }

        private static TreeInstance[] PlaceTrees(TrackDefinition definition, RoadField field, List<Creek> creeks, TerrainData data)
        {
            var random = new System.Random(definition.Seed + 101);
            float size = definition.TerrainSize;
            float spacing = definition.TreeSpacing;
            int cells = Mathf.FloorToInt(size / spacing);
            var trees = new List<TreeInstance>();

            for (int gz = 0; gz < cells; gz++)
            {
                for (int gx = 0; gx < cells; gx++)
                {
                    float x = (gx + 0.5f + (float)(random.NextDouble() - 0.5) * 0.8f) * spacing;
                    float z = (gz + 0.5f + (float)(random.NextDouble() - 0.5) * 0.8f) * spacing;
                    double variant = random.NextDouble();
                    float scale = 0.8f + (float)random.NextDouble() * 0.45f;
                    float stretch = 0.9f + (float)random.NextDouble() * 0.3f;
                    float rotation = (float)random.NextDouble() * Mathf.PI * 2f;

                    if (!CanPlaceTree(definition, field, creeks, data, x, z))
                        continue;

                    trees.Add(new TreeInstance
                    {
                        position = new Vector3(x / size, 0f, z / size),
                        prototypeIndex = variant < 0.6 ? 0 : 1,
                        widthScale = scale,
                        heightScale = scale * stretch,
                        rotation = rotation,
                        color = Color.white,
                        lightmapColor = Color.white,
                    });
                }
            }

            return trees.ToArray();
        }

        private static bool CanPlaceTree(TrackDefinition definition, RoadField field, List<Creek> creeks, TerrainData data, float x, float z)
        {
            float size = definition.TerrainSize;
            if (x < MapEdgeMargin || z < MapEdgeMargin || x > size - MapEdgeMargin || z > size - MapEdgeMargin)
                return false;

            if (ForestDensity(definition, x, z) < definition.ClearingAmount)
                return false;

            // The field only extends a limited range from each road (infinity beyond it), which covers the clearance.
            if (field.EdgeDistanceAt(x, z) < definition.RoadBevelWidth + definition.TreeClearance)
                return false;

            var point = new Vector2(x, z);
            foreach (var creek in creeks)
            {
                if (creek.Contains(point, 3f))
                    return false;
            }

            return data.GetSteepness(x / size, z / size) <= MaxTreeSlope;
        }

        private static Vector2 SeedOffset(int seed, int salt)
        {
            var random = new System.Random(seed * 31 + salt);
            return new Vector2((float)random.NextDouble() * 1000f + 100f, (float)random.NextDouble() * 1000f + 100f);
        }

        private static float SmoothStep(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
