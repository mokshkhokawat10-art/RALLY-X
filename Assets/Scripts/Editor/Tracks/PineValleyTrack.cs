using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Initial authoring of Pine Valley. Creates the definition asset once; after that the asset is the source of
    /// truth and can be edited in the Inspector before rebuilding the scene.
    /// </summary>
    public static class PineValleyTrack
    {
        public const string DefinitionPath = "Assets/Settings/Tracks/PineValley_Track.asset";
        public const string ThemePath = "Assets/Settings/Tracks/Themes/Forest_Theme.asset";

        public static TrackDefinition LoadOrCreate()
        {
            var definition = EditorAssetUtility.LoadOrCreate(DefinitionPath, CreateDefinition);

            // A definition made before themes existed gets the forest theme, which is how it already looked.
            if (definition.Theme == null)
            {
                definition.SetTheme(EditorAssetUtility.LoadOrCreate(ThemePath, TrackTheme.CreateForest));
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
            }

            return definition;
        }

        private static TrackDefinition CreateDefinition()
        {
            var asphalt = VehicleContentBuilder.LoadSurface("Asphalt");
            var dirt = VehicleContentBuilder.LoadSurface("Dirt");
            var gravel = VehicleContentBuilder.LoadSurface("Gravel");
            var mud = VehicleContentBuilder.LoadSurface("Mud");

            // Counter-clockwise loop on a 640 m map: asphalt start straight (south), climb to a crest jump (east),
            // bridge and gravel run to a hairpin (north), S-curves, mud and a second bridge and jump (west).
            var nodes = new[]
            {
                new TrackNode(200f, 88f, 10f, 10f, asphalt),   // 0  start/finish
                new TrackNode(320f, 90f, 10.5f, 9.5f, dirt),   // 1
                new TrackNode(420f, 100f, 11.5f, 9f, dirt),    // 2  end of the long straight
                new TrackNode(495f, 135f, 13f, 9f, dirt),      // 3  sweeping left
                new TrackNode(535f, 205f, 17f, 9f, dirt),      // 4  climb
                new TrackNode(548f, 290f, 23f, 9f, dirt),      // 5
                new TrackNode(535f, 365f, 27f, 9f, dirt),      // 6  crest
                new TrackNode(500f, 425f, 22f, 9f, dirt),      // 7  descent with jump
                new TrackNode(452f, 468f, 19f, 9f, dirt),      // 8  bridge 1
                new TrackNode(400f, 515f, 17f, 9f, gravel),    // 9  gravel section
                new TrackNode(330f, 540f, 16f, 9f, gravel),    // 10
                new TrackNode(262f, 545f, 16f, 9f, gravel),    // 11
                new TrackNode(192f, 560f, 17f, 11f, gravel),   // 12 hairpin entry
                new TrackNode(140f, 548f, 18f, 12f, gravel),   // 13
                new TrackNode(128f, 508f, 18f, 12f, gravel),   // 14 hairpin
                new TrackNode(168f, 488f, 17f, 10f, dirt),     // 15
                new TrackNode(215f, 462f, 16f, 9f, dirt),      // 16 right-hander
                new TrackNode(212f, 412f, 15f, 9f, dirt),      // 17 S-curves (shortcut leaves)
                new TrackNode(172f, 365f, 14f, 9f, dirt),      // 18
                new TrackNode(205f, 318f, 13f, 9f, mud),       // 19 mud patch (shortcut rejoins)
                new TrackNode(170f, 268f, 12f, 9f, dirt),      // 20 bridge 2
                new TrackNode(150f, 205f, 11f, 9f, dirt),      // 21 jump 2
                new TrackNode(140f, 150f, 10f, 9f, dirt),      // 22
                new TrackNode(150f, 100f, 10f, 10f, asphalt),  // 23 corner onto the start straight
            };

            var jumps = new[]
            {
                // Sits on a descent, so a long landing keeps the combined downhill grade drivable.
                new TrackJump { start = new TrackAnchor(6, 0.25f), height = 1.3f, rampLength = 16f, landingLength = 44f },
                new TrackJump { start = new TrackAnchor(21, 0.35f), height = 1.3f, rampLength = 14f, landingLength = 26f },
            };

            var bridges = new[]
            {
                new TrackBridge { center = new TrackAnchor(8, 0.5f), length = 22f, creekDepth = 4f },
                new TrackBridge { center = new TrackAnchor(20, 0.55f), length = 18f, creekDepth = 3.5f },
            };

            var shortcuts = new[]
            {
                new TrackShortcut
                {
                    from = new TrackAnchor(17, 0.05f),
                    to = new TrackAnchor(19, 0.1f),
                    waypoints = new[] { new Vector2(210f, 365f) },
                    width = 5.5f,
                    surface = mud,
                },
            };

            var definition = ScriptableObject.CreateInstance<TrackDefinition>();
            definition.SetLayout("Pine Valley", "PineValley", nodes, jumps, bridges, shortcuts);
            return definition;
        }
    }
}
