using UnityEngine;

namespace RallyX.Cameras
{
    /// <summary>Physics queries that keep a camera out of walls and the ground.</summary>
    public static class CameraOcclusion
    {
        /// <summary>
        /// Fraction (0..1) of the way from <paramref name="origin"/> to <paramref name="desired"/> that a sphere
        /// of <paramref name="radius"/> can travel before touching an obstacle.
        /// </summary>
        public static float ClearFraction(Vector3 origin, Vector3 desired, float radius, int obstacleMask)
        {
            Vector3 offset = desired - origin;
            float length = offset.magnitude;
            if (length < 1e-4f)
                return 1f;

            return Physics.SphereCast(origin, radius, offset / length, out RaycastHit hit, length, obstacleMask, QueryTriggerInteraction.Ignore)
                ? hit.distance / length
                : 1f;
        }

        /// <summary>
        /// Raises <paramref name="position"/> so it sits at least <paramref name="clearance"/> above the ground below it.
        /// The probe starts at <paramref name="probeTopY"/> (e.g. the car's height), so overhead bridges are ignored.
        /// </summary>
        public static Vector3 KeepAboveGround(Vector3 position, float probeTopY, float clearance, int obstacleMask)
        {
            float startY = Mathf.Max(position.y, probeTopY);
            var start = new Vector3(position.x, startY, position.z);
            float probeLength = startY - position.y + clearance;

            if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, probeLength, obstacleMask, QueryTriggerInteraction.Ignore))
                position.y = Mathf.Max(position.y, hit.point.y + clearance);

            return position;
        }
    }
}
