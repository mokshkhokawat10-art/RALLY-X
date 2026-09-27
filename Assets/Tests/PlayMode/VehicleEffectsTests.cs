using System.Collections;
using NUnit.Framework;
using RallyX.Tracks;
using RallyX.Vehicle;
using RallyX.VFX;
using UnityEngine;
using UnityEngine.TestTools;

namespace RallyX.Tests
{
    /// <summary>
    /// The car's effects follow what it is really doing: nothing from the tyres when driving cleanly, smoke and marks in a
    /// drift on asphalt, stones on gravel, clumps (and no dust) on mud, dust when landing a jump, sparks when hitting a
    /// wall; brake and reversing lights follow the pedals; the body dives under braking and leans in corners.
    /// Counts and lean angles are printed.
    /// </summary>
    public sealed class VehicleEffectsTests : VehicleTestBase
    {
        private const string SurfacesFolder = "Assets/Settings/Surfaces";

        private VehicleEffects Effects => Car.GetComponent<VehicleEffects>();

        private VehicleVisuals Visuals => Car.GetComponent<VehicleVisuals>();

        [UnityTest]
        public IEnumerator CleanDriving_OnAsphalt_MakesNoTyreEffects()
        {
            yield return CreateCourse();
            Assert.IsTrue(Effects.IsReady, "The car's effects should be set up once it is driving.");

            yield return AccelerateTo(90f);
            yield return Drive(new VehicleInputState(0.4f, 0f, 0f, false), 3f);

            TestContext.WriteLine($"Clean run: puffs {Effects.PuffsEmitted}, debris {Effects.DebrisEmitted}, marks {Effects.SkidMarkCount}, exhaust {Effects.ExhaustEmitted}");
            Assert.AreEqual(0, Effects.PuffsEmitted, "No tyre smoke without a slide.");
            Assert.AreEqual(0, Effects.DebrisEmitted, "Asphalt throws nothing.");
            Assert.AreEqual(0, Effects.SkidMarkCount, "No marks without a slide.");
            Assert.Greater(Effects.ExhaustEmitted, 0, "The exhaust smokes while the engine runs.");
        }

        [UnityTest]
        public IEnumerator HandbrakeDrift_OnAsphalt_SmokesAndLeavesMarks()
        {
            yield return CreateCourse();
            yield return AccelerateTo(80f);
            yield return Drive(new VehicleInputState(0.6f, 0f, 1f, true), 1.2f);

            TestContext.WriteLine($"Asphalt drift: puffs {Effects.PuffsEmitted}, marks {Effects.SkidMarkCount}, debris {Effects.DebrisEmitted}");
            Assert.Greater(Effects.PuffsEmitted, 20, "A drift on asphalt should smoke.");
            Assert.Greater(Effects.SkidMarkCount, 10, "A drift should leave tyre marks.");
            Assert.AreEqual(0, Effects.DebrisEmitted, "Asphalt throws no stones.");
        }

        [UnityTest]
        public IEnumerator Gravel_ThrowsStonesAndDust()
        {
            yield return CreateCourse(Surface("Gravel"));
            yield return Drive(FullThrottle, 2f);
            int launchStones = Effects.DebrisEmitted;
            yield return AccelerateTo(70f);
            yield return Drive(new VehicleInputState(0.6f, 0f, 1f, true), 1f);

            TestContext.WriteLine($"Gravel: stones pulling away {launchStones}, after a drift {Effects.DebrisEmitted}, dust {Effects.PuffsEmitted}, marks {Effects.SkidMarkCount}");
            Assert.Greater(launchStones, 0, "Pulling away on gravel should throw stones.");
            Assert.Greater(Effects.DebrisEmitted, launchStones + 10, "A drift on gravel should throw more.");
            Assert.Greater(Effects.PuffsEmitted, 0, "Gravel raises dust.");
        }

