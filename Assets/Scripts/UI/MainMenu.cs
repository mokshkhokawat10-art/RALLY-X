using System.Collections.Generic;
using RallyX.Audio;
using RallyX.Core;
using RallyX.Garage;
using RallyX.Progression;
using RallyX.SaveSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// The title screen: RACE (the chosen track in the player's car), GARAGE, SETTINGS (volumes and vibration,
    /// <see cref="SettingsPanel"/>) and QUIT, with the player's car turning on the showroom turntable behind and their
    /// credits. The menu slides in when the screen opens. Left and right (or the arrows) browse the tracks: each shows
    /// its laps and best lap, a locked one what unlocks it (RACE is off while it is shown); showing an unlocked track
    /// chooses it, saved in the profile. Keyboard, gamepad and mouse. Layout and styling are in
    /// Assets/UI/Menus/MainMenu.uxml and Menus.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class MainMenu : MonoBehaviour
    {
        private const string SingleTrackHints = "UP / DOWN   CHOOSE        ENTER   CONFIRM";
        private const string TrackHints = "UP / DOWN   CHOOSE        LEFT / RIGHT   TRACK        ENTER   CONFIRM";
        private const string SettingsHints = "UP / DOWN   CHOOSE        LEFT / RIGHT   CHANGE        ESC   BACK";

        [SerializeField] private VehicleRoster roster;
        [SerializeField] private VehicleShowroom showroom;

        [Tooltip("Tracks and starting credits. RACE loads the chosen track from here.")]
        [SerializeField] private ProgressionSettings progression;

        [Tooltip("Track scene RACE loads when there are no progression settings.")]
        [SerializeField] private string raceScene = "PineValley";

        private MenuSelection selection = new MenuSelection();
        private readonly SettingsPanel settings = new SettingsPanel();
        private UIDocument document;
        private RallyInputActions input;
        private PlayerProfile profile;
        private VisualElement boundRoot;
        private Label carName;
        private Label carCategory;
        private Label credits;
        private Label trackName;
        private Label trackDetails;
        private Label trackRecord;
        private Label hints;
        private Button raceButton;
        private Button previousTrackButton;
        private Button nextTrackButton;
        private int trackIndex = -1;

        public MenuSelection Selection => selection;

        /// <summary>The settings screen opened from SETTINGS.</summary>
        public SettingsPanel Settings => settings;

        /// <summary>The track shown, which RACE loads when it is unlocked; null without progression settings.</summary>
        public TrackInfo Track { get; private set; }

        /// <summary>True when the track shown is locked, so it can't be raced.</summary>
        public bool TrackLocked => Track != null && !Track.IsUnlocked(profile);

        public string RaceScene => Track != null ? Track.SceneName : raceScene;

        /// <summary>True when RACE can be pressed: the track shown is unlocked and its scene is in the build.</summary>
        public bool CanRace => !TrackLocked && GameScenes.CanLoad(RaceScene);

        /// <summary>The car the player will race, shown on the turntable.</summary>
        public VehicleDefinition Vehicle { get; private set; }

        public string CreditsText => credits != null ? credits.text : null;
        public string TrackRecordText => trackRecord != null ? trackRecord.text : null;
        public string TrackDetailsText => trackDetails != null ? trackDetails.text : null;

        public void Configure(VehicleRoster newRoster, VehicleShowroom newShowroom, ProgressionSettings newProgression, string fallbackRaceScene)
        {
            roster = newRoster;
            showroom = newShowroom;
            progression = newProgression;
            raceScene = fallbackRaceScene;
            trackIndex = -1;
        }

        /// <summary>Loads the track shown, unless it is locked.</summary>
        public void Race()
        {
            if (CanRace)
                GameScenes.Load(RaceScene);
        }

        public void OpenGarage() => GameScenes.Load(GameScenes.Garage);

        /// <summary>Opens the settings screen (volumes and vibration) over the menu.</summary>
        public void OpenSettings()
        {
            settings.Open();
            if (hints != null)
                hints.text = SettingsHints;
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Shows a track, wrapping round the ends of the list. An unlocked track becomes the player's chosen track;
        /// a locked one is only shown, with what unlocks it.
        /// </summary>
        public void ShowTrack(int index)
        {
            var tracks = Tracks;
            if (tracks.Count == 0)
                return;

            trackIndex = (index % tracks.Count + tracks.Count) % tracks.Count;
            var track = tracks[trackIndex];
            if (track != null && track.IsUnlocked(profile))
                profile.SelectTrack(track.Id);
            Refresh();
        }

        public void NextTrack()
        {
            GameAudio.Play(UiCue.Switch);
            ShowTrack(trackIndex + 1);
        }

        public void PreviousTrack()
        {
            GameAudio.Play(UiCue.Switch);
            ShowTrack(trackIndex - 1);
        }

        private IReadOnlyList<TrackInfo> Tracks =>
            progression != null && progression.Tracks != null ? progression.Tracks.Tracks : System.Array.Empty<TrackInfo>();

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            input = new RallyInputActions();
            settings.Closed += Refresh;
        }

        private void OnEnable()
        {
            input.EnableMenu();
            profile = PlayerProfile.Current;
            profile.Changed += Refresh;
        }

        private void OnDisable()
        {
            input.DisableMenu();
            profile.Changed -= Refresh;
        }

        private void OnDestroy() => input.Dispose();

        private void Start()
        {
            if (roster == null)
            {
                Debug.LogError($"{name}: MainMenu has no VehicleRoster.", this);
                enabled = false;
                return;
            }

            if (progression != null)
                profile.EnsureStarted(progression.StartingCredits);
            Bind();
            Refresh();
        }

        private void Update()
        {
            if (!Bind())
                return;

            if (settings.IsOpen)
            {
                settings.HandleInput(input);
                return;
            }

            if (input.MenuUp.WasPressedThisFrame())
                selection.Move(-1);
            if (input.MenuDown.WasPressedThisFrame())
                selection.Move(1);
            if (Tracks.Count > 1)
            {
                if (input.MenuLeft.WasPressedThisFrame())
                    PreviousTrack();
                if (input.MenuRight.WasPressedThisFrame())
                    NextTrack();
            }

            if (input.Submit.WasPressedThisFrame())
                selection.Submit();
        }

        private void Refresh()
        {
            if (roster == null)
                return;

            Vehicle = roster.Resolve(profile);

            // Open on the track the player races; after that, keep the one they browsed to.
            var tracks = Tracks;
            if (trackIndex < 0 || trackIndex >= tracks.Count)
            {
                var resolved = progression != null && progression.Tracks != null ? progression.Tracks.Resolve(profile) : null;
                trackIndex = resolved != null ? IndexOf(tracks, resolved) : -1;
            }

            Track = trackIndex >= 0 ? tracks[trackIndex] : null;

            if (Bind() && Vehicle != null)
            {
                carName.text = Vehicle.DisplayName.ToUpperInvariant();
                carCategory.text = Vehicle.Category.ToUpperInvariant();
                credits.text = Credits.Format(profile.Credits);

                bool locked = TrackLocked;
                trackName.text = Track != null ? Track.DisplayName.ToUpperInvariant() : RaceScene.ToUpperInvariant();
                trackName.EnableInClassList("track-name--locked", locked);
                trackDetails.text = DescribeTrack();
                trackRecord.text = locked ? "LOCKED: " + Track.UnlockHint(profile) : DescribeTrackRecord();
                trackRecord.EnableInClassList("track-record--locked", locked);
                raceButton.SetEnabled(CanRace);

                bool browsable = tracks.Count > 1;
                SetVisible(previousTrackButton, browsable);
                SetVisible(nextTrackButton, browsable);
                hints.text = settings.IsOpen ? SettingsHints : browsable ? TrackHints : SingleTrackHints;
            }

            if (showroom != null)
                showroom.Show(Vehicle, locked: false);
        }

        /// <summary>The track's setting, laps and pay, e.g. "DRY CANYON  |  3 LAPS  |  CREDITS x1.25".</summary>
        private string DescribeTrack()
        {
            if (Track == null)
                return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Track.Description))
                parts.Add(Track.Description.ToUpperInvariant());
            if (Track.Laps > 0)
                parts.Add($"{Track.Laps} {(Track.Laps == 1 ? "LAP" : "LAPS")}");
            if (!Mathf.Approximately(Track.CreditMultiplier, 1f))
                parts.Add($"CREDITS x{Track.CreditMultiplier:0.##}");
            return string.Join("  |  ", parts);
        }

        private string DescribeTrackRecord()
        {
            if (Track == null)
                return string.Empty;

            var record = profile.GetTrackRecord(Track.Id);
            if (record.BestLap <= 0f)
                return "NO LAP RECORD YET";

            var vehicle = roster.Find(record.BestLapVehicle);
            string car = vehicle != null ? $"  ({vehicle.DisplayName.ToUpperInvariant()})" : string.Empty;
            return $"BEST LAP {Racing.RaceTime.Format(record.BestLap)}{car}";
        }

        private static int IndexOf(IReadOnlyList<TrackInfo> tracks, TrackInfo track)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] == track)
                    return i;
            }

            return -1;
        }

        private static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Finds the menu's elements; again whenever the document rebuilds its tree.</summary>
        private bool Bind()
        {
            var root = document.rootVisualElement;
            if (root == null)
                return false;
            if (root == boundRoot)
                return true;

            raceButton = root.Q<Button>("race-button");
            var garageButton = root.Q<Button>("garage-button");
            var settingsButton = root.Q<Button>("settings-button");
            var quitButton = root.Q<Button>("quit-button");
            previousTrackButton = root.Q<Button>("track-previous");
            nextTrackButton = root.Q<Button>("track-next");
            carName = root.Q<Label>("car-name");
            carCategory = root.Q<Label>("car-category");
            credits = root.Q<Label>("credits");
            trackName = root.Q<Label>("track-name");
            trackDetails = root.Q<Label>("track-details");
            trackRecord = root.Q<Label>("track-record");
            hints = root.Q<Label>("hints");
            if (raceButton == null || garageButton == null || quitButton == null || previousTrackButton == null || nextTrackButton == null
                || carName == null || carCategory == null || credits == null || trackName == null || trackDetails == null || trackRecord == null
                || hints == null)
            {
                Debug.LogError($"{name}: the menu layout is missing elements MainMenu needs. Check MainMenu.uxml.", this);
                enabled = false;
                return false;
            }

            boundRoot = root;
            previousTrackButton.focusable = false;
            nextTrackButton.focusable = false;
            previousTrackButton.clicked += PreviousTrack;
            nextTrackButton.clicked += NextTrack;
            raceButton.SetEnabled(CanRace);
            selection = new MenuSelection();
            selection.Add(raceButton, Race);
            selection.Add(garageButton, OpenGarage);

            // Optional: only offered when the layout has the settings screen.
            if (settingsButton != null && settings.Bind(root))
                selection.Add(settingsButton, OpenSettings);
            else if (settingsButton != null)
                SetVisible(settingsButton, false);
            selection.Add(quitButton, Quit);

            // The menu slides in: the entry state is applied now and removed next frame, so the transition runs.
            var side = root.Q(className: "menu-side");
            if (side != null)
            {
                side.AddToClassList("menu-side--enter");
                side.schedule.Execute(() => side.RemoveFromClassList("menu-side--enter"));
            }

            return true;
        }
    }
}
