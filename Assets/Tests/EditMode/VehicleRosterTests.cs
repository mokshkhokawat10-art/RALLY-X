using System;
using System.Collections.Generic;
using NUnit.Framework;
using RallyX.EditorTools;
using RallyX.Garage;
using RallyX.SaveSystem;
using RallyX.Vehicle;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RallyX.Tests
{
    /// <summary>The project's garage cars: a complete roster, distinct cars, stats that follow the configs, and unlocking.</summary>
    public sealed class VehicleRosterTests
    {
        private static readonly VehicleStat[] AllStats = (VehicleStat[])Enum.GetValues(typeof(VehicleStat));

        private VehicleRoster roster;

        [SetUp]
        public void LoadRoster()
        {
            roster = GarageContentBuilder.LoadRoster();
            Assert.IsNotNull(roster, $"Missing {GarageContentBuilder.RosterPath}. Run RALLY X > Build > Create Missing Garage Content.");
        }

        [Test]
        public void Roster_HasAtLeastThreeCompleteCarsWithAnUnlockedDefaultAndALockedCar()
        {
            Assert.IsNull(roster.FindProblem());
            Assert.GreaterOrEqual(roster.Count, 3, "The garage needs at least three cars.");
            Assert.AreEqual(0, roster.Default.WinsToUnlock, "The default car must be available from the start.");
            Assert.IsTrue(Array.Exists(ToArray(roster.Vehicles), vehicle => vehicle.WinsToUnlock > 0), "At least one car should be locked.");

            var prefabs = new HashSet<VehicleController>();
            var configs = new HashSet<VehicleConfig>();
            foreach (var vehicle in roster.Vehicles)
            {
                Assert.IsTrue(prefabs.Add(vehicle.Prefab), $"{vehicle.DisplayName} needs its own model.");
                Assert.IsTrue(configs.Add(vehicle.Prefab.Config), $"{vehicle.DisplayName} needs its own handling.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(vehicle.DisplayName));
                Assert.IsFalse(string.IsNullOrWhiteSpace(vehicle.Category));
                Assert.IsFalse(string.IsNullOrWhiteSpace(vehicle.Description));
            }

            Assert.AreEqual(VehicleContentBuilder.CarPrefabPath, AssetDatabase.GetAssetPath(roster.Default.Prefab),
                "The default car is the RallyCar the physics, track and AI were tuned with.");
        }

        [Test]
        public void Cars_AreDistinctAndEachBarIsReadable()
        {
            var ratings = new Dictionary<VehicleDefinition, float[]>();
            foreach (var vehicle in roster.Vehicles)
            {
                var stats = vehicle.MeasureStats();
                var values = new float[AllStats.Length];
                var described = new List<string>();
                foreach (var stat in AllStats)
                {
                    float rating = roster.Rate(stats, stat);
                    values[(int)stat] = rating;
                    described.Add($"{VehicleStats.Label(stat)} {stats.Describe(stat, rating)} ({rating:0.00})");
                    Assert.That(rating, Is.InRange(0.1f, 0.95f), $"{vehicle.DisplayName}'s {stat} bar should be neither empty nor full.");
                }

                ratings[vehicle] = values;
                TestContext.WriteLine($"{vehicle.DisplayName}: {string.Join(", ", described)}; turn-in {stats.TurnIn:0.0000} 1/m");
            }

            // A real choice: no car is beaten by another at everything, and each is either the best at something
            // or the all-rounder (the car whose weakest bar is the strongest).
            float bestWeakest = 0f;
            foreach (var vehicle in roster.Vehicles)
                bestWeakest = Mathf.Max(bestWeakest, Weakest(ratings[vehicle]));

            foreach (var vehicle in roster.Vehicles)
            {
                bool bestAtSomething = false;
                foreach (var stat in AllStats)
                {
                    bool best = true;
                    foreach (var other in roster.Vehicles)
                        best &= other == vehicle || ratings[vehicle][(int)stat] >= ratings[other][(int)stat];
                    bestAtSomething |= best;
                }

                bool allRounder = Mathf.Approximately(Weakest(ratings[vehicle]), bestWeakest);
                Assert.IsTrue(bestAtSomething || allRounder, $"{vehicle.DisplayName} should be the best car at something, or the all-rounder.");

                foreach (var other in roster.Vehicles)
                {
                    if (other == vehicle)
                        continue;
                    bool beatenEverywhere = true;
                    foreach (var stat in AllStats)
                        beatenEverywhere &= ratings[other][(int)stat] > ratings[vehicle][(int)stat];
                    Assert.IsFalse(beatenEverywhere, $"The {other.DisplayName} beats the {vehicle.DisplayName} at everything.");
                }
            }
        }

        private static float Weakest(float[] ratings)
        {
            float weakest = float.MaxValue;
            foreach (float rating in ratings)
                weakest = Mathf.Min(weakest, rating);
            return weakest;
        }

        [Test]
        public void Stats_FollowTheCarsOwnConfigAndWheels()
        {
            var prefab = roster.Default.Prefab;
            var config = Object.Instantiate(prefab.Config);
            var car = Object.Instantiate(prefab);
            try
            {
                // A flat acceleration curve makes 0-100 km/h exact: 27.78 m/s at 10 m/s².
                var serializedConfig = new SerializedObject(config);
                serializedConfig.FindProperty("acceleration").floatValue = 10f;
                serializedConfig.FindProperty("accelerationCurve").animationCurveValue = AnimationCurve.Constant(0f, 1f, 1f);
                serializedConfig.FindProperty("brakeDeceleration").floatValue = 20f;
                serializedConfig.FindProperty("tyreFriction").floatValue = 1.5f;
                serializedConfig.FindProperty("frontGrip").floatValue = 1f;
                serializedConfig.FindProperty("rearGrip").floatValue = 0.9f;
                serializedConfig.ApplyModifiedPropertiesWithoutUndo();
                var serializedCar = new SerializedObject(car);
                serializedCar.FindProperty("config").objectReferenceValue = config;
                serializedCar.ApplyModifiedPropertiesWithoutUndo();

                var stats = VehicleStats.Measure(car);
                float lockAt100 = config.MaxSteerAngle * config.SteerBySpeed.Evaluate(VehicleStats.ReferenceSpeed / config.MaxSpeed);
                Assert.AreEqual(config.MaxSpeedKmh, stats.TopSpeedKmh, 1e-3f);
                Assert.AreEqual(100f / 36f, stats.ZeroToHundredSeconds, 0.01f, "0-100 km/h at a steady 10 m/s².");
                Assert.AreEqual(27.778f * 27.778f / 40f, stats.BrakingDistance, 0.05f, "100-0 km/h at 20 m/s².");
                Assert.AreEqual(1.35f, stats.GripG, 1e-3f, "Grip is the weaker axle's.");
                Assert.AreEqual(2.65f, VehicleStats.Wheelbase(car), 0.01f, "The RallyCar's axles are 1.35 m ahead and 1.3 m behind the centre.");
                Assert.AreEqual(Mathf.Tan(lockAt100 * Mathf.Deg2Rad) / 2.65f, stats.TurnIn, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void LockedCar_UnlocksWithWinsOrOutright()
        {
            var locked = Array.Find(ToArray(roster.Vehicles), vehicle => vehicle.WinsToUnlock > 0);
            var profile = PlayerProfile.InMemory();
            Assert.IsFalse(locked.IsUnlocked(profile));
            StringAssert.Contains($"(0/{locked.WinsToUnlock})", locked.UnlockHint(profile));

            profile.RecordRaceFinish(2);
            Assert.IsFalse(locked.IsUnlocked(profile), "Second place isn't a win.");

            for (int i = 0; i < locked.WinsToUnlock; i++)
                profile.RecordRaceFinish(1);
            Assert.IsTrue(locked.IsUnlocked(profile), $"{locked.WinsToUnlock} wins unlock the {locked.DisplayName}.");
            Assert.AreEqual(string.Empty, locked.UnlockHint(profile));

            var outright = PlayerProfile.InMemory();
            outright.Unlock(locked.Id);
            Assert.IsTrue(locked.IsUnlocked(outright), "An outright unlock (e.g. a purchase) works too.");
        }

        [Test]
        public void Resolve_RacesTheChosenCarOnlyWhenItIsUnlocked()
        {
            var profile = PlayerProfile.InMemory();
            Assert.AreSame(roster.Default, roster.Resolve(profile), "Nothing chosen: the default car.");

            var second = roster.Vehicles[1];
            profile.SelectVehicle(second.Id);
            Assert.AreSame(second, roster.Resolve(profile));

            var locked = Array.Find(ToArray(roster.Vehicles), vehicle => vehicle.WinsToUnlock > 0);
            profile.SelectVehicle(locked.Id);
            Assert.AreSame(roster.Default, roster.Resolve(profile), "A locked car is never raced.");

            profile.SelectVehicle("no-such-car");
            Assert.AreSame(roster.Default, roster.Resolve(profile), "A car that no longer exists falls back to the default.");
        }

        private static VehicleDefinition[] ToArray(IReadOnlyList<VehicleDefinition> vehicles)
        {
            var array = new VehicleDefinition[vehicles.Count];
            for (int i = 0; i < array.Length; i++)
                array[i] = vehicles[i];
            return array;
        }
    }
}
