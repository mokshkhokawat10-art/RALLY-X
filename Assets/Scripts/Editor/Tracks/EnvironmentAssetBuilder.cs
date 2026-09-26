using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static RallyX.EditorTools.ProceduralTextures;

namespace RallyX.EditorTools
{
    /// <summary>Placeholder environment art for a generated track: shared props plus its theme's ground, roads, trees and rocks.</summary>
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

        /// <summary>Creek water, or null when the theme's creeks are dry.</summary>
        public Material Water;

        public Material PostWhite;
        public Material PostRed;
        public Material Banner;
        public Material BannerText;

        /// <summary>The theme's sky, or null for Unity's default sky.</summary>
        public Material Sky;

        public Font BannerFont;
        public TerrainLayer[] TerrainLayers;
        public GameObject[] TreePrefabs;

        /// <summary>Share of the trees planted with each of <see cref="TreePrefabs"/>, adding up to 1.</summary>
        public float[] TreeShares;

        public GameObject[] RockPrefabs;

        /// <summary>Terrain layer indices, matching <see cref="TerrainLayers"/>.</summary>
        public const int GrassLayer = 0;
        public const int ForestLayer = 1;
        public const int DirtLayer = 2;
        public const int RockLayer = 3;
    }

    /// <summary>
    /// Creates any missing placeholder environment art. Props shared by every track live in the shared folders; a
    /// theme's ground, roads, trees and rocks live in its own subfolder (the forest theme's in the shared folders).
    /// Existing assets are reused untouched, so delete a theme's folders to regenerate its art.
    /// </summary>
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

        public static EnvironmentAssets LoadOrCreate(TrackTheme theme)
        {
            var art = new ThemeFolders(theme.ArtFolder);
            var bark = Lit(art.Material("Bark"), Painted(art.Texture("Tree_Bark"), (u, v) => PaintBark(u, v, theme.Bark)), 0.1f, instanced: true);
            var needles = Lit(art.Material("Needles"), Painted(art.Texture("Tree_Needles"), (u, v) => PaintNeedles(u, v, theme.Foliage)), 0.05f,
                instanced: true);
            var rock = Lit(art.Material("Rock"),
                Painted(art.Texture("Rock"), (u, v) => PaintBandedRock(u, v, 11f, theme.Rock, theme.RockBanding)), 0.15f, instanced: true);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var shared = new ThemeFolders(string.Empty);

            var treeShapes = theme.Trees;
            var treePrefabs = new GameObject[treeShapes.Length];
            var treeShares = new float[treeShapes.Length];
            float totalShare = 0f;
            foreach (var shape in treeShapes)
                totalShare += shape.share;
            for (int i = 0; i < treeShapes.Length; i++)
            {
                treePrefabs[i] = TreePrefab(art, treeShapes[i], bark, needles);
                treeShares[i] = totalShare > 0f ? treeShapes[i].share / totalShare : 1f / treeShapes.Length;
            }

            return new EnvironmentAssets
            {
                RoadDirt = Lit(art.Material("Road_Dirt"), Painted(art.Texture("Road_Dirt"), (u, v) => PaintRoadDirt(u, v, theme.RoadDirt)), 0.1f),
                RoadGravel = Lit(art.Material("Road_Gravel"),
                    Painted(art.Texture("Road_Gravel"), (u, v) => PaintRoadGravel(u, v, theme.RoadGravel, theme.RoadGravelDarkStones)), 0.1f),
                RoadMud = Lit(shared.Material("Road_Mud"), Painted(shared.Texture("Road_Mud"), PaintRoadMud), 0.45f),
                RoadAsphalt = Lit(shared.Material("Road_Asphalt"), Painted(shared.Texture("Road_Asphalt"), PaintRoadAsphalt), 0.25f),
                BridgeDeck = Lit(shared.Material("Bridge_Planks"), Painted(shared.Texture("Bridge_Planks"), PaintPlanks), 0.15f),
                Concrete = Lit(shared.Material("Concrete"), Painted(shared.Texture("Concrete"), (u, v) => Shade(PaintRock(u, v, 23f, GreyRock), 1.35f)),
                    0.1f, instanced: true),
                Barrier = Lit(shared.Material("Barrier"), Painted(shared.Texture("Barrier_Stripes"), PaintStripes), 0.35f, instanced: true),
                ChevronLeft = Lit(shared.Material("Chevron_Left"), Painted(shared.Texture("Sign_ChevronLeft"), (u, v) => PaintChevron(1f - u, v), repeat: false),
                    0.3f),
                ChevronRight = Lit(shared.Material("Chevron_Right"), Painted(shared.Texture("Sign_ChevronRight"), PaintChevron, repeat: false), 0.3f),
                Checker = Lit(shared.Material("Checker"), Painted(shared.Texture("Line_Checker"), PaintChecker), 0.2f),
                Water = theme.CreekWater
                    ? Lit(art.Material("Water"), Painted(art.Texture("Water"), (u, v) => PaintWater(u, v, theme.Water)), 0.92f)
                    : null,
                PostWhite = Lit(shared.Material("Post_White"), null, 0.3f, new Color(0.9f, 0.9f, 0.88f), instanced: true),
                PostRed = Lit(shared.Material("Post_Red"), null, 0.5f, new Color(0.85f, 0.08f, 0.05f), instanced: true),
                Banner = Lit(shared.Material("Banner"), null, 0.3f, new Color(0.06f, 0.09f, 0.2f)),
                BannerText = TextMaterial(shared.Material("Banner_Text"), font),
                Sky = theme.CustomSky ? SkyMaterial(art.Material("Sky"), theme) : null,
                BannerFont = font,
                TerrainLayers = new[]
                {
                    Layer(art.TerrainLayer("Grass"), Painted(art.Texture("Ground_Grass"), (u, v) => PaintGrass(u, v, theme.Grass)), 6f),
                    Layer(art.TerrainLayer("ForestFloor"),
                        Painted(art.Texture("Ground_ForestFloor"), (u, v) => PaintForestFloor(u, v, theme.ForestFloor, theme.ForestFloorPatches)), 8f),
                    Layer(art.TerrainLayer("Dirt"), Painted(art.Texture("Ground_Dirt"), (u, v) => PaintDirt(u, v, theme.Dirt)), 5f),
                    Layer(art.TerrainLayer("Rock"), Painted(art.Texture("Ground_Rock"), (u, v) => PaintRock(u, v, 5f, theme.Rock)), 10f),
                },
                TreePrefabs = treePrefabs,
                TreeShares = treeShares,
                RockPrefabs = new[]
                {
                    RockPrefab(art, "Rock_A", rock, 1, theme.RockBanding > 0f),
                    RockPrefab(art, "Rock_B", rock, 2, theme.RockBanding > 0f),
                    RockPrefab(art, "Rock_C", rock, 3, theme.RockBanding > 0f),
                },
            };
        }

        /// <summary>Where a theme's generated art goes: its subfolder of each art folder, or the folders themselves.</summary>
        private readonly struct ThemeFolders
        {
            private readonly string subfolder;

            public ThemeFolders(string artFolder) => subfolder = string.IsNullOrEmpty(artFolder) ? string.Empty : "/" + artFolder.Trim('/');

            public string Texture(string name) => $"{TextureFolder}{subfolder}/{name}.png";
            public string Material(string name) => $"{MaterialFolder}{subfolder}/{name}.mat";
            public string TerrainLayer(string name) => $"{MaterialFolder}{subfolder}/TerrainLayers/{name}.terrainlayer";
            public string Mesh(string name) => $"{MeshFolder}{subfolder}/{name}.asset";
            public string Prefab(string name) => $"{PrefabFolder}{subfolder}/{name}.prefab";
            public string PrefabFolderPath => $"{PrefabFolder}{subfolder}";
        }

        // ---------------------------------------------------------------- textures

        private static readonly Palette GreyRock = new Palette(new Color(0.3f, 0.3f, 0.3f), new Color(0.52f, 0.51f, 0.49f), new Color(0.3f, 0.3f, 0.3f));

        private static Texture2D Painted(string path, Painter paint, bool repeat = true) =>
            ProceduralTextures.LoadOrCreate(path, TextureSize, paint, repeat);

        private static Color PaintGrass(float u, float v, Palette colors)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 6, 4, 1f));
            float dry = TileableNoise(u, v, 48, 2, 2f);
            return dry > 0.6f ? Color.Lerp(color, colors.detail, (dry - 0.6f) * 2f) : color;
        }

        private static Color PaintForestFloor(float u, float v, Palette colors, Color patches)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 6, 4, 3f));
            if (TileableNoise(u, v, 3, 2, 4f) > 0.58f)
                color = Color.Lerp(color, patches, 0.6f);
            return TileableNoise(u, v, 64, 2, 5f) > 0.6f ? colors.detail : color;
        }

        private static Color PaintDirt(float u, float v, Palette colors)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 8, 4, 6f));
            return TileableNoise(u, v, 48, 2, 7f) > 0.64f ? colors.detail : color;
        }

        private static Color PaintRock(float u, float v, float seed, Palette colors)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 5, 5, seed));
            return Mathf.Abs(TileableNoise(u, v, 12, 3, seed + 1f) - 0.5f) < 0.015f ? Shade(color, 0.6f) : color;
        }

        /// <summary>Rock with horizontal bands of lighter and darker stone along v (the height on banded boulders).</summary>
        private static Color PaintBandedRock(float u, float v, float seed, Palette colors, float banding)
        {
            Color color = PaintRock(u, v, seed, colors);
            if (banding <= 0f)
                return color;

            float band = Mathf.Sin((v * 7f + TileableNoise(u, v, 3, 2, seed + 2f) * 0.6f) * Mathf.PI * 2f);
            return Shade(color, 1f + 0.22f * banding * band);
        }

        private static Color PaintRoadDirt(float u, float v, Palette colors)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 6, 4, 8f));
            return Shade(color, 1f - 0.18f * WheelTracks(u) * (0.6f + 0.4f * TileableNoise(u, v, 16, 2, 9f)));
        }

        private static Color PaintRoadGravel(float u, float v, Palette colors, Color darkStones)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 10, 3, 10f));
            float stones = TileableNoise(u, v, 64, 1, 11f);
            if (stones > 0.6f)
                color = colors.detail;
            else if (stones < 0.4f)
                color = darkStones;
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

        private static Color PaintBark(float u, float v, Palette colors)
        {
            float streaks = Mathf.Sin((u * 24f + TileableNoise(u, v, 4, 2, 16f) * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            return Color.Lerp(colors.dark, colors.light, streaks * 0.6f + TileableNoise(u, v, 8, 3, 17f) * 0.4f);
        }

        private static Color PaintNeedles(float u, float v, Palette colors)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 12, 4, 18f));
            return TileableNoise(u, v, 64, 1, 19f) > 0.65f ? colors.detail : color;
        }

        private static Color PaintStripes(float u, float v) =>
            Mathf.Repeat((u + v) * 4f, 1f) < 0.5f ? new Color(0.78f, 0.08f, 0.06f) : new Color(0.93f, 0.93f, 0.9f);

        private static Color PaintChecker(float u, float v) =>
            ((int)(u * 8f) + (int)(v * 2f)) % 2 == 0 ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.95f, 0.95f, 0.95f);

        private static Color PaintWater(float u, float v, Palette colors) =>
            Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 6, 3, 20f));

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

        private static Material Lit(string path, Texture2D texture, float smoothness, Color? tint = null, bool instanced = false)
        {
            var material = EditorAssetUtility.LoadOrCreateLitMaterial(path, tint ?? Color.white, smoothness);
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
        private static Material TextMaterial(string path, Font font)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(WorldTextShaderPath);
            var material = EditorAssetUtility.LoadOrCreate(path, () => new Material(shader));
            if (material.shader != shader || material.mainTexture != font.material.mainTexture)
            {
                material.shader = shader;
                material.mainTexture = font.material.mainTexture;
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>A procedural sky in the theme's colours. Kept in step with the theme on every build.</summary>
        private static Material SkyMaterial(string path, TrackTheme theme)
        {
            var material = EditorAssetUtility.LoadOrCreate(path, () => new Material(Shader.Find("Skybox/Procedural")));
            material.SetColor("_SkyTint", theme.SkyTint);
            material.SetColor("_GroundColor", theme.SkyGround);
            material.SetFloat("_AtmosphereThickness", theme.AtmosphereThickness);
            material.SetFloat("_Exposure", theme.SkyExposure);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static TerrainLayer Layer(string path, Texture2D texture, float tileSize)
        {
            return EditorAssetUtility.LoadOrCreate(path, () => new TerrainLayer
            {
                diffuseTexture = texture,
                tileSize = new Vector2(tileSize, tileSize),
            });
        }

        // ---------------------------------------------------------------- meshes and prefabs

        private static GameObject TreePrefab(ThemeFolders art, TreeShape shape, Material bark, Material needles)
        {
            string path = art.Prefab(shape.name);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var mesh = EditorAssetUtility.LoadOrCreate(art.Mesh(shape.name), () => BuildTreeMesh(shape.name, shape.cones));
            var root = new GameObject(shape.name);
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

                // The trunk collider stops at the lowest foliage, so low bushes don't grow tall invisible posts.
                float trunkHeight = Mathf.Clamp(shape.cones.Length > 0 ? shape.cones[0].tipHeight : 3.2f, 1f, 3.2f);
                var trunk = root.AddComponent<CapsuleCollider>();
                trunk.center = new Vector3(0f, trunkHeight * 0.5f, 0f);
                trunk.radius = 0.3f;
                trunk.height = trunkHeight;
                EditorAssetUtility.EnsureFolder(art.PrefabFolderPath);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject RockPrefab(ThemeFolders art, string name, Material material, int seed, bool banded)
        {
            string path = art.Prefab(name);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var mesh = EditorAssetUtility.LoadOrCreate(art.Mesh(name), () => BuildRockMesh(name, seed, banded));
            var root = new GameObject(name);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterial = material;
                var collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = true;
                EditorAssetUtility.EnsureFolder(art.PrefabFolderPath);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Mesh BuildTreeMesh(string name, TreeCone[] cones)
        {
            const int trunkSides = 8;
            const int coneSides = 10;
            const float trunkRadius = 0.22f;
            const float trunkBottom = -0.4f;
            float trunkTop = cones[cones.Length - 1].baseHeight + 0.5f;

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
                var apex = Vector3.up * cone.tipHeight;
                var center = Vector3.up * cone.baseHeight;
                for (int i = 0; i < coneSides; i++)
                {
                    Vector3 b0 = RingDirection(i, coneSides) * cone.radius + center;
                    Vector3 b1 = RingDirection(i + 1, coneSides) * cone.radius + center;
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

        /// <summary>
        /// Subdivided icosahedron, displaced with noise and flattened, with flat-shaded faces. Banded rocks map the
        /// texture's v to height, so the texture's bands run round the rock like layers of sandstone.
        /// </summary>
        private static Mesh BuildRockMesh(string name, int seed, bool banded)
        {
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
                    AddTriangle(vertices, uvs, triangles, p1, q1, r1, RockUv(p1, banded), RockUv(q1, banded), RockUv(r1, banded));
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

        private static Vector2 RockUv(Vector3 point, bool banded) => banded ? new Vector2(point.x + point.z, point.y * 0.5f) : new Vector2(point.x, point.z);

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
