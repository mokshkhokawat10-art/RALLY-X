using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static RallyX.EditorTools.ProceduralTextures;

namespace RallyX.EditorTools
{
    /// <summary>Placeholder environment art shared by generated tracks.</summary>
    public sealed class EnvironmentAssets
    {
        public Material RoadDirt;
        public Material RoadGravel;
        public Material RoadMud;
        public Material RoadAsphalt;
        public Material BridgeDeck;
        public Material Concrete;
        public Material Barrier;
        public Material ChevronLeft;
        public Material ChevronRight;
        public Material Checker;
        public Material Water;
        public Material PostWhite;
        public Material PostRed;
        public Material Banner;
        public Material BannerText;
        public Font BannerFont;
        public TerrainLayer[] TerrainLayers;
        public GameObject[] TreePrefabs;
        public GameObject[] RockPrefabs;

        /// <summary>Terrain layer indices, matching <see cref="TerrainLayers"/>.</summary>
        public const int GrassLayer = 0;
        public const int ForestLayer = 1;
        public const int DirtLayer = 2;
        public const int RockLayer = 3;
    }

    /// <summary>Creates any missing placeholder environment assets. Existing assets are reused untouched.</summary>
    public static class EnvironmentAssetBuilder
    {
        private const string TextureFolder = "Assets/Textures/Generated";
        private const string MaterialFolder = "Assets/Materials/Environment";
        private const string MeshFolder = "Assets/Models/Generated";
        private const string PrefabFolder = "Assets/Prefabs/Environment";
        private const string WorldTextShaderPath = "Assets/Materials/Shaders/WorldText.shader";
        private const int TextureSize = 256;

        // Fraction of the screen height below which a tree is not drawn (about 700 m away for a pine).
        private const float TreeCullScreenHeight = 0.01f;

        public static EnvironmentAssets LoadOrCreate()
        {
            var bark = Lit("Bark", PaintedTexture("Tree_Bark", PaintBark), 0.1f, instanced: true);
            var needles = Lit("Needles", PaintedTexture("Tree_Needles", PaintNeedles), 0.05f, instanced: true);
            var rock = Lit("Rock", PaintedTexture("Rock", (u, v) => PaintRock(u, v, 11f)), 0.15f, instanced: true);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return new EnvironmentAssets
            {
                RoadDirt = Lit("Road_Dirt", PaintedTexture("Road_Dirt", PaintRoadDirt), 0.1f),
                RoadGravel = Lit("Road_Gravel", PaintedTexture("Road_Gravel", PaintRoadGravel), 0.1f),
                RoadMud = Lit("Road_Mud", PaintedTexture("Road_Mud", PaintRoadMud), 0.45f),
                RoadAsphalt = Lit("Road_Asphalt", PaintedTexture("Road_Asphalt", PaintRoadAsphalt), 0.25f),
                BridgeDeck = Lit("Bridge_Planks", PaintedTexture("Bridge_Planks", PaintPlanks), 0.15f),
                Concrete = Lit("Concrete", PaintedTexture("Concrete", (u, v) => Shade(PaintRock(u, v, 23f), 1.35f)), 0.1f, instanced: true),
                Barrier = Lit("Barrier", PaintedTexture("Barrier_Stripes", PaintStripes), 0.35f, instanced: true),
                ChevronLeft = Lit("Chevron_Left", PaintedTexture("Sign_ChevronLeft", (u, v) => PaintChevron(1f - u, v), repeat: false), 0.3f),
                ChevronRight = Lit("Chevron_Right", PaintedTexture("Sign_ChevronRight", PaintChevron, repeat: false), 0.3f),
                Checker = Lit("Checker", PaintedTexture("Line_Checker", PaintChecker), 0.2f),
                Water = Lit("Water", PaintedTexture("Water", PaintWater), 0.92f),
                PostWhite = Lit("Post_White", null, 0.3f, new Color(0.9f, 0.9f, 0.88f), instanced: true),
                PostRed = Lit("Post_Red", null, 0.5f, new Color(0.85f, 0.08f, 0.05f), instanced: true),
                Banner = Lit("Banner", null, 0.3f, new Color(0.06f, 0.09f, 0.2f)),
                BannerText = TextMaterial("Banner_Text", font),
                BannerFont = font,
                TerrainLayers = new[]
                {
                    Layer("Grass", PaintedTexture("Ground_Grass", PaintGrass), 6f),
                    Layer("ForestFloor", PaintedTexture("Ground_ForestFloor", PaintForestFloor), 8f),
                    Layer("Dirt", PaintedTexture("Ground_Dirt", PaintDirt), 5f),
                    Layer("Rock", PaintedTexture("Ground_Rock", (u, v) => PaintRock(u, v, 5f)), 10f),
                },
                TreePrefabs = new[]
                {
                    TreePrefab("PineTall", bark, needles, new[] { new Cone(1.8f, 5.4f, 1.9f), new Cone(3.5f, 6.9f, 1.5f), new Cone(5.1f, 8.4f, 1.05f) }),
                    TreePrefab("PineBushy", bark, needles, new[] { new Cone(1.2f, 4.2f, 2.3f), new Cone(2.6f, 5.4f, 1.9f), new Cone(3.9f, 6.5f, 1.4f), new Cone(5.1f, 7.3f, 0.9f) }),
                },
                RockPrefabs = new[] { RockPrefab("Rock_A", rock, 1), RockPrefab("Rock_B", rock, 2), RockPrefab("Rock_C", rock, 3) },
            };
        }

