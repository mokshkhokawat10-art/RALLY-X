using System.Collections.Generic;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>One evenly spaced sample of a road centreline.</summary>
    public struct TrackSample
    {
        /// <summary>Centre of the road surface.</summary>
        public Vector3 Position;

        /// <summary>Horizontal unit direction of travel.</summary>
        public Vector3 Forward;

        /// <summary>Horizontal unit vector to the driver's right.</summary>
        public Vector3 Right;

        public float Width;
        public float Distance;

        /// <summary>Signed curvature, 1/metres. Positive turns right.</summary>
        public float Curvature;

        public SurfaceType Surface;

        /// <summary>Index into the definition's bridges, or -1.</summary>
        public int BridgeIndex;

        public float HalfWidth => Width * 0.5f;
        public bool IsBridge => BridgeIndex >= 0;
        public float Radius => Mathf.Abs(Curvature) > 1e-5f ? 1f / Mathf.Abs(Curvature) : float.PositiveInfinity;
    }

    /// <summary>A resampled road: the closed main loop or an open shortcut.</summary>
    public sealed class RoadPath
    {
        public RoadPath(TrackSample[] samples, bool closed, float length)
        {
            Samples = samples;
            Closed = closed;
            Length = length;
        }

        public TrackSample[] Samples { get; }
        public bool Closed { get; }
        public float Length { get; }
        public int Count => Samples.Length;

        /// <summary>Sample index wrapped for closed paths and clamped for open ones.</summary>
        public TrackSample Get(int index)
        {
            int count = Samples.Length;
            return Closed ? Samples[((index % count) + count) % count] : Samples[Mathf.Clamp(index, 0, count - 1)];
        }
    }

    /// <summary>Turns a TrackDefinition into evenly spaced road samples with jumps, bridges and curvature applied.</summary>
    public sealed class TrackLayout
    {
        // Catmull-Rom subdivisions per node segment before arc-length resampling.
        private const int SubdivisionsPerSegment = 48;

        // Half-width, in samples, of the window used to measure curvature (smooths out sampling noise).
        private const int CurvatureWindow = 3;

        // Length of the virtual lead-in/lead-out used to make shortcuts leave and join the main road tangentially.
        private const float ShortcutTangentLength = 12f;

        // Shortcuts overlap the main road where they leave and rejoin it. Raising them slightly keeps the two
        // surfaces from flickering through each other there; wheels roll over a step this small unnoticed.
        private const float ShortcutSurfaceLift = 0.04f;

        private float[] nodeDistances;

        private TrackLayout(TrackDefinition definition)
        {
            Definition = definition;
        }

        public TrackDefinition Definition { get; }
        public RoadPath Main { get; private set; }
        public IReadOnlyList<RoadPath> Shortcuts { get; private set; }

        public static TrackLayout Build(TrackDefinition definition)
        {
            var layout = new TrackLayout(definition);
            layout.BuildMainLoop();
            layout.BuildShortcuts();
            return layout;
        }

        /// <summary>Distance along the main loop of an anchor.</summary>
        public float AnchorDistance(TrackAnchor anchor)
        {
            int count = nodeDistances.Length;
            int node = ((anchor.node % count) + count) % count;
            float start = nodeDistances[node];
            float end = node + 1 < count ? nodeDistances[node + 1] : Main.Length;
            return start + Mathf.Clamp01(anchor.t) * (end - start);
        }

        /// <summary>Index of the main-loop sample nearest to a distance along the loop.</summary>
        public int SampleIndexAt(float distance)
        {
            float spacing = Main.Length / Main.Count;
            int count = Main.Count;
            return ((Mathf.RoundToInt(Mathf.Repeat(distance, Main.Length) / spacing) % count) + count) % count;
        }

        /// <summary>Problems that make the layout undrivable or overlapping. Empty when the layout is sound.</summary>
        public List<string> Validate(float minRadius = 12f, float maxGrade = 0.15f, float corridorMargin = 3f)
        {
            var problems = new List<string>();
            var samples = Main.Samples;
            int count = samples.Length;

            for (int i = 0; i < count; i++)
            {
                if (samples[i].Radius < minRadius)
                {
                    problems.Add($"Corner too tight at {samples[i].Distance:0} m: radius {samples[i].Radius:0.0} m (min {minRadius}).");
                    break;
                }
            }

            for (int i = 0; i < count; i++)
            {
                var a = samples[i];
                var b = Main.Get(i + 1);
                float run = Vector2.Distance(Flat(a.Position), Flat(b.Position));
                float grade = run > 0f ? Mathf.Abs(b.Position.y - a.Position.y) / run : 0f;
                if (grade > maxGrade)
                {
                    problems.Add($"Road too steep at {a.Distance:0} m: grade {grade:P0} (max {maxGrade:P0}).");
                    break;
                }
            }

            // Two parts of the loop that are far apart along the road must not overlap on the map.
            float spacing = Main.Length / count;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    int alongGap = Mathf.Min(j - i, count - (j - i));
                    float requiredGap = samples[i].HalfWidth + samples[j].HalfWidth + corridorMargin;
                    if (alongGap * spacing < requiredGap * 3f)
                        continue;

                    float gap = Vector2.Distance(Flat(samples[i].Position), Flat(samples[j].Position));
                    if (gap < requiredGap)
                    {
                        problems.Add($"Road overlaps itself between {samples[i].Distance:0} m and {samples[j].Distance:0} m ({gap:0.0} m apart).");
                        return problems;
                    }
                }
            }

            return problems;
        }

        private void BuildMainLoop()
        {
            var nodes = Definition.Nodes;
            int nodeCount = nodes.Length;
            var controlPoints = new Vector3[nodeCount];
            for (int i = 0; i < nodeCount; i++)
                controlPoints[i] = new Vector3(nodes[i].position.x, nodes[i].elevation, nodes[i].position.y);

            var dense = new List<Vector3>(nodeCount * SubdivisionsPerSegment + 1);
            for (int i = 0; i < nodeCount; i++)
            {
                Vector3 p0 = controlPoints[(i - 1 + nodeCount) % nodeCount];
                Vector3 p1 = controlPoints[i];
                Vector3 p2 = controlPoints[(i + 1) % nodeCount];
                Vector3 p3 = controlPoints[(i + 2) % nodeCount];
                for (int s = 0; s < SubdivisionsPerSegment; s++)
                    dense.Add(CentripetalCatmullRom(p0, p1, p2, p3, s / (float)SubdivisionsPerSegment));
            }

            dense.Add(dense[0]);
            float[] denseDistances = CumulativeDistances(dense);
            float length = denseDistances[denseDistances.Length - 1];

            nodeDistances = new float[nodeCount];
            for (int i = 0; i < nodeCount; i++)
                nodeDistances[i] = denseDistances[i * SubdivisionsPerSegment];

            int sampleCount = Mathf.Max(8, Mathf.RoundToInt(length / Definition.SampleSpacing));
            var samples = new TrackSample[sampleCount];
            float step = length / sampleCount;
            int cursor = 0;
            for (int k = 0; k < sampleCount; k++)
            {
                float distance = k * step;
                while (cursor < denseDistances.Length - 2 && denseDistances[cursor + 1] < distance)
                    cursor++;

                float segmentLength = denseDistances[cursor + 1] - denseDistances[cursor];
                float t = segmentLength > 0f ? (distance - denseDistances[cursor]) / segmentLength : 0f;
                int node = cursor / SubdivisionsPerSegment;
                float nodeT = (cursor % SubdivisionsPerSegment + t) / SubdivisionsPerSegment;

                samples[k] = new TrackSample
                {
                    Position = Vector3.Lerp(dense[cursor], dense[cursor + 1], t),
                    Distance = distance,
                    Width = Mathf.Lerp(nodes[node].width, nodes[(node + 1) % nodeCount].width, nodeT),
                    Surface = nodes[node].surface,
                    BridgeIndex = -1,
                };
            }

            Main = new RoadPath(samples, closed: true, length);
            ApplyJumps();
            ApplyBridges();
            ComputeFrames(Main);
        }

        private void ApplyJumps()
        {
            var samples = Main.Samples;
            foreach (var jump in Definition.Jumps)
            {
                float start = AnchorDistance(jump.start);
                for (int k = 0; k < samples.Length; k++)
                {
                    float offset = Mathf.Repeat(samples[k].Distance - start, Main.Length);
                    samples[k].Position.y += jump.height * JumpProfile(offset, jump.rampLength, jump.landingLength);
                }
            }
        }

        /// <summary>
        /// Height fraction of a crest jump at a distance past its start: an eased-in ramp to a lip,
        /// then a smooth drop into the landing. The sudden change of slope at the lip launches the car.
        /// </summary>
        public static float JumpProfile(float offset, float rampLength, float landingLength)
        {
            const float easeFraction = 0.3f;
            if (offset <= 0f || offset >= rampLength + landingLength)
                return 0f;

            if (offset < rampLength)
            {
                float x = offset / rampLength;
                float slope = 1f / (1f - easeFraction * 0.5f);
                return x < easeFraction
                    ? slope * x * x / (2f * easeFraction)
                    : slope * (x - easeFraction * 0.5f);
            }

            float y = (offset - rampLength) / landingLength;
            return 1f - y * y * (3f - 2f * y);
        }

        private void ApplyBridges()
        {
            var samples = Main.Samples;
            var bridges = Definition.Bridges;
            for (int b = 0; b < bridges.Length; b++)
            {
                float center = AnchorDistance(bridges[b].center);
                float halfLength = bridges[b].length * 0.5f;
                for (int k = 0; k < samples.Length; k++)
                {
                    float offset = Mathf.Repeat(samples[k].Distance - center + Main.Length * 0.5f, Main.Length) - Main.Length * 0.5f;
                    if (Mathf.Abs(offset) <= halfLength)
                        samples[k].BridgeIndex = b;
                }
            }
        }

        private void BuildShortcuts()
        {
            var shortcuts = new List<RoadPath>();
            foreach (var shortcut in Definition.Shortcuts)
            {
                var from = Main.Get(SampleIndexAt(AnchorDistance(shortcut.from)));
                var to = Main.Get(SampleIndexAt(AnchorDistance(shortcut.to)));
                var waypoints = shortcut.waypoints ?? System.Array.Empty<Vector2>();

                var controls = new List<Vector3> { from.Position - from.Forward * ShortcutTangentLength, from.Position };
                foreach (var waypoint in waypoints)
                    controls.Add(new Vector3(waypoint.x, 0f, waypoint.y));
                controls.Add(to.Position);
                controls.Add(to.Position + to.Forward * ShortcutTangentLength);

                var dense = new List<Vector3>();
                for (int i = 1; i < controls.Count - 2; i++)
                {
                    for (int s = 0; s < SubdivisionsPerSegment; s++)
                        dense.Add(CentripetalCatmullRom(controls[i - 1], controls[i], controls[i + 1], controls[i + 2], s / (float)SubdivisionsPerSegment));
                }

                dense.Add(to.Position);
                float[] denseDistances = CumulativeFlatDistances(dense);
                float length = denseDistances[denseDistances.Length - 1];
                int sampleCount = Mathf.Max(2, Mathf.RoundToInt(length / Definition.SampleSpacing) + 1);
                var samples = new TrackSample[sampleCount];
                int cursor = 0;
                for (int k = 0; k < sampleCount; k++)
                {
                    float distance = length * k / (sampleCount - 1);
                    while (cursor < denseDistances.Length - 2 && denseDistances[cursor + 1] < distance)
                        cursor++;
                    float segmentLength = denseDistances[cursor + 1] - denseDistances[cursor];
                    float t = segmentLength > 0f ? (distance - denseDistances[cursor]) / segmentLength : 0f;
                    Vector3 position = Vector3.Lerp(dense[cursor], dense[cursor + 1], t);

                    // Waypoints carry no height, so the shortcut climbs or descends evenly between its two ends.
                    position.y = Mathf.Lerp(from.Position.y, to.Position.y, distance / length) + ShortcutSurfaceLift;
                    samples[k] = new TrackSample
                    {
                        Position = position,
                        Distance = distance,
                        Width = shortcut.width,
                        Surface = shortcut.surface,
                        BridgeIndex = -1,
                    };
                }

                var path = new RoadPath(samples, closed: false, length);
                ComputeFrames(path);
                shortcuts.Add(path);
            }

            Shortcuts = shortcuts;
        }

        private static void ComputeFrames(RoadPath path)
        {
            var samples = path.Samples;
            for (int k = 0; k < samples.Length; k++)
            {
                Vector3 forward = path.Get(k + 1).Position - path.Get(k - 1).Position;
                forward.y = 0f;
                forward.Normalize();
                samples[k].Forward = forward;
                samples[k].Right = new Vector3(forward.z, 0f, -forward.x);
            }

            for (int k = 0; k < samples.Length; k++)
            {
                bool hasFullWindow = path.Closed || (k >= CurvatureWindow && k < samples.Length - CurvatureWindow);
                if (!hasFullWindow)
                {
                    samples[k].Curvature = 0f;
                    continue;
                }

                var before = path.Get(k - CurvatureWindow);
                var after = path.Get(k + CurvatureWindow);
                float angle = Vector3.SignedAngle(before.Forward, after.Forward, Vector3.up) * Mathf.Deg2Rad;
                samples[k].Curvature = angle / ClosedArc(before.Distance, after.Distance, path);
            }
        }

        private static float ClosedArc(float from, float to, RoadPath path)
        {
            float arc = to - from;
            if (path.Closed && arc <= 0f)
                arc += path.Length;
            return Mathf.Max(arc, 1e-3f);
        }

        /// <summary>Centripetal Catmull-Rom (Barry-Goldman) between p1 and p2, knots from horizontal distance.</summary>
        private static Vector3 CentripetalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t0 = 0f;
            float t1 = t0 + Knot(p0, p1);
            float t2 = t1 + Knot(p1, p2);
            float t3 = t2 + Knot(p2, p3);
            float u = Mathf.Lerp(t1, t2, t);

            Vector3 a1 = Blend(p0, p1, t0, t1, u);
            Vector3 a2 = Blend(p1, p2, t1, t2, u);
            Vector3 a3 = Blend(p2, p3, t2, t3, u);
            Vector3 b1 = Blend(a1, a2, t0, t2, u);
            Vector3 b2 = Blend(a2, a3, t1, t3, u);
            return Blend(b1, b2, t1, t2, u);
        }

        private static float Knot(Vector3 a, Vector3 b) => Mathf.Max(Mathf.Sqrt(Vector2.Distance(Flat(a), Flat(b))), 1e-3f);

        private static Vector3 Blend(Vector3 a, Vector3 b, float ta, float tb, float u) =>
            (tb - u) / (tb - ta) * a + (u - ta) / (tb - ta) * b;

        private static float[] CumulativeDistances(List<Vector3> points)
        {
            var distances = new float[points.Count];
            for (int i = 1; i < points.Count; i++)
                distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            return distances;
        }

        private static float[] CumulativeFlatDistances(List<Vector3> points)
        {
            var distances = new float[points.Count];
            for (int i = 1; i < points.Count; i++)
                distances[i] = distances[i - 1] + Vector2.Distance(Flat(points[i - 1]), Flat(points[i]));
            return distances;
        }

        public static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
    }
}
