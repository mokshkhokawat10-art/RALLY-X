using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Creates the surface assets and the placeholder rally car used until real art exists.
    /// Only missing assets are created, so tuning done in the Editor is never overwritten.
    /// </summary>
    public static class VehicleContentBuilder
    {
        public const string CarPrefabPath = "Assets/Prefabs/Vehicles/RallyCar.prefab";
        public const string SurfacesFolder = "Assets/Settings/Surfaces";
        public const string CarConfigPath = "Assets/Settings/Vehicles/RallyCar_Config.asset";

        private const string LogPrefix = "[RALLY X Build] ";

        // The first placeholder car (the Falcon RX in the garage), also driven by the AI.
        private static readonly PlaceholderCarBuilder.Spec RallyCarSpec = new PlaceholderCarBuilder.Spec
        {
            Name = "RallyCar",
            PrefabPath = CarPrefabPath,
            Paint = new Color(0.85f, 0.28f, 0.08f),
            Parts = new[]
            {
                new PlaceholderCarBuilder.Part("Body", new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 0.55f, 4.2f), PlaceholderCarBuilder.Finish.Paint, collides: true),
                new PlaceholderCarBuilder.Part("Cabin", new Vector3(0f, 1.2f, -0.25f), new Vector3(1.5f, 0.45f, 2.1f), PlaceholderCarBuilder.Finish.Cabin, collides: true),
                new PlaceholderCarBuilder.Part("Headlight_L", new Vector3(-0.6f, 0.75f, 2.11f), new Vector3(0.35f, 0.15f, 0.04f), PlaceholderCarBuilder.Finish.Light),
                new PlaceholderCarBuilder.Part("Headlight_R", new Vector3(0.6f, 0.75f, 2.11f), new Vector3(0.35f, 0.15f, 0.04f), PlaceholderCarBuilder.Finish.Light),
            },
            HalfTrack = 0.82f,
            FrontAxle = 1.35f,
            RearAxle = -1.3f,
        };

        private static readonly SurfaceDefinition[] SurfaceDefinitions =
        {
            new SurfaceDefinition("Asphalt", grip: 1f, topSpeed: 1f, acceleration: 1f, rollingResistance: 0f),
            new SurfaceDefinition("Dirt", grip: 0.85f, topSpeed: 0.95f, acceleration: 0.95f, rollingResistance: 0.4f),
            new SurfaceDefinition("Gravel", grip: 0.72f, topSpeed: 0.92f, acceleration: 0.9f, rollingResistance: 0.6f),
            new SurfaceDefinition("Mud", grip: 0.55f, topSpeed: 0.78f, acceleration: 0.6f, rollingResistance: 2f),
            new SurfaceDefinition("Grass", grip: 0.45f, topSpeed: 0.7f, acceleration: 0.8f, rollingResistance: 1.5f),
            // Loose desert ground off the road (Red Canyon): like grass, but it drags a little more.
            new SurfaceDefinition("Sand", grip: 0.45f, topSpeed: 0.68f, acceleration: 0.75f, rollingResistance: 1.8f),
        };

        [MenuItem("RALLY X/Build/Create Missing Vehicle Content")]
        public static void BuildMissingFromMenu() => BuildMissing();

        [MenuItem("RALLY X/Build/Rebuild Placeholder Car Prefab")]
        public static void RebuildCarFromMenu()
        {
            if (EditorUtility.DisplayDialog("Rebuild placeholder car",
                    $"Overwrite {CarPrefabPath}? Manual changes to the prefab will be lost.", "Rebuild", "Cancel"))
                BuildCarPrefab(overwrite: true);
        }

        public static bool BuildMissing()
        {
            BuildSurfaces();
            bool carOk = BuildCarPrefab(overwrite: false) && EnsureLivery();
            AssetDatabase.SaveAssets();
            return carOk;
        }

        public static SurfaceType LoadSurface(string surfaceName) =>
            AssetDatabase.LoadAssetAtPath<SurfaceType>($"{SurfacesFolder}/Surface_{surfaceName}.asset");

        private static void BuildSurfaces()
        {
            foreach (var definition in SurfaceDefinitions)
            {
                string path = $"{SurfacesFolder}/Surface_{definition.Name}.asset";
                if (AssetDatabase.LoadAssetAtPath<SurfaceType>(path) != null)
                    continue;

                var surface = EditorAssetUtility.LoadOrCreate(path, ScriptableObject.CreateInstance<SurfaceType>);
                var serialized = new SerializedObject(surface);
                serialized.FindProperty("displayName").stringValue = definition.Name;
                serialized.FindProperty("grip").floatValue = definition.Grip;
                serialized.FindProperty("topSpeed").floatValue = definition.TopSpeed;
                serialized.FindProperty("acceleration").floatValue = definition.Acceleration;
                serialized.FindProperty("rollingResistance").floatValue = definition.RollingResistance;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static bool BuildCarPrefab(bool overwrite)
        {
            var config = EditorAssetUtility.LoadOrCreate(CarConfigPath, ScriptableObject.CreateInstance<VehicleConfig>);
            return PlaceholderCarBuilder.Build(RallyCarSpec, config, overwrite);
        }

        /// <summary>Adds the paint component to a car prefab built before it existed, keeping any other edits to the prefab.</summary>
        private static bool EnsureLivery()
        {
            var contents = PrefabUtility.LoadPrefabContents(CarPrefabPath);
            try
            {
                if (contents.GetComponent<VehicleLivery>() != null)
                    return true;

                AddLivery(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, CarPrefabPath, out bool saved);
                if (!saved)
                {
                    Debug.LogError(LogPrefix + $"Failed to add paint to {CarPrefabPath}.");
                    return false;
                }

                Debug.Log(LogPrefix + $"Added VehicleLivery to {CarPrefabPath}.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>The body panels are the paint; the cabin, lights and tyres keep their own colours.</summary>
        private static void AddLivery(GameObject car)
        {
            var body = car.transform.Find("Body");
            var painted = body != null ? new[] { body.GetComponent<Renderer>() } : System.Array.Empty<Renderer>();
            car.AddComponent<VehicleLivery>().SetPaintedRenderers(painted);
        }

        private readonly struct SurfaceDefinition
        {
            public readonly string Name;
            public readonly float Grip;
            public readonly float TopSpeed;
            public readonly float Acceleration;
            public readonly float RollingResistance;

            public SurfaceDefinition(string name, float grip, float topSpeed, float acceleration, float rollingResistance)
            {
                Name = name;
                Grip = grip;
                TopSpeed = topSpeed;
                Acceleration = acceleration;
                RollingResistance = rollingResistance;
            }
        }
    }
}
