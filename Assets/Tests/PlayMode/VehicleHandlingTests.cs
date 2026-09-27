using System.Collections;
using NUnit.Framework;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.TestTools;

namespace RallyX.Tests
{
    /// <summary>
    /// The handling added in Phase 10, driven for real on the flat test course: load moves to the front tyres under
    /// braking and to the rear under power, hard landings don't bottom the body out and settle without bouncing, steering
    /// in the air turns the car at a limited rate that fades without input, the brake dips the nose in the air, the car
    /// lines up with a slope it is about to land on, and the throttle holds a drift the handbrake started. Measured values
    /// are printed.
    /// </summary>
    public sealed class VehicleHandlingTests : VehicleTestBase
    {
        [UnityTest]
        public IEnumerator Braking_LoadsTheFrontTyres_AndPowerTheRear()
        {
            yield return CreateCourse();
            float atRest = FrontToRearLoad();

            float launch = float.MaxValue;
            yield return Drive(FullThrottle, 1f, () => launch = Mathf.Min(launch, FrontToRearLoad()));
            yield return AccelerateTo(90f);
            float braking = 0f;
            yield return Drive(new VehicleInputState(0f, 1f, 0f, false), 1f, () => braking = Mathf.Max(braking, FrontToRearLoad()));

            TestContext.WriteLine($"Front/rear tyre load: at rest {atRest:0.00}, pulling away {launch:0.00}, braking from 90 km/h {braking:0.00}");
            Assert.Greater(braking, atRest * 1.25f, "Braking should move load onto the front tyres.");
            Assert.Less(launch, atRest * 0.9f, "Accelerating should move load onto the rear tyres.");
        }

        [UnityTest]
        public IEnumerator HardLanding_DoesNotBottomOutAndSettles()
        {
            yield return CreateCourse(null, SpawnPosition + Vector3.up * 5f, Quaternion.identity, settle: false);
            int bodyHits = 0;
            Car.Impact += (_, _) => bodyHits++;
            float lowest = float.MaxValue;
            bool landed = false;
            int bounces = 0;
            float lastVertical = 0f;

            yield return Drive(default, 4f, () =>
            {
                lowest = Mathf.Min(lowest, Car.Body.position.y - SpawnPosition.y);
                landed |= Car.IsGrounded;
                float vertical = Car.Body.linearVelocity.y;
                if (landed && Mathf.Abs(vertical) > 0.15f && Mathf.Sign(vertical) != Mathf.Sign(lastVertical) && lastVertical != 0f)
                    bounces++;
                if (Mathf.Abs(vertical) > 0.15f)
                    lastVertical = vertical;
            });

            TestContext.WriteLine($"5 m drop: lowest body {lowest:0.000} m below ride height, body hits {bodyHits}, bounces {bounces}, " +
                                  $"vertical speed now {Car.Body.linearVelocity.y:0.000} m/s");
            Assert.AreEqual(0, bodyHits, "The body should never touch the ground: the bump stops catch it.");
            Assert.LessOrEqual(bounces, 3, "Firm rebound damping should settle the car within a couple of bounces.");
            Assert.Less(Mathf.Abs(Car.Body.linearVelocity.y), 0.1f, "The car should be at rest again.");
            AssertUpright(0.98f);
        }

        [UnityTest]
        public IEnumerator AirSteering_TurnsAtALimitedRateThatFades()
        {
            yield return CreateCourse(null, SpawnPosition + Vector3.up * 14f, Quaternion.identity, settle: false);
            float fastest = 0f;
            yield return Drive(new VehicleInputState(0f, 0f, 1f, false), 0.9f, () => fastest = Mathf.Max(fastest, Mathf.Abs(YawRate())));
            float whenSteering = YawRate();
            yield return Drive(default, 0.6f);
            float afterRelease = YawRate();

            TestContext.WriteLine($"Air yaw rate: fastest {fastest:0.00} rad/s (limit {Car.Config.AirYawRate}), 0.6 s after letting go {afterRelease:0.00} rad/s; " +
                                  $"airborne {Car.AirborneTime:0.00} s");
            Assert.Greater(Car.AirborneTime, 1.2f, "Precondition: still in the air.");
            Assert.Greater(Mathf.Abs(whenSteering), Car.Config.AirYawRate * 0.7f, "Steering should turn the car in the air.");
            Assert.LessOrEqual(fastest, Car.Config.AirYawRate * 1.1f, "The air yaw rate should be limited.");
            Assert.Less(Mathf.Abs(afterRelease), 0.2f, "Without steering the spin should fade.");
        }

