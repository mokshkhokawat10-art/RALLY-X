using System.Linq;
using NUnit.Framework;
using RallyX.EditorTools;
using RallyX.Tracks;
using UnityEditor;
using UnityEngine;

namespace RallyX.Tests
{
    /// <summary>Geometry checks of generated track layouts, before any scene is built.</summary>
    public sealed class TrackLayoutTests
    {
        private TrackLayout layout;

        [SetUp]
        public void SetUp()
        {
            var definition = AssetDatabase.LoadAssetAtPath<TrackDefinition>(PineValleyTrack.DefinitionPath);
            Assert.IsNotNull(definition, $"Missing {PineValleyTrack.DefinitionPath}. Run the track build first.");
            layout = TrackLayout.Build(definition);
        }

        [Test]
        public void Checkpoints_StartAtTheLineAndStayOffRoadThatShortcutsBypass()
        {
            var distances = CheckpointPlacement.Distances(layout);
            var excluded = CheckpointPlacement.ExcludedSpans(layout);
            float length = layout.Main.Length;
            TestContext.WriteLine($"{distances.Count} gates at {string.Join(", ", distances.Select(d => d.ToString("0")))} m");

            Assert.AreEqual(0f, distances[0], "The first gate is the start/finish line.");
            Assert.GreaterOrEqual(distances.Count, 8, "Too few gates to stop cars cutting the course.");
            float largestGap = length - distances[distances.Count - 1];
            for (int i = 1; i < distances.Count; i++)
            {
                Assert.Greater(distances[i], distances[i - 1], "Gates must be in driving order.");
                largestGap = Mathf.Max(largestGap, distances[i] - distances[i - 1]);
                foreach (var (start, spanLength) in excluded)
                    Assert.Greater(Mathf.Repeat(distances[i] - start, length), spanLength,
                        $"Gate {i} at {distances[i]:0} m is on road a shortcut bypasses, so shortcut takers would miss it.");
            }

            float longestSpan = excluded.Count > 0 ? excluded.Max(span => span.length) : 0f;
            Assert.LessOrEqual(largestGap, layout.Definition.CheckpointSpacing + longestSpan + 1f, "Gates are too far apart.");
        }

        [Test]
        public void PineValley_LayoutHasNoTightCornersSteepGradesOrOverlaps()
        {
            var samples = layout.Main.Samples;
            float minRadius = samples.Min(s => s.Radius);
            TestContext.WriteLine($"Pine Valley: {layout.Main.Length:0} m, {samples.Length} samples, tightest radius {minRadius:0.0} m, " +
                                  $"max grade {MaxGrade():P1}, height range {samples.Min(s => s.Position.y):0.0}-{samples.Max(s => s.Position.y):0.0} m");

            var problems = layout.Validate();
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void MainLoop_IsEvenlySampledAndClosed()
        {
            var main = layout.Main;
            float spacing = main.Length / main.Count;
            for (int i = 0; i < main.Count; i++)
            {
                float gap = Vector3.Distance(main.Get(i).Position, main.Get(i + 1).Position);
                Assert.That(gap, Is.EqualTo(spacing).Within(spacing * 0.1f), $"Uneven spacing at sample {i}.");
            }
        }

        [Test]
        public void Bridges_CoverTheirConfiguredLength()
        {
            var bridges = layout.Definition.Bridges;
            float spacing = layout.Main.Length / layout.Main.Count;
            for (int b = 0; b < bridges.Length; b++)
            {
                int bridgeSamples = layout.Main.Samples.Count(s => s.BridgeIndex == b);
                Assert.That(bridgeSamples * spacing, Is.EqualTo(bridges[b].length).Within(spacing * 2f), $"Bridge {b} length.");
            }
        }

        [Test]
        public void Shortcuts_JoinTheMainRoadAndAreShorterThanTheRoadTheySkip()
        {
            var definition = layout.Definition;
            for (int i = 0; i < layout.Shortcuts.Count; i++)
            {
                var shortcut = layout.Shortcuts[i];
                float from = layout.AnchorDistance(definition.Shortcuts[i].from);
                float to = layout.AnchorDistance(definition.Shortcuts[i].to);
                float skipped = Mathf.Repeat(to - from, layout.Main.Length);
                TestContext.WriteLine($"Shortcut {i}: {shortcut.Length:0} m instead of {skipped:0} m of main road");

                Assert.Less(Vector3.Distance(shortcut.Samples[0].Position, layout.Main.Get(layout.SampleIndexAt(from)).Position), 0.5f);
                Assert.Less(Vector3.Distance(shortcut.Samples[shortcut.Count - 1].Position, layout.Main.Get(layout.SampleIndexAt(to)).Position), 0.5f);
                Assert.Less(shortcut.Length, skipped, "A shortcut must be shorter than the road it bypasses.");
            }
        }

        [Test]
        public void JumpProfile_RisesToLipThenFallsSmoothly()
        {
            const float ramp = 16f;
            const float landing = 30f;
            Assert.AreEqual(0f, TrackLayout.JumpProfile(0f, ramp, landing), 1e-4f);
            Assert.AreEqual(1f, TrackLayout.JumpProfile(ramp, ramp, landing), 1e-3f);
            Assert.AreEqual(0f, TrackLayout.JumpProfile(ramp + landing, ramp, landing), 1e-3f);

            float previous = 0f;
            for (float x = 0.1f; x < ramp; x += 0.1f)
            {
                float value = TrackLayout.JumpProfile(x, ramp, landing);
                Assert.Greater(value, previous, "The ramp must rise continuously.");
                previous = value;
            }
        }

        [Test]
        public void TrackPath_EvaluatesAndFindsPositionsOnALoop()
        {
            const int count = 360;
            const float radius = 100f;
            var points = new Vector3[count];
            var widths = new float[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                points[i] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                widths[i] = 8f;
            }

            var gameObject = new GameObject("TrackPathTest");
            try
            {
                var path = gameObject.AddComponent<TrackPath>();
                path.SetSamples(points, widths);

                Assert.That(path.Length, Is.EqualTo(2f * Mathf.PI * radius).Within(0.5f));
                float quarter = path.Length * 0.25f;
                Vector3 position = path.EvaluatePosition(quarter);
                Assert.That(Vector3.Distance(position, new Vector3(radius, 0f, 0f)), Is.LessThan(0.1f));
                Assert.That(path.FindClosestDistance(position + Vector3.right * 3f), Is.EqualTo(quarter).Within(0.5f));
                Assert.That(path.FindClosestDistance(position, hintDistance: quarter - 5f), Is.EqualTo(quarter).Within(0.5f));
                Assert.Less(Vector3.Distance(path.EvaluatePosition(path.Length + quarter), position), 0.01f, "Distances wrap around the lap.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private float MaxGrade()
        {
            float max = 0f;
            for (int i = 0; i < layout.Main.Count; i++)
            {
                var a = layout.Main.Get(i);
                var b = layout.Main.Get(i + 1);
                float run = Vector2.Distance(TrackLayout.Flat(a.Position), TrackLayout.Flat(b.Position));
                max = Mathf.Max(max, Mathf.Abs(b.Position.y - a.Position.y) / run);
            }

            return max;
        }
    }
}
