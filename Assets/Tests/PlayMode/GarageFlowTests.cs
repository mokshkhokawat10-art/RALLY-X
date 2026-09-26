using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using RallyX.Cameras;
using RallyX.Core;
using RallyX.Garage;
using RallyX.Player;
using RallyX.Progression;
using RallyX.Racing;
using RallyX.SaveSystem;
using RallyX.UI;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace RallyX.Tests
{
    /// <summary>
    /// The front end in the real scenes: the main menu, browsing and choosing cars in the garage (stats, locks,
    /// saving the choice), buying upgrades (preview, confirmation, not enough credits, saving) and starting a race in
    /// the chosen car. Rebuild the menus (RALLY X > Build > Rebuild Menu Scenes) before running these.
    /// </summary>
    public sealed class GarageFlowTests : VehicleTestBase
    {
        private static readonly VehicleStat[] AllStats = (VehicleStat[])Enum.GetValues(typeof(VehicleStat));
        private static int emptyScenes;

        [UnityTearDown]
        public IEnumerator UnloadScenes()
        {
            // The menus replace every open scene; leave a single empty one for the next test.
            var empty = SceneManager.CreateScene($"Empty{++emptyScenes}");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest]
        public IEnumerator MainMenu_ShowsYourCarTurningAndMovesBetweenItems()
        {
            yield return Open(GameScenes.MainMenu);
            var menu = Object.FindFirstObjectByType<MainMenu>();
            var showroom = Object.FindFirstObjectByType<VehicleShowroom>();
            var roster = LoadAsset<VehicleRoster>("Assets/Settings/Vehicles/VehicleRoster.asset");
            Assert.IsNotNull(menu);
            Assert.IsNotNull(showroom);

            Assert.AreSame(roster.Default, menu.Vehicle, "With nothing chosen the player races the default car.");
            Assert.AreSame(roster.Default, showroom.Shown);
            AssertDisplayOnly(showroom.Model);
            Assert.AreEqual(roster.Default.DisplayName.ToUpperInvariant(), Text(menu, "car-name"));
            var progression = LoadAsset<ProgressionSettings>("Assets/Settings/Progression/ProgressionSettings.asset");
            Assert.AreEqual(Credits.Format(progression.StartingCredits), menu.CreditsText, "A new career starts with credits.");
            Assert.AreEqual("PINE VALLEY", Text(menu, "track-name"));
            Assert.AreEqual("NO LAP RECORD YET", menu.TrackRecordText);

            Assert.AreEqual("race-button", menu.Selection.Selected.name);
            Assert.IsTrue(menu.Selection.Selected.enabledSelf, "RACE needs the track in the build.");
            menu.Selection.Move(1);
            Assert.AreEqual("garage-button", menu.Selection.Selected.name);
            menu.Selection.Move(1);
            menu.Selection.Move(1);
            Assert.AreEqual("quit-button", menu.Selection.Selected.name, "The highlight stops at the last item.");
            menu.Selection.Move(-2);
            Assert.AreEqual("race-button", menu.Selection.Selected.name);
            Assert.IsTrue(menu.Selection.Selected.ClassListContains(MenuSelection.SelectedClass));

            float yaw = showroom.Turntable.eulerAngles.y;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yaw, showroom.Turntable.eulerAngles.y)), 0.5f, "The car turns on the turntable.");

            menu.OpenGarage();
            yield return WaitForActiveScene(GameScenes.Garage);
            Assert.IsNotNull(Object.FindFirstObjectByType<GarageMenu>());
        }

        [UnityTest]
        public IEnumerator Garage_ShowsEachCarsStatsLocksAndSavesTheChoice()
        {
            yield return Open(GameScenes.Garage);
            var garage = Object.FindFirstObjectByType<GarageMenu>();
            var showroom = Object.FindFirstObjectByType<VehicleShowroom>();
            var roster = garage.Roster;
            var profile = PlayerProfile.Current;
            Assert.IsNull(profile.FilePath, "Tests must use a profile kept in memory.");

            // Opens on the car raced now.
            Assert.AreSame(roster.Default, garage.Current);
            Assert.AreEqual("SELECTED", garage.SelectButtonText);
            Assert.IsFalse(garage.SelectButtonEnabled);
            Assert.AreEqual("YOUR CAR", garage.StatusText);
            AssertStatsShown(garage, roster.Default);

            // Another unlocked car: shown, compared and chosen.
            garage.Next();
            var second = roster.Vehicles[1];
            Assert.AreSame(second, garage.Current);
            Assert.AreSame(second, showroom.Shown);
            Assert.IsFalse(showroom.ShownLocked);
            AssertDisplayOnly(showroom.Model);
            Assert.AreEqual(second.DisplayName.ToUpperInvariant(), garage.ShownName);
            Assert.AreEqual("SELECT", garage.SelectButtonText);
            Assert.IsTrue(garage.SelectButtonEnabled);
            AssertStatsShown(garage, second);
            TestContext.WriteLine($"{second.DisplayName}: {StatsLine(garage)}");

            Assert.IsTrue(garage.SelectCurrent());
            Assert.AreEqual(second.Id, profile.SelectedVehicleId, "Choosing a car saves it in the profile.");
            Assert.AreEqual("SELECTED", garage.SelectButtonText);
            Assert.AreEqual("YOUR CAR", garage.StatusText);

            // The locked car: a silhouette with what unlocks it, and it can't be chosen.
            var locked = FindLocked(roster);
            garage.ShowVehicle(roster.IndexOf(locked));
            Assert.IsTrue(showroom.ShownLocked);
            StringAssert.StartsWith($"WIN {locked.WinsToUnlock} RACE", garage.StatusText);
            Assert.AreEqual("LOCKED", garage.SelectButtonText);
            Assert.IsFalse(garage.SelectButtonEnabled);
            Assert.IsFalse(garage.SelectCurrent());
            Assert.AreEqual(second.Id, profile.SelectedVehicleId, "A locked car is never chosen.");
            TestContext.WriteLine($"{locked.DisplayName}: {garage.StatusText}; {StatsLine(garage)}");

            // Winning unlocks it while the garage is open.
            for (int i = 0; i < locked.WinsToUnlock; i++)
                profile.RecordRaceFinish(1);
            Assert.IsFalse(showroom.ShownLocked);
            Assert.AreEqual("SELECT", garage.SelectButtonText);
            Assert.IsTrue(garage.SelectButtonEnabled);

            // Browsing wraps round, and the car can be turned by hand.
            garage.ShowVehicle(roster.Count - 1);
            garage.Next();
            Assert.AreEqual(0, garage.Index);
            float yaw = showroom.Turntable.eulerAngles.y;
            showroom.Rotate(90f);
            Assert.AreEqual(Mathf.Repeat(yaw + 90f, 360f), showroom.Turntable.eulerAngles.y, 0.5f);

            // Back to the main menu, which now shows the chosen car.
            garage.Back();
            yield return WaitForActiveScene(GameScenes.MainMenu);
            yield return null;
            Assert.AreSame(second, Object.FindFirstObjectByType<MainMenu>().Vehicle);
        }

        [UnityTest]
        public IEnumerator Race_StartsTheTrackInTheChosenCarWithTheCameraOnIt()
        {
            var roster = LoadAsset<VehicleRoster>("Assets/Settings/Vehicles/VehicleRoster.asset");
            var chosen = roster.Vehicles[1];
            PlayerProfile.Current.SelectVehicle(chosen.Id);

            yield return Open(GameScenes.MainMenu);
            var menu = Object.FindFirstObjectByType<MainMenu>();
            menu.Race();
            yield return WaitForActiveScene(menu.RaceScene);
            yield return null;

            var entry = Object.FindFirstObjectByType<PlayerRaceEntry>();
            var race = Object.FindFirstObjectByType<RaceManager>();
            Assert.IsNotNull(entry, "The track needs a PlayerRaceEntry. Rebuild the track.");
            Assert.AreSame(chosen, entry.Vehicle);
            Assert.AreSame(chosen.Prefab.Config, entry.Car.Config, "The player races the chosen car's handling.");
            Assert.AreSame(entry.Car, race.Player.Vehicle);
            Assert.AreEqual(1, Object.FindObjectsByType<PlayerVehicleInput>(FindObjectsSortMode.None).Length, "Exactly one player car.");
            Assert.AreSame(entry.Car, Object.FindFirstObjectByType<ChaseCamera>().Target, "The camera follows the player's car.");
            var pole = race.Circuit.Grid.GetSlot(0);
            Assert.Less(Vector3.Distance(entry.Car.transform.position, pole.position), 1f, "The player starts on pole.");
            Assert.AreEqual(4, race.Participants.Count, "The AI field races too.");

            yield return null;
            var menuButton = Object.FindFirstObjectByType<RaceHud>().GetComponent<UIDocument>().rootVisualElement.Q<Button>("menu-button");
            Assert.AreEqual(DisplayStyle.Flex, menuButton.style.display.value, "Results offer a way back to the main menu.");
        }

        [UnityTest]
        public IEnumerator Garage_BuysUpgradesAfterConfirmingAndRefusesWhatItCantAfford()
        {
            // A real save file, to check purchases are kept.
            string file = Path.Combine(Path.GetTempPath(), $"rallyx-garage-test-{Guid.NewGuid():N}.json");
            PlayerProfile.Current = PlayerProfile.Load(file);
            try
            {
                yield return Open(GameScenes.Garage);
                var garage = Object.FindFirstObjectByType<GarageMenu>();
                var profile = PlayerProfile.Current;
                var falcon = garage.Current;
                var engine = garage.Catalog.Find(UpgradeType.Engine);
                int starting = profile.Credits;
                Assert.Greater(starting, engine.CostOf(1), "A new career can afford a first upgrade.");
                Assert.Less(starting, engine.CostOf(1) + engine.CostOf(2), "...but not the next stage straight after.");
                Assert.AreEqual(Credits.Format(starting), garage.CreditsText, "The garage shows the balance.");

                Assert.IsTrue(garage.OpenUpgrades());
                garage.SelectUpgrade(Array.IndexOf(ToArray(garage.Catalog), engine));
                Assert.AreEqual(0, garage.UpgradePipsLit(UpgradeType.Engine));
                Assert.AreEqual(Credits.Format(engine.CostOf(1)), garage.UpgradeCostText(UpgradeType.Engine));
                Assert.AreEqual($"BUY  {Credits.Format(engine.CostOf(1))}", garage.BuyButtonText);
                Assert.IsTrue(garage.BuyButtonEnabled);

                // The preview: acceleration improves with the next stage; the bar shows it before buying.
                float before = garage.StatRating(VehicleStat.Acceleration);
                Assert.Greater(garage.StatPreviewRating(VehicleStat.Acceleration), before + 0.01f);
                StringAssert.Contains("\u2192", garage.StatText(VehicleStat.Acceleration), "Current and upgraded figures are both shown.");
                Assert.AreEqual(garage.StatRating(VehicleStat.Braking), garage.StatPreviewRating(VehicleStat.Braking), "Only what the part changes.");
                TestContext.WriteLine($"Preview: ACCELERATION {garage.StatText(VehicleStat.Acceleration)}");

                // Buying asks first; cancelling spends nothing.
                Assert.IsTrue(garage.RequestPurchase());
                Assert.IsTrue(garage.ConfirmingPurchase);
                StringAssert.Contains("ENGINE", garage.ConfirmText);
                StringAssert.Contains(Credits.Format(engine.CostOf(1)), garage.ConfirmText);
                garage.CancelPurchase();
                Assert.AreEqual(starting, profile.Credits);
                Assert.AreEqual(0, profile.GetUpgradeLevel(falcon.Id, engine.Id));

                Assert.IsTrue(garage.RequestPurchase());
                Assert.IsTrue(garage.ConfirmPurchase());
                Assert.AreEqual(starting - engine.CostOf(1), profile.Credits);
                Assert.AreEqual(1, profile.GetUpgradeLevel(falcon.Id, engine.Id));
                Assert.AreEqual(1, garage.UpgradePipsLit(UpgradeType.Engine));
                Assert.AreEqual(Credits.Format(profile.Credits), garage.CreditsText);
                Assert.AreEqual("ENGINE STAGE 1 INSTALLED", garage.UpgradeStatusText);
                Assert.Greater(garage.StatRating(VehicleStat.Acceleration), before + 0.01f, "The bar now includes the upgrade.");

                // Not enough for stage 2: the button is off, asking explains why, and nothing is spent.
                Assert.IsFalse(garage.BuyButtonEnabled);
                Assert.IsFalse(garage.RequestPurchase());
                StringAssert.StartsWith("NOT ENOUGH CREDITS", garage.UpgradeStatusText);
                Assert.IsFalse(garage.ConfirmingPurchase);
                Assert.IsFalse(garage.ConfirmPurchase());
                Assert.AreEqual(starting - engine.CostOf(1), profile.Credits);
                TestContext.WriteLine($"Short of credits: {garage.UpgradeStatusText}");

                // Kept in the save file.
                var reloaded = PlayerProfile.Load(file);
                Assert.AreEqual(profile.Credits, reloaded.Credits);
                Assert.AreEqual(1, reloaded.GetUpgradeLevel(falcon.Id, engine.Id));

                // With the credits there, every stage can be bought, then it is maxed.
                profile.AddCredits(20000);
                while (garage.RequestPurchase())
                    Assert.IsTrue(garage.ConfirmPurchase());
                Assert.AreEqual(engine.MaxLevel, profile.GetUpgradeLevel(falcon.Id, engine.Id));
                Assert.AreEqual("MAXED", garage.BuyButtonText);
                Assert.AreEqual("MAXED", garage.UpgradeCostText(UpgradeType.Engine));
                Assert.AreEqual(engine.MaxLevel, garage.UpgradePipsLit(UpgradeType.Engine));

                // A locked car's parts can't be bought.
                garage.ShowVehicle(garage.Roster.IndexOf(FindLocked(garage.Roster)));
                Assert.AreEqual("LOCKED", garage.BuyButtonText);
                Assert.IsFalse(garage.RequestPurchase());

                // Back leaves the upgrades before it leaves the garage.
                garage.Back();
                Assert.IsFalse(garage.ShowingUpgrades);
                Assert.AreEqual(GameScenes.Garage, SceneManager.GetActiveScene().name);
            }
            finally
            {
                PlayerProfile.Current = PlayerProfile.InMemory();
                SaveFile.Delete(file);
            }
        }

        private static UpgradeDefinition[] ToArray(UpgradeCatalog catalog)
        {
            var upgrades = new UpgradeDefinition[catalog.Upgrades.Count];
            for (int i = 0; i < upgrades.Length; i++)
                upgrades[i] = catalog.Upgrades[i];
            return upgrades;
        }

        private static IEnumerator Open(string sceneName)
        {
            Assert.IsTrue(GameScenes.CanLoad(sceneName), $"{sceneName} is not in the build scene list. Run RALLY X > Build > Rebuild Menu Scenes.");
            SceneManager.LoadScene(sceneName);
            yield return WaitForActiveScene(sceneName);
            yield return null;
        }

        private static IEnumerator WaitForActiveScene(string sceneName)
        {
            for (int frame = 0; SceneManager.GetActiveScene().name != sceneName; frame++)
            {
                Assert.Less(frame, 600, $"{sceneName} did not load.");
                yield return null;
            }
        }

        private static void AssertStatsShown(GarageMenu garage, VehicleDefinition vehicle)
        {
            var stats = vehicle.MeasureStats();
            foreach (var stat in AllStats)
            {
                float rating = garage.Roster.Rate(stats, stat);
                Assert.AreEqual(rating, garage.StatRating(stat), 1e-4f, $"{vehicle.DisplayName}'s {stat} bar.");
                Assert.AreEqual(stats.Describe(stat, rating), garage.StatText(stat), $"{vehicle.DisplayName}'s {stat} figure.");
            }
        }

        private static string StatsLine(GarageMenu garage)
        {
            var parts = new string[AllStats.Length];
            for (int i = 0; i < AllStats.Length; i++)
                parts[i] = $"{VehicleStats.Label(AllStats[i])} {garage.StatText(AllStats[i])}";
            return string.Join(", ", parts);
        }

        private static void AssertDisplayOnly(GameObject model)
        {
            Assert.IsNotNull(model, "A car should be on the turntable.");
            Assert.IsFalse(model.GetComponent<VehicleController>().enabled, "The display car doesn't drive.");
            var body = model.GetComponent<Rigidbody>();
            Assert.IsTrue(body.isKinematic && !body.detectCollisions, "The display car has no physics.");
        }

        private static VehicleDefinition FindLocked(VehicleRoster roster)
        {
            foreach (var vehicle in roster.Vehicles)
            {
                if (vehicle.WinsToUnlock > 0)
                    return vehicle;
            }

            Assert.Fail("The roster has no locked car.");
            return null;
        }

        private static string Text(Component owner, string labelName) =>
            owner.GetComponent<UIDocument>().rootVisualElement.Q<Label>(labelName).text;
    }
}
