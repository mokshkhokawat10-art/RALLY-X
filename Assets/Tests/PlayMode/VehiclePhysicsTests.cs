using System.Collections;
using System.Collections.Generic;
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
    /// The vehicle physics added in Phase 11, driven for real on the flat test course: each drivetrain (all-, front- and
    /// rear-wheel drive) drives the wheels it should, putting the power down costs the driven tyres sideways grip on every
    /// car, cornering loads the outside tyres, mud spins the wheels of a rear-driven car where asphalt doesn't, a
    /// limited-slip differential moves drive to the wheel that grips, the throttle lifts the nose in the air, and a knock
    /// that spins the car spins it less with impact yaw damping. Drivetrains and differentials are set on copies of a
    /// car's handling, and impact yaw damping is compared against a copy with it switched off. Measured values are printed.
    /// </summary>
    public sealed class VehiclePhysicsTests : VehicleTestBase
    {
        private const string KestrelPrefabPath = "Assets/Prefabs/Vehicles/Kestrel.prefab";
        private const string TitanPrefabPath = "Assets/Prefabs/Vehicles/Titan.prefab";
        private const string MudSurfacePath = "Assets/Settings/Surfaces/Surface_Mud.asset";

        private readonly List<Object> configCopies = new List<Object>();

        /// <summary>The garage cars: all-wheel drive (Falcon), front-wheel drive (Kestrel) and rear-biased all-wheel drive (Titan).</summary>
        public static IEnumerable<string> CarPrefabs()
        {
            yield return CarPrefabPath;
            yield return KestrelPrefabPath;
            yield return TitanPrefabPath;
        }

        [UnityTearDown]
        public IEnumerator DestroyConfigCopies()
        {
            foreach (var copy in configCopies)
            {
                if (copy != null)
                    Object.Destroy(copy);
            }

            configCopies.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Drivetrain_DrivesItsOwnWheels([Values] Drivetrain drivetrain)
        {
            yield return CreateCourse();
            Car.SetConfig(CopyConfig(("drivetrain", (float)drivetrain)));
            float front = 0f;
            float rear = 0f;
            yield return Drive(FullThrottle, 2f, () =>
            {
                foreach (var wheel in Car.Wheels)
                {
                    if (wheel.IsFront)
                        front += Mathf.Abs(wheel.DriveForce);
                    else
                        rear += Mathf.Abs(wheel.DriveForce);
                }
            });

            float frontShare = front / Mathf.Max(front + rear, 1f);
            TestContext.WriteLine($"Falcon RX as {Car.Config.Drivetrain}: front share of the drive {frontShare:0.00} " +
                                  $"(drivetrain {Car.Config.FrontDriveShare:0.00}), {Car.SpeedKmh:0} km/h after 2 s");
            Assert.Greater(Car.SpeedKmh, 30f, "The car should pull away.");
            Assert.AreEqual(Car.Config.FrontDriveShare, frontShare, 0.05f, "The drive should go to the wheels the drivetrain drives.");
        }

        [UnityTest]
        public IEnumerator Power_CostsTheDrivenTyresSidewaysGrip([ValueSource(nameof(CarPrefabs))] string prefabPath)
        {
            yield return CreateCourse(null, SpawnPosition, Quaternion.identity, settle: true, carPrefab: LoadAsset<GameObject>(prefabPath));
            float front = 1f;
            float rear = 1f;
            yield return Drive(FullThrottle, 1.5f, () =>
            {
                foreach (var wheel in Car.Wheels)
                {
                    if (wheel.IsFront)
                        front = Mathf.Min(front, wheel.SidewaysGrip);
                    else
                        rear = Mathf.Min(rear, wheel.SidewaysGrip);
                }
            });

            float frontShare = Car.Config.FrontDriveShare;
            TestContext.WriteLine($"{Car.name}: {Car.Config.Drivetrain}, least sideways grip left under full power: front {front:0.00}, rear {rear:0.00}");
            AssertGripLoss(front, frontShare > 0f, "front");
            AssertGripLoss(rear, frontShare < 1f, "rear");
        }

        [UnityTest]
        public IEnumerator Cornering_LoadsTheOutsideTyres()
        {
            yield return CreateCourse();
            yield return AccelerateTo(60f);
            var turn = new VehicleInputState(0.4f, 0f, 0.6f, false);
            yield return Drive(turn, 1.5f);

            float left = 0f;
            float right = 0f;
            float lateralG = 0f;
            int samples = 0;
            yield return Drive(turn, 0.5f, () =>
            {
                foreach (var wheel in Car.Wheels)
                {
                    if (wheel.Side < 0)
                        left += wheel.Load;
                    else if (wheel.Side > 0)
                        right += wheel.Load;
                }

                lateralG += Car.LocalAcceleration.x / Physics.gravity.magnitude;
                samples++;
            });

            TestContext.WriteLine($"Right-hand turn at {Car.SpeedKmh:0} km/h, {lateralG / samples:0.00} g: outside (left) to inside tyre load {left / Mathf.Max(right, 1f):0.00}");
            Assert.Greater(lateralG / samples, 0.3f, "Precondition: the car should be cornering.");
            Assert.Greater(left, right * 1.2f, "Cornering should load the outside tyres.");
        }

        [UnityTest]
        public IEnumerator Mud_SpinsTheWheelsWhereAsphaltDoesNot()
        {
            var titan = LoadAsset<GameObject>(TitanPrefabPath);
            yield return CreateCourse(null, SpawnPosition, Quaternion.identity, settle: true, carPrefab: titan);
            Car.SetConfig(CopyConfig(("drivetrain", (float)Drivetrain.RearWheelDrive)));
            float asphalt = 0f;
            yield return Drive(FullThrottle, 3f, () => asphalt = Mathf.Max(asphalt, RearWheelSpin()));

            Object.Destroy(Car.gameObject);
            Object.Destroy(GameObject.Find("TestGround"));
            yield return null;

            yield return CreateCourse(LoadAsset<SurfaceType>(MudSurfacePath), SpawnPosition, Quaternion.identity, settle: true, carPrefab: titan);
            Car.SetConfig(CopyConfig(("drivetrain", (float)Drivetrain.RearWheelDrive)));
            float mud = 0f;
            float mudGrip = 1f;
            yield return Drive(FullThrottle, 3f, () =>
            {
                mud = Mathf.Max(mud, RearWheelSpin());
                foreach (var wheel in Car.Wheels)
                {
                    if (!wheel.IsFront)
                        mudGrip = Mathf.Min(mudGrip, wheel.SidewaysGrip);
                }
            });

            TestContext.WriteLine($"Titan as rear-wheel drive at full throttle: most rear wheelspin on asphalt {asphalt:0.00}, on mud {mud:0.00}; " +
                                  $"least rear sideways grip left on mud {mudGrip:0.00}");
            Assert.Less(asphalt, 0.01f, "The tyres should put the power down on asphalt.");
            Assert.Greater(mud, 0.02f, "The rear wheels should spin on mud.");
        }

        [UnityTest]
        public IEnumerator LimitedSlip_SendsDriveToTheWheelThatGrips()
        {
            var titan = LoadAsset<GameObject>(TitanPrefabPath);
            yield return CreateCourse(null, SpawnPosition, Quaternion.identity, settle: false, carPrefab: titan);

            // A strip of mud under the car's left wheels, just above the asphalt, so they spin while the right ones grip.
            var mud = Track(GameObject.CreatePrimitive(PrimitiveType.Plane));
            mud.name = "MudStrip";
            mud.transform.position = SpawnPosition + new Vector3(-5f, -0.29f, 50f);
            mud.transform.localScale = new Vector3(1f, 1f, 20f);
            mud.AddComponent<SurfaceMarker>().Surface = LoadAsset<SurfaceType>(MudSurfacePath);

            var original = Car.Config;
            var drive = new float[2];
            for (int run = 0; run < 2; run++)
            {
                Car.SetConfig(CopyConfig(original, ("drivetrain", (float)Drivetrain.RearWheelDrive), ("limitedSlip", run == 0 ? 0f : 1f)));
                Car.PlaceAt(SpawnPosition, Quaternion.identity);
                yield return Drive(default, SettleSeconds);
                Assert.AreEqual("Mud", Wheel(front: false, side: -1).SurfaceType.DisplayName, "Precondition: the left rear wheel is on the mud.");
                Assert.AreEqual("Asphalt", Wheel(front: false, side: 1).SurfaceType.DisplayName, "Precondition: the right rear wheel is on asphalt.");

                float total = 0f;
                yield return Drive(FullThrottle, 1f, () =>
                {
                    foreach (var wheel in Car.Wheels)
                        total += Mathf.Abs(wheel.DriveForce) * Time.fixedDeltaTime;
                });
                drive[run] = total;
            }

            TestContext.WriteLine($"Titan as rear-wheel drive with its left wheels on mud, drive put down in 1 s: open differential {drive[0]:0} N·s, " +
                                  $"locked {drive[1]:0} N·s");
            Assert.Greater(drive[1], drive[0] * 1.03f, "A locked differential should pass the spinning wheel's drive to the one that grips.");
        }

        [UnityTest]
        public IEnumerator AirThrottle_LiftsTheNose()
        {
            yield return CreateCourse(null, SpawnPosition + Vector3.up * 14f, Quaternion.identity, settle: false);
            yield return Drive(default, 1.2f);
            float level = NosePitch();
            Car.PlaceAt(SpawnPosition + Vector3.up * 14f, Quaternion.identity);
            yield return Drive(FullThrottle, 1.2f);
            float throttle = NosePitch();

            TestContext.WriteLine($"Nose pitch after 1.2 s in the air: no input {level:0.0} deg, full throttle {throttle:0.0} deg (positive = nose down); " +
                                  $"airborne {Car.AirborneTime:0.00} s");
            Assert.Greater(Car.AirborneTime, 1.1f, "Precondition: still in the air.");
            Assert.Less(throttle, level - 1.5f, "Holding the throttle in the air should lift the nose.");
        }

        [UnityTest]
        public IEnumerator OffsetKnock_SpinsTheCarLessWithImpactDamping()
        {
            yield return CreateCourse();

            // A post 25 m ahead that the right front corner clips (0.4 m of the car's width).
            var post = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            post.name = "Post";
            post.transform.SetPositionAndRotation(SpawnPosition + new Vector3(0.75f, 0.8f, 25f), Quaternion.identity);
            post.transform.localScale = new Vector3(0.5f, 1.6f, 0.5f);
            var original = Car.Config;

            var spin = new float[2];
            for (int run = 0; run < 2; run++)
            {
                Car.SetConfig(run == 0 ? original : CopyConfig(("impactYawDamping", 0f)));
                Car.PlaceAt(SpawnPosition, Quaternion.identity);
                yield return Drive(default, SettleSeconds);
                int hits = 0;
                void CountHit(float impulse, Vector3 point) => hits++;
                Car.Impact += CountHit;

                // Arrive at 40 km/h with a little throttle and no steering; measure the heading 1 s after the hit.
                Car.Body.linearVelocity = Vector3.forward * (40f / 3.6f);
                float sinceHit = 0f;
                yield return Drive(new VehicleInputState(0.3f, 0f, 0f, false), 2.5f, () =>
                {
                    if (hits == 0 || sinceHit > 1f)
                        return;
                    sinceHit += Time.fixedDeltaTime;
                    spin[run] = Mathf.Abs(Vector3.SignedAngle(Vector3.forward, Heading(), Vector3.up));
                });
                Car.Impact -= CountHit;

                Assert.Greater(hits, 0, "Precondition: the car should hit the post.");
                AssertUpright(0.9f);
            }

            TestContext.WriteLine($"Right front corner clipping a post at 40 km/h, heading change 1 s after the hit: with impact yaw damping " +
                                  $"({original.ImpactYawDamping}/s) {spin[0]:0.0} deg, without {spin[1]:0.0} deg");
            Assert.Greater(spin[1], 5f, "Precondition: the hit should spin the car.");
            Assert.Less(spin[0], spin[1] - 2f, "Impact yaw damping should spin the car less after a knock.");
        }

        private static void AssertGripLoss(float least, bool driven, string axle)
        {
            if (driven)
                Assert.Less(least, 0.99f, $"The driven {axle} tyres should give up some sideways grip under full power.");
            else
                Assert.AreEqual(1f, least, 1e-4f, $"The undriven {axle} tyres should keep all their sideways grip.");
        }

        private float RearWheelSpin()
        {
            float spin = 0f;
            foreach (var wheel in Car.Wheels)
            {
                if (!wheel.IsFront)
                    spin = Mathf.Max(spin, wheel.WheelSpin);
            }

            return spin;
        }

        private VehicleWheel Wheel(bool front, int side)
        {
            foreach (var wheel in Car.Wheels)
            {
                if (wheel.IsFront == front && wheel.Side == side)
                    return wheel;
            }

            Assert.Fail($"The car has no {(front ? "front" : "rear")} wheel on side {side}.");
            return null;
        }

        /// <summary>A copy of the car's handling with some values changed, destroyed after the test.</summary>
        private VehicleConfig CopyConfig(params (string field, float value)[] changes) => CopyConfig(Car.Config, changes);

        /// <summary>A copy of a handling config with some values changed (a choice such as the drivetrain by its index).</summary>
        private VehicleConfig CopyConfig(VehicleConfig source, params (string field, float value)[] changes)
        {
#if UNITY_EDITOR
            var copy = Object.Instantiate(source);
            configCopies.Add(copy);
            var serialized = new SerializedObject(copy);
            foreach (var (field, value) in changes)
            {
                var property = serialized.FindProperty(field);
                if (property.propertyType == SerializedPropertyType.Enum)
                    property.enumValueIndex = Mathf.RoundToInt(value);
                else
                    property.floatValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return copy;
#else
            Assert.Ignore("These tests change handling values and run only in the Unity Editor.");
            return null;
#endif
        }

        /// <summary>Degrees the nose points below the horizon.</summary>
        private float NosePitch() => -Mathf.Asin(Mathf.Clamp((Car.Body.rotation * Vector3.forward).y, -1f, 1f)) * Mathf.Rad2Deg;
    }
}
