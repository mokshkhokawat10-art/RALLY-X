using System;
using UnityEngine;

namespace RallyX.Tracks
{
    /// <summary>
    /// A track's own colours for the dust, spray and tyre marks its surfaces make, so red canyon dirt throws red dust
    /// while the same surface type in a pine forest throws brown. Surfaces not listed use their own colours. The track
    /// builder adds one to each track scene from its theme.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfacePalette : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public SurfaceType surface;
            public Color sprayColor;
            public Color markColor;

            public Entry(SurfaceType surface, Color sprayColor, Color markColor)
            {
                this.surface = surface;
                this.sprayColor = sprayColor;
                this.markColor = markColor;
            }
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        /// <summary>The palette of the loaded track, or null.</summary>
        public static SurfacePalette Active { get; private set; }

        public void SetEntries(Entry[] newEntries) => entries = newEntries ?? Array.Empty<Entry>();

        /// <summary>The spray colour for a surface on the loaded track (its own colour when no track palette lists it).</summary>
        public static Color SprayColor(SurfaceType surface) =>
            Active != null && Active.TryFind(surface, out var entry) ? entry.sprayColor : surface.SprayColor;

        /// <summary>The tyre-mark colour for a surface on the loaded track (its own colour when no track palette lists it).</summary>
        public static Color MarkColor(SurfaceType surface) =>
            Active != null && Active.TryFind(surface, out var entry) ? entry.markColor : surface.MarkColor;

        private bool TryFind(SurfaceType surface, out Entry found)
        {
            foreach (var entry in entries)
            {
                if (entry.surface == surface)
                {
                    found = entry;
                    return true;
                }
            }

            found = default;
            return false;
        }

        private void OnEnable() => Active = this;

        private void OnDisable()
        {
            if (Active == this)
                Active = null;
        }
    }
}
