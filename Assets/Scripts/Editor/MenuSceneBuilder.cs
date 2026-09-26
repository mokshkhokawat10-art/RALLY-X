using RallyX.Garage;
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
    /// Generates the front-end scenes, the main menu and the garage: a dark showroom with the car on a turntable
    /// and a UI Toolkit screen on top. Both scenes are rebuilt from scratch each time and put first in the build
    /// scene list, so a build opens on the main menu.
    /// </summary>
    public static class MenuSceneBuilder
    {
        public const string MainMenuScenePath = "Assets/Scenes/MainMenu/MainMenu.unity";
        public const string GarageScenePath = "Assets/Scenes/Garage/Garage.unity";
        public const string MainMenuLayoutPath = "Assets/UI/Menus/MainMenu.uxml";
        public const string GarageLayoutPath = "Assets/UI/Menus/Garage.uxml";

        private const string LogPrefix = "[RALLY X Build] ";
        private const string MaterialsFolder = "Assets/Materials/Showroom";
        private const string VolumeProfilePath = "Assets/Settings/Rendering/Showroom_Volume.asset";

        // Turntable top, radius and the camera framing. The main menu pulls the view back and slides it left so the
        // car sits to the right of the menu panel.
        private const float PlatformHeight = 0.14f;
        private const float PlatformRadius = 3.1f;
        private const float CameraFieldOfView = 34f;
        private const float MainMenuShift = 2.6f;
        private const float MainMenuPullBack = 1.2f;
        private static readonly Vector3 CameraPosition = new Vector3(0f, 2.4f, -11f);
        private static readonly Vector3 CameraTarget = new Vector3(0f, 0.45f, 0f);

        // The floor fades into the dark beyond the turntable, so there is no visible edge to the room.
        private const float FogStart = 16f;
        private const float FogEnd = 40f;

        private static readonly Color Background = new Color(0.02f, 0.024f, 0.032f);
        private static readonly Color AccentColor = new Color(1f, 0.77f, 0.14f);

        [MenuItem("RALLY X/Build/Rebuild Menu Scenes")]
        public static void BuildFromMenu() => Build();

        public static bool Build()
        {
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

            if (RaceContentBuilder.LoadPanelSettings() == null || AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuLayoutPath) == null
                || AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GarageLayoutPath) == null)
            {
                Debug.LogError(LogPrefix + $"UI is missing: run RALLY X > Build > Create Missing Race Content and check {MainMenuLayoutPath} and {GarageLayoutPath}.");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogError(LogPrefix + "Menu build cancelled: unsaved changes in the open scene were not saved.");
                return false;
            }

            string raceScene = PineValleyTrack.LoadOrCreate().SceneName;
            if (!BuildScene(MainMenuScenePath, mainMenu: true, raceScene) || !BuildScene(GarageScenePath, mainMenu: false, raceScene))
                return false;

            EditorAssetUtility.PlaceScenesFirstInBuildSettings(MainMenuScenePath, GarageScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + $"Built {MainMenuScenePath} and {GarageScenePath}; the main menu opens first.");
            return true;
        }

        private static bool BuildScene(string scenePath, bool mainMenu, string raceScene)
        {
            // Opening a scene unloads assets only scripts hold on to, so load everything after it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var roster = GarageContentBuilder.LoadRoster();
            var progression = ProgressionContentBuilder.LoadSettings();
            var panelSettings = RaceContentBuilder.LoadPanelSettings();
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(mainMenu ? MainMenuLayoutPath : GarageLayoutPath);
            var materials = new ShowroomMaterials();

            ConfigureLighting(scene);
            if (mainMenu)
                ConfigureCamera(scene, MainMenuShift, MainMenuPullBack);
            else
                ConfigureCamera(scene, 0f, 1f);
            CreateVolume();
            var showroom = CreateShowroom(materials);

            var ui = new GameObject("UI");
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = layout;
            if (mainMenu)
                ui.AddComponent<MainMenu>().Configure(roster, showroom, progression, raceScene);
            else
                ui.AddComponent<GarageMenu>().Configure(roster, showroom, progression);

            EditorAssetUtility.EnsureFolder(System.IO.Path.GetDirectoryName(scenePath)?.Replace('\\', '/'));
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                Debug.LogError(LogPrefix + $"Failed to save {scenePath}.");
                return false;
            }

            return true;
        }

        private static void ConfigureLighting(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent(out Light light) || light.type != LightType.Directional)
                    continue;

                // A dim cool fill; the spot lights do the modelling.
                light.name = "Fill Light";
                light.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
                light.intensity = 0.35f;
                light.color = new Color(0.72f, 0.8f, 1f);
                light.shadows = LightShadows.None;
            }

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.14f, 0.15f, 0.18f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;
            RenderSettings.fogColor = Background;

            // Rim lights sit behind the car aiming at its roofline, so their light ends on the car, not the floor.
            var lights = new GameObject("Lights").transform;
            CreateSpotLight(lights, "Key Light", new Vector3(4f, 7.5f, -6f), new Vector3(0f, 0.6f, 0f), new Color(1f, 0.96f, 0.9f), 140f, 42f, LightShadows.Soft);
            CreateSpotLight(lights, "Rim Light Left", new Vector3(-6f, 5.5f, 6f), new Vector3(0f, 1.1f, 0f), new Color(0.6f, 0.75f, 1f), 45f, 26f, LightShadows.None);
            CreateSpotLight(lights, "Rim Light Right", new Vector3(6.5f, 5f, 5f), new Vector3(0f, 1.1f, 0f), new Color(1f, 0.8f, 0.5f), 40f, 26f, LightShadows.None);
            CreateSpotLight(lights, "Top Light", new Vector3(0f, 9f, 0f), new Vector3(0f, 0f, 0f), new Color(1f, 1f, 1f), 70f, 60f, LightShadows.None);
        }

        private static void CreateSpotLight(Transform parent, string name, Vector3 position, Vector3 aim, Color color, float intensity, float angle,
            LightShadows shadows)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.position = position;
            light.transform.LookAt(aim);
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.6f;
            light.range = 30f;
            light.shadows = shadows;
        }

        private static void ConfigureCamera(Scene scene, float shift, float pullBack)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent(out Camera camera))
                    continue;

                var offset = Vector3.left * shift;
                root.transform.position = CameraTarget + (CameraPosition - CameraTarget) * pullBack + offset;
                root.transform.LookAt(CameraTarget + offset);
                camera.fieldOfView = CameraFieldOfView;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 120f;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                return;
            }
        }

        private static void CreateVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                EditorAssetUtility.EnsureFolder(System.IO.Path.GetDirectoryName(VolumeProfilePath)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);

                var tonemapping = profile.Add<Tonemapping>(true);
                tonemapping.mode.Override(TonemappingMode.ACES);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(0.95f);
                bloom.intensity.Override(0.6f);
                var vignette = profile.Add<Vignette>(true);
                vignette.intensity.Override(0.38f);
                vignette.smoothness.Override(0.45f);
                foreach (var component in profile.components)
                    AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            var volume = new GameObject("Post Processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static VehicleShowroom CreateShowroom(ShowroomMaterials materials)
        {
            var root = new GameObject("Showroom");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = materials.Floor;

            // The rim stays still; the platform, its stripe and the car turn.
            Cylinder("Rim", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(PlatformRadius * 2f + 0.3f, 0.03f, PlatformRadius * 2f + 0.3f), materials.Accent);
            var turntable = new GameObject("Turntable").transform;
            turntable.SetParent(root.transform, false);
            turntable.localPosition = new Vector3(0f, PlatformHeight, 0f);
            Cylinder("Platform", turntable, new Vector3(0f, -PlatformHeight * 0.5f, 0f), new Vector3(PlatformRadius * 2f, PlatformHeight * 0.5f, PlatformRadius * 2f), materials.Platform);
            Box("Stripe", turntable, new Vector3(0f, 0.002f, 0f), new Vector3(0.12f, 0.004f, PlatformRadius * 2f - 0.2f), materials.Accent);


            var showroom = root.AddComponent<VehicleShowroom>();
            showroom.Configure(turntable);
            return showroom;
        }

        private static void Cylinder(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material) =>
            Primitive(PrimitiveType.Cylinder, name, parent, localPosition, scale, material);

        private static void Box(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material) =>
            Primitive(PrimitiveType.Cube, name, parent, localPosition, scale, material);

        private static void Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            Object.DestroyImmediate(primitive.GetComponent<Collider>());
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = scale;
            primitive.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private sealed class ShowroomMaterials
        {
            public ShowroomMaterials()
            {
                Floor = EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialsFolder}/Showroom_Floor.mat", new Color(0.055f, 0.06f, 0.07f), 0.82f);
                Platform = EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialsFolder}/Showroom_Platform.mat", new Color(0.13f, 0.135f, 0.15f), 0.65f);
                Accent = EditorAssetUtility.LoadOrCreate($"{MaterialsFolder}/Showroom_Accent.mat", () =>
                {
                    // Glows faintly so the stripes read in the dark and catch the bloom.
                    var material = new Material(GraphicsSettings.defaultRenderPipeline.defaultMaterial);
                    material.SetColor("_BaseColor", AccentColor);
                    material.SetFloat("_Smoothness", 0.4f);
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", AccentColor * 1.6f);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    return material;
                });
            }

            public Material Floor { get; }
            public Material Platform { get; }
            public Material Accent { get; }
        }
    }
}
