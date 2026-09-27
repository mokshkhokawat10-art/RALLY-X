using RallyX.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// The loading screen shown between scenes (<see cref="LoadingScreen"/>): its layout, the panel it draws on, the tracks
    /// it can describe, the tips it shows and its timing. Loaded from a Resources folder (<see cref="ResourcePath"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Loading Screen Config", fileName = "LoadingScreen")]
    public sealed class LoadingScreenConfig : ScriptableObject
    {
        /// <summary>Where the loading screen loads its config from: Assets/Resources/RallyX/LoadingScreen.asset.</summary>
        public const string ResourcePath = "RallyX/LoadingScreen";

        [SerializeField] private PanelSettings panelSettings;
        [SerializeField] private VisualTreeAsset layout;

        [Tooltip("Tracks, so loading one shows its name, details and map.")]
        [SerializeField] private TrackRoster tracks;

        [Tooltip("One is shown, at random, on each loading screen.")]
        [SerializeField, TextArea] private string[] tips =
        {
            "Brake in a straight line before the corner, then turn in. The car grips best when it isn't doing both.",
            "A tap of the handbrake swings the tail round a hairpin. Hold the throttle to keep the slide going.",
            "Gravel and dirt spin the wheels sooner than asphalt. Squeeze the throttle out of slow corners.",
            "Mud and grass slow you right down. Stay on the road, even if it means a wider line.",
            "Hold the brake in the air to dip the nose, the throttle to lift it, and land square.",
            "Upgrades are bought per car. The garage shows what each stage does before you pay.",
            "A top-three finish at Pine Valley opens Red Canyon, and harder tracks pay more credits.",
            "Rev the engine on the grid during the countdown. It won't move until GO.",
            "Change volumes and vibration in SETTINGS, from the main menu or the pause menu.",
        };

        [Tooltip("Seconds to cover the screen before the scene is swapped.")]
        [SerializeField, Range(0.05f, 1f)] private float fadeIn = 0.22f;

        [Tooltip("Seconds the screen stays up once the new scene is in, while it settles.")]
        [SerializeField, Range(0f, 2f)] private float hold = 0.35f;

        [Tooltip("Seconds to uncover the new scene.")]
        [SerializeField, Range(0.05f, 1f)] private float fadeOut = 0.4f;

        public PanelSettings PanelSettings => panelSettings;
        public VisualTreeAsset Layout => layout;
        public TrackRoster Tracks => tracks;
        public string[] Tips => tips;
        public float FadeIn => fadeIn;
        public float Hold => hold;
        public float FadeOut => fadeOut;

#if UNITY_EDITOR
        /// <summary>Sets what the builder made or found.</summary>
        public void Configure(PanelSettings newPanelSettings, VisualTreeAsset newLayout, TrackRoster newTracks)
        {
            panelSettings = newPanelSettings;
            layout = newLayout;
            tracks = newTracks;
        }
#endif
    }
}
