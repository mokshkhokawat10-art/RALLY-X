using RallyX.Racing;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.EditorTools
{
    /// <summary>Creates the default race settings and the UI panel settings if they are missing.</summary>
    public static class RaceContentBuilder
    {
        public const string RaceSettingsPath = "Assets/Settings/Racing/RaceSettings_Default.asset";
        public const string PanelSettingsPath = "Assets/UI/RallyPanelSettings.asset";
        public const string ThemePath = "Assets/UI/RallyTheme.tss";
        public const string RaceHudLayoutPath = "Assets/UI/Race/RaceHud.uxml";

        // UI is laid out for this screen size and scaled to the real one.
        private static readonly Vector2Int ReferenceResolution = new Vector2Int(1920, 1080);

        [MenuItem("RALLY X/Build/Create Missing Race Content")]
        public static void BuildMissingFromMenu() => BuildMissing();

        public static bool BuildMissing()
        {
            EditorAssetUtility.LoadOrCreate(RaceSettingsPath, ScriptableObject.CreateInstance<RaceSettings>);

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
            {
                Debug.LogError($"[RALLY X Build] Missing UI theme {ThemePath}.");
                return false;
            }

            EditorAssetUtility.LoadOrCreate(PanelSettingsPath, () =>
            {
                var panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.themeStyleSheet = theme;
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = ReferenceResolution;
                // The whole 1920x1080 layout always fits; wider screens get more room at the sides.
                panel.screenMatchMode = PanelScreenMatchMode.Shrink;
                return panel;
            });

            AssetDatabase.SaveAssets();
            return true;
        }

        public static RaceSettings LoadRaceSettings() => AssetDatabase.LoadAssetAtPath<RaceSettings>(RaceSettingsPath);

        public static PanelSettings LoadPanelSettings() => AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);

        public static VisualTreeAsset LoadRaceHudLayout() => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RaceHudLayoutPath);
    }
}
