using UnityEngine;

namespace RallyX.Tracks
{
    /// <summary>
    /// Implemented by components on ground colliders that report which surface is at a point.
    /// Lets uniform surfaces (SurfaceMarker) and future per-point sources (e.g. terrain layers) coexist.
    /// </summary>
    public interface ISurfaceSource
    {
        /// <summary>Surface at a world-space point, or null to use the vehicle's fallback surface.</summary>
        SurfaceType GetSurfaceAt(Vector3 worldPoint);
    }
}
