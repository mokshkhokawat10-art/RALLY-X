using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Entry points for headless runs (Unity accepts one -executeMethod per launch):
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; -executeMethod RallyX.EditorTools.BatchTasks.SetupAndBuild
    /// </summary>
    public static class BatchTasks
    {
        /// <summary>Setup plus every generated asset that is missing, including the scenes of tracks that have none yet.</summary>
        public static void SetupAndBuild()
        {
            bool success = ProjectSetup.Run()
                           && VehicleContentBuilder.BuildMissing()
                           && CameraContentBuilder.BuildMissing()
                           && RaceContentBuilder.BuildMissing()
                           && AIContentBuilder.BuildMissing()
                           && GarageContentBuilder.BuildMissing()
                           && ProgressionContentBuilder.BuildMissing()
                           && TrackCatalog.BuildMissing()
                           && SandboxCourseBuilder.Rebuild()
                           && MenuSceneBuilder.Build()
                           && AudioContentBuilder.Build()
                           && UiContentBuilder.Build();
            if (!success)
                EditorApplication.Exit(1);
        }

        /// <summary>Setup plus a full regeneration of every track scene.</summary>
        public static void SetupAndBuildTracks()
        {
            bool success = SetupTrackContent() && TrackCatalog.BuildAll();
            if (!success)
                EditorApplication.Exit(1);
        }

        /// <summary>Setup plus a full regeneration of one track's scene: -track &lt;scene name&gt;, e.g. -track RedCanyon.</summary>
        public static void SetupAndBuildTrack()
        {
            string sceneName = TrackCaptureTool.CommandLineValue("-track");
            bool success = SetupTrackContent();
            var definition = success ? TrackCatalog.Find(sceneName) : null;
            if (success && definition == null)
                Debug.LogError($"[RALLY X Build] No track with the scene name '{sceneName}'. Pass -track <scene name>.");
            if (definition == null || !TrackSceneBuilder.Build(definition))
                EditorApplication.Exit(1);
        }

        /// <summary>Setup plus a regeneration of every car prefab (handling configs, definitions and the roster are kept).</summary>
        public static void SetupAndRebuildCars()
        {
            bool success = ProjectSetup.Run()
                           && VehicleContentBuilder.BuildMissing()
                           && GarageContentBuilder.BuildMissing()
                           && GarageContentBuilder.RebuildCars();
            if (!success)
                EditorApplication.Exit(1);
        }

        /// <summary>
        /// The audio content only: placeholder sounds (regenerated when their recipes changed), configs, surface sounds and
        /// the cars' VehicleAudio. Touches no scene.
        /// </summary>
        public static void BuildAudio()
        {
            if (!AudioContentBuilder.Build())
                EditorApplication.Exit(1);
        }

        /// <summary>The front end's shared content only: panel scaling, the loading screen config and the track maps. Touches no scene.</summary>
        public static void BuildUi()
        {
            if (!UiContentBuilder.Build())
                EditorApplication.Exit(1);
        }

        /// <summary>Setup plus the garage content and the main menu and garage scenes.</summary>
        public static void SetupAndBuildMenus()
        {
            bool success = ProjectSetup.Run()
                           && VehicleContentBuilder.BuildMissing()
                           && RaceContentBuilder.BuildMissing()
                           && GarageContentBuilder.BuildMissing()
                           && ProgressionContentBuilder.BuildMissing()
                           && MenuSceneBuilder.Build();
            if (!success)
                EditorApplication.Exit(1);
        }

        private static bool SetupTrackContent() =>
            ProjectSetup.Run()
            && VehicleContentBuilder.BuildMissing()
            && CameraContentBuilder.BuildMissing()
            && RaceContentBuilder.BuildMissing()
            && AIContentBuilder.BuildMissing()
            && GarageContentBuilder.BuildMissing()
            && ProgressionContentBuilder.BuildMissing();
    }
}
