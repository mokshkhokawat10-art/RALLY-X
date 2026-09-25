using System.Collections;
using NUnit.Framework;
using RallyX.Cameras;
using RallyX.Core;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.TestTools;

namespace RallyX.Tests
{
    /// <summary>
    /// Drives the real car with the chase camera attached and checks framing, FOV, drift/reverse behaviour,
    /// obstacle handling and shake. Limits come from the camera config, so tuning doesn't break the tests.
    /// Camera-versus-car values are read from a sampler that runs after the camera each frame.
    /// </summary>
    public sealed class ChaseCameraTests : VehicleTestBase
    {
        private const string CameraConfigPath = "Assets/Settings/Cameras/ChaseCamera_Default.asset";
        private const float NearClipPlane = 0.1f;

        // Smoothing needs a moment to settle after the camera is created.
        private const float CameraSettleSeconds = 1f;

        private ChaseCamera chaseCamera;
        private ChaseCameraConfig config;
        private CameraFrameSampler sampler;

        [UnityTest]
        public IEnumerator AtRest_SitsBehindCarAtConfiguredDistanceAndHeight()
        {
            yield return CreateCourseWithCamera();
            var frame = sampler.Latest;

            TestContext.WriteLine($"At rest: distance {frame.HorizontalDistance:0.00} m (config {config.Distance}), " +
                                  $"height {frame.HeightAboveCar:0.00} m (config {config.Height}), FOV {frame.FieldOfView:0.0}");
            Assert.That(frame.HorizontalDistance, Is.EqualTo(config.Distance).Within(0.25f));
            Assert.That(frame.HeightAboveCar, Is.EqualTo(config.Height).Within(0.25f));
            Assert.Less(frame.BehindDot, -0.97f, "The camera should be directly behind the car.");
            Assert.That(frame.FieldOfView, Is.EqualTo(config.BaseFieldOfView).Within(0.5f));
            Assert.AreEqual(0f, chaseCamera.ShakeTrauma, "Nothing should shake an idle car.");
            AssertFramesClean();
        }

        [UnityTest]
        public IEnumerator AtSpeed_DistanceStaysStableAndFieldOfViewWidens()
        {
            yield return CreateCourseWithCamera();
            sampler.ResetStats();

            yield return Drive(FullThrottle, 15f);

            var frame = sampler.Latest;
            float maxAllowedDistance = config.Distance + config.ExtraDistanceAtSpeed + 0.5f;
            float wideFieldOfView = Mathf.Lerp(config.BaseFieldOfView, config.MaxFieldOfView, 0.75f);
            TestContext.WriteLine($"At {Car.SpeedKmh:0} km/h: distance {frame.HorizontalDistance:0.00} m " +
                                  $"(largest {sampler.MaxHorizontalDistance:0.00}), FOV {frame.FieldOfView:0.0}, frames {sampler.Frames}");
            Assert.LessOrEqual(sampler.MaxHorizontalDistance, maxAllowedDistance, "The camera must not fall behind at speed.");
            Assert.GreaterOrEqual(frame.HorizontalDistance, config.Distance - 0.25f);
            Assert.Greater(frame.FieldOfView, wideFieldOfView, "FOV should widen with speed.");
            AssertFramesClean();
        }

        [UnityTest]
        public IEnumerator Reversing_StaysBehindHeadingWithoutFlipping()
        {
            yield return CreateCourseWithCamera();
            sampler.ResetStats();

            yield return Drive(new VehicleInputState(0f, 1f, 0f, false), 5f);

            TestContext.WriteLine($"Reversing at {-Car.ForwardSpeed * 3.6f:0} km/h: worst behind-dot {sampler.WorstBehindDot:0.000}, " +
                                  $"closest {sampler.MinDistanceToCar:0.00} m");
            Assert.Less(Car.ForwardSpeed, -5f, "Precondition: the car should be reversing.");
            Assert.Less(sampler.WorstBehindDot, -0.95f, "The camera should stay behind the car's nose, not swing round.");
            Assert.GreaterOrEqual(sampler.MinDistanceToCar, config.MinDistance, "The reversing car must not close in on the camera.");
            AssertFramesClean();
        }

