using System;
using System.Collections.Generic;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>
    /// Arcade raycast vehicle: suspension, drive, brakes, reverse, steering, tyre grip, drift assist,
    /// downforce and air control. Driven through <see cref="SetInput"/> by a player or AI component.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class VehicleController : MonoBehaviour
    {
        // Lateral stiffness is authored per step at this timestep and rescaled for others.
        private const float StiffnessReferenceTimestep = 0.02f;

        // Below this forward speed (m/s) the brake input engages reverse instead of braking.
        private const float ReverseEngageSpeed = 0.5f;

        // Degrees past the max drift angle over which anti-spin ramps up to full strength.
        private const float AntiSpinRampDegrees = 10f;

        // A car with locked input is slowed with this much brake (0..1), then held by the handbrake below the hold speed (m/s).
        private const float LockedBrake = 0.35f;
        private const float LockedHoldSpeed = 2f;

        [SerializeField] private VehicleConfig config;
        [SerializeField] private VehicleWheel[] wheels = Array.Empty<VehicleWheel>();

        [Tooltip("Surface used where the ground has no SurfaceMarker.")]
        [SerializeField] private SurfaceType fallbackSurface;

        [Tooltip("Layers the wheels can drive on. The vehicle's own layer is always excluded.")]
        [SerializeField] private LayerMask groundLayers = ~0;

        [Tooltip("Height added to the current position when the vehicle is reset, metres.")]
        [SerializeField, Min(0f)] private float resetLift = 1f;

        /// <summary>Raised on collision with the impulse magnitude (N·s) and first contact point.</summary>
        public event Action<float, Vector3> Impact;

        /// <summary>Raised when a wheel touches down after being airborne, with the air time (s) and downward speed (m/s).</summary>
        public event Action<float, float> Landed;

        public VehicleConfig Config => config;
        public Rigidbody Body => body;
        public VehicleDrift Drift { get; } = new VehicleDrift();
        public IReadOnlyList<VehicleWheel> Wheels => wheels;

        /// <summary>The input most recently requested by the driver (player or AI), even while input is locked.</summary>
        public VehicleInputState CurrentInput => requestedInput;

        /// <summary>
        /// While locked (race countdown, after the finish) the driver's input is ignored: the car brakes gently
        /// to a stop in a straight line and is then held by the handbrake.
        /// </summary>
        public bool InputLocked { get; set; }

        /// <summary>Speed along the vehicle's heading, m/s. Negative when reversing.</summary>
        public float ForwardSpeed { get; private set; }

        public float SpeedKmh => body.linearVelocity.magnitude * 3.6f;
        public float SteerAngle { get; private set; }

        /// <summary>True while reverse is engaged; stays set until throttle is applied or the car rolls forward.</summary>
        public bool IsReversing { get; private set; }

        public int GroundedWheelCount { get; private set; }
        public bool IsGrounded => GroundedWheelCount > 0;

        /// <summary>Seconds since every wheel left the ground (0 while grounded).</summary>
        public float AirborneTime { get; private set; }

        private Rigidbody body;
        private VehicleInputState requestedInput;
        private VehicleInputState input;
        private int groundMask;
        private int frontWheelCount;
        private int rearWheelCount;

        public void SetInput(in VehicleInputState state) => requestedInput = state;

        /// <summary>
        /// Drives with another handling config from the next physics step, for example a copy with upgrades applied
        /// (<see cref="VehicleConfig.CreateTuned"/>). The wheels keep their size and suspension travel.
        /// </summary>
        public void SetConfig(VehicleConfig newConfig)
        {
            if (newConfig == null || newConfig == config)
                return;

#if UNITY_EDITOR
            if (isActiveAndEnabled && config != null)
                config.Changed -= ApplyBodySettings;
#endif
            config = newConfig;
#if UNITY_EDITOR
            if (isActiveAndEnabled)
                config.Changed += ApplyBodySettings;
#endif
            if (body != null)
                ApplyBodySettings();
        }

        /// <summary>Puts the vehicle back on its wheels, facing its current heading, with no velocity.</summary>
        public void ResetVehicle() => PlaceAt(body.position + Vector3.up * resetLift, UprightRotation(body.rotation));

        /// <summary>Moves the vehicle to a pose at rest, as if it had just been placed there (e.g. a grid slot).</summary>
        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);

            SteerAngle = 0f;
            IsReversing = false;
            Drift.Reset();
            foreach (var wheel in wheels)
                wheel.ResetState(config);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (!IsSetUpCorrectly())
            {
                enabled = false;
                return;
            }

            groundMask = groundLayers & ~(1 << gameObject.layer);
            foreach (var wheel in wheels)
            {
                wheel.Initialize(body, config);
                if (wheel.IsFront)
                    frontWheelCount++;
                else
                    rearWheelCount++;
            }

            ApplyBodySettings();
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            if (config != null)
                config.Changed += ApplyBodySettings;
        }

        private void OnDisable()
        {
            if (config != null)
                config.Changed -= ApplyBodySettings;
        }
