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
    /// The title screen: RACE, GARAGE, SETTINGS (volumes and vibration, <see cref="SettingsPanel"/>) and QUIT, each with a
    /// line saying what it does, the player's car turning on the showroom turntable behind, their career (credits, wins,
    /// races) and the next race's track, which Left and Right (or its arrows) browse. RACE opens the track selection screen:
    /// the track's map, laps, lap length, pay and best lap, or what unlocks it; Left and Right browse, Enter races, Esc goes
    /// back. Showing an unlocked track chooses it, saved in the profile; a locked one can't be raced. Everything slides in
    /// when the screen opens, and the control hints follow the device in use. Keyboard, gamepad and mouse. Layout and
    /// styling are in Assets/UI/Menus/MainMenu.uxml and Menus.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class MainMenu : MonoBehaviour
    {
        // Delay between menu items sliding in, milliseconds.
        private const long ItemStagger = 70;

        private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            ["race-button"] = "Choose a track and take on the field",
            ["garage-button"] = "Choose your car and buy upgrades",
            ["settings-button"] = "Sound volumes and vibration",
            ["quit-button"] = "Leave RALLY X",
        };

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
        private Label wins;
        private Label races;
        private Label trackName;
        private Label trackDetails;
        private Label trackRecord;
        private Label description;
        private HintBar hints;
        private Button raceButton;
        private Button previousTrackButton;
        private Button nextTrackButton;
        private TrackSelect trackSelect;
        private int trackIndex = -1;

        public MenuSelection Selection => selection;

        /// <summary>The settings screen opened from SETTINGS.</summary>
        public SettingsPanel Settings => settings;

        /// <summary>True while the track selection screen is open.</summary>
        public bool TrackSelectOpen => trackSelect != null && trackSelect.IsOpen;

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

        /// <summary>The control hints shown now, e.g. "ENTER SELECT". For tests and tooling.</summary>
        public string HintText => hints != null ? hints.Text : null;

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

        /// <summary>
        /// What RACE on the menu does: opens the track selection screen, or races straight away when there is no track list
        /// (or no screen for it in the layout).
        /// </summary>
        public void ChooseTrack()
        {
            if (trackSelect != null && Tracks.Count > 0)
            {
                trackSelect.Open();
                ShowHints();
            }
            else
            {
                Race();
            }
        }

        public void OpenGarage() => GameScenes.Load(GameScenes.Garage);

        /// <summary>Opens the settings screen (volumes and vibration) over the menu.</summary>
        public void OpenSettings()
        {
            settings.Open();
            ShowHints();
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
            trackSelect?.AnimateSwap(1);
        }

        public void PreviousTrack()
        {
            GameAudio.Play(UiCue.Switch);
            ShowTrack(trackIndex - 1);
            trackSelect?.AnimateSwap(-1);
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

            if (TrackSelectOpen)
            {
                UpdateTrackSelect();
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

        private void UpdateTrackSelect()
        {
            if (input.Back.WasPressedThisFrame())
            {
                CloseTrackSelect();
                return;
            }

            if (Tracks.Count > 1)
            {
                if (input.MenuLeft.WasPressedThisFrame())
                    PreviousTrack();
                if (input.MenuRight.WasPressedThisFrame())
                    NextTrack();
            }

            if (input.Submit.WasPressedThisFrame())
                RaceFromTrackSelect();
        }

        private void RaceFromTrackSelect()
        {
            if (!CanRace)
            {
                GameAudio.Play(UiCue.Error);
                return;
            }

            GameAudio.Play(UiCue.Confirm);
            Race();
        }

        private void CloseTrackSelect()
        {
            if (trackSelect == null || !trackSelect.IsOpen)
                return;

            GameAudio.Play(UiCue.Back);
            trackSelect.Close();
            ShowHints();
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
                if (wins != null)
                    wins.text = profile.Wins.ToString();
                if (races != null)
                    races.text = profile.RacesFinished.ToString();

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
                trackSelect?.Show(Track, trackIndex, tracks.Count, locked, locked ? Track.UnlockHint(profile) : null,
                    Track != null ? profile.GetTrackRecord(Track.Id).BestLap : 0f, CanRace);
                ShowHints();
            }

            if (showroom != null)
                showroom.Show(Vehicle, locked: false);
        }

        /// <summary>The hints for what is on screen: the settings, the track selection or the menu.</summary>
        private void ShowHints()
        {
            if (hints == null)
                return;

            if (settings.IsOpen)
                hints.Show((HintAction.Choose, "CHOOSE"), (HintAction.Change, "CHANGE"), (HintAction.Back, "BACK"));
            else if (TrackSelectOpen)
                hints.Show((HintAction.Browse, "TRACK"), (HintAction.Confirm, "RACE"), (HintAction.Back, "BACK"));
            else if (Tracks.Count > 1)
                hints.Show((HintAction.Choose, "CHOOSE"), (HintAction.Browse, "TRACK"), (HintAction.Confirm, "SELECT"));
            else
                hints.Show((HintAction.Choose, "CHOOSE"), (HintAction.Confirm, "SELECT"));
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

        private void ShowDescription(int index)
        {
            if (description == null || index < 0)
                return;

            var button = selection.Selected;
            description.text = button != null && Descriptions.TryGetValue(button.name, out string text) ? text : string.Empty;
            description.AddToClassList("menu-description--enter");
            description.schedule.Execute(() => description.RemoveFromClassList("menu-description--enter"));
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
            if (raceButton == null || garageButton == null || quitButton == null || previousTrackButton == null || nextTrackButton == null
                || carName == null || carCategory == null || credits == null || trackName == null || trackDetails == null || trackRecord == null)
            {
                Debug.LogError($"{name}: the menu layout is missing elements MainMenu needs. Check MainMenu.uxml.", this);
                enabled = false;
                return false;
            }

            boundRoot = root;

            // Optional parts of the layout.
            wins = root.Q<Label>("career-wins");
            races = root.Q<Label>("career-races");
            description = root.Q<Label>("menu-description");
            var hintKeys = root.Q("hint-keys");
            hints = hintKeys != null ? new HintBar(hintKeys) : null;
            trackSelect = TrackSelect.Bind(root, PreviousTrack, NextTrack, RaceFromTrackSelect, CloseTrackSelect);

            previousTrackButton.focusable = false;
            nextTrackButton.focusable = false;
            previousTrackButton.clicked += PreviousTrack;
            nextTrackButton.clicked += NextTrack;
            raceButton.SetEnabled(CanRace);
            selection = new MenuSelection();
            selection.Changed += ShowDescription;
            selection.Add(raceButton, ChooseTrack);
            selection.Add(garageButton, OpenGarage);

            // Optional: only offered when the layout has the settings screen.
            if (settingsButton != null && settings.Bind(root))
                selection.Add(settingsButton, OpenSettings);
            else if (settingsButton != null)
                SetVisible(settingsButton, false);
            selection.Add(quitButton, Quit);
            ShowDescription(selection.Index);
            PlayEntrance(root);
            return true;
        }

        /// <summary>
        /// The screen's entrance: the side panel slides in, then each menu item one after another, then the side cards. Each
        /// element's entry state (a style class) is applied now and removed after its delay, so its transition runs.
        /// </summary>
        private static void PlayEntrance(VisualElement root)
        {
            Enter(root.Q(className: "menu-side"), "menu-side--enter", 0);
            int index = 0;
            foreach (var item in root.Query<Button>(className: "menu-item").ToList())
                Enter(item, "menu-item--enter", 180 + ItemStagger * index++);
            foreach (var card in root.Query(className: "side-card").ToList())
                Enter(card, "side-card--enter", 300 + ItemStagger * index++);
        }

        private static void Enter(VisualElement element, string entryClass, long delay)
        {
            if (element == null)
                return;

            element.AddToClassList(entryClass);
            element.schedule.Execute(() => element.RemoveFromClassList(entryClass)).StartingIn(delay);
        }
    }
}
