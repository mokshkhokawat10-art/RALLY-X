using RallyX.Core;
using RallyX.Player;
using RallyX.Progression;
using RallyX.Racing;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// Race HUD and results screen for the player's racer: position, lap, race and lap times, speed, the start
    /// countdown, the wrong-way warning and lap messages, then results with the credits earned (from the player's
    /// <see cref="PlayerRaceEntry"/>) and restart and main menu buttons. The restart input works at any time; the pause
    /// input leaves for the main menu once the results are up. Layout and styling are in Assets/UI/Race/RaceHud.uxml
    /// and RaceHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class RaceHud : MonoBehaviour
    {
        private const string NoTime = "-:--.---";

        [SerializeField] private RaceManager race;

        [Tooltip("The player's entry, for the credits and unlocks shown with the results. Optional.")]
        [SerializeField] private PlayerRaceEntry playerEntry;

        [Tooltip("How long lap and finish messages stay on screen, seconds.")]
        [SerializeField, Min(0.5f)] private float messageDuration = 2f;

        private UIDocument document;
        private RallyInputActions input;
        private VisualElement boundRoot;
        private Label positionLabel;
        private Label lapLabel;
        private Label timerLabel;
        private Label lapTimeLabel;
        private Label bestLapLabel;
        private Label speedLabel;
        private Label countdownLabel;
        private Label wrongWayLabel;
        private Label messageLabel;
        private VisualElement results;
        private VisualElement resultsTable;
        private Label resultsPlace;
        private Label resultsSummary;
        private Label resultsLaps;
        private Button restartButton;
        private Button menuButton;
        private VisualElement rewards;
        private Label rewardCredits;
        private Label rewardBalance;
        private Label rewardNews;

        private float messageTimeLeft;
        private float playerFinishedAt = -1f;
        private bool resultsVisible;
        private int shownPosition = -1;
        private int shownRacers = -1;
        private int shownLap = -1;
        private int shownSpeed = -1;

        public RaceManager Race => race;

        /// <summary>True while the results screen is on show.</summary>
        public bool ResultsVisible => resultsVisible;

        public void Configure(RaceManager newRace, PlayerRaceEntry newPlayerEntry = null)
        {
            race = newRace;
            playerEntry = newPlayerEntry;
        }

        /// <summary>Starts the race again from the grid. Used by the results button and the restart input.</summary>
        public void RestartRace()
        {
            if (race != null)
                race.Restart();
        }

        /// <summary>Leaves the race for the main menu, if the game has one.</summary>
        public void GoToMainMenu() => GameScenes.Load(GameScenes.MainMenu);

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            input = new RallyInputActions();
        }

        private void OnEnable()
        {
            input.Enable();
            if (playerEntry != null)
                playerEntry.ResultRecorded += OnResultRecorded;
            if (race == null)
                return;

            race.RaceRestarted += OnRaceRestarted;
            race.LapCompleted += OnLapCompleted;
            race.ParticipantFinished += OnParticipantFinished;
        }

        private void OnDisable()
        {
            input.Disable();
            if (playerEntry != null)
                playerEntry.ResultRecorded -= OnResultRecorded;
            if (race == null)
                return;

            race.RaceRestarted -= OnRaceRestarted;
            race.LapCompleted -= OnLapCompleted;
            race.ParticipantFinished -= OnParticipantFinished;
        }

        private void OnDestroy() => input.Dispose();

        private void Update()
        {
            if (input.RestartRace.WasPressedThisFrame())
                RestartRace();
            else if (resultsVisible && input.Pause.WasPressedThisFrame() && GameScenes.CanLoad(GameScenes.MainMenu))
                GoToMainMenu();

            if (race == null || !Bind())
                return;

            var player = race.Player;
            var tracker = player != null ? player.Tracker : null;
            boundRoot.style.display = tracker != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (tracker == null)
                return;

            UpdateStats(player, tracker);
            UpdateCentreMessages(tracker);

            if (!resultsVisible && playerFinishedAt >= 0f && Time.time - playerFinishedAt >= race.Settings.ResultsDelay)
                ShowResults();
        }

        private void UpdateStats(RaceParticipant player, RaceTracker tracker)
        {
            if (player.Position != shownPosition || race.Participants.Count != shownRacers)
            {
                shownPosition = player.Position;
                shownRacers = race.Participants.Count;
                positionLabel.text = $"{shownPosition}/{shownRacers}";
            }

            if (tracker.CurrentLap != shownLap)
            {
                shownLap = tracker.CurrentLap;
                lapLabel.text = $"{shownLap}/{race.Laps}";
            }

            int speed = Mathf.RoundToInt(player.Vehicle.SpeedKmh);
            if (speed != shownSpeed)
            {
                shownSpeed = speed;
                speedLabel.text = speed.ToString();
            }

            timerLabel.text = RaceTime.Format(tracker.Finished ? tracker.FinishTime : race.RaceTime);
            lapTimeLabel.text = "LAP  " + (tracker.Finished ? NoTime : RaceTime.Format(tracker.CurrentLapTime(race.RaceTime)));
            float best = tracker.BestLapTime;
            bestLapLabel.text = "BEST  " + (best > 0f ? RaceTime.Format(best) : NoTime);
        }

        private void UpdateCentreMessages(RaceTracker tracker)
        {
            int number = race.CountdownNumber;
            countdownLabel.text = number > 0 ? number.ToString() : race.ShowingGo ? "GO!" : string.Empty;
            SetVisible(wrongWayLabel, tracker.IsWrongWay);

            if (messageTimeLeft > 0f)
            {
                messageTimeLeft -= Time.deltaTime;
                if (messageTimeLeft <= 0f)
                    messageLabel.text = string.Empty;
            }
        }

        private void ShowMessage(string text)
        {
            messageTimeLeft = messageDuration;
            if (messageLabel != null)
                messageLabel.text = text;
        }

        private void OnRaceRestarted()
        {
            playerFinishedAt = -1f;
            messageTimeLeft = 0f;
            if (messageLabel != null)
                messageLabel.text = string.Empty;
            SetResultsVisible(false);
        }

        private void OnLapCompleted(RaceParticipant participant, int lap, float lapTime)
        {
            if (participant != race.Player || participant.Tracker.Finished)
                return;

            int next = lap + 1;
            ShowMessage(next == race.Laps ? "FINAL LAP" : $"LAP {next}/{race.Laps}");
        }

        private void OnParticipantFinished(RaceParticipant participant)
        {
            if (participant == race.Player)
            {
                playerFinishedAt = Time.time;
                ShowMessage("FINISH!");
            }
            else if (resultsVisible)
            {
                FillResultsTable();
            }
        }

        private void ShowResults()
        {
            var player = race.Player;
            var tracker = player.Tracker;
            resultsPlace.text = RaceTime.Ordinal(player.Position);
            resultsSummary.text = $"TIME  {RaceTime.Format(tracker.FinishTime)}        BEST LAP  {RaceTime.Format(tracker.BestLapTime)}";

            var laps = new System.Text.StringBuilder();
            for (int i = 0; i < tracker.LapTimes.Count; i++)
                laps.Append(i == 0 ? string.Empty : "      ").Append("LAP ").Append(i + 1).Append("  ").Append(RaceTime.Format(tracker.LapTimes[i]));
            resultsLaps.text = laps.ToString();

            FillResultsTable();
            FillRewards(playerEntry != null ? playerEntry.LastReward : null);
            messageLabel.text = string.Empty;
            SetResultsVisible(true);
            restartButton.Focus();
        }

        private void OnResultRecorded(RaceReward reward)
        {
            if (resultsVisible)
                FillRewards(reward);
        }

        /// <summary>Credits earned, the new total, and any record or unlock. Hidden until the race has been paid.</summary>
        private void FillRewards(RaceReward reward)
        {
            if (rewards == null)
                return;

            SetVisible(rewards, reward != null);
            if (reward == null)
            {
                SetVisible(rewardNews, false);
                return;
            }

            rewardCredits.text = "+" + Credits.Format(reward.CreditsEarned);
            rewardBalance.text = Credits.Format(reward.TotalCredits);
            var news = new System.Collections.Generic.List<string>();
            if (reward.NewLapRecord)
                news.Add("NEW TRACK RECORD");
            foreach (string vehicle in reward.UnlockedVehicles)
                news.Add($"{vehicle.ToUpperInvariant()} UNLOCKED");
            foreach (string unlockedTrack in reward.UnlockedTracks)
                news.Add($"{unlockedTrack.ToUpperInvariant()} UNLOCKED");
            rewardNews.text = string.Join("   |   ", news);
            SetVisible(rewardNews, news.Count > 0);
        }

        private void FillResultsTable()
        {
            resultsTable.Clear();
            resultsTable.Add(ResultsRow("POS", "DRIVER", "TIME", "results-row--header"));
            foreach (var participant in race.Standings)
            {
                var tracker = participant.Tracker;
                string time = tracker.Finished ? RaceTime.Format(tracker.FinishTime) : "--";
                resultsTable.Add(ResultsRow(participant.Position.ToString(), participant.DisplayName, time,
                    participant.IsPlayer ? "results-row--player" : null));
            }
        }

        private static VisualElement ResultsRow(string position, string driver, string time, string extraClass)
        {
            var row = new VisualElement();
            row.AddToClassList("results-row");
            if (extraClass != null)
                row.AddToClassList(extraClass);
            row.Add(Cell(position, "results-cell--position"));
            row.Add(Cell(driver, "results-cell--driver"));
            row.Add(Cell(time, "results-cell--time"));
            return row;
        }

        private static Label Cell(string text, string cellClass)
        {
            var label = new Label(text);
            label.AddToClassList("results-cell");
            label.AddToClassList(cellClass);
            return label;
        }

        private void SetResultsVisible(bool visible)
        {
            resultsVisible = visible;
            if (results != null)
                SetVisible(results, visible);
        }

        private static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Finds the HUD's elements; again whenever the document rebuilds its tree.</summary>
        private bool Bind()
        {
            var root = document.rootVisualElement;
            if (root == null)
                return false;
            if (root == boundRoot)
                return true;

            positionLabel = root.Q<Label>("position");
            lapLabel = root.Q<Label>("lap");
            timerLabel = root.Q<Label>("timer");
            lapTimeLabel = root.Q<Label>("lap-time");
            bestLapLabel = root.Q<Label>("best-lap");
            speedLabel = root.Q<Label>("speed");
            countdownLabel = root.Q<Label>("countdown");
            wrongWayLabel = root.Q<Label>("wrong-way");
            messageLabel = root.Q<Label>("message");
            results = root.Q("results");
            resultsTable = root.Q("results-table");
            resultsPlace = root.Q<Label>("results-place");
            resultsSummary = root.Q<Label>("results-summary");
            resultsLaps = root.Q<Label>("results-laps");
            restartButton = root.Q<Button>("restart-button");

            if (positionLabel == null || lapLabel == null || timerLabel == null || lapTimeLabel == null || bestLapLabel == null
                || speedLabel == null || countdownLabel == null || wrongWayLabel == null || messageLabel == null || results == null
                || resultsTable == null || resultsPlace == null || resultsSummary == null || resultsLaps == null || restartButton == null)
            {
                Debug.LogError($"{name}: the HUD layout is missing elements RaceHud needs. Check RaceHud.uxml.", this);
                enabled = false;
                return false;
            }

            boundRoot = root;
            restartButton.clicked += RestartRace;

            // Optional: the rewards, filled once the player's race has been paid.
            rewards = root.Q("results-rewards");
            rewardCredits = root.Q<Label>("results-credits");
            rewardBalance = root.Q<Label>("results-balance");
            rewardNews = root.Q<Label>("results-news");
            if (rewardCredits == null || rewardBalance == null || rewardNews == null)
                rewards = null;
            else
                FillRewards(null);

            // Optional: only shown when the game has a main menu to go back to.
            menuButton = root.Q<Button>("menu-button");
            if (menuButton != null)
            {
                menuButton.clicked += GoToMainMenu;
                SetVisible(menuButton, GameScenes.CanLoad(GameScenes.MainMenu));
            }

            shownPosition = shownRacers = shownLap = shownSpeed = -1;
            messageLabel.text = string.Empty;
            SetVisible(results, resultsVisible);
            return true;
        }
    }
}