#endif

        private void FixedUpdate()
        {
            input = InputLocked ? LockedInput() : requestedInput;
            float deltaTime = Time.fixedDeltaTime;
            Quaternion rotation = body.rotation;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            Vector3 velocity = body.linearVelocity;
            ForwardSpeed = Vector3.Dot(velocity, forward);

            UpdateReverseState();
            UpdateSteering(deltaTime);
            UpdateSuspension(deltaTime);
            Drift.Step(config, input.Handbrake, IsReversing, IsGrounded, velocity, forward, up, deltaTime);

            if (IsGrounded)
            {
                if (AirborneTime > 0f)
                    Landed?.Invoke(AirborneTime, Mathf.Max(0f, -velocity.y));
                AirborneTime = 0f;
                ApplyTyreForces(rotation, up, deltaTime);
                ApplyDriftAssist(up);
                ApplyAntiSpin(up, deltaTime);
                ApplyDownforce(up, velocity);
            }
            else
            {
                AirborneTime += deltaTime;
                foreach (var wheel in wheels)
                    wheel.AdvanceSpin(deltaTime);
                ApplyAirControl(up);
            }
        }

        private void Update()
        {
            foreach (var wheel in wheels)
                wheel.UpdateVisual();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Impact == null || collision.contactCount == 0)
                return;

            Impact.Invoke(collision.impulse.magnitude, collision.GetContact(0).point);
        }

        private bool IsSetUpCorrectly()
        {
            if (config == null)
            {
                Debug.LogError($"{name}: VehicleController has no VehicleConfig assigned.", this);
                return false;
            }

            bool hasMissingWheel = wheels.Length == 0;
            foreach (var wheel in wheels)
                hasMissingWheel |= wheel == null;

            if (hasMissingWheel)
            {
                Debug.LogError($"{name}: VehicleController needs at least one wheel and no empty wheel slots.", this);
                return false;
            }

            return true;
        }

        private void ApplyBodySettings()
        {
            body.mass = config.Mass;
            body.centerOfMass = config.CenterOfMass;
            body.angularDamping = config.AngularDamping;
            body.linearDamping = 0f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void UpdateReverseState()
        {
            // Sticky, so a car coasting backwards after reversing isn't mistaken for one spinning out.
            if (input.Throttle > 0f || ForwardSpeed > ReverseEngageSpeed)
                IsReversing = false;
            else if (input.Brake > 0f)
                IsReversing = true;
        }

        private void UpdateSteering(float deltaTime)
        {
            float speedFactor = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / config.MaxSpeed);
            float target = input.Steer * config.MaxSteerAngle * config.SteerBySpeed.Evaluate(speedFactor);
            SteerAngle = Mathf.MoveTowards(SteerAngle, target, config.SteerSpeed * deltaTime);

            foreach (var wheel in wheels)
            {
                if (wheel.IsFront)
                    wheel.SteerAngle = SteerAngle;
            }
        }

        private void UpdateSuspension(float deltaTime)
        {
            // Rates derived from frequency and damping ratio keep the ride identical when mass changes.
            float sprungMassPerWheel = body.mass / wheels.Length;
            float angularFrequency = 2f * Mathf.PI * config.SpringFrequency;
            float springRate = sprungMassPerWheel * angularFrequency * angularFrequency;
            float damperRate = 2f * config.DampingRatio * Mathf.Sqrt(springRate * sprungMassPerWheel);

            int grounded = 0;
            foreach (var wheel in wheels)
            {
                if (wheel.UpdateSuspension(body, config, groundMask, fallbackSurface, springRate, damperRate, deltaTime))
                    grounded++;
            }

            GroundedWheelCount = grounded;
        }

        private void ApplyTyreForces(Quaternion rotation, Vector3 up, float deltaTime)
        {
            float massPerWheel = body.mass / wheels.Length;
            Vector3 centerOfMass = body.worldCenterOfMass;
            ResolveLongitudinalInput(out float driveAcceleration, out float brakeInput);
            bool coasting = input.Throttle <= 0f && input.Brake <= 0f;
            float stiffness = 1f - Mathf.Pow(1f - config.LateralStiffness, deltaTime / StiffnessReferenceTimestep);

            foreach (var wheel in wheels)
            {
                if (!wheel.IsGrounded)
                {
                    wheel.AdvanceSpin(deltaTime);
                    continue;
                }

                RaycastHit hit = wheel.Hit;
                Vector3 wheelForward = Vector3.ProjectOnPlane(wheel.GetForward(rotation), hit.normal).normalized;
                Vector3 wheelRight = Vector3.Cross(hit.normal, wheelForward);
                Vector3 pointVelocity = body.GetPointVelocity(hit.point);
                float lateralSpeed = Vector3.Dot(pointVelocity, wheelRight);
                float rollingSpeed = Vector3.Dot(pointVelocity, wheelForward);
                SurfaceProperties surface = wheel.Surface;
                bool rearLocked = !wheel.IsFront && input.Handbrake;

                // Sideways: cancel sliding up to the tyre's friction limit, which scales with the wheel's load.
                float slip = Mathf.Abs(lateralSpeed) / Mathf.Max(pointVelocity.magnitude, 1f);
                float axleGrip = wheel.IsFront ? config.FrontGrip : config.RearGrip * Drift.RearGripScale;
                float friction = config.TyreFriction * axleGrip * surface.Grip * config.GripBySlip.Evaluate(slip);
                float maxLateralForce = friction * wheel.SuspensionForce;
                float lateralForce = Mathf.Clamp(-lateralSpeed * stiffness * massPerWheel / deltaTime, -maxLateralForce, maxLateralForce);

                // Along the tyre: drive, then brakes and drag, which never push past standstill.
                float driveShare = wheel.IsFront
                    ? config.FrontDriveShare / frontWheelCount
                    : (1f - config.FrontDriveShare) / rearWheelCount;
                float longitudinalForce = driveAcceleration * surface.Acceleration * body.mass * driveShare;

                float resistance = massPerWheel * (brakeInput * config.BrakeDeceleration * surface.Grip
                                                   + surface.RollingResistance
                                                   + (coasting ? config.CoastDeceleration : 0f));
                if (rearLocked)
                    resistance += config.HandbrakeDeceleration * surface.Grip * body.mass / rearWheelCount;
                longitudinalForce += OpposeMotion(rollingSpeed, resistance, massPerWheel, deltaTime);

                // Applying tyre forces nearer the centre of mass limits body roll and pitch.
                Vector3 forcePoint = hit.point + up * (Vector3.Dot(centerOfMass - hit.point, up) * config.AntiRoll);
                body.AddForceAtPosition(wheelRight * lateralForce + wheelForward * longitudinalForce, forcePoint);

                wheel.UpdateSpin(rollingSpeed, rearLocked, config.WheelRadius, deltaTime);
            }
        }

        private void ResolveLongitudinalInput(out float driveAcceleration, out float brakeInput)
        {
            float drive;
            if (ForwardSpeed > ReverseEngageSpeed)
            {
                drive = input.Throttle;
                brakeInput = input.Brake;
            }
            else if (ForwardSpeed < -ReverseEngageSpeed)
            {
                drive = -input.Brake;
                brakeInput = input.Throttle;
            }
            else
            {
                drive = input.Throttle - input.Brake;
                brakeInput = 0f;
            }

            float topSpeed = config.MaxSpeed * AverageGroundedTopSpeedMultiplier();
            driveAcceleration = 0f;
            if (drive > 0f && ForwardSpeed < topSpeed)
                driveAcceleration = drive * config.Acceleration * config.AccelerationCurve.Evaluate(Mathf.Clamp01(ForwardSpeed / topSpeed));
            else if (drive < 0f && -ForwardSpeed < config.MaxReverseSpeed)
                driveAcceleration = drive * config.ReverseAcceleration;
        }

        private float AverageGroundedTopSpeedMultiplier()
        {
            float sum = 0f;
            int count = 0;
            foreach (var wheel in wheels)
            {
                if (!wheel.IsGrounded)
                    continue;
                sum += wheel.Surface.TopSpeed;
                count++;
            }

            return count > 0 ? sum / count : 1f;
        }

        private void ApplyDriftAssist(Vector3 up)
        {
            if (!config.DriftEnabled)
                return;

            float speed = Drift.PlanarSpeed;
            bool initiating = input.Handbrake && speed >= config.MinDriftSpeed;
            if (!Drift.IsDrifting && !initiating)
                return;

            float speedFactor = Mathf.Clamp01(speed / (2f * config.MinDriftSpeed));
            float yawAcceleration = input.Steer * config.DriftMultiplier * speedFactor;

            // Yawing against the slip sign deepens the slide; fade that out towards the limit so the assist can't spin the car.
            float slip = Drift.SlipAngle;
            if (yawAcceleration * slip < 0f)
                yawAcceleration *= Mathf.Clamp01(1f - Mathf.Abs(slip) / config.MaxDriftAngle);

            body.AddTorque(up * yawAcceleration, ForceMode.Acceleration);
        }

        private void ApplyAntiSpin(Vector3 up, float deltaTime)
        {
            float slip = Drift.SlipAngle;
            float excess = Mathf.Abs(slip) - (config.MaxDriftAngle - AntiSpinRampDegrees);
            if (excess <= 0f || Drift.PlanarSpeed < config.MinDriftSpeed)
                return;

            // Yawing in the slip's direction turns the nose back towards the direction of travel.
            float ramp = Mathf.Clamp01(excess / AntiSpinRampDegrees);
            float towardsTravel = Mathf.Sign(slip);
            float yawAcceleration = towardsTravel * config.AntiSpin * ramp;

            // Correcting the angle alone can't stop a car that is already rotating fast, so also brake the spin itself.
            float yawRate = Vector3.Dot(body.angularVelocity, up);
            if (yawRate * towardsTravel < 0f)
            {
                float damping = 1f - Mathf.Pow(1f - config.AntiSpinDamping, deltaTime / StiffnessReferenceTimestep);
                yawAcceleration -= yawRate * damping * ramp / deltaTime;
            }

            body.AddTorque(up * yawAcceleration, ForceMode.Acceleration);
        }

        private void ApplyDownforce(Vector3 up, Vector3 velocity)
        {
            float speedRatio = velocity.magnitude / config.MaxSpeed;
            float force = config.Downforce * Physics.gravity.magnitude * body.mass * speedRatio * speedRatio;
            body.AddForce(-up * force);
        }

        private void ApplyAirControl(Vector3 up)
        {
            // A critically damped spring levels pitch and roll; steering controls yaw.
            float stiffness = config.AirAutoLevel;
            Vector3 angularVelocity = body.angularVelocity;
            Vector3 pitchRollVelocity = angularVelocity - up * Vector3.Dot(angularVelocity, up);

            Vector3 torque = Vector3.Cross(up, Vector3.up) * stiffness
                             - pitchRollVelocity * (2f * Mathf.Sqrt(stiffness))
                             + up * (input.Steer * config.AirControl);
            body.AddTorque(torque, ForceMode.Acceleration);
        }

        private VehicleInputState LockedInput()
        {
            // Brake is only used while clearly moving: near standstill it would engage reverse.
            // Rolling backwards, the throttle is what slows the car.
            if (ForwardSpeed > LockedHoldSpeed)
                return new VehicleInputState(0f, LockedBrake, 0f, false);
            if (ForwardSpeed < -LockedHoldSpeed)
                return new VehicleInputState(LockedBrake, 0f, 0f, false);
            return new VehicleInputState(0f, 0f, 0f, true);
        }

        private static float OpposeMotion(float speed, float force, float mass, float deltaTime) =>
            -Mathf.Sign(speed) * Mathf.Min(force, Mathf.Abs(speed) * mass / deltaTime);

        private static Quaternion UprightRotation(Quaternion rotation)
        {
            Vector3 heading = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            if (heading.sqrMagnitude < 1e-4f)
                heading = Vector3.ProjectOnPlane(rotation * Vector3.up, Vector3.up);
            if (heading.sqrMagnitude < 1e-4f)
                heading = Vector3.forward;

            return Quaternion.LookRotation(heading.normalized, Vector3.up);
        }
    }
}
