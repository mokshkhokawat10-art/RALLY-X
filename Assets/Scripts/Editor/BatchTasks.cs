using UnityEditor;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Entry points for headless runs (Unity accepts one -executeMethod per launch):
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; -executeMethod RallyX.EditorTools.BatchTasks.SetupAndBuild
    /// </summary>
    public static class BatchTasks
    {
        public static void SetupAndBuild()
        {
            bool success = ProjectSetup.Run()
                           && VehicleContentBuilder.BuildMissing()
                           && CameraContentBuilder.BuildMissing()
                           && RaceContentBuilder.BuildMissing()
                           && PineValleyTrack.LoadOrCreate() != null
                           && SandboxCourseBuilder.Rebuild();
            if (!success)
                EditorApplication.Exit(1);
        }

        /// <summary>Setup plus a full regeneration of every track scene.</summary>
        public static void SetupAndBuildTracks()
        {
            bool success = ProjectSetup.Run()
                           && VehicleContentBuilder.BuildMissing()
                           && CameraContentBuilder.BuildMissing()
                           && RaceContentBuilder.BuildMissing()
                           && TrackSceneBuilder.Build(PineValleyTrack.LoadOrCreate());
            if (!success)
                EditorApplication.Exit(1);
        }
    }
}
