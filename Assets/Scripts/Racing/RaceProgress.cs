using System;

namespace RallyX.Racing
{
    /// <summary>
    /// A racer's standing, compared in this order: finished racers first (earliest finish first), then most
    /// completed laps, then most checkpoints passed this lap, then the shortest distance along the track to the
    /// next checkpoint. Positions never depend on straight-line distances between cars.
    /// </summary>
    public readonly struct RaceProgress : IComparable<RaceProgress>
    {
        public readonly int CompletedLaps;

        /// <summary>Gates passed since the current lap began, counting the start/finish line.</summary>
        public readonly int CheckpointsThisLap;

        /// <summary>Distance along the track to the next gate, metres.</summary>
        public readonly float DistanceToNextCheckpoint;

        public readonly bool Finished;
        public readonly float FinishTime;

        public RaceProgress(int completedLaps, int checkpointsThisLap, float distanceToNextCheckpoint, bool finished, float finishTime)
        {
            CompletedLaps = completedLaps;
            CheckpointsThisLap = checkpointsThisLap;
            DistanceToNextCheckpoint = distanceToNextCheckpoint;
            Finished = finished;
            FinishTime = finishTime;
        }

        /// <summary>Negative when this racer is ahead of <paramref name="other"/>, positive when behind.</summary>
        public int CompareTo(RaceProgress other)
        {
            if (Finished != other.Finished)
                return Finished ? -1 : 1;
            if (Finished)
                return FinishTime.CompareTo(other.FinishTime);
            if (CompletedLaps != other.CompletedLaps)
                return other.CompletedLaps.CompareTo(CompletedLaps);
            if (CheckpointsThisLap != other.CheckpointsThisLap)
                return other.CheckpointsThisLap.CompareTo(CheckpointsThisLap);
            return DistanceToNextCheckpoint.CompareTo(other.DistanceToNextCheckpoint);
        }
    }
}
