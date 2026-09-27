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
    /// creeks under bridges, painted ground layers with ragged natural edges, a forest of terrain trees kept clear of the
    /// road, and grass and bushes (terrain details, which don't collide) along the verges and in open ground.
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

        // Vegetation: one detail cell per this many metres; none on slopes steeper than this, or this close to a road edge.
        private const float DetailCellSize = 2f;
        private const float MaxDetailSlope = 35f;
        private const float DetailRoadClearance = 1.2f;

        public static GeneratedTerrain Generate(TrackDefinition definition, TrackLayout layout, EnvironmentAssets assets, TrackTheme theme,
            string assetPath)
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
            data.alphamapResolution = definition.AlphamapResolution;

            // Save the asset before painting it: the ground layers' weights live in textures that are only stored with
            // the terrain when it is already an asset (painted first, they were lost and the ground showed one layer).
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
            EditorAssetUtility.EnsureFolder(System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            AssetDatabase.CreateAsset(data, assetPath);

            data.SetHeights(0, 0, BuildHeights(definition, field, creeks));

            data.terrainLayers = assets.TerrainLayers;
            data.SetAlphamaps(0, 0, BuildAlphamaps(definition, theme, roads, creeks, data));

            data.treePrototypes = assets.TreePrefabs.Select(prefab => new TreePrototype { prefab = prefab }).ToArray();
            data.SetTreeInstances(PlaceTrees(definition, field, creeks, data, assets.TreeShares), snapToHeightmap: true);
            PlaceDetails(definition, theme, field, creeks, data, assets);
            EditorUtility.SetDirty(data);

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
            return Terrace(definition, definition.BaseHeight + definition.NoiseAmplitude * noise / 0.9375f + rim);
        }

        /// <summary>
        /// Turns smooth hills into flat benches with steep walls between them, like canyon country: each step of
        /// <see cref="TrackDefinition.TerraceHeight"/> stays level, then rises over the step's last fraction. Off at 0.
        /// </summary>
        private static float Terrace(TrackDefinition definition, float height)
        {
            float step = definition.TerraceHeight;
            if (step <= 0f)
                return height;

            float level = height / step;
            float bench = Mathf.Floor(level);
            float wall = definition.TerraceWall;
            return (bench + SmoothStep((level - bench - (1f - wall)) / wall)) * step;
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

        private static float[,,] BuildAlphamaps(TrackDefinition definition, TrackTheme theme, List<RoadPath> roads, List<Creek> creeks,
            TerrainData data)
        {
            int resolution = definition.AlphamapResolution;
            float size = definition.TerrainSize;
            float cell = size / resolution;
            var field = RoadField.Compute(roads, resolution, cell, cell * 0.5f, 8f);
            var maps = new float[resolution, resolution, 4];
            var verge = SeedOffset(definition.Seed, 3);

            for (int iz = 0; iz < resolution; iz++)
            {
                float z = field.WorldCoordinate(iz);
                for (int ix = 0; ix < resolution; ix++)
                {
                    float x = field.WorldCoordinate(ix);
                    // The worn verge beside the road, with a ragged edge that wanders in and out.
                    float roadEdge = field.Distance[iz, ix] - field.HalfWidth[iz, ix];
                    float wander = Mathf.PerlinNoise(x * 0.12f + verge.x, z * 0.12f + verge.y) * 2f - 1f;
                    float dirt = float.IsPositiveInfinity(roadEdge) ? 0f : 1f - SmoothStep((roadEdge - 0.5f - wander * 1f) / (4f + wander * 1.5f));

                    // Dry creeks are washes of bare, worn ground.
                    if (!theme.CreekWater)
                        dirt = Mathf.Max(dirt, DryWash(creeks, new Vector2(x, z)));

                    float outcrop = (Mathf.PerlinNoise(x * 0.05f + verge.y, z * 0.05f + verge.x) - 0.5f) * 10f;
                    float rock = SmoothStep((data.GetSteepness(x / size, z / size) - theme.RockSlope + outcrop) / theme.RockBlend) * (1f - dirt);
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

        /// <summary>1 in a creek's bed, fading out across its banks and towards its ends.</summary>
        private static float DryWash(List<Creek> creeks, Vector2 point)
        {
            float wash = 0f;
            foreach (var creek in creeks)
            {
                Vector2 offset = point - creek.Center;
                float along = Mathf.Abs(Vector2.Dot(offset, creek.Along));
                if (along > creek.Reach)
                    continue;

                float across = Mathf.Abs(Vector2.Dot(offset, creek.Across));
                float bed = 1f - SmoothStep((across - creek.HalfWidth) / (creek.BankWidth * 0.5f));
                float ends = 1f - SmoothStep((along - (creek.Reach - CreekEndFade)) / CreekEndFade);
                wash = Mathf.Max(wash, bed * ends);
            }

            return wash;
        }

        private static TreeInstance[] PlaceTrees(TrackDefinition definition, RoadField field, List<Creek> creeks, TerrainData data, float[] shares)
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
                        prototypeIndex = PickShape(shares, variant),
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

        /// <summary>
        /// Grass tufts and bushes as terrain details: thick along the verges, in patches across open ground, sparse under
        /// the trees; none on the road, its shoulders, creeks or steep slopes. They are drawn only near the camera.
        /// </summary>
        private static void PlaceDetails(TrackDefinition definition, TrackTheme theme, RoadField field, List<Creek> creeks, TerrainData data,
            EnvironmentAssets assets)
        {
            if (assets.DetailPrefabs == null || assets.DetailPrefabs.Length < 2 || theme.VegetationDensity <= 0f)
            {
                data.detailPrototypes = new DetailPrototype[0];
                return;
            }

            float size = definition.TerrainSize;
            int resolution = Mathf.RoundToInt(size / DetailCellSize);
            data.SetDetailResolution(resolution, 16);
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.detailPrototypes = new[]
            {
                DetailLayer(assets.DetailPrefabs[EnvironmentAssets.GrassDetail], 0.8f, 1.4f, 0.7f, 1.4f),
                DetailLayer(assets.DetailPrefabs[EnvironmentAssets.BushDetail], 0.7f, 1.5f, 0.6f, 1.3f),
            };

            var random = new System.Random(definition.Seed + 303);
            var patches = SeedOffset(definition.Seed, 4);
            var grass = new int[resolution, resolution];
            var bushes = new int[resolution, resolution];
            float cell = size / resolution;
            float clearance = definition.RoadBevelWidth + DetailRoadClearance;
            for (int iz = 0; iz < resolution; iz++)
            {
                float z = (iz + 0.5f) * cell;
                for (int ix = 0; ix < resolution; ix++)
                {
                    float x = (ix + 0.5f) * cell;
                    double roll = random.NextDouble();
                    float edge = field.EdgeDistanceAt(x, z);
                    if (edge < clearance || data.GetSteepness(x / size, z / size) > MaxDetailSlope || InCreek(creeks, x, z))
                        continue;

                    float open = 1f - SmoothStep((ForestDensity(definition, x, z) - definition.ClearingAmount - 0.1f) / 0.25f);
                    float roadside = float.IsPositiveInfinity(edge) ? 0f : 1f - SmoothStep((edge - clearance - 2f) / 6f);
                    float patch = Mathf.PerlinNoise(x / 9f + patches.x, z / 9f + patches.y);
                    float amount = theme.VegetationDensity * (0.9f * roadside + 1.1f * open * patch + 0.15f);
                    grass[iz, ix] = Mathf.Clamp(Mathf.FloorToInt(amount * 2.2f + (float)roll), 0, 4);

                    float bushChance = theme.VegetationDensity * (0.08f * roadside + 0.05f * open + 0.06f * (1f - open)) * patch;
                    bushes[iz, ix] = roll < bushChance ? 1 : 0;
                }
            }

            data.SetDetailLayer(0, 0, EnvironmentAssets.GrassDetail, grass);
            data.SetDetailLayer(0, 0, EnvironmentAssets.BushDetail, bushes);
        }

        private static DetailPrototype DetailLayer(GameObject prefab, float minWidth, float maxWidth, float minHeight, float maxHeight)
        {
            var prototype = new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minWidth = minWidth,
                maxWidth = maxWidth,
                minHeight = minHeight,
                maxHeight = maxHeight,
                noiseSpread = 0.4f,
                healthyColor = Color.white,
                dryColor = Color.white,
                alignToGround = 0.6f,
                positionJitter = 1f,
            };

            if (!prototype.Validate(out string problem))
                Debug.LogWarning($"[RALLY X Build] Detail {prefab.name}: {problem}");
            return prototype;
        }

        private static bool InCreek(List<Creek> creeks, float x, float z)
        {
            var point = new Vector2(x, z);
            foreach (var creek in creeks)
            {
                if (creek.Contains(point, 1f))
                    return true;
            }

            return false;
        }

        /// <summary>The tree shape for a random number in 0..1, each shape taking its share of the range in order.</summary>
        private static int PickShape(float[] shares, double variant)
        {
            double upTo = 0.0;
            for (int i = 0; i < shares.Length - 1; i++)
            {
                upTo += shares[i];
                if (variant < upTo)
                    return i;
            }

            return shares.Length - 1;
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
