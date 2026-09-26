using RallyX.Garage;
using RallyX.Vehicle;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Creates the garage's cars if they are missing: a handling config and placeholder prefab for each car beyond the
    /// first (which is the vehicle content builder's RallyCar), a VehicleDefinition for each, and the VehicleRoster.
    /// Existing assets are never overwritten, so tuning done in the Editor is kept.
    /// </summary>
    public static class GarageContentBuilder
    {
        public const string RosterPath = "Assets/Settings/Vehicles/VehicleRoster.asset";

        private const string LogPrefix = "[RALLY X Build] ";
        private const string DefinitionsFolder = "Assets/Settings/Vehicles/Definitions";
        private const string ConfigsFolder = "Assets/Settings/Vehicles";
        private const string PrefabsFolder = "Assets/Prefabs/Vehicles";

        private static readonly PlaceholderCarBuilder.Spec KestrelSpec = new PlaceholderCarBuilder.Spec
        {
            Name = "Kestrel",
            PrefabPath = $"{PrefabsFolder}/Kestrel.prefab",
            Paint = new Color(0.04f, 0.56f, 0.64f),
            Trim = new Color(0.92f, 0.93f, 0.95f),
            Parts = new[]
            {
                new PlaceholderCarBuilder.Part("Body", new Vector3(0f, 0.68f, 0f), new Vector3(1.74f, 0.54f, 3.9f), PlaceholderCarBuilder.Finish.Paint, collides: true),
                new PlaceholderCarBuilder.Part("Cabin", new Vector3(0f, 1.17f, -0.2f), new Vector3(1.5f, 0.46f, 1.95f), PlaceholderCarBuilder.Finish.Cabin, collides: true),
                new PlaceholderCarBuilder.Part("Stripe_Bonnet", new Vector3(0f, 0.956f, 1.35f), new Vector3(0.5f, 0.012f, 1.2f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("Stripe_Roof", new Vector3(0f, 1.406f, -0.2f), new Vector3(0.5f, 0.012f, 1.95f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("RoofVent", new Vector3(0f, 1.45f, 0.45f), new Vector3(0.36f, 0.09f, 0.36f), PlaceholderCarBuilder.Finish.Cabin),
                new PlaceholderCarBuilder.Part("Spoiler", new Vector3(0f, 1.42f, -1.22f), new Vector3(1.36f, 0.05f, 0.3f), PlaceholderCarBuilder.Finish.Paint),
                new PlaceholderCarBuilder.Part("Headlight_L", new Vector3(-0.56f, 0.74f, 1.955f), new Vector3(0.32f, 0.14f, 0.04f), PlaceholderCarBuilder.Finish.Light),
                new PlaceholderCarBuilder.Part("Headlight_R", new Vector3(0.56f, 0.74f, 1.955f), new Vector3(0.32f, 0.14f, 0.04f), PlaceholderCarBuilder.Finish.Light),
            },
            HalfTrack = 0.8f,
            FrontAxle = 1.22f,
            RearAxle = -1.2f,
        };

        private static readonly PlaceholderCarBuilder.Spec TitanSpec = new PlaceholderCarBuilder.Spec
        {
            Name = "Titan",
            PrefabPath = $"{PrefabsFolder}/Titan.prefab",
            Paint = new Color(0.6f, 0.05f, 0.1f),
            Trim = new Color(0.06f, 0.06f, 0.07f),
            Parts = new[]
            {
                new PlaceholderCarBuilder.Part("Body", new Vector3(0f, 0.66f, 0.05f), new Vector3(1.92f, 0.5f, 4.5f), PlaceholderCarBuilder.Finish.Paint, collides: true),
                new PlaceholderCarBuilder.Part("Cabin", new Vector3(0f, 1.1f, -0.45f), new Vector3(1.54f, 0.42f, 2f), PlaceholderCarBuilder.Finish.Cabin, collides: true),
                new PlaceholderCarBuilder.Part("BonnetScoop", new Vector3(0f, 0.95f, 1.2f), new Vector3(0.62f, 0.1f, 0.8f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("Stripe_L", new Vector3(-0.2f, 0.912f, 0.05f), new Vector3(0.16f, 0.012f, 4.5f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("Stripe_R", new Vector3(0.2f, 0.912f, 0.05f), new Vector3(0.16f, 0.012f, 4.5f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("Splitter", new Vector3(0f, 0.43f, 2.3f), new Vector3(1.9f, 0.05f, 0.2f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("WingPost_L", new Vector3(-0.55f, 1.03f, -1.95f), new Vector3(0.08f, 0.24f, 0.14f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("WingPost_R", new Vector3(0.55f, 1.03f, -1.95f), new Vector3(0.08f, 0.24f, 0.14f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("Wing", new Vector3(0f, 1.17f, -2f), new Vector3(1.86f, 0.06f, 0.44f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("Headlight_L", new Vector3(-0.64f, 0.72f, 2.305f), new Vector3(0.38f, 0.13f, 0.04f), PlaceholderCarBuilder.Finish.Light),
                new PlaceholderCarBuilder.Part("Headlight_R", new Vector3(0.64f, 0.72f, 2.305f), new Vector3(0.38f, 0.13f, 0.04f), PlaceholderCarBuilder.Finish.Light),
            },
            HalfTrack = 0.86f,
            FrontAxle = 1.45f,
            RearAxle = -1.4f,
        };

        // Garage order. The first car is the default. Handling changes are relative to the RallyCar config.
        private static readonly CarDefinition[] Cars =
        {
            new CarDefinition("falcon-rx", "Falcon RX", "Group R4  |  All-rounder",
                "The car you learned on. Balanced, predictable and quick on every surface, with no nasty habits.",
                winsToUnlock: 0, spec: null, configName: null),
            new CarDefinition("kestrel-s", "Kestrel S", "Group R2  |  Lightweight hatch",
                "Light, eager and glued to the road. It darts into corners and stops on a coin, but runs out of breath on the long straights.",
                winsToUnlock: 0, spec: KestrelSpec, configName: "Kestrel_Config",
                ("mass", 1080f), ("maxSpeedKmh", 150f), ("acceleration", 12f), ("maxSteerAngle", 35f), ("steerSpeed", 240f),
                ("tyreFriction", 1.4f), ("rearGrip", 0.97f), ("brakeDeceleration", 20f)),
            new CarDefinition("titan-gt", "Titan GT", "Group B  |  Heavy coupe",
                "A brute with the highest top speed in the garage. Heavy and slow to turn in: brake early and keep it pointed straight.",
                winsToUnlock: 1, spec: TitanSpec, configName: "Titan_Config",
                ("mass", 1420f), ("maxSpeedKmh", 185f), ("acceleration", 10.5f), ("maxSteerAngle", 28f), ("steerSpeed", 170f),
                ("tyreFriction", 1.2f), ("rearGrip", 0.93f), ("brakeDeceleration", 16f)),
        };

        [MenuItem("RALLY X/Build/Create Missing Garage Content")]
        public static void BuildMissingFromMenu() => BuildMissing();

        /// <summary>Creates whatever is missing. Needs the vehicle content (RallyCar prefab and config) first.</summary>
        public static bool BuildMissing()
        {
            var baseConfig = AssetDatabase.LoadAssetAtPath<VehicleConfig>(VehicleContentBuilder.CarConfigPath);
            var baseCar = AssetDatabase.LoadAssetAtPath<VehicleController>(VehicleContentBuilder.CarPrefabPath);
            if (baseConfig == null || baseCar == null)
            {
                Debug.LogError(LogPrefix + "Vehicle content is missing. Run RALLY X > Build > Create Missing Vehicle Content first.");
                return false;
            }

            var definitions = new VehicleDefinition[Cars.Length];
            for (int i = 0; i < Cars.Length; i++)
            {
                var car = Cars[i];
                var prefab = car.Spec == null ? baseCar : BuildCar(car, baseConfig);
                if (prefab == null)
                    return false;

                definitions[i] = EditorAssetUtility.LoadOrCreate($"{DefinitionsFolder}/Vehicle_{car.DisplayName.Replace(" ", string.Empty)}.asset",
                    () => VehicleDefinition.Create(car.Id, car.DisplayName, car.Category, car.Description, prefab, car.WinsToUnlock));
            }

            EditorAssetUtility.LoadOrCreate(RosterPath, () =>
            {
                var roster = ScriptableObject.CreateInstance<VehicleRoster>();
                roster.SetVehicles(definitions);
                return roster;
            });

            AssetDatabase.SaveAssets();
            string problem = LoadRoster().FindProblem();
            if (problem != null)
            {
                Debug.LogError(LogPrefix + $"{RosterPath} is not usable: {problem}.");
                return false;
            }

            return true;
        }

        public static VehicleRoster LoadRoster() => AssetDatabase.LoadAssetAtPath<VehicleRoster>(RosterPath);

        private static VehicleController BuildCar(CarDefinition car, VehicleConfig baseConfig)
        {
            string configPath = $"{ConfigsFolder}/{car.ConfigName}.asset";
            var config = AssetDatabase.LoadAssetAtPath<VehicleConfig>(configPath);
            if (config == null)
            {
                // Start from the first car's handling and change what makes this car different.
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(baseConfig), configPath))
                {
                    Debug.LogError(LogPrefix + $"Failed to create {configPath}.");
                    return null;
                }

                config = AssetDatabase.LoadAssetAtPath<VehicleConfig>(configPath);
                var serialized = new SerializedObject(config);
                foreach (var (property, value) in car.Handling)
                    serialized.FindProperty(property).floatValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log(LogPrefix + $"Created {configPath}.");
            }

            if (!PlaceholderCarBuilder.Build(car.Spec, config, overwrite: false))
                return null;
            return AssetDatabase.LoadAssetAtPath<VehicleController>(car.Spec.PrefabPath);
        }

        private readonly struct CarDefinition
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string Category;
            public readonly string Description;
            public readonly int WinsToUnlock;
            public readonly PlaceholderCarBuilder.Spec Spec;
            public readonly string ConfigName;
            public readonly (string property, float value)[] Handling;

            public CarDefinition(string id, string displayName, string category, string description, int winsToUnlock,
                PlaceholderCarBuilder.Spec spec, string configName, params (string property, float value)[] handling)
            {
                Id = id;
                DisplayName = displayName;
                Category = category;
                Description = description;
                WinsToUnlock = winsToUnlock;
                Spec = spec;
                ConfigName = configName;
                Handling = handling;
            }
        }
    }
}
