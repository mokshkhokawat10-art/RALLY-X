using System;
using System.Collections.Generic;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>
    /// Arcade raycast vehicle: suspension, drive, brakes, reverse, steering, tyre grip with load transfer and wheelspin,
    /// drift assist, downforce, air control and settling after landings and knocks. Driven through <see cref="SetInput"/>
    /// by a player or AI component.
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

        // Measured accelerations are clamped to this, m/s², so a collision can't throw the load transfer or body lean about.
        private const float MaxMeasuredAcceleration = 30f;

        // In the air the landing spot is looked for this many seconds of travel ahead, up to this far below, metres.
        // Ground steeper than the slope limit (normal's y below it) is ignored.
        private const float LandingProbeLead = 0.35f;
        private const float LandingProbeDistance = 30f;
        private const float LandingSlopeLimit = 0.5f;

        // Seconds for steering in the air to bring the yaw rate to what it asks for.
        private const float AirYawResponseTime = 0.25f;

        // Hops shorter than this, seconds, don't need settling on landing.
        private const float SettleMinAirTime = 0.25f;

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

        /// <summary>
        /// The car's smoothed acceleration in its own space (x right, y up, z forward), m/s²: negative z while braking,
        /// x in corners. Drives the load transfer between the axles; body lean and the camera read it too.
        /// </summary>
        public Vector3 LocalAcceleration { get; private set; }

        /// <summary>The input the car is actually driving with this step: the driver's, or the brake and hold while input is locked.</summary>
        public VehicleInputState AppliedInput => input;

        private Rigidbody body;
        private VehicleInputState requestedInput;
        private VehicleInputState input;
        private int groundMask;
        private int frontWheelCount;
        private int rearWheelCount;
        private float frontAxle;
        private float rearAxle;
        private Vector3 previousVelocity;
        private bool hasPreviousVelocity;
        private float settleTimeLeft;

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
            AirborneTime = 0f;
            LocalAcceleration = Vector3.zero;
            hasPreviousVelocity = false;
            settleTimeLeft = 0f;
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
                float along = transform.InverseTransformPoint(wheel.transform.position).z;
                if (wheel.IsFront)
                {
                    frontAxle += along;
                    frontWheelCount++;
                }
                else
                {
                    rearAxle += along;
                    rearWheelCount++;
                }
            }

            // Axle positions along the car, for the load transfer between them. A car without both axles has none.
            frontAxle = frontWheelCount > 0 ? frontAxle / frontWheelCount : 0f;
            rearAxle = rearWheelCount > 0 ? rearAxle / rearWheelCount : 0f;
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
            MeasureAcceleration(velocity, rotation, deltaTime);

            UpdateReverseState();
            UpdateSteering(deltaTime);
            UpdateSuspension(deltaTime);
            Drift.Step(config, input.Handbrake, input.Throttle, IsReversing, IsGrounded, velocity, forward, up, deltaTime);

            if (IsGrounded)
            {
                if (AirborneTime > 0f)
                {
                    if (AirborneTime >= SettleMinAirTime)
                        settleTimeLeft = config.SettleTime;
                    Landed?.Invoke(AirborneTime, Mathf.Max(0f, -velocity.y));
                }

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
                ApplyAirControl(rotation, up, velocity);
            }

            ApplySettling(up, deltaTime);
        }

        private void Update()
        {
            foreach (var wheel in wheels)
                wheel.UpdateVisual();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0)
                return;

            // A hard knock starts the settling, so a hit to the side rocks the car instead of rolling it.
            float impulse = collision.impulse.magnitude;
            if (config != null && body != null && impulse / body.mass >= config.KnockSpeed)
                settleTimeLeft = config.SettleTime;
            Impact?.Invoke(impulse, collision.GetContact(0).point);
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

        /// <summary>The car's acceleration over the last step in its own space, smoothed over the weight transfer time.</summary>
        private void MeasureAcceleration(Vector3 velocity, Quaternion rotation, float deltaTime)
        {
            Vector3 measured = hasPreviousVelocity ? (velocity - previousVelocity) / deltaTime : Vector3.zero;
            previousVelocity = velocity;
            hasPreviousVelocity = true;

            Vector3 local = Vector3.ClampMagnitude(Quaternion.Inverse(rotation) * measured, MaxMeasuredAcceleration);
            LocalAcceleration = Vector3.Lerp(LocalAcceleration, local, 1f - Mathf.Exp(-deltaTime / config.WeightTransferTime));
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
            float mass = body.mass;
            float massPerWheel = mass / wheels.Length;
            Vector3 centerOfMass = body.worldCenterOfMass;
            ResolveLongitudinalInput(out float driveAcceleration, out float brakeInput);
            bool coasting = input.Throttle <= 0f && input.Brake <= 0f;
            float stiffness = 1f - Mathf.Pow(1f - config.LateralStiffness, deltaTime / StiffnessReferenceTimestep);
            AxleLoadScales(out float frontLoadScale, out float rearLoadScale);

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

                // The tyre's load: the spring's force, plus or minus the load braking and accelerating move between the axles.
                float load = wheel.SuspensionForce * (wheel.IsFront ? frontLoadScale : rearLoadScale);

                // Sideways: cancel sliding up to the tyre's friction limit, which scales with its load. Loose surfaces lose
                // less grip as the slide grows, so slides on them are progressive.
                float slip = Mathf.Abs(lateralSpeed) / Mathf.Max(pointVelocity.magnitude, 1f);
                float axleGrip = wheel.IsFront ? config.FrontGrip : config.RearGrip * Drift.RearGripScale;
                float slideGrip = 1f - (1f - config.GripBySlip.Evaluate(slip)) * surface.SlideFalloff;
                float friction = config.TyreFriction * axleGrip * surface.Grip * slideGrip;
                float maxLateralForce = friction * load;
                float lateralForce = Mathf.Clamp(-lateralSpeed * stiffness * massPerWheel / deltaTime, -maxLateralForce, maxLateralForce);

                // Along the tyre: drive, up to what the tyre can put down (beyond it the wheel spins), then brakes and
                // drag, which never push past standstill.
                float driveShare = wheel.IsFront
                    ? config.FrontDriveShare / frontWheelCount
                    : (1f - config.FrontDriveShare) / rearWheelCount;
                float driveForce = driveAcceleration * surface.Acceleration * mass * driveShare;
                float traction = config.DriveTraction * config.TyreFriction * surface.Grip * load;
                float wheelSpin = 0f;
                if (Mathf.Abs(driveForce) > traction)
                {
                    wheelSpin = Mathf.Clamp01(Mathf.Abs(driveForce) / Mathf.Max(traction, 1f) - 1f);
                    driveForce = Mathf.Sign(driveForce) * traction;
                }

                float brakeShare = wheel.IsFront ? config.BrakeBias / frontWheelCount : (1f - config.BrakeBias) / rearWheelCount;
                float resistance = brakeInput * config.BrakeDeceleration * surface.Grip * mass * brakeShare
                                   + massPerWheel * (surface.RollingResistance + (coasting ? config.CoastDeceleration : 0f));
                if (rearLocked)
                    resistance += config.HandbrakeDeceleration * surface.Grip * mass / rearWheelCount;
                float longitudinalForce = driveForce + OpposeMotion(rollingSpeed, resistance, massPerWheel, deltaTime);

                // Applying tyre forces nearer the centre of mass limits body roll and pitch.
                Vector3 forcePoint = hit.point + up * (Vector3.Dot(centerOfMass - hit.point, up) * config.AntiRoll);
                body.AddForceAtPosition(wheelRight * lateralForce + wheelForward * longitudinalForce, forcePoint);

                wheel.Load = load;
                wheel.SidewaysSpeed = Mathf.Abs(lateralSpeed);
                wheel.WheelSpin = wheelSpin;
                wheel.Locked = rearLocked && Mathf.Abs(rollingSpeed) > ReverseEngageSpeed;
                wheel.UpdateSpin(rollingSpeed, rearLocked, wheelSpin, config.WheelRadius, deltaTime);
            }
        }

        /// <summary>
        /// Factors on each axle's tyre load for the weight braking and accelerating move between them, beyond what the
        /// springs carry (<see cref="VehicleConfig.WeightTransfer"/>). The car's total load is unchanged.
        /// </summary>
        private void AxleLoadScales(out float front, out float rear)
        {
            front = rear = 1f;
            float wheelbase = frontAxle - rearAxle;
            if (frontWheelCount == 0 || rearWheelCount == 0 || wheelbase < 0.5f || config.WeightTransfer <= 0f)
                return;

            // Weight on the front axle at rest, from where the centre of mass sits between the axles.
            float frontShare = Mathf.Clamp((config.CenterOfMass.z - rearAxle) / wheelbase, 0.2f, 0.8f);

            // Load moved to the rear as a share of the car's weight: acceleration x centre of mass height / (g x wheelbase).
            float moved = config.WeightTransfer * LocalAcceleration.z * config.CenterOfMass.y / (Physics.gravity.magnitude * wheelbase);
            front = Mathf.Max(0f, 1f - moved / frontShare);
            rear = Mathf.Max(0f, 1f + moved / (1f - frontShare));
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

        private void ApplyAirControl(Quaternion rotation, Vector3 up, Vector3 velocity)
        {
            // A critically damped spring levels pitch and roll towards the ground the car will land on. Steering sets the
            // yaw rate up to a limit, and without steering the spin the car took off with fades. The brake dips the nose.
            float stiffness = config.AirAutoLevel;
            Vector3 angularVelocity = body.angularVelocity;
            float yawRate = Vector3.Dot(angularVelocity, up);
            Vector3 pitchRollVelocity = angularVelocity - up * yawRate;
            float yawAcceleration = Mathf.Clamp((input.Steer * config.AirYawRate - yawRate) / AirYawResponseTime, -config.AirControl, config.AirControl);

            Vector3 torque = Vector3.Cross(up, LandingUp(velocity)) * stiffness
                             - pitchRollVelocity * (2f * Mathf.Sqrt(stiffness))
                             + up * yawAcceleration
                             + rotation * Vector3.right * (input.Brake * config.AirPitchControl);
            body.AddTorque(torque, ForceMode.Acceleration);
        }

        /// <summary>
        /// The up direction to level to in the air: part way from world up to the normal of the ground below where the car
        /// is heading, so it lands square on a slope. World up when there is no usable ground below.
        /// </summary>
        private Vector3 LandingUp(Vector3 velocity)
        {
            if (config.LandingAlignment <= 0f)
                return Vector3.up;

            Vector3 lead = new Vector3(velocity.x, 0f, velocity.z) * LandingProbeLead;
            if (!Physics.Raycast(body.worldCenterOfMass + lead, Vector3.down, out RaycastHit hit, LandingProbeDistance, groundMask,
                    QueryTriggerInteraction.Ignore) || hit.normal.y < LandingSlopeLimit)
                return Vector3.up;

            return Vector3.Slerp(Vector3.up, hit.normal, config.LandingAlignment);
        }

        /// <summary>After a landing or a hard knock, damps pitch and roll for a moment so the car settles instead of rocking or rolling.</summary>
        private void ApplySettling(Vector3 up, float deltaTime)
        {
            if (settleTimeLeft <= 0f)
                return;

            settleTimeLeft -= deltaTime;
            Vector3 angularVelocity = body.angularVelocity;
            Vector3 pitchRollVelocity = angularVelocity - up * Vector3.Dot(angularVelocity, up);
            float removed = 1f - Mathf.Exp(-config.SettleDamping * deltaTime);
            body.AddTorque(-pitchRollVelocity * (removed / deltaTime), ForceMode.Acceleration);
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
