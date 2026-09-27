using RallyX.Audio;
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
    /// <see cref="PlayerRaceEntry"/>) and restart and main menu buttons. The restart input works at any time. The pause
    /// input pauses the race (time stops) with a menu to resume, restart, change the settings (<see cref="SettingsPanel"/>)
    /// or leave for the main menu; once the results are up it leaves for the main menu. Motorsport touches: start lights
    /// that fill red on 3-2-1 and turn green at GO, a rev bar and gear (from the car's sound-only engine), a minimap with
    /// every racer, and each lap's time against the best so far. The HUD slides in for each start, the countdown numbers and
    /// GO pop in, lap and finish messages punch in (the finish as a banner), the pause menu sums up the race, and the
    /// results fade up with podium colours and their rows (best lap and gap to the winner) following one after another.
    /// Control hints follow the device in use. Layout and styling are in Assets/UI/Race/RaceHud.uxml and RaceHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class RaceHud : MonoBehaviour
    {
        private const string NoTime = "-:--.---";

        // Delay between results rows appearing, milliseconds.
        private const long ResultsRowStagger = 70;

        // Segments in the rev bar; the last few are the red zone.
        private const int RevSegments = 16;
        private const int RedSegments = 3;

        // Seconds the lap time and its delta stay up.
        private const float DeltaDuration = 4f;

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
        private VisualElement pauseOverlay;
        private Label pauseTrack;
        private MenuSelection pauseSelection = new MenuSelection();
        private readonly SettingsPanel settings = new SettingsPanel();
        private readonly System.Collections.Generic.List<VisualElement> revSegments = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.List<VisualElement> startLights = new System.Collections.Generic.List<VisualElement>();
        private string shownCountdown = string.Empty;
        private RaceMinimap minimap;
        private Label gearLabel;
        private Label deltaLabel;
        private VisualElement startLightRow;
        private Label pauseSummary;
        private HintBar pauseHints;
        private HintBar resultsHints;
        private VehicleAudio playerAudio;
        private Racing.RaceParticipant playerAudioOwner;
        private float deltaTimeLeft;
        private int shownRevs = -1;
        private int shownGear = int.MinValue;
        private float timeScaleBeforePause = 1f;

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

        /// <summary>True while the race is paused with the pause menu open.</summary>
        public bool Paused { get; private set; }

        /// <summary>The pause menu's buttons (resume, restart, main menu) and which one is highlighted.</summary>
        public MenuSelection PauseSelection => pauseSelection;

        /// <summary>The settings screen opened from the pause menu.</summary>
        public SettingsPanel Settings => settings;

        public void Configure(RaceManager newRace, PlayerRaceEntry newPlayerEntry = null)
        {
            race = newRace;
            playerEntry = newPlayerEntry;
        }

        /// <summary>Starts the race again from the grid. Used by the results and pause buttons and the restart input.</summary>
        public void RestartRace()
        {
            Resume();
            if (race != null)
                race.Restart();
        }

        /// <summary>Leaves the race for the main menu, if the game has one.</summary>
        public void GoToMainMenu()
        {
            Resume();
            GameScenes.Load(GameScenes.MainMenu);
        }

        /// <summary>
        /// Stops time and opens the pause menu. Does nothing once the results are up (the race is over) or when already
        /// paused. Returns true if the race was paused.
        /// </summary>
        public bool Pause()
        {
            if (Paused || resultsVisible)
                return false;

            Paused = true;
            GameAudio.Play(UiCue.Confirm);
            timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            input.EnableMenu();
            if (Bind())
            {
                SetVisible(pauseOverlay, true);
                pauseSelection.Select(0);
                if (pauseSummary != null)
                    pauseSummary.text = DescribeStanding();
                if (pauseOverlay != null)
                    Pop(pauseOverlay, "pause--enter");
            }

            return true;
        }

        /// <summary>Closes the pause menu and lets time run again at the speed it ran before.</summary>
        public void Resume()
        {
            if (!Paused)
                return;

            Paused = false;
            if (settings.IsOpen)
                settings.Close();
            Time.timeScale = timeScaleBeforePause;
            AudioListener.pause = false;
            input.DisableMenu();
            if (pauseOverlay != null)
                SetVisible(pauseOverlay, false);
        }

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
            // Never leave the game frozen, e.g. when the scene is unloaded while paused.
            Resume();
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
            else if (Paused)
                UpdatePauseMenu();
            else if (input.Pause.WasPressedThisFrame())
            {
                if (!resultsVisible)
                    Pause();
                else if (GameScenes.CanLoad(GameScenes.MainMenu))
                    GoToMainMenu();
            }

            if (race == null || !Bind())
                return;

            var player = race.Player;
            var tracker = player != null ? player.Tracker : null;
            boundRoot.style.display = tracker != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (tracker == null)
                return;

            UpdateStats(player, tracker);
            UpdateTelemetry(player);
            minimap?.Update(race, Time.unscaledDeltaTime);
            UpdateCentreMessages(tracker);

            if (!resultsVisible && playerFinishedAt >= 0f && Time.time - playerFinishedAt >= race.Settings.ResultsDelay)
                ShowResults();
        }

        private void UpdatePauseMenu()
        {
            if (settings.IsOpen)
            {
                settings.HandleInput(input);
                return;
            }

            // Esc is both the pause and the back input, so one press resumes once.
            if (input.Pause.WasPressedThisFrame() || input.Back.WasPressedThisFrame())
            {
                GameAudio.Play(UiCue.Back);
                Resume();
                return;
            }

            if (input.MenuUp.WasPressedThisFrame())
                pauseSelection.Move(-1);
            if (input.MenuDown.WasPressedThisFrame())
                pauseSelection.Move(1);
            if (input.Submit.WasPressedThisFrame())
                pauseSelection.Submit();
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
            string countdown = number > 0 ? number.ToString() : race.ShowingGo ? "GO!" : string.Empty;
            if (countdown != shownCountdown)
            {
                shownCountdown = countdown;
                countdownLabel.text = countdown;
                countdownLabel.EnableInClassList("countdown--go", number == 0 && countdown.Length > 0);
                if (countdown.Length > 0)
                    Pop(countdownLabel, "countdown--pop");
                ShowStartLights(number, number == 0 && countdown.Length > 0);
            }

            if (deltaTimeLeft > 0f)
            {
                deltaTimeLeft -= Time.deltaTime;
                if (deltaTimeLeft <= 0f && deltaLabel != null)
                    deltaLabel.AddToClassList("lap-delta--hidden");
            }

            SetVisible(wrongWayLabel, tracker.IsWrongWay);

            if (messageTimeLeft > 0f)
            {
                messageTimeLeft -= Time.deltaTime;
                if (messageTimeLeft <= 0f)
                    ClearMessage();
            }
        }

        private void ClearMessage()
        {
            if (messageLabel == null)
                return;

            messageLabel.text = string.Empty;
            messageLabel.RemoveFromClassList("centre-message--banner");
        }

        private void ShowMessage(string text, bool banner = false)
        {
            messageTimeLeft = messageDuration;
            if (messageLabel == null)
                return;

            messageLabel.text = text;
            messageLabel.EnableInClassList("centre-message--banner", banner);
            Pop(messageLabel, "centre-message--pop");
        }

        /// <summary>
        /// Punches an element in: a style class holding its entry state (bigger, clear) is applied now and removed on the
        /// next frame, so the element eases from it into its normal look (the transitions are in RaceHud.uss).
        /// </summary>
        private static void Pop(VisualElement element, string entryClass)
        {
            element.AddToClassList(entryClass);
            element.schedule.Execute(() => element.RemoveFromClassList(entryClass));
        }

        private void OnRaceRestarted()
        {
            playerFinishedAt = -1f;
            messageTimeLeft = 0f;
            ClearMessage();
            SetResultsVisible(false);
            deltaTimeLeft = 0f;
            deltaLabel?.AddToClassList("lap-delta--hidden");
            PlayEntrance();
        }

        private void OnLapCompleted(RaceParticipant participant, int lap, float lapTime)
        {
            if (participant != race.Player || participant.Tracker.Finished)
                return;

            int next = lap + 1;
            ShowMessage(next == race.Laps ? "FINAL LAP" : $"LAP {next}/{race.Laps}");
            ShowLapDelta(participant.Tracker, lapTime);
        }

        /// <summary>The lap just done and how it compares with the best before it: green if faster, red if slower.</summary>
        private void ShowLapDelta(RaceTracker tracker, float lapTime)
        {
            if (deltaLabel == null)
                return;

            float bestBefore = float.MaxValue;
            var laps = tracker.LapTimes;
            for (int i = 0; i < laps.Count - 1; i++)
                bestBefore = Mathf.Min(bestBefore, laps[i]);

            string delta = string.Empty;
            bool faster = true;
            if (bestBefore < float.MaxValue)
            {
                float difference = lapTime - bestBefore;
                faster = difference < 0f;
                delta = $"   {(faster ? "-" : "+")}{Mathf.Abs(difference):0.000}";
            }

            deltaLabel.text = $"LAP  {RaceTime.Format(lapTime)}{delta}";
            deltaLabel.EnableInClassList("lap-delta--faster", faster && delta.Length > 0);
            deltaLabel.EnableInClassList("lap-delta--slower", !faster);
            deltaLabel.RemoveFromClassList("lap-delta--hidden");
            Pop(deltaLabel, "lap-delta--enter");
            deltaTimeLeft = DeltaDuration;
        }

        /// <summary>The rev bar and gear, from the player's car's sound-only engine.</summary>
        private void UpdateTelemetry(RaceParticipant player)
        {
            if (gearLabel == null && revSegments.Count == 0)
                return;

            if (player != playerAudioOwner)
            {
                playerAudioOwner = player;
                playerAudio = player.Vehicle.GetComponent<VehicleAudio>();
            }

            var engine = playerAudio != null && playerAudio.IsReady ? playerAudio.Engine : null;
            var config = playerAudio != null ? playerAudio.Config : null;
            float share = engine != null && config != null ? Mathf.InverseLerp(config.IdleRpm, config.RedlineRpm, engine.Rpm) : 0f;
            int lit = Mathf.RoundToInt(share * revSegments.Count);
            if (lit != shownRevs)
            {
                shownRevs = lit;
                for (int i = 0; i < revSegments.Count; i++)
                    revSegments[i].EnableInClassList("rev-seg--on", i < lit);
            }

            int gear = engine != null ? engine.Gear : 0;
            if (gearLabel != null && gear != shownGear)
            {
                shownGear = gear;
                gearLabel.text = gear < 0 ? "R" : gear == 0 || player.Vehicle.SpeedKmh < 1f && gear == 1 ? "N" : gear.ToString();
                Pop(gearLabel, "gear--change");
            }
        }

        /// <summary>Start lights: one more red lamp for each of 3, 2, 1, all green at GO, off otherwise.</summary>
        private void ShowStartLights(int number, bool go)
        {
            if (startLightRow == null)
                return;

            bool counting = race.State == RaceState.Countdown || go;
            startLightRow.EnableInClassList("start-lights--hidden", !counting);
            int lit = number > 0 ? race.Settings.CountdownFrom - number + 1 : 0;
            for (int i = 0; i < startLights.Count; i++)
            {
                startLights[i].EnableInClassList("start-light--red", !go && i < lit);
                startLights[i].EnableInClassList("start-light--go", go);
            }
        }

        /// <summary>The HUD's panels slide in, one after another, for each start.</summary>
        private void PlayEntrance()
        {
            if (boundRoot == null)
                return;

            long delay = 0;
            foreach (var panel in boundRoot.Query(className: "hud-panel").ToList())
            {
                panel.AddToClassList("hud-panel--enter");
                var shown = panel;
                panel.schedule.Execute(() => shown.RemoveFromClassList("hud-panel--enter")).StartingIn(150 + delay);
                delay += 90;
            }
        }

        /// <summary>The player's place, lap and time, e.g. "P2 / 4     LAP 2 / 3     1:23.456".</summary>
        private string DescribeStanding()
        {
            var player = race != null ? race.Player : null;
            var tracker = player != null ? player.Tracker : null;
            if (tracker == null)
                return string.Empty;

            return $"P{player.Position} / {race.Participants.Count}     LAP {Mathf.Min(tracker.CurrentLap, race.Laps)} / {race.Laps}     " +
                   RaceTime.Format(tracker.Finished ? tracker.FinishTime : race.RaceTime);
        }

        private void OnParticipantFinished(RaceParticipant participant)
        {
            if (participant == race.Player)
            {
                playerFinishedAt = Time.time;
                ShowMessage("FINISH!", banner: true);
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
            for (int place = 1; place <= 3; place++)
                resultsPlace.EnableInClassList($"results-place--p{place}", player.Position == place);
            resultsSummary.text = $"TIME  {RaceTime.Format(tracker.FinishTime)}        BEST LAP  {RaceTime.Format(tracker.BestLapTime)}";

            var laps = new System.Text.StringBuilder();
            for (int i = 0; i < tracker.LapTimes.Count; i++)
                laps.Append(i == 0 ? string.Empty : "      ").Append("LAP ").Append(i + 1).Append("  ").Append(RaceTime.Format(tracker.LapTimes[i]));
            resultsLaps.text = laps.ToString();

            FillResultsTable();
            FillRewards(playerEntry != null ? playerEntry.LastReward : null);
            ClearMessage();
            SetResultsVisible(true);
            restartButton.Focus();
            RevealResults();
        }

        /// <summary>The results fade up, the card settles into place and its rows follow one after another.</summary>
        private void RevealResults()
        {
            GameAudio.Play(RaceCue.Results);
            Pop(results, "results--enter");
            Pop(resultsPlace, "results-place--enter");
            int index = 0;
            foreach (var row in resultsTable.Children())
            {
                row.AddToClassList("results-row--enter");
                var shown = row;
                row.schedule.Execute(() => shown.RemoveFromClassList("results-row--enter")).StartingIn(200 + ResultsRowStagger * index++);
            }
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
            resultsTable.Add(ResultsRow("POS", "DRIVER", "BEST LAP", "TIME", "GAP", "results-row--header"));

            // Gaps are to the winner's time.
            float winnerTime = -1f;
            foreach (var participant in race.Standings)
            {
                if (participant.Tracker.Finished && (winnerTime < 0f || participant.Tracker.FinishTime < winnerTime))
                    winnerTime = participant.Tracker.FinishTime;
            }

            foreach (var participant in race.Standings)
            {
                var tracker = participant.Tracker;
                string time = tracker.Finished ? RaceTime.Format(tracker.FinishTime) : "--";
                string best = tracker.BestLapTime > 0f ? RaceTime.Format(tracker.BestLapTime) : "--";
                string gap = !tracker.Finished ? "--" : Mathf.Approximately(tracker.FinishTime, winnerTime) ? "WINNER" : $"+{tracker.FinishTime - winnerTime:0.000}";
                string rowClass = participant.IsPlayer ? "results-row--player" : null;
                var row = ResultsRow(participant.Position.ToString(), participant.DisplayName, best, time, gap, rowClass);
                if (participant.Position <= 3)
                    row.AddToClassList($"results-row--p{participant.Position}");
                resultsTable.Add(row);
            }
        }

        private static VisualElement ResultsRow(string position, string driver, string best, string time, string gap, string extraClass)
        {
            var row = new VisualElement();
            row.AddToClassList("results-row");
            if (extraClass != null)
                row.AddToClassList(extraClass);
            row.Add(Cell(position, "results-cell--position"));
            row.Add(Cell(driver, "results-cell--driver"));
            row.Add(Cell(best, "results-cell--best"));
            row.Add(Cell(time, "results-cell--time"));
            row.Add(Cell(gap, "results-cell--gap"));
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

            // Optional: the pause menu. Main menu is only offered when the game has one.
            pauseOverlay = root.Q("pause");
            pauseTrack = root.Q<Label>("pause-track");
            var resume = root.Q<Button>("pause-resume");
            var restart = root.Q<Button>("pause-restart");
            var openSettings = root.Q<Button>("pause-settings");
            var leave = root.Q<Button>("pause-menu");
            pauseSelection = new MenuSelection();
            if (pauseOverlay != null && resume != null && restart != null && leave != null)
            {
                pauseSelection.Add(resume, Resume);
                pauseSelection.Add(restart, RestartRace);

                // Optional: only offered when the layout has the settings screen.
                if (openSettings != null && settings.Bind(root))
                    pauseSelection.Add(openSettings, settings.Open);
                else if (openSettings != null)
                    SetVisible(openSettings, false);
                pauseSelection.Add(leave, GoToMainMenu);
                leave.SetEnabled(GameScenes.CanLoad(GameScenes.MainMenu));
                if (pauseTrack != null)
                    pauseTrack.text = DescribeRace();
                SetVisible(pauseOverlay, Paused);
            }
            else
            {
                pauseOverlay = null;
            }

            BindExtras(root);
            shownPosition = shownRacers = shownLap = shownSpeed = -1;
            shownRevs = -1;
            shownGear = int.MinValue;
            shownCountdown = string.Empty;
            ClearMessage();
            SetVisible(results, resultsVisible);
            return true;
        }

        /// <summary>The HUD's optional parts: minimap, rev bar and gear, lap delta, start lights, pause summary and hints.</summary>
        private void BindExtras(VisualElement root)
        {
            var mapHolder = root.Q("minimap");
            minimap = mapHolder != null ? new RaceMinimap(mapHolder) : null;
            gearLabel = root.Q<Label>("gear");
            deltaLabel = root.Q<Label>("lap-delta");
            deltaLabel?.AddToClassList("lap-delta--hidden");
            pauseSummary = root.Q<Label>("pause-summary");

            revSegments.Clear();
            var revBar = root.Q("rev-bar");
            if (revBar != null)
            {
                revBar.Clear();
                for (int i = 0; i < RevSegments; i++)
                {
                    var segment = new VisualElement { pickingMode = PickingMode.Ignore };
                    segment.AddToClassList("rev-seg");
                    if (i >= RevSegments - RedSegments)
                        segment.AddToClassList("rev-seg--red");
                    revBar.Add(segment);
                    revSegments.Add(segment);
                }
            }

            startLights.Clear();
            startLightRow = root.Q("start-lights");
            if (startLightRow != null)
                startLights.AddRange(startLightRow.Query(className: "start-light").ToList());

            var pauseKeys = root.Q("pause-hints");
            pauseHints = pauseKeys != null ? new HintBar(pauseKeys) : null;
            pauseHints?.Show((HintAction.Choose, "CHOOSE"), (HintAction.Confirm, "SELECT"), (HintAction.Back, "RESUME"));
            var resultKeys = root.Q("results-hints");
            resultsHints = resultKeys != null ? new HintBar(resultKeys) : null;
            resultsHints?.Show((HintAction.Restart, "RESTART"), (HintAction.Pause, "MAIN MENU"));
        }

        /// <summary>The track and race length, e.g. "PINE VALLEY  |  3 LAPS".</summary>
        private string DescribeRace()
        {
            var track = playerEntry != null ? playerEntry.Track : null;
            string laps = race != null ? $"{race.Laps} {(race.Laps == 1 ? "LAP" : "LAPS")}" : string.Empty;
            return track != null ? $"{track.DisplayName.ToUpperInvariant()}  |  {laps}" : laps;
        }
    }
}
