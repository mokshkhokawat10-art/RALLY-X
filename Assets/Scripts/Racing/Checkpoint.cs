using UnityEngine;

namespace RallyX.Racing
{
    /// <summary>
    /// A gate across the road that racers must drive through in the direction it faces. The gate is a plane,
    /// not a collider, so it never affects physics, wheel raycasts or the camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Checkpoint : MonoBehaviour
    {
        // How far below its base a racer can pass and still count, metres (dips in the road, low-slung cars).
        private const float DepthBelowBase = 3f;

        [Tooltip("Width across the road, metres. Cover the road plus some run-off so cars running wide still count.")]
        [SerializeField, Min(1f)] private float width = 20f;

        [Tooltip("Height above the base, metres. Tall enough for cars in the air.")]
        [SerializeField, Min(1f)] private float height = 12f;

        public float Width => width;
        public float Height => height;

        public void SetSize(float newWidth, float newHeight)
        {
            width = newWidth;
            height = newHeight;
        }

        /// <summary>
        /// True when a racer moving from <paramref name="from"/> to <paramref name="to"/> went through the gate
        /// forwards. <paramref name="fraction"/> is how far along that move it crossed (0..1).
        /// </summary>
        public bool Crossed(Vector3 from, Vector3 to, out float fraction)
        {
            fraction = 0f;
            Vector3 origin = transform.position;
            Vector3 forward = Forward;
            float before = Vector3.Dot(from - origin, forward);
            float after = Vector3.Dot(to - origin, forward);
            if (before >= 0f || after < 0f)
                return false;

            fraction = before / (before - after);
            Vector3 offset = Vector3.Lerp(from, to, fraction) - origin;
            float across = Vector3.Dot(offset, new Vector3(forward.z, 0f, -forward.x));
            return Mathf.Abs(across) <= width * 0.5f && offset.y >= -DepthBelowBase && offset.y <= height;
        }

        /// <summary>Horizontal direction racers must pass through the gate.</summary>
        public Vector3 Forward
        {
            get
            {
                Vector3 forward = transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.78f, 0.1f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.LookRotation(Forward), Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, 0.05f));
            Gizmos.DrawLine(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 4f));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
