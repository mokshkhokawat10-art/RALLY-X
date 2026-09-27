using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
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

        /// <summary>The sloped shoulders either side of every road: worn ground blending into the terrain's verge.</summary>
        public Material Verge;

        public Material Concrete;
        public Material Barrier;
        public Material Galvanised;
        public Material ChevronLeft;
        public Material ChevronRight;

        /// <summary>A yellow board warning of a crest jump.</summary>
        public Material JumpSign;
        public Material Checker;

        /// <summary>Creek water, or null when the theme's creeks are dry.</summary>
        public Material Water;

        public Material PostWhite;
        public Material PostRed;
        public Material Banner;
        public Material BannerText;

        /// <summary>The theme's sky, or null for Unity's default sky.</summary>
        public Material Sky;

        /// <summary>The terrain's material: URP Terrain Lit with normal maps and height-based blending between the ground layers.</summary>
        public Material TerrainMaterial;

        public Font BannerFont;
        public TerrainLayer[] TerrainLayers;
        public GameObject[] TreePrefabs;

        /// <summary>Share of the trees planted with each of <see cref="TreePrefabs"/>, adding up to 1.</summary>
        public float[] TreeShares;

        public GameObject[] RockPrefabs;

        /// <summary>Terrain detail meshes: grass tufts (<see cref="GrassDetail"/>) and bushes (<see cref="BushDetail"/>).</summary>
        public GameObject[] DetailPrefabs;

        /// <summary>Terrain layer indices, matching <see cref="TerrainLayers"/>.</summary>
        public const int GrassLayer = 0;
        public const int ForestLayer = 1;
        public const int DirtLayer = 2;
        public const int RockLayer = 3;

        /// <summary>Detail prototype indices, matching <see cref="DetailPrefabs"/>.</summary>
        public const int GrassDetail = 0;
        public const int BushDetail = 1;

        /// <summary>Metres of road shoulder covered by one tile of the verge texture (the road meshes map the shoulders in metres / 8).</summary>
        public const float VergeTile = 5f;
    }

    /// <summary>
    /// Creates any missing placeholder environment art, and repaints art made by an older revision of the painters in
    /// place (same assets, same GUIDs). Props shared by every track live in the shared folders; a theme's ground, roads,
    /// trees and rocks live in its own subfolder (the forest theme's in the shared folders). Materials are brought up to
    /// date on every build.
    /// </summary>
    public static class EnvironmentAssetBuilder
    {
        private const string TextureFolder = "Assets/Textures/Generated";
        private const string MaterialFolder = "Assets/Materials/Environment";
        private const string MeshFolder = "Assets/Models/Generated";
        private const string PrefabFolder = "Assets/Prefabs/Environment";
        private const string WorldTextShaderPath = "Assets/Materials/Shaders/WorldText.shader";

        // Bump when a painter changes, to repaint the generated textures in place.
        private const string ArtRevision = "env-4";

        private const int GroundTextureSize = 512;
        private const int PropTextureSize = 256;

        // Fraction of the screen height below which a tree is not drawn (about 700 m away for a pine).
        private const float TreeCullScreenHeight = 0.01f;

        // Road textures span the road's width once and 8 m along it (RoadMeshBuilder).
        private const float RoadEdgeWear = 0.065f;

        public static EnvironmentAssets LoadOrCreate(TrackTheme theme)
        {
            var art = new ThemeFolders(theme.ArtFolder);
            var shared = new ThemeFolders(string.Empty);
            var bark = Lit(art.Material("Bark"), Painted(art.Texture("Tree_Bark"), (u, v) => PaintBark(u, v, theme.Bark), PropTextureSize), 0.1f,
                instanced: true, normal: Normal(art.Texture("Tree_Bark_Normal"), HeightBark, 2.5f, PropTextureSize));
            var needles = Lit(art.Material("Needles"), Painted(art.Texture("Tree_Needles"), (u, v) => PaintNeedles(u, v, theme.Foliage), PropTextureSize),
                0.08f, instanced: true, normal: Normal(art.Texture("Tree_Needles_Normal"), HeightNeedles, 2f, PropTextureSize));
            var rock = Lit(art.Material("Rock"),
                Painted(art.Texture("Rock"), (u, v) => PaintBandedRock(u, v, 11f, theme.Rock, theme.RockBanding), PropTextureSize), 0.18f,
                instanced: true, normal: Normal(art.Texture("Rock_Normal"), (u, v) => HeightRock(u, v, 11f), 4f, PropTextureSize));
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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

            Color vergeColor = theme.Dirt.dark;
            var roadHeightDirt = (HeightPainter)HeightRoadDirt;
            var layers = new[]
            {
                Layer(art.TerrainLayer("Grass"), Painted(art.Texture("Ground_Grass"), (u, v) => PaintGrass(u, v, theme.Grass)),
                    Normal(art.Texture("Ground_Grass_Normal"), HeightGrass, 2f), Mask(art.Texture("Ground_Grass_Mask"), HeightGrass, 0.12f), 6f),
                Layer(art.TerrainLayer("ForestFloor"),
                    Painted(art.Texture("Ground_ForestFloor"), (u, v) => PaintForestFloor(u, v, theme.ForestFloor, theme.ForestFloorPatches)),
                    Normal(art.Texture("Ground_ForestFloor_Normal"), HeightForestFloor, 2.5f), Mask(art.Texture("Ground_ForestFloor_Mask"), HeightForestFloor, 0.1f),
                    8f),
                Layer(art.TerrainLayer("Dirt"), Painted(art.Texture("Ground_Dirt"), (u, v) => PaintDirt(u, v, theme.Dirt)),
                    Normal(art.Texture("Ground_Dirt_Normal"), HeightDirt, 3f), Mask(art.Texture("Ground_Dirt_Mask"), HeightDirt, 0.15f), EnvironmentAssets.VergeTile),
                Layer(art.TerrainLayer("Rock"), Painted(art.Texture("Ground_Rock"), (u, v) => PaintRock(u, v, 5f, theme.Rock)),
                    Normal(art.Texture("Ground_Rock_Normal"), (u, v) => HeightRock(u, v, 5f), 4f), Mask(art.Texture("Ground_Rock_Mask"), (u, v) => HeightRock(u, v, 5f), 0.22f),
                    10f),
            };

            return new EnvironmentAssets
            {
                RoadDirt = Lit(art.Material("Road_Dirt"), Painted(art.Texture("Road_Dirt"), (u, v) => PaintRoadDirt(u, v, theme.RoadDirt, vergeColor)), 1f,
                    normal: Normal(art.Texture("Road_Dirt_Normal"), roadHeightDirt, 3f), smoothnessFromAlpha: true),
                RoadGravel = Lit(art.Material("Road_Gravel"),
                    Painted(art.Texture("Road_Gravel"), (u, v) => PaintRoadGravel(u, v, theme.RoadGravel, theme.RoadGravelDarkStones, vergeColor)), 1f,
                    normal: Normal(art.Texture("Road_Gravel_Normal"), HeightRoadGravel, 4f), smoothnessFromAlpha: true),
                RoadMud = Lit(shared.Material("Road_Mud"), Painted(shared.Texture("Road_Mud"), PaintRoadMud), 1f,
                    normal: Normal(shared.Texture("Road_Mud_Normal"), HeightRoadMud, 3f), smoothnessFromAlpha: true),
                RoadAsphalt = Lit(shared.Material("Road_Asphalt"), Painted(shared.Texture("Road_Asphalt"), PaintRoadAsphalt), 1f,
                    normal: Normal(shared.Texture("Road_Asphalt_Normal"), HeightRoadAsphalt, 1.5f), smoothnessFromAlpha: true),
                Verge = Lit(art.Material("Verge"), Painted(art.Texture("Verge"), (u, v) => PaintVerge(u, v, theme.Dirt)), 0.12f,
                    normal: Normal(art.Texture("Verge_Normal"), HeightDirt, 3f), tiling: 8f / EnvironmentAssets.VergeTile),
                BridgeDeck = Lit(shared.Material("Bridge_Planks"), Painted(shared.Texture("Bridge_Planks"), PaintPlanks, PropTextureSize), 0.15f,
                    normal: Normal(shared.Texture("Bridge_Planks_Normal"), HeightPlanks, 3f, PropTextureSize)),
                Concrete = Lit(shared.Material("Concrete"), Painted(shared.Texture("Concrete"), (u, v) => Shade(PaintRock(u, v, 23f, GreyRock), 1.35f), PropTextureSize),
                    0.1f, instanced: true, normal: Normal(shared.Texture("Concrete_Normal"), (u, v) => HeightRock(u, v, 23f), 2f, PropTextureSize)),
                Barrier = Lit(shared.Material("Barrier"), Painted(shared.Texture("Barrier_Stripes"), PaintStripes, PropTextureSize), 0.35f, instanced: true),
                Galvanised = Lit(shared.Material("Galvanised"), Painted(shared.Texture("Galvanised"), PaintGalvanised, PropTextureSize), 0.55f, metallic: 0.75f,
                    instanced: true),
                ChevronLeft = Lit(shared.Material("Chevron_Left"),
                    Painted(shared.Texture("Sign_ChevronLeft"), (u, v) => PaintChevron(1f - u, v), PropTextureSize, repeat: false), 0.55f),
                ChevronRight = Lit(shared.Material("Chevron_Right"), Painted(shared.Texture("Sign_ChevronRight"), PaintChevron, PropTextureSize, repeat: false),
                    0.55f),
                JumpSign = Lit(shared.Material("Sign_Jump"), Painted(shared.Texture("Sign_Jump"), PaintJumpSign, PropTextureSize, repeat: false), 0.55f),
                Checker = Lit(shared.Material("Checker"), Painted(shared.Texture("Line_Checker"), PaintChecker, PropTextureSize), 0.3f),
                Water = theme.CreekWater
                    ? Lit(art.Material("Water"), Painted(art.Texture("Water"), (u, v) => PaintWater(u, v, theme.Water), PropTextureSize), 0.95f,
                        normal: Normal(art.Texture("Water_Normal"), HeightWater, 1.2f, PropTextureSize), tiling: 3f)
                    : null,
                PostWhite = Lit(shared.Material("Post_White"), null, 0.3f, new Color(0.9f, 0.9f, 0.88f), instanced: true),
                PostRed = Lit(shared.Material("Post_Red"), null, 0.5f, new Color(0.85f, 0.08f, 0.05f), instanced: true),
                Banner = Lit(shared.Material("Banner"), null, 0.3f, new Color(0.06f, 0.09f, 0.2f)),
                BannerText = TextMaterial(shared.Material("Banner_Text"), font),
                Sky = theme.CustomSky ? SkyMaterial(art.Material("Sky"), theme) : null,
                TerrainMaterial = TerrainMaterial(shared.Material("Terrain_Lit")),
                BannerFont = font,
                TerrainLayers = layers,
                TreePrefabs = treePrefabs,
                TreeShares = treeShares,
                RockPrefabs = new[]
                {
                    RockPrefab(art, "Rock_A", rock, 1, theme.RockBanding > 0f),
                    RockPrefab(art, "Rock_B", rock, 2, theme.RockBanding > 0f),
                    RockPrefab(art, "Rock_C", rock, 3, theme.RockBanding > 0f),
                },
                DetailPrefabs = new[]
                {
                    DetailPrefab(art, "Detail_GrassTuft", theme.GrassTufts, BuildGrassTuftMesh),
                    DetailPrefab(art, "Detail_Bush", theme.Bushes, BuildBushMesh),
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

        // ---------------------------------------------------------------- texture helpers

        private static readonly Palette GreyRock = new Palette(new Color(0.3f, 0.3f, 0.3f), new Color(0.52f, 0.51f, 0.49f), new Color(0.3f, 0.3f, 0.3f));

        private static Texture2D Painted(string path, Painter paint, int size = GroundTextureSize, bool repeat = true) =>
            ProceduralTextures.LoadOrCreate(path, size, paint, repeat, ArtRevision);

        private static Texture2D Normal(string path, HeightPainter height, float strength, int size = GroundTextureSize) =>
            LoadOrCreateNormalMap(path, size, height, strength, ArtRevision);

        /// <summary>A terrain layer mask: R metallic (none), G ambient occlusion from the height, B height, A smoothness.</summary>
        private static Texture2D Mask(string path, HeightPainter height, float smoothness) =>
            ProceduralTextures.LoadOrCreate(path, GroundTextureSize, (u, v) =>
            {
                float h = Mathf.Clamp01(height(u, v));
                return new Color(0f, Mathf.Lerp(0.55f, 1f, h), h, smoothness);
            }, true, ArtRevision);

        /// <summary>1 at a road's very edge, fading to 0 a little way in, with a ragged line.</summary>
        private static float EdgeWear(float u, float v, float width, float seed)
        {
            float jag = (TileableNoise(u, v, 16, 3, seed) - 0.5f) * width * 1.1f;
            float fromEdge = Mathf.Min(u, 1f - u) + jag;
            return 1f - SmoothStep(0f, width, fromEdge);
        }

        /// <summary>Stones on a grid of cells: how far into a stone the point is (0 outside, 1 at its middle), and the stone's id.</summary>
        private static float Stones(float u, float v, int cells, float size, float seed, out int id)
        {
            TileableCells(u, v, cells, seed, out float nearest, out _, out id);
            return Mathf.Clamp01(1f - nearest / size);
        }

        /// <summary>Like <see cref="Stones"/>, but each stone has its own size, between a third of and the full size.</summary>
        private static float VariedStones(float u, float v, int cells, float size, float seed, out int id)
        {
            TileableCells(u, v, cells, seed, out float nearest, out _, out id);
            float own = size * (0.35f + 0.65f * Hash(id, 9, seed));
            return Mathf.Clamp01(1f - nearest / own);
        }

        private static float WheelTracks(float u) =>
            Mathf.Clamp01(Mathf.Max(1f - Mathf.Abs(u - 0.28f) / 0.07f, 1f - Mathf.Abs(u - 0.72f) / 0.07f));

        private static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        // ---------------------------------------------------------------- ground

        private static Color PaintGrass(float u, float v, Palette colors)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 6, 4, 1f));
            float clumps = Stones(u, v, 56, 0.45f, 30f, out int id);
            color = Shade(color, 0.9f + 0.18f * clumps * Hash(id, 1, 31f) + 0.08f * TileableNoise(u, v, 96, 1, 32f));
            float dry = TileableNoise(u, v, 48, 2, 2f);
            return dry > 0.6f ? Color.Lerp(color, colors.detail, (dry - 0.6f) * 2f) : color;
        }

        private static float HeightGrass(float u, float v) =>
            0.35f + 0.25f * Stones(u, v, 56, 0.45f, 30f, out _) + 0.2f * TileableNoise(u, v, 12, 3, 33f);

        private static Color PaintForestFloor(float u, float v, Palette colors, Color patches)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 6, 4, 3f));
            if (TileableNoise(u, v, 3, 2, 4f) > 0.58f)
                color = Color.Lerp(color, patches, 0.6f);
            float needles = TileableNoise(u, v, 64, 2, 5f);
            if (needles > 0.6f)
                color = Color.Lerp(color, colors.detail, (needles - 0.6f) * 3f);
            return Shade(color, 0.9f + 0.15f * TileableNoise(u, v, 128, 1, 34f));
        }

        private static float HeightForestFloor(float u, float v) =>
            0.4f + 0.3f * TileableNoise(u, v, 16, 3, 35f) + 0.15f * TileableNoise(u, v, 64, 2, 5f);

        private static Color PaintDirt(float u, float v, Palette colors)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 8, 4, 6f));
            color = Shade(color, 0.93f + 0.12f * TileableNoise(u, v, 64, 2, 36f));
            float pebble = VariedStones(u, v, 48, 0.4f, 37f, out int id);
            if (pebble > 0f && Hash(id, 2, 38f) > 0.6f)
                color = Color.Lerp(color, Shade(colors.detail, 0.9f + 0.25f * pebble), 0.6f * Mathf.Clamp01(pebble * 3f));
            return color;
        }

        private static float HeightDirt(float u, float v)
        {
            float pebble = VariedStones(u, v, 48, 0.4f, 37f, out int id);
            return 0.3f + 0.2f * TileableNoise(u, v, 10, 3, 39f) + (Hash(id, 2, 38f) > 0.6f ? 0.45f * Mathf.Sqrt(pebble) : 0f);
        }

        /// <summary>Road shoulders: the ground's worn dirt with more stones kicked off the road.</summary>
        private static Color PaintVerge(float u, float v, Palette colors)
        {
            Color color = PaintDirt(u, v, colors);
            float stone = VariedStones(u, v, 72, 0.42f, 40f, out int id);
            if (stone > 0f && Hash(id, 4, 41f) > 0.62f)
                color = Color.Lerp(color, Shade(colors.detail, 0.85f + 0.3f * stone), 0.6f * Mathf.Clamp01(stone * 3f));
            return color;
        }

        private static Color PaintRock(float u, float v, float seed, Palette colors)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 5, 5, seed));
            TileableCells(u, v, 10, seed + 3f, out float nearest, out float second, out _);
            float crack = (1f - SmoothStep(0f, 0.035f, second - nearest)) * TileableNoise(u, v, 6, 2, seed + 4f);
            color = Shade(color, 1f - 0.3f * crack);
            return Mathf.Abs(TileableNoise(u, v, 12, 3, seed + 1f) - 0.5f) < 0.015f ? Shade(color, 0.6f) : color;
        }

        private static float HeightRock(float u, float v, float seed)
        {
            TileableCells(u, v, 10, seed + 3f, out float nearest, out float second, out _);
            float crack = (1f - SmoothStep(0f, 0.035f, second - nearest)) * TileableNoise(u, v, 6, 2, seed + 4f);
            return 0.55f + 0.35f * TileableNoise(u, v, 5, 5, seed) - 0.3f * crack + 0.1f * TileableNoise(u, v, 32, 2, seed + 5f);
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

        // ---------------------------------------------------------------- roads (u across, v along; alpha = smoothness)

        private static Color PaintRoadDirt(float u, float v, Palette colors, Color verge)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 6, 4, 8f));
            color = Shade(color, 0.93f + 0.14f * TileableNoise(u, v, 48, 2, 21f));

            // The wheels pack the dirt into darker, smoother tracks; loose stones collect between and beside them.
            float tracks = WheelTracks(u) * (0.6f + 0.4f * TileableNoise(u, v, 12, 2, 9f));
            color = Shade(color, 1f - 0.17f * tracks);
            float stone = VariedStones(u, v, 64, 0.34f, 22f, out int id);
            if (stone > 0f && Hash(id, 3, 23f) > 0.62f + 0.3f * tracks)
            {
                Color tone = Hash(id, 8, 23f) < 0.5f ? colors.dark : colors.light;
                color = Color.Lerp(color, Shade(tone, 0.9f + 0.25f * stone), 0.55f * Mathf.Clamp01(stone * 2.5f));
            }

            color = Color.Lerp(color, verge, EdgeWear(u, v, RoadEdgeWear, 24f) * 0.8f);
            return WithAlpha(color, 0.12f + 0.12f * tracks);
        }

        private static float HeightRoadDirt(float u, float v)
        {
            float tracks = WheelTracks(u);
            float stone = VariedStones(u, v, 64, 0.34f, 22f, out int id);
            float stones = Hash(id, 3, 23f) > 0.62f + 0.3f * tracks ? 0.4f * Mathf.Sqrt(stone) : 0f;
            return 0.45f - 0.18f * tracks + 0.2f * TileableNoise(u, v, 12, 3, 25f) + stones;
        }

        private static Color PaintRoadGravel(float u, float v, Palette colors, Color darkStones, Color verge)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 10, 3, 10f));
            float tracks = WheelTracks(u);

            // Packed stones everywhere, sparser in the wheel tracks, each in its own shade but close to the road's.
            float stone = VariedStones(u, v, 84, 0.5f, 11f, out int id);
            float pick = Hash(id, 5, 12f);
            if (stone > 0f && Hash(id, 6, 13f) > 0.25f + 0.45f * tracks)
            {
                Color tone = pick < 0.35f ? darkStones : pick < 0.75f ? colors.light : colors.detail;
                color = Color.Lerp(color, Shade(tone, 0.88f + 0.22f * stone), 0.7f * Mathf.Clamp01(stone * 3f));
            }

            color = Shade(color, 1f - 0.12f * tracks);
            color = Color.Lerp(color, verge, EdgeWear(u, v, RoadEdgeWear, 26f) * 0.7f);
            return WithAlpha(color, 0.14f + 0.1f * tracks);
        }

        private static float HeightRoadGravel(float u, float v)
        {
            float tracks = WheelTracks(u);
            float stone = VariedStones(u, v, 84, 0.5f, 11f, out int id);
            float stones = Hash(id, 6, 13f) > 0.25f + 0.45f * tracks ? Mathf.Sqrt(stone) : 0f;
            return 0.3f + 0.55f * stones - 0.1f * tracks;
        }

        private static Color PaintRoadMud(float u, float v)
        {
            var dark = new Color(0.16f, 0.11f, 0.07f);
            var light = new Color(0.3f, 0.21f, 0.13f);
            Color color = Color.Lerp(dark, light, TileableNoise(u, v, 5, 4, 12f));
            float tracks = WheelTracks(u) * (0.7f + 0.3f * TileableNoise(u, v, 10, 2, 14f));
            color = Shade(color, 1f - 0.3f * tracks);

            // Water lies in the ruts: dark and mirror-smooth.
            float puddle = TileableNoise(u, v, 4, 3, 13f) * (0.4f + 0.8f * tracks);
            if (puddle > 0.52f)
                return WithAlpha(Color.Lerp(color, new Color(0.08f, 0.06f, 0.045f), Mathf.Clamp01((puddle - 0.52f) * 8f)), 0.92f);
            return WithAlpha(color, 0.3f + 0.35f * tracks);
        }

        private static float HeightRoadMud(float u, float v)
        {
            float tracks = WheelTracks(u);
            float puddle = TileableNoise(u, v, 4, 3, 13f) * (0.4f + 0.8f * tracks);
            return puddle > 0.52f ? 0.2f : 0.6f - 0.35f * tracks + 0.25f * TileableNoise(u, v, 16, 3, 15f);
        }

        private static Color PaintRoadAsphalt(float u, float v)
        {
            // Fine aggregate, polished and darkened by rubber in the wheel tracks.
            float grain = TileableNoise(u, v, 128, 2, 14f);
            Color color = Color.Lerp(new Color(0.12f, 0.12f, 0.13f), new Color(0.21f, 0.21f, 0.22f), TileableNoise(u, v, 24, 3, 16f));
            color = Shade(color, 0.85f + 0.3f * grain);
            if (Stones(u, v, 160, 0.3f, 17f, out int id) > 0f && Hash(id, 7, 18f) > 0.7f)
                color = Shade(color, 1.35f);
            float tracks = WheelTracks(u);
            color = Shade(color, 1f - 0.12f * tracks);

            float lineWear = TileableNoise(u, v, 32, 2, 19f);
            bool edgeLine = (u > 0.035f && u < 0.065f) || (u > 0.935f && u < 0.965f);
            bool centreDash = Mathf.Abs(u - 0.5f) < 0.012f && Mathf.Repeat(v * 2f, 1f) < 0.5f;
            if (edgeLine && lineWear > 0.25f)
                return WithAlpha(Color.Lerp(color, new Color(0.85f, 0.85f, 0.82f), 0.9f), 0.25f);
            if (centreDash && lineWear > 0.2f)
                return WithAlpha(Color.Lerp(color, new Color(0.9f, 0.74f, 0.18f), 0.9f), 0.25f);

            // Outside the edge lines the tarmac breaks up into the gravel shoulder.
            color = Color.Lerp(color, new Color(0.38f, 0.36f, 0.33f), EdgeWear(u, v, 0.03f, 20f) * 0.85f);
            return WithAlpha(color, 0.14f + 0.12f * tracks);
        }

        private static float HeightRoadAsphalt(float u, float v)
        {
            bool line = (u > 0.035f && u < 0.065f) || (u > 0.935f && u < 0.965f);
            return 0.5f + 0.25f * TileableNoise(u, v, 128, 2, 14f) + (line ? 0.15f : 0f) - 0.3f * EdgeWear(u, v, 0.03f, 20f);
        }

        // ---------------------------------------------------------------- props

        private static Color PaintPlanks(float u, float v)
        {
            const int planks = 8;
            float plank = Mathf.Floor(v * planks);
            if (Mathf.Repeat(v * planks, 1f) < 0.07f)
                return new Color(0.12f, 0.08f, 0.05f);

            float tint = Mathf.PerlinNoise(plank * 3.1f, 0.5f);
            Color wood = Color.Lerp(new Color(0.34f, 0.23f, 0.14f), new Color(0.52f, 0.37f, 0.22f), tint);
            float grain = Mathf.Sin((u * 40f + TileableNoise(u, v, 4, 2, 15f + plank) * 6f) * Mathf.PI) * 0.5f + 0.5f;
            return Shade(wood, 0.82f + 0.2f * TileableNoise(u, v, 24, 2, 15f + plank) + 0.1f * grain);
        }

        private static float HeightPlanks(float u, float v)
        {
            const int planks = 8;
            float across = Mathf.Repeat(v * planks, 1f);
            return across < 0.07f ? 0f : 0.7f + 0.2f * TileableNoise(u, v, 24, 2, 42f);
        }

        private static Color PaintBark(float u, float v, Palette colors)
        {
            float streaks = Mathf.Sin((u * 24f + TileableNoise(u, v, 4, 2, 16f) * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            return Color.Lerp(colors.dark, colors.light, streaks * 0.6f + TileableNoise(u, v, 8, 3, 17f) * 0.4f);
        }

        private static float HeightBark(float u, float v) =>
            Mathf.Sin((u * 24f + TileableNoise(u, v, 4, 2, 16f) * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;

        /// <summary>Foliage, darker towards the bottom of each cone (v = 0) where the branches shade each other.</summary>
        private static Color PaintNeedles(float u, float v, Palette colors)
        {
            Color color = Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 12, 4, 18f));
            if (TileableNoise(u, v, 64, 1, 19f) > 0.65f)
                color = colors.detail;
            return Shade(color, Mathf.Lerp(0.62f, 1.12f, Mathf.Clamp01(v)));
        }

        private static float HeightNeedles(float u, float v) => TileableNoise(u, v, 32, 3, 43f);

        private static Color PaintStripes(float u, float v) =>
            Mathf.Repeat((u + v) * 4f, 1f) < 0.5f ? new Color(0.78f, 0.08f, 0.06f) : new Color(0.93f, 0.93f, 0.9f);

        private static Color PaintGalvanised(float u, float v)
        {
            float spangle = TileableNoise(u, v, 24, 3, 44f);
            float grime = TileableNoise(u, v, 4, 2, 45f);
            return Color.Lerp(new Color(0.5f, 0.52f, 0.54f), new Color(0.7f, 0.72f, 0.73f), spangle) * (0.85f + 0.15f * grime);
        }

        private static Color PaintChecker(float u, float v) =>
            ((int)(u * 8f) + (int)(v * 2f)) % 2 == 0 ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.95f, 0.95f, 0.95f);

        private static Color PaintWater(float u, float v, Palette colors) =>
            Color.Lerp(colors.dark, colors.light, TileableNoise(u, v, 6, 3, 20f));

        private static float HeightWater(float u, float v) => TileableNoise(u, v, 8, 4, 46f);

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

        /// <summary>A black crest (a hump) on a yellow board with a black border.</summary>
        private static Color PaintJumpSign(float u, float v)
        {
            var black = new Color(0.03f, 0.03f, 0.03f);
            if (u < 0.05f || u > 0.95f || v < 0.05f || v > 0.95f)
                return black;

            float x = (u - 0.5f) / 0.17f;
            float crest = 0.26f + 0.36f * Mathf.Exp(-x * x);
            return u > 0.14f && u < 0.86f && v > 0.2f && v < crest ? black : new Color(1f, 0.78f, 0.05f);
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(point, a + ab * t);
        }

        // ---------------------------------------------------------------- materials and layers

        /// <summary>
        /// URP Lit material, created if missing and brought up to date: base map and colour, smoothness (or smoothness from
        /// the base map's alpha), metallic, normal map and tiling.
        /// </summary>
        private static Material Lit(string path, Texture2D texture, float smoothness, Color? tint = null, bool instanced = false, float metallic = 0f,
            Texture2D normal = null, bool smoothnessFromAlpha = false, float tiling = 1f)
        {
            var material = EditorAssetUtility.LoadOrCreateLitMaterial(path, tint ?? Color.white, smoothness);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", Vector2.one * tiling);
            material.SetColor("_BaseColor", tint ?? Color.white);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.enableInstancing = instanced;

            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            SetKeyword(material, "_NORMALMAP", normal != null);

            material.SetFloat("_SmoothnessTextureChannel", smoothnessFromAlpha ? 1f : 0f);
            SetKeyword(material, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", smoothnessFromAlpha);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
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

        /// <summary>URP Terrain Lit with per-layer normal and mask maps and height-based blending, so stones and rock show through softer ground.</summary>
        private static Material TerrainMaterial(string path)
        {
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            var material = EditorAssetUtility.LoadOrCreate(path, () => new Material(shader));
            material.shader = shader;
            material.SetFloat("_EnableHeightBlend", 1f);
            material.SetFloat("_HeightTransition", 0.3f);
            material.SetFloat("_EnableInstancedPerPixelNormal", 1f);
            material.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
            material.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_MASKMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static TerrainLayer Layer(string path, Texture2D texture, Texture2D normal, Texture2D mask, float tileSize)
        {
            var layer = EditorAssetUtility.LoadOrCreate(path, () => new TerrainLayer());
            layer.diffuseTexture = texture;
            layer.normalMapTexture = normal;
            layer.normalScale = 1f;
            layer.maskMapTexture = mask;
            layer.maskMapRemapMin = Vector4.zero;
            layer.maskMapRemapMax = Vector4.one;
            layer.tileSize = new Vector2(tileSize, tileSize);
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // ---------------------------------------------------------------- meshes and prefabs

        private static GameObject TreePrefab(ThemeFolders art, TreeShape shape, Material bark, Material needles)
        {
            string path = art.Prefab(shape.name);
            var mesh = EditorAssetUtility.LoadOrCreate(art.Mesh(shape.name), () => BuildTreeMesh(shape.name, shape.cones));
            EnsureTangents(mesh);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

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
            var mesh = EditorAssetUtility.LoadOrCreate(art.Mesh(name), () => BuildRockMesh(name, seed, banded));
            EnsureTangents(mesh);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

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

        /// <summary>
        /// A terrain detail: one mesh, no collider, lit with a texture that darkens towards the ground. Its mesh is refreshed
        /// on every build; the prefab is only saved when missing or out of date, since saving a prefab can unload assets
        /// the track builder still holds.
        /// </summary>
        private static GameObject DetailPrefab(ThemeFolders art, string name, Palette colors, System.Func<string, Mesh> build)
        {
            var texture = ProceduralTextures.LoadOrCreate(art.Texture(name), 64,
                (u, v) => Color.Lerp(Shade(colors.dark, 0.7f), colors.light, Mathf.Clamp01(v * 1.6f)) * (0.92f + 0.16f * TileableNoise(u, 0.5f, 8, 1, 47f)),
                false, ArtRevision + ColorKey(colors));
            var material = Lit(art.Material(name), texture, 0.12f, instanced: true);
            material.SetFloat("_Cull", (float)CullMode.Back);

            string meshPath = art.Mesh(name);
            var mesh = build(name);
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existingMesh == null)
            {
                EditorAssetUtility.EnsureFolder(System.IO.Path.GetDirectoryName(meshPath)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                EditorUtility.CopySerialized(mesh, existingMesh);
                Object.DestroyImmediate(mesh);
                mesh = existingMesh;
            }

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(art.Prefab(name));
            if (existing != null && existing.TryGetComponent(out MeshFilter existingFilter) && existingFilter.sharedMesh == mesh
                && existing.TryGetComponent(out MeshRenderer existingRenderer) && existingRenderer.sharedMaterial == material)
                return existing;

            var root = new GameObject(name);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                EditorAssetUtility.EnsureFolder(art.PrefabFolderPath);
                return PrefabUtility.SaveAsPrefabAsset(root, art.Prefab(name));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Normal maps need tangents; meshes made before the art had normal maps get them added (their shape is unchanged).</summary>
        private static void EnsureTangents(Mesh mesh)
        {
            if (mesh.tangents.Length == mesh.vertexCount)
                return;
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
        }

        private static string ColorKey(Palette colors) => $"-{ColorUtility.ToHtmlStringRGB(colors.dark)}{ColorUtility.ToHtmlStringRGB(colors.light)}";

        /// <summary>A tuft of grass: thin blades leaning out from a point, each two-sided. Texture v runs up the blades.</summary>
        private static Mesh BuildGrassTuftMesh(string name)
        {
            var builder = new LowPolyMeshBuilder();
            var random = new System.Random(5);
            const int blades = 9;
            for (int i = 0; i < blades; i++)
            {
                float angle = (i + (float)random.NextDouble() * 0.6f) * Mathf.PI * 2f / blades;
                var out1 = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var side = new Vector3(out1.z, 0f, -out1.x) * 0.035f;
                float height = 0.35f + (float)random.NextDouble() * 0.3f;
                Vector3 root = out1 * 0.05f;
                Vector3 tip = out1 * (0.12f + (float)random.NextDouble() * 0.12f) + Vector3.up * height;
                builder.Triangle(root - side, root + side, tip, root - out1);
                builder.Triangle(root - side, root + side, tip, root + out1 * 2f);
            }

            return builder.ToMesh(name, VerticalUv);
        }

        /// <summary>A low bush: a few squashed, lumpy spheres. Texture v runs up the bush.</summary>
        private static Mesh BuildBushMesh(string name)
        {
            var builder = new LowPolyMeshBuilder();
            var lumps = new[] { (new Vector3(0f, 0.28f, 0f), 0.42f), (new Vector3(0.26f, 0.2f, 0.1f), 0.3f), (new Vector3(-0.22f, 0.2f, -0.14f), 0.32f) };
            int seed = 1;
            foreach (var (centre, radius) in lumps)
            {
                AddLump(builder, centre, radius, seed++);
            }

            return builder.ToMesh(name, VerticalUv);
        }

        private static Vector2 VerticalUv(Vector3 position) => new Vector2(position.x + position.z, position.y / 0.7f);

        private static void AddLump(LowPolyMeshBuilder builder, Vector3 centre, float radius, int seed)
        {
            // An octahedron split once, pushed out to a lumpy sphere and flattened a little.
            var corners = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[] faces = { 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5 };
            for (int f = 0; f < faces.Length; f += 3)
            {
                Vector3 a = corners[faces[f]], b = corners[faces[f + 1]], c = corners[faces[f + 2]];
                Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
                foreach (var (p, q, r) in new[] { (a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca) })
                    builder.Triangle(Lump(p, centre, radius, seed), Lump(q, centre, radius, seed), Lump(r, centre, radius, seed), centre);
            }
        }

        private static Vector3 Lump(Vector3 unit, Vector3 centre, float radius, int seed)
        {
            float noise = Mathf.PerlinNoise(unit.x * 2.1f + seed * 7.3f + 30f, unit.z * 2.1f + unit.y * 1.3f + 30f);
            Vector3 point = unit * (radius * (0.8f + 0.4f * noise));
            point.y *= 0.75f;
            return centre + point;
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
