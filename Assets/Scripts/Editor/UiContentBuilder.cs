using RallyX.Progression;
using RallyX.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.EditorTools
{
    /// <summary>
    /// The front end's shared content: the panel's scaling (the 1920x1080 layout always fits and wider screens get room at
    /// the sides), the loading screen's config in a Resources folder (<see cref="LoadingScreenConfig"/>, created once, so its
    /// tips and timing are kept) and the tracks' maps (<see cref="TrackMapBaker"/>). Touches no scene.
    /// </summary>
    public static class UiContentBuilder
    {
        public const string LoadingConfigPath = "Assets/Resources/RallyX/LoadingScreen.asset";
        public const string LoadingLayoutPath = "Assets/UI/Loading/LoadingScreen.uxml";

        [MenuItem("RALLY X/Build/Create Missing UI Content")]
        public static void BuildFromMenu() => Build();

        /// <summary>Returns false (and logs why) if something the UI needs is missing.</summary>
        public static bool Build()
        {
            if (!RaceContentBuilder.BuildMissing())
                return false;

            var panel = RaceContentBuilder.LoadPanelSettings();
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LoadingLayoutPath);
            var tracks = AssetDatabase.LoadAssetAtPath<TrackRoster>(ProgressionContentBuilder.TracksPath);
            if (panel == null || layout == null)
            {
                Debug.LogError($"[RALLY X Build] Missing {RaceContentBuilder.PanelSettingsPath} or {LoadingLayoutPath}.");
                return false;
            }

            if (panel.screenMatchMode != PanelScreenMatchMode.Shrink)
            {
                panel.screenMatchMode = PanelScreenMatchMode.Shrink;
                EditorUtility.SetDirty(panel);
            }

            var config = EditorAssetUtility.LoadOrCreate(LoadingConfigPath, ScriptableObject.CreateInstance<LoadingScreenConfig>);
            if (config.PanelSettings == null || config.Layout == null || config.Tracks == null)
            {
                config.Configure(config.PanelSettings != null ? config.PanelSettings : panel, config.Layout != null ? config.Layout : layout,
                    config.Tracks != null ? config.Tracks : tracks);
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            return TrackMapBaker.Bake();
        }
    }
}