        [UnityTest]
        public IEnumerator Drifting_SwingIsBoundedAndCarStaysInView()
        {
            yield return CreateCourseWithCamera();
            yield return AccelerateTo(90f);
            sampler.ResetStats();

            yield return Drive(new VehicleInputState(0.6f, 0f, 1f, true), 1.2f);
            float largestOffset = sampler.MaxAbsYawOffset;
            yield return Drive(new VehicleInputState(0.3f, 0f, 0f, false), 3f);

            TestContext.WriteLine($"Camera offset from the nose while drifting: up to {largestOffset:0.0}°; " +
                                  $"worst aim outside the nose-to-travel arc: {sampler.MaxYawOutsideTravelArc:0.0}°; " +
                                  $"after recovery {sampler.Latest.YawOffset:0.0}°");
            // Rotation lag and the drift swing both turn the camera towards the direction of travel.
            // It must never look beyond it (overshoot) or away from it (swing in the wrong direction).
            Assert.LessOrEqual(sampler.MaxYawOutsideTravelArc, 10f, "The camera should aim between the car's nose and its direction of travel.");
            Assert.Less(Mathf.Abs(sampler.Latest.YawOffset), 5f, "The camera should settle behind the car after the drift.");
            AssertFramesClean();
        }

        [UnityTest]
        public IEnumerator WallBehindCar_PullsCameraInThenEasesBackOut()
        {
            yield return CreateCourseWithCamera();
            const float wallGap = 4f;
            var wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.SetPositionAndRotation(Car.Body.position - Heading() * wallGap + Vector3.up * 3f, Quaternion.LookRotation(Heading()));
            wall.transform.localScale = new Vector3(20f, 6f, 0.5f);
            sampler.ResetStats();

            yield return WaitSimulated(0.5f);
            var blocked = sampler.Latest;
            AssertFramesClean();

            Object.Destroy(wall);
            yield return WaitSimulated(2f);

            TestContext.WriteLine($"Wall {wallGap} m behind: camera at {blocked.HorizontalDistance:0.00} m " +
                                  $"({blocked.DistanceToCar:0.00} m from the car); after removal {sampler.Latest.HorizontalDistance:0.00} m");
            Assert.Less(blocked.HorizontalDistance, wallGap - 0.25f, "The camera should stay on the car's side of the wall.");
            Assert.GreaterOrEqual(blocked.DistanceToCar, config.MinDistance - 0.01f, "The camera must not be pulled into the car.");
            Assert.That(sampler.Latest.HorizontalDistance, Is.EqualTo(config.Distance).Within(0.3f), "The camera should ease back once clear.");
        }

        [UnityTest]
        public IEnumerator SteepSlopeBehindCar_KeepsCameraAboveGround()
        {
            yield return CreateCourseWithCamera();
            CreateSlopeBehindCar(angle: 35f, gap: 2.5f);
            sampler.ResetStats();

            yield return WaitSimulated(0.5f);

            Vector3 cameraPosition = chaseCamera.transform.position;
            float clearance = cameraPosition.y - GroundHeightBelow(cameraPosition);
            TestContext.WriteLine($"Slope behind car: camera {clearance:0.00} m above the slope, {sampler.Latest.HorizontalDistance:0.00} m behind");
            Assert.GreaterOrEqual(clearance, config.GroundClearance - 0.05f, "The camera should keep its ground clearance.");
            AssertFramesClean();
        }

        [UnityTest]
        public IEnumerator HardLanding_ShakesThenSettles()
        {
            yield return CreateCourseWithCamera(SpawnPosition + Vector3.up * 4f, settleCar: false);
            float strongestShake = 0f;

            yield return Drive(default, 2f, () => strongestShake = Mathf.Max(strongestShake, chaseCamera.ShakeTrauma));
            yield return WaitSimulated(3f);

            TestContext.WriteLine($"Landing shake trauma peak: {strongestShake:0.00}");
            Assert.Greater(strongestShake, 0.15f, "A hard landing should shake the camera.");
            Assert.AreEqual(0f, chaseCamera.ShakeTrauma, "Shake should fade out completely.");
            AssertFramesClean();
        }

