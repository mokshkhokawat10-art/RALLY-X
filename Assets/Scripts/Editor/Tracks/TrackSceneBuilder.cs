using System.Collections.Generic;
using System.IO;
using RallyX.AI;
using RallyX.Cameras;
using RallyX.Core;
using RallyX.Player;
using RallyX.Progression;
using RallyX.Racing;
using RallyX.Tracks;
using RallyX.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

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
        private const float DetailDistance = 80f;

        // The reflection cubemap is taken this high above the start line, at this size per face.
        private const float ReflectionHeight = 3f;
        private const int ReflectionResolution = 128;

        public static string ScenePath(TrackDefinition definition) => $"{TracksFolder}/{definition.SceneName}.unity";

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

            if (RaceContentBuilder.LoadRaceSettings() == null || RaceContentBuilder.LoadPanelSettings() == null ||
                RaceContentBuilder.LoadRaceHudLayout() == null)
            {
                Debug.LogError(LogPrefix + "Race content is missing. Run RALLY X > Build > Create Missing Race Content first.");
                return false;
            }

            if (AIContentBuilder.LoadSettings() == null || AIContentBuilder.LoadDrivers() == null)
            {
                Debug.LogError(LogPrefix + "AI content is missing. Run RALLY X > Build > Create Missing AI Content first.");
                return false;
            }

            if (GarageContentBuilder.LoadRoster() == null)
            {
                Debug.LogError(LogPrefix + "Garage content is missing. Run RALLY X > Build > Create Missing Garage Content first.");
                return false;
            }

            if (ProgressionContentBuilder.LoadSettings() == null)
            {
                Debug.LogError(LogPrefix + "Progression content is missing. Run RALLY X > Build > Create Missing Progression Content first.");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogError(LogPrefix + "Track build cancelled: unsaved changes in the open scene were not saved.");
                return false;
            }

            // Opening a scene, deleting the old generated assets and generating art (imports) all unload assets that only
            // scripts hold on to, which turns earlier references to the definition, its theme and surfaces and the camera
            // config into destroyed objects. So those come first: the old assets are deleted, the art is generated, then
            // the definition and the art are loaded afresh (the art is only loaded the second time), and everything else
            // is loaded after that.
            // By instance id: a definition loaded before another track was built may already be unloaded, and then only
            // its id still leads to its file.
            string definitionPath = AssetDatabase.GetAssetPath(definition.GetInstanceID());
            string generatedFolder = $"{TracksFolder}/{definition.SceneName}";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(generatedFolder);
            EditorAssetUtility.EnsureFolder(generatedFolder);
            definition = LoadFresh(definitionPath, definition);

            // A track without a theme is the pine forest.
            var forest = definition.Theme == null ? TrackTheme.CreateForest() : null;
            if (forest != null)
                forest.hideFlags = HideFlags.DontUnloadUnusedAsset;
            EnvironmentAssetBuilder.LoadOrCreate(forest != null ? forest : definition.Theme);
            definition = LoadFresh(definitionPath, definition);
            var theme = forest != null ? forest : definition.Theme;
            var assets = EnvironmentAssetBuilder.LoadOrCreate(theme);

            var layout = TrackLayout.Build(definition);
            var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleContentBuilder.CarPrefabPath);
            var cameraConfig = CameraContentBuilder.LoadChaseCameraConfig();
            var raceSettings = RaceContentBuilder.LoadRaceSettings();
            var panelSettings = RaceContentBuilder.LoadPanelSettings();
            var hudLayout = RaceContentBuilder.LoadRaceHudLayout();
            var aiSettings = AIContentBuilder.LoadSettings();
            var aiDrivers = AIContentBuilder.LoadDrivers();
            var roster = GarageContentBuilder.LoadRoster();
            var progression = ProgressionContentBuilder.LoadSettings();
            var trackInfo = FindTrackForScene(progression, definition.SceneName);

            ConfigureLighting(scene, theme, assets.Sky);

            var offRoad = theme.OffRoadSurface != null ? theme.OffRoadSurface : VehicleContentBuilder.LoadSurface("Grass");
            var terrain = TerrainGenerator.Generate(definition, layout, assets, theme, $"{generatedFolder}/Terrain.asset");
            CreateTerrainObject(terrain.Data, offRoad, assets.TerrainMaterial);
            BuildRoads(layout, assets, generatedFolder);
            TrackPropsBuilder.Build(new GameObject("Props").transform, layout, terrain, assets, generatedFolder);
            CreatePostProcessing(theme, generatedFolder);
            CreateSurfacePalette(theme, offRoad);

            var path = CreateTrackPath(layout);
            CreateBranches(layout, path);
            var grid = CreateSpawnGrid(layout, path);
            var chaseCamera = ConfigureCamera(scene, grid.GetSlot(0), cameraConfig);
            var circuit = CreateRaceCircuit(layout, path, grid);
            var race = new GameObject(nameof(RaceManager)).AddComponent<RaceManager>();
            race.Configure(raceSettings, circuit);

            // The player's car is the one chosen in the garage, with its upgrades, so it is spawned when the scene loads.
            var entry = new GameObject(nameof(PlayerRaceEntry)).AddComponent<PlayerRaceEntry>();
            entry.Configure(roster, grid, race, chaseCamera, progression, trackInfo);
            CreateHud(race, entry, panelSettings, hudLayout);
            new GameObject(nameof(AIOpponents)).AddComponent<AIOpponents>().Configure(race, carPrefab, aiSettings, aiDrivers);

            // The race HUD uses the screen corners; the dev panels stay available on F1 and F2.
            HideDevPanel(new GameObject(nameof(DevOverlay), typeof(DevOverlay)).GetComponent<DevOverlay>());

            // Last, so the reflections show the finished scene.
            BakeReflections(layout.Main.Samples[0].Position, generatedFolder);

            string scenePath = ScenePath(definition);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                Debug.LogError(LogPrefix + $"Failed to save {scenePath}.");
                return false;
            }

            EditorAssetUtility.AddSceneToBuildSettings(scenePath);
            AssetDatabase.SaveAssets();
            if (forest != null)
                Object.DestroyImmediate(forest);
            Debug.Log(LogPrefix + $"Built {definition.DisplayName}: {layout.Main.Length:0} m lap, {circuit.CheckpointCount} checkpoints, " +
                      $"{terrain.Data.treeInstanceCount} trees, scene {scenePath}.");
            return true;
        }

        /// <summary>
        /// The definition at a path read afresh from disk, with its theme: both are unloaded first, since a copy loaded
        /// earlier (say, before another track was built) can hold references to assets unloaded since. An unsaved
        /// definition is kept as it is.
        /// </summary>
        private static TrackDefinition LoadFresh(string path, TrackDefinition current)
        {
            if (string.IsNullOrEmpty(path))
                return current;

            var loaded = AssetDatabase.LoadAssetAtPath<TrackDefinition>(path);
            if (loaded != null)
            {
                var theme = new SerializedObject(loaded).FindProperty("theme")?.objectReferenceValue;
                if (theme != null && EditorUtility.IsPersistent(theme))
                    Resources.UnloadAsset(theme);
                Resources.UnloadAsset(loaded);
            }

            EditorUtility.UnloadUnusedAssetsImmediate();
            return AssetDatabase.LoadAssetAtPath<TrackDefinition>(path);
        }

        private static void ConfigureLighting(Scene scene, TrackTheme theme, Material sky)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent(out Light light) || light.type != LightType.Directional)
                    continue;

                light.transform.rotation = Quaternion.Euler(theme.SunRotation);
                light.intensity = theme.SunIntensity;
                light.color = theme.SunColor;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = theme.ShadowStrength;
                RenderSettings.sun = light;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = theme.FogDensity;
            RenderSettings.fogColor = theme.FogColor;

            // A theme's own sky comes with its own ambient light, set directly so it never depends on baking.
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = theme.AmbientSky;
                RenderSettings.ambientEquatorColor = theme.AmbientEquator;
                RenderSettings.ambientGroundColor = theme.AmbientGround;
            }
        }

        private static void CreateTerrainObject(TerrainData data, SurfaceType offRoad, Material material)
        {
            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terrain";
            var terrain = terrainObject.GetComponent<Terrain>();
            terrain.materialTemplate = material != null ? material : GraphicsSettings.defaultRenderPipeline.defaultTerrainMaterial;
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 250f;
            terrain.treeDistance = 400f;
            terrain.treeBillboardDistance = 400f;

            // Grass and bushes are drawn near the camera only; the fog hides where they stop.
            terrain.detailObjectDistance = DetailDistance;
            terrain.detailObjectDensity = 1f;

            // Anything off the road is the theme's off-road surface as far as the wheels are concerned.
            terrainObject.AddComponent<SurfaceMarker>().Surface = offRoad;
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

                    // Roads have a second submesh, their shoulders, drawn as the verge; bridge decks are one piece.
                    var piece = new GameObject(name);
                    piece.transform.SetParent(root, false);
                    piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                    piece.AddComponent<MeshRenderer>().sharedMaterials = section.IsBridge ? new[] { material } : new[] { material, assets.Verge };
                    piece.AddComponent<MeshCollider>().sharedMesh = mesh;
                    piece.AddComponent<SurfaceMarker>().Surface = surface;
                    GameObjectUtility.SetStaticEditorFlags(piece, StaticEditorFlags.BatchingStatic);
                    index++;
                }
            }
        }

        /// <summary>
        /// The track's colour grade, as a global volume over the pipeline's defaults: filmic (ACES) tonemapping, exposure,
        /// contrast and saturation, white balance, split toning of shadows and highlights, bloom and a light vignette.
        /// </summary>
        private static void CreatePostProcessing(TrackTheme theme, string generatedFolder)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, $"{generatedFolder}/PostProcessing.asset");

            AddOverride<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var colour = AddOverride<ColorAdjustments>(profile);
            colour.postExposure.Override(theme.PostExposure);
            colour.contrast.Override(theme.Contrast);
            colour.saturation.Override(theme.Saturation);
            AddOverride<WhiteBalance>(profile).temperature.Override(theme.Temperature);
            var split = AddOverride<SplitToning>(profile);
            split.shadows.Override(theme.ShadowTint);
            split.highlights.Override(theme.HighlightTint);
            var bloom = AddOverride<Bloom>(profile);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(theme.Bloom);
            bloom.scatter.Override(0.65f);
            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(theme.Vignette);
            vignette.smoothness.Override(0.45f);

            var volume = new GameObject("PostProcessing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(profile);
        }

        private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>();
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>The track's own dust, spray and tyre-mark colours for its road surfaces and off-road ground, from its theme.</summary>
        private static void CreateSurfacePalette(TrackTheme theme, SurfaceType offRoad)
        {
            var dirt = VehicleContentBuilder.LoadSurface("Dirt");
            var gravel = VehicleContentBuilder.LoadSurface("Gravel");
            var palette = new GameObject(nameof(SurfacePalette)).AddComponent<SurfacePalette>();
            palette.SetEntries(new[]
            {
                new SurfacePalette.Entry(dirt, theme.RoadDirt.light, Mark(theme.RoadDirt.dark, 0.5f, 0.55f)),
                new SurfacePalette.Entry(gravel, theme.RoadGravel.light, Mark(theme.RoadGravelDarkStones, 0.6f, 0.5f)),
                new SurfacePalette.Entry(offRoad, theme.Grass.light, Mark(theme.Dirt.dark, 0.6f, 0.5f)),
            });
        }

        private static Color Mark(Color color, float shade, float alpha) => new Color(color.r * shade, color.g * shade, color.b * shade, alpha);

        /// <summary>
        /// Renders the finished scene into a cubemap from above the start line and uses it for every reflection on the
        /// track (car paint, glass, wet mud), so reflections show this track's sky and ground rather than a default sky.
        /// </summary>
        private static void BakeReflections(Vector3 start, string generatedFolder)
        {
            var probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            probe.transform.position = start + Vector3.up * ReflectionHeight;
            probe.mode = ReflectionProbeMode.Baked;
            probe.resolution = ReflectionResolution;
            probe.hdr = true;
            probe.size = new Vector3(4000f, 1000f, 4000f);
            probe.importance = 0;

            string path = $"{generatedFolder}/Reflections.exr";
            if (!Lightmapping.BakeReflectionProbe(probe, path))
            {
                Debug.LogWarning(LogPrefix + "Could not bake the track's reflections; it will use the sky's.");
                Object.DestroyImmediate(probe.gameObject);
                return;
            }

            AssetDatabase.ImportAsset(path);
            var cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = cubemap;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cubemap;
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

        /// <summary>Shortcuts as runtime branches off the main path, for AI drivers.</summary>
        private static void CreateBranches(TrackLayout layout, TrackPath path)
        {
            if (layout.Shortcuts.Count == 0)
                return;

            var root = new GameObject("TrackBranches").transform;
            for (int i = 0; i < layout.Shortcuts.Count; i++)
            {
                var samples = layout.Shortcuts[i].Samples;
                var points = new Vector3[samples.Length];
                var widths = new float[samples.Length];
                for (int k = 0; k < samples.Length; k++)
                {
                    points[k] = samples[k].Position;
                    widths[k] = samples[k].Width;
                }

                var branch = new GameObject($"Shortcut{i + 1}").AddComponent<TrackBranch>();
                branch.transform.SetParent(root, false);
                branch.SetSamples(path, points, widths);
            }
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

        /// <summary>The chase camera, waiting behind pole position for the player's car (set when the scene loads).</summary>
        private static ChaseCamera ConfigureCamera(Scene scene, Transform pole, ChaseCameraConfig config)
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
                serialized.ApplyModifiedPropertiesWithoutUndo();

                root.transform.position = pole.position - pole.forward * config.Distance + Vector3.up * config.Height;
                root.transform.LookAt(pole.position + Vector3.up * config.LookHeight);
                return chaseCamera;
            }

            return null;
        }

        private static RaceCircuit CreateRaceCircuit(TrackLayout layout, TrackPath path, TrackSpawnGrid grid)
        {
            var definition = layout.Definition;
            var root = new GameObject("RaceCircuit");
            var distances = CheckpointPlacement.Distances(layout);
            var checkpoints = new Checkpoint[distances.Count];
            for (int i = 0; i < distances.Count; i++)
            {
                var sample = layout.Main.Get(layout.SampleIndexAt(distances[i]));
                var gate = new GameObject(i == 0 ? "Checkpoint_00_StartFinish" : $"Checkpoint_{i:00}").AddComponent<Checkpoint>();
                gate.transform.SetParent(root.transform, false);
                gate.transform.SetPositionAndRotation(sample.Position, Quaternion.LookRotation(sample.Forward));
                gate.SetSize(sample.Width + 2f * definition.CheckpointMargin, definition.CheckpointHeight);
                checkpoints[i] = gate;
            }

            var circuit = root.AddComponent<RaceCircuit>();
            circuit.Configure(path, grid, checkpoints);
            return circuit;
        }

        private static void CreateHud(RaceManager race, PlayerRaceEntry entry, PanelSettings panelSettings, VisualTreeAsset hudLayout)
        {
            var hud = new GameObject(nameof(RaceHud));
            var document = hud.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = hudLayout;
            hud.AddComponent<RaceHud>().Configure(race, entry);
        }

        /// <summary>The career's entry for a track scene, or null (the track then keeps no records).</summary>
        private static TrackInfo FindTrackForScene(ProgressionSettings progression, string sceneName)
        {
            foreach (var track in progression.Tracks.Tracks)
            {
                if (track != null && track.SceneName == sceneName)
                    return track;
            }

            Debug.LogWarning(LogPrefix + $"No TrackInfo in {ProgressionContentBuilder.TracksPath} races {sceneName}; best laps won't be kept.");
            return null;
        }

        private static void HideDevPanel(Component panel)
        {
            var serialized = new SerializedObject(panel);
            serialized.FindProperty("visible").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>True when the track's scene file exists.</summary>
        public static bool SceneExists(TrackDefinition definition) => File.Exists(ScenePath(definition));
    }
}
