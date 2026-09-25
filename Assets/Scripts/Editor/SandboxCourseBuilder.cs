using RallyX.Core;
using RallyX.Player;
using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Rebuilds the vehicle test course in the Sandbox scene: surface pads, jumps, bumps,
    /// barriers, crates and the player car. Everything it generates lives under two roots
    /// (TestCourse, PlayerCar) that are replaced on each rebuild.
    /// </summary>
    public static class SandboxCourseBuilder
    {
        private const string LogPrefix = "[RALLY X Build] ";
        private const string CourseRootName = "TestCourse";
        private const string PlayerCarName = "PlayerCar";
        private const string GroundName = "Ground";
        private const string CameraName = "Main Camera";
        private const string MaterialsFolder = "Assets/Materials/Dev";

        // Pads float just above the ground plane so wheel raycasts hit them first.
        private const float PadHeight = 0.01f;
        private const float CrateMass = 40f;

        private static readonly Vector3 CarSpawnPosition = new Vector3(0f, 0.3f, -85f);

        [MenuItem("RALLY X/Build/Rebuild Sandbox Test Course")]
        public static void RebuildFromMenu() => Rebuild();

        public static bool Rebuild()
        {
            var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleContentBuilder.CarPrefabPath);
            if (carPrefab == null)
            {
                Debug.LogError(LogPrefix + "Car prefab missing. Run RALLY X > Build > Create Missing Vehicle Content first.");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogError(LogPrefix + "Sandbox rebuild cancelled: unsaved changes in the open scene were not saved.");
                return false;
            }

            var scene = EditorSceneManager.OpenScene(ProjectSetup.SandboxScenePath, OpenSceneMode.Single);
            DestroyRoot(scene, CourseRootName);
            DestroyRoot(scene, PlayerCarName);

            var materials = new CourseMaterials();
            var course = new GameObject(CourseRootName).transform;
            ConfigureGround(scene, materials.Dirt, VehicleContentBuilder.LoadSurface("Dirt"));
            BuildSurfacePads(course, materials);
            BuildJumps(course, materials.Ramp, VehicleContentBuilder.LoadSurface("Asphalt"));
            BuildSpeedBumps(course, materials.Ramp, VehicleContentBuilder.LoadSurface("Asphalt"));
            BuildObstacles(course, materials);

            var car = SpawnPlayerCar(carPrefab, scene);
            ConfigureCamera(scene, car.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError(LogPrefix + $"Failed to save {ProjectSetup.SandboxScenePath}.");
                return false;
            }

            Debug.Log(LogPrefix + "Rebuilt the Sandbox test course.");
            return true;
        }

        private static void ConfigureGround(Scene scene, Material material, SurfaceType surface)
        {
            var ground = FindRoot(scene, GroundName);
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = GroundName;
                ground.transform.localScale = new Vector3(20f, 1f, 20f);
            }

            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!ground.TryGetComponent(out SurfaceMarker marker))
                marker = ground.AddComponent<SurfaceMarker>();
            marker.Surface = surface;
        }

        private static void BuildSurfacePads(Transform course, CourseMaterials materials)
        {
            var pads = new GameObject("SurfacePads").transform;
            pads.SetParent(course, false);

            CreatePad("Asphalt_Straight", pads, new Vector2(0f, -5f), new Vector2(14f, 180f), materials.Asphalt, VehicleContentBuilder.LoadSurface("Asphalt"));
            CreatePad("Gravel", pads, new Vector2(45f, -55f), new Vector2(30f, 35f), materials.Gravel, VehicleContentBuilder.LoadSurface("Gravel"));
            CreatePad("Mud", pads, new Vector2(45f, -10f), new Vector2(30f, 35f), materials.Mud, VehicleContentBuilder.LoadSurface("Mud"));
            CreatePad("Grass", pads, new Vector2(45f, 35f), new Vector2(30f, 35f), materials.Grass, VehicleContentBuilder.LoadSurface("Grass"));
        }

        private static void BuildJumps(Transform course, Material material, SurfaceType surface)
        {
            var jumps = new GameObject("Jumps").transform;
            jumps.SetParent(course, false);

            CreateRamp("Ramp_Small", jumps, new Vector2(-25f, -50f), width: 8f, length: 6f, angle: 12f, material, surface);
            CreateRamp("Ramp_Big", jumps, new Vector2(-25f, 10f), width: 10f, length: 12f, angle: 18f, material, surface);
        }

        private static void BuildSpeedBumps(Transform course, Material material, SurfaceType surface)
        {
            var bumps = new GameObject("SpeedBumps").transform;
            bumps.SetParent(course, false);

            for (int i = 0; i < 6; i++)
            {
                var bump = CreateBlock($"Bump_{i + 1}", bumps, new Vector3(20f, 0.1f, -70f + i * 8f), Quaternion.identity,
                    new Vector3(8f, 0.2f, 1.2f), material);
                bump.AddComponent<SurfaceMarker>().Surface = surface;
            }
        }

        private static void BuildObstacles(Transform course, CourseMaterials materials)
        {
            var obstacles = new GameObject("Obstacles").transform;
            obstacles.SetParent(course, false);

            CreateBlock("Barrier_Wall", obstacles, new Vector3(-70f, 0.75f, 0f), Quaternion.identity, new Vector3(1f, 1.5f, 140f), materials.Barrier);
            CreateBlock("Block_1", obstacles, new Vector3(-50f, 1f, -60f), Quaternion.identity, Vector3.one * 2f, materials.Block);
            CreateBlock("Block_2", obstacles, new Vector3(-55f, 1f, -40f), Quaternion.Euler(0f, 30f, 0f), Vector3.one * 2f, materials.Block);
            CreateBlock("Block_3", obstacles, new Vector3(-48f, 1f, -20f), Quaternion.Euler(0f, 60f, 0f), Vector3.one * 2f, materials.Block);

            for (int i = 0; i < 4; i++)
            {
                var crate = CreateBlock($"Crate_{i + 1}", obstacles, new Vector3(-50f, 0.5f, 45f + i * 1.3f), Quaternion.identity, Vector3.one, materials.Crate);
                crate.AddComponent<Rigidbody>().mass = CrateMass;
            }
        }

        private static GameObject SpawnPlayerCar(GameObject carPrefab, Scene scene)
        {
            var car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab, scene);
            car.name = PlayerCarName;
            car.transform.SetPositionAndRotation(CarSpawnPosition, Quaternion.identity);
            PrefabUtility.RecordPrefabInstancePropertyModifications(car.transform);
            car.AddComponent<PlayerVehicleInput>();
            car.AddComponent<VehicleDebugPanel>();
            return car;
        }

        private static void ConfigureCamera(Scene scene, Transform target)
        {
            var cameraObject = FindRoot(scene, CameraName);
            if (cameraObject == null)
            {
                Debug.LogWarning(LogPrefix + $"No '{CameraName}' in the Sandbox; the car will not be followed.");
                return;
            }

            if (!cameraObject.TryGetComponent(out DevFollowCamera follow))
                follow = cameraObject.AddComponent<DevFollowCamera>();
            follow.Target = target;
            cameraObject.transform.SetPositionAndRotation(target.position + new Vector3(0f, 2.8f, -7.5f), Quaternion.Euler(12f, 0f, 0f));
        }

        private static void CreatePad(string name, Transform parent, Vector2 center, Vector2 size, Material material, SurfaceType surface)
        {
            // The plane primitive is 10 x 10 units.
            var pad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pad.name = name;
            pad.transform.SetParent(parent, false);
            pad.transform.localPosition = new Vector3(center.x, PadHeight, center.y);
            pad.transform.localScale = new Vector3(size.x / 10f, 1f, size.y / 10f);
            pad.GetComponent<MeshRenderer>().sharedMaterial = material;
            pad.AddComponent<SurfaceMarker>().Surface = surface;
        }

        private static void CreateRamp(string name, Transform parent, Vector2 center, float width, float length, float angle,
            Material material, SurfaceType surface)
        {
            // A tilted slab whose near top edge sits on the ground; its far edge ends length * sin(angle) up.
            const float thickness = 1f;
            float radians = angle * Mathf.Deg2Rad;
            float centerHeight = 0.5f * length * Mathf.Sin(radians) - 0.5f * thickness * Mathf.Cos(radians);

            var ramp = CreateBlock(name, parent, new Vector3(center.x, centerHeight, center.y), Quaternion.Euler(-angle, 0f, 0f),
                new Vector3(width, thickness, length), material);
            ramp.AddComponent<SurfaceMarker>().Surface = surface;
        }

        private static GameObject CreateBlock(string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.SetLocalPositionAndRotation(position, rotation);
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = material;
            return block;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }

            return null;
        }

        private static void DestroyRoot(Scene scene, string name)
        {
            var root = FindRoot(scene, name);
            if (root != null)
                Object.DestroyImmediate(root);
        }

        private sealed class CourseMaterials
        {
            public readonly Material Asphalt = Lit("Surface_Asphalt", new Color(0.17f, 0.17f, 0.19f), 0.15f);
            public readonly Material Dirt = Lit("Surface_Dirt", new Color(0.45f, 0.33f, 0.22f), 0.05f);
            public readonly Material Gravel = Lit("Surface_Gravel", new Color(0.62f, 0.6f, 0.56f), 0.05f);
            public readonly Material Mud = Lit("Surface_Mud", new Color(0.25f, 0.17f, 0.11f), 0.35f);
            public readonly Material Grass = Lit("Surface_Grass", new Color(0.24f, 0.48f, 0.19f), 0.05f);
            public readonly Material Ramp = Lit("Dev_Ramp", new Color(0.35f, 0.36f, 0.42f), 0.2f);
            public readonly Material Barrier = Lit("Dev_Barrier", new Color(0.95f, 0.72f, 0.1f), 0.3f);
            public readonly Material Block = Lit("Dev_Block", new Color(0.55f, 0.55f, 0.55f), 0.1f);
            public readonly Material Crate = Lit("Dev_Crate", new Color(0.62f, 0.4f, 0.2f), 0.1f);

            private static Material Lit(string name, Color color, float smoothness) =>
                EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialsFolder}/{name}.mat", color, smoothness);
        }
    }
}
