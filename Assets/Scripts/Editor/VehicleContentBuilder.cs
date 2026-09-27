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

        // The first placeholder car (the Falcon RX in the garage), also driven by the AI: a Group R4 hatch in orange with a
        // white roof, stripe and wheels, black arches, a four-lamp light pod on the bonnet, a roof vent and a hatch spoiler.
        private static readonly PlaceholderCarBuilder.Spec RallyCarSpec = new PlaceholderCarBuilder.Spec
        {
            Name = "RallyCar",
            PrefabPath = CarPrefabPath,
            Paint = new Color(0.85f, 0.28f, 0.08f),
            Trim = new Color(0.93f, 0.93f, 0.9f),
            Rim = new Color(0.9f, 0.9f, 0.88f),
            Caliper = new Color(0.82f, 0.08f, 0.05f),
            RaceNumber = 7,
            Body = new[]
            {
                new BodyStation(-2.1f, 0.42f, 0.86f, 0.8f, 0.76f, 0.08f),
                new BodyStation(-2.04f, 0.36f, 0.95f, 0.87f, 0.83f, 0.1f),
                new BodyStation(-1.6f, 0.33f, 0.99f, 0.9f, 0.85f, 0.12f),
                new BodyStation(0.75f, 0.33f, 0.99f, 0.9f, 0.85f, 0.12f),
                new BodyStation(1.45f, 0.33f, 0.93f, 0.9f, 0.84f, 0.12f),
                new BodyStation(2f, 0.34f, 0.84f, 0.88f, 0.8f, 0.1f),
                new BodyStation(2.1f, 0.4f, 0.78f, 0.8f, 0.74f, 0.08f),
            },
            Cabin = new[]
            {
                new BodyStation(0.78f, 0.96f, 1f, 0.82f, 0.8f, 0.02f),
                new BodyStation(0.05f, 0.96f, 1.42f, 0.8f, 0.66f, 0.06f),
                new BodyStation(-0.95f, 0.96f, 1.43f, 0.8f, 0.67f, 0.06f),
                new BodyStation(-1.5f, 0.96f, 1.06f, 0.81f, 0.74f, 0.03f),
            },
            RoofFront = 0.05f,
            RoofRear = -0.95f,
            TrimRoof = true,
            FlareWidth = 0.05f,
            Details = new[]
            {
                new PlaceholderCarBuilder.Part("SideStripe_L", new Vector3(-0.868f, 0.8f, 0f), new Vector3(0.01f, 0.07f, 3.7f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("SideStripe_R", new Vector3(0.868f, 0.8f, 0f), new Vector3(0.01f, 0.07f, 3.7f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("LightPod", new Vector3(0f, 0.945f, 1.72f), new Vector3(1.12f, 0.15f, 0.12f), PlaceholderCarBuilder.Finish.Plastic),
                new PlaceholderCarBuilder.Part("Lamp_1", new Vector3(-0.405f, 0.945f, 1.785f), new Vector3(0.19f, 0.19f, 0.03f), PlaceholderCarBuilder.Finish.HeadLight,
                    shape: PlaceholderCarBuilder.Shape.Cylinder),
                new PlaceholderCarBuilder.Part("Lamp_2", new Vector3(-0.135f, 0.945f, 1.785f), new Vector3(0.19f, 0.19f, 0.03f), PlaceholderCarBuilder.Finish.HeadLight,
                    shape: PlaceholderCarBuilder.Shape.Cylinder),
                new PlaceholderCarBuilder.Part("Lamp_3", new Vector3(0.135f, 0.945f, 1.785f), new Vector3(0.19f, 0.19f, 0.03f), PlaceholderCarBuilder.Finish.HeadLight,
                    shape: PlaceholderCarBuilder.Shape.Cylinder),
                new PlaceholderCarBuilder.Part("Lamp_4", new Vector3(0.405f, 0.945f, 1.785f), new Vector3(0.19f, 0.19f, 0.03f), PlaceholderCarBuilder.Finish.HeadLight,
                    shape: PlaceholderCarBuilder.Shape.Cylinder),
                new PlaceholderCarBuilder.Part("RoofVent", new Vector3(0f, 1.47f, -0.15f), new Vector3(0.36f, 0.08f, 0.42f), PlaceholderCarBuilder.Finish.Plastic),
                new PlaceholderCarBuilder.Part("Spoiler", new Vector3(0f, 1.42f, -1.02f), new Vector3(1.3f, 0.05f, 0.34f), PlaceholderCarBuilder.Finish.Paint,
                    rotation: new Vector3(-8f, 0f, 0f)),
            },
            Exhausts = new[] { new Vector3(0.5f, 0.38f, -2.12f) },
            Colliders = new[]
            {
                new PlaceholderCarBuilder.ColliderBox("BodyCollider", new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 0.55f, 4.2f)),
                new PlaceholderCarBuilder.ColliderBox("CabinCollider", new Vector3(0f, 1.2f, -0.25f), new Vector3(1.5f, 0.45f, 2.1f)),
            },
            HalfTrack = 0.82f,
            FrontAxle = 1.35f,
            RearAxle = -1.3f,
        };

        // Loose surfaces keep more grip in a slide (slide falloff below 1), so slides on them are progressive.
        private static readonly SurfaceDefinition[] SurfaceDefinitions =
        {
            new SurfaceDefinition("Asphalt", grip: 1f, topSpeed: 1f, acceleration: 1f, rollingResistance: 0f, slideFalloff: 1f,
                SurfaceSpray.TyreSmoke, new Color(0.85f, 0.85f, 0.85f), new Color(0.04f, 0.04f, 0.04f, 0.75f), rollingSpray: 0f, roughness: 0f),
            new SurfaceDefinition("Dirt", grip: 0.85f, topSpeed: 0.95f, acceleration: 0.95f, rollingResistance: 0.4f, slideFalloff: 0.7f,
                SurfaceSpray.Dust, new Color(0.55f, 0.45f, 0.33f), new Color(0.2f, 0.14f, 0.09f, 0.55f), rollingSpray: 0.25f, roughness: 0.35f),
            new SurfaceDefinition("Gravel", grip: 0.72f, topSpeed: 0.92f, acceleration: 0.9f, rollingResistance: 0.6f, slideFalloff: 0.6f,
                SurfaceSpray.Gravel, new Color(0.6f, 0.58f, 0.54f), new Color(0.24f, 0.22f, 0.19f, 0.5f), rollingSpray: 0.15f, roughness: 0.6f),
            new SurfaceDefinition("Mud", grip: 0.55f, topSpeed: 0.78f, acceleration: 0.6f, rollingResistance: 2f, slideFalloff: 0.5f,
                SurfaceSpray.Mud, new Color(0.24f, 0.17f, 0.11f), new Color(0.12f, 0.08f, 0.05f, 0.65f), rollingSpray: 0.1f, roughness: 0.4f),
            new SurfaceDefinition("Grass", grip: 0.45f, topSpeed: 0.7f, acceleration: 0.8f, rollingResistance: 1.5f, slideFalloff: 0.8f,
                SurfaceSpray.Grass, new Color(0.33f, 0.4f, 0.2f), new Color(0.18f, 0.15f, 0.08f, 0.55f), rollingSpray: 0.05f, roughness: 0.5f),
            // Loose desert ground off the road (Red Canyon): like grass, but it drags a little more.
            new SurfaceDefinition("Sand", grip: 0.45f, topSpeed: 0.68f, acceleration: 0.75f, rollingResistance: 1.8f, slideFalloff: 0.6f,
                SurfaceSpray.Dust, new Color(0.83f, 0.66f, 0.46f), new Color(0.55f, 0.4f, 0.28f, 0.45f), rollingSpray: 0.35f, roughness: 0.45f),
        };

        [MenuItem("RALLY X/Build/Create Missing Vehicle Content")]
        public static void BuildMissingFromMenu() => BuildMissing();

        [MenuItem("RALLY X/Build/Rebuild Placeholder Car Prefab")]
        public static void RebuildCarFromMenu()
        {
            if (EditorUtility.DisplayDialog("Rebuild placeholder car",
                    $"Overwrite {CarPrefabPath}? Manual changes to the prefab will be lost.", "Rebuild", "Cancel"))
                RebuildCar();
        }

        /// <summary>Creates missing surfaces and the car, and brings a car prefab built before the current look up to date.</summary>
        public static bool BuildMissing()
        {
            BuildSurfaces();
            bool carOk = BuildCarPrefab(overwrite: false);
            AssetDatabase.SaveAssets();
            return carOk;
        }

        /// <summary>Regenerates the car prefab from its spec (its root and handling config are kept).</summary>
        public static bool RebuildCar()
        {
            bool carOk = BuildCarPrefab(overwrite: true);
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
                serialized.FindProperty("slideFalloff").floatValue = definition.SlideFalloff;
                serialized.FindProperty("spray").enumValueIndex = (int)definition.Spray;
                serialized.FindProperty("sprayColor").colorValue = definition.SprayColor;
                serialized.FindProperty("markColor").colorValue = definition.MarkColor;
                serialized.FindProperty("rollingSpray").floatValue = definition.RollingSpray;
                serialized.FindProperty("roughness").floatValue = definition.Roughness;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static bool BuildCarPrefab(bool overwrite)
        {
            var config = EditorAssetUtility.LoadOrCreate(CarConfigPath, ScriptableObject.CreateInstance<VehicleConfig>);
            return PlaceholderCarBuilder.Build(RallyCarSpec, config, overwrite);
        }

        private readonly struct SurfaceDefinition
        {
            public readonly string Name;
            public readonly float Grip;
            public readonly float TopSpeed;
            public readonly float Acceleration;
            public readonly float RollingResistance;
            public readonly float SlideFalloff;
            public readonly SurfaceSpray Spray;
            public readonly Color SprayColor;
            public readonly Color MarkColor;
            public readonly float RollingSpray;
            public readonly float Roughness;

            public SurfaceDefinition(string name, float grip, float topSpeed, float acceleration, float rollingResistance, float slideFalloff,
                SurfaceSpray spray, Color sprayColor, Color markColor, float rollingSpray, float roughness)
            {
                Name = name;
                Grip = grip;
                TopSpeed = topSpeed;
                Acceleration = acceleration;
                RollingResistance = rollingResistance;
                SlideFalloff = slideFalloff;
                Spray = spray;
                SprayColor = sprayColor;
                MarkColor = markColor;
                RollingSpray = rollingSpray;
                Roughness = roughness;
            }
        }
    }
}
