using UnityEngine;

namespace RallyX.Tracks
{
    /// <summary>
    /// Queries on a closed loop of points with cumulative distances: the position at a distance along the loop and
    /// the distance of the loop point nearest a position. Shared by <see cref="TrackPath"/> and AI routes.
    /// </summary>
    public static class LoopMath
    {
        /// <summary>Distance from the first point to each point, and the length of the whole loop.</summary>
        public static float[] CumulativeDistances(Vector3[] points, out float length)
        {
            var distances = new float[points.Length];
            float total = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                distances[i] = total;
                total += Vector3.Distance(points[i], points[(i + 1) % points.Length]);
            }

            length = total;
            return distances;
        }

        public static int Wrap(int index, int count) => ((index % count) + count) % count;

        /// <summary>Index of the segment containing a distance along the loop (wrapped), and how far along it (0..1).</summary>
        public static int FindSegment(Vector3[] points, float[] distances, float length, float distance, out float t)
        {
            distance = Mathf.Repeat(distance, length);
            int low = 0;
            int high = distances.Length - 1;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (distances[mid] <= distance)
                    low = mid;
                else
                    high = mid - 1;
            }

            float segmentLength = Vector3.Distance(points[low], points[(low + 1) % points.Length]);
            t = segmentLength > 0f ? (distance - distances[low]) / segmentLength : 0f;
            return low;
        }

        public static Vector3 EvaluatePosition(Vector3[] points, float[] distances, float length, float distance)
        {
            int index = FindSegment(points, distances, length, distance, out float t);
            return Vector3.Lerp(points[index], points[(index + 1) % points.Length], t);
        }

        /// <summary>
        /// Distance along the loop of the point nearest a position, ignoring height. With a hint (a previous result),
        /// only segments within <paramref name="searchRadius"/> metres of it are searched.
        /// </summary>
        public static float FindClosestDistance(Vector3[] points, float[] distances, float length, Vector3 position,
            float? hintDistance, float searchRadius)
        {
            int count = points.Length;
            int first = 0;
            int segments = count;
            if (hintDistance.HasValue)
            {
                float spacing = length / count;
                int span = Mathf.CeilToInt(searchRadius / spacing);
                first = FindSegment(points, distances, length, hintDistance.Value, out _) - span;
                segments = Mathf.Min(count, span * 2 + 1);
            }

            float bestSqr = float.MaxValue;
            float bestDistance = 0f;
            var flatPosition = new Vector2(position.x, position.z);
            for (int s = 0; s < segments; s++)
            {
                int i = Wrap(first + s, count);
                Vector3 a = points[i];
                Vector3 b = points[(i + 1) % count];
                var a2 = new Vector2(a.x, a.z);
                var ab = new Vector2(b.x, b.z) - a2;
                float t = Mathf.Clamp01(Vector2.Dot(flatPosition - a2, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                float sqr = (a2 + ab * t - flatPosition).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestDistance = distances[i] + t * Vector3.Distance(a, b);
                }
            }

            return Mathf.Repeat(bestDistance, length);
        }
    }
}
