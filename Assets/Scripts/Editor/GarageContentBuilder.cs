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

        // A short, upright hatch in teal with a white stripe over bonnet and roof, a roof spoiler, silver wheels, yellow
        // calipers and a side-exit exhaust.
        private static readonly PlaceholderCarBuilder.Spec KestrelSpec = new PlaceholderCarBuilder.Spec
        {
            Name = "Kestrel",
            PrefabPath = $"{PrefabsFolder}/Kestrel.prefab",
            Paint = new Color(0.04f, 0.56f, 0.64f),
            Trim = new Color(0.92f, 0.93f, 0.95f),
            Rim = new Color(0.72f, 0.74f, 0.77f),
            Caliper = new Color(0.95f, 0.72f, 0.06f),
            RaceNumber = 21,
            Body = new[]
            {
                new BodyStation(-1.95f, 0.42f, 0.88f, 0.8f, 0.76f, 0.08f),
                new BodyStation(-1.89f, 0.35f, 0.95f, 0.85f, 0.81f, 0.1f),
                new BodyStation(-1.5f, 0.32f, 0.97f, 0.87f, 0.83f, 0.12f),
                new BodyStation(0.7f, 0.32f, 0.96f, 0.87f, 0.83f, 0.12f),
                new BodyStation(1.35f, 0.32f, 0.9f, 0.87f, 0.82f, 0.12f),
                new BodyStation(1.88f, 0.34f, 0.82f, 0.85f, 0.78f, 0.1f),
                new BodyStation(1.95f, 0.4f, 0.76f, 0.78f, 0.72f, 0.08f),
            },
            Cabin = new[]
            {
                new BodyStation(0.72f, 0.94f, 0.98f, 0.8f, 0.78f, 0.02f),
                new BodyStation(0.02f, 0.94f, 1.4f, 0.79f, 0.66f, 0.06f),
                new BodyStation(-1f, 0.94f, 1.41f, 0.79f, 0.67f, 0.06f),
                new BodyStation(-1.62f, 0.94f, 1.02f, 0.8f, 0.74f, 0.03f),
            },
            RoofFront = 0.02f,
            RoofRear = -1f,
            StripeWidth = 0.46f,
            FlareWidth = 0.05f,
            Details = new[]
            {
                new PlaceholderCarBuilder.Part("RoofVent", new Vector3(0f, 1.46f, 0.25f), new Vector3(0.36f, 0.08f, 0.36f), PlaceholderCarBuilder.Finish.Plastic),
                new PlaceholderCarBuilder.Part("Spoiler", new Vector3(0f, 1.4f, -1.08f), new Vector3(1.3f, 0.05f, 0.3f), PlaceholderCarBuilder.Finish.Paint,
                    rotation: new Vector3(-10f, 0f, 0f)),
                new PlaceholderCarBuilder.Part("FogLamp_L", new Vector3(-0.5f, 0.47f, 1.97f), new Vector3(0.12f, 0.12f, 0.03f), PlaceholderCarBuilder.Finish.HeadLight,
                    shape: PlaceholderCarBuilder.Shape.Cylinder),
                new PlaceholderCarBuilder.Part("FogLamp_R", new Vector3(0.5f, 0.47f, 1.97f), new Vector3(0.12f, 0.12f, 0.03f), PlaceholderCarBuilder.Finish.HeadLight,
                    shape: PlaceholderCarBuilder.Shape.Cylinder),
            },
            Exhausts = new[] { new Vector3(-0.5f, 0.36f, -1.97f) },
            Colliders = new[]
            {
                new PlaceholderCarBuilder.ColliderBox("BodyCollider", new Vector3(0f, 0.68f, 0f), new Vector3(1.74f, 0.54f, 3.9f)),
                new PlaceholderCarBuilder.ColliderBox("CabinCollider", new Vector3(0f, 1.17f, -0.2f), new Vector3(1.5f, 0.46f, 1.95f)),
            },
            HalfTrack = 0.8f,
            FrontAxle = 1.22f,
            RearAxle = -1.2f,
            PitchPerG = 1.6f,
            RollPerG = 2.2f,
            LeanFrequency = 3.2f,
        };

        // A long, low Group B coupe in dark red with a black roof, twin black stripes, wide painted flares, a bonnet scoop,
        // a splitter, a big rear wing on posts, gold wheels and twin exhausts.
        private static readonly PlaceholderCarBuilder.Spec TitanSpec = new PlaceholderCarBuilder.Spec
        {
            Name = "Titan",
            PrefabPath = $"{PrefabsFolder}/Titan.prefab",
            Paint = new Color(0.6f, 0.05f, 0.1f),
            Trim = new Color(0.06f, 0.06f, 0.07f),
            Rim = new Color(0.86f, 0.66f, 0.26f),
            Caliper = new Color(0.82f, 0.08f, 0.05f),
            RaceNumber = 1,
            Body = new[]
            {
                new BodyStation(-2.2f, 0.4f, 0.84f, 0.86f, 0.8f, 0.08f),
                new BodyStation(-2.13f, 0.34f, 0.92f, 0.93f, 0.88f, 0.1f),
                new BodyStation(-1.7f, 0.32f, 0.95f, 0.96f, 0.9f, 0.12f),
                new BodyStation(0.5f, 0.31f, 0.93f, 0.96f, 0.9f, 0.12f),
                new BodyStation(1.6f, 0.31f, 0.86f, 0.95f, 0.88f, 0.12f),
                new BodyStation(2.22f, 0.32f, 0.74f, 0.92f, 0.84f, 0.1f),
                new BodyStation(2.3f, 0.38f, 0.68f, 0.84f, 0.78f, 0.07f),
            },
            Cabin = new[]
            {
                new BodyStation(0.52f, 0.9f, 0.94f, 0.84f, 0.82f, 0.02f),
                new BodyStation(-0.2f, 0.9f, 1.3f, 0.83f, 0.68f, 0.06f),
                new BodyStation(-1.05f, 0.9f, 1.31f, 0.83f, 0.69f, 0.06f),
                new BodyStation(-1.75f, 0.9f, 0.98f, 0.86f, 0.8f, 0.03f),
            },
            RoofFront = -0.2f,
            RoofRear = -1.05f,
            TrimRoof = true,
            StripeWidth = 0.16f,
            StripeSpacing = 0.4f,
            FlareWidth = 0.09f,
            PaintedFlares = true,
            Details = new[]
            {
                new PlaceholderCarBuilder.Part("BonnetScoop", new Vector3(0f, 0.93f, 1.1f), new Vector3(0.62f, 0.09f, 0.8f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("Splitter", new Vector3(0f, 0.33f, 2.3f), new Vector3(1.9f, 0.04f, 0.24f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("WingPost_L", new Vector3(-0.55f, 1.03f, -1.95f), new Vector3(0.08f, 0.24f, 0.14f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("WingPost_R", new Vector3(0.55f, 1.03f, -1.95f), new Vector3(0.08f, 0.24f, 0.14f), PlaceholderCarBuilder.Finish.Trim),
                new PlaceholderCarBuilder.Part("Wing", new Vector3(0f, 1.16f, -2f), new Vector3(1.86f, 0.05f, 0.44f), PlaceholderCarBuilder.Finish.Trim,
                    rotation: new Vector3(-6f, 0f, 0f)),
                new PlaceholderCarBuilder.Part("SideIntake_L", new Vector3(-0.935f, 0.74f, -0.95f), new Vector3(0.02f, 0.14f, 0.5f), PlaceholderCarBuilder.Finish.Plastic),
                new PlaceholderCarBuilder.Part("SideIntake_R", new Vector3(0.935f, 0.74f, -0.95f), new Vector3(0.02f, 0.14f, 0.5f), PlaceholderCarBuilder.Finish.Plastic),
            },
            Exhausts = new[] { new Vector3(-0.35f, 0.36f, -2.24f), new Vector3(0.35f, 0.36f, -2.24f) },
            Colliders = new[]
            {
                new PlaceholderCarBuilder.ColliderBox("BodyCollider", new Vector3(0f, 0.66f, 0.05f), new Vector3(1.92f, 0.5f, 4.5f)),
                new PlaceholderCarBuilder.ColliderBox("CabinCollider", new Vector3(0f, 1.1f, -0.45f), new Vector3(1.54f, 0.42f, 2f)),
            },
            HalfTrack = 0.86f,
            FrontAxle = 1.45f,
            RearAxle = -1.4f,
            PitchPerG = 2.8f,
            RollPerG = 3.4f,
            LeanFrequency = 2.2f,
        };

        // Garage order. The first car is the default. Handling changes are relative to the RallyCar config.
        private static readonly CarDefinition[] Cars =
        {
            new CarDefinition("falcon-rx", "Falcon RX", "Group R4  |  All-rounder",
                "The car you learned on. Balanced, predictable and quick on every surface, with no nasty habits.",
                winsToUnlock: 0, spec: null, configName: null),
            // Front-biased drive, stiff springs and little weight transfer: it turns in sharply and is hard to unsettle,
            // but its drifts want the handbrake more than the throttle.
            new CarDefinition("kestrel-s", "Kestrel S", "Group R2  |  Lightweight hatch",
                "Light, eager and glued to the road. It darts into corners and stops on a coin, but runs out of breath on the long straights.",
                winsToUnlock: 0, spec: KestrelSpec, configName: "Kestrel_Config",
                Curve((0f, 0.72f, 0.7333f), (0.15f, 0.83f, 0.5f), (0.42f, 0.93f, 0f), (0.64f, 0.8f, -1.1667f), (0.84f, 0.44f, -2.0968f),
                    (0.95f, 0.15f, -2.75f), (1f, 0f, 0f)),
                ("mass", 1080f), ("maxSpeedKmh", 150f), ("acceleration", 12f), ("maxSteerAngle", 35f), ("steerSpeed", 240f),
                ("tyreFriction", 1.4f), ("rearGrip", 0.97f), ("brakeDeceleration", 20f), ("frontDriveShare", 0.6f), ("brakeBias", 0.6f),
                ("weightTransfer", 0.12f), ("springFrequency", 2f), ("dampingRatio", 0.6f), ("reboundDamping", 1.4f),
                ("driftSustainGrip", 0.72f), ("driftMultiplier", 2.8f), ("airYawRate", 1.4f)),

            // Rear-biased drive, soft springs and a lot of weight transfer: a turbo surge mid-range, a tail that steps out
            // when braking into corners, and drifts that hold on the throttle.
            new CarDefinition("titan-gt", "Titan GT", "Group B  |  Heavy coupe",
                "A brute with the highest top speed in the garage. Heavy and slow to turn in: brake early and keep it pointed straight.",
                winsToUnlock: 1, spec: TitanSpec, configName: "Titan_Config",
                Curve((0f, 0.74f, 0.7143f), (0.14f, 0.84f, 0.875f), (0.32f, 1.02f, 0f), (0.5f, 0.9f, -1.2381f), (0.74f, 0.5f, -1.7143f),
                    (0.92f, 0.18f, -1.9231f), (1f, 0f, 0f)),
                ("mass", 1420f), ("maxSpeedKmh", 185f), ("acceleration", 10.5f), ("maxSteerAngle", 28f), ("steerSpeed", 170f),
                ("tyreFriction", 1.2f), ("rearGrip", 0.93f), ("brakeDeceleration", 16f), ("frontDriveShare", 0.25f), ("brakeBias", 0.52f),
                ("weightTransfer", 0.28f), ("springFrequency", 1.65f), ("reboundDamping", 1.6f), ("driftSustainGrip", 0.58f),
                ("driftMultiplier", 2.2f), ("airYawRate", 1f), ("settleDamping", 4f)),
        };

        /// <summary>An acceleration curve from (time, value, slope) keys.</summary>
        private static AnimationCurve Curve(params (float time, float value, float slope)[] keys)
        {
            var frames = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                frames[i] = new Keyframe(keys[i].time, keys[i].value, keys[i].slope, keys[i].slope);
            return new AnimationCurve(frames);
        }

        [MenuItem("RALLY X/Build/Create Missing Garage Content")]
        public static void BuildMissingFromMenu() => BuildMissing();

        [MenuItem("RALLY X/Build/Rebuild All Car Prefabs")]
        public static void RebuildCarsFromMenu()
        {
            if (EditorUtility.DisplayDialog("Rebuild car prefabs", "Regenerate every garage car's prefab? Manual changes to them will be lost " +
                                                                   "(handling configs are kept).", "Rebuild", "Cancel"))
                RebuildCars();
        }

        /// <summary>Regenerates every car prefab from its spec. Handling configs, definitions and the roster are kept.</summary>
        public static bool RebuildCars()
        {
            if (!VehicleContentBuilder.RebuildCar())
                return false;

            var baseConfig = AssetDatabase.LoadAssetAtPath<VehicleConfig>(VehicleContentBuilder.CarConfigPath);
            foreach (var car in Cars)
            {
                if (car.Spec != null && BuildCar(car, baseConfig, overwrite: true) == null)
                    return false;
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Creates whatever is missing, and brings car prefabs built before the current look up to date. Needs the vehicle
        /// content (RallyCar prefab and config) first.
        /// </summary>
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
                var prefab = car.Spec == null ? baseCar : BuildCar(car, baseConfig, overwrite: false);
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

        private static VehicleController BuildCar(CarDefinition car, VehicleConfig baseConfig, bool overwrite)
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
                if (car.AccelerationCurve != null)
                    serialized.FindProperty("accelerationCurve").animationCurveValue = car.AccelerationCurve;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log(LogPrefix + $"Created {configPath}.");
            }

            if (!PlaceholderCarBuilder.Build(car.Spec, config, overwrite))
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
            public readonly AnimationCurve AccelerationCurve;
            public readonly (string property, float value)[] Handling;

            public CarDefinition(string id, string displayName, string category, string description, int winsToUnlock,
                PlaceholderCarBuilder.Spec spec, string configName, AnimationCurve accelerationCurve = null,
                params (string property, float value)[] handling)
            {
                Id = id;
                DisplayName = displayName;
                Category = category;
                Description = description;
                WinsToUnlock = winsToUnlock;
                Spec = spec;
                ConfigName = configName;
                AccelerationCurve = accelerationCurve;
                Handling = handling;
            }
        }
    }
}