        // ---------------------------------------------------------------- textures

        private static Texture2D PaintedTexture(string name, Painter paint, bool repeat = true) =>
            ProceduralTextures.LoadOrCreate($"{TextureFolder}/{name}.png", TextureSize, paint, repeat);

        private static Color PaintGrass(float u, float v)
        {
            Color color = Color.Lerp(new Color(0.16f, 0.27f, 0.09f), new Color(0.3f, 0.42f, 0.17f), TileableNoise(u, v, 6, 4, 1f));
            float dry = TileableNoise(u, v, 48, 2, 2f);
            return dry > 0.6f ? Color.Lerp(color, new Color(0.45f, 0.44f, 0.22f), (dry - 0.6f) * 2f) : color;
        }

        private static Color PaintForestFloor(float u, float v)
        {
            Color color = Color.Lerp(new Color(0.13f, 0.1f, 0.06f), new Color(0.26f, 0.2f, 0.12f), TileableNoise(u, v, 6, 4, 3f));
            if (TileableNoise(u, v, 3, 2, 4f) > 0.58f)
                color = Color.Lerp(color, new Color(0.17f, 0.25f, 0.09f), 0.6f);
            return TileableNoise(u, v, 64, 2, 5f) > 0.6f ? new Color(0.36f, 0.25f, 0.14f) : color;
        }

        private static Color PaintDirt(float u, float v)
        {
            Color color = Color.Lerp(new Color(0.33f, 0.24f, 0.16f), new Color(0.5f, 0.39f, 0.27f), TileableNoise(u, v, 8, 4, 6f));
            return TileableNoise(u, v, 48, 2, 7f) > 0.64f ? new Color(0.58f, 0.52f, 0.45f) : color;
        }

        private static Color PaintRock(float u, float v, float seed)
        {
            Color color = Color.Lerp(new Color(0.3f, 0.3f, 0.3f), new Color(0.52f, 0.51f, 0.49f), TileableNoise(u, v, 5, 5, seed));
            return Mathf.Abs(TileableNoise(u, v, 12, 3, seed + 1f) - 0.5f) < 0.015f ? Shade(color, 0.6f) : color;
        }

        private static Color PaintRoadDirt(float u, float v)
        {
            Color color = Color.Lerp(new Color(0.42f, 0.32f, 0.22f), new Color(0.56f, 0.44f, 0.31f), TileableNoise(u, v, 6, 4, 8f));
            return Shade(color, 1f - 0.18f * WheelTracks(u) * (0.6f + 0.4f * TileableNoise(u, v, 16, 2, 9f)));
        }

