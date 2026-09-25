using UnityEngine;

namespace RallyX.Tracks
{
    /// <summary>Assigns one surface to every point of the collider on the same GameObject.</summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceMarker : MonoBehaviour, ISurfaceSource
    {
        [SerializeField] private SurfaceType surface;

        public SurfaceType Surface
        {
            get => surface;
            set => surface = value;
        }

        public SurfaceType GetSurfaceAt(Vector3 worldPoint) => surface;
    }
}
