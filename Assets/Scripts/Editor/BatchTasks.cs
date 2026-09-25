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
                           && SandboxCourseBuilder.Rebuild();
            if (!success)
                EditorApplication.Exit(1);
        }
    }
}
