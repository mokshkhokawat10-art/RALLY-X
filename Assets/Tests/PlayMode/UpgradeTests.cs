using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RallyX.Garage;
using RallyX.Player;
using RallyX.Progression;
using RallyX.Racing;
using RallyX.SaveSystem;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace RallyX.Tests
{
    /// <summary>
    /// Upgrades change how the car really drives. The default car is driven on the flat test course as built and then
    /// with one part at its last stage, and the difference is measured: 0-100 km/h, top speed, braking distance from
    /// 100 km/h, cornering grip on a skidpad, turn-in at 100 km/h and body roll. Each part must improve what it is sold
    /// on and make nothing else worse. Also that a track puts the player's car on the grid with its installed upgrades
    /// while the AI cars stay as built. Measured values are printed.
    /// </summary>
    public sealed class UpgradeTests : VehicleTestBase
    {
        private const string SettingsPath = "Assets/Settings/Progression/ProgressionSettings.asset";
        private const string RosterPath = "Assets/Settings/Vehicles/VehicleRoster.asset";
        private const string TrackScene = "PineValley";

        private const float StraightSeconds = 30f;

        // A small steering input at a steady 100 km/h, well inside the grip limit, so the car goes where its wheels point.
        private const float TurnInSteer = 0.05f;
        private const float TurnInSpeedKmh = 100f;

        // What a part must gain on the figures it is sold on, and what its side effects may cost the others.
        private const float MinimumGain = 0.015f;
        private const float SideEffectTolerance = 0.03f;

        private enum Figure
        {
            ZeroToHundred,
            TopSpeed,
            BrakingDistance,
            CorneringGrip,
            TurnIn,
            RollPerG,
        }

        private static readonly Dictionary<UpgradeType, Figure[]> SoldOn = new Dictionary<UpgradeType, Figure[]>
        {
            [UpgradeType.Engine] = new[] { Figure.ZeroToHundred },
            [UpgradeType.Turbo] = new[] { Figure.ZeroToHundred, Figure.TopSpeed },
            [UpgradeType.Tires] = new[] { Figure.CorneringGrip },
            [UpgradeType.Brakes] = new[] { Figure.BrakingDistance },
            [UpgradeType.Suspension] = new[] { Figure.TurnIn, Figure.RollPerG },
            [UpgradeType.Gearbox] = new[] { Figure.TopSpeed },
        };

        private Scene trackScene;

        [UnityTearDown]
        public IEnumerator UnloadTrack()
        {
            if (trackScene.IsValid() && trackScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(trackScene);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Upgrade_ImprovesWhatItIsSoldOnWhenDriven([Values] UpgradeType type)
        {
            var upgrade = LoadAsset<ProgressionSettings>(SettingsPath).Upgrades.Find(type);
            var vehicle = LoadAsset<VehicleRoster>(RosterPath).Default;
            yield return CreateCourse(null, SpawnPosition, Quaternion.identity, settle: false, carPrefab: vehicle.Prefab.gameObject);

            var stock = new Dictionary<Figure, float>();
            yield return Measure(stock);

            var tuned = vehicle.Prefab.Config.CreateTuned(upgrade.TuningAt(upgrade.MaxLevel));
            var upgraded = new Dictionary<Figure, float>();
            try
            {
                Car.SetConfig(tuned);
                yield return Measure(upgraded);
            }
            finally
            {
                Car.SetConfig(vehicle.Prefab.Config);
                Object.Destroy(tuned);
            }

            TestContext.WriteLine($"{upgrade.DisplayName} stage {upgrade.MaxLevel} on the {vehicle.DisplayName}:");
            foreach (Figure figure in Enum.GetValues(typeof(Figure)))
                TestContext.WriteLine($"  {figure,-16} stock {stock[figure],8:0.0000}   upgraded {upgraded[figure],8:0.0000}   gain {Gain(figure, stock, upgraded) * 100f:+0.0;-0.0}%");

            foreach (Figure figure in Enum.GetValues(typeof(Figure)))
            {
                float gain = Gain(figure, stock, upgraded);
                if (Array.IndexOf(SoldOn[type], figure) >= 0)
                    Assert.Greater(gain, MinimumGain, $"{upgrade.DisplayName} should improve the car's {figure} when driven.");
                else
                    Assert.Greater(gain, -SideEffectTolerance, $"{upgrade.DisplayName} must not make the car's {figure} worse.");
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Track_PutsThePlayersCarOnTheGridWithItsUpgrades()
        {
            var catalog = LoadAsset<ProgressionSettings>(SettingsPath).Upgrades;
            var vehicle = LoadAsset<VehicleRoster>(RosterPath).Default;
            var stockConfig = vehicle.Prefab.Config;
            float stockAcceleration = stockConfig.Acceleration;

            var profile = PlayerProfile.Current;
            profile.AddCredits(100000);
            foreach (var type in new[] { UpgradeType.Engine, UpgradeType.Engine, UpgradeType.Tires, UpgradeType.Gearbox })
                Assert.AreEqual(PurchaseCheck.Available, UpgradeShop.Buy(profile, vehicle, catalog.Find(type)));
            var installed = catalog.TuningFor(profile, vehicle.Id);

            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(TrackScene), $"{TrackScene} is not in the build scene list.");
            yield return SceneManager.LoadSceneAsync(TrackScene, LoadSceneMode.Additive);
            trackScene = SceneManager.GetSceneByName(TrackScene);
            yield return null;

            var entry = Object.FindFirstObjectByType<PlayerRaceEntry>();
            Assert.AreSame(vehicle, entry.Vehicle);
            Assert.AreEqual(catalog.Find(UpgradeType.Engine).TuningAt(2).Acceleration, entry.Tuning.Acceleration, 1e-5f);

            var config = entry.Car.Config;
            Assert.AreNotSame(stockConfig, config, "The upgraded car drives its own copy of the handling.");
            Assert.AreEqual(stockConfig.Acceleration * (1f + installed.Acceleration), config.Acceleration, 1e-4f);
            Assert.AreEqual(stockConfig.TyreFriction * (1f + installed.Grip), config.TyreFriction, 1e-4f);
            Assert.AreEqual(stockConfig.MaxSpeedKmh * (1f + installed.TopSpeed), config.MaxSpeedKmh, 1e-3f);
            Assert.AreEqual(stockConfig.BrakeDeceleration, config.BrakeDeceleration, "Parts not bought stay as built.");
            Assert.AreEqual(stockAcceleration, stockConfig.Acceleration, "The car's config asset is never changed.");
            TestContext.WriteLine($"Player's {vehicle.DisplayName}: acceleration {stockConfig.Acceleration} -> {config.Acceleration:0.00}, " +
                                  $"grip {stockConfig.TyreFriction} -> {config.TyreFriction:0.000}, top speed {stockConfig.MaxSpeedKmh} -> {config.MaxSpeedKmh:0.0} km/h");

            var race = Object.FindFirstObjectByType<RaceManager>();
            int opponents = 0;
            foreach (var participant in race.Participants)
            {
                if (participant.IsPlayer)
                    continue;
                opponents++;
                Assert.AreNotSame(config, participant.Vehicle.Config);
                StringAssert.DoesNotContain("(Tuned)", participant.Vehicle.Config.name, "AI cars race as built.");
            }

            Assert.AreEqual(3, opponents);

            // The upgraded handling is released with the track.
            yield return SceneManager.UnloadSceneAsync(trackScene);
            yield return null;
            Assert.IsTrue(config == null, "The upgraded copy of the handling should be destroyed with the car.");
        }

        /// <summary>Drives the car through the straight-line, turn-in and skidpad runs and records its figures.</summary>
        private IEnumerator Measure(Dictionary<Figure, float> figures)
        {
            // From a standstill: 0-100 km/h and the fastest speed reached, then full braking measured from 100 km/h.
            yield return Restart();
            float elapsed = 0f;
            float zeroToHundred = float.PositiveInfinity;
            float topSpeed = 0f;
            yield return Drive(FullThrottle, StraightSeconds, () =>
            {
                elapsed += Time.fixedDeltaTime;
                topSpeed = Mathf.Max(topSpeed, Car.SpeedKmh);
                if (float.IsPositiveInfinity(zeroToHundred) && Car.SpeedKmh >= 100f)
                    zeroToHundred = elapsed;
            });

            Vector3? from100 = null;
            float braking = float.PositiveInfinity;
            Car.SetInput(new VehicleInputState(0f, 1f, 0f, false));
            for (float t = 0f; float.IsPositiveInfinity(braking); t += Time.fixedDeltaTime)
            {
                Assert.Less(t, 15f, "The car should stop.");
                yield return NextPhysicsStep;
                if (from100 == null && Car.SpeedKmh <= 100f)
                    from100 = Car.Body.position;
                if (from100 != null && Car.Body.linearVelocity.magnitude < 0.3f)
                    braking = Vector3.Distance(Car.Body.position, from100.Value);
            }

            // Turn-in: how tightly a small steering input curves the path at a steady 100 km/h.
            yield return Restart();
            yield return AccelerateTo(TurnInSpeedKmh);
            var turnIn = new PathSampler();
            for (float t = 0f; t < 2f; t += Time.fixedDeltaTime)
            {
                float throttle = Mathf.Clamp01(0.3f + (TurnInSpeedKmh - Car.SpeedKmh) * 0.3f);
                Car.SetInput(new VehicleInputState(throttle, 0f, TurnInSteer, false));
                yield return NextPhysicsStep;
                turnIn.Sample(Car, record: t >= 1f);
            }

            // Skidpad: full lock and full throttle until the car circles at the limit of its grip.
            yield return Restart();
            yield return AccelerateTo(60f);
            var skidpad = new PathSampler();
            elapsed = 0f;
            yield return Drive(new VehicleInputState(1f, 0f, 1f, false), 6f, () =>
            {
                elapsed += Time.fixedDeltaTime;
                skidpad.Sample(Car, record: elapsed >= 3f);
            });
            Assert.Greater(Uprightness(), 0.7f, "The car should stay on its wheels on the skidpad.");

            figures[Figure.ZeroToHundred] = zeroToHundred;
            figures[Figure.TopSpeed] = topSpeed;
            figures[Figure.BrakingDistance] = braking;
            figures[Figure.TurnIn] = turnIn.Curvature;
            figures[Figure.CorneringGrip] = skidpad.LateralG;
            figures[Figure.RollPerG] = skidpad.RollDegrees / skidpad.LateralG;
        }

        private IEnumerator Restart()
        {
            Car.PlaceAt(SpawnPosition, Quaternion.identity);
            yield return Drive(default, SettleSeconds);
        }

        /// <summary>Improvement as a fraction of the stock figure (negative when worse).</summary>
        private static float Gain(Figure figure, Dictionary<Figure, float> stock, Dictionary<Figure, float> upgraded)
        {
            bool lowerIsBetter = figure == Figure.ZeroToHundred || figure == Figure.BrakingDistance || figure == Figure.RollPerG;
            float change = (upgraded[figure] - stock[figure]) / stock[figure];
            return lowerIsBetter ? -change : change;
        }

        /// <summary>The path the car takes: how much it turns per metre, its sideways acceleration and its body roll.</summary>
        private sealed class PathSampler
        {
            private Vector3 lastDirection;
            private float turned;
            private float travelled;
            private float sideways;
            private float roll;
            private float time;

            /// <summary>Path curvature, 1/m.</summary>
            public float Curvature => turned / travelled;

            /// <summary>Average sideways (centripetal) acceleration, g.</summary>
            public float LateralG => sideways / time / Physics.gravity.magnitude;

            /// <summary>Average body roll, degrees.</summary>
            public float RollDegrees => roll / time;

            public void Sample(VehicleController car, bool record)
            {
                Vector3 velocity = Vector3.ProjectOnPlane(car.Body.linearVelocity, Vector3.up);
                Vector3 direction = velocity.normalized;
                if (record && lastDirection != Vector3.zero)
                {
                    // Atan2 keeps its precision for the tiny angles turned in one physics step.
                    float angle = Mathf.Abs(Mathf.Atan2(Vector3.Dot(Vector3.Cross(lastDirection, direction), Vector3.up),
                        Vector3.Dot(lastDirection, direction)));
                    float step = Time.fixedDeltaTime;
                    turned += angle;
                    travelled += velocity.magnitude * step;
                    sideways += velocity.magnitude * angle;
                    roll += Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(car.Body.rotation * Vector3.right, Vector3.up), -1f, 1f))) * Mathf.Rad2Deg * step;
                    time += step;
                }

                lastDirection = direction;
            }
        }
    }
}
