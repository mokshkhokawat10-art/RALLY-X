using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Places track furniture from the layout: guard rails, bridge structures, corner chevrons, jump warnings, marker
    /// posts, the start/finish gantry, rocks, creek water and invisible map-edge walls. Guard rails collide as plain
    /// boxes and are drawn as galvanised rails on posts; bridge posts and beams are visual only.
    /// </summary>
    public static class TrackPropsBuilder
    {
        // Guard rails go on the outside of corners tighter than this, and wherever the ground drops away.
        private const float BarrierCornerRadius = 45f;
        private const float BarrierDropHeight = 2.5f;
        private const int BarrierDilateSamples = 5;
        private const int MinBarrierRunSamples = 6;
        private const int BarrierSegmentSamples = 3;
        private const float BarrierHeight = 0.9f;
        private const float BarrierSink = 0.35f;

        private const float ChevronRadius = 55f;
        private const int ChevronSpacingSamples = 6;
        private const int ChevronWarningSamples = 15;

        private const int MarkerPostSpacingSamples = 10;
        private const float JunctionClearance = 20f;
        private const float DeckThickness = 0.6f;

        // Guard rail look: the rail's height above the road and its posts' spacing (samples).
        private const float RailTop = 0.6f;
        private const int RailPostSpacingSamples = 2;
        private const float JumpWarningDistance = 45f;

        /// <param name="generatedFolder">Folder for the meshes the props are drawn with (the track's generated folder).</param>
        public static void Build(Transform root, TrackLayout layout, GeneratedTerrain terrain, EnvironmentAssets assets, string generatedFolder)
        {
            var context = new Context(root, layout, terrain, assets, generatedFolder);
            bool[,] barriers = BarrierFlags(context);
            BuildBarriers(context, barriers);
            BuildBridges(context);
            BuildChevrons(context);
            BuildJumpSigns(context);
            BuildMarkerPosts(context, barriers);
            BuildStartGantry(context);
            BuildRocks(context);
            BuildWater(context);
            BuildBoundaryWalls(context);
        }

        private sealed class Context
        {
            public Context(Transform root, TrackLayout layout, GeneratedTerrain terrain, EnvironmentAssets assets, string generatedFolder)
            {
                GeneratedFolder = generatedFolder;
                Root = root;
                Layout = layout;
                Terrain = terrain;
                Assets = assets;
                Definition = layout.Definition;
                Size = Definition.TerrainSize;
                foreach (var shortcut in Definition.Shortcuts)
                {
                    Junctions.Add(layout.AnchorDistance(shortcut.from));
                    Junctions.Add(layout.AnchorDistance(shortcut.to));
                }
            }

            public string GeneratedFolder { get; }
            public Transform Root { get; }
            public TrackLayout Layout { get; }
            public GeneratedTerrain Terrain { get; }
            public EnvironmentAssets Assets { get; }
            public TrackDefinition Definition { get; }
            public float Size { get; }
            public List<float> Junctions { get; } = new List<float>();

            public float GroundHeight(Vector3 position) =>
                Terrain.Data.GetInterpolatedHeight(Mathf.Clamp01(position.x / Size), Mathf.Clamp01(position.z / Size));

            public bool NearJunction(float distance)
            {
                foreach (float junction in Junctions)
                {
                    float gap = Mathf.Abs(Mathf.Repeat(distance - junction + Layout.Main.Length * 0.5f, Layout.Main.Length) - Layout.Main.Length * 0.5f);
                    if (gap < JunctionClearance)
                        return true;
                }

                return false;
            }

            public Transform Group(string name)
            {
                var existing = Root.Find(name);
                if (existing != null)
                    return existing;

                var group = new GameObject(name).transform;
                group.SetParent(Root, false);
                return group;
            }
        }

        // ---------------------------------------------------------------- guard rails

        /// <summary>[side, sample]: side 0 = left, 1 = right.</summary>
        private static bool[,] BarrierFlags(Context context)
        {
            var samples = context.Layout.Main.Samples;
            int count = samples.Length;
            float bevel = context.Definition.RoadBevelWidth;
            var raw = new bool[2, count];

            for (int k = 0; k < count; k++)
            {
                var s = samples[k];
                if (s.IsBridge || context.NearJunction(s.Distance))
                    continue;

                if (s.Radius < BarrierCornerRadius)
                    raw[s.Curvature > 0f ? 0 : 1, k] = true;

                for (int side = 0; side < 2; side++)
                {
                    Vector3 probe = s.Position + s.Right * (Sign(side) * (s.HalfWidth + bevel + 3f));
                    if (s.Position.y - context.GroundHeight(probe) > BarrierDropHeight)
                        raw[side, k] = true;
                }
            }

            // Grow flags so rails start before and end after the hazard, then drop runs too short to matter.
            var flags = new bool[2, count];
            for (int side = 0; side < 2; side++)
            {
                for (int k = 0; k < count; k++)
                {
                    for (int d = -BarrierDilateSamples; d <= BarrierDilateSamples && !flags[side, k]; d++)
                        flags[side, k] = raw[side, Wrap(k + d, count)];
                    if (samples[k].IsBridge || context.NearJunction(samples[k].Distance))
                        flags[side, k] = false;
                }

                foreach (var (start, length) in Runs(flags, side, count))
                {
                    if (length >= MinBarrierRunSamples)
                        continue;
                    for (int k = 0; k < length; k++)
                        flags[side, Wrap(start + k, count)] = false;
                }
            }

            return flags;
        }

        private static void BuildBarriers(Context context, bool[,] flags)
        {
            var group = context.Group("GuardRails");
            var main = context.Layout.Main;
            int count = main.Count;
            float offset = context.Definition.RoadBevelWidth + 0.35f;
            var steel = new LowPolyMeshBuilder();
            var reflectors = new LowPolyMeshBuilder();

            for (int side = 0; side < 2; side++)
            {
                float sign = Sign(side);
                foreach (var (start, length) in Runs(flags, side, count))
                {
                    // What the cars hit: plain boxes, unchanged from the first tracks.
                    for (int k = 0; k < length - 1; k += BarrierSegmentSamples)
                    {
                        var a = main.Get(start + k);
                        var b = main.Get(start + Mathf.Min(k + BarrierSegmentSamples, length - 1));
                        Vector3 from = a.Position + a.Right * (sign * (a.HalfWidth + offset));
                        Vector3 to = b.Position + b.Right * (sign * (b.HalfWidth + offset));
                        var collider = Rail(group, "Rail", from, to, BarrierHeight, 0.25f, BarrierSink, context.Assets.Barrier);
                        if (collider == null)
                            continue;
                        Object.DestroyImmediate(collider.GetComponent<MeshRenderer>());
                        Object.DestroyImmediate(collider.GetComponent<MeshFilter>());
                    }

                    // What the player sees: a W-beam (two stacked bulges) facing the road, on posts, with a reflector on every third.
                    for (int k = 0; k < length - 1; k++)
                    {
                        var a = main.Get(start + k);
                        var b = main.Get(start + k + 1);
                        Vector3 from = a.Position + a.Right * (sign * (a.HalfWidth + offset - 0.1f));
                        Vector3 to = b.Position + b.Right * (sign * (b.HalfWidth + offset - 0.1f));
                        Vector3 inset = a.Right * (sign * 0.02f);
                        steel.Beam(from + Vector3.up * (RailTop - 0.08f), to + Vector3.up * (RailTop - 0.08f), 0.05f, 0.15f, Vector3.up);
                        steel.Beam(from + Vector3.up * (RailTop - 0.24f) + inset, to + Vector3.up * (RailTop - 0.24f) + inset, 0.05f, 0.15f, Vector3.up);
                    }

                    for (int k = 0; k < length; k += RailPostSpacingSamples)
                    {
                        var s = main.Get(start + k);
                        Vector3 post = s.Position + s.Right * (sign * (s.HalfWidth + offset - 0.02f));
                        float bottom = Mathf.Max(Mathf.Min(post.y, context.GroundHeight(post)) - 0.2f, post.y - 3f);
                        float top = post.y + RailTop - 0.02f;
                        var rotation = Quaternion.LookRotation(s.Forward);
                        steel.Box(new Vector3(post.x, (top + bottom) * 0.5f, post.z), new Vector3(0.1f, top - bottom, 0.12f), rotation);
                        if ((k / RailPostSpacingSamples) % 3 == 0)
                            reflectors.Box(post + Vector3.up * (RailTop + 0.02f), new Vector3(0.1f, 0.08f, 0.14f), rotation);
                    }
                }
            }

            DrawMesh(context, group, "GuardRailVisuals", LowPolyMeshBuilder.Combine("GuardRails", Vector3.zero, steel, reflectors),
                context.Assets.Galvanised, context.Assets.PostRed);
        }

        /// <summary>Saves a props mesh into the track's generated folder and draws it (no collider).</summary>
        private static void DrawMesh(Context context, Transform group, string name, Mesh mesh, params Material[] materials)
        {
            if (mesh.vertexCount == 0)
            {
                Object.DestroyImmediate(mesh);
                return;
            }

            AssetDatabase.CreateAsset(mesh, $"{context.GeneratedFolder}/{name}.asset");
            var drawn = new GameObject(name);
            drawn.transform.SetParent(group, false);
            drawn.AddComponent<MeshFilter>().sharedMesh = mesh;
            drawn.AddComponent<MeshRenderer>().sharedMaterials = materials;
            GameObjectUtility.SetStaticEditorFlags(drawn, StaticEditorFlags.BatchingStatic);
        }

        // ---------------------------------------------------------------- bridges

        private static void BuildBridges(Context context)
        {
            var group = context.Group("Bridges");
            var main = context.Layout.Main;
            var bridges = context.Definition.Bridges;
            for (int b = 0; b < bridges.Length; b++)
            {
                var deck = new List<TrackSample>();
                foreach (var sample in main.Samples)
                {
                    if (sample.BridgeIndex == b)
                        deck.Add(sample);
                }

                if (deck.Count < 2)
                    continue;

                var creek = context.Terrain.Creeks[b];
                for (int side = 0; side < 2; side++)
                {
                    for (int k = 0; k < deck.Count - 1; k += 2)
                    {
                        var a = deck[k];
                        var c = deck[Mathf.Min(k + 2, deck.Count - 1)];
                        Vector3 from = a.Position + a.Right * (Sign(side) * (a.HalfWidth - 0.12f));
                        Vector3 to = c.Position + c.Right * (Sign(side) * (c.HalfWidth - 0.12f));
                        Rail(group, "Railing", from, to, 1f, 0.2f, 0f, context.Assets.BridgeDeck);
                    }
                }

                foreach (int index in new[] { 0, deck.Count / 2, deck.Count - 1 })
                {
                    var s = deck[index];
                    for (int side = 0; side < 2; side++)
                    {
                        Vector3 top = s.Position + s.Right * (Sign(side) * (s.HalfWidth - 0.6f)) + Vector3.down * DeckThickness;
                        float bottom = Mathf.Min(creek.BedHeight, context.GroundHeight(top)) - 1f;
                        float height = top.y - bottom;
                        EditorPrimitives.Box("Pillar", group, top + Vector3.down * (height * 0.5f), Quaternion.LookRotation(s.Forward),
                            new Vector3(0.7f, height, 0.7f), context.Assets.Concrete, collider: true);
                    }
                }

                // Timber detail, drawn only: railing posts, and stringers and cross beams under the deck.
                var timber = new LowPolyMeshBuilder();
                for (int k = 0; k < deck.Count; k += 2)
                {
                    var s = deck[k];
                    var rotation = Quaternion.LookRotation(s.Forward);
                    for (int side = 0; side < 2; side++)
                        timber.Box(s.Position + s.Right * (Sign(side) * (s.HalfWidth - 0.12f)) + Vector3.up * 0.55f, new Vector3(0.16f, 1.15f, 0.16f), rotation);
                    timber.Box(s.Position + Vector3.down * (DeckThickness + 0.12f), new Vector3(s.Width + 0.4f, 0.24f, 0.3f), rotation);
                }

                for (int k = 0; k < deck.Count - 1; k++)
                {
                    var a = deck[k];
                    var c = deck[k + 1];
                    for (int side = 0; side < 2; side++)
                    {
                        float across = Sign(side) * (a.HalfWidth - 1.1f);
                        timber.Beam(a.Position + a.Right * across + Vector3.down * (DeckThickness + 0.35f),
                            c.Position + c.Right * across + Vector3.down * (DeckThickness + 0.35f), 0.35f, 0.5f, Vector3.up);
                    }
                }

                DrawMesh(context, group, $"BridgeDetails_{b + 1}", timber.ToMesh($"BridgeDetails_{b + 1}"), context.Assets.BridgeDeck);
            }
        }

        // ---------------------------------------------------------------- signs and posts

        private static void BuildChevrons(Context context)
        {
            var group = context.Group("CornerSigns");
            var main = context.Layout.Main;
            int count = main.Count;
            float offset = context.Definition.RoadBevelWidth + 1.4f;

            var tight = new bool[1, count];
            for (int k = 0; k < count; k++)
                tight[0, k] = main.Samples[k].Radius < ChevronRadius;

            foreach (var (start, length) in Runs(tight, 0, count))
            {
                if (length < 4)
                    continue;

                int apex = start;
                for (int k = 0; k < length; k++)
                {
                    if (Mathf.Abs(main.Get(start + k).Curvature) > Mathf.Abs(main.Get(apex).Curvature))
                        apex = start + k;
                }

                bool rightTurn = main.Get(apex).Curvature > 0f;
                int outside = rightTurn ? 0 : 1;
                var material = rightTurn ? context.Assets.ChevronRight : context.Assets.ChevronLeft;

                var positions = new List<int> { start - ChevronWarningSamples, apex };
                if (apex - ChevronSpacingSamples > start)
                    positions.Add(apex - ChevronSpacingSamples);
                if (apex + ChevronSpacingSamples < start + length)
                    positions.Add(apex + ChevronSpacingSamples);

                foreach (int index in positions)
                {
                    var s = main.Get(index);
                    if (s.IsBridge)
                        continue;
                    Vector3 basePosition = s.Position + s.Right * (Sign(outside) * (s.HalfWidth + offset));
                    PlaceChevron(group, basePosition, context.GroundHeight(basePosition), s.Forward, material, context.Assets.PostWhite);
                }
            }
        }

        private static void PlaceChevron(Transform group, Vector3 position, float groundHeight, Vector3 facing, Material face, Material post)
        {
            var root = new GameObject("Chevron").transform;
            root.SetParent(group, false);
            root.SetPositionAndRotation(new Vector3(position.x, groundHeight, position.z), Quaternion.LookRotation(facing));

            EditorPrimitives.Box("Post", root, root.position + Vector3.up * 0.8f, root.rotation, new Vector3(0.1f, 1.6f, 0.1f), post, collider: false);
            EditorPrimitives.Box("Back", root, root.position + Vector3.up * 1.45f + facing * 0.03f, root.rotation,
                new Vector3(1.12f, 0.82f, 0.04f), post, collider: false);

            // A quad is visible from its -Z side, which faces drivers approaching along `facing`.
            EditorPrimitives.Quad("Face", root, root.position + Vector3.up * 1.45f, root.rotation, new Vector2(1.1f, 0.8f), face);
        }

        /// <summary>A yellow crest warning on the right of the road before each jump.</summary>
        private static void BuildJumpSigns(Context context)
        {
            var group = context.Group("JumpSigns");
            var main = context.Layout.Main;
            float offset = context.Definition.RoadBevelWidth + 1.4f;
            foreach (var jump in context.Definition.Jumps)
            {
                var s = main.Get(context.Layout.SampleIndexAt(context.Layout.AnchorDistance(jump.start) - JumpWarningDistance));
                if (s.IsBridge || context.NearJunction(s.Distance))
                    continue;
                Vector3 basePosition = s.Position + s.Right * (s.HalfWidth + offset);
                PlaceChevron(group, basePosition, context.GroundHeight(basePosition), s.Forward, context.Assets.JumpSign, context.Assets.PostWhite);
            }
        }

        private static void BuildMarkerPosts(Context context, bool[,] barriers)
        {
            var group = context.Group("MarkerPosts");
            var main = context.Layout.Main;
            float offset = context.Definition.RoadBevelWidth + 0.5f;

            for (int k = 0; k < main.Count; k += MarkerPostSpacingSamples)
            {
                var s = main.Samples[k];
                if (s.IsBridge || context.NearJunction(s.Distance) || s.Distance < 30f)
                    continue;

                for (int side = 0; side < 2; side++)
                {
                    if (barriers[side, k])
                        continue;

                    Vector3 position = s.Position + s.Right * (Sign(side) * (s.HalfWidth + offset));
                    position.y = context.GroundHeight(position);
                    var rotation = Quaternion.LookRotation(s.Forward);
                    EditorPrimitives.Box("Post", group, position + Vector3.up * 0.5f, rotation, new Vector3(0.12f, 1f, 0.12f),
                        context.Assets.PostWhite, collider: false);
                    EditorPrimitives.Box("Reflector", group, position + Vector3.up * 1.02f, rotation, new Vector3(0.14f, 0.12f, 0.14f),
                        context.Assets.PostRed, collider: false);
                }
            }
        }

        private static void BuildStartGantry(Context context)
        {
            var group = context.Group("StartFinish");
            var s = context.Layout.Main.Samples[0];
            var rotation = Quaternion.LookRotation(s.Forward);
            float halfSpan = s.HalfWidth + context.Definition.RoadBevelWidth + 0.8f;
            const float gantryHeight = 5.5f;

            for (int side = 0; side < 2; side++)
            {
                Vector3 foot = s.Position + s.Right * (Sign(side) * halfSpan);
                foot.y = Mathf.Min(foot.y, context.GroundHeight(foot));
                float height = s.Position.y + gantryHeight - foot.y;
                EditorPrimitives.Box("GantryPost", group, foot + Vector3.up * (height * 0.5f), rotation, new Vector3(0.45f, height, 0.45f),
                    context.Assets.PostWhite, collider: true);
            }

            // The beam is out of reach of the cars, so it has no collider.
            Vector3 beamCentre = s.Position + Vector3.up * gantryHeight;
            EditorPrimitives.Box("GantryBeam", group, beamCentre, rotation, new Vector3(halfSpan * 2f + 0.45f, 1f, 0.5f),
                context.Assets.Banner, collider: false);
            Banner(group, beamCentre - s.Forward * 0.27f, rotation, context.Definition.DisplayName.ToUpperInvariant(), context.Assets);
            Banner(group, beamCentre + s.Forward * 0.27f, Quaternion.LookRotation(-s.Forward), "RALLY X", context.Assets);

            EditorPrimitives.Quad("StartLine", group, s.Position + Vector3.up * 0.02f, Quaternion.LookRotation(Vector3.down, s.Forward),
                new Vector2(s.Width, 1.6f), context.Assets.Checker);
        }

        private static void Banner(Transform parent, Vector3 position, Quaternion rotation, string text, EnvironmentAssets assets)
        {
            var banner = new GameObject($"Banner_{text}");
            banner.transform.SetParent(parent, false);
            banner.transform.SetPositionAndRotation(position, rotation);
            var textMesh = banner.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.font = assets.BannerFont;
            textMesh.fontSize = 64;
            textMesh.characterSize = 0.09f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(1f, 0.82f, 0.2f);
            banner.GetComponent<MeshRenderer>().sharedMaterial = assets.BannerText;
        }

        // ---------------------------------------------------------------- scenery

        private static void BuildRocks(Context context)
        {
            var group = context.Group("Rocks");
            var random = new System.Random(context.Definition.Seed + 202);
            var main = context.Layout.Main;
            float bevel = context.Definition.RoadBevelWidth;
            var prefabs = context.Assets.RockPrefabs;

            // Roadside rocks: a hazard for running wide, never on the road itself.
            for (int k = 0; k < main.Count; k += 15)
            {
                var s = main.Samples[k];
                double chance = random.NextDouble();
                int side = random.NextDouble() < 0.5 ? 0 : 1;
                float gap = bevel + 1.8f + (float)random.NextDouble() * 3f;
                float scale = 0.6f + (float)random.NextDouble() * 0.9f;
                int variant = random.Next(prefabs.Length);
                if (chance > context.Definition.RoadsideRockChance || s.IsBridge || context.NearJunction(s.Distance) || s.Distance < 40f)
                    continue;

                Vector3 position = s.Position + s.Right * (Sign(side) * (s.HalfWidth + gap));
                if (context.Terrain.Field.EdgeDistanceAt(position.x, position.z) < bevel + 1.5f || InCreek(context, position))
                    continue;
                PlaceRock(context, group, prefabs[variant], position, scale, random);
            }

            // Boulders scattered through the forest.
            for (int placed = 0, attempts = 0; placed < context.Definition.ForestBoulderCount && attempts < 2000; attempts++)
            {
                var position = new Vector3((float)random.NextDouble() * context.Size, 0f, (float)random.NextDouble() * context.Size);
                float scale = 1.6f + (float)random.NextDouble() * 1.6f;
                int variant = random.Next(prefabs.Length);
                if (context.Terrain.Field.EdgeDistanceAt(position.x, position.z) < bevel + 10f || InCreek(context, position)
                    || position.x < 20f || position.z < 20f || position.x > context.Size - 20f || position.z > context.Size - 20f)
                    continue;

                PlaceRock(context, group, prefabs[variant], position, scale, random);
                placed++;
            }
        }

        private static void PlaceRock(Context context, Transform group, GameObject prefab, Vector3 position, float scale, System.Random random)
        {
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            position.y = context.GroundHeight(position) - 0.25f * scale;
            var rotation = Quaternion.Euler((float)random.NextDouble() * 10f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 10f);
            rock.transform.SetPositionAndRotation(position, rotation);
            rock.transform.localScale = Vector3.one * scale;
            GameObjectUtility.SetStaticEditorFlags(rock, StaticEditorFlags.BatchingStatic);
        }

        private static bool InCreek(Context context, Vector3 position)
        {
            foreach (var creek in context.Terrain.Creeks)
            {
                if (creek.Contains(TrackLayout.Flat(position), 2f))
                    return true;
            }

            return false;
        }

        private static void BuildWater(Context context)
        {
            // Dry themes have no water: their creeks are washes.
            if (context.Assets.Water == null)
                return;

            var group = context.Group("Water");
            foreach (var creek in context.Terrain.Creeks)
            {
                var along = new Vector3(creek.Along.x, 0f, creek.Along.y);
                var position = new Vector3(creek.Center.x, creek.BedHeight + 0.8f, creek.Center.y);
                float width = (creek.HalfWidth + creek.BankWidth * 0.6f) * 2f;
                float length = (creek.Reach - 10f) * 2f;

                // The plane primitive is 10 x 10 units; water is visual only.
                var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
                water.name = "Creek";
                Object.DestroyImmediate(water.GetComponent<Collider>());
                water.transform.SetParent(group, false);
                water.transform.SetPositionAndRotation(position, Quaternion.LookRotation(along));
                water.transform.localScale = new Vector3(width / 10f, 1f, length / 10f);
                water.GetComponent<MeshRenderer>().sharedMaterial = context.Assets.Water;
            }
        }

        private static void BuildBoundaryWalls(Context context)
        {
            var group = context.Group("BoundaryWalls");
            float size = context.Size;
            const float thickness = 2f;
            const float height = 400f;
            var walls = new[]
            {
                (new Vector3(-thickness * 0.5f, 0f, size * 0.5f), new Vector3(thickness, height, size + thickness * 2f)),
                (new Vector3(size + thickness * 0.5f, 0f, size * 0.5f), new Vector3(thickness, height, size + thickness * 2f)),
                (new Vector3(size * 0.5f, 0f, -thickness * 0.5f), new Vector3(size + thickness * 2f, height, thickness)),
                (new Vector3(size * 0.5f, 0f, size + thickness * 0.5f), new Vector3(size + thickness * 2f, height, thickness)),
            };

            foreach (var (center, extents) in walls)
            {
                var wall = new GameObject("Wall");
                wall.transform.SetParent(group, false);
                wall.transform.position = center;
                wall.AddComponent<BoxCollider>().size = extents;
            }
        }

        // ---------------------------------------------------------------- helpers

        private static GameObject Rail(Transform group, string name, Vector3 from, Vector3 to, float height, float thickness, float sink, Material material)
        {
            Vector3 span = to - from;
            float length = span.magnitude + 0.1f;
            if (length < 0.2f)
                return null;

            Vector3 center = (from + to) * 0.5f + Vector3.up * (height * 0.5f - sink);
            return EditorPrimitives.Box(name, group, center, Quaternion.LookRotation(span), new Vector3(thickness, height, length), material, collider: true);
        }

        /// <summary>Contiguous runs of set flags on one side as (start, length); runs may wrap past the end.</summary>
        private static List<(int start, int length)> Runs(bool[,] flags, int side, int count)
        {
            var runs = new List<(int, int)>();
            int first = -1;
            for (int k = 0; k < count; k++)
            {
                if (!flags[side, k])
                {
                    first = k;
                    break;
                }
            }

            if (first < 0)
            {
                runs.Add((0, count));
                return runs;
            }

            for (int step = 1; step <= count; step++)
            {
                int k = Wrap(first + step, count);
                if (!flags[side, k])
                    continue;

                int length = 0;
                while (length < count && flags[side, Wrap(k + length, count)])
                    length++;
                runs.Add((k, length));
                step += length;
            }

            return runs;
        }

        private static float Sign(int side) => side == 0 ? -1f : 1f;

        private static int Wrap(int index, int count) => ((index % count) + count) % count;
    }
}
