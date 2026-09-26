using System.Collections.Generic;
using NUnit.Framework;
using RallyX.Racing;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.Tests
{
    /// <summary>
    /// Race rules on a simple circular circuit (radius 100 m, driven anticlockwise seen from above, a gate every
    /// 90 degrees starting at the start line), stepping the tracker directly with positions instead of physics.
    /// </summary>
    public sealed class RaceTrackerTests
    {
        private const float Radius = 100f;
        private const float Speed = 20f;
        private const float StepDegrees = 0.5f;
        private const float GridAngle = -10f;

        private static readonly float LapLength = 2f * Mathf.PI * Radius;

        private GameObject root;
        private RaceCircuit circuit;
        private RaceSettings settings;
        private RaceTracker tracker;
        private float raceTime;

        [SetUp]
        public void CreateCircuit()
        {
            root = new GameObject("TestCircuit");

            const int samples = 720;
            var points = new Vector3[samples];
            var widths = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                points[i] = PointAt(i * 360f / samples);
                widths[i] = 10f;
            }

            var path = Child("Path").AddComponent<TrackPath>();
            path.SetSamples(points, widths);

            var slot = Child("Slot").transform;
            slot.SetPositionAndRotation(PointAt(GridAngle), Quaternion.LookRotation(DirectionAt(GridAngle)));
            var grid = Child("Grid").AddComponent<TrackSpawnGrid>();
            grid.SetSlots(new[] { slot });

            var gates = new Checkpoint[4];
            for (int g = 0; g < gates.Length; g++)
            {
                float angle = g * 90f;
                gates[g] = Child($"Gate{g}").AddComponent<Checkpoint>();
                gates[g].transform.SetPositionAndRotation(PointAt(angle), Quaternion.LookRotation(DirectionAt(angle)));
                gates[g].SetSize(20f, 10f);
            }

            circuit = root.AddComponent<RaceCircuit>();
            circuit.Configure(path, grid, gates);
            settings = ScriptableObject.CreateInstance<RaceSettings>();
            tracker = new RaceTracker(circuit, settings);
            tracker.Reset(PointAt(GridAngle));
            raceTime = 0f;
        }

        [TearDown]
        public void DestroyCircuit()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Circuit_IsValid()
        {
            Assert.IsNull(circuit.FindProblem());
            Assert.AreEqual(0f, circuit.CheckpointDistance(0));
            Assert.AreEqual(LapLength / 4f, circuit.CheckpointDistance(1), 0.5f);
        }

        [Test]
        public void FullRace_CountsThreeLapsAndFinishesExactlyOnTheLine()
        {
            var events = DriveAround(GridAngle, 3 * 360f + 5f);

            float lapTime = LapLength / Speed;
            float runUp = -GridAngle * Mathf.Deg2Rad * Radius / Speed;
            TestContext.WriteLine($"Laps: {string.Join(", ", tracker.LapTimes)}; finish {tracker.FinishTime:0.000} s");
            Assert.IsTrue(tracker.Finished);
            Assert.IsTrue((events & RaceEvents.Finished) != 0);
            Assert.AreEqual(3, tracker.CompletedLaps);
            Assert.AreEqual(3, tracker.CurrentLap, "The lap display stays on the last lap after the finish.");
            Assert.AreEqual(3, tracker.LapTimes.Count);
            Assert.AreEqual(lapTime + runUp, tracker.LapTimes[0], 0.01f, "Lap 1 is timed from the start, including the run to the line.");
            Assert.AreEqual(lapTime, tracker.LapTimes[1], 0.01f);
            Assert.AreEqual(lapTime, tracker.LapTimes[2], 0.01f);
            Assert.AreEqual(lapTime, tracker.BestLapTime, 0.01f);
            Assert.AreEqual(runUp + 3f * lapTime, tracker.FinishTime, 0.01f, "Finish time is taken where the car crossed the line.");
            Assert.AreEqual(tracker.LapTimes[0] + tracker.LapTimes[1] + tracker.LapTimes[2], tracker.FinishTime, 1e-3f);
        }

        [Test]
        public void CrossingTheLine_FromTheGrid_StartsLapOneWithoutCompletingALap()
        {
            Assert.IsFalse(tracker.HasStarted);
            Assert.AreEqual(1, tracker.CurrentLap);

            var events = DriveAround(GridAngle, 5f);

            Assert.IsTrue(tracker.HasStarted);
            Assert.AreEqual(0, tracker.CompletedLaps);
            Assert.AreEqual(1, tracker.NextCheckpoint);
            Assert.IsTrue((events & RaceEvents.LapCompleted) == 0);
        }

        [Test]
        public void CuttingAcrossTheInfield_DoesNotCountTheLap()
        {
            DriveAround(GridAngle, 100f);
            Assert.AreEqual(2, tracker.NextCheckpoint);

            // Straight across the middle to just before the line, missing gates 2 and 3.
            Step(PointAt(350f), DirectionAt(350f), Speed, 5f);
            var events = DriveAround(350f, 370f);

            Assert.AreEqual(0, tracker.CompletedLaps, "A lap missing gates must not count.");
            Assert.AreEqual(2, tracker.NextCheckpoint, "The racer still owes gate 2.");
            Assert.IsTrue((events & RaceEvents.LapCompleted) == 0);
        }

        [Test]
        public void DistanceToNextCheckpoint_IsMeasuredAlongTheTrack()
        {
            DriveAround(GridAngle, 10f);

            // Next gate is at 90 degrees: 80 degrees of road ahead, although the straight line to it is shorter.
            float alongTrack = 80f * Mathf.Deg2Rad * Radius;
            float straightLine = 2f * Radius * Mathf.Sin(40f * Mathf.Deg2Rad);
            TestContext.WriteLine($"Along the track {alongTrack:0.0} m, straight line {straightLine:0.0} m, tracker {tracker.DistanceToNextCheckpoint:0.0} m");
            Assert.AreEqual(alongTrack, tracker.DistanceToNextCheckpoint, 1f);
        }

        [Test]
        public void RaceDistance_IsNegativeOnTheGridAndCountsWholeLaps()
        {
            float degree = Mathf.Deg2Rad * Radius;
            Assert.AreEqual(GridAngle * degree, tracker.RaceDistance, 0.5f, "On the grid, behind the line.");

            DriveAround(GridAngle, 100f);
            Assert.AreEqual(100f * degree, tracker.RaceDistance, 0.5f);

            DriveAround(100f, 370f);
            Assert.AreEqual(LapLength + 10f * degree, tracker.RaceDistance, 0.5f, "One lap plus 10 degrees.");
        }

        [Test]
        public void DrivingTheWrongWay_WarnsAfterTheDelayAndClearsWhenTurnedAround()
        {
            DriveAround(GridAngle, 45f);

            // Facing and driving back down the track, for less than the delay...
            DriveAround(45f, 40f, facingForward: false);
            Assert.IsFalse(tracker.IsWrongWay, "Brief wrong-way moments (spins) should not warn.");

            // ...then for longer.
            var events = DriveAround(40f, 20f, facingForward: false);
            Assert.IsTrue(tracker.IsWrongWay);
            Assert.IsTrue((events & RaceEvents.WrongWayChanged) != 0);

            events = DriveAround(20f, 21f);
            Assert.IsFalse(tracker.IsWrongWay, "Turning back the right way clears the warning.");
            Assert.IsTrue((events & RaceEvents.WrongWayChanged) != 0);
        }

        [Test]
        public void Reversing_IsNotDrivingTheWrongWay()
        {
            DriveAround(GridAngle, 45f);

            // Facing the right way but rolling backwards, as when reversing out of trouble.
            DriveAround(45f, 10f, facingForward: true, forwardSpeed: -5f);

            Assert.IsFalse(tracker.IsWrongWay);
        }

        [Test]
        public void Positions_CompareLapsThenCheckpointsThenDistanceToTheNextCheckpoint()
        {
            var lapAhead = new RaceProgress(2, 1, 150f, false, 0f);
            var moreCheckpoints = new RaceProgress(1, 4, 90f, false, 0f);
            var closerToNext = new RaceProgress(1, 3, 10f, false, 0f);
            var furtherFromNext = new RaceProgress(1, 3, 50f, false, 0f);
            var order = new List<RaceProgress> { furtherFromNext, closerToNext, lapAhead, moreCheckpoints };

            order.Sort();

            CollectionAssert.AreEqual(new[] { lapAhead, moreCheckpoints, closerToNext, furtherFromNext }, order);
        }

        [Test]
        public void Positions_FinishedRacersLeadInTheOrderTheyFinished()
        {
            var winner = new RaceProgress(3, 1, 0f, true, 250f);
            var runnerUp = new RaceProgress(3, 1, 0f, true, 262.5f);
            var stillRacing = new RaceProgress(2, 4, 1f, false, 0f);
            var order = new List<RaceProgress> { stillRacing, runnerUp, winner };

            order.Sort();

            CollectionAssert.AreEqual(new[] { winner, runnerUp, stillRacing }, order);
        }

        [TestCase(0f, "0:00.000")]
        [TestCase(83.4567f, "1:23.457")]
        [TestCase(59.9996f, "1:00.000")]
        [TestCase(3725.5f, "62:05.500")]
        public void RaceTime_FormatsMinutesSecondsAndMilliseconds(float seconds, string expected) =>
            Assert.AreEqual(expected, RaceTime.Format(seconds));

        [TestCase(1, "1st")]
        [TestCase(2, "2nd")]
        [TestCase(3, "3rd")]
        [TestCase(4, "4th")]
        [TestCase(11, "11th")]
        [TestCase(12, "12th")]
        [TestCase(13, "13th")]
        [TestCase(21, "21st")]
        [TestCase(22, "22nd")]
        public void RaceTime_WritesOrdinalPositions(int position, string expected) =>
            Assert.AreEqual(expected, RaceTime.Ordinal(position));

        /// <summary>Moves the racer round the circle between two angles at <see cref="Speed"/>, as the race manager would.</summary>
        private RaceEvents DriveAround(float fromDegrees, float toDegrees, bool facingForward = true, float forwardSpeed = Speed)
        {
            float direction = Mathf.Sign(toDegrees - fromDegrees);
            int steps = Mathf.RoundToInt(Mathf.Abs(toDegrees - fromDegrees) / StepDegrees);
            float stepTime = StepDegrees * Mathf.Deg2Rad * Radius / Speed;
            var events = RaceEvents.None;
            for (int i = 1; i <= steps; i++)
            {
                float angle = fromDegrees + direction * i * StepDegrees;
                Vector3 heading = facingForward ? DirectionAt(angle) : -DirectionAt(angle);
                events |= Step(PointAt(angle), heading, forwardSpeed, stepTime);
            }

            return events;
        }

        private RaceEvents Step(Vector3 position, Vector3 heading, float forwardSpeed, float deltaTime)
        {
            raceTime += deltaTime;
            return tracker.Step(position, heading, forwardSpeed, raceTime, deltaTime);
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private static Vector3 PointAt(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * Radius;
        }

        private static Vector3 DirectionAt(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }
    }
}
