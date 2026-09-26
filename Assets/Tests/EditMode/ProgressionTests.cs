using System;
using System.Collections.Generic;
using NUnit.Framework;
using RallyX.EditorTools;
using RallyX.Garage;
using RallyX.Progression;
using RallyX.SaveSystem;
using RallyX.Vehicle;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RallyX.Tests
{
    /// <summary>
    /// Career rules on the project's data: upgrades change the handling they should (on a copy of the config), the shop
    /// refuses what can't be bought, races pay by position and unlock cars, and tracks unlock by requirement.
    /// </summary>
    public sealed class ProgressionTests
    {
        // The stat each upgrade is sold on.
        private static readonly Dictionary<UpgradeType, VehicleStat[]> Improves = new Dictionary<UpgradeType, VehicleStat[]>
        {
            [UpgradeType.Engine] = new[] { VehicleStat.Acceleration },
            [UpgradeType.Turbo] = new[] { VehicleStat.Acceleration, VehicleStat.TopSpeed },
            [UpgradeType.Tires] = new[] { VehicleStat.Grip },
            [UpgradeType.Brakes] = new[] { VehicleStat.Braking },
            [UpgradeType.Suspension] = new[] { VehicleStat.Handling },
            [UpgradeType.Gearbox] = new[] { VehicleStat.TopSpeed },
        };

        private static readonly VehicleStat[] AllStats = (VehicleStat[])Enum.GetValues(typeof(VehicleStat));

        private ProgressionSettings settings;
        private VehicleRoster roster;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void LoadContent()
        {
            settings = ProgressionContentBuilder.LoadSettings();
            roster = GarageContentBuilder.LoadRoster();
            Assert.IsNotNull(settings, $"Missing {ProgressionContentBuilder.SettingsPath}. Run RALLY X > Build > Create Missing Progression Content.");
            Assert.IsNotNull(roster);
        }

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var item in created)
                Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void Tuning_ChangesACopyOfTheConfigAndLeavesTheAssetAlone()
        {
            var config = roster.Default.Prefab.Config;
            float baseAcceleration = config.Acceleration;
            var tuning = new VehicleTuning(acceleration: 0.2f, highSpeedPull: 0.3f, topSpeed: 0.1f, grip: 0.12f, braking: 0.25f, steering: 0.2f,
                stability: 0.1f);
            var tuned = Keep(config.CreateTuned(tuning));

            Assert.AreNotSame(config, tuned);
            Assert.AreEqual(baseAcceleration, config.Acceleration, "The asset is never changed.");
            Assert.AreEqual(config.Acceleration * 1.2f, tuned.Acceleration, 1e-4f);
            Assert.AreEqual(config.MaxSpeedKmh * 1.1f, tuned.MaxSpeedKmh, 1e-3f);
            Assert.AreEqual(config.Downforce / (config.MaxSpeed * config.MaxSpeed), tuned.Downforce / (tuned.MaxSpeed * tuned.MaxSpeed), 1e-6f,
                "A higher top speed keeps the same downforce at every speed.");
            Assert.AreEqual(config.TyreFriction * 1.12f, tuned.TyreFriction, 1e-4f);
            Assert.AreEqual(config.BrakeDeceleration * 1.25f, tuned.BrakeDeceleration, 1e-4f);
            Assert.AreEqual(Mathf.Min(1f, config.AntiRoll + 0.1f), tuned.AntiRoll, 1e-4f);
            Assert.AreEqual(config.Mass, tuned.Mass, "Everything else is as built.");
            Assert.AreEqual(config.MaxSteerAngle, tuned.MaxSteerAngle);

            // Turbo pull: none at a standstill, most at speed, none at the top speed limit.
            Assert.AreEqual(config.AccelerationCurve.Evaluate(0f), tuned.AccelerationCurve.Evaluate(0f), 1e-4f);
            Assert.AreEqual(config.AccelerationCurve.Evaluate(0.6f) * 1.18f, tuned.AccelerationCurve.Evaluate(0.6f), 0.01f);
            Assert.AreEqual(0f, tuned.AccelerationCurve.Evaluate(1f), 1e-4f);

            // Suspension: more steering at speed, the same full lock at a standstill.
            Assert.AreEqual(config.SteerBySpeed.Evaluate(0f), tuned.SteerBySpeed.Evaluate(0f), 1e-4f);
            Assert.AreEqual(Mathf.Min(1f, config.SteerBySpeed.Evaluate(1f) * 1.2f), tuned.SteerBySpeed.Evaluate(1f), 1e-3f);

            var stock = Keep(config.CreateTuned(default));
            Assert.AreEqual(config.Acceleration, stock.Acceleration);
            Assert.AreEqual(config.AccelerationCurve.Evaluate(0.4f), stock.AccelerationCurve.Evaluate(0.4f), 1e-5f);
        }

        [Test]
        public void Catalog_SellsSixPartsInDearerAndBetterStagesThatImproveTheirStatOnEveryCar()
        {
            var catalog = settings.Upgrades;
            Assert.IsNull(settings.FindProblem());
            Assert.AreEqual(6, catalog.Upgrades.Count);
            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
            {
                var upgrade = catalog.Find(type);
                Assert.IsNotNull(upgrade, $"{type} is missing.");
                Assert.GreaterOrEqual(upgrade.MaxLevel, 3, $"{type} needs several stages.");

                foreach (var vehicle in roster.Vehicles)
                {
                    var previous = vehicle.MeasureStats();
                    var line = new List<string>();
                    for (int level = 1; level <= upgrade.MaxLevel; level++)
                    {
                        if (level > 1)
                            Assert.Greater(upgrade.CostOf(level), upgrade.CostOf(level - 1), $"{type} stage {level} should cost more.");

                        var stats = vehicle.MeasureStats(upgrade.TuningAt(level));
                        foreach (var stat in AllStats)
                        {
                            bool sold = Array.IndexOf(Improves[type], stat) >= 0;
                            float before = Better(stat, previous.Get(stat));
                            float after = Better(stat, stats.Get(stat));
                            if (sold)
                                Assert.Greater(after, before + 1e-4f, $"{type} stage {level} should improve the {vehicle.DisplayName}'s {stat}.");
                            else
                                Assert.GreaterOrEqual(after, before - 1e-4f, $"{type} stage {level} must not worsen the {vehicle.DisplayName}'s {stat}.");
                        }

                        line.Add($"{level}: {upgrade.CostOf(level)} CR {Describe(stats, Improves[type])}");
                        previous = stats;
                    }

                    if (vehicle == roster.Default)
                        TestContext.WriteLine($"{upgrade.DisplayName} on the {vehicle.DisplayName}: {Describe(vehicle.MeasureStats(), Improves[type])} stock; {string.Join("; ", line)}");
                }
            }
        }

        [Test]
        public void StatBars_HaveRoomForEveryCarFullyUpgraded()
        {
            var everything = default(VehicleTuning);
            foreach (var upgrade in settings.Upgrades.Upgrades)
                everything += upgrade.TuningAt(upgrade.MaxLevel);

            foreach (var vehicle in roster.Vehicles)
            {
                var stock = vehicle.MeasureStats();
                var maxed = vehicle.MeasureStats(everything);
                foreach (var stat in AllStats)
                {
                    // Unclamped, so a figure past the full bar shows up; a clamped bar would hide the last stages' gains.
                    var scale = roster.Scale(stat);
                    float fill = Mathf.InverseLerp(scale.Worst, scale.Best, maxed.Get(stat));
                    float unclamped = (maxed.Get(stat) - scale.Worst) / (scale.Best - scale.Worst);
                    Assert.LessOrEqual(unclamped, 1f, $"The fully upgraded {vehicle.DisplayName}'s {stat} ({maxed.Get(stat):0.###}) is past a full bar " +
                                                      $"({scale.Best:0.###}); widen the roster's Stat Bars.");
                    Assert.Greater(fill, roster.Rate(stock, stat) - 1e-4f);
                }

                TestContext.WriteLine($"{vehicle.DisplayName} fully upgraded: {Describe(maxed, AllStats)}  (stock {Describe(stock, AllStats)})");
            }
        }

        [Test]
        public void Shop_RefusesLockedCarsMaxedPartsAndPurchasesItCantAfford()
        {
            var profile = PlayerProfile.InMemory();
            var falcon = roster.Default;
            var engine = settings.Upgrades.Find(UpgradeType.Engine);
            int stage1 = engine.CostOf(1);

            profile.AddCredits(stage1 - 1);
            Assert.AreEqual(PurchaseCheck.NotEnoughCredits, UpgradeShop.Buy(profile, falcon, engine));
            Assert.AreEqual(0, profile.GetUpgradeLevel(falcon.Id, engine.Id));
            Assert.AreEqual(stage1 - 1, profile.Credits, "A refused purchase costs nothing.");

            profile.AddCredits(1);
            Assert.AreEqual(PurchaseCheck.Available, UpgradeShop.Buy(profile, falcon, engine));
            Assert.AreEqual(1, profile.GetUpgradeLevel(falcon.Id, engine.Id));
            Assert.AreEqual(0, profile.Credits);

            profile.AddCredits(100000);
            while (UpgradeShop.Check(profile, falcon, engine) == PurchaseCheck.Available)
                UpgradeShop.Buy(profile, falcon, engine);
            Assert.AreEqual(engine.MaxLevel, profile.GetUpgradeLevel(falcon.Id, engine.Id));
            Assert.AreEqual(PurchaseCheck.Maxed, UpgradeShop.Buy(profile, falcon, engine));
            Assert.AreEqual(0, UpgradeShop.NextCost(profile, falcon, engine));

            var locked = FindLockedCar();
            int credits = profile.Credits;
            Assert.AreEqual(PurchaseCheck.CarLocked, UpgradeShop.Buy(profile, locked, engine));
            Assert.AreEqual(credits, profile.Credits);

            var tuning = settings.Upgrades.TuningFor(profile, falcon.Id);
            Assert.AreEqual(engine.TuningAt(engine.MaxLevel).Acceleration, tuning.Acceleration, 1e-5f, "Installed parts make the car's tuning.");
            Assert.IsTrue(settings.Upgrades.TuningFor(profile, roster.Vehicles[1].Id).IsStock, "Each car has its own upgrades.");
        }

        [Test]
        public void Races_PayByPositionAndUnlockCarsAndRecordTheTrack()
        {
            TestContext.WriteLine($"Credits by position: {settings.CreditsFor(1)}, {settings.CreditsFor(2)}, {settings.CreditsFor(3)}, {settings.CreditsFor(4)}");
            for (int position = 2; position <= 4; position++)
                Assert.Less(settings.CreditsFor(position), settings.CreditsFor(position - 1), "A better finish pays more.");
            Assert.Greater(settings.CreditsFor(4), 0, "Every finish pays something.");
            Assert.AreEqual(settings.CreditsFor(4), settings.CreditsFor(9), "Positions past the table pay the last amount.");
            Assert.AreEqual(0, settings.CreditsFor(0));

            var profile = PlayerProfile.InMemory();
            profile.EnsureStarted(settings.StartingCredits);
            var track = settings.Tracks.Tracks[0];
            var locked = FindLockedCar();

            var third = RaceReward.Record(profile, settings, roster, track, 3, 4, 200f, 62f, newLapRecord: true);
            Assert.AreEqual(settings.CreditsFor(3), third.CreditsEarned);
            Assert.AreEqual(settings.StartingCredits + settings.CreditsFor(3), third.TotalCredits);
            Assert.IsTrue(third.NewLapRecord);
            Assert.IsEmpty(third.UnlockedVehicles);

            var win = RaceReward.Record(profile, settings, roster, track, 1, 4, 190f, 60f, newLapRecord: false);
            Assert.AreEqual(settings.CreditsFor(1), win.CreditsEarned);
            Assert.AreEqual(profile.Credits, win.TotalCredits);
            CollectionAssert.AreEqual(new[] { locked.DisplayName }, win.UnlockedVehicles, "The first win unlocks the locked car.");
            Assert.IsTrue(profile.HasUnlocked(locked.Id), "The unlock is kept in the profile.");
            Assert.AreEqual(1, profile.GetTrackRecord(track.Id).BestPosition);
            Assert.AreEqual(2, profile.GetTrackRecord(track.Id).Finishes);

            var again = RaceReward.Record(profile, settings, roster, track, 1, 4, 185f, 59f, newLapRecord: false);
            Assert.IsEmpty(again.UnlockedVehicles, "An unlock is only announced once.");
        }

        [Test]
        public void Tracks_UnlockByWinsOrAFinishOnTheTrackBefore()
        {
            var open = Keep(TrackInfo.Create("valley", "Valley", "ValleyScene"));
            var byWins = Keep(TrackInfo.Create("ridge", "Ridge", "RidgeScene", winsToUnlock: 2));
            var byPodium = Keep(TrackInfo.Create("canyon", "Canyon", "CanyonScene", previousTrack: open, positionOnPreviousTrack: 3));
            var tracks = Keep(ScriptableObject.CreateInstance<TrackRoster>());
            tracks.SetTracks(new[] { open, byWins, byPodium });
            var careerSettings = Keep(Object.Instantiate(settings));
            careerSettings.Configure(settings.Upgrades, tracks);
            Assert.IsNull(tracks.FindProblem());

            var profile = PlayerProfile.InMemory();
            Assert.IsTrue(open.IsUnlocked(profile));
            Assert.IsFalse(byWins.IsUnlocked(profile));
            Assert.IsFalse(byPodium.IsUnlocked(profile));
            Assert.AreEqual("WIN 2 RACES (0/2)", byWins.UnlockHint(profile));
            Assert.AreEqual("FINISH TOP 3 AT VALLEY", byPodium.UnlockHint(profile));

            profile.SelectTrack(byWins.Id);
            Assert.AreSame(open, tracks.Resolve(profile), "A locked track is never raced.");

            var fourth = RaceReward.Record(profile, careerSettings, roster, open, 4, 4, 200f, 60f, false);
            Assert.IsEmpty(fourth.UnlockedTracks, "Fourth isn't top 3.");
            var podium = RaceReward.Record(profile, careerSettings, roster, open, 2, 4, 200f, 60f, false);
            CollectionAssert.AreEqual(new[] { "Canyon" }, podium.UnlockedTracks);
            Assert.IsTrue(profile.HasUnlockedTrack(byPodium.Id));

            RaceReward.Record(profile, careerSettings, roster, open, 1, 4, 200f, 60f, false);
            var secondWin = RaceReward.Record(profile, careerSettings, roster, open, 1, 4, 200f, 60f, false);
            CollectionAssert.AreEqual(new[] { "Ridge" }, secondWin.UnlockedTracks);
            Assert.AreSame(byWins, tracks.Resolve(profile), "The chosen track is raced once it is unlocked.");
        }

        [Test]
        public void ProjectTracks_StartWithPineValleyOpen()
        {
            var tracks = settings.Tracks;
            Assert.IsNull(tracks.FindProblem());
            var first = tracks.Tracks[0];
            Assert.AreEqual("PineValley", first.SceneName);
            Assert.IsTrue(first.OpenFromStart);
            Assert.AreSame(first, tracks.Resolve(PlayerProfile.InMemory()));
        }

        private T Keep<T>(T item) where T : Object
        {
            created.Add(item);
            return item;
        }

        private VehicleDefinition FindLockedCar()
        {
            foreach (var vehicle in roster.Vehicles)
            {
                if (vehicle.WinsToUnlock == 1)
                    return vehicle;
            }

            Assert.Fail("Expected a car unlocked by one win.");
            return null;
        }

        /// <summary>A stat's figure turned so that bigger is always better.</summary>
        private static float Better(VehicleStat stat, float figure) =>
            stat == VehicleStat.Acceleration || stat == VehicleStat.Braking ? -figure : figure;

        private string Describe(VehicleStats stats, VehicleStat[] shown)
        {
            var parts = new List<string>();
            foreach (var stat in shown)
            {
                string figure = stats.Describe(stat, roster.Rate(stats, stat)).Replace("  ", " ");
                parts.Add(stat == VehicleStat.Handling ? $"handling {figure}" : figure);
            }

            return string.Join(", ", parts);
        }
    }
}
