using System.Collections.Generic;
using NUnit.Framework;
using RallyX.AI;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.Tests
{
    /// <summary>
    /// AI routes on a synthetic oval: 200 m straights joined by 30 m-radius half circles, 12 m wide, driven
    /// anticlockwise seen from above, with a 6 m wide shortcut straight across the right-hand end.
    /// </summary>
    public sealed class AIRouteTests
    {
        private const float StraightLength = 200f;
        private const float BendRadius = 30f;
        private const float RoadWidth = 12f;
        private const float BranchWidth = 6f;
        private const float BranchX = 60f;
        private const float Spacing = 2f;

        private GameObject root;
        private TrackPath path;
        private TrackBranch branch;
        private AISettings settings;

        [SetUp]
        public void CreateOval()
        {
            root = new GameObject("TestOval");
            var points = new List<Vector3>();
            float halfStraight = StraightLength * 0.5f;
            AddStraight(points, new Vector3(-halfStraight, 0f, -BendRadius), new Vector3(halfStraight, 0f, -BendRadius));
            AddBend(points, new Vector3(halfStraight, 0f, 0f), -90f);
            AddStraight(points, new Vector3(halfStraight, 0f, BendRadius), new Vector3(-halfStraight, 0f, BendRadius));
            AddBend(points, new Vector3(-halfStraight, 0f, 0f), 90f);

            var widths = new float[points.Count];
            for (int i = 0; i < widths.Length; i++)
                widths[i] = RoadWidth;
            path = Child("Path").AddComponent<TrackPath>();
            path.SetSamples(points.ToArray(), widths);

            // Straight across from the bottom straight to the top one, skipping the right-hand bend.
            var branchPoints = new List<Vector3>();
            AddStraight(branchPoints, new Vector3(BranchX, 0f, -BendRadius), new Vector3(BranchX, 0f, BendRadius));
            branchPoints.Add(new Vector3(BranchX, 0f, BendRadius));
            var branchWidths = new float[branchPoints.Count];
            for (int i = 0; i < branchWidths.Length; i++)
                branchWidths[i] = BranchWidth;
            branch = Child("Branch").AddComponent<TrackBranch>();
            branch.SetSamples(path, branchPoints.ToArray(), branchWidths);

            settings = ScriptableObject.CreateInstance<AISettings>();
        }

        [TearDown]
        public void DestroyOval()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void RacingLine_StaysOnTheRoadAndTakesTheBendsWideInsideWide()
        {
            var route = AIRouteBuilder.BuildRoute("Main", path, null, settings, 0);
            float allowed = RoadWidth * 0.5f - settings.EdgeMargin;
            float furthest = 0f;
            for (int i = 0; i < route.Count; i++)
            {
                Vector3 point = route.PointAt(i);
                furthest = Mathf.Max(furthest, FlatDistance(point, path.EvaluatePosition(path.FindClosestDistance(point))));
            }

            // The right-hand bend turns left: its outside is south (-z) on the way in and north (+z) on the way out.
            float halfStraight = StraightLength * 0.5f;
            var bendCentre = new Vector3(halfStraight, 0f, 0f);
            float entry = route.Evaluate(route.FindClosest(new Vector3(halfStraight - 20f, 0f, -BendRadius))).z + BendRadius;
            float exit = route.Evaluate(route.FindClosest(new Vector3(halfStraight - 20f, 0f, BendRadius))).z - BendRadius;
            float apex = FlatDistance(route.Evaluate(route.FindClosest(bendCentre + Vector3.right * BendRadius)), bendCentre);

            TestContext.WriteLine($"Line {route.Length:0} m (centreline {path.Length:0} m), furthest from centre {furthest:0.00} m (allowed {allowed:0.00}); " +
                                  $"20 m before the bend {entry:+0.0;-0.0} m, apex {apex:0.0} m from the bend's centre ({BendRadius} m), " +
                                  $"20 m after {exit:+0.0;-0.0} m");
            Assert.LessOrEqual(furthest, allowed + 0.05f, "The line must stay clear of the road edges.");
            Assert.Less(entry, -1f, "The line should come into the bend from the outside.");
            Assert.Less(apex, BendRadius - allowed * 0.6f, "The line should be on the inside at the apex.");
            Assert.Greater(exit, 1f, "The line should run out to the outside after the bend.");
            Assert.LessOrEqual(route.Length, path.Length);
        }

        [Test]
        public void RacingLine_BendsGraduallyUnlikeTheCentreline()
        {
            var line = AIRouteBuilder.BuildRoute("Main", path, null, settings, 0);
            var unsmoothed = ScriptableObject.CreateInstance<AISettings>();
            var serialized = new UnityEditor.SerializedObject(unsmoothed);
            serialized.FindProperty("lineSmoothing").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var centreline = AIRouteBuilder.BuildRoute("Centre", path, null, unsmoothed, 0);
            Object.DestroyImmediate(unsmoothed);

            float lineChange = LargestCurvatureChange(line);
            float centreChange = LargestCurvatureChange(centreline);
            TestContext.WriteLine($"Largest change in curvature between neighbouring points: line {lineChange:0.00000}, centreline {centreChange:0.00000} per metre");
            Assert.Less(lineChange, centreChange * 0.5f, "Steering into and out of bends should build up gradually.");
        }

        [Test]
        public void BranchRoute_GoesThroughTheBranchWithoutJumps()
        {
            var main = AIRouteBuilder.BuildRoute("Main", path, null, settings, 0);
            var shortcut = AIRouteBuilder.BuildRoute("Branch", path, branch, settings, 0);

            float longestSegment = 0f;
            for (int i = 0; i < shortcut.Count; i++)
                longestSegment = Mathf.Max(longestSegment, shortcut.SegmentLength(i));

            float furthestFromBranch = 0f;
            for (int i = 0; i < branch.Count; i++)
            {
                Vector3 point = branch.GetPoint(i);
                furthestFromBranch = Mathf.Max(furthestFromBranch, FlatDistance(point, shortcut.Evaluate(shortcut.FindClosest(point))));
            }

            TestContext.WriteLine($"Shortcut route {shortcut.Length:0} m vs main {main.Length:0} m; longest segment {longestSegment:0.00} m; " +
                                  $"furthest branch point from the line {furthestFromBranch:0.00} m");
            Assert.Less(longestSegment, Spacing * 2f, "The route must not jump between separate parts of the track.");
            Assert.Less(furthestFromBranch, BranchWidth * 0.5f, "The route should follow the branch.");
            Assert.Less(shortcut.Length, main.Length - 60f, "Through the branch should be the shorter way round.");
            Assert.AreSame(branch, shortcut.Branch);
            Assert.IsNull(main.Branch);
        }

        [Test]
        public void SpeedPlan_SlowsForBendsAndBrakesForThemInTime()
        {
            const float cornerGrip = 8f;
            const float braking = 10f;
            const float topSpeed = 40f;
            var route = AIRouteBuilder.BuildRoute("Main", path, null, settings, 0);
            float[] plan = route.PlanSpeeds(cornerGrip, braking, topSpeed);

            float slowest = float.MaxValue;
            float fastest = 0f;
            for (int i = 0; i < plan.Length; i++)
            {
                int next = (i + 1) % plan.Length;
                Assert.LessOrEqual(plan[i], Mathf.Sqrt(cornerGrip * route.Radius(i)) + 1e-3f, $"Too fast for the bend at point {i}.");
                Assert.LessOrEqual(plan[i], topSpeed + 1e-3f);
                Assert.LessOrEqual(plan[i] * plan[i], plan[next] * plan[next] + 2f * braking * route.SegmentLength(i) + 1e-2f,
                    $"Can't brake in time from point {i}.");
                slowest = Mathf.Min(slowest, plan[i]);
                fastest = Mathf.Max(fastest, plan[i]);
            }

            TestContext.WriteLine($"Planned speeds {slowest * 3.6f:0}-{fastest * 3.6f:0} km/h");
            Assert.AreEqual(topSpeed, fastest, 1e-3f, "The long straights should reach top speed.");
            Assert.Less(slowest, 20f, "The bends should be slower.");
        }

        [Test]
        public void SpeedPlan_TakesOffSlowlyEnoughToMakeTheBendAfterAJump()
        {
            // A 1.5 m kicker at the end of the bottom straight, its lip 50 m before the right-hand bend.
            const float lipX = 50f;
            const float lipHeight = 1.5f;
            var jumpPoints = new Vector3[path.Count];
            var jumpWidths = new float[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 point = path.GetPoint(i);
                if (point.z < -BendRadius + 0.1f && point.x >= lipX - 20f && point.x <= lipX)
                    point.y = lipHeight * (point.x - (lipX - 20f)) / 20f;
                jumpPoints[i] = point;
                jumpWidths[i] = path.GetWidth(i);
            }

            var jumpPath = Child("JumpPath").AddComponent<TrackPath>();
            jumpPath.SetSamples(jumpPoints, jumpWidths);
            var flat = AIRouteBuilder.BuildRoute("Flat", path, null, settings, 0);
            var jump = AIRouteBuilder.BuildRoute("Jump", jumpPath, null, settings, 0);
            float[] flatPlan = flat.PlanSpeeds(8f, 10f, 40f);
            float[] jumpPlan = jump.PlanSpeeds(8f, 10f, 40f);

            var lip = new Vector3(lipX, lipHeight, -BendRadius);
            int flatLip = flat.IndexAt(flat.FindClosest(lip));
            int jumpLip = jump.IndexAt(jump.FindClosest(lip));
            float inTheAir = jumpPlan[jump.IndexAt(jump.FindClosest(new Vector3(lipX + 10f, 0f, -BendRadius)))];
            TestContext.WriteLine($"Speed at the lip: {flatPlan[flatLip] * 3.6f:0} km/h without the jump, {jumpPlan[jumpLip] * 3.6f:0} km/h with it; " +
                                  $"{inTheAir * 3.6f:0} km/h 10 m on, in the air");
            Assert.Less(jumpPlan[jumpLip], flatPlan[flatLip] - 3f, "Braking for the bend has to be done before taking off.");
            Assert.AreEqual(jumpPlan[jumpLip], inTheAir, 0.5f, "Speed can't change in the air.");
        }

        private static float LargestCurvatureChange(AIRoute route)
        {
            float largest = 0f;
            for (int i = 0; i < route.Count; i++)
            {
                float change = Mathf.Abs(1f / route.Radius(i + 1) - 1f / route.Radius(i)) / route.SegmentLength(i);
                largest = Mathf.Max(largest, change);
            }

            return largest;
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child;
        }

        /// <summary>Points every <see cref="Spacing"/> metres from <paramref name="from"/> up to (not including) <paramref name="to"/>.</summary>
        private static void AddStraight(List<Vector3> points, Vector3 from, Vector3 to)
        {
            int steps = Mathf.RoundToInt(Vector3.Distance(from, to) / Spacing);
            for (int i = 0; i < steps; i++)
                points.Add(Vector3.Lerp(from, to, i / (float)steps));
        }

        /// <summary>A half circle turning left (anticlockwise seen from above), starting at <paramref name="startDegrees"/>.</summary>
        private static void AddBend(List<Vector3> points, Vector3 centre, float startDegrees)
        {
            int steps = Mathf.RoundToInt(Mathf.PI * BendRadius / Spacing);
            for (int i = 0; i < steps; i++)
            {
                float radians = (startDegrees + 180f * i / steps) * Mathf.Deg2Rad;
                points.Add(centre + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * BendRadius);
            }
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
