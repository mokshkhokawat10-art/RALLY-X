using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.Cameras
{
    /// <summary>
    /// Third-person rally chase camera. It smooths angles and height rather than position, so the follow
    /// distance stays exact at any speed and a reversing car can never drive into the camera.
    /// Adds speed-based FOV and look-ahead, a subtle swing and pull-out while drifting, shake plus a spring dip on
    /// landings, shake plus a small push away from hits, a faint rumble at speed on rough ground, and a touch more
    /// vignette at high speed (<see cref="SpeedVignette"/>).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class ChaseCamera : MonoBehaviour
    {
        [SerializeField] private ChaseCameraConfig config;
        [SerializeField] private VehicleController target;

        [Tooltip("Layers the camera must not pass through. The target's own layer is always excluded.")]
        [SerializeField] private LayerMask obstacleLayers = ~0;

        private readonly CameraShake shake = new CameraShake();
        private readonly SpeedVignette speedVignette = new SpeedVignette();
        private Camera cameraComponent;
        private float yaw;
        private float pivotHeight;
        private float clearFraction = 1f;
        private float fieldOfView;
        private Vector3 lastTargetPosition;
        private bool needsSnap = true;
        private Vector3 bump;
        private Vector3 bumpVelocity;
        private float rumbleTime;

        /// <summary>The landing dip and impact kick currently applied to the camera, metres.</summary>
        public Vector3 BumpOffset => bump;

        public ChaseCameraConfig Config
        {
            get => config;
            set => config = value;
        }

        public VehicleController Target
        {
            get => target;
            set
            {
                if (target == value)
                    return;

                Unsubscribe();
                target = value;
                needsSnap = true;
                if (isActiveAndEnabled)
                    Subscribe();
            }
        }

        /// <summary>Current shake trauma (0..1).</summary>
        public float ShakeTrauma => shake.Trauma;

        /// <summary>Adds shake for other systems (boost, explosions). Amount is trauma, 0..1.</summary>
        public void AddShake(float trauma) => shake.AddTrauma(trauma);

        /// <summary>Jumps straight to the resting position behind the target on the next frame.</summary>
        public void SnapToTarget() => needsSnap = true;

        private void Awake() => cameraComponent = GetComponent<Camera>();

        private void OnEnable()
        {
            Subscribe();
            needsSnap = true;
        }

        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => speedVignette.Destroy();

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            if (target == null || config == null || deltaTime <= 0f)
                return;

            Transform car = target.transform;
            Vector3 carPosition = car.position;
            float teleportDistance = config.TeleportDistance;
            if (needsSnap || (carPosition - lastTargetPosition).sqrMagnitude > teleportDistance * teleportDistance)
                Snap(car);
            lastTargetPosition = carPosition;

            UpdateYaw(car, deltaTime);
            float heightSharpness = target.IsGrounded ? config.HeightSharpness : config.AirborneHeightSharpness;
            pivotHeight = Mathf.Lerp(pivotHeight, carPosition.y, Blend(heightSharpness, deltaTime));

            float speedFactor = Mathf.Clamp01(target.SpeedKmh / config.SpeedForMaxEffectKmh);
            fieldOfView = Mathf.Lerp(fieldOfView, TargetFieldOfView(speedFactor), Blend(config.FieldOfViewSharpness, deltaTime));
            cameraComponent.fieldOfView = fieldOfView;
            speedVignette.Update(config, transform, speedFactor, deltaTime);

            Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
            var pivot = new Vector3(carPosition.x, pivotHeight, carPosition.z);
            Vector3 aimOrigin = pivot + Vector3.up * config.LookHeight;
            Vector3 lookPoint = aimOrigin + yawRotation * (Vector3.forward * (config.LookAhead + config.LookAheadAtSpeed * speedFactor));
            float driftFactor = config.MaxDriftSwing > 0f ? Mathf.Clamp01(Mathf.Abs(target.Drift.SlipAngle * config.DriftSwing) / config.MaxDriftSwing) : 0f;
            float distance = config.Distance + config.ExtraDistanceAtSpeed * speedFactor + config.DriftDistance * driftFactor;

            // The landing dip and impact kick move the camera itself, before obstacles, so it still never enters the ground.
            UpdateBump(deltaTime);
            Vector3 desiredPosition = pivot + yawRotation * (Vector3.back * distance) + Vector3.up * config.Height + bump;

            Vector3 position = ResolveObstacles(aimOrigin, desiredPosition, deltaTime);
            Quaternion rotation = Quaternion.LookRotation(lookPoint - position);

            // Shake and rumble are applied on top of the smoothed pose and never fed back into it.
            shake.Update(config, deltaTime);
            transform.SetPositionAndRotation(position + rotation * shake.PositionOffset, rotation * shake.RotationOffset * Rumble(speedFactor, deltaTime));
        }

        /// <summary>A damped spring pulling the landing dip and impact kick back to nothing, stepped finely so long frames stay stable.</summary>
        private void UpdateBump(float deltaTime)
        {
            float omega = 2f * Mathf.PI * config.BumpFrequency;
            for (float left = deltaTime; left > 0f; left -= 0.01f)
            {
                float step = Mathf.Min(left, 0.01f);
                bumpVelocity += (-bump * (omega * omega) - bumpVelocity * (2f * config.BumpDamping * omega)) * step;
                bump += bumpVelocity * step;
            }

            float limit = Mathf.Max(config.LandingDip, config.ImpactKick) * 1.5f;
            bump = Vector3.ClampMagnitude(bump, limit);
        }

        /// <summary>
        /// A faint, fast vibration that grows with speed and the roughness of the ground under the car. Kept separate from
        /// the trauma shake, and far below what makes a moving camera uncomfortable.
        /// </summary>
        private Quaternion Rumble(float speedFactor, float deltaTime)
        {
            if (config.SpeedRumble <= 0f || !target.IsGrounded)
                return Quaternion.identity;

            // Indexed, not foreach: the wheels are an interface list, and enumerating one allocates every frame.
            float roughness = 0f;
            int grounded = 0;
            var wheels = target.Wheels;
            for (int i = 0; i < wheels.Count; i++)
            {
                var wheel = wheels[i];
                if (!wheel.IsGrounded || wheel.SurfaceType == null)
                    continue;
                roughness += wheel.SurfaceType.Roughness;
                grounded++;
            }

            roughness = grounded > 0 ? roughness / grounded : 0f;
            float amount = config.SpeedRumble * speedFactor * speedFactor * (0.35f + 0.65f * roughness);
            if (amount <= 0f)
                return Quaternion.identity;

            rumbleTime += deltaTime * config.SpeedRumbleFrequency;
            float pitch = (Mathf.PerlinNoise(rumbleTime, 3.3f) * 2f - 1f) * amount;
            float roll = (Mathf.PerlinNoise(7.7f, rumbleTime) * 2f - 1f) * amount * 0.5f;
            return Quaternion.Euler(pitch, 0f, roll);
        }

        /// <summary>Sets the spring moving so the camera travels about <paramref name="peak"/> (metres) in a direction before settling.</summary>
        private void Kick(Vector3 direction, float peak)
        {
            // A damped spring started with velocity v peaks at roughly 0.6 v / omega for the damping used here.
            float omega = 2f * Mathf.PI * config.BumpFrequency;
            bumpVelocity += direction * (peak * omega / 0.6f);
        }

        private void UpdateYaw(Transform car, float deltaTime)
        {
            if (!TryGetHeadingYaw(car, out float heading))
                return;

            // Swing partly towards the direction of travel while sliding. SlipAngle is 0 when reversing,
            // so the camera stays behind the car's heading instead of flipping around.
            float swing = Mathf.Clamp(target.Drift.SlipAngle * config.DriftSwing, -config.MaxDriftSwing, config.MaxDriftSwing);
            yaw = Mathf.LerpAngle(yaw, heading + swing, Blend(config.RotationSharpness, deltaTime));
        }

        private Vector3 ResolveObstacles(Vector3 origin, Vector3 desiredPosition, float deltaTime)
        {
            int mask = obstacleLayers & ~(1 << target.gameObject.layer);
            float free = CameraOcclusion.ClearFraction(origin, desiredPosition, config.CollisionRadius, mask);

            // Pull in at once so the view is never blocked, then ease back out so the camera doesn't pop.
            clearFraction = free < clearFraction
                ? free
                : Mathf.Lerp(clearFraction, free, Blend(config.ObstacleRecoverySharpness, deltaTime));

            Vector3 offset = desiredPosition - origin;
            float length = offset.magnitude;
            float minFraction = length > 0f ? Mathf.Min(1f, config.MinDistance / length) : 1f;
            Vector3 position = origin + offset * Mathf.Max(clearFraction, minFraction);

            return CameraOcclusion.KeepAboveGround(position, origin.y, config.GroundClearance, mask);
        }

        private void Snap(Transform car)
        {
            if (TryGetHeadingYaw(car, out float heading))
                yaw = heading;
            pivotHeight = car.position.y;
            clearFraction = 1f;
            fieldOfView = TargetFieldOfView(Mathf.Clamp01(target.SpeedKmh / config.SpeedForMaxEffectKmh));
            shake.Clear();
            bump = bumpVelocity = Vector3.zero;
            needsSnap = false;
        }

        private float TargetFieldOfView(float speedFactor) =>
            Mathf.Lerp(config.BaseFieldOfView, config.MaxFieldOfView, config.FieldOfViewBySpeed.Evaluate(speedFactor));

        private void Subscribe()
        {
            if (target == null)
                return;

            target.Impact += OnImpact;
            target.Landed += OnLanded;
        }

        private void Unsubscribe()
        {
            if (target == null)
                return;

            target.Impact -= OnImpact;
            target.Landed -= OnLanded;
        }

        private void OnImpact(float impulse, Vector3 contactPoint)
        {
            if (config == null)
                return;

            float strength = Mathf.InverseLerp(config.MinCollisionImpulse, config.MaxCollisionImpulse, impulse);
            if (strength <= 0f)
                return;

            shake.AddTrauma(config.CollisionShake * strength);

            // Pushed away from the hit, sideways and along the ground only.
            Vector3 away = target.transform.position - contactPoint;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f)
                Kick(away.normalized, config.ImpactKick * strength);
        }

        private void OnLanded(float airTime, float landingSpeed)
        {
            if (config == null || airTime < config.MinLandingAirTime)
                return;

            float strength = Mathf.Clamp01(landingSpeed / config.LandingSpeedForFullShake);
            shake.AddTrauma(config.LandingShake * strength);
            Kick(Vector3.down, config.LandingDip * strength);
        }

        private static bool TryGetHeadingYaw(Transform car, out float headingYaw)
        {
            Vector3 forward = Vector3.ProjectOnPlane(car.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
            {
                headingYaw = 0f;
                return false;
            }

            headingYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            return true;
        }

        // Frame-rate independent exponential smoothing factor.
        private static float Blend(float sharpness, float deltaTime) => 1f - Mathf.Exp(-sharpness * deltaTime);
    }
}
