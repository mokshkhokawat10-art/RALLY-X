using System.Collections;
using System.Collections.Generic;
using RallyX.Core;
using RallyX.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// The screen between scenes. Every scene change made through <see cref="GameScenes.Load"/> fades this in over the old
    /// scene, swaps the scene while it covers the screen, holds for a moment while the new scene settles, then fades out.
    /// It names where the player is going (a track with its details and map, the garage, the main menu), shows a tip and
    /// a progress bar. The scene is loaded as soon as the screen is covered, so a change takes a fraction of a second longer
    /// than without it. Created when the game starts and kept across scenes, with its layout and settings from
    /// <see cref="LoadingScreenConfig"/> in a Resources folder; without it scenes change directly. The same persistent host
    /// keeps <see cref="InputHints"/> following the device in use.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadingScreen : MonoBehaviour
    {
        // Drawn above every scene's UI on the same panel.
        private const int SortingOrder = 1000;

        // The cover-up never takes more frames than this, however fast frames come (a light scene uncapped, or tests),
        // so a scene change is never held up for long.
        private const int MaxCoverFrames = 40;

        private static LoadingScreen instance;

        private LoadingScreenConfig config;
        private UIDocument document;
        private VisualElement overlay;
        private Label eyebrow;
        private Label title;
        private Label details;
        private Label tip;
        private VisualElement fill;
        private TrackMap map;
        private int nextTip = -1;

        // True until the new scene is in; a second request then is a double press and is dropped.
        private bool covering;

        /// <summary>The running instance, or null before the game has started.</summary>
        public static LoadingScreen Instance => instance;

        /// <summary>True from the moment a scene change starts until the new scene is uncovered.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>The scene being loaded, or null.</summary>
        public string Loading { get; private set; }

        /// <summary>The title shown (e.g. "PINE VALLEY"). For tests and tooling.</summary>
        public string TitleText => title != null ? title.text : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            var host = new GameObject("RallyUI");
            DontDestroyOnLoad(host);
            host.AddComponent<LoadingScreen>();
        }

        // Entering Play Mode without a domain reload keeps statics.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            instance = null;
            GameScenes.Loader = null;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            config = Resources.Load<LoadingScreenConfig>(LoadingScreenConfig.ResourcePath);
            if (config == null || config.PanelSettings == null || config.Layout == null)
                return;

            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = config.PanelSettings;
            document.sortingOrder = SortingOrder;
            document.visualTreeAsset = config.Layout;
        }

        // The document builds its tree once enabled; bind on the first frame, before any scene change can be asked for.
        private void Start()
        {
            if (document != null && Bind())
                GameScenes.Loader = Begin;
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;

            instance = null;
            if (GameScenes.Loader == (System.Func<string, bool>)Begin)
                GameScenes.Loader = null;
        }

        private void Update() => InputHints.Poll();

        private bool Bind()
        {
            var root = document.rootVisualElement;
            if (root == null)
                return false;

            // The document spans the screen; only the overlay itself may catch the pointer, and only while shown.
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0f;
            overlay = root.Q("loading");
            eyebrow = root.Q<Label>("loading-eyebrow");
            title = root.Q<Label>("loading-title");
            details = root.Q<Label>("loading-details");
            tip = root.Q<Label>("loading-tip");
            fill = root.Q("loading-fill");
            var mapHolder = root.Q("loading-map");
            if (overlay == null || eyebrow == null || title == null || details == null || tip == null || fill == null || mapHolder == null)
            {
                Debug.LogError($"{name}: the loading screen layout is missing elements. Check LoadingScreen.uxml.", this);
                return false;
            }

            map = new TrackMap { LineWidth = 7f };
            map.AddToClassList("loading-map-drawing");
            mapHolder.Add(map);
            overlay.style.display = DisplayStyle.None;
            return true;
        }

        /// <summary>Starts a scene change behind the loading screen. Returns false if it can't (the plain load goes ahead).</summary>
        private bool Begin(string sceneName)
        {
            if (overlay == null || !isActiveAndEnabled)
                return false;

            // A second request while the screen is still covering (a double press) is dropped; one while the new scene is
            // being uncovered starts over for the next scene.
            if (IsLoading && covering)
                return true;

            StopAllCoroutines();
            IsLoading = true;
            covering = true;
            Loading = sceneName;
            Describe(sceneName);
            StartCoroutine(Run(sceneName));
            return true;
        }

        private IEnumerator Run(string sceneName)
        {
            // Cover the screen.
            fill.style.width = Length.Percent(0f);
            overlay.style.display = DisplayStyle.Flex;
            overlay.AddToClassList("loading--hidden");
            overlay.schedule.Execute(() => overlay.RemoveFromClassList("loading--hidden"));
            yield return Progress(0f, 0.55f, config.FadeIn, MaxCoverFrames);

            // One frame fully covered, then swap the scene.
            yield return null;
            SceneManager.LoadScene(sceneName);
            yield return null;
            covering = false;
            yield return Progress(0.55f, 1f, Mathf.Max(config.Hold, 0.05f));

            // Uncover the new scene.
            overlay.AddToClassList("loading--hidden");
            yield return new WaitForSecondsRealtime(config.FadeOut);
            overlay.style.display = DisplayStyle.None;
            overlay.RemoveFromClassList("loading--hidden");
            IsLoading = false;
            Loading = null;
        }

        private IEnumerator Progress(float from, float to, float seconds, int maxFrames = int.MaxValue)
        {
            int frames = 0;
            for (float time = 0f; time < seconds && frames < maxFrames; time += Time.unscaledDeltaTime, frames++)
            {
                float t = time / seconds;
                fill.style.width = Length.Percent(Mathf.Lerp(from, to, 1f - (1f - t) * (1f - t)) * 100f);
                yield return null;
            }

            fill.style.width = Length.Percent(to * 100f);
        }

        /// <summary>What the player is going to: a track with its details and map, or a screen by name.</summary>
        private void Describe(string sceneName)
        {
            var track = FindTrack(sceneName);
            if (track != null)
            {
                eyebrow.text = "NEXT STAGE";
                title.text = track.DisplayName.ToUpperInvariant();
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(track.Description))
                    parts.Add(track.Description.ToUpperInvariant());
                if (track.Laps > 0)
                    parts.Add($"{track.Laps} {(track.Laps == 1 ? "LAP" : "LAPS")}");
                details.text = string.Join("   |   ", parts);
                var outline = new Vector2[track.MapOutline.Count];
                for (int i = 0; i < outline.Length; i++)
                    outline[i] = track.MapOutline[i];
                map.SetOutline(outline);
            }
            else
            {
                eyebrow.text = "RALLY X";
                title.text = sceneName == GameScenes.MainMenu ? "MAIN MENU" : sceneName == GameScenes.Garage ? "GARAGE" : sceneName.ToUpperInvariant();
                details.text = string.Empty;
                map.SetOutline(null);
            }

            map.style.display = map.HasOutline ? DisplayStyle.Flex : DisplayStyle.None;
            tip.text = NextTip();
        }

        private TrackInfo FindTrack(string sceneName)
        {
            if (config.Tracks == null)
                return null;

            foreach (var track in config.Tracks.Tracks)
            {
                if (track != null && track.SceneName == sceneName)
                    return track;
            }

            return null;
        }

        private string NextTip()
        {
            var tips = config.Tips;
            if (tips == null || tips.Length == 0)
                return string.Empty;

            // A random first tip, then round the list, so the same one doesn't come twice in a row.
            nextTip = nextTip < 0 ? Random.Range(0, tips.Length) : (nextTip + 1) % tips.Length;
            return tips[nextTip];
        }
    }
}