        [UnityTest]
        public IEnumerator Mud_ThrowsClumpsButNoDust()
        {
            yield return CreateCourse(Surface("Mud"));
            yield return AccelerateTo(50f);
            yield return Drive(new VehicleInputState(0.6f, 0f, 1f, true), 1f);

            TestContext.WriteLine($"Mud: clumps {Effects.DebrisEmitted}, puffs {Effects.PuffsEmitted}");
            Assert.Greater(Effects.DebrisEmitted, 10, "Sliding in mud should throw clumps.");
            Assert.AreEqual(0, Effects.PuffsEmitted, "Mud is wet: no dust.");
        }

        [UnityTest]
        public IEnumerator Landing_ThrowsUpDustOnce()
        {
            yield return CreateCourse(Surface("Dirt"), SpawnPosition + Vector3.up * 4f, Quaternion.identity, settle: false);
            yield return null;
            int before = Effects.PuffsEmitted;

            yield return Drive(default, 3f);

            TestContext.WriteLine($"Landing: bursts {Effects.LandingBursts}, puffs {Effects.PuffsEmitted - before}");
            Assert.AreEqual(1, Effects.LandingBursts, "One landing, one burst.");
            Assert.Greater(Effects.PuffsEmitted, before, "The landing should throw up dust.");
        }

        [UnityTest]
        public IEnumerator HittingAWall_Sparks()
        {
            yield return CreateCourse();
            var wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.position = SpawnPosition + new Vector3(0f, 1.5f, 40f);
            wall.transform.localScale = new Vector3(20f, 3f, 1f);

            yield return Drive(FullThrottle, 4f);

            TestContext.WriteLine($"Wall: sparks {Effects.SparksEmitted}");
            Assert.Greater(Effects.SparksEmitted, 5, "A hard hit on a wall should spark.");
        }

        [UnityTest]
        public IEnumerator Lights_FollowTheBrakeAndReverse()
        {
            yield return CreateCourse();
            yield return AccelerateTo(60f);
            yield return Drive(new VehicleInputState(0f, 1f, 0f, false), 0.5f);
            float braking = Visuals.BrakeLight;
            yield return Drive(FullThrottle, 0.5f);
            float accelerating = Visuals.BrakeLight;

            // Stop, then hold the brake to reverse.
            yield return Drive(new VehicleInputState(0f, 1f, 0f, false), 4f);
            float reversing = Visuals.ReverseLight;

            TestContext.WriteLine($"Brake light braking {braking:0.00}, accelerating {accelerating:0.00}; reversing light {reversing:0.00}");
            Assert.Greater(braking, 0.9f, "Brake lights on while braking.");
            Assert.Less(accelerating, 0.1f, "Brake lights off on the throttle.");
            Assert.Greater(reversing, 0.9f, "Reversing lights on while reversing.");
        }

        [UnityTest]
        public IEnumerator Body_DivesWhenBrakingAndLeansInCorners()
        {
            yield return CreateCourse();
            yield return AccelerateTo(90f);
            float dive = 0f;
            yield return Drive(new VehicleInputState(0f, 1f, 0f, false), 0.8f, () => dive = Mathf.Max(dive, Visuals.Lean.x));

            yield return AccelerateTo(60f);
            float roll = 0f;
            yield return Drive(new VehicleInputState(0.5f, 0f, 1f, false), 1.5f, () => roll = Mathf.Max(roll, Visuals.Lean.y));

            TestContext.WriteLine($"Nose dive braking from 90 km/h: {dive:0.0} deg; lean turning right: {roll:0.0} deg");
            Assert.Greater(dive, 1f, "The nose should dip under braking.");
            Assert.Greater(roll, 1f, "The body should lean to the outside of a right turn (left side down).");
        }

        private static SurfaceType Surface(string surfaceName) => LoadAsset<SurfaceType>($"{SurfacesFolder}/Surface_{surfaceName}.asset");
    }
}
