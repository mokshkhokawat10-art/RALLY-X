using RallyX.Tracks;
using UnityEngine;

namespace RallyX.Racing
{
    /// <summary>
    /// Everything a race needs from a track: its centreline, the checkpoint gates in driving order (the first is
    /// the start/finish line) and the starting grid. Distances are measured along the centreline from the start line.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceCircuit : MonoBehaviour
    {
        // How far along the lap to search when following a racer from one step to the next, metres. Wide enough
        // to keep track of cars on a shortcut, which can be well away from the main road.
        private const float FollowSearchRadius = 100f;

        [SerializeField] private TrackPath path;
        [SerializeField] private TrackSpawnGrid grid;

        [Tooltip("Gates in driving order. The first one is the start/finish line.")]
        [SerializeField] private Checkpoint[] checkpoints = System.Array.Empty<Checkpoint>();

        private float[] checkpointDistances;
        private float startOffset;

        public TrackPath Path => path;
        public TrackSpawnGrid Grid => grid;
        public int CheckpointCount => checkpoints.Length;
        public float LapLength => path.Length;

        public Checkpoint GetCheckpoint(int index) => checkpoints[index];

        public void Configure(TrackPath newPath, TrackSpawnGrid newGrid, Checkpoint[] newCheckpoints)
        {
            path = newPath;
            grid = newGrid;
            checkpoints = newCheckpoints;
            checkpointDistances = null;
        }

        /// <summary>Distance of a gate from the start line along the centreline, metres.</summary>
        public float CheckpointDistance(int index)
        {
            EnsureDistances();
            return checkpointDistances[index];
        }

        /// <summary>
        /// Distance along the lap from the start line to the centreline point nearest a position (ignoring height).
        /// Pass the racer's previous result as a hint so it is followed along the road rather than jumping to
        /// another part of the track that happens to be close.
        /// </summary>
        public float TrackDistance(Vector3 position, float? hint = null)
        {
            EnsureDistances();
            float? pathHint = hint.HasValue ? hint.Value + startOffset : (float?)null;
            return Mathf.Repeat(path.FindClosestDistance(position, pathHint, FollowSearchRadius) - startOffset, path.Length);
        }

        /// <summary>Horizontal direction of travel at a distance from the start line.</summary>
        public Vector3 DirectionAt(float distance)
        {
            float onPath = distance + startOffset;
            Vector3 direction = path.EvaluatePosition(onPath + 1f) - path.EvaluatePosition(onPath - 1f);
            direction.y = 0f;
            return direction.normalized;
        }

        /// <summary>How far ahead <paramref name="to"/> is from <paramref name="from"/> along the lap (0..lap length).</summary>
        public float DistanceAhead(float from, float to) => Mathf.Repeat(to - from, path.Length);

        /// <summary>Why the circuit can't be raced, or null when it is complete.</summary>
        public string FindProblem()
        {
            if (path == null || path.Count < 2)
                return "it has no TrackPath with a baked centreline";
            if (grid == null || grid.SlotCount == 0)
                return "it has no TrackSpawnGrid with slots";
            if (checkpoints.Length < 2)
                return "it needs at least two checkpoints (the start/finish line and one more)";
            foreach (var checkpoint in checkpoints)
            {
                if (checkpoint == null)
                    return "a checkpoint slot is empty";
            }

            checkpointDistances = null;
            EnsureDistances();
            for (int i = 1; i < checkpoints.Length; i++)
            {
                if (checkpointDistances[i] <= checkpointDistances[i - 1])
                    return $"checkpoint {i} ({checkpoints[i].name}) is not further along the track than checkpoint {i - 1}";
            }

            return null;
        }

        private void Awake()
        {
            string problem = FindProblem();
            if (problem != null)
                Debug.LogError($"{name}: RaceCircuit is not usable: {problem}.", this);
        }

        private void EnsureDistances()
        {
            if (checkpointDistances != null && checkpointDistances.Length == checkpoints.Length)
                return;

            startOffset = checkpoints.Length > 0 ? path.FindClosestDistance(checkpoints[0].transform.position) : 0f;
            checkpointDistances = new float[checkpoints.Length];
            for (int i = 1; i < checkpoints.Length; i++)
            {
                float onPath = path.FindClosestDistance(checkpoints[i].transform.position);
                checkpointDistances[i] = Mathf.Repeat(onPath - startOffset, path.Length);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            for (int i = 0; i < checkpoints.Length - 1; i++)
            {
                if (checkpoints[i] != null && checkpoints[i + 1] != null)
                    Gizmos.DrawLine(checkpoints[i].transform.position + Vector3.up, checkpoints[i + 1].transform.position + Vector3.up);
            }
        }
    }
}
