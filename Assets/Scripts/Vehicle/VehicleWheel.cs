using RallyX.Tracks;
using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>
    /// One wheel of a VehicleController: suspension raycast, ground contact, surface lookup and visuals.
    /// It has no Update of its own; the controller steps every wheel in a fixed order.
    /// The GameObject marks the top of the suspension and must share the vehicle's orientation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleWheel : MonoBehaviour
    {
        [SerializeField] private bool isFront;

        [Tooltip("Child transform moved and rotated to show suspension travel, steering and spin.")]
        [SerializeField] private Transform visual;

        public bool IsFront => isFront;
        public bool IsGrounded { get; private set; }
        public RaycastHit Hit => hit;

        /// <summary>Current spring length, metres: 0 = fully compressed, suspension travel = fully extended.</summary>
        public float SpringLength { get; private set; }

        /// <summary>Upward force at this wheel, newtons. Also the tyre's normal load.</summary>
        public float SuspensionForce { get; private set; }

        public float SteerAngle { get; internal set; }

        /// <summary>Surface under the wheel from the last grounded step (null if none was found).</summary>
        public SurfaceType SurfaceType { get; private set; }

        public SurfaceProperties Surface { get; private set; } = SurfaceProperties.Neutral;

        private RaycastHit hit;
        private Vector3 localMount;
        private float previousSpringLength;
        private float spinAngle;
        private float spinSpeed;
        private Collider lastCollider;
        private ISurfaceSource lastSurfaceSource;

        internal void Initialize(Rigidbody body, VehicleConfig config)
        {
            localMount = body.transform.InverseTransformPoint(transform.position);
            ResetState(config);
        }

        internal void ResetState(VehicleConfig config)
        {
            SpringLength = previousSpringLength = config.SuspensionTravel;
            SuspensionForce = 0f;
            IsGrounded = false;
            spinSpeed = 0f;
        }

        internal Vector3 GetForward(Quaternion bodyRotation) =>
            bodyRotation * (Quaternion.Euler(0f, SteerAngle, 0f) * Vector3.forward);

        /// <summary>Casts for ground, applies the spring/damper force and resolves the surface. Returns true when grounded.</summary>
        internal bool UpdateSuspension(Rigidbody body, VehicleConfig config, int groundMask, SurfaceType fallbackSurface,
            float springRate, float damperRate, float deltaTime)
        {
            // Use the rigidbody pose, not the transform: with interpolation the transform lags the physics state.
            Quaternion rotation = body.rotation;
            Vector3 up = rotation * Vector3.up;
            Vector3 origin = body.position + rotation * localMount;
            float travel = config.SuspensionTravel;

            IsGrounded = Physics.Raycast(origin, -up, out hit, travel + config.WheelRadius, groundMask, QueryTriggerInteraction.Ignore);
            if (!IsGrounded)
            {
                SpringLength = previousSpringLength = travel;
                SuspensionForce = 0f;
                return false;
            }

            SpringLength = Mathf.Max(0f, hit.distance - config.WheelRadius);
            float compressionSpeed = (previousSpringLength - SpringLength) / deltaTime;
            previousSpringLength = SpringLength;

            SuspensionForce = Mathf.Max(0f, (travel - SpringLength) * springRate + compressionSpeed * damperRate);
            body.AddForceAtPosition(up * SuspensionForce, origin);

            ResolveSurface(fallbackSurface);
            return true;
        }

        internal void UpdateSpin(float rollingSpeed, bool locked, float wheelRadius, float deltaTime)
        {
            spinSpeed = locked ? 0f : rollingSpeed / wheelRadius * Mathf.Rad2Deg;
            AdvanceSpin(deltaTime);
        }

        /// <summary>Keeps an airborne wheel spinning at its last speed.</summary>
        internal void AdvanceSpin(float deltaTime) => spinAngle = Mathf.Repeat(spinAngle + spinSpeed * deltaTime, 360f);

        internal void UpdateVisual()
        {
            if (visual == null)
                return;

            visual.localPosition = new Vector3(0f, -SpringLength, 0f);
            visual.localRotation = Quaternion.Euler(0f, SteerAngle, 0f) * Quaternion.Euler(spinAngle, 0f, 0f);
        }

        private void ResolveSurface(SurfaceType fallbackSurface)
        {
            // Wheels stay on the same collider for long stretches, so only look up the source when it changes.
            Collider groundCollider = hit.collider;
            if (groundCollider != lastCollider)
            {
                lastCollider = groundCollider;
                groundCollider.TryGetComponent(out lastSurfaceSource);
            }

            SurfaceType surface = lastSurfaceSource != null ? lastSurfaceSource.GetSurfaceAt(hit.point) : null;
            if (surface == null)
                surface = fallbackSurface;

            SurfaceType = surface;
            Surface = surface != null ? surface.Properties : SurfaceProperties.Neutral;
        }

        private void OnDrawGizmosSelected()
        {
            var controller = GetComponentInParent<VehicleController>();
            if (controller == null || controller.Config == null)
                return;

            var config = controller.Config;
            Vector3 origin = transform.position;
            Vector3 down = -transform.up;
            float springLength = Application.isPlaying ? SpringLength : config.SuspensionTravel;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + down * (config.SuspensionTravel + config.WheelRadius));
            Gizmos.color = IsGrounded ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(origin + down * springLength, config.WheelRadius);
        }
    }
}
