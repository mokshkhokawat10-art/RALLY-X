using System.Collections.Generic;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Chooses where a track's checkpoint gates go along the main loop: the start/finish line, then roughly every
    /// <see cref="TrackDefinition.CheckpointSpacing"/> metres. Gates stay off main road that a shortcut bypasses
    /// (so either route passes every gate) and away from the junctions at either end.
    /// </summary>
    public static class CheckpointPlacement
    {
        /// <summary>Gate distances along the main loop from the start line, in driving order; the first is 0.</summary>
        public static List<float> Distances(TrackLayout layout)
        {
            var definition = layout.Definition;
            float length = layout.Main.Length;
            float spacing = definition.CheckpointSpacing;
            var excluded = ExcludedSpans(layout);

            var distances = new List<float> { 0f };
            float distance = spacing;
            while (distance < length - spacing * 0.5f)
            {
                if (TryGetSpanEnd(excluded, distance, length, out float spanEnd))
                {
                    distance = spanEnd;
                    continue;
                }

                distances.Add(distance);
                distance += spacing;
            }

            return distances;
        }

        /// <summary>Stretches of the main loop, as (start, length), where no gate may go.</summary>
        public static List<(float start, float length)> ExcludedSpans(TrackLayout layout)
        {
            var definition = layout.Definition;
            float lapLength = layout.Main.Length;
            float clearance = definition.CheckpointJunctionClearance;
            var spans = new List<(float, float)>();
            foreach (var shortcut in definition.Shortcuts)
            {
                float start = layout.AnchorDistance(shortcut.from) - clearance;
                float end = layout.AnchorDistance(shortcut.to) + clearance;
                spans.Add((Mathf.Repeat(start, lapLength), Mathf.Repeat(end - start, lapLength)));
            }

            return spans;
        }

        private static bool TryGetSpanEnd(List<(float start, float length)> spans, float distance, float lapLength, out float end)
        {
            foreach (var (start, spanLength) in spans)
            {
                float into = Mathf.Repeat(distance - start, lapLength);
                if (into <= spanLength)
                {
                    // Step just past the span; a small margin avoids landing exactly on its edge again.
                    end = distance + (spanLength - into) + 0.01f;
                    return true;
                }
            }

            end = distance;
            return false;
        }
    }
}
