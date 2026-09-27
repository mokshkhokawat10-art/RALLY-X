using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// A track drawn as a line: its outline (normalised to a unit square, see <see cref="Normalise"/>), a yellow tick at the
    /// start line and optional dots for cars. Drawn with the vector painter, so it stays sharp at any size and costs one
    /// small mesh. Used on the track selection screen and as the race HUD's minimap.
    /// </summary>
    public sealed class TrackMap : VisualElement
    {
        private readonly List<(Vector2 point, Color color, float radius)> markers = new List<(Vector2, Color, float)>();
        private Vector2[] outline = System.Array.Empty<Vector2>();

        public TrackMap()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public Color LineColor { get; set; } = Color.white;
        public Color CasingColor { get; set; } = new Color(0f, 0f, 0f, 0.65f);
        public Color StartColor { get; set; } = new Color(1f, 0.77f, 0.14f);
        public float LineWidth { get; set; } = 6f;

        /// <summary>Space kept clear round the drawing, as a share of the element's smaller side.</summary>
        public float Padding { get; set; } = 0.08f;

        public bool HasOutline => outline.Length > 1;

        /// <summary>The track to draw: points round the lap from the start line, normalised with <see cref="Normalise"/>.</summary>
        public void SetOutline(Vector2[] normalisedOutline)
        {
            outline = normalisedOutline ?? System.Array.Empty<Vector2>();
            MarkDirtyRepaint();
        }

        /// <summary>Removes the car dots (call before adding this frame's).</summary>
        public void ClearMarkers() => markers.Clear();

        /// <summary>A dot at a normalised point. Later dots are drawn on top.</summary>
        public void AddMarker(Vector2 normalisedPoint, Color color, float radius) => markers.Add((normalisedPoint, color, radius));

        /// <summary>Redraws after the markers changed.</summary>
        public void Refresh() => MarkDirtyRepaint();

        /// <summary>
        /// Fits map points (x east, y north) into a unit square, keeping their shape and centring them, and returns the
        /// mapping, so other points (cars) can be placed with <see cref="ToNormalised"/>.
        /// </summary>
        public static Vector2[] Normalise(IReadOnlyList<Vector2> points, out Vector2 centre, out float size)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var point in points)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            centre = (min + max) * 0.5f;
            size = Mathf.Max(max.x - min.x, max.y - min.y, 0.001f);
            var normalised = new Vector2[points.Count];
            for (int i = 0; i < normalised.Length; i++)
                normalised[i] = ToNormalised(points[i], centre, size);
            return normalised;
        }

        public static Vector2 ToNormalised(Vector2 point, Vector2 centre, float size) => (point - centre) / size + new Vector2(0.5f, 0.5f);

        private Vector2 ToLocal(Vector2 normalised, Rect rect, float scale) =>
            new Vector2(rect.center.x + (normalised.x - 0.5f) * scale, rect.center.y - (normalised.y - 0.5f) * scale);

        private void Draw(MeshGenerationContext context)
        {
            if (outline.Length < 2)
                return;

            var rect = contentRect;
            float side = Mathf.Min(rect.width, rect.height);
            if (side <= 1f)
                return;

            float scale = side * (1f - 2f * Padding);
            var painter = context.painter2D;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;

            // A dark casing under the line, so it reads over any background.
            StrokeOutline(painter, rect, scale, CasingColor, LineWidth + 5f);
            StrokeOutline(painter, rect, scale, LineColor, LineWidth);

            // The start line: a short yellow tick across the track.
            Vector2 start = ToLocal(outline[0], rect, scale);
            Vector2 along = (ToLocal(outline[1], rect, scale) - start).normalized;
            Vector2 across = new Vector2(-along.y, along.x) * (LineWidth * 1.6f + 4f);
            painter.strokeColor = StartColor;
            painter.lineWidth = LineWidth * 0.8f;
            painter.BeginPath();
            painter.MoveTo(start - across);
            painter.LineTo(start + across);
            painter.Stroke();

            foreach (var (point, color, radius) in markers)
            {
                Vector2 at = ToLocal(point, rect, scale);
                painter.fillColor = new Color(0f, 0f, 0f, 0.7f);
                painter.BeginPath();
                painter.Arc(at, radius + 2.5f, 0f, 360f);
                painter.Fill();
                painter.fillColor = color;
                painter.BeginPath();
                painter.Arc(at, radius, 0f, 360f);
                painter.Fill();
            }
        }

        private void StrokeOutline(Painter2D painter, Rect rect, float scale, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(ToLocal(outline[0], rect, scale));
            for (int i = 1; i < outline.Length; i++)
                painter.LineTo(ToLocal(outline[i], rect, scale));
            painter.ClosePath();
            painter.Stroke();
        }
    }
}
