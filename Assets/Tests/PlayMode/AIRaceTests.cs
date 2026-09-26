using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RallyX.AI;
using RallyX.Core;
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
    /// AI opponents on the real Pine Valley scene: grid and start, full races on every difficulty with positions
    /// checked throughout, pace by difficulty, passing, recovery and shortcuts. Measured values are printed for
    /// tuning; the checks allow for normal racing incidents.
    /// </summary>
    public sealed class AIRaceTests : VehicleTestBase
    {
        private const string SceneName = "PineValley";

        // Contact closer than this to another car's centre counts as hitting that car, metres.
        private const float CarContactRadius = 3.5f;

        private Scene scene;
        private RaceManager race;
        private AIOpponents opponents;
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
            opponents = Object.FindFirstObjectByType<AIOpponents>();
            Assert.IsNotNull(race, "The track scene has no RaceManager.");
            Assert.IsNotNull(opponents, "The track scene has no AIOpponents.");
            yield return null;

            player = race.Player;
            Car = player.Vehicle;
            path = race.Circuit.Path;
            Car.GetComponent<PlayerVehicleInput>().enabled = false;
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
        public IEnumerator Opponents_LineUpBehindThePlayerAndWaitForGo()
        {
            var drivers = opponents.Drivers;
            Assert.AreEqual(3, drivers.Count, "Three AI opponents.");
            Assert.AreEqual(4, race.Participants.Count);
            Assert.AreEqual(0, player.GridSlot, "The player starts on pole.");

            var grid = race.Circuit.Grid;
            var names = new HashSet<string>();
            var paints = new HashSet<Color>();
            var starts = new Dictionary<AIDriver, Vector3>();
            foreach (var driver in drivers)
            {
                var racer = driver.GetComponent<RaceParticipant>();
                var livery = driver.GetComponent<VehicleLivery>();
                Assert.IsTrue(names.Add(racer.DisplayName), "Opponents need different names.");
                Assert.IsNotNull(livery.Paint, $"{racer.DisplayName} should be painted.");
                Assert.IsTrue(paints.Add(livery.Paint.Value), "Opponents need different colours.");
                Assert.AreNotEqual(0, racer.GridSlot);
                Assert.Less(Vector3.Distance(Flat(driver.transform.position), Flat(grid.GetSlot(racer.GridSlot).position)), 1f,
                    $"{racer.DisplayName} should be on grid slot {racer.GridSlot + 1}.");
                starts[driver] = driver.transform.position;
            }

            TestContext.WriteLine($"Grid: {string.Join(", ", GridOrder())}");

            float crept = 0f;
            while (race.State == RaceState.Countdown)
            {
                foreach (var driver in drivers)
                    crept = Mathf.Max(crept, Vector3.Distance(Flat(driver.transform.position), Flat(starts[driver])));
                yield return NextPhysicsStep;
            }

            Assert.Less(crept, 0.3f, "AI cars must wait for GO.");
            yield return null;
            StringAssert.EndsWith("/4", HudText("position"));

            yield return Drive(default, 4f);
            foreach (var driver in drivers)
            {
                float moved = Vector3.Distance(Flat(driver.transform.position), Flat(starts[driver]));
                TestContext.WriteLine($"{driver.Profile.DisplayName}: {moved:0} m in the first 4 s");
                Assert.Greater(moved, 25f, $"{driver.Profile.DisplayName} should race off at GO.");
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator AIField_RacesThreeLapsCleanlyWithCorrectPositions([Values] AIDifficulty difficulty)
        {
            opponents.Difficulty = difficulty;
            var drivers = opponents.Drivers;
            var stats = new Dictionary<AIDriver, DriverStats>();
            foreach (var driver in drivers)
            {
                var entry = new DriverStats(driver, race);
                stats[driver] = entry;
                driver.GetComponent<VehicleController>().Impact += entry.OnImpact;
            }

            // The player is a steady moving car the field has to get past.
            var playerDriver = new ScriptedDriver(Car, path, topSpeed: 19f);
            var shortcut = Object.FindFirstObjectByType<TrackBranch>();
            var previousPositions = new Dictionary<RaceParticipant, int>();
            int positionChanges = 0;
            yield return WaitForGo();
            try
            {
                while (!AllFinished(drivers))
                {
                    if (!player.Tracker.Finished)
                        playerDriver.Steer();
                    yield return NextPhysicsStep;
                    foreach (var entry in stats.Values)
                        entry.Sample(path, shortcut);

                    string problem = StandingsProblem();
                    Assert.IsNull(problem, $"Standings wrong at {race.RaceTime:0.00} s: {problem}");
                    foreach (var participant in race.Participants)
                    {
                        if (previousPositions.TryGetValue(participant, out int previous) && previous != participant.Position)
                            positionChanges++;
                        previousPositions[participant] = participant.Position;
                    }

                    Assert.Less(race.RaceTime, 420f, $"Not every AI finished: {string.Join("; ", Summaries(stats))}");
                }
            }
            finally
            {
                foreach (var entry in stats.Values)
                    entry.Driver.GetComponent<VehicleController>().Impact -= entry.OnImpact;
            }

            TestContext.WriteLine($"Difficulty {difficulty}");
            foreach (var line in Summaries(stats))
                TestContext.WriteLine(line);
            TestContext.WriteLine($"Finishing order: {string.Join(", ", FinishOrder())}; position changes during the race: {positionChanges}");

            float fastestLap = float.MaxValue;
            var finishers = new List<RaceParticipant>();
            foreach (var entry in stats.Values)
            {
                var tracker = entry.Tracker;
                Assert.AreEqual(3, tracker.CompletedLaps);
                Assert.AreEqual(3, tracker.LapTimes.Count);
                Assert.AreEqual(tracker.FinishTime, tracker.LapTimes[0] + tracker.LapTimes[1] + tracker.LapTimes[2], 0.01f,
                    $"{entry.Name}'s lap times should add up to the finish time.");
                Assert.IsTrue(entry.Driver.GetComponent<VehicleController>().InputLocked, $"{entry.Name} should be held once finished.");
                Assert.LessOrEqual(entry.Driver.Respawns, 1, $"{entry.Name} needed putting back on the track too often.");
                Assert.Less(entry.FurthestOffRoad, 6f, $"{entry.Name} went too far off the road.");
                fastestLap = Mathf.Min(fastestLap, tracker.BestLapTime);
                finishers.Add(entry.Driver.GetComponent<RaceParticipant>());
            }

            // Finishers are placed in the order they crossed the line, ahead of the player still racing.
            finishers.Sort((a, b) => a.Tracker.FinishTime.CompareTo(b.Tracker.FinishTime));
            for (int i = 0; i < finishers.Count; i++)
                Assert.AreEqual(i + 1, finishers[i].Position, $"{finishers[i].DisplayName} finished {RaceTime.Ordinal(i + 1)}.");
            Assert.AreEqual(4, player.Position, "The slow player should be last.");
            yield return null;
            Assert.AreEqual("4/4", HudText("position"), "The HUD should show the player's position.");

            Assert.Less(fastestLap, 76f, "The quickest AI should lap faster than the cautious test driver (76-78 s).");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Difficulty_SetsThePaceFromEasyToHard()
        {
            var driver = KeepOnlyOpponent(0);
            var vehicle = driver.GetComponent<VehicleController>();
            var tracker = driver.GetComponent<RaceParticipant>().Tracker;
            var lapTimes = new Dictionary<AIDifficulty, float>();
            foreach (var difficulty in new[] { AIDifficulty.Easy, AIDifficulty.Normal, AIDifficulty.Hard })
            {
                opponents.Difficulty = difficulty;
                race.Restart();
                yield return WaitForGo();

                float topSpeed = 0f;
                while (tracker.CompletedLaps < 1)
                {
                    yield return NextPhysicsStep;
                    topSpeed = Mathf.Max(topSpeed, vehicle.SpeedKmh);
                    Assert.Less(race.RaceTime, 150f, $"{driver.Profile.DisplayName} should complete a lap on {difficulty}.");
                }

                lapTimes[difficulty] = tracker.LapTimes[0];
                TestContext.WriteLine($"{difficulty}: {driver.Profile.DisplayName} lap {RaceTime.Format(tracker.LapTimes[0])}, top {topSpeed:0} km/h, " +
                                      $"respawns {driver.Respawns}, reversing {driver.ReverseManoeuvres}");
                Assert.AreEqual(0, driver.Respawns, $"{driver.Profile.DisplayName} should lap cleanly on {difficulty}.");
                Assert.LessOrEqual(topSpeed, vehicle.Config.MaxSpeedKmh + 3f, "No difficulty lets the car beat its own top speed.");
            }

            Assert.Greater(lapTimes[AIDifficulty.Easy], lapTimes[AIDifficulty.Normal] * 1.04f, "Easy should be clearly slower than Normal.");
            Assert.Greater(lapTimes[AIDifficulty.Normal], lapTimes[AIDifficulty.Hard] * 1.02f, "Hard should be clearly faster than Normal.");
        }

        [UnityTest]
        public IEnumerator AI_DrivesAroundACarStoppedOnItsLine()
        {
            var driver = KeepOnlyOpponent(0);
            var vehicle = driver.GetComponent<VehicleController>();
            yield return WaitForGo();

            // The player's car parked on the start straight, right on the AI's racing line.
            var route = driver.ActiveRoute;
            float parkedAt = route.FindClosest(path.EvaluatePosition(170f));
            Car.PlaceAt(route.Evaluate(parkedAt) + Vector3.up * 0.3f, Quaternion.LookRotation(route.DirectionAt(parkedAt)));
            Car.SetInput(new VehicleInputState(0f, 0f, 0f, true));

            int contacts = 0;
            void OnImpact(float impulse, Vector3 point)
            {
                if (Vector3.Distance(point, Car.Body.position) < CarContactRadius)
                    contacts++;
            }

            vehicle.Impact += OnImpact;
            float closest = float.MaxValue;
            float passedAt = -1f;
            float fastest = 0f;
            try
            {
                for (float elapsed = 0f; elapsed < 25f && passedAt < 0f; elapsed += Time.fixedDeltaTime)
                {
                    yield return NextPhysicsStep;
                    float along = route.Separation(parkedAt, route.FindClosest(vehicle.Body.position));
                    fastest = Mathf.Max(fastest, vehicle.SpeedKmh);
                    if (Mathf.Abs(along) < 5f)
                        closest = Mathf.Min(closest, Vector3.Distance(Flat(vehicle.Body.position), Flat(Car.Body.position)));
                    if (along > 25f)
                        passedAt = elapsed;
                }
            }
            finally
            {
                vehicle.Impact -= OnImpact;
            }

            TestContext.WriteLine($"{driver.Profile.DisplayName} passed the parked car {passedAt:0.0} s after GO at up to {fastest:0} km/h; " +
                                  $"closest {closest:0.0} m centre to centre; contacts {contacts}");
            Assert.Greater(passedAt, 0f, "The AI should get past the parked car.");
            Assert.AreEqual(0, contacts, "The AI should not hit the parked car.");
            Assert.Greater(closest, 2f, "The AI should leave a car's width.");
        }

        [UnityTest]
        public IEnumerator AI_RecoversFromRollingOverAndFromBeingLostInTheTrees()
        {
            var driver = KeepOnlyOpponent(1);
            var vehicle = driver.GetComponent<VehicleController>();
            var tracker = driver.GetComponent<RaceParticipant>().Tracker;
            yield return WaitForGo();
            yield return Drive(default, 6f);

            // Upside down on the road.
            vehicle.PlaceAt(vehicle.Body.position + Vector3.up * 1.5f, vehicle.Body.rotation * Quaternion.Euler(0f, 0f, 180f));
            float recoveredAfter = -1f;
            for (float t = 0f; t < 8f && recoveredAfter < 0f; t += Time.fixedDeltaTime)
            {
                yield return NextPhysicsStep;
                if (driver.Respawns >= 1 && Uprightness(vehicle) > 0.95f)
                    recoveredAfter = t;
            }

            TestContext.WriteLine($"Rolled over: back on its wheels on the track after {recoveredAfter:0.0} s");
            Assert.Greater(recoveredAfter, 0f, "A rolled-over AI should be put back on the track.");

            // Dropped among the trees, well away from the road.
            float distance = path.FindClosestDistance(vehicle.Body.position);
            Vector3 roadPoint = path.EvaluatePosition(distance);
            Vector3 right = Vector3.Cross(Vector3.up, path.EvaluatePosition(distance + 1f) - path.EvaluatePosition(distance - 1f)).normalized;
            Vector3 lost = roadPoint + right * 30f;
            lost.y = Terrain.activeTerrain.SampleHeight(lost) + 1f;
            vehicle.PlaceAt(lost, vehicle.Body.rotation);
            float raceDistanceBefore = tracker.RaceDistance;

            float backAfter = -1f;
            for (float t = 0f; t < 20f && backAfter < 0f; t += Time.fixedDeltaTime)
            {
                yield return NextPhysicsStep;
                if (OffRoad(vehicle.Body.position) < 0f && vehicle.SpeedKmh > 20f)
                    backAfter = t;
            }

            yield return Drive(default, 10f);
            TestContext.WriteLine($"Lost in the trees: racing on the road again after {backAfter:0.0} s; " +
                                  $"respawns {driver.Respawns}, reversing {driver.ReverseManoeuvres}; " +
                                  $"{tracker.RaceDistance - raceDistanceBefore:0} m of race distance covered 10 s later");
            Assert.Greater(backAfter, 0f, "A lost AI should get back to racing on the road.");
            Assert.Greater(tracker.RaceDistance - raceDistanceBefore, 100f, "It should carry on racing afterwards.");
        }

        [UnityTest]
        public IEnumerator AI_ReversesOutOfADeadEnd()
        {
            const float carHalfWidth = 0.9f;
            const float carHalfLength = 2.1f;
            const float sideGap = 0.4f;
            const float noseGap = 0.5f;
            const float wallThickness = 0.5f;

            var driver = KeepOnlyOpponent(1);
            var vehicle = driver.GetComponent<VehicleController>();
            var tracker = driver.GetComponent<RaceParticipant>().Tracker;
            yield return WaitForGo();
            yield return Drive(default, 6f);

            // Parked nose first in a walled pocket just off the road, at right angles to it, level with where it was
            // so no gate is skipped. The pocket leaves the road clear. Driving forwards gets nowhere: it has to back out.
            float distance = path.FindClosestDistance(vehicle.Body.position);
            Vector3 along = Flat(path.EvaluatePosition(distance + 1f) - path.EvaluatePosition(distance - 1f)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, along);
            float fromCentre = path.EvaluateWidth(distance) * 0.5f + 0.5f + carHalfLength;
            Vector3 carPoint = GroundBelow(path.EvaluatePosition(distance) + right * fromCentre);
            var facing = Quaternion.LookRotation(right);
            float pocketWidth = 2f * (carHalfWidth + sideGap);
            float pocketDepth = 2f * carHalfLength + noseGap;
            float backWall = carHalfLength + noseGap + wallThickness * 0.5f;
            float sideWall = carHalfWidth + sideGap + wallThickness * 0.5f;
            Wall(carPoint + right * backWall, facing, new Vector3(pocketWidth + 2f * wallThickness, 3f, wallThickness));
            Wall(carPoint + along * sideWall + right * (noseGap * 0.5f), facing, new Vector3(wallThickness, 3f, pocketDepth));
            Wall(carPoint - along * sideWall + right * (noseGap * 0.5f), facing, new Vector3(wallThickness, 3f, pocketDepth));
            vehicle.PlaceAt(carPoint + Vector3.up * 0.5f, facing);
            float raceDistanceBefore = tracker.RaceDistance;

            float racingAfter = -1f;
            for (float t = 0f; t < 20f && racingAfter < 0f; t += Time.fixedDeltaTime)
            {
                yield return NextPhysicsStep;
                if (OffRoad(vehicle.Body.position) < 0f && vehicle.SpeedKmh > 30f
                    && Vector3.Angle(Flat(vehicle.Body.rotation * Vector3.forward), along) < 45f)
                    racingAfter = t;
            }

            yield return Drive(default, 10f);
            TestContext.WriteLine($"Dead end: racing again after {racingAfter:0.0} s; reversing {driver.ReverseManoeuvres}, " +
                                  $"respawns {driver.Respawns}; {tracker.RaceDistance - raceDistanceBefore:0} m of race distance covered 10 s later");
            Assert.GreaterOrEqual(driver.ReverseManoeuvres, 1, "A stuck AI should reverse out.");
            Assert.AreEqual(0, driver.Respawns, "Backing out of a dead end shouldn't need the car putting back on the track.");
            Assert.Greater(racingAfter, 0f, "The AI should get out of the dead end and race on.");
            Assert.Greater(tracker.RaceDistance - raceDistanceBefore, 100f, "It should carry on racing afterwards.");
        }

        [UnityTest]
        public IEnumerator AI_TakesTheShortcutWhenItChoosesToAndTheLapStillCounts()
        {
            var driver = KeepOnlyOpponent(2);
            var branch = Object.FindFirstObjectByType<TrackBranch>();
            Assert.IsNotNull(branch, "The track has no shortcut branch.");
            var alwaysShortcut = AIDriverProfile.Create("Shortcutter", Color.magenta, 7f, 9f, 0.9f, 0f, 1f);
            driver.Configure(race, opponents.Settings, alwaysShortcut, opponents.Routes, opponents.Difficulty);
            var tracker = driver.GetComponent<RaceParticipant>().Tracker;
            Vector3 branchMiddle = branch.GetPoint(branch.Count / 2);
            yield return WaitForGo();

            float closestToBranch = float.MaxValue;
            while (tracker.CompletedLaps < 1)
            {
                yield return NextPhysicsStep;
                closestToBranch = Mathf.Min(closestToBranch, Vector3.Distance(Flat(driver.transform.position), Flat(branchMiddle)));
                Assert.Less(race.RaceTime, 150f, "The lap should be completed.");
            }

            TestContext.WriteLine($"Shortcut lap {RaceTime.Format(tracker.LapTimes[0])}; passed {closestToBranch:0.0} m from the shortcut's middle; " +
                                  $"branches taken {driver.BranchesTaken}");
            Object.Destroy(alwaysShortcut);
            Assert.AreEqual(1, driver.BranchesTaken);
            Assert.Less(closestToBranch, 3f, "The AI should drive through the shortcut.");
        }

        private AIDriver KeepOnlyOpponent(int index)
        {
            var keep = opponents.Drivers[index];
            foreach (var participant in new List<RaceParticipant>(race.Participants))
            {
                if (participant.IsPlayer || participant.gameObject == keep.gameObject)
                    continue;
                race.RemoveParticipant(participant);
                Object.Destroy(participant.gameObject);
            }

            race.Restart();
            return keep;
        }

        private IEnumerator WaitForGo()
        {
            while (race.State == RaceState.Countdown)
                yield return NextPhysicsStep;
        }

        private bool AllFinished(IReadOnlyList<AIDriver> drivers)
        {
            foreach (var driver in drivers)
            {
                if (!driver.GetComponent<RaceParticipant>().Tracker.Finished)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Why the standings are wrong, or null: positions numbered 1, 2, 3... down the list, finishers first in the
        /// order they finished, then everyone else by distance covered along the track.
        /// </summary>
        private string StandingsProblem()
        {
            var standings = race.Standings;
            for (int i = 0; i < standings.Count; i++)
            {
                var racer = standings[i];
                if (racer.Position != i + 1)
                    return $"{racer.DisplayName} is listed {i + 1} but has position {racer.Position}";
                if (i == 0)
                    continue;

                var ahead = standings[i - 1];
                if (racer.Tracker.Finished && (!ahead.Tracker.Finished || racer.Tracker.FinishTime < ahead.Tracker.FinishTime))
                    return $"{racer.DisplayName} finished before {ahead.DisplayName} but is behind them";
                if (!racer.Tracker.Finished && !ahead.Tracker.Finished && racer.Tracker.RaceDistance > ahead.Tracker.RaceDistance + 0.01f)
                    return $"{racer.DisplayName} ({racer.Tracker.RaceDistance:0.0} m) is placed behind {ahead.DisplayName} ({ahead.Tracker.RaceDistance:0.0} m)";
            }

            return null;
        }

        private GameObject Wall(Vector3 groundPoint, Quaternion rotation, Vector3 size)
        {
            var wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.name = "TestWall";
            wall.transform.SetPositionAndRotation(groundPoint + Vector3.up * (size.y * 0.5f - 1f), rotation);
            wall.transform.localScale = size;
            return wall;
        }

        /// <summary>The road or ground under a point, ignoring cars.</summary>
        private static Vector3 GroundBelow(Vector3 point)
        {
            int vehicleLayer = LayerMask.NameToLayer(GameLayers.Vehicle);
            bool hit = Physics.Raycast(point + Vector3.up * 5f, Vector3.down, out var ground, 20f, ~(1 << vehicleLayer), QueryTriggerInteraction.Ignore);
            Assert.IsTrue(hit, "No ground under the test point.");
            return ground.point;
        }

        private IEnumerable<string> GridOrder()
        {
            foreach (var participant in race.Participants)
                yield return $"{participant.GridSlot + 1}. {participant.DisplayName}";
        }

        private IEnumerable<string> FinishOrder()
        {
            foreach (var participant in race.Standings)
            {
                var tracker = participant.Tracker;
                yield return $"{participant.Position}. {participant.DisplayName} " +
                             (tracker.Finished ? RaceTime.Format(tracker.FinishTime) : $"lap {tracker.CurrentLap}");
            }
        }

        private static IEnumerable<string> Summaries(Dictionary<AIDriver, DriverStats> stats)
        {
            foreach (var entry in stats.Values)
                yield return entry.Summary();
        }

        /// <summary>Distance beyond the road edge (negative = on the road), metres.</summary>
        private float OffRoad(Vector3 position)
        {
            float distance = path.FindClosestDistance(position);
            return Flat(position - path.EvaluatePosition(distance)).magnitude - path.EvaluateWidth(distance) * 0.5f;
        }

        private static float Uprightness(VehicleController vehicle) => Vector3.Dot(vehicle.Body.rotation * Vector3.up, Vector3.up);

        private string HudText(string name) =>
            Object.FindFirstObjectByType<RaceHud>().GetComponent<UIDocument>().rootVisualElement.Q<Label>(name).text;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private sealed class DriverStats
        {
            private readonly RaceManager race;
            private readonly VehicleController vehicle;

            public DriverStats(AIDriver driver, RaceManager race)
            {
                Driver = driver;
                this.race = race;
                vehicle = driver.GetComponent<VehicleController>();
                Tracker = driver.GetComponent<RaceParticipant>().Tracker;
                Name = driver.Profile.DisplayName;
            }

            public AIDriver Driver { get; }
            public RaceTracker Tracker { get; }
            public string Name { get; }
            public float FurthestOffRoad { get; private set; } = float.MinValue;
            public float TopSpeed { get; private set; }
            public float LongestAir { get; private set; }
            public int CarContacts { get; private set; }
            public int SceneryContacts { get; private set; }

            public void Sample(TrackPath path, TrackBranch shortcut)
            {
                if (Tracker.Finished)
                    return;

                Vector3 position = vehicle.Body.position;
                TopSpeed = Mathf.Max(TopSpeed, vehicle.SpeedKmh);
                LongestAir = Mathf.Max(LongestAir, vehicle.AirborneTime);

                // Beyond the main road's edge doesn't count while on the shortcut, which the main path doesn't cover.
                float distance = path.FindClosestDistance(position);
                float offRoad = Flat(position - path.EvaluatePosition(distance)).magnitude - path.EvaluateWidth(distance) * 0.5f;
                if (offRoad > 0f && shortcut != null && OnBranch(position, shortcut))
                    offRoad = 0f;
                FurthestOffRoad = Mathf.Max(FurthestOffRoad, offRoad);
            }

            private static bool OnBranch(Vector3 position, TrackBranch branch)
            {
                for (int i = 0; i < branch.Count - 1; i++)
                {
                    Vector3 a = Flat(branch.GetPoint(i));
                    Vector3 ab = Flat(branch.GetPoint(i + 1)) - a;
                    float t = Mathf.Clamp01(Vector3.Dot(Flat(position) - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                    if (Vector3.Distance(Flat(position), a + ab * t) <= branch.GetWidth(i) * 0.5f)
                        return true;
                }

                return false;
            }

            public void OnImpact(float impulse, Vector3 point)
            {
                foreach (var other in race.Participants)
                {
                    if (other.Vehicle != vehicle && Vector3.Distance(point, other.Vehicle.Body.position) < CarContactRadius)
                    {
                        CarContacts++;
                        return;
                    }
                }

                SceneryContacts++;
            }

            public string Summary()
            {
                var laps = new List<string>();
                foreach (float lap in Tracker.LapTimes)
                    laps.Add(RaceTime.Format(lap));
                return $"{Name}: {(Tracker.Finished ? RaceTime.Format(Tracker.FinishTime) : "not finished")}, laps {string.Join(" ", laps)}, " +
                       $"top {TopSpeed:0} km/h, longest jump {LongestAir:0.00} s, furthest off road {FurthestOffRoad:+0.0;-0.0} m, " +
                       $"shortcuts {Driver.BranchesTaken}, respawns {Driver.Respawns}, reversing {Driver.ReverseManoeuvres}, " +
                       $"contacts: cars {CarContacts}, scenery {SceneryContacts}";
            }
        }
    }
}