        [UnityTest]
        public IEnumerator HittingWall_ShakesCamera()
        {
            yield return CreateCourseWithCamera();
            var wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.SetPositionAndRotation(Car.Body.position + Heading() * 40f + Vector3.up * 1.5f, Quaternion.LookRotation(Heading()));
            wall.transform.localScale = new Vector3(20f, 3f, 1f);
            float strongestShake = 0f;
            float fastest = 0f;

            yield return Drive(FullThrottle, 5f, () =>
            {
                strongestShake = Mathf.Max(strongestShake, chaseCamera.ShakeTrauma);
                fastest = Mathf.Max(fastest, Car.SpeedKmh);
            });

            TestContext.WriteLine($"Wall hit at up to {fastest:0} km/h: shake trauma peak {strongestShake:0.00}");
            Assert.Greater(strongestShake, 0.2f, "Hitting a wall should shake the camera.");
            AssertFramesClean();
        }

        [UnityTest]
        public IEnumerator Teleport_SnapsInsteadOfSwinging()
        {
            yield return CreateCourseWithCamera();
            Vector3 destination = Car.Body.position + new Vector3(200f, 0f, 0f);
            Quaternion facingSideways = Quaternion.Euler(0f, 90f, 0f);
            Car.Body.position = destination;
            Car.Body.rotation = facingSideways;
            Car.transform.SetPositionAndRotation(destination, facingSideways);

            yield return WaitSimulated(0.1f);
            yield return null;

            var frame = sampler.Latest;
            TestContext.WriteLine($"After teleport: distance {frame.HorizontalDistance:0.00} m, behind-dot {frame.BehindDot:0.000}");
            Assert.That(frame.HorizontalDistance, Is.EqualTo(config.Distance).Within(0.3f), "The camera should snap to the car.");
            Assert.Less(frame.BehindDot, -0.97f, "The camera should snap behind the new heading.");
        }

        private IEnumerator CreateCourseWithCamera() => CreateCourseWithCamera(SpawnPosition, settleCar: true);

        private IEnumerator CreateCourseWithCamera(Vector3 carPosition, bool settleCar)
        {
            yield return CreateCourse(null, carPosition, Quaternion.identity, settleCar);

            config = LoadAsset<ChaseCameraConfig>(CameraConfigPath);
            var cameraObject = Track(new GameObject("TestChaseCamera"));
            var cameraComponent = cameraObject.AddComponent<Camera>();
            cameraComponent.nearClipPlane = NearClipPlane;
            chaseCamera = cameraObject.AddComponent<ChaseCamera>();
            chaseCamera.Config = config;
            chaseCamera.Target = Car;
            sampler = cameraObject.AddComponent<CameraFrameSampler>();
            sampler.Initialize(cameraComponent, Car, config.LookHeight);

            if (settleCar)
                yield return WaitSimulated(CameraSettleSeconds);
            else
                yield return null;
        }

        private IEnumerator WaitSimulated(float seconds) => Drive(Car.CurrentInput, seconds);

        private void AssertFramesClean()
        {
            Assert.Greater(sampler.Frames, 0, "The sampler recorded no frames.");
            Assert.AreEqual(0, sampler.FramesCarOutOfView, "The car left the screen.");
            Assert.AreEqual(0, sampler.FramesInsideCollider, "The camera entered a collider (the car, a wall or the ground).");
        }

        private void CreateSlopeBehindCar(float angle, float gap)
        {
            // A tilted slab whose low edge is `gap` metres behind the car and which rises away from it.
            const float length = 20f;
            const float thickness = 1f;
            float radians = angle * Mathf.Deg2Rad;
            float centerHeight = 0.5f * length * Mathf.Sin(radians) - 0.5f * thickness * Mathf.Cos(radians);
            float centerBack = gap + 0.5f * length * Mathf.Cos(radians) + 0.5f * thickness * Mathf.Sin(radians);

            Vector3 carBase = Vector3.ProjectOnPlane(Car.Body.position, Vector3.up);
            var slope = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            slope.name = "TestSlope";
            slope.transform.SetPositionAndRotation(
                carBase - Heading() * centerBack + Vector3.up * centerHeight,
                Quaternion.LookRotation(Heading()) * Quaternion.Euler(angle, 0f, 0f));
            slope.transform.localScale = new Vector3(30f, thickness, length);
        }

        private static float GroundHeightBelow(Vector3 position)
        {
            const float probeHeight = 100f;
            int groundMask = ~LayerMask.GetMask(GameLayers.Vehicle);
            return Physics.Raycast(position + Vector3.up * probeHeight, Vector3.down, out RaycastHit hit, probeHeight * 2f, groundMask)
                ? hit.point.y
                : float.NegativeInfinity;
        }
    }
}
