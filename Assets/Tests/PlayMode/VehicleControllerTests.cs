using System;
using System.Collections;
using NUnit.Framework;
using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RallyX.Tests
{
    /// <summary>
    /// Drives the real car prefab with scripted input on a flat plane and checks the physics outcomes.
    /// Thresholds come from the car's VehicleConfig, so the tests keep passing through normal tuning.
    /// </summary>
    public sealed class VehicleControllerTests
    {
        private const string CarPrefabPath = "Assets/Prefabs/Vehicles/RallyCar.prefab";
        private const string GrassSurfacePath = "Assets/Settings/Surfaces/Surface_Grass.asset";

        // Physics runs at its normal fixed timestep; time scale only makes the run finish sooner.
        private const float SimulationSpeedup = 8f;
        private const float SettleSeconds = 2f;

        // 3 km square ground with the car near one edge, so long runs stay on it.
        private const float GroundScale = 300f;
        private static readonly Vector3 SpawnPosition = new Vector3(0f, 0.3f, -1400f);

        private static readonly WaitForFixedUpdate NextPhysicsStep = new WaitForFixedUpdate();
        private static readonly VehicleInputState FullThrottle = new VehicleInputState(1f, 0f, 0f, false);

        private GameObject ground;
        private VehicleController car;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = SimulationSpeedup;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = previousTimeScale;
            if (car != null)
                Object.Destroy(car.gameObject);
            if (ground != null)
                Object.Destroy(ground);
            yield return null;
        }

        [UnityTest]
        public IEnumerator IdleCar_SettlesUprightOnAllFourWheels()
        {
            yield return CreateCourse();

            Assert.AreEqual(4, car.GroundedWheelCount, "All wheels should touch the ground.");
            Assert.Less(car.Body.linearVelocity.magnitude, 0.2f, "An idle car should come to rest.");
            AssertUpright(0.99f);
            Assert.That(car.Body.position.y, Is.InRange(-0.1f, 0.15f), "Suspension should hold the car at its designed ride height.");
        }

        [UnityTest]
        public IEnumerator FullThrottle_ApproachesButNeverExceedsTopSpeed()
        {
            yield return CreateCourse();
            float topSpeed = car.Config.MaxSpeedKmh;
            float fastest = 0f;
            float elapsed = 0f;
            float timeTo100 = -1f;

            yield return Drive(FullThrottle, 25f, () =>
            {
                elapsed += Time.fixedDeltaTime;
                fastest = Mathf.Max(fastest, car.SpeedKmh);
                if (timeTo100 < 0f && car.SpeedKmh >= 100f)
                    timeTo100 = elapsed;
            });

            TestContext.WriteLine($"0-100 km/h: {timeTo100:0.00} s, fastest: {fastest:0.0} km/h (limit {topSpeed})");
            Assert.LessOrEqual(fastest, topSpeed * 1.02f, "Top speed limit exceeded.");
            Assert.GreaterOrEqual(car.SpeedKmh, topSpeed * 0.9f, "Car should get close to its top speed.");
        }

        [UnityTest]
        public IEnumerator Grass_LowersTopSpeedButStaysDrivable()
        {
            var grass = LoadAsset<SurfaceType>(GrassSurfacePath);
            yield return CreateCourse(grass);
            float limit = car.Config.MaxSpeedKmh * grass.Properties.TopSpeed;
            float fastest = 0f;

            yield return Drive(FullThrottle, 25f, () => fastest = Mathf.Max(fastest, car.SpeedKmh));

            TestContext.WriteLine($"Grass fastest: {fastest:0.0} km/h (limit {limit:0.0})");
            Assert.LessOrEqual(fastest, limit * 1.02f, "Grass should cap speed below the asphalt top speed.");
            Assert.Greater(fastest, limit * 0.6f, "The car should still make progress on grass.");
        }

        [UnityTest]
        public IEnumerator Brake_StopsCarWithinExpectedDistance()
        {
            yield return CreateCourse();
            yield return AccelerateTo(80f);
            float startSpeed = car.ForwardSpeed;
            Vector3 start = car.Body.position;
            float elapsed = 0f;

            car.SetInput(new VehicleInputState(0f, 1f, 0f, false));
            while (car.ForwardSpeed > 0.5f && elapsed < 6f)
            {
                yield return NextPhysicsStep;
                elapsed += Time.fixedDeltaTime;
            }

            float distance = Vector3.Distance(start, car.Body.position);
            float idealDistance = startSpeed * startSpeed / (2f * car.Config.BrakeDeceleration);
            TestContext.WriteLine($"Braking from {startSpeed * 3.6f:0} km/h: {distance:0.0} m in {elapsed:0.00} s (ideal {idealDistance:0.0} m)");
            Assert.Less(elapsed, 6f, "The car did not stop.");
            Assert.Less(distance, idealDistance * 1.5f + 2f, $"Stopping took {distance:0.0} m; ideal is {idealDistance:0.0} m.");
            AssertUpright(0.9f);
        }

        [UnityTest]
        public IEnumerator BrakeAtStandstill_ReversesUpToReverseLimit()
        {
            yield return CreateCourse();
            float fastestReverse = 0f;

            yield return Drive(new VehicleInputState(0f, 1f, 0f, false), 6f,
                () => fastestReverse = Mathf.Max(fastestReverse, -car.ForwardSpeed * 3.6f));

            TestContext.WriteLine($"Fastest reverse: {fastestReverse:0.0} km/h (limit {car.Config.MaxReverseSpeedKmh})");
            Assert.Less(car.ForwardSpeed, -1f, "Holding brake from a standstill should reverse.");
            Assert.LessOrEqual(fastestReverse, car.Config.MaxReverseSpeedKmh * 1.05f, "Reverse speed limit exceeded.");
        }

        [UnityTest]
        public IEnumerator SteeringRight_TurnsCarRight()
        {
            yield return CreateCourse();
            yield return AccelerateTo(60f);
            Vector3 startHeading = Heading();

            yield return Drive(new VehicleInputState(0.5f, 0f, 1f, false), 1.5f);

            float yaw = Vector3.SignedAngle(startHeading, Heading(), Vector3.up);
            TestContext.WriteLine($"Yaw after 1.5 s full right lock from 60 km/h: {yaw:0.0}°, speed now {car.SpeedKmh:0} km/h, slip {car.Drift.SlipAngle:0}°");
            Assert.Greater(yaw, 20f, "Steering right should turn the car right.");
            AssertUpright(0.9f);
        }

        [UnityTest]
        public IEnumerator HandbrakeTurn_DriftsThenRecoversWithoutRollingOver()
        {
            yield return CreateCourse();
            yield return AccelerateTo(90f);
            int driftsStarted = 0;
            float largestSlip = 0f;
            float lowestUprightness = 1f;
            car.Drift.DriftStarted += () => driftsStarted++;

            yield return Drive(new VehicleInputState(0.6f, 0f, 1f, true), 1.2f, () =>
            {
                largestSlip = Mathf.Max(largestSlip, Mathf.Abs(TravelAngle()));
                lowestUprightness = Mathf.Min(lowestUprightness, Uprightness());
            });
            float slipAtRelease = TravelAngle();
            yield return Drive(new VehicleInputState(0.3f, 0f, 0f, false), 3f, () =>
            {
                largestSlip = Mathf.Max(largestSlip, Mathf.Abs(TravelAngle()));
                lowestUprightness = Mathf.Min(lowestUprightness, Uprightness());
            });

            TestContext.WriteLine($"Drifts started: {driftsStarted}, largest slip: {largestSlip:0.0}°, slip at release: {slipAtRelease:0.0}°, " +
                                  $"lowest uprightness: {lowestUprightness:0.00}, final speed {car.SpeedKmh:0} km/h");
            Assert.AreEqual(1, driftsStarted, "One handbrake turn should produce exactly one continuous drift.");
            Assert.Greater(largestSlip, car.Config.DriftAngleThreshold, "The rear should slide out.");
            Assert.LessOrEqual(largestSlip, car.Config.MaxDriftAngle + 15f, "Anti-spin should stop the car spinning out.");
            Assert.Greater(lowestUprightness, 0.5f, "The car must not roll over.");
            Assert.IsFalse(car.Drift.IsDrifting, "The drift should end once the handbrake is released and steering straightened.");
        }

        [UnityTest]
        public IEnumerator DroppedTiltedCar_LandsUprightOnAllWheels()
        {
            yield return CreateCourse(null, SpawnPosition + Vector3.up * 4f, Quaternion.Euler(10f, 0f, 25f), settle: false);
            float longestAirTime = 0f;

            yield return Drive(default, 4f, () => longestAirTime = Mathf.Max(longestAirTime, car.AirborneTime));

            TestContext.WriteLine($"Air time: {longestAirTime:0.00} s, uprightness after landing: {Uprightness():0.000}");
            Assert.Greater(longestAirTime, 0.5f, "The car should have been airborne.");
            Assert.AreEqual(4, car.GroundedWheelCount, "All wheels should be down after landing.");
            AssertUpright(0.98f);
        }

        [UnityTest]
        public IEnumerator ResetVehicle_RightsUpsideDownCarKeepingHeading()
        {
            yield return CreateCourse(null, SpawnPosition + Vector3.up, Quaternion.Euler(0f, 30f, 180f), settle: true);
            Assert.Less(Uprightness(), 0f, "Precondition: the car should be upside down.");

            car.ResetVehicle();
            yield return Drive(default, SettleSeconds);

            AssertUpright(0.99f);
            Assert.AreEqual(4, car.GroundedWheelCount);
            Assert.That(Vector3.SignedAngle(Vector3.forward, Heading(), Vector3.up), Is.EqualTo(30f).Within(5f), "Reset should keep the heading.");
        }

        private IEnumerator CreateCourse(SurfaceType surface = null) =>
            CreateCourse(surface, SpawnPosition, Quaternion.identity, settle: true);

        private IEnumerator CreateCourse(SurfaceType surface, Vector3 position, Quaternion rotation, bool settle)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "TestGround";
            ground.transform.localScale = new Vector3(GroundScale, 1f, GroundScale);
            if (surface != null)
                ground.AddComponent<SurfaceMarker>().Surface = surface;

            car = Object.Instantiate(LoadAsset<GameObject>(CarPrefabPath), position, rotation).GetComponent<VehicleController>();
            Assert.IsNotNull(car, "The car prefab needs a VehicleController.");

            if (settle)
                yield return Drive(default, SettleSeconds);
        }

        private IEnumerator Drive(VehicleInputState input, float seconds, Action afterEachStep = null)
        {
            car.SetInput(input);
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.fixedDeltaTime)
            {
                yield return NextPhysicsStep;
                afterEachStep?.Invoke();
            }
        }

        private IEnumerator AccelerateTo(float speedKmh)
        {
            const float timeout = 20f;
            car.SetInput(FullThrottle);
            for (float elapsed = 0f; car.SpeedKmh < speedKmh; elapsed += Time.fixedDeltaTime)
            {
                Assert.Less(elapsed, timeout, $"The car did not reach {speedKmh} km/h within {timeout} s.");
                yield return NextPhysicsStep;
            }
        }

        private float Uprightness() => Vector3.Dot(car.Body.rotation * Vector3.up, Vector3.up);

        private Vector3 Heading() => Vector3.ProjectOnPlane(car.Body.rotation * Vector3.forward, Vector3.up).normalized;

        /// <summary>Full-range angle between heading and direction of travel (unlike SlipAngle, not clipped at 90°).</summary>
        private float TravelAngle()
        {
            Vector3 planarVelocity = Vector3.ProjectOnPlane(car.Body.linearVelocity, Vector3.up);
            return planarVelocity.magnitude < 1f ? 0f : Vector3.SignedAngle(Heading(), planarVelocity, Vector3.up);
        }

        private void AssertUpright(float minimum) =>
            Assert.Greater(Uprightness(), minimum, "The car should be upright.");

        private static T LoadAsset<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"Missing {path}. Run RALLY X > Build > Create Missing Vehicle Content.");
            return asset;
#else
            Assert.Ignore("Vehicle tests load project assets and run only in the Unity Editor.");
            return null;
#endif
        }
    }
}
