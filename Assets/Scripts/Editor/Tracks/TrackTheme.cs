using System;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// How a generated track looks: sun, fog and sky, the colours of the ground, roads, trees and rocks, the shapes of
    /// its trees and whether its creeks hold water. The defaults are Pine Valley's pine forest. A theme's placeholder
    /// art is generated once into its own folders; delete those to regenerate it after changing colours or shapes.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Track Theme", fileName = "Theme")]
    public sealed class TrackTheme : ScriptableObject
    {
        [Tooltip("Subfolder for this theme's generated textures, materials, meshes and prefabs. Empty = the shared folders, " +
                 "which hold the original forest art.")]
        [SerializeField] private string artFolder = string.Empty;

        [Header("Sun and Sky")]
        [SerializeField] private Vector3 sunRotation = new Vector3(40f, -35f, 0f);
        [SerializeField, Min(0f)] private float sunIntensity = 1.3f;
        [SerializeField] private Color sunColor = new Color(1f, 0.95f, 0.86f);
        [SerializeField] private Color fogColor = new Color(0.64f, 0.72f, 0.78f);
        [SerializeField, Min(0f)] private float fogDensity = 0.0032f;

        [Tooltip("Use this theme's own sky and ambient light. Off = Unity's default sky, lighting the scene.")]
        [SerializeField] private bool customSky;
        [SerializeField] private Color skyTint = new Color(0.5f, 0.5f, 0.5f);
        [SerializeField] private Color skyGround = new Color(0.37f, 0.35f, 0.34f);
        [SerializeField, Range(0f, 5f)] private float atmosphereThickness = 1f;
        [SerializeField, Range(0f, 8f)] private float skyExposure = 1.3f;
        [Tooltip("Ambient light from above, the horizon and below, with a custom sky.")]
        [SerializeField] private Color ambientSky = new Color(0.55f, 0.6f, 0.68f);
        [SerializeField] private Color ambientEquator = new Color(0.45f, 0.44f, 0.42f);
        [SerializeField] private Color ambientGround = new Color(0.25f, 0.22f, 0.2f);

        [Header("Ground")]
        [Tooltip("What the wheels feel off the road. None = Grass.")]
        [SerializeField] private SurfaceType offRoadSurface;
        [Tooltip("Terrain slope, degrees, where bare rock starts to show.")]
        [SerializeField, Range(5f, 60f)] private float rockSlope = 30f;
        [Tooltip("Degrees of slope over which the ground turns fully to rock.")]
        [SerializeField, Range(1f, 30f)] private float rockBlend = 12f;
        [Tooltip("Open ground: dark, light, and the dry patches.")]
        [SerializeField] private Palette grass = new Palette(new Color(0.16f, 0.27f, 0.09f), new Color(0.3f, 0.42f, 0.17f), new Color(0.45f, 0.44f, 0.22f));
        [Tooltip("Ground under the trees: dark, light, and the needles; plus the moss patches.")]
        [SerializeField] private Palette forestFloor = new Palette(new Color(0.13f, 0.1f, 0.06f), new Color(0.26f, 0.2f, 0.12f), new Color(0.36f, 0.25f, 0.14f));
        [SerializeField] private Color forestFloorPatches = new Color(0.17f, 0.25f, 0.09f);
        [Tooltip("Worn ground beside the roads: dark, light, and the pebbles.")]
        [SerializeField] private Palette dirt = new Palette(new Color(0.33f, 0.24f, 0.16f), new Color(0.5f, 0.39f, 0.27f), new Color(0.58f, 0.52f, 0.45f));
        [Tooltip("Rock on steep ground and the boulders: dark and light (the third colour is unused).")]
        [SerializeField] private Palette rock = new Palette(new Color(0.3f, 0.3f, 0.3f), new Color(0.52f, 0.51f, 0.49f), new Color(0.3f, 0.3f, 0.3f));
        [Tooltip("Horizontal bands in the boulders' rock, 0 = none (sedimentary rock such as sandstone).")]
        [SerializeField, Range(0f, 1f)] private float rockBanding;

        [Header("Roads")]
        [Tooltip("Dirt roads: dark and light (the third colour is unused).")]
        [SerializeField] private Palette roadDirt = new Palette(new Color(0.42f, 0.32f, 0.22f), new Color(0.56f, 0.44f, 0.31f), new Color(0.56f, 0.44f, 0.31f));
        [Tooltip("Gravel roads: dark and light, and the light stones; plus the dark stones.")]
        [SerializeField] private Palette roadGravel = new Palette(new Color(0.46f, 0.44f, 0.4f), new Color(0.62f, 0.59f, 0.54f), new Color(0.72f, 0.7f, 0.66f));
        [SerializeField] private Color roadGravelDarkStones = new Color(0.36f, 0.34f, 0.31f);

        [Header("Trees")]
        [Tooltip("Tree shapes, as stacked cones on a trunk. Each tree picks a shape by its share.")]
        [SerializeField] private TreeShape[] trees =
        {
            new TreeShape("PineTall", 0.6f, new TreeCone(1.8f, 5.4f, 1.9f), new TreeCone(3.5f, 6.9f, 1.5f), new TreeCone(5.1f, 8.4f, 1.05f)),
            new TreeShape("PineBushy", 0.4f, new TreeCone(1.2f, 4.2f, 2.3f), new TreeCone(2.6f, 5.4f, 1.9f), new TreeCone(3.9f, 6.5f, 1.4f),
                new TreeCone(5.1f, 7.3f, 0.9f)),
        };
        [SerializeField] private Palette bark = new Palette(new Color(0.22f, 0.15f, 0.1f), new Color(0.4f, 0.28f, 0.18f), new Color(0.4f, 0.28f, 0.18f));
        [Tooltip("Foliage: dark, light, and the highlights.")]
        [SerializeField] private Palette foliage = new Palette(new Color(0.06f, 0.16f, 0.07f), new Color(0.15f, 0.3f, 0.12f), new Color(0.24f, 0.4f, 0.18f));

        [Header("Water")]
        [Tooltip("Creeks under bridges hold water. Off = dry washes with worn ground in their beds.")]
        [SerializeField] private bool creekWater = true;
        [SerializeField] private Palette water = new Palette(new Color(0.06f, 0.14f, 0.16f), new Color(0.12f, 0.24f, 0.26f), new Color(0.12f, 0.24f, 0.26f));

        public string ArtFolder => artFolder;
        public Vector3 SunRotation => sunRotation;
        public float SunIntensity => sunIntensity;
        public Color SunColor => sunColor;
        public Color FogColor => fogColor;
        public float FogDensity => fogDensity;
        public bool CustomSky => customSky;
        public Color SkyTint => skyTint;
        public Color SkyGround => skyGround;
        public float AtmosphereThickness => atmosphereThickness;
        public float SkyExposure => skyExposure;
        public Color AmbientSky => ambientSky;
        public Color AmbientEquator => ambientEquator;
        public Color AmbientGround => ambientGround;
        public SurfaceType OffRoadSurface => offRoadSurface;
        public float RockSlope => rockSlope;
        public float RockBlend => rockBlend;
        public Palette Grass => grass;
        public Palette ForestFloor => forestFloor;
        public Color ForestFloorPatches => forestFloorPatches;
        public Palette Dirt => dirt;
        public Palette Rock => rock;
        public float RockBanding => rockBanding;
        public Palette RoadDirt => roadDirt;
        public Palette RoadGravel => roadGravel;
        public Color RoadGravelDarkStones => roadGravelDarkStones;
        public TreeShape[] Trees => trees;
        public Palette Bark => bark;
        public Palette Foliage => foliage;
        public bool CreekWater => creekWater;
        public Palette Water => water;

        /// <summary>The original pine forest, for tracks without a theme.</summary>
        public static TrackTheme CreateForest()
        {
            var theme = CreateInstance<TrackTheme>();
            theme.name = "Forest";
            return theme;
        }

        /// <summary>Sets the fields a theme's author changes. Anything left out keeps the forest default.</summary>
        public void SetArt(string artFolder, Palette grass, Palette forestFloor, Color forestFloorPatches, Palette dirt, Palette rock,
            float rockBanding, float rockSlope, Palette roadDirt, Palette roadGravel, Color roadGravelDarkStones, TreeShape[] trees,
            Palette bark, Palette foliage, bool creekWater, SurfaceType offRoadSurface)
        {
            this.artFolder = artFolder;
            this.grass = grass;
            this.forestFloor = forestFloor;
            this.forestFloorPatches = forestFloorPatches;
            this.dirt = dirt;
            this.rock = rock;
            this.rockBanding = rockBanding;
            this.rockSlope = rockSlope;
            this.roadDirt = roadDirt;
            this.roadGravel = roadGravel;
            this.roadGravelDarkStones = roadGravelDarkStones;
            this.trees = trees;
            this.bark = bark;
            this.foliage = foliage;
            this.creekWater = creekWater;
            this.offRoadSurface = offRoadSurface;
        }

        /// <summary>Gives the theme its own sun, fog, sky and ambient light.</summary>
        public void SetSky(Vector3 sunRotation, float sunIntensity, Color sunColor, Color fogColor, float fogDensity, Color skyTint,
            Color skyGround, float atmosphereThickness, float skyExposure, Color ambientSky, Color ambientEquator, Color ambientGround)
        {
            this.sunRotation = sunRotation;
            this.sunIntensity = sunIntensity;
            this.sunColor = sunColor;
            this.fogColor = fogColor;
            this.fogDensity = fogDensity;
            customSky = true;
            this.skyTint = skyTint;
            this.skyGround = skyGround;
            this.atmosphereThickness = atmosphereThickness;
            this.skyExposure = skyExposure;
            this.ambientSky = ambientSky;
            this.ambientEquator = ambientEquator;
            this.ambientGround = ambientGround;
        }
    }

    /// <summary>Three colours for a painted texture: its dark and light tones and a detail colour.</summary>
    [Serializable]
    public struct Palette
    {
        public Color dark;
        public Color light;
        public Color detail;

        public Palette(Color dark, Color light, Color detail)
        {
            this.dark = dark;
            this.light = light;
            this.detail = detail;
        }
    }

    /// <summary>One cone of a low-poly tree: from its base height to its tip, and its radius at the base.</summary>
    [Serializable]
    public struct TreeCone
    {
        [Min(0f)] public float baseHeight;
        [Min(0.1f)] public float tipHeight;
        [Min(0.1f)] public float radius;

        public TreeCone(float baseHeight, float tipHeight, float radius)
        {
            this.baseHeight = baseHeight;
            this.tipHeight = tipHeight;
            this.radius = radius;
        }
    }

    /// <summary>A tree shape: cones stacked on a trunk, and its share of the trees planted.</summary>
    [Serializable]
    public struct TreeShape
    {
        [Tooltip("Name of the generated mesh and prefab.")]
        public string name;
        [Range(0f, 1f)] public float share;
        public TreeCone[] cones;

        public TreeShape(string name, float share, params TreeCone[] cones)
        {
            this.name = name;
            this.share = share;
            this.cones = cones;
        }
    }
}
