using RallyX.Cameras;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>Creates the default chase camera settings asset if it is missing.</summary>
    public static class CameraContentBuilder
    {
        public const string ChaseCameraConfigPath = "Assets/Settings/Cameras/ChaseCamera_Default.asset";

        [MenuItem("RALLY X/Build/Create Missing Camera Content")]
        public static void BuildMissingFromMenu() => BuildMissing();

        public static bool BuildMissing()
        {
            EditorAssetUtility.LoadOrCreate(ChaseCameraConfigPath, ScriptableObject.CreateInstance<ChaseCameraConfig>);
            AssetDatabase.SaveAssets();
            return true;
        }

        public static ChaseCameraConfig LoadChaseCameraConfig() =>
            AssetDatabase.LoadAssetAtPath<ChaseCameraConfig>(ChaseCameraConfigPath);
    }
}
