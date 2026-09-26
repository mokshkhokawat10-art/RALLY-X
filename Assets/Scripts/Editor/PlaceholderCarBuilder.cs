using RallyX.Core;
using RallyX.Vehicle;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Builds a placeholder car prefab from primitives until real models exist: box parts (the body and cabin collide,
    /// the rest is trim), four suspension wheels placed from the car's <see cref="VehicleConfig"/>, the controller
    /// wired up, and paint on the painted parts. Each car gets its own materials, named after it.
    /// </summary>
    internal static class PlaceholderCarBuilder
    {
        public const string MaterialsFolder = "Assets/Materials/Vehicles";

        private const string LogPrefix = "[RALLY X Build] ";
        private const string BodyPhysicsMaterialPath = "Assets/Settings/Physics/VehicleBody.physicsMaterial";
        private const float TyreWidth = 0.26f;

        private static readonly Color CabinColor = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Color TyreColor = new Color(0.05f, 0.05f, 0.05f);
        private static readonly Color LightColor = new Color(1f, 0.95f, 0.7f);

        public enum Finish
        {
            Paint,
            Cabin,
            Light,
            Trim,
        }

        public readonly struct Part
        {
            public readonly string Name;
            public readonly Vector3 Position;
            public readonly Vector3 Size;
            public readonly Finish Finish;
            public readonly bool Collides;

            public Part(string name, Vector3 position, Vector3 size, Finish finish, bool collides = false)
            {
                Name = name;
                Position = position;
                Size = size;
                Finish = finish;
                Collides = collides;
            }
        }

        public sealed class Spec
        {
            /// <summary>Root object name and material name prefix.</summary>
            public string Name;
            public string PrefabPath;
            public Color Paint;
            public Color Trim = Color.white;
            public Part[] Parts;

            /// <summary>Wheel centres: half the track width, and the front and rear axles along the car, metres.</summary>
            public float HalfTrack;
            public float FrontAxle;
            public float RearAxle;
        }

        /// <summary>Builds the prefab. Returns false (and logs why) if it couldn't be saved.</summary>
        public static bool Build(Spec spec, VehicleConfig config, bool overwrite)
        {
            if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) != null)
                return true;

            int vehicleLayer = LayerMask.NameToLayer(GameLayers.Vehicle);
            if (vehicleLayer < 0)
            {
                Debug.LogError(LogPrefix + $"Layer '{GameLayers.Vehicle}' is missing. Run RALLY X > Setup > Run Project Setup first.");
                return false;
            }

            var bodyPhysics = EditorAssetUtility.LoadOrCreate(BodyPhysicsMaterialPath, CreateBodyPhysicsMaterial);
            var paint = EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialsFolder}/{spec.Name}_Body.mat", spec.Paint, 0.55f);
            var cabin = EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialsFolder}/{spec.Name}_Cabin.mat", CabinColor, 0.8f);
            var tyre = EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialsFolder}/{spec.Name}_Tyre.mat", TyreColor, 0.1f);
            var light = EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialsFolder}/{spec.Name}_Light.mat", LightColor, 0.6f);
            bool hasTrim = System.Array.Exists(spec.Parts, part => part.Finish == Finish.Trim);
            var trim = hasTrim ? EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialsFolder}/{spec.Name}_Trim.mat", spec.Trim, 0.5f) : null;

            var root = new GameObject(spec.Name);
            try
            {
                root.AddComponent<Rigidbody>();
                var controller = root.AddComponent<VehicleController>();

                var painted = new System.Collections.Generic.List<Renderer>();
                foreach (var part in spec.Parts)
                {
                    var material = part.Finish switch
                    {
                        Finish.Paint => paint,
                        Finish.Cabin => cabin,
                        Finish.Light => light,
                        _ => trim,
                    };
                    var renderer = CreateBox(part.Name, root.transform, part.Position, part.Size, material, part.Collides ? bodyPhysics : null);
                    if (part.Finish == Finish.Paint)
                        painted.Add(renderer);
                }

                // Place the suspension mounts so the tyres just touch the ground at static sag with the root at ground level.
                float angularFrequency = 2f * Mathf.PI * config.SpringFrequency;
                float staticSag = Physics.gravity.magnitude / (angularFrequency * angularFrequency);
                float restSpringLength = config.SuspensionTravel - staticSag;
                float mountHeight = config.WheelRadius + restSpringLength;

                var wheels = new[]
                {
                    CreateWheel("Wheel_FL", root.transform, new Vector3(-spec.HalfTrack, mountHeight, spec.FrontAxle), true, config.WheelRadius, restSpringLength, tyre),
                    CreateWheel("Wheel_FR", root.transform, new Vector3(spec.HalfTrack, mountHeight, spec.FrontAxle), true, config.WheelRadius, restSpringLength, tyre),
                    CreateWheel("Wheel_RL", root.transform, new Vector3(-spec.HalfTrack, mountHeight, spec.RearAxle), false, config.WheelRadius, restSpringLength, tyre),
                    CreateWheel("Wheel_RR", root.transform, new Vector3(spec.HalfTrack, mountHeight, spec.RearAxle), false, config.WheelRadius, restSpringLength, tyre),
                };

                var serializedController = new SerializedObject(controller);
                serializedController.FindProperty("config").objectReferenceValue = config;
                serializedController.FindProperty("fallbackSurface").objectReferenceValue = VehicleContentBuilder.LoadSurface("Asphalt");
                var wheelsProperty = serializedController.FindProperty("wheels");
                wheelsProperty.arraySize = wheels.Length;
                for (int i = 0; i < wheels.Length; i++)
                    wheelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                root.AddComponent<VehicleLivery>().SetPaintedRenderers(painted.ToArray());
                EditorAssetUtility.SetLayerRecursively(root, vehicleLayer);
                EditorAssetUtility.EnsureFolder(System.IO.Path.GetDirectoryName(spec.PrefabPath)?.Replace('\\', '/'));
                PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath, out bool saved);
                if (!saved)
                {
                    Debug.LogError(LogPrefix + $"Failed to save {spec.PrefabPath}.");
                    return false;
                }

                Debug.Log(LogPrefix + $"Built {spec.PrefabPath}.");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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
        private static Renderer CreateBox(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material,
            PhysicsMaterial physicsMaterial)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            var renderer = box.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            var collider = box.GetComponent<BoxCollider>();
            if (physicsMaterial == null)
                Object.DestroyImmediate(collider);
            else
                collider.sharedMaterial = physicsMaterial;
            return renderer;
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
    }
}
