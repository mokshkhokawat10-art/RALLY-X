using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RallyX.Racing;
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
    /// Shared fixture: a large flat test plane, the real car prefab, scripted driving and cleanup.
    /// Physics runs at its normal fixed timestep; time scale only makes the tests finish sooner.
    /// </summary>
    public abstract class VehicleTestBase
    {
        protected const string CarPrefabPath = "Assets/Prefabs/Vehicles/RallyCar.prefab";
        protected const float SettleSeconds = 2f;

        protected static readonly WaitForFixedUpdate NextPhysicsStep = new WaitForFixedUpdate();
        protected static readonly VehicleInputState FullThrottle = new VehicleInputState(1f, 0f, 0f, false);

        // 3 km square ground with the car near one edge, so long runs stay on it.
        protected static readonly Vector3 SpawnPosition = new Vector3(0f, 0.3f, -1400f);

        private const float SimulationSpeedup = 8f;
        private const float GroundScale = 300f;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private float previousTimeScale;

        protected VehicleController Car { get; set; }

        [SetUp]
        public void SetUpSimulation()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = SimulationSpeedup;
        }

        [UnityTearDown]
        public IEnumerator TearDownScene()
        {
            Time.timeScale = previousTimeScale;
            foreach (var gameObject in spawned)
            {
                if (gameObject != null)
                    Object.Destroy(gameObject);
            }

            spawned.Clear();
            Car = null;
            yield return null;
        }

        /// <summary>Registers an object to be destroyed after the test.</summary>
        protected GameObject Track(GameObject gameObject)
        {
            spawned.Add(gameObject);
            return gameObject;
        }

        protected IEnumerator CreateCourse(SurfaceType surface = null) =>
            CreateCourse(surface, SpawnPosition, Quaternion.identity, settle: true);

        protected IEnumerator CreateCourse(SurfaceType surface, Vector3 position, Quaternion rotation, bool settle)
        {
            var ground = Track(GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.name = "TestGround";
            ground.transform.localScale = new Vector3(GroundScale, 1f, GroundScale);
            if (surface != null)
                ground.AddComponent<SurfaceMarker>().Surface = surface;

            var carObject = Track(Object.Instantiate(LoadAsset<GameObject>(CarPrefabPath), position, rotation));
            Car = carObject.GetComponent<VehicleController>();
            Assert.IsNotNull(Car, "The car prefab needs a VehicleController.");

            if (settle)
                yield return Drive(default, SettleSeconds);
        }

        protected IEnumerator Drive(VehicleInputState input, float seconds, Action afterEachStep = null)
        {
            Car.SetInput(input);
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.fixedDeltaTime)
            {
                yield return NextPhysicsStep;
                afterEachStep?.Invoke();
            }
        }

        protected IEnumerator AccelerateTo(float speedKmh)
        {
            const float timeout = 20f;
            Car.SetInput(FullThrottle);
            for (float elapsed = 0f; Car.SpeedKmh < speedKmh; elapsed += Time.fixedDeltaTime)
            {
                Assert.Less(elapsed, timeout, $"The car did not reach {speedKmh} km/h within {timeout} s.");
                yield return NextPhysicsStep;
            }
        }

        /// <summary>Takes every racer except the player out of the race and removes their cars.</summary>
        protected static void RemoveOpponents(RaceManager race)
        {
            foreach (var participant in new List<RaceParticipant>(race.Participants))
            {
                if (participant.IsPlayer)
                    continue;
                race.RemoveParticipant(participant);
                Object.Destroy(participant.gameObject);
            }
        }

        protected float Uprightness() => Vector3.Dot(Car.Body.rotation * Vector3.up, Vector3.up);

        protected Vector3 Heading() => Vector3.ProjectOnPlane(Car.Body.rotation * Vector3.forward, Vector3.up).normalized;

        /// <summary>Full-range angle between heading and direction of travel (unlike SlipAngle, not zeroed when reversing).</summary>
        protected float TravelAngle()
        {
            Vector3 planarVelocity = Vector3.ProjectOnPlane(Car.Body.linearVelocity, Vector3.up);
            return planarVelocity.magnitude < 1f ? 0f : Vector3.SignedAngle(Heading(), planarVelocity, Vector3.up);
        }

        protected void AssertUpright(float minimum) =>
            Assert.Greater(Uprightness(), minimum, "The car should be upright.");

        protected static T LoadAsset<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"Missing {path}. Run RALLY X > Build to create generated content.");
            return asset;
#else
            Assert.Ignore("These tests load project assets and run only in the Unity Editor.");
            return null;
#endif
        }
    }
}
