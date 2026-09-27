using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Renders every garage car from the front, side, rear and above to PNG files for review without opening the Editor.
    /// Batch mode: -executeMethod RallyX.EditorTools.VehicleCaptureTool.CaptureBatch -captureDir &lt;folder&gt;
    /// </summary>
    public static class VehicleCaptureTool
    {
        private const int Width = 1280;
        private const int Height = 720;

        [MenuItem("RALLY X/Dev/Capture Car Screenshots")]
        public static void CaptureFromMenu()
        {
            string folder = Path.GetFullPath("Captures/Cars");
            if (Capture(folder))
                EditorUtility.RevealInFinder(folder);
        }

        public static void CaptureBatch()
        {
            string folder = TrackCaptureTool.CommandLineValue("-captureDir") ?? Path.GetFullPath("Captures");
            if (!Capture(Path.Combine(folder, "Cars")))
                EditorApplication.Exit(1);
        }

        public static bool Capture(string folder)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            // A new scene unloads assets only scripts hold, so load the roster afterwards.
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var roster = GarageContentBuilder.LoadRoster();
            if (roster == null)
            {
                Debug.LogError("[RALLY X Capture] No vehicle roster.");
                return false;
            }

            Directory.CreateDirectory(folder);
            foreach (var root in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                root.transform.rotation = Quaternion.Euler(38f, -140f, 0f);
                root.intensity = 1.4f;
                root.shadows = LightShadows.Soft;
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 5f;
            ground.GetComponent<Renderer>().sharedMaterial = EditorAssetUtility.LoadOrCreateLitMaterial(
                "Assets/Materials/Dev/Surface_Asphalt.mat", new Color(0.2f, 0.2f, 0.21f), 0.2f);

            // Compile shaders before drawing, or the first view of a new material shows the placeholder shader.
            bool asyncCompilation = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            var cameraObject = new GameObject("CaptureCamera");
            var renderTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.targetTexture = renderTexture;
                camera.fieldOfView = 30f;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

                foreach (var vehicle in roster.Vehicles)
                {
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(vehicle.Prefab.gameObject);
                    car.transform.position = Vector3.zero;
                    string id = vehicle.Id;

                    // The first frame after a car appears can still draw with incomplete materials; draw one and throw it away.
                    Shoot(camera, renderTexture, new Vector3(5.5f, 2.2f, 7.5f), null, null);
                    Shoot(camera, renderTexture, new Vector3(5.5f, 2.2f, 7.5f), folder, $"{id}_front");
                    Shoot(camera, renderTexture, new Vector3(9.5f, 1.6f, 0f), folder, $"{id}_side");
                    Shoot(camera, renderTexture, new Vector3(-6f, 2.6f, -7.5f), folder, $"{id}_rear");
                    Shoot(camera, renderTexture, new Vector3(0.01f, 12f, 0f), folder, $"{id}_top");
                    Shoot(camera, renderTexture, new Vector3(2.2f, 0.7f, 3.2f), folder, $"{id}_wheel", new Vector3(0.8f, 0.4f, 1.3f));
                    Object.DestroyImmediate(car);
                }

                Debug.Log($"[RALLY X Capture] Saved car views to {folder}.");
                return true;
            }
            finally
            {
                EditorSettings.asyncShaderCompilation = asyncCompilation;
                Object.DestroyImmediate(cameraObject);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
            }
        }

        private static void Shoot(Camera camera, RenderTexture target, Vector3 position, string folder, string name, Vector3? lookAt = null)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation((lookAt ?? new Vector3(0f, 0.7f, 0f)) - position));
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
                RenderPipeline.SubmitRenderRequest(camera, request);
            else
                camera.Render();

            if (folder == null)
                return;

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
    }
}
