using System;
using System.Collections.Generic;
using UnityEngine;

namespace RallyX.Racing
{
    /// <summary>What happened to a racer during one <see cref="RaceTracker.Step"/>.</summary>
    [Flags]
    public enum RaceEvents
    {
        None = 0,
        Checkpoint = 1,
        LapCompleted = 2,
        Finished = 4,
        WrongWayChanged = 8,
    }

    /// <summary>
    /// Follows one racer around a circuit: gates in order, laps and lap times, the finish and wrong-way driving.
    /// Plain C#, so the player and AI racers share it and it can be tested without physics.
    /// </summary>
    public sealed class RaceTracker
    {
        private readonly RaceCircuit circuit;
        private readonly RaceSettings settings;
        private readonly List<float> lapTimes = new List<float>();
        private Vector3 lastPosition;
        private float lapStartTime;
        private float wrongWayTime;

        public RaceTracker(RaceCircuit circuit, RaceSettings settings)
        {
            this.circuit = circuit;
            this.settings = settings;
        }

        public int CompletedLaps { get; private set; }

        /// <summary>Gates passed since the current lap began, counting the start/finish line.</summary>
        public int CheckpointsThisLap { get; private set; }

        /// <summary>Index of the gate the racer must pass next (0 = start/finish line).</summary>
        public int NextCheckpoint => CheckpointsThisLap % circuit.CheckpointCount;

        /// <summary>Index of the last gate passed, or -1 before the racer has crossed the start line.</summary>
        public int LastCheckpoint => HasStarted ? (NextCheckpoint + circuit.CheckpointCount - 1) % circuit.CheckpointCount : -1;

        /// <summary>True once the racer has crossed the start line after leaving the grid.</summary>
        public bool HasStarted => CompletedLaps > 0 || CheckpointsThisLap > 0;

        /// <summary>Lap being driven, from 1, for display. Stays on the last lap after the finish.</summary>
        public int CurrentLap => Mathf.Min(CompletedLaps + 1, settings.Laps);

        /// <summary>Distance along the lap from the start line, metres.</summary>
        public float TrackDistance { get; private set; }

        /// <summary>Distance along the track to the next gate, metres.</summary>
        public float DistanceToNextCheckpoint { get; private set; }

        public bool IsWrongWay { get; private set; }
        public bool Finished { get; private set; }

        /// <summary>Race time at which the racer crossed the finish line, seconds.</summary>
        public float FinishTime { get; private set; }

        public IReadOnlyList<float> LapTimes => lapTimes;

        /// <summary>Fastest completed lap, seconds, or 0 before the first lap is complete.</summary>
        public float BestLapTime
        {
            get
            {
                float best = 0f;
                foreach (float time in lapTimes)
                {
                    if (best <= 0f || time < best)
                        best = time;
                }

                return best;
            }
        }

        public RaceProgress Progress =>
            new RaceProgress(CompletedLaps, CheckpointsThisLap, DistanceToNextCheckpoint, Finished, FinishTime);

        /// <summary>Time on the current lap, seconds. The first lap is timed from the start of the race.</summary>
        public float CurrentLapTime(float raceTime) => Finished ? 0f : raceTime - lapStartTime;

        /// <summary>Starts over for a new race, with the racer on the grid at <paramref name="position"/>.</summary>
        public void Reset(Vector3 position)
        {
            lapTimes.Clear();
            CompletedLaps = 0;
            CheckpointsThisLap = 0;
            Finished = false;
            FinishTime = 0f;
            IsWrongWay = false;
            lapStartTime = 0f;
            wrongWayTime = 0f;
            lastPosition = position;
            TrackDistance = circuit.TrackDistance(position);
            UpdateDistanceToNext();
        }

        /// <summary>
        /// Moves the racer on to its latest position. <paramref name="raceTime"/> is the race clock at that position
        /// and <paramref name="deltaTime"/> the time since the previous step.
        /// </summary>
        public RaceEvents Step(Vector3 position, Vector3 heading, float forwardSpeed, float raceTime, float deltaTime)
        {
            if (Finished)
            {
                lastPosition = position;
                return RaceEvents.None;
            }

            var events = RaceEvents.None;
            if (circuit.GetCheckpoint(NextCheckpoint).Crossed(lastPosition, position, out float fraction))
                events |= PassCheckpoint(raceTime - deltaTime * (1f - fraction));

            lastPosition = position;
            TrackDistance = circuit.TrackDistance(position, TrackDistance);
            UpdateDistanceToNext();
            if (!Finished)
                events |= UpdateWrongWay(heading, forwardSpeed, deltaTime);
            return events;
        }

        private RaceEvents PassCheckpoint(float time)
        {
            if (NextCheckpoint != 0)
            {
                CheckpointsThisLap++;
                return RaceEvents.Checkpoint;
            }

            // Leaving the grid: lap 1 is already being timed from the start of the race.
            if (!HasStarted)
            {
                CheckpointsThisLap = 1;
                return RaceEvents.Checkpoint;
            }

            lapTimes.Add(time - lapStartTime);
            lapStartTime = time;
            CompletedLaps++;
            CheckpointsThisLap = 1;
            if (CompletedLaps < settings.Laps)
                return RaceEvents.Checkpoint | RaceEvents.LapCompleted;

            Finished = true;
            FinishTime = time;
            var events = RaceEvents.Checkpoint | RaceEvents.LapCompleted | RaceEvents.Finished;
            if (IsWrongWay)
            {
                IsWrongWay = false;
                events |= RaceEvents.WrongWayChanged;
            }

            return events;
        }

        private void UpdateDistanceToNext()
        {
            // Never further than the gap back to the previous gate: a racer that has reversed past its last gate,
            // or gone round a gate instead of through it, counts as being at that last gate.
            int next = NextCheckpoint;
            int previous = (next + circuit.CheckpointCount - 1) % circuit.CheckpointCount;
            float nextDistance = circuit.CheckpointDistance(next);
            float gap = circuit.DistanceAhead(circuit.CheckpointDistance(previous), nextDistance);
            DistanceToNextCheckpoint = Mathf.Min(circuit.DistanceAhead(TrackDistance, nextDistance), gap);
        }

        private RaceEvents UpdateWrongWay(Vector3 heading, float forwardSpeed, float deltaTime)
        {
            heading.y = 0f;
            float angle = Vector3.Angle(heading, circuit.DirectionAt(TrackDistance));
            bool wasWrongWay = IsWrongWay;

            if (IsWrongWay)
            {
                if (angle < settings.RightWayAngle)
                    IsWrongWay = false;
            }
            else if (angle > settings.WrongWayAngle && forwardSpeed > settings.WrongWayMinSpeed)
            {
                // Only driving forwards counts: reversing back down the road to recover isn't going the wrong way.
                wrongWayTime += deltaTime;
                IsWrongWay = wrongWayTime >= settings.WrongWayDelay;
            }
            else
            {
                wrongWayTime = 0f;
            }

            if (IsWrongWay == wasWrongWay)
                return RaceEvents.None;

            wrongWayTime = 0f;
            return RaceEvents.WrongWayChanged;
        }
    }
}
