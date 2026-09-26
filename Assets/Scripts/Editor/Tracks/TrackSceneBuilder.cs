using System.Collections.Generic;
using System.IO;
using RallyX.Cameras;
using RallyX.Core;
using RallyX.Player;
using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Generates a complete, drivable track scene from a TrackDefinition. The scene and its generated assets are
    /// rebuilt from scratch each time, so make changes in the definition rather than in the scene.
    /// </summary>
    public static class TrackSceneBuilder
    {
        public const string TracksFolder = "Assets/Scenes/Tracks";

        private const string LogPrefix = "[RALLY X Build] ";
        private const int GridSlots = 4;
        private const float GridFirstSlotDistance = 12f;
        private const float GridRowSpacing = 6.5f;
        private const float CameraNearClip = 0.1f;
        private const float CameraFarClip = 900f;

        public static string ScenePath(TrackDefinition definition) => $"{TracksFolder}/{definition.SceneName}.unity";

        [MenuItem("RALLY X/Build/Rebuild Pine Valley Track")]
        public static void RebuildPineValleyFromMenu() => Build(PineValleyTrack.LoadOrCreate());

        public static bool Build(TrackDefinition definition)
        {
            var problems = TrackLayout.Build(definition).Validate();
            if (problems.Count > 0)
            {
                Debug.LogError(LogPrefix + $"{definition.DisplayName} layout is invalid:\n{string.Join("\n", problems)}");
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(VehicleContentBuilder.CarPrefabPath) == null ||
                CameraContentBuilder.LoadChaseCameraConfig() == null)
            {
                Debug.LogError(LogPrefix + "Vehicle or camera content is missing. Run the vehicle and camera content builders first.");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogError(LogPrefix + "Track build cancelled: unsaved changes in the open scene were not saved.");
                return false;
            }

            // Opening a scene unloads assets that only scripts hold on to, which turns earlier references to the
            // definition, its surfaces and the camera config into destroyed objects. Load everything afterwards.
            string definitionPath = AssetDatabase.GetAssetPath(definition);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            if (!string.IsNullOrEmpty(definitionPath))
                definition = AssetDatabase.LoadAssetAtPath<TrackDefinition>(definitionPath);
            var layout = TrackLayout.Build(definition);
            var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleContentBuilder.CarPrefabPath);
            var cameraConfig = CameraContentBuilder.LoadChaseCameraConfig();

            string generatedFolder = $"{TracksFolder}/{definition.SceneName}";
            AssetDatabase.DeleteAsset(generatedFolder);
            EditorAssetUtility.EnsureFolder(generatedFolder);

            var assets = EnvironmentAssetBuilder.LoadOrCreate();
            ConfigureLighting(scene);

            var terrain = TerrainGenerator.Generate(definition, layout, assets, $"{generatedFolder}/Terrain.asset");
            CreateTerrainObject(terrain.Data);
            BuildRoads(layout, assets, generatedFolder);
            TrackPropsBuilder.Build(new GameObject("Props").transform, layout, terrain, assets);

            var path = CreateTrackPath(layout);
            var grid = CreateSpawnGrid(layout, path);
            var car = SpawnPlayerCar(carPrefab, grid.GetSlot(0));
            ConfigureCamera(scene, car, cameraConfig);
            new GameObject(nameof(DevOverlay), typeof(DevOverlay));

            string scenePath = ScenePath(definition);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                Debug.LogError(LogPrefix + $"Failed to save {scenePath}.");
                return false;
            }

            EditorAssetUtility.AddSceneToBuildSettings(scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + $"Built {definition.DisplayName}: {layout.Main.Length:0} m lap, " +
                      $"{terrain.Data.treeInstanceCount} trees, scene {scenePath}.");
            return true;
        }

        private static void ConfigureLighting(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent(out Light light) || light.type != LightType.Directional)
                    continue;

                light.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
                light.intensity = 1.3f;
                light.color = new Color(1f, 0.95f, 0.86f);
                light.shadows = LightShadows.Soft;
                RenderSettings.sun = light;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0032f;
            RenderSettings.fogColor = new Color(0.64f, 0.72f, 0.78f);
        }

        private static void CreateTerrainObject(TerrainData data)
        {
            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terrain";
            var terrain = terrainObject.GetComponent<Terrain>();
            terrain.materialTemplate = GraphicsSettings.defaultRenderPipeline.defaultTerrainMaterial;
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 250f;
            terrain.treeDistance = 400f;
            terrain.treeBillboardDistance = 400f;

            // Anything off the road is grass as far as the wheels are concerned.
            terrainObject.AddComponent<SurfaceMarker>().Surface = VehicleContentBuilder.LoadSurface("Grass");
        }

        private static void BuildRoads(TrackLayout layout, EnvironmentAssets assets, string generatedFolder)
        {
            var root = new GameObject("Road").transform;
            var asphalt = VehicleContentBuilder.LoadSurface("Asphalt");
            var materials = new Dictionary<SurfaceType, Material>
            {
                [asphalt] = assets.RoadAsphalt,
                [VehicleContentBuilder.LoadSurface("Gravel")] = assets.RoadGravel,
                [VehicleContentBuilder.LoadSurface("Mud")] = assets.RoadMud,
            };

            var roads = new List<(string name, RoadPath path)> { ("Main", layout.Main) };
            for (int i = 0; i < layout.Shortcuts.Count; i++)
                roads.Add(($"Shortcut{i + 1}", layout.Shortcuts[i]));

            int index = 0;
            foreach (var (roadName, path) in roads)
            {
                foreach (var section in RoadMeshBuilder.Split(path))
                {
                    // Bridge decks are planked and grip like asphalt whatever the road either side is.
                    var surface = section.IsBridge ? asphalt : section.Surface;
                    string name = section.IsBridge
                        ? $"{roadName}_{index:00}_Bridge{section.BridgeIndex + 1}"
                        : $"{roadName}_{index:00}_{(surface != null ? surface.DisplayName : "Road")}";
                    var mesh = section.IsBridge
                        ? RoadMeshBuilder.BuildDeck(section.Samples, name)
                        : RoadMeshBuilder.BuildRoad(section.Samples, layout.Definition.RoadBevelWidth, layout.Definition.RoadBevelDrop, name);
                    AssetDatabase.CreateAsset(mesh, $"{generatedFolder}/{name}.asset");

                    var material = section.IsBridge
                        ? assets.BridgeDeck
                        : surface != null && materials.TryGetValue(surface, out var surfaceMaterial) ? surfaceMaterial : assets.RoadDirt;

                    var piece = new GameObject(name);
                    piece.transform.SetParent(root, false);
                    piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                    piece.AddComponent<MeshRenderer>().sharedMaterial = material;
                    piece.AddComponent<MeshCollider>().sharedMesh = mesh;
                    piece.AddComponent<SurfaceMarker>().Surface = surface;
                    GameObjectUtility.SetStaticEditorFlags(piece, StaticEditorFlags.BatchingStatic);
                    index++;
                }
            }
        }

        private static TrackPath CreateTrackPath(TrackLayout layout)
        {
            var samples = layout.Main.Samples;
            var points = new Vector3[samples.Length];
            var widths = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                points[i] = samples[i].Position;
                widths[i] = samples[i].Width;
            }

            var path = new GameObject("TrackPath").AddComponent<TrackPath>();
            path.SetSamples(points, widths);
            return path;
        }

        private static TrackSpawnGrid CreateSpawnGrid(TrackLayout layout, TrackPath path)
        {
            var grid = new GameObject("SpawnGrid").AddComponent<TrackSpawnGrid>();
            var slots = new Transform[GridSlots];
            for (int i = 0; i < GridSlots; i++)
            {
                // Staggered two-wide grid behind the start line; pole position on the right.
                float distance = path.Length - GridFirstSlotDistance - i * GridRowSpacing;
                var sample = layout.Main.Get(layout.SampleIndexAt(distance));
                float lateral = (i % 2 == 0 ? 1f : -1f) * path.EvaluateWidth(distance) * 0.25f;

                var slot = new GameObject($"Slot_{i + 1}").transform;
                slot.SetParent(grid.transform, false);
                slot.SetPositionAndRotation(path.EvaluatePosition(distance) + sample.Right * lateral + Vector3.up * 0.3f,
                    Quaternion.LookRotation(sample.Forward));
                slots[i] = slot;
            }

            grid.SetSlots(slots);
            return grid;
        }

        private static VehicleController SpawnPlayerCar(GameObject carPrefab, Transform slot)
        {
            var car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab);
            car.name = "PlayerCar";
            car.transform.SetPositionAndRotation(slot.position, slot.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(car.transform);
            car.AddComponent<PlayerVehicleInput>();
            car.AddComponent<VehicleDebugPanel>();
            return car.GetComponent<VehicleController>();
        }

        private static void ConfigureCamera(Scene scene, VehicleController target, ChaseCameraConfig config)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent(out Camera camera))
                    continue;

                camera.nearClipPlane = CameraNearClip;
                camera.farClipPlane = CameraFarClip;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                var chaseCamera = root.AddComponent<ChaseCamera>();
                var serialized = new SerializedObject(chaseCamera);
                serialized.FindProperty("config").objectReferenceValue = config;
                serialized.FindProperty("target").objectReferenceValue = target;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Transform car = target.transform;
                root.transform.position = car.position - car.forward * config.Distance + Vector3.up * config.Height;
                root.transform.LookAt(car.position + Vector3.up * config.LookHeight);
                return;
            }
        }

        /// <summary>True when the track's scene file exists.</summary>
        public static bool SceneExists(TrackDefinition definition) => File.Exists(ScenePath(definition));
    }
}
