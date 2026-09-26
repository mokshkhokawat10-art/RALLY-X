using RallyX.Core;
using RallyX.Racing;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// Race HUD and results screen for the player's racer: position, lap, race and lap times, speed, the start
    /// countdown, the wrong-way warning and lap messages, then results with a restart button. The restart input
    /// works at any time. Layout and styling are in Assets/UI/Race/RaceHud.uxml and RaceHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class RaceHud : MonoBehaviour
    {
        private const string NoTime = "-:--.---";

        [SerializeField] private RaceManager race;

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

        public void Configure(RaceManager newRace) => race = newRace;

        /// <summary>Starts the race again from the grid. Used by the results button and the restart input.</summary>
        public void RestartRace()
        {
            if (race != null)
                race.Restart();
        }

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            input = new RallyInputActions();
        }

        private void OnEnable()
        {
            input.Enable();
            if (race == null)
                return;

            race.RaceRestarted += OnRaceRestarted;
            race.LapCompleted += OnLapCompleted;
            race.ParticipantFinished += OnParticipantFinished;
        }

        private void OnDisable()
        {
            input.Disable();
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
            messageLabel.text = string.Empty;
            SetResultsVisible(true);
            restartButton.Focus();
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
            shownPosition = shownRacers = shownLap = shownSpeed = -1;
            messageLabel.text = string.Empty;
            SetVisible(results, resultsVisible);
            return true;
        }
    }
}
