using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RallyX.Player;
using RallyX.Racing;
using RallyX.Tracks;
using RallyX.UI;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RallyX.Tests
{
    /// <summary>
    /// The race system on the real Pine Valley scene with the real car: countdown, checkpoints and laps, the finish
    /// and results, wrong-way warnings, restarts and positions, checked through both the race state and the HUD.
    /// The AI opponents are taken out so the race rules are checked on their own (AIRaceTests covers them).
    /// </summary>
    public sealed class RaceSystemTests : VehicleTestBase
    {
        private const string SceneName = "PineValley";
        private const float RaceTimeout = 480f;

        private Scene scene;
        private RaceManager race;
        private RaceHud hud;
        private RaceParticipant player;
        private TrackPath path;

        [UnitySetUp]
        public IEnumerator LoadRace()
        {
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(SceneName),
                $"{SceneName} is not in the build scene list. Run RALLY X > Build > Rebuild Pine Valley Track.");
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByName(SceneName);

            race = Object.FindFirstObjectByType<RaceManager>();
            hud = Object.FindFirstObjectByType<RaceHud>();
            Assert.IsNotNull(race, "The track scene has no RaceManager.");
            Assert.IsNotNull(hud, "The track scene has no RaceHud.");
            yield return null;

            player = race.Player;
            Assert.IsNotNull(player, "The race has no player racer.");
            Car = player.Vehicle;
            path = race.Circuit.Path;

            // The tests drive the car themselves.
            var playerInput = Car.GetComponent<PlayerVehicleInput>();
            if (playerInput != null)
                playerInput.enabled = false;

            RemoveOpponents(race);
            race.Restart();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnloadRace()
        {
            Car = null;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator Circuit_GatesRunFromTheStartLineRoundTheWholeLap()
        {
            var circuit = race.Circuit;
            Assert.IsNull(circuit.FindProblem());
            Assert.GreaterOrEqual(circuit.CheckpointCount, 8);
            Assert.Less(Vector3.Distance(Flat(circuit.GetCheckpoint(0).transform.position), Flat(path.GetPoint(0))), 0.5f,
                "The first gate should be the start/finish line.");

            var gates = new List<string>();
            float largestGap = 0f;
            for (int i = 0; i < circuit.CheckpointCount; i++)
            {
                var gate = circuit.GetCheckpoint(i);
                float distance = circuit.CheckpointDistance(i);
                float next = i + 1 < circuit.CheckpointCount ? circuit.CheckpointDistance(i + 1) : circuit.LapLength;
                largestGap = Mathf.Max(largestGap, next - distance);
                float roadWidth = path.EvaluateWidth(path.FindClosestDistance(gate.transform.position));
                gates.Add($"{distance:0}");
                Assert.Greater(gate.Width, roadWidth + 2f, $"Gate {i} should reach past both road edges.");
            }

            TestContext.WriteLine($"{circuit.CheckpointCount} gates at {string.Join(", ", gates)} m; largest gap {largestGap:0} m");
            Assert.Less(largestGap, 300f);
            yield break;
        }

        [UnityTest]
        public IEnumerator Countdown_HoldsTheCarOnTheGridThenStartsTheClockAtGo()
        {
            // Real time, so no frame is long enough to skip a countdown number on screen.
            Time.timeScale = 1f;
            Vector3 start = Car.Body.position;
            var numbersShown = new List<string>();
            float furthestMoved = 0f;

            // Full throttle all through the countdown.
            Car.SetInput(FullThrottle);
            while (race.State == RaceState.Countdown)
            {
                Assert.AreEqual(0f, race.RaceTime, "The clock must not run before GO.");
                Assert.IsTrue(Car.InputLocked);
                furthestMoved = Mathf.Max(furthestMoved, Vector3.Distance(Flat(Car.Body.position), Flat(start)));
                string shown = HudText("countdown");
                if (shown.Length > 0 && (numbersShown.Count == 0 || numbersShown[numbersShown.Count - 1] != shown))
                    numbersShown.Add(shown);
                yield return null;
            }

            yield return null;
            string atStart = HudText("countdown");
            TestContext.WriteLine($"Countdown shown: {string.Join(" ", numbersShown)} {atStart}; car moved {furthestMoved:0.00} m while held");
            CollectionAssert.AreEqual(new[] { "3", "2", "1" }, numbersShown);
            Assert.AreEqual("GO!", atStart);
            Assert.Less(furthestMoved, 0.3f, "The car crept forward on the grid.");
            Assert.IsFalse(Car.InputLocked);

            float clockBefore = race.RaceTime;
            yield return Drive(FullThrottle, 2f);
            float moved = Vector3.Distance(Flat(Car.Body.position), Flat(start));
            TestContext.WriteLine($"2 s of driving after GO: moved {moved:0.0} m, race clock advanced {race.RaceTime - clockBefore:0.00} s");
            Assert.Greater(moved, 5f, "The car should be released at GO.");
            Assert.AreEqual(2f, race.RaceTime - clockBefore, 0.05f, "The race clock should run in step with the simulation.");
        }

        [UnityTest]
        public IEnumerator ThreeLapRace_FinishesWithLapTimesAndShowsTheResults()
        {
            yield return WaitForGo();
            var tracker = player.Tracker;
            var driver = new ScriptedDriver(Car, path);
            var lapsShown = new List<string>();
            var lapEvents = new List<int>();
            int speedChecks = 0;
            int speedMismatches = 0;

            void OnLap(RaceParticipant racer, int lap, float time)
            {
                if (racer == player)
                    lapEvents.Add(lap);
            }

            race.LapCompleted += OnLap;
            try
            {
                while (!tracker.Finished)
                {
                    driver.Steer();
                    yield return NextPhysicsStep;
                    Assert.Less(race.RaceTime, RaceTimeout, $"No finish after {RaceTimeout} s: lap {tracker.CurrentLap}, gate {tracker.NextCheckpoint}.");
                    Assert.IsFalse(tracker.IsWrongWay, $"Wrong-way warning while driving the right way at {tracker.TrackDistance:0} m.");

                    string lap = HudText("lap");
                    if (lapsShown.Count == 0 || lapsShown[lapsShown.Count - 1] != lap)
                        lapsShown.Add(lap);
                    if (int.TryParse(HudText("speed"), out int shownSpeed))
                    {
                        speedChecks++;
                        // The HUD updates once per frame, several physics steps apart at the test's time scale.
                        if (Mathf.Abs(shownSpeed - Car.SpeedKmh) > 12f)
                            speedMismatches++;
                    }
                }
            }
            finally
            {
                race.LapCompleted -= OnLap;
            }

            float lapSum = 0f;
            foreach (float lapTime in tracker.LapTimes)
                lapSum += lapTime;
            TestContext.WriteLine($"Finished in {RaceTime.Format(tracker.FinishTime)}; laps {string.Join(", ", FormatAll(tracker.LapTimes))}; " +
                                  $"best {RaceTime.Format(tracker.BestLapTime)}; HUD laps {string.Join(" ", lapsShown)}");

            Assert.AreEqual(3, tracker.CompletedLaps);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, lapEvents);
            Assert.AreEqual(lapSum, tracker.FinishTime, 0.01f, "Lap times should add up to the race time.");
            Assert.AreEqual(1, player.Position);
            Assert.AreEqual(RaceState.Finished, race.State);
            Assert.IsTrue(Car.InputLocked, "A finished car is taken out of the driver's hands.");
            CollectionAssert.AreEqual(new[] { "1/3", "2/3", "3/3" }, lapsShown);
            Assert.Less(speedMismatches, speedChecks / 20 + 1, "The HUD speed should follow the car.");

            // The results appear after the configured delay.
            Assert.IsFalse(hud.ResultsVisible, "Results should wait for the finish delay.");
            yield return new WaitForSeconds(race.Settings.ResultsDelay + 0.5f);
            Assert.IsTrue(hud.ResultsVisible);
            Assert.IsTrue(IsShown("results"));
            Assert.AreEqual("1st", HudText("results-place"));
            Assert.AreEqual(RaceTime.Format(tracker.FinishTime), HudText("timer"), "The HUD clock stops at the finish time.");
            StringAssert.Contains(RaceTime.Format(tracker.FinishTime), HudText("results-summary"));
            var rows = Hud.Q("results-table").Query<VisualElement>(className: "results-row").ToList();
            Assert.AreEqual(2, rows.Count, "A header row and one row per racer.");
            StringAssert.Contains("You", rows[1].Q<Label>(className: "results-cell--driver").text);

            // Out of the driver's hands, the finished car brakes to a stop on the road.
            for (float waited = 0f; Car.SpeedKmh > 1f && waited < 15f; waited += Time.fixedDeltaTime)
                yield return NextPhysicsStep;
            float stoppedAt = path.FindClosestDistance(Car.Body.position);
            float offRoad = Flat(Car.Body.position - path.EvaluatePosition(stoppedAt)).magnitude - path.EvaluateWidth(stoppedAt) * 0.5f;
            TestContext.WriteLine($"Finished car stopped {stoppedAt:0} m past the line, {offRoad:+0.0;-0.0} m from the road edge");
            Assert.Less(Car.SpeedKmh, 1f, "The finished car should come to a stop.");
            Assert.Less(offRoad, 0f, "The finished car should stop on the road.");

            // The results screen's button restarts the race (as Enter / Submit on the focused button does).
            var button = Hud.Q<Button>("restart-button");
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }

            yield return null;
            Assert.AreEqual(RaceState.Countdown, race.State, "The restart button should restart the race.");
            Assert.IsFalse(hud.ResultsVisible);
        }

        [UnityTest]
        public IEnumerator WrongWay_WarnsWhenDrivingBackAlongTheTrackButNotWhenReversing()
        {
            yield return WaitForGo();
            var tracker = player.Tracker;
            int warnings = 0;

            void OnWrongWay(RaceParticipant racer, bool wrongWay)
            {
                if (racer == player && wrongWay)
                    warnings++;
            }

            // On the start straight, facing back towards the grid.
            const float spot = 90f;
            Vector3 point = path.EvaluatePosition(spot) + Vector3.up * 0.3f;
            Vector3 forward = Flat(path.EvaluatePosition(spot + 1f) - path.EvaluatePosition(spot - 1f)).normalized;
            var cruise = new VehicleInputState(0.6f, 0f, 0f, false);

            race.WrongWayChanged += OnWrongWay;
            try
            {
                Car.PlaceAt(point, Quaternion.LookRotation(-forward));
                float shownAfter = -1f;
                float elapsed = 0f;
                yield return Drive(cruise, 3f, () =>
                {
                    elapsed += Time.fixedDeltaTime;
                    if (shownAfter < 0f && tracker.IsWrongWay)
                        shownAfter = elapsed;
                });

                yield return null;
                TestContext.WriteLine($"Wrong way shown {shownAfter:0.00} s after driving off backwards (delay {race.Settings.WrongWayDelay} s)");
                Assert.IsTrue(tracker.IsWrongWay);
                Assert.IsTrue(IsShown("wrong-way"), "The HUD should show WRONG WAY.");
                Assert.GreaterOrEqual(shownAfter, race.Settings.WrongWayDelay);

                // Turned the right way round, the warning clears.
                Car.PlaceAt(point, Quaternion.LookRotation(forward));
                yield return Drive(cruise, 1f);
                yield return null;
                Assert.IsFalse(tracker.IsWrongWay);
                Assert.IsFalse(IsShown("wrong-way"));

                // Reversing back down the road to recover is not driving the wrong way.
                Car.PlaceAt(point, Quaternion.LookRotation(forward));
                yield return Drive(new VehicleInputState(0f, 1f, 0f, false), 3f);
                TestContext.WriteLine($"Reversed at {Car.ForwardSpeed * 3.6f:0} km/h without a warning");
                Assert.Less(Car.ForwardSpeed, -2f, "The car should have been reversing.");
                Assert.IsFalse(tracker.IsWrongWay);
                Assert.AreEqual(1, warnings);
            }
            finally
            {
                race.WrongWayChanged -= OnWrongWay;
            }
        }

        [UnityTest]
        public IEnumerator Restart_PutsTheCarBackOnTheGridAndResetsTheRace()
        {
            yield return WaitForGo();
            var tracker = player.Tracker;
            var driver = new ScriptedDriver(Car, path);
            while (tracker.CheckpointsThisLap < 2)
            {
                driver.Steer();
                yield return NextPhysicsStep;
                Assert.Less(race.RaceTime, 60f, "The car should reach the second gate.");
            }

            float clockBefore = race.RaceTime;
            int restarts = 0;
            void OnRestart() => restarts++;

            race.RaceRestarted += OnRestart;
            try
            {
                hud.RestartRace();
                yield return null;
            }
            finally
            {
                race.RaceRestarted -= OnRestart;
            }

            var pole = race.Circuit.Grid.GetSlot(0);
            TestContext.WriteLine($"Restarted after {clockBefore:0.0} s; car {Vector3.Distance(Flat(Car.Body.position), Flat(pole.position)):0.00} m from pole");
            Assert.AreEqual(1, restarts);
            Assert.AreEqual(RaceState.Countdown, race.State);
            Assert.AreEqual(0f, race.RaceTime);
            Assert.AreEqual(0, tracker.CompletedLaps);
            Assert.AreEqual(0, tracker.CheckpointsThisLap);
            Assert.IsTrue(Car.InputLocked);
            Assert.Less(Vector3.Distance(Flat(Car.Body.position), Flat(pole.position)), 0.5f, "The car should be back on pole.");
            Assert.Less(Car.Body.linearVelocity.magnitude, 1f);
            Assert.AreEqual("1/3", HudText("lap"));
            Assert.AreEqual("0:00.000", HudText("timer"));
            Assert.IsFalse(hud.ResultsVisible);

            // And the new race runs.
            yield return WaitForGo();
            yield return Drive(FullThrottle, 1.5f);
            Assert.Greater(Car.SpeedKmh, 20f);
        }

        [UnityTest]
        public IEnumerator Positions_TwoCarsAreRankedByProgressAlongTheTrack()
        {
            var rivalObject = Track(Object.Instantiate(LoadAsset<GameObject>(CarPrefabPath)));
            rivalObject.name = "RivalCar";
            var rival = rivalObject.AddComponent<RaceParticipant>();
            rival.Configure("Rival", false);
            race.AddParticipant(rival);

            // The test is about ranking, so the cars pass through each other rather than crash.
            foreach (var mine in Car.GetComponentsInChildren<Collider>())
            {
                foreach (var theirs in rivalObject.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(mine, theirs);
            }

            race.Restart();
            yield return WaitForGo();
            Assert.AreEqual(1, player.Position, "Pole position leads at the start.");
            Assert.AreEqual(2, rival.Position);
            yield return null;
            Assert.AreEqual("1/2", HudText("position"));

            // The rival starts behind but is much faster, so it passes the player.
            var playerDriver = new ScriptedDriver(Car, path, topSpeed: 13f);
            var rivalDriver = new ScriptedDriver(rival.Vehicle, path);
            float playerProgress = Unwrap(playerDriver.Distance);
            float rivalProgress = Unwrap(rivalDriver.Distance);
            float overtakenAt = -1f;
            int wrongOrders = 0;
            int checks = 0;

            for (float elapsed = 0f; elapsed < 40f; elapsed += Time.fixedDeltaTime)
            {
                float playerBefore = playerDriver.Distance;
                float rivalBefore = rivalDriver.Distance;
                playerDriver.Steer();
                rivalDriver.Steer();
                yield return NextPhysicsStep;
                playerProgress += Advance(playerBefore, playerDriver.Distance);
                rivalProgress += Advance(rivalBefore, rivalDriver.Distance);

                // Wherever one car is clearly further along the road, it must be the one ranked ahead.
                if (Mathf.Abs(playerProgress - rivalProgress) > 3f)
                {
                    checks++;
                    var expectedLeader = playerProgress > rivalProgress ? player : rival;
                    if (race.Standings[0] != expectedLeader)
                        wrongOrders++;
                }

                if (overtakenAt < 0f && rival.Position == 1)
                    overtakenAt = elapsed;
            }

            yield return null;
            TestContext.WriteLine($"Rival took the lead {overtakenAt:0.0} s after GO; after 40 s the rival is " +
                                  $"{rivalProgress - playerProgress:0} m ahead along the road; {wrongOrders} wrong orders in {checks} checks");
            Assert.Greater(overtakenAt, 0f, "The faster rival should take the lead.");
            Assert.AreEqual(0, wrongOrders, "Positions must follow progress along the track.");
            Assert.AreEqual(1, rival.Position);
            Assert.AreEqual(2, player.Position);
            Assert.AreEqual("2/2", HudText("position"));
        }

        private IEnumerator WaitForGo()
        {
            while (race.State == RaceState.Countdown)
                yield return NextPhysicsStep;
        }

        private float Unwrap(float distance) => distance > path.Length * 0.5f ? distance - path.Length : distance;

        private float Advance(float from, float to) =>
            Mathf.Repeat(to - from + path.Length * 0.5f, path.Length) - path.Length * 0.5f;

        private VisualElement Hud => hud.GetComponent<UIDocument>().rootVisualElement;

        private string HudText(string name) => Hud.Q<Label>(name).text;

        private bool IsShown(string name) => Hud.Q(name).style.display.value != DisplayStyle.None;

        private static IEnumerable<string> FormatAll(IReadOnlyList<float> times)
        {
            foreach (float time in times)
                yield return RaceTime.Format(time);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
