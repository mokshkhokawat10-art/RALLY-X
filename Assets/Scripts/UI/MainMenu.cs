using RallyX.Core;
using RallyX.Garage;
using RallyX.Progression;
using RallyX.SaveSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// The title screen: RACE (the player's track in the player's car), GARAGE and QUIT, with the player's car turning
    /// on the showroom turntable behind, their credits, and the track's best lap. Keyboard, gamepad and mouse. Layout
    /// and styling are in Assets/UI/Menus/MainMenu.uxml and Menus.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] private VehicleRoster roster;
        [SerializeField] private VehicleShowroom showroom;

        [Tooltip("Tracks and starting credits. RACE loads the player's track from here.")]
        [SerializeField] private ProgressionSettings progression;

        [Tooltip("Track scene RACE loads when there are no progression settings.")]
        [SerializeField] private string raceScene = "PineValley";

        private MenuSelection selection = new MenuSelection();
        private UIDocument document;
        private RallyInputActions input;
        private PlayerProfile profile;
        private VisualElement boundRoot;
        private Label carName;
        private Label carCategory;
        private Label credits;
        private Label trackName;
        private Label trackRecord;
        private Button raceButton;

        public MenuSelection Selection => selection;

        /// <summary>The track RACE loads, or null without progression settings.</summary>
        public TrackInfo Track { get; private set; }

        public string RaceScene => Track != null ? Track.SceneName : raceScene;

        /// <summary>The car the player will race, shown on the turntable.</summary>
        public VehicleDefinition Vehicle { get; private set; }

        public string CreditsText => credits != null ? credits.text : null;
        public string TrackRecordText => trackRecord != null ? trackRecord.text : null;

        public void Configure(VehicleRoster newRoster, VehicleShowroom newShowroom, ProgressionSettings newProgression, string fallbackRaceScene)
        {
            roster = newRoster;
            showroom = newShowroom;
            progression = newProgression;
            raceScene = fallbackRaceScene;
        }

        public void Race() => GameScenes.Load(RaceScene);

        public void OpenGarage() => GameScenes.Load(GameScenes.Garage);

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            input = new RallyInputActions();
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

            if (input.MenuUp.WasPressedThisFrame())
                selection.Move(-1);
            if (input.MenuDown.WasPressedThisFrame())
                selection.Move(1);
            if (input.Submit.WasPressedThisFrame())
                selection.Submit();
        }

        private void Refresh()
        {
            Vehicle = roster.Resolve(profile);
            Track = progression != null && progression.Tracks != null ? progression.Tracks.Resolve(profile) : null;
            if (Bind() && Vehicle != null)
            {
                carName.text = Vehicle.DisplayName.ToUpperInvariant();
                carCategory.text = Vehicle.Category.ToUpperInvariant();
                credits.text = Credits.Format(profile.Credits);
                trackName.text = Track != null ? Track.DisplayName.ToUpperInvariant() : RaceScene.ToUpperInvariant();
                trackRecord.text = DescribeTrackRecord();
                raceButton.SetEnabled(GameScenes.CanLoad(RaceScene));
            }

            if (showroom != null)
                showroom.Show(Vehicle, locked: false);
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
            var quitButton = root.Q<Button>("quit-button");
            carName = root.Q<Label>("car-name");
            carCategory = root.Q<Label>("car-category");
            credits = root.Q<Label>("credits");
            trackName = root.Q<Label>("track-name");
            trackRecord = root.Q<Label>("track-record");
            if (raceButton == null || garageButton == null || quitButton == null || carName == null || carCategory == null || credits == null
                || trackName == null || trackRecord == null)
            {
                Debug.LogError($"{name}: the menu layout is missing elements MainMenu needs. Check MainMenu.uxml.", this);
                enabled = false;
                return false;
            }

            boundRoot = root;
            raceButton.SetEnabled(GameScenes.CanLoad(RaceScene));
            selection = new MenuSelection();
            selection.Add(raceButton, Race);
            selection.Add(garageButton, OpenGarage);
            selection.Add(quitButton, Quit);
            return true;
        }
    }
}
