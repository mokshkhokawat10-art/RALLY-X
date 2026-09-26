using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RallyX.Garage;
using RallyX.Player;
using RallyX.Progression;
using RallyX.Racing;
using RallyX.SaveSystem;
using RallyX.Tracks;
using RallyX.UI;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RallyX.Tests
{
    /// <summary>
    /// Every garage car driven for real: it accelerates and brakes as its garage stats say, stays on its wheels at
    /// full lock, and laps Pine Valley when chosen. Also that finishing a race first is recorded and unlocks the
    /// locked car. Measured values are printed for tuning.
    /// </summary>
    public sealed class GarageCarTests : VehicleTestBase
    {
        private const string RosterPath = "Assets/Settings/Vehicles/VehicleRoster.asset";
        private const string ProgressionSettingsPath = "Assets/Settings/Progression/ProgressionSettings.asset";
        private const string TrackScene = "PineValley";

        private Scene trackScene;

        /// <summary>Ids of the roster's cars, for parameterised tests.</summary>
        public static IEnumerable<string> CarIds()
        {
#if UNITY_EDITOR
            var roster = AssetDatabase.LoadAssetAtPath<VehicleRoster>(RosterPath);
            if (roster != null)
            {
                foreach (var vehicle in roster.Vehicles)
                    yield return vehicle.Id;
                yield break;
            }
#endif
            yield return "missing-roster";
        }

        [UnityTearDown]
        public IEnumerator UnloadTrack()
        {
            if (trackScene.IsValid() && trackScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(trackScene);
        }

        [UnityTest]
        public IEnumerator Car_AcceleratesReachesTopSpeedAndBrakesAsItsStatsSay([ValueSource(nameof(CarIds))] string id)
        {
            var vehicle = Find(id);
            var stats = vehicle.MeasureStats();
            yield return CreateCourse(null, SpawnPosition, Quaternion.identity, settle: true, carPrefab: vehicle.Prefab.gameObject);

            float elapsed = 0f;
            float timeTo100 = -1f;
            float fastest = 0f;
            yield return Drive(FullThrottle, 25f, () =>
            {
                elapsed += Time.fixedDeltaTime;
                fastest = Mathf.Max(fastest, Car.SpeedKmh);
                if (timeTo100 < 0f && Car.SpeedKmh >= 100f)
                    timeTo100 = elapsed;
            });

            // Full braking, measured from the moment the car is back down to 100 km/h.
            Vector3? from100 = null;
            float stopped = -1f;
            Car.SetInput(new VehicleInputState(0f, 1f, 0f, false));
            for (float t = 0f; t < 15f && stopped < 0f; t += Time.fixedDeltaTime)
            {
                yield return NextPhysicsStep;
                if (from100 == null && Car.SpeedKmh <= 100f)
                    from100 = Car.Body.position;
                if (from100 != null && Car.Body.linearVelocity.magnitude < 0.3f)
                    stopped = Vector3.Distance(Car.Body.position, from100.Value);
            }

            TestContext.WriteLine($"{vehicle.DisplayName}: 0-100 {timeTo100:0.00} s (stat {stats.ZeroToHundredSeconds:0.00}), top {fastest:0.0} km/h " +
                                  $"(stat {stats.TopSpeedKmh:0}), 100-0 {stopped:0.0} m (stat {stats.BrakingDistance:0.0})");
            Assert.AreEqual(stats.ZeroToHundredSeconds, timeTo100, stats.ZeroToHundredSeconds * 0.1f, "0-100 km/h should match the garage.");
            Assert.LessOrEqual(fastest, stats.TopSpeedKmh * 1.02f, "Top speed limit exceeded.");
            Assert.GreaterOrEqual(fastest, stats.TopSpeedKmh * 0.9f, "The car should get close to its top speed.");
            Assert.AreEqual(stats.BrakingDistance, stopped, stats.BrakingDistance * 0.1f, "Braking distance should match the garage.");
        }

        [UnityTest]
        public IEnumerator Car_StaysOnItsWheelsAtFullLockAt80([ValueSource(nameof(CarIds))] string id)
        {
            var vehicle = Find(id);
            yield return CreateCourse(null, SpawnPosition, Quaternion.identity, settle: true, carPrefab: vehicle.Prefab.gameObject);
            yield return AccelerateTo(80f);

            float lowest = 1f;
            float turned = 0f;
            Vector3 startHeading = Heading();
            yield return Drive(new VehicleInputState(0.5f, 0f, 1f, false), 4f, () =>
            {
                lowest = Mathf.Min(lowest, Uprightness());
                turned = Mathf.Max(turned, Vector3.Angle(startHeading, Heading()));
            });

            TestContext.WriteLine($"{vehicle.DisplayName}: lowest uprightness {lowest:0.00}, turned {turned:0} deg");
            Assert.Greater(lowest, 0.7f, $"The {vehicle.DisplayName} should not roll over.");
            Assert.Greater(turned, 90f, $"The {vehicle.DisplayName} should turn.");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator ChosenCar_LapsPineValley([ValueSource(nameof(CarIds))] string id)
        {
            var vehicle = Find(id);
            var profile = PlayerProfile.Current;
            profile.Unlock(id);
            profile.SelectVehicle(id);
            yield return LoadTrack();

            var entry = Object.FindFirstObjectByType<PlayerRaceEntry>();
            Assert.AreSame(vehicle, entry.Vehicle, "The track should spawn the chosen car.");
            Assert.IsNull(PlayerProfile.Current.FilePath, "Tests must use a profile kept in memory.");

            // Free driving on an empty track.
            var race = Object.FindFirstObjectByType<RaceManager>();
            RemoveOpponents(race);
            race.enabled = false;
            Car = entry.Car;
            Car.GetComponent<PlayerVehicleInput>().enabled = false;
            yield return Drive(default, SettleSeconds);

            var path = race.Circuit.Path;
            var driver = new ScriptedDriver(Car, path);
            float distance = driver.Distance;
            float progress = 0f;
            float elapsed = 0f;
            float lowest = 1f;
            while (progress < path.Length)
            {
                driver.Steer();
                yield return NextPhysicsStep;
                elapsed += Time.fixedDeltaTime;
                float next = path.FindClosestDistance(Car.Body.position, distance, 30f);
                progress += Mathf.Repeat(next - distance + path.Length * 0.5f, path.Length) - path.Length * 0.5f;
                distance = next;
                lowest = Mathf.Min(lowest, Uprightness());
                Assert.Less(elapsed, 200f, $"The {vehicle.DisplayName} did not finish the lap ({progress:0} of {path.Length:0} m).");
            }

            TestContext.WriteLine($"{vehicle.DisplayName}: lap in {elapsed:0.0} s, lowest uprightness {lowest:0.00}");
            Assert.Greater(lowest, 0.5f, $"The {vehicle.DisplayName} should stay on its wheels.");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator WinningARace_IsRecordedAndUnlocksTheLockedCar()
        {
            var roster = Roster();
            VehicleDefinition locked = null;
            foreach (var vehicle in roster.Vehicles)
            {
                if (vehicle.WinsToUnlock == 1)
                    locked = vehicle;
            }

            Assert.IsNotNull(locked, "Expected a car unlocked by one win.");
            var profile = PlayerProfile.Current;
            Assert.IsFalse(locked.IsUnlocked(profile));
            yield return LoadTrack();
            var settings = LoadAsset<ProgressionSettings>(ProgressionSettingsPath);
            int creditsBefore = profile.Credits;
            Assert.AreEqual(settings.StartingCredits, creditsBefore, "A new career starts with its credits.");

            // A race alone: the player wins it.
            var race = Object.FindFirstObjectByType<RaceManager>();
            RemoveOpponents(race);
            var entry = Object.FindFirstObjectByType<PlayerRaceEntry>();
            Car = entry.Car;
            Car.GetComponent<PlayerVehicleInput>().enabled = false;
            race.Restart();
            var driver = new ScriptedDriver(Car, race.Circuit.Path);
            while (!race.Player.Tracker.Finished)
            {
                driver.Steer();
                yield return NextPhysicsStep;
                Assert.Less(race.RaceTime, 400f, "The race should finish.");
            }

            for (int frame = 0; entry.LastReward == null; frame++)
            {
                Assert.Less(frame, 10, "The race should be paid straight after the finish.");
                yield return null;
            }

            var reward = entry.LastReward;
            var tracker = race.Player.Tracker;
            TestContext.WriteLine($"Finished {race.Player.Position} in {RaceTime.Format(tracker.FinishTime)}, best lap {RaceTime.Format(tracker.BestLapTime)}; " +
                                  $"+{reward.CreditsEarned} credits ({profile.Credits} total); profile: {profile.Wins} wins, {profile.RacesFinished} races");
            Assert.AreEqual(1, profile.RacesFinished, "The finished race is recorded once.");
            Assert.AreEqual(1, profile.Wins);
            Assert.IsTrue(locked.IsUnlocked(profile), $"Winning unlocks the {locked.DisplayName}.");
            CollectionAssert.Contains(reward.UnlockedVehicles, locked.DisplayName);

            // Paid for the position, with the best lap kept as the track's record.
            Assert.AreEqual(1, reward.Position);
            Assert.AreEqual(settings.CreditsFor(1), reward.CreditsEarned);
            Assert.AreEqual(creditsBefore + settings.CreditsFor(1), profile.Credits);
            Assert.AreEqual(profile.Credits, reward.TotalCredits);
            Assert.AreEqual(tracker.FinishTime, reward.RaceTime);
            Assert.AreEqual(tracker.BestLapTime, reward.BestLap);
            Assert.IsTrue(reward.NewLapRecord, "The first race on a track sets its lap record.");
            var record = profile.GetTrackRecord(entry.Track.Id);
            Assert.AreEqual(tracker.BestLapTime, record.BestLap, 1e-4f);
            Assert.AreEqual(entry.Vehicle.Id, record.BestLapVehicle);
            Assert.AreEqual(1, record.BestPosition);

            // The results screen: position, race time, best lap, credits earned and the new total.
            var hud = Object.FindFirstObjectByType<RaceHud>();
            for (float start = Time.time; !hud.ResultsVisible;)
            {
                Assert.Less(Time.time - start, 10f, "The results screen should open.");
                yield return null;
            }

            var root = hud.GetComponent<UIDocument>().rootVisualElement;
            string summary = Text(root, "results-summary");
            TestContext.WriteLine($"Results: {Text(root, "results-place")} | {summary} | earned {Text(root, "results-credits")} | " +
                                  $"total {Text(root, "results-balance")} | {Text(root, "results-news")}");
            Assert.AreEqual("1st", Text(root, "results-place"));
            StringAssert.Contains($"TIME  {RaceTime.Format(tracker.FinishTime)}", summary);
            StringAssert.Contains($"BEST LAP  {RaceTime.Format(tracker.BestLapTime)}", summary);
            Assert.AreEqual(DisplayStyle.Flex, root.Q("results-rewards").style.display.value, "Credits are shown with the results.");
            Assert.AreEqual("+" + Credits.Format(settings.CreditsFor(1)), Text(root, "results-credits"));
            Assert.AreEqual(Credits.Format(profile.Credits), Text(root, "results-balance"));
            StringAssert.Contains("NEW TRACK RECORD", Text(root, "results-news"));
            StringAssert.Contains($"{locked.DisplayName.ToUpperInvariant()} UNLOCKED", Text(root, "results-news"));
        }

        private static string Text(VisualElement root, string labelName) => root.Q<Label>(labelName).text;

        private IEnumerator LoadTrack()
        {
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(TrackScene), $"{TrackScene} is not in the build scene list.");
            yield return SceneManager.LoadSceneAsync(TrackScene, LoadSceneMode.Additive);
            trackScene = SceneManager.GetSceneByName(TrackScene);
            yield return null;
        }

        private static VehicleRoster Roster() => LoadAsset<VehicleRoster>(RosterPath);

        private static VehicleDefinition Find(string id)
        {
            var vehicle = Roster().Find(id);
            Assert.IsNotNull(vehicle, $"No car '{id}' in {RosterPath}. Run RALLY X > Build > Create Missing Garage Content.");
            return vehicle;
        }
    }
}
