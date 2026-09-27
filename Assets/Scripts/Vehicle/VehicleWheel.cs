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
        // A spinning wheel on a car at a standstill still turns visibly, degrees per second at full wheelspin.
        private const float MinSpinDegreesPerSecond = 720f;

        // Share of the bump stop's force left while the wheel extends again.
        private const float BumpStopReturn = 0.2f;

        // A wheel mounted closer than this to the car's centre line, metres, is on neither side.
        private const float CentreLineTolerance = 0.05f;

        [SerializeField] private bool isFront;

        [Tooltip("Child transform moved and rotated to show suspension travel, steering and (without a spin visual) spin.")]
        [SerializeField] private Transform visual;

        [Tooltip("Optional child of the visual that shows the wheel's spin, so parts on the visual such as brake calipers " +
                 "only steer. Empty = the visual itself spins.")]
        [SerializeField] private Transform spinVisual;

        public bool IsFront => isFront;
        public bool IsGrounded { get; private set; }
        public RaycastHit Hit => hit;

        /// <summary>Current spring length, metres: 0 = fully compressed, suspension travel = fully extended.</summary>
        public float SpringLength { get; private set; }

        /// <summary>Upward force at this wheel, newtons.</summary>
        public float SuspensionForce { get; private set; }

        /// <summary>
        /// The tyre's normal load, newtons: the suspension force plus the load braking, accelerating and cornering move
        /// onto it (<see cref="VehicleConfig.BrakingWeightTransfer"/> and the others). 0 in the air.
        /// </summary>
        public float Load { get; internal set; }

        /// <summary>How fast the tyre moves sideways over the ground, m/s: near 0 while it grips, large in a slide. 0 in the air.</summary>
        public float SidewaysSpeed { get; internal set; }

        /// <summary>How far the drive is overpowering the tyre, 0 (gripping) .. 1 (spinning hard). 0 in the air.</summary>
        public float WheelSpin { get; internal set; }

        /// <summary>Drive force the tyre is putting down this step, newtons (negative in reverse). 0 in the air.</summary>
        public float DriveForce { get; internal set; }

        /// <summary>
        /// Share of the tyre's sideways grip left after what the drive and brakes its axle is putting down take from it (the
        /// friction circle), 0..1. The handbrake's drop on the rear tyres is not included. 1 in the air.
        /// </summary>
        public float SidewaysGrip { get; internal set; } = 1f;

        /// <summary>Which side of the car the wheel is on: -1 left, 1 right, 0 on the centre line.</summary>
        public int Side { get; private set; }

        /// <summary>True while the wheel is held still by the handbrake as the car moves.</summary>
        public bool Locked { get; internal set; }

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
            Side = localMount.x > CentreLineTolerance ? 1 : localMount.x < -CentreLineTolerance ? -1 : 0;
            ResetState(config);
        }

        internal void ResetState(VehicleConfig config)
        {
            SpringLength = previousSpringLength = config.SuspensionTravel;
            SuspensionForce = 0f;
            IsGrounded = false;
            spinSpeed = 0f;
            ClearContact();
        }

        /// <summary>Forgets the tyre's contact state, for a wheel in the air or a car put somewhere new.</summary>
        internal void ClearContact()
        {
            Load = 0f;
            SidewaysSpeed = 0f;
            WheelSpin = 0f;
            DriveForce = 0f;
            SidewaysGrip = 1f;
            Locked = false;
        }

        internal Vector3 GetForward(Quaternion bodyRotation) =>
            bodyRotation * (Quaternion.Euler(0f, SteerAngle, 0f) * Vector3.forward);

        /// <summary>Casts for ground, applies the spring/damper force and resolves the surface. Returns true when grounded.</summary>
        /// <param name="reboundScale">Extra multiplier on the rebound damping, such as while settling after a landing.</param>
        internal bool UpdateSuspension(Rigidbody body, VehicleConfig config, int groundMask, SurfaceType fallbackSurface,
            float springRate, float damperRate, float reboundScale, float deltaTime)
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
                ClearContact();
                return false;
            }

            // Past full compression (a big landing) the tyre squashes into the bump stop: the length goes negative, and the
            // damper and bump stop keep working on it until the body would touch the ground.
            float length = hit.distance - config.WheelRadius;
            SpringLength = Mathf.Max(0f, length);
            float compressionSpeed = (previousSpringLength - length) / deltaTime;
            previousSpringLength = length;

            float force = (travel - SpringLength) * springRate;

            // Firmer damping as the spring extends settles the body without making bumps harsher. Fast compression (ruts,
            // rocks, landings) is damped less beyond the knee, so sharp hits are soaked up instead of kicking the body.
            if (compressionSpeed < 0f)
                force += compressionSpeed * damperRate * config.ReboundDamping * reboundScale;
            else
                force += damperRate * (Mathf.Min(compressionSpeed, config.FastBumpSpeed)
                                       + Mathf.Max(0f, compressionSpeed - config.FastBumpSpeed) * config.FastBumpDamping);

            // The bump stop: a much stiffer spring over the last of the travel and beyond, so big landings don't bottom out.
            // Like a rubber stop it gives back little of what it takes, so it pushes only lightly while the wheel extends
            // and the car doesn't bounce off it.
            float intoBumpStop = config.BumpStopTravel - length;
            if (intoBumpStop > 0f)
                force += intoBumpStop * springRate * config.BumpStopStiffness * (compressionSpeed < 0f ? BumpStopReturn : 1f);

            SuspensionForce = Mathf.Max(0f, force);
            body.AddForceAtPosition(up * SuspensionForce, origin);

            ResolveSurface(fallbackSurface);
            return true;
        }

        /// <param name="overspin">Extra spin from wheelspin, as a fraction of the rolling speed.</param>
        internal void UpdateSpin(float rollingSpeed, bool locked, float overspin, float wheelRadius, float deltaTime)
        {
            spinSpeed = locked ? 0f : rollingSpeed / wheelRadius * Mathf.Rad2Deg;
            if (overspin > 0f)
                spinSpeed += Mathf.Sign(rollingSpeed + 0.01f) * Mathf.Max(Mathf.Abs(spinSpeed), MinSpinDegreesPerSecond) * overspin;
            AdvanceSpin(deltaTime);
        }

        /// <summary>Keeps an airborne wheel spinning at its last speed.</summary>
        internal void AdvanceSpin(float deltaTime) => spinAngle = Mathf.Repeat(spinAngle + spinSpeed * deltaTime, 360f);

        internal void UpdateVisual()
        {
            if (visual == null)
                return;

            visual.localPosition = new Vector3(0f, -SpringLength, 0f);
            if (spinVisual != null)
            {
                visual.localRotation = Quaternion.Euler(0f, SteerAngle, 0f);
                spinVisual.localRotation = Quaternion.Euler(spinAngle, 0f, 0f);
            }
            else
            {
                visual.localRotation = Quaternion.Euler(0f, SteerAngle, 0f) * Quaternion.Euler(spinAngle, 0f, 0f);
            }
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
