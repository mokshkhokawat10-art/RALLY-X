using System.Collections;
using NUnit.Framework;
using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.TestTools;

namespace RallyX.Tests
{
    /// <summary>
    /// Drives the real car prefab with scripted input on a flat plane and checks the physics outcomes.
    /// Thresholds come from the car's VehicleConfig, so the tests keep passing through normal tuning.
    /// </summary>
    public sealed class VehicleControllerTests : VehicleTestBase
    {
        private const string GrassSurfacePath = "Assets/Settings/Surfaces/Surface_Grass.asset";

        [UnityTest]
        public IEnumerator IdleCar_SettlesUprightOnAllFourWheels()
        {
            yield return CreateCourse();

            Assert.AreEqual(4, Car.GroundedWheelCount, "All wheels should touch the ground.");
            Assert.Less(Car.Body.linearVelocity.magnitude, 0.2f, "An idle car should come to rest.");
            AssertUpright(0.99f);
            Assert.That(Car.Body.position.y, Is.InRange(-0.1f, 0.15f), "Suspension should hold the car at its designed ride height.");
        }

        [UnityTest]
        public IEnumerator FullThrottle_ApproachesButNeverExceedsTopSpeed()
        {
            yield return CreateCourse();
            float topSpeed = Car.Config.MaxSpeedKmh;
            float fastest = 0f;
            float elapsed = 0f;
            float timeTo100 = -1f;

            yield return Drive(FullThrottle, 25f, () =>
            {
                elapsed += Time.fixedDeltaTime;
                fastest = Mathf.Max(fastest, Car.SpeedKmh);
                if (timeTo100 < 0f && Car.SpeedKmh >= 100f)
                    timeTo100 = elapsed;
            });

            TestContext.WriteLine($"0-100 km/h: {timeTo100:0.00} s, fastest: {fastest:0.0} km/h (limit {topSpeed})");
            Assert.LessOrEqual(fastest, topSpeed * 1.02f, "Top speed limit exceeded.");
            Assert.GreaterOrEqual(Car.SpeedKmh, topSpeed * 0.9f, "Car should get close to its top speed.");
        }

        [UnityTest]
        public IEnumerator Grass_LowersTopSpeedButStaysDrivable()
        {
            var grass = LoadAsset<SurfaceType>(GrassSurfacePath);
            yield return CreateCourse(grass);
            float limit = Car.Config.MaxSpeedKmh * grass.Properties.TopSpeed;
            float fastest = 0f;

            yield return Drive(FullThrottle, 25f, () => fastest = Mathf.Max(fastest, Car.SpeedKmh));

            TestContext.WriteLine($"Grass fastest: {fastest:0.0} km/h (limit {limit:0.0})");
            Assert.LessOrEqual(fastest, limit * 1.02f, "Grass should cap speed below the asphalt top speed.");
            Assert.Greater(fastest, limit * 0.6f, "The car should still make progress on grass.");
        }

        [UnityTest]
        public IEnumerator Brake_StopsCarWithinExpectedDistance()
        {
            yield return CreateCourse();
            yield return AccelerateTo(80f);
            float startSpeed = Car.ForwardSpeed;
            Vector3 start = Car.Body.position;
            float elapsed = 0f;

            Car.SetInput(new VehicleInputState(0f, 1f, 0f, false));
            while (Car.ForwardSpeed > 0.5f && elapsed < 6f)
            {
                yield return NextPhysicsStep;
                elapsed += Time.fixedDeltaTime;
            }

            float distance = Vector3.Distance(start, Car.Body.position);
            float idealDistance = startSpeed * startSpeed / (2f * Car.Config.BrakeDeceleration);
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
                () => fastestReverse = Mathf.Max(fastestReverse, -Car.ForwardSpeed * 3.6f));

            TestContext.WriteLine($"Fastest reverse: {fastestReverse:0.0} km/h (limit {Car.Config.MaxReverseSpeedKmh})");
            Assert.Less(Car.ForwardSpeed, -1f, "Holding brake from a standstill should reverse.");
            Assert.LessOrEqual(fastestReverse, Car.Config.MaxReverseSpeedKmh * 1.05f, "Reverse speed limit exceeded.");
        }

        [UnityTest]
        public IEnumerator SteeringRight_TurnsCarRight()
        {
            yield return CreateCourse();
            yield return AccelerateTo(60f);
            Vector3 startHeading = Heading();

            yield return Drive(new VehicleInputState(0.5f, 0f, 1f, false), 1.5f);

            float yaw = Vector3.SignedAngle(startHeading, Heading(), Vector3.up);
            TestContext.WriteLine($"Yaw after 1.5 s full right lock from 60 km/h: {yaw:0.0}°, speed now {Car.SpeedKmh:0} km/h, slip {Car.Drift.SlipAngle:0}°");
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
            Car.Drift.DriftStarted += () => driftsStarted++;

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
                                  $"lowest uprightness: {lowestUprightness:0.00}, final speed {Car.SpeedKmh:0} km/h");
            Assert.AreEqual(1, driftsStarted, "One handbrake turn should produce exactly one continuous drift.");
            Assert.Greater(largestSlip, Car.Config.DriftAngleThreshold, "The rear should slide out.");
            Assert.LessOrEqual(largestSlip, Car.Config.MaxDriftAngle + 15f, "Anti-spin should stop the car spinning out.");
            Assert.Greater(lowestUprightness, 0.5f, "The car must not roll over.");
            Assert.IsFalse(Car.Drift.IsDrifting, "The drift should end once the handbrake is released and steering straightened.");
        }

        [UnityTest]
        public IEnumerator DroppedTiltedCar_LandsUprightOnAllWheels()
        {
            yield return CreateCourse(null, SpawnPosition + Vector3.up * 4f, Quaternion.Euler(10f, 0f, 25f), settle: false);
            float longestAirTime = 0f;

            yield return Drive(default, 4f, () => longestAirTime = Mathf.Max(longestAirTime, Car.AirborneTime));

            TestContext.WriteLine($"Air time: {longestAirTime:0.00} s, uprightness after landing: {Uprightness():0.000}");
            Assert.Greater(longestAirTime, 0.5f, "The car should have been airborne.");
            Assert.AreEqual(4, Car.GroundedWheelCount, "All wheels should be down after landing.");
            AssertUpright(0.98f);
        }

        [UnityTest]
        public IEnumerator ResetVehicle_RightsUpsideDownCarKeepingHeading()
        {
            yield return CreateCourse(null, SpawnPosition + Vector3.up, Quaternion.Euler(0f, 30f, 180f), settle: true);
            Assert.Less(Uprightness(), 0f, "Precondition: the car should be upside down.");

            Car.ResetVehicle();
            yield return Drive(default, SettleSeconds);

            AssertUpright(0.99f);
            Assert.AreEqual(4, Car.GroundedWheelCount);
            Assert.That(Vector3.SignedAngle(Vector3.forward, Heading(), Vector3.up), Is.EqualTo(30f).Within(5f), "Reset should keep the heading.");
        }
    }
}
