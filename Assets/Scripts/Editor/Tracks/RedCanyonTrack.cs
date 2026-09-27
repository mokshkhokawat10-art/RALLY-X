using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Initial authoring of Red Canyon and its dry canyon theme. Creates the definition and theme assets once; after that
    /// the assets are the source of truth and can be edited in the Inspector before rebuilding the scene.
    /// </summary>
    public static class RedCanyonTrack
    {
        public const string DefinitionPath = "Assets/Settings/Tracks/RedCanyon_Track.asset";
        public const string ThemePath = "Assets/Settings/Tracks/Themes/RedCanyon_Theme.asset";

        public static TrackDefinition LoadOrCreate()
        {
            var theme = EditorAssetUtility.LoadOrCreate(ThemePath, CreateTheme);
            return EditorAssetUtility.LoadOrCreate(DefinitionPath, () => CreateDefinition(theme));
        }

        private static TrackDefinition CreateDefinition(TrackTheme theme)
        {
            var asphalt = VehicleContentBuilder.LoadSurface("Asphalt");
            var dirt = VehicleContentBuilder.LoadSurface("Dirt");
            var gravel = VehicleContentBuilder.LoadSurface("Gravel");

            // Clockwise on a 700 m map: asphalt start straight heading west along the canyon floor (south), fast gravel
            // up the west side over a dry wash, two switchbacks climbing onto the mesa (north-west), the mesa top with a
            // crest jump (north), then a gravel descent (east) with an S-bend round a butte and a second jump.
            var nodes = new[]
            {
                new TrackNode(390f, 104f, 8f, 11f, asphalt),    // 0  start/finish
                new TrackNode(300f, 100f, 8f, 11f, asphalt),    // 1
                new TrackNode(215f, 104f, 8.5f, 10.5f, gravel), // 2  end of the straight: brake for the sweeper
                new TrackNode(150f, 130f, 9f, 10f, gravel),     // 3
                new TrackNode(112f, 190f, 10f, 10f, gravel),    // 4  canyon floor, west side
                new TrackNode(100f, 262f, 11.5f, 10f, gravel),  // 5  bridge over the dry wash
                new TrackNode(108f, 335f, 13f, 10f, dirt),      // 6
                new TrackNode(100f, 400f, 15f, 10f, dirt),      // 7
                new TrackNode(122f, 452f, 18f, 10f, dirt),      // 8  the climb starts
                new TrackNode(170f, 475f, 20.5f, 9.5f, dirt),   // 9  first switchback leg, heading east
                new TrackNode(245f, 472f, 24f, 9.5f, dirt),     // 10
                new TrackNode(300f, 470f, 26.5f, 9.5f, dirt),   // 11
                new TrackNode(330f, 488f, 28f, 11f, gravel),    // 12 hairpin left
                new TrackNode(318f, 515f, 29f, 11f, gravel),    // 13
                new TrackNode(285f, 520f, 30.5f, 9.5f, dirt),   // 14 second leg, heading west
                new TrackNode(225f, 518f, 33.5f, 9.5f, dirt),   // 15
                new TrackNode(178f, 530f, 35f, 11f, gravel),    // 16 hairpin right
                new TrackNode(175f, 560f, 36f, 11f, gravel),    // 17
                new TrackNode(215f, 578f, 37f, 10f, dirt),      // 18 mesa top, heading east
                new TrackNode(300f, 580f, 38.5f, 10f, dirt),    // 19 crest jump
                new TrackNode(385f, 572f, 38f, 10f, dirt),      // 20
                new TrackNode(460f, 555f, 37f, 10f, dirt),      // 21
                new TrackNode(520f, 520f, 35f, 10.5f, dirt),    // 22 right onto the descent
                new TrackNode(548f, 460f, 31f, 10f, gravel),    // 23
                new TrackNode(560f, 400f, 27f, 10f, gravel),    // 24 S-bend round the butte (shortcut leaves)
                new TrackNode(510f, 352f, 24f, 10f, gravel),    // 25
                new TrackNode(545f, 300f, 21f, 10f, gravel),    // 26 (shortcut rejoins)
                new TrackNode(535f, 235f, 16f, 10f, dirt),      // 27 jump on the descent
                new TrackNode(510f, 170f, 12f, 10f, dirt),      // 28
                new TrackNode(478f, 124f, 9f, 10.5f, asphalt),  // 29 corner onto the start straight
            };

            var jumps = new[]
            {
                new TrackJump { start = new TrackAnchor(19, 0.3f), height = 1.3f, rampLength = 16f, landingLength = 34f },
                // On the descent, so a long landing keeps the combined downhill grade drivable.
                new TrackJump { start = new TrackAnchor(27, 0.3f), height = 1.2f, rampLength = 14f, landingLength = 32f },
            };

            var bridges = new[]
            {
                new TrackBridge { center = new TrackAnchor(5, 0.5f), length = 20f, creekDepth = 3.5f },
            };

            var shortcuts = new[]
            {
                // A narrow gully straight down past the butte the main road swings round.
                new TrackShortcut
                {
                    from = new TrackAnchor(24, 0.15f),
                    to = new TrackAnchor(26, 0.15f),
                    waypoints = new[] { new Vector2(552f, 352f) },
                    width = 5.5f,
                    surface = gravel,
                },
            };

            var definition = ScriptableObject.CreateInstance<TrackDefinition>();
            definition.SetLayout("Red Canyon", "RedCanyon", nodes, jumps, bridges, shortcuts);
            definition.SetTheme(theme);

            // Canyon country: a bigger map, high stepped walls and sparse scrub among the rocks.
            var serialized = new SerializedObject(definition);
            SetFloat(serialized, "terrainSize", 700f);
            SetFloat(serialized, "terrainMaxHeight", 140f);
            SetFloat(serialized, "baseHeight", 12f);
            SetFloat(serialized, "noiseAmplitude", 26f);
            SetFloat(serialized, "noiseScale", 140f);
            SetFloat(serialized, "rimHeight", 70f);
            SetFloat(serialized, "rimStart", 0.78f);
            SetFloat(serialized, "embankmentWidth", 14f);
            SetFloat(serialized, "terraceHeight", 8f);
            SetFloat(serialized, "terraceWall", 0.2f);
            serialized.FindProperty("seed").intValue = 7321;
            SetFloat(serialized, "treeSpacing", 18f);
            SetFloat(serialized, "treeClearance", 6f);
            SetFloat(serialized, "clearingAmount", 0.5f);
            SetFloat(serialized, "roadsideRockChance", 0.7f);
            serialized.FindProperty("forestBoulderCount").intValue = 140;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private static TrackTheme CreateTheme()
        {
            var theme = ScriptableObject.CreateInstance<TrackTheme>();
            theme.SetArt(artFolder: "RedCanyon",
                grass: new Palette(new Color(0.7f, 0.55f, 0.4f), new Color(0.82f, 0.68f, 0.52f), new Color(0.88f, 0.78f, 0.62f)),
                forestFloor: new Palette(new Color(0.6f, 0.47f, 0.35f), new Color(0.72f, 0.59f, 0.45f), new Color(0.5f, 0.44f, 0.3f)),
                forestFloorPatches: new Color(0.52f, 0.5f, 0.36f),
                dirt: new Palette(new Color(0.76f, 0.63f, 0.5f), new Color(0.87f, 0.76f, 0.62f), new Color(0.6f, 0.5f, 0.42f)),
                rock: new Palette(new Color(0.4f, 0.15f, 0.08f), new Color(0.64f, 0.29f, 0.15f), new Color(0.4f, 0.15f, 0.08f)),
                rockBanding: 0.8f,
                rockSlope: 20f,
                roadDirt: new Palette(new Color(0.6f, 0.38f, 0.25f), new Color(0.74f, 0.5f, 0.34f), new Color(0.74f, 0.5f, 0.34f)),
                roadGravel: new Palette(new Color(0.58f, 0.48f, 0.4f), new Color(0.72f, 0.62f, 0.52f), new Color(0.84f, 0.76f, 0.66f)),
                roadGravelDarkStones: new Color(0.46f, 0.34f, 0.27f),
                trees: new[]
                {
                    new TreeShape("Juniper", 0.55f, new TreeCone(0.6f, 1.7f, 1.5f), new TreeCone(1.2f, 2.5f, 1.45f), new TreeCone(1.9f, 3.2f, 1.2f),
                        new TreeCone(2.6f, 3.7f, 0.7f)),
                    new TreeShape("Sagebrush", 0.45f, new TreeCone(0.05f, 1.1f, 1.3f), new TreeCone(0.5f, 1.6f, 0.8f)),
                },
                bark: new Palette(new Color(0.3f, 0.24f, 0.2f), new Color(0.5f, 0.42f, 0.34f), new Color(0.5f, 0.42f, 0.34f)),
                foliage: new Palette(new Color(0.2f, 0.24f, 0.12f), new Color(0.36f, 0.4f, 0.22f), new Color(0.48f, 0.5f, 0.3f)),
                creekWater: false,
                offRoadSurface: VehicleContentBuilder.LoadSurface("Sand"));
            theme.SetSky(
                sunRotation: new Vector3(32f, 125f, 0f),
                sunIntensity: 1.45f,
                sunColor: new Color(1f, 0.86f, 0.68f),
                fogColor: new Color(0.86f, 0.72f, 0.58f),
                fogDensity: 0.0022f,
                skyTint: new Color(0.55f, 0.48f, 0.42f),
                skyGround: new Color(0.45f, 0.3f, 0.2f),
                atmosphereThickness: 1.3f,
                skyExposure: 1.25f,
                ambientSky: new Color(0.78f, 0.72f, 0.66f),
                ambientEquator: new Color(0.68f, 0.54f, 0.42f),
                ambientGround: new Color(0.42f, 0.28f, 0.2f));

            // Warm light with cool, slightly purple shadows; sparse dry grass and sage.
            theme.SetMood(shadowStrength: 0.82f, postExposure: 0.45f, contrast: 12f, saturation: 10f, temperature: 9f,
                shadowTint: new Color(0.47f, 0.46f, 0.56f), highlightTint: new Color(0.6f, 0.52f, 0.42f), bloom: 0.4f, vignette: 0.26f,
                vegetationDensity: 0.55f,
                grassTufts: new Palette(new Color(0.55f, 0.46f, 0.28f), new Color(0.78f, 0.68f, 0.44f), new Color(0.78f, 0.68f, 0.44f)),
                bushes: new Palette(new Color(0.3f, 0.34f, 0.2f), new Color(0.46f, 0.5f, 0.32f), new Color(0.46f, 0.5f, 0.32f)));
            return theme;
        }

        private static void SetFloat(SerializedObject serialized, string property, float value) =>
            serialized.FindProperty(property).floatValue = value;
    }
}