        private static Color PaintRoadGravel(float u, float v)
        {
            Color color = Color.Lerp(new Color(0.46f, 0.44f, 0.4f), new Color(0.62f, 0.59f, 0.54f), TileableNoise(u, v, 10, 3, 10f));
            float stones = TileableNoise(u, v, 64, 1, 11f);
            if (stones > 0.6f)
                color = new Color(0.72f, 0.7f, 0.66f);
            else if (stones < 0.4f)
                color = new Color(0.36f, 0.34f, 0.31f);
            return Shade(color, 1f - 0.1f * WheelTracks(u));
        }

        private static Color PaintRoadMud(float u, float v)
        {
            Color color = Color.Lerp(new Color(0.18f, 0.12f, 0.08f), new Color(0.3f, 0.21f, 0.13f), TileableNoise(u, v, 5, 4, 12f));
            if (TileableNoise(u, v, 3, 3, 13f) > 0.6f)
                color = new Color(0.12f, 0.09f, 0.06f);
            return Shade(color, 1f - 0.25f * WheelTracks(u));
        }

        private static Color PaintRoadAsphalt(float u, float v)
        {
            Color color = Color.Lerp(new Color(0.13f, 0.13f, 0.14f), new Color(0.2f, 0.2f, 0.21f), TileableNoise(u, v, 32, 3, 14f));
            bool edgeLine = (u > 0.035f && u < 0.065f) || (u > 0.935f && u < 0.965f);
            bool centreDash = Mathf.Abs(u - 0.5f) < 0.012f && Mathf.Repeat(v * 2f, 1f) < 0.5f;
            if (edgeLine)
                return new Color(0.85f, 0.85f, 0.82f);
            return centreDash ? new Color(0.9f, 0.74f, 0.18f) : color;
        }

        private static Color PaintPlanks(float u, float v)
        {
            const int planks = 8;
            float plank = Mathf.Floor(v * planks);
            if (Mathf.Repeat(v * planks, 1f) < 0.07f)
                return new Color(0.12f, 0.08f, 0.05f);

            float tint = Mathf.PerlinNoise(plank * 3.1f, 0.5f);
            Color wood = Color.Lerp(new Color(0.34f, 0.23f, 0.14f), new Color(0.52f, 0.37f, 0.22f), tint);
            return Shade(wood, 0.85f + 0.3f * TileableNoise(u, v, 24, 2, 15f + plank));
        }

        private static Color PaintBark(float u, float v)
        {
            float streaks = Mathf.Sin((u * 24f + TileableNoise(u, v, 4, 2, 16f) * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            return Color.Lerp(new Color(0.22f, 0.15f, 0.1f), new Color(0.4f, 0.28f, 0.18f), streaks * 0.6f + TileableNoise(u, v, 8, 3, 17f) * 0.4f);
        }

        private static Color PaintNeedles(float u, float v)
        {
            Color color = Color.Lerp(new Color(0.06f, 0.16f, 0.07f), new Color(0.15f, 0.3f, 0.12f), TileableNoise(u, v, 12, 4, 18f));
            return TileableNoise(u, v, 64, 1, 19f) > 0.65f ? new Color(0.24f, 0.4f, 0.18f) : color;
        }

        private static Color PaintStripes(float u, float v) =>
            Mathf.Repeat((u + v) * 4f, 1f) < 0.5f ? new Color(0.78f, 0.08f, 0.06f) : new Color(0.93f, 0.93f, 0.9f);

        private static Color PaintChecker(float u, float v) =>
            ((int)(u * 8f) + (int)(v * 2f)) % 2 == 0 ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.95f, 0.95f, 0.95f);

        private static Color PaintWater(float u, float v) =>
            Color.Lerp(new Color(0.06f, 0.14f, 0.16f), new Color(0.12f, 0.24f, 0.26f), TileableNoise(u, v, 6, 3, 20f));

        /// <summary>Black ">" chevron on a yellow board with a black border, pointing right.</summary>
        private static Color PaintChevron(float u, float v)
        {
            var black = new Color(0.03f, 0.03f, 0.03f);
            if (u < 0.05f || u > 0.95f || v < 0.05f || v > 0.95f)
                return black;

            float distance = Mathf.Min(DistanceToSegment(new Vector2(u, v), new Vector2(0.3f, 0.18f), new Vector2(0.66f, 0.5f)),
                DistanceToSegment(new Vector2(u, v), new Vector2(0.66f, 0.5f), new Vector2(0.3f, 0.82f)));
            return distance < 0.09f ? black : new Color(1f, 0.78f, 0.05f);
        }

        private static float WheelTracks(float u) =>
            Mathf.Clamp01(Mathf.Max(1f - Mathf.Abs(u - 0.28f) / 0.07f, 1f - Mathf.Abs(u - 0.72f) / 0.07f));

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(point, a + ab * t);
        }

