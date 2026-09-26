using RallyX.Core;
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

        private const string LogPrefix = "[RALLY X Build] ";
        private const string CarConfigPath = "Assets/Settings/Vehicles/RallyCar_Config.asset";
        private const string BodyPhysicsMaterialPath = "Assets/Settings/Physics/VehicleBody.physicsMaterial";
        private const string CarMaterialsFolder = "Assets/Materials/Vehicles";
        private const float TyreWidth = 0.26f;

        private static readonly SurfaceDefinition[] SurfaceDefinitions =
        {
            new SurfaceDefinition("Asphalt", grip: 1f, topSpeed: 1f, acceleration: 1f, rollingResistance: 0f),
            new SurfaceDefinition("Dirt", grip: 0.85f, topSpeed: 0.95f, acceleration: 0.95f, rollingResistance: 0.4f),
            new SurfaceDefinition("Gravel", grip: 0.72f, topSpeed: 0.92f, acceleration: 0.9f, rollingResistance: 0.6f),
            new SurfaceDefinition("Mud", grip: 0.55f, topSpeed: 0.78f, acceleration: 0.6f, rollingResistance: 2f),
            new SurfaceDefinition("Grass", grip: 0.45f, topSpeed: 0.7f, acceleration: 0.8f, rollingResistance: 1.5f),
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
            if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefabPath) != null)
                return true;

            int vehicleLayer = LayerMask.NameToLayer(GameLayers.Vehicle);
            if (vehicleLayer < 0)
            {
                Debug.LogError(LogPrefix + $"Layer '{GameLayers.Vehicle}' is missing. Run RALLY X > Setup > Run Project Setup first.");
                return false;
            }

            var config = EditorAssetUtility.LoadOrCreate(CarConfigPath, ScriptableObject.CreateInstance<VehicleConfig>);
            var bodyPhysics = EditorAssetUtility.LoadOrCreate(BodyPhysicsMaterialPath, CreateBodyPhysicsMaterial);
            var bodyMaterial = EditorAssetUtility.LoadOrCreateLitMaterial($"{CarMaterialsFolder}/RallyCar_Body.mat", new Color(0.85f, 0.28f, 0.08f), 0.55f);
            var cabinMaterial = EditorAssetUtility.LoadOrCreateLitMaterial($"{CarMaterialsFolder}/RallyCar_Cabin.mat", new Color(0.08f, 0.09f, 0.11f), 0.8f);
            var tyreMaterial = EditorAssetUtility.LoadOrCreateLitMaterial($"{CarMaterialsFolder}/RallyCar_Tyre.mat", new Color(0.05f, 0.05f, 0.05f), 0.1f);
            var lightMaterial = EditorAssetUtility.LoadOrCreateLitMaterial($"{CarMaterialsFolder}/RallyCar_Light.mat", new Color(1f, 0.95f, 0.7f), 0.6f);

            var root = new GameObject("RallyCar");
            try
            {
                root.AddComponent<Rigidbody>();
                var controller = root.AddComponent<VehicleController>();

                CreateBox("Body", root.transform, new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 0.55f, 4.2f), bodyMaterial, bodyPhysics);
                CreateBox("Cabin", root.transform, new Vector3(0f, 1.2f, -0.25f), new Vector3(1.5f, 0.45f, 2.1f), cabinMaterial, bodyPhysics);
                CreateBox("Headlight_L", root.transform, new Vector3(-0.6f, 0.75f, 2.11f), new Vector3(0.35f, 0.15f, 0.04f), lightMaterial, null);
                CreateBox("Headlight_R", root.transform, new Vector3(0.6f, 0.75f, 2.11f), new Vector3(0.35f, 0.15f, 0.04f), lightMaterial, null);

                // Place the suspension mounts so the tyres just touch the ground at static sag with the root at ground level.
                float angularFrequency = 2f * Mathf.PI * config.SpringFrequency;
                float staticSag = Physics.gravity.magnitude / (angularFrequency * angularFrequency);
                float restSpringLength = config.SuspensionTravel - staticSag;
                float mountHeight = config.WheelRadius + restSpringLength;

                var wheels = new[]
                {
                    CreateWheel("Wheel_FL", root.transform, new Vector3(-0.82f, mountHeight, 1.35f), true, config.WheelRadius, restSpringLength, tyreMaterial),
                    CreateWheel("Wheel_FR", root.transform, new Vector3(0.82f, mountHeight, 1.35f), true, config.WheelRadius, restSpringLength, tyreMaterial),
                    CreateWheel("Wheel_RL", root.transform, new Vector3(-0.82f, mountHeight, -1.3f), false, config.WheelRadius, restSpringLength, tyreMaterial),
                    CreateWheel("Wheel_RR", root.transform, new Vector3(0.82f, mountHeight, -1.3f), false, config.WheelRadius, restSpringLength, tyreMaterial),
                };

                var serializedController = new SerializedObject(controller);
                serializedController.FindProperty("config").objectReferenceValue = config;
                serializedController.FindProperty("fallbackSurface").objectReferenceValue = LoadSurface("Asphalt");
                var wheelsProperty = serializedController.FindProperty("wheels");
                wheelsProperty.arraySize = wheels.Length;
                for (int i = 0; i < wheels.Length; i++)
                    wheelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                AddLivery(root);
                EditorAssetUtility.SetLayerRecursively(root, vehicleLayer);
                EditorAssetUtility.EnsureFolder("Assets/Prefabs/Vehicles");
                PrefabUtility.SaveAsPrefabAsset(root, CarPrefabPath, out bool saved);
                if (!saved)
                {
                    Debug.LogError(LogPrefix + $"Failed to save {CarPrefabPath}.");
                    return false;
                }

                Debug.Log(LogPrefix + $"Built {CarPrefabPath}.");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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

        private static PhysicsMaterial CreateBodyPhysicsMaterial()
        {
            // Low friction lets cars scrape along barriers instead of snagging on them.
            return new PhysicsMaterial("VehicleBody")
            {
                dynamicFriction = 0.1f,
                staticFriction = 0.1f,
                bounciness = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Average,
            };
        }

        /// <summary>Box primitive. Pass a null physics material for a visual-only box without a collider.</summary>
        private static void CreateBox(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material,
            PhysicsMaterial physicsMaterial)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;

            var collider = box.GetComponent<BoxCollider>();
            if (physicsMaterial == null)
                Object.DestroyImmediate(collider);
            else
                collider.sharedMaterial = physicsMaterial;
        }

        private static VehicleWheel CreateWheel(string name, Transform parent, Vector3 localPosition, bool isFront, float radius,
            float restSpringLength, Material tyreMaterial)
        {
            var mount = new GameObject(name);
            mount.transform.SetParent(parent, false);
            mount.transform.localPosition = localPosition;
            var wheel = mount.AddComponent<VehicleWheel>();

            var visual = new GameObject("Visual").transform;
            visual.SetParent(mount.transform, false);
            visual.localPosition = new Vector3(0f, -restSpringLength, 0f);

            // The cylinder primitive's axis is Y and it is 2 units tall; turn it so the axle runs along X.
            var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tyre.name = "Tyre";
            Object.DestroyImmediate(tyre.GetComponent<Collider>());
            tyre.transform.SetParent(visual, false);
            tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tyre.transform.localScale = new Vector3(radius * 2f, TyreWidth * 0.5f, radius * 2f);
            tyre.GetComponent<MeshRenderer>().sharedMaterial = tyreMaterial;

            var serializedWheel = new SerializedObject(wheel);
            serializedWheel.FindProperty("isFront").boolValue = isFront;
            serializedWheel.FindProperty("visual").objectReferenceValue = visual;
            serializedWheel.ApplyModifiedPropertiesWithoutUndo();
            return wheel;
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
