using System;
using System.IO;
using RallyX.Tracks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Renders a set of views of each track to PNG files for review without opening the scenes:
    /// a top-down overview, chase-camera views around the lap and aerial views of each bridge.
    /// Batch mode: -executeMethod RallyX.EditorTools.TrackCaptureTool.CaptureBatch -captureDir &lt;folder&gt; [-track &lt;scene name&gt;]
    /// </summary>
    public static class TrackCaptureTool
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const float ViewSpacing = 120f;

        [MenuItem("RALLY X/Dev/Capture Track Screenshots")]
        public static void CaptureFromMenu()
        {
            string folder = Path.GetFullPath("Captures");
            if (CaptureTracks(null, folder))
                EditorUtility.RevealInFinder(folder);
        }

        /// <summary>Batch mode: -captureDir &lt;folder&gt; (default Captures) and optionally -track &lt;scene name&gt; (default every track).</summary>
        public static void CaptureBatch()
        {
            string folder = CommandLineValue("-captureDir") ?? Path.GetFullPath("Captures");
            if (!CaptureTracks(CommandLineValue("-track"), folder))
                EditorApplication.Exit(1);
        }

        /// <summary>Captures one track (by scene name) or, with null, every track, each into its own subfolder.</summary>
        public static bool CaptureTracks(string sceneName, string folder)
        {
            bool captured = false;
            foreach (var definition in TrackCatalog.LoadOrCreateAll())
            {
                if (sceneName != null && definition.SceneName != sceneName)
                    continue;
                if (!Capture(definition, Path.Combine(folder, definition.SceneName)))
                    return false;
                captured = true;
            }

            if (!captured)
                Debug.LogError($"[RALLY X Capture] No track with the scene name '{sceneName}'.");
            return captured;
        }

        public static bool Capture(TrackDefinition definition, string folder)
        {
            string scenePath = TrackSceneBuilder.ScenePath(definition);
            if (!File.Exists(scenePath) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogError($"[RALLY X Capture] Cannot open {scenePath}.");
                return false;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null)
            {
                Debug.LogError("[RALLY X Capture] The track scene has no TrackPath.");
                return false;
            }

            Directory.CreateDirectory(folder);
            var layout = TrackLayout.Build(definition);
            var cameraObject = new GameObject("CaptureCamera");
            var renderTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.farClipPlane = 1200f;
                camera.targetTexture = renderTexture;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

                CaptureOverview(camera, definition, folder, renderTexture);

                int view = 1;
                for (float distance = 0f; distance < path.Length; distance += ViewSpacing, view++)
                {
                    Vector3 position = path.EvaluatePosition(distance - 8f) + Vector3.up * 3.2f;
                    Vector3 target = path.EvaluatePosition(distance + 25f) + Vector3.up * 1f;
                    Save(camera, renderTexture, position, target, 60f, Path.Combine(folder, $"{view:00}_lap_{distance:0000}m.png"));
                }

                var bridges = definition.Bridges;
                for (int b = 0; b < bridges.Length; b++)
                {
                    var center = layout.Main.Get(layout.SampleIndexAt(layout.AnchorDistance(bridges[b].center)));
                    Vector3 position = center.Position + center.Right * 28f - center.Forward * 18f + Vector3.up * 14f;
                    Save(camera, renderTexture, position, center.Position, 55f, Path.Combine(folder, $"{view++:00}_bridge_{b + 1}.png"));
                }

                Debug.Log($"[RALLY X Capture] Saved {view} views to {folder}.");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
            }
        }

        private static void CaptureOverview(Camera camera, TrackDefinition definition, string folder, RenderTexture target)
        {
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;

            // From 500 m up the terrain drops to its coarsest detail and covers the road, so render it at full detail.
            var terrain = Terrain.activeTerrain;
            float pixelError = terrain != null ? terrain.heightmapPixelError : 0f;
            if (terrain != null)
                terrain.heightmapPixelError = 1f;

            float size = definition.TerrainSize;
            camera.orthographic = true;
            camera.orthographicSize = size * 0.5f;
            camera.transform.SetPositionAndRotation(new Vector3(size * 0.5f, 500f, size * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            Render(camera, target, Path.Combine(folder, "00_overview.png"));
            camera.orthographic = false;
            RenderSettings.fog = fog;
            if (terrain != null)
                terrain.heightmapPixelError = pixelError;
        }

        private static void Save(Camera camera, RenderTexture target, Vector3 position, Vector3 lookAt, float fieldOfView, string file)
        {
            camera.fieldOfView = fieldOfView;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
            Render(camera, target, file);
        }

        private static void Render(Camera camera, RenderTexture target, string file)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
                RenderPipeline.SubmitRenderRequest(camera, request);
            else
                camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(file, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        /// <summary>The value after a command-line option, e.g. "Captures" for -captureDir Captures, or null.</summary>
        internal static string CommandLineValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