        // ---------------------------------------------------------------- materials and layers

        private static Material Lit(string name, Texture2D texture, float smoothness, Color? tint = null, bool instanced = false)
        {
            var material = EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialFolder}/{name}.mat", tint ?? Color.white, smoothness);
            if (material.GetTexture("_BaseMap") != texture || material.enableInstancing != instanced)
            {
                material.SetTexture("_BaseMap", texture);
                material.enableInstancing = instanced;
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>
        /// Material for 3D text. The font's own material skips the depth test, so text would show through anything
        /// in front of it, including the back of the sign it is on.
        /// </summary>
        private static Material TextMaterial(string name, Font font)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(WorldTextShaderPath);
            var material = EditorAssetUtility.LoadOrCreate($"{MaterialFolder}/{name}.mat", () => new Material(shader));
            if (material.shader != shader || material.mainTexture != font.material.mainTexture)
            {
                material.shader = shader;
                material.mainTexture = font.material.mainTexture;
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        private static TerrainLayer Layer(string name, Texture2D texture, float tileSize) =>
            EditorAssetUtility.LoadOrCreate($"{MaterialFolder}/TerrainLayers/{name}.terrainlayer", () => new TerrainLayer
            {
                diffuseTexture = texture,
                tileSize = new Vector2(tileSize, tileSize),
            });

        // ---------------------------------------------------------------- meshes and prefabs

        private readonly struct Cone
        {
            public readonly float BaseY;
            public readonly float TopY;
            public readonly float Radius;

            public Cone(float baseY, float topY, float radius)
            {
                BaseY = baseY;
                TopY = topY;
                Radius = radius;
            }
        }

        private static GameObject TreePrefab(string name, Material bark, Material needles, Cone[] cones)
        {
            string path = $"{PrefabFolder}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var mesh = EditorAssetUtility.LoadOrCreate($"{MeshFolder}/{name}.asset", () => BuildPineMesh(name, cones));
            var root = new GameObject(name);
            try
            {
                // Terrain trees must be a single renderer, so trunk and foliage are submeshes of one mesh.
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { bark, needles };

                // The terrain only billboards trees that use its Nature shaders and warns about any others,
                // unless the tree has a LOD group. This one just culls trees too small to see.
                var lodGroup = root.AddComponent<LODGroup>();
                lodGroup.SetLODs(new[] { new LOD(TreeCullScreenHeight, new Renderer[] { renderer }) });
                lodGroup.RecalculateBounds();

                var trunk = root.AddComponent<CapsuleCollider>();
                trunk.center = new Vector3(0f, 1.6f, 0f);
                trunk.radius = 0.3f;
                trunk.height = 3.2f;
                EditorAssetUtility.EnsureFolder(PrefabFolder);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject RockPrefab(string name, Material material, int seed)
        {
            string path = $"{PrefabFolder}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var mesh = EditorAssetUtility.LoadOrCreate($"{MeshFolder}/{name}.asset", () => BuildRockMesh(name, seed));
            var root = new GameObject(name);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterial = material;
                var collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = true;
                EditorAssetUtility.EnsureFolder(PrefabFolder);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Mesh BuildPineMesh(string name, Cone[] cones)
        {
            const int trunkSides = 8;
            const int coneSides = 10;
            const float trunkRadius = 0.22f;
            const float trunkBottom = -0.4f;
            float trunkTop = cones[cones.Length - 1].BaseY + 0.5f;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var trunk = new List<int>();
            var foliage = new List<int>();

            for (int i = 0; i <= trunkSides; i++)
            {
                Vector3 direction = RingDirection(i, trunkSides);
                vertices.Add(direction * trunkRadius + Vector3.up * trunkBottom);
                vertices.Add(direction * trunkRadius * 0.7f + Vector3.up * trunkTop);
                uvs.Add(new Vector2(i / (float)trunkSides, 0f));
                uvs.Add(new Vector2(i / (float)trunkSides, trunkTop - trunkBottom));
            }

            for (int i = 0; i < trunkSides; i++)
            {
                int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                trunk.AddRange(new[] { b0, b1, t1, b0, t1, t0 });
            }

            // Faceted cones: each face gets its own vertices for a clean low-poly look.
            foreach (var cone in cones)
            {
                var apex = Vector3.up * cone.TopY;
                var center = Vector3.up * cone.BaseY;
                for (int i = 0; i < coneSides; i++)
                {
                    Vector3 b0 = RingDirection(i, coneSides) * cone.Radius + center;
                    Vector3 b1 = RingDirection(i + 1, coneSides) * cone.Radius + center;
                    AddTriangle(vertices, uvs, foliage, b0, b1, apex, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f));
                    AddTriangle(vertices, uvs, foliage, center, b1, b0, new Vector2(0.5f, 0.5f), new Vector2(1f, 0f), new Vector2(0f, 0f));
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(trunk, 0);
            mesh.SetTriangles(foliage, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildRockMesh(string name, int seed)
        {
            // Subdivided icosahedron, displaced with noise and flattened, with flat-shaded faces.
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var corners = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int f = 0; f < faces.Length; f += 3)
            {
                Vector3 a = corners[faces[f]].normalized;
                Vector3 b = corners[faces[f + 1]].normalized;
                Vector3 c = corners[faces[f + 2]].normalized;
                Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
                foreach (var (p, q, r) in new[] { (a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca) })
                {
                    Vector3 p1 = Displace(p, seed), q1 = Displace(q, seed), r1 = Displace(r, seed);

                    // Keep every face pointing outwards regardless of the source winding.
                    Vector3 normal = Vector3.Cross(q1 - p1, r1 - p1);
                    if (Vector3.Dot(normal, p1 + q1 + r1) < 0f)
                        (q1, r1) = (r1, q1);
                    AddTriangle(vertices, uvs, triangles, p1, q1, r1, new Vector2(p1.x, p1.z), new Vector2(q1.x, q1.z), new Vector2(r1.x, r1.z));
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Displace(Vector3 unit, int seed)
        {
            float noise = Mathf.PerlinNoise(unit.x * 1.7f + seed * 13.1f + 50f, unit.z * 1.7f + unit.y * 0.9f + 50f);
            Vector3 point = unit * (0.75f + 0.5f * noise);
            point.y *= 0.65f;
            return point;
        }

        private static void AddTriangle(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, Vector3 a, Vector3 b, Vector3 c,
            Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            int index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            uvs.Add(uvA);
            uvs.Add(uvB);
            uvs.Add(uvC);
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
        }

        /// <summary>Unit direction around the Y axis; indices increase clockwise seen from above.</summary>
        private static Vector3 RingDirection(int index, int sides)
        {
            float angle = index * Mathf.PI * 2f / sides;
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        }
    }
}
