using System.Collections.Generic;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.AI
{
    /// <summary>
    /// Works out AI routes from a track: the main loop, and one route through each branch. Each gets a racing line
    /// that cuts corners within the road edges, plus corner radius and surface grip along it.
    /// </summary>
    public static class AIRouteBuilder
    {
        // Points closer together than this are merged where a branch joins the main loop, metres.
        private const float MinPointGap = 0.5f;

        // Main-loop points this close to where a branch leaves or rejoins count as part of the replaced stretch, metres.
        private const float JunctionTolerance = 0.05f;

        // Chord over which corner radius is measured, metres. Long enough to ignore small wobbles in the line.
        private const float RadiusChord = 8f;

        private const float GripProbeHeight = 4f;

        // Racing line smoothing works from long sweeps down to detail: neighbours this many points either side.
        // Bending evenly spreads slowly along the line, so the coarse passes do most of the reshaping.
        private static readonly int[] SmoothingScales = { 32, 16, 8, 4, 2 };

        /// <summary>The main-loop route first, then one route through each branch of <paramref name="path"/>.</summary>
        /// <param name="groundMask">Layers to read road surfaces from (exclude the cars).</param>
        public static List<AIRoute> Build(TrackPath path, IReadOnlyList<TrackBranch> branches, AISettings settings, int groundMask)
        {
            var routes = new List<AIRoute> { BuildRoute("Main", path, null, settings, groundMask) };
            foreach (var branch in branches)
            {
                if (branch != null && branch.MainPath == path && branch.Count >= 2)
                    routes.Add(BuildRoute(branch.name, path, branch, settings, groundMask));
            }

            return routes;
        }

        public static AIRoute BuildRoute(string name, TrackPath path, TrackBranch branch, AISettings settings, int groundMask)
        {
            var centre = new List<Vector3>(path.Count + 64);
            var widths = new List<float>(path.Count + 64);
            CollectCentreline(path, branch, centre, widths);

            int count = centre.Count;
            var rights = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = centre[LoopMath.Wrap(i + 1, count)] - centre[LoopMath.Wrap(i - 1, count)];
                tangent.y = 0f;
                tangent.Normalize();
                rights[i] = new Vector3(tangent.z, 0f, -tangent.x);
            }

            float[] offsets = RacingLineOffsets(centre, widths, rights, settings);
            var points = new Vector3[count];
            var roomLeft = new float[count];
            var roomRight = new float[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = centre[i] + rights[i] * offsets[i];
                float halfRoom = widths[i] * 0.5f - settings.EdgeMargin;
                roomLeft[i] = Mathf.Max(0f, halfRoom + offsets[i]);
                roomRight[i] = Mathf.Max(0f, halfRoom - offsets[i]);
            }

            float spacing = 0f;
            for (int i = 0; i < count; i++)
                spacing += Vector3.Distance(points[i], points[LoopMath.Wrap(i + 1, count)]);
            spacing /= count;

            int chord = Mathf.Max(2, Mathf.RoundToInt(RadiusChord / spacing));
            var radii = new float[count];
            var grip = new float[count];
            for (int i = 0; i < count; i++)
            {
                radii[i] = CircleRadius(points[LoopMath.Wrap(i - chord, count)], points[i], points[LoopMath.Wrap(i + chord, count)]);
                grip[i] = SurfaceGrip(points[i], groundMask);
            }

            return new AIRoute(name, branch, points, rights, roomLeft, roomRight, radii, grip);
        }

        /// <summary>
        /// The centreline of the whole loop. Through a branch, the main-loop stretch it bypasses is left out and the
        /// branch's own centreline goes in its place.
        /// </summary>
        private static void CollectCentreline(TrackPath path, TrackBranch branch, List<Vector3> centre, List<float> widths)
        {
            int count = path.Count;
            if (branch == null)
            {
                for (int i = 0; i < count; i++)
                    Add(centre, widths, path.GetPoint(i), path.GetWidth(i));
                return;
            }

            float length = path.Length;
            float leave = branch.LeaveDistance;
            float rejoin = branch.RejoinDistance;
            float bypassed = Mathf.Repeat(rejoin - leave, length);

            // Main-loop points from the leave point to the rejoin point inclusive are replaced by the branch, whose own
            // ends sit exactly on them. Start just after the rejoin, so the replaced stretch comes last in one piece.
            int start = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float afterRejoin = Mathf.Repeat(path.GetDistance(i) - rejoin, length);
                if (afterRejoin > JunctionTolerance && afterRejoin < nearest)
                {
                    nearest = afterRejoin;
                    start = i;
                }
            }

            for (int n = 0; n < count; n++)
            {
                int i = (start + n) % count;
                if (Mathf.Repeat(path.GetDistance(i) - leave + JunctionTolerance, length) > bypassed + 2f * JunctionTolerance)
                    Add(centre, widths, path.GetPoint(i), path.GetWidth(i));
            }

            for (int i = 0; i < branch.Count; i++)
                Add(centre, widths, branch.GetPoint(i), branch.GetWidth(i));
        }

        private static void Add(List<Vector3> centre, List<float> widths, Vector3 point, float width)
        {
            if (centre.Count > 0 && Vector3.Distance(centre[centre.Count - 1], point) < MinPointGap)
                return;

            centre.Add(point);
            widths.Add(width);
        }

        /// <summary>
        /// Sideways offsets from the centreline that make the line bend as gently as the road allows, within its edges:
        /// wide into a corner, tight at the apex, wide on the way out. Each pass moves every point towards where a
        /// smooth curve through its neighbours would put it (the curve's bend stays even: fourth differences vanish).
        /// </summary>
        private static float[] RacingLineOffsets(List<Vector3> centre, List<float> widths, Vector3[] rights, AISettings settings)
        {
            int count = centre.Count;
            var offsets = new float[count];
            var limits = new float[count];
            for (int i = 0; i < count; i++)
                limits[i] = Mathf.Max(0f, widths[i] * 0.5f - settings.EdgeMargin);

            Vector3 LinePoint(int index)
            {
                int wrapped = LoopMath.Wrap(index, count);
                return centre[wrapped] + rights[wrapped] * offsets[wrapped];
            }

            int passes = settings.LineSmoothing / SmoothingScales.Length;
            foreach (int scale in SmoothingScales)
            {
                for (int pass = 0; pass < passes; pass++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 smooth = (4f * (LinePoint(i - scale) + LinePoint(i + scale)) - LinePoint(i - 2 * scale) - LinePoint(i + 2 * scale)) / 6f;
                        float wanted = Vector3.Dot(smooth - centre[i], rights[i]);
                        offsets[i] = Mathf.Clamp(Mathf.Lerp(offsets[i], wanted, 0.5f), -limits[i], limits[i]);
                    }
                }
            }

            return offsets;
        }

        private static float CircleRadius(Vector3 a, Vector3 b, Vector3 c)
        {
            a.y = b.y = c.y = 0f;
            float twiceArea = Vector3.Cross(b - a, c - a).magnitude;
            return twiceArea < 1e-4f
                ? float.PositiveInfinity
                : Vector3.Distance(a, b) * Vector3.Distance(b, c) * Vector3.Distance(c, a) / (2f * twiceArea);
        }

        private static float SurfaceGrip(Vector3 point, int groundMask)
        {
            if (!Physics.Raycast(point + Vector3.up * GripProbeHeight, Vector3.down, out var hit, GripProbeHeight * 2.5f, groundMask,
                    QueryTriggerInteraction.Ignore))
                return 1f;

            var source = hit.collider.GetComponent<ISurfaceSource>();
            var surface = source != null ? source.GetSurfaceAt(hit.point) : null;
            return surface != null ? surface.Properties.Grip : 1f;
        }
    }
}