        [UnityTest]
        public IEnumerator AirBrake_DipsTheNose()
        {
            yield return CreateCourse(null, SpawnPosition + Vector3.up * 10f, Quaternion.identity, settle: false);
            yield return Drive(default, 0.8f);
            float level = NosePitch();
            Car.PlaceAt(SpawnPosition + Vector3.up * 10f, Quaternion.identity);
            yield return Drive(new VehicleInputState(0f, 1f, 0f, false), 0.8f);
            float braking = NosePitch();

            TestContext.WriteLine($"Nose pitch after 0.8 s in the air: no input {level:0.0} deg, brake held {braking:0.0} deg (positive = nose down)");
            Assert.Greater(braking, level + 5f, "Holding the brake in the air should dip the nose.");
        }

        [UnityTest]
        public IEnumerator Airborne_LinesUpWithTheSlopeBelow()
        {
            const float slope = 15f;
            yield return CreateCourse(null, SpawnPosition + Vector3.up * 9f, Quaternion.identity, settle: false);
            var ramp = Track(GameObject.CreatePrimitive(PrimitiveType.Plane));
            ramp.transform.SetPositionAndRotation(SpawnPosition + Vector3.up * 2f, Quaternion.Euler(-slope, 0f, 0f));
            ramp.transform.localScale = new Vector3(5f, 1f, 5f);
            Vector3 normal = ramp.transform.up;

            float atTouchdown = -1f;
            yield return Drive(default, 2.5f, () =>
            {
                if (atTouchdown < 0f && Car.IsGrounded)
                    atTouchdown = Vector3.Angle(Car.Body.rotation * Vector3.up, normal);
            });

            TestContext.WriteLine($"Angle between the car and a {slope} deg slope at touchdown: {atTouchdown:0.0} deg");
            Assert.GreaterOrEqual(atTouchdown, 0f, "The car should land on the slope.");
            Assert.Less(atTouchdown, slope - 5f, "The car should have turned towards the slope it lands on.");
        }

        [UnityTest]
        public IEnumerator Throttle_HoldsAHandbrakeDrift()
        {
            float onThrottle = 0f;
            float offThrottle = 0f;
            float widest = 0f;
            yield return CreateCourse();
            for (int run = 0; run < 2; run++)
            {
                float throttle = run == 0 ? 1f : 0f;
                Car.PlaceAt(SpawnPosition, Quaternion.identity);
                yield return Drive(default, SettleSeconds);
                yield return AccelerateTo(70f);
                yield return Drive(new VehicleInputState(0.6f, 0f, 1f, true), 0.6f);

                float lasted = 0f;
                yield return Drive(new VehicleInputState(throttle, 0f, 0.35f, false), 3f, () =>
                {
                    if (Car.Drift.IsDrifting)
                        lasted = Car.Drift.CurrentDriftDuration;
                    widest = Mathf.Max(widest, Mathf.Abs(TravelAngle()));
                });

                if (run == 0)
                    onThrottle = lasted;
                else
                    offThrottle = lasted;
            }

            TestContext.WriteLine($"Drift after the handbrake: {onThrottle:0.00} s on full throttle, {offThrottle:0.00} s off it; widest slip {widest:0} deg");
            Assert.Greater(onThrottle, offThrottle + 0.3f, "Full throttle should hold the drift longer than lifting off.");
            Assert.Less(widest, Car.Config.MaxDriftAngle + 20f, "Anti-spin should still stop a spin.");
        }

        /// <summary>Summed tyre load on the front axle over the rear.</summary>
        private float FrontToRearLoad()
        {
            float front = 0f;
            float rear = 0f;
            foreach (var wheel in Car.Wheels)
            {
                if (wheel.IsFront)
                    front += wheel.Load;
                else
                    rear += wheel.Load;
            }

            return rear > 1f ? front / rear : 0f;
        }

        private float YawRate() => Vector3.Dot(Car.Body.angularVelocity, Car.Body.rotation * Vector3.up);

        /// <summary>Degrees the nose points below the horizon.</summary>
        private float NosePitch() => -Mathf.Asin(Mathf.Clamp((Car.Body.rotation * Vector3.forward).y, -1f, 1f)) * Mathf.Rad2Deg;
    }
}
