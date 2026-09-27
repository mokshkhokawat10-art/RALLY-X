using System;
using System.Collections.Generic;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>
    /// Arcade raycast vehicle: suspension, drive through front, rear or all-wheel drive with differentials, brakes,
    /// reverse, steering, tyre grip with load transfer, load sensitivity, wheelspin and a friction circle, drift assist,
    /// high-speed stability, downforce, air control, and settling after landings and knocks. Driven through
    /// <see cref="SetInput"/> by a player or AI component.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class VehicleController : MonoBehaviour
    {
        // Lateral stiffness is authored per step at this timestep and rescaled for others.
        private const float StiffnessReferenceTimestep = 0.02f;

        // Axles closer together than this, metres, are treated as one: no load transfer or yaw from steering geometry.
        private const float MinWheelbase = 0.5f;

        // The friction circle: drive or braking beyond a tyre's traction counts up to this multiple of it, and a tyre never
        // keeps less than this share of its sideways grip.
        private const float MaxGripUse = 1.25f;
        private const float MinSidewaysGrip = 0.25f;

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
        private float wheelbase;
        private float frontHalfTrack;
        private float rearHalfTrack;
        private TyreContact[] contacts = Array.Empty<TyreContact>();
        private Vector3 previousVelocity;
        private bool hasPreviousVelocity;
        private float settleTimeLeft;
        private float knockTimeLeft;

        private bool HasBothAxles => frontWheelCount > 0 && rearWheelCount > 0 && wheelbase >= MinWheelbase;

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
            knockTimeLeft = 0f;
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
            contacts = new TyreContact[wheels.Length];
            foreach (var wheel in wheels)
            {
                wheel.Initialize(body, config);
                Vector3 mount = transform.InverseTransformPoint(wheel.transform.position);
                if (wheel.IsFront)
                {
                    frontAxle += mount.z;
                    frontHalfTrack += Mathf.Abs(mount.x);
                    frontWheelCount++;
                }
                else
                {
                    rearAxle += mount.z;
                    rearHalfTrack += Mathf.Abs(mount.x);
                    rearWheelCount++;
                }
            }

            // Axle positions along the car and half track widths, for the load transfer between and across the axles.
            // A car without both axles has none.
            frontAxle = frontWheelCount > 0 ? frontAxle / frontWheelCount : 0f;
            rearAxle = rearWheelCount > 0 ? rearAxle / rearWheelCount : 0f;
            frontHalfTrack = frontWheelCount > 0 ? frontHalfTrack / frontWheelCount : 0f;
            rearHalfTrack = rearWheelCount > 0 ? rearHalfTrack / rearWheelCount : 0f;
            wheelbase = frontAxle - rearAxle;
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
                ApplyStabilityAssist(up, deltaTime);
                ApplyDownforce(up, forward, velocity);
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

            // A hard knock starts the settling, so a hit to the side rocks the car instead of rolling it or spinning it round.
            float impulse = collision.impulse.magnitude;
            if (config != null && body != null && impulse / body.mass >= config.KnockSpeed)
                settleTimeLeft = knockTimeLeft = config.SettleTime;
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

            // The wheels come back to the centre, and swing across to countersteer, faster than they turn in.
            bool returning = Mathf.Abs(target) < Mathf.Abs(SteerAngle) || target * SteerAngle < 0f;
            float rate = config.SteerSpeed * (returning ? config.SteerReturnRate : 1f);
            SteerAngle = Mathf.MoveTowards(SteerAngle, target, rate * deltaTime);

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

            // While settling after a landing or a knock the springs rebound more slowly, so the body comes back up once
            // instead of bouncing.
            float reboundScale = settleTimeLeft > 0f ? config.LandingReboundDamping : 1f;

            int grounded = 0;
            foreach (var wheel in wheels)
            {
                if (wheel.UpdateSuspension(body, config, groundMask, fallbackSurface, springRate, damperRate, reboundScale, deltaTime))
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
            float driveMagnitude = Mathf.Abs(driveAcceleration) * mass;

            MeasureContacts(rotation);
            ResolveTraction(driveMagnitude);
            if (driveMagnitude > 0f)
                SplitDrive();

            // The drive the differentials send each tyre and its brake force, then the friction circle for each axle.
            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                ref TyreContact contact = ref contacts[i];
                float brakeShare = wheel.IsFront ? config.BrakeBias / frontWheelCount : (1f - config.BrakeBias) / rearWheelCount;
                contact.Drive = Mathf.Sign(driveAcceleration) * contact.DriveShare * driveMagnitude * wheel.Surface.Acceleration;
                contact.Brake = brakeInput * config.BrakeDeceleration * wheel.Surface.Grip * mass * brakeShare;
            }

            SidewaysGripLeft(out float frontGripLeft, out float rearGripLeft);

            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                if (!wheel.IsGrounded)
                {
                    wheel.AdvanceSpin(deltaTime);
                    continue;
                }

                ref TyreContact contact = ref contacts[i];
                SurfaceProperties surface = wheel.Surface;
                bool rearLocked = !wheel.IsFront && input.Handbrake;

                // Along the tyre: the drive the differentials send it, up to what it can put down (beyond it the wheel
                // spins), then brakes and drag, which never push past standstill.
                float driveForce = contact.Drive;
                float driveUse = Mathf.Abs(driveForce) / Mathf.Max(contact.Traction, 1f);
                float wheelSpin = 0f;
                if (driveUse > 1f)
                {
                    wheelSpin = Mathf.Clamp01(driveUse - 1f);
                    driveForce = Mathf.Sign(driveForce) * contact.Traction;
                }

                float resistance = contact.Brake + massPerWheel * (surface.RollingResistance + (coasting ? config.CoastDeceleration : 0f));
                if (rearLocked)
                    resistance += config.HandbrakeDeceleration * surface.Grip * mass / rearWheelCount;
                float longitudinalForce = driveForce + OpposeMotion(contact.RollingSpeed, resistance, massPerWheel, deltaTime);
                float sidewaysGrip = wheel.IsFront ? frontGripLeft : rearGripLeft;

                // Sideways: cancel sliding up to the tyre's friction limit, which grows with its load. Loose surfaces lose
                // less grip as the slide grows, so slides on them are progressive.
                float slip = Mathf.Abs(contact.LateralSpeed) / Mathf.Max(contact.PointSpeed, 1f);
                float axleGrip = wheel.IsFront ? config.FrontGrip : config.RearGrip * Drift.RearGripScale;
                float slideGrip = 1f - (1f - config.GripBySlip.Evaluate(slip)) * surface.SlideFalloff;
                float friction = config.TyreFriction * axleGrip * surface.Grip * slideGrip * sidewaysGrip;
                float maxLateralForce = friction * contact.GripLoad;
                float lateralForce = Mathf.Clamp(-contact.LateralSpeed * stiffness * massPerWheel / deltaTime, -maxLateralForce, maxLateralForce);

                // Applying tyre forces nearer the centre of mass limits body roll and pitch.
                Vector3 hitPoint = wheel.Hit.point;
                Vector3 forcePoint = hitPoint + up * (Vector3.Dot(centerOfMass - hitPoint, up) * config.AntiRoll);
                body.AddForceAtPosition(contact.Right * lateralForce + contact.Forward * longitudinalForce, forcePoint);

                wheel.Load = contact.Load;
                wheel.SidewaysSpeed = Mathf.Abs(contact.LateralSpeed);
                wheel.WheelSpin = wheelSpin;
                wheel.DriveForce = driveForce;
                wheel.SidewaysGrip = sidewaysGrip;
                wheel.Locked = rearLocked && Mathf.Abs(contact.RollingSpeed) > ReverseEngageSpeed;
                wheel.UpdateSpin(contact.RollingSpeed, rearLocked, wheelSpin, config.WheelRadius, deltaTime);
            }
        }

        /// <summary>
        /// Each grounded tyre's contact frame, its speed over the ground and its load: the spring's force plus the load
        /// braking, accelerating and cornering move onto it beyond what the springs carry.
        /// </summary>
        private void MeasureContacts(Quaternion rotation)
        {
            float frontShare = HasBothAxles ? StaticFrontShare() : 0.5f;
            AxleLoadScales(frontShare, out float frontScale, out float rearScale);

            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                ref TyreContact contact = ref contacts[i];
                if (!wheel.IsGrounded)
                {
                    contact = default;
                    continue;
                }

                RaycastHit hit = wheel.Hit;
                contact.Forward = Vector3.ProjectOnPlane(wheel.GetForward(rotation), hit.normal).normalized;
                contact.Right = Vector3.Cross(hit.normal, contact.Forward);
                Vector3 pointVelocity = body.GetPointVelocity(hit.point);
                contact.LateralSpeed = Vector3.Dot(pointVelocity, contact.Right);
                contact.RollingSpeed = Vector3.Dot(pointVelocity, contact.Forward);
                contact.PointSpeed = pointVelocity.magnitude;
                contact.Load = wheel.SuspensionForce * (wheel.IsFront ? frontScale : rearScale);
            }

            // Cornering moves load from the inside tyres to the outside ones; the outside tyres gain what the inside ones lose.
            float frontMoved = SideLoadTransfer(true);
            float rearMoved = SideLoadTransfer(false);
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i].IsGrounded)
                    contacts[i].Load -= wheels[i].Side * (wheels[i].IsFront ? frontMoved : rearMoved);
            }
        }

        /// <summary>
        /// The load each tyre's grip works from and the drive it can put down before spinning, and the drivetrain's split
        /// of the drive before the differentials act: an airborne wheel can put down none.
        /// </summary>
        private void ResolveTraction(float driveMagnitude)
        {
            float loadLimit = body.mass / wheels.Length * Physics.gravity.magnitude * config.GripLoadLimit;
            AxleMeanLoads(out float frontMean, out float rearMean);
            float frontDriveShare = config.FrontDriveShare;

            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                ref TyreContact contact = ref contacts[i];
                contact.DriveShare = wheel.IsFront ? frontDriveShare / frontWheelCount : (1f - frontDriveShare) / rearWheelCount;
                if (!wheel.IsGrounded)
                {
                    contact.DriveCap = 0f;
                    continue;
                }

                SurfaceProperties surface = wheel.Surface;
                contact.GripLoad = Mathf.Min(SensitiveLoad(contact.Load, wheel.IsFront ? frontMean : rearMean), loadLimit);
                contact.Traction = config.DriveTraction * config.TyreFriction * surface.Grip * surface.Traction * contact.GripLoad;
                contact.DriveCap = driveMagnitude > 0f ? contact.Traction / (driveMagnitude * Mathf.Max(surface.Acceleration, 0.01f)) : 0f;
            }
        }

        /// <summary>
        /// The differentials: a limited-slip differential passes part of the drive a wheel can't put down to the other wheel
        /// on its axle, and on all-wheel drive the centre differential passes part of what an axle can't put down to the
        /// other axle.
        /// </summary>
        private void SplitDrive()
        {
            float limitedSlip = config.LimitedSlip;
            if (limitedSlip > 0f)
            {
                PassDrive(true, true, limitedSlip);
                PassDrive(false, false, limitedSlip);
            }

            float coupling = config.CenterCoupling;
            if (coupling > 0f)
            {
                PassDrive(true, false, coupling);
                PassDrive(false, true, coupling);
            }
        }

        /// <summary>
        /// Moves up to <paramref name="fraction"/> of the drive the wheels on one axle can't put down to the wheels on
        /// another (or the same) axle that have traction to spare, in shares of the car's drive.
        /// </summary>
        private void PassDrive(bool fromFront, bool toFront, float fraction)
        {
            float surplus = 0f;
            float spare = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                ref TyreContact contact = ref contacts[i];
                if (wheels[i].IsFront == fromFront)
                    surplus += Mathf.Max(0f, contact.DriveShare - contact.DriveCap);
                if (wheels[i].IsFront == toFront)
                    spare += Mathf.Max(0f, contact.DriveCap - contact.DriveShare);
            }

            float moved = Mathf.Min(surplus * fraction, spare);
            if (moved <= 0f)
                return;

            float taken = moved / surplus;
            float given = moved / spare;
            for (int i = 0; i < wheels.Length; i++)
            {
                ref TyreContact contact = ref contacts[i];
                if (wheels[i].IsFront == fromFront)
                {
                    float over = contact.DriveShare - contact.DriveCap;
                    if (over > 0f)
                    {
                        contact.DriveShare -= over * taken;
                        continue;
                    }
                }

                if (wheels[i].IsFront == toFront)
                    contact.DriveShare += Mathf.Max(0f, contact.DriveCap - contact.DriveShare) * given;
            }
        }

        /// <summary>
        /// The friction circle, per axle: the drive and braking an axle puts down, for the traction of its grounded tyres,
        /// take some of its sideways grip. Power swings the tail of a rear-driven car and pushes the nose of a front-driven
        /// one wide, and rear-biased brakes let the tail step out. Worked out for the whole axle, so how the load or drive
        /// is spread across it doesn't change the axle's grip.
        /// </summary>
        private void SidewaysGripLeft(out float front, out float rear)
        {
            float frontDrive = 0f;
            float rearDrive = 0f;
            float frontBrake = 0f;
            float rearBrake = 0f;
            float frontTraction = 0f;
            float rearTraction = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (!wheels[i].IsGrounded)
                    continue;

                ref TyreContact contact = ref contacts[i];
                if (wheels[i].IsFront)
                {
                    frontDrive += Mathf.Abs(contact.Drive);
                    frontBrake += contact.Brake;
                    frontTraction += contact.Traction;
                }
                else
                {
                    rearDrive += Mathf.Abs(contact.Drive);
                    rearBrake += contact.Brake;
                    rearTraction += contact.Traction;
                }
            }

            front = GripLeft(frontDrive, frontBrake, frontTraction);
            rear = GripLeft(rearDrive, rearBrake, rearTraction);
        }

        private float GripLeft(float drive, float brake, float traction)
        {
            traction = Mathf.Max(traction, 1f);
            float driveUse = Mathf.Min(drive / traction, MaxGripUse);
            float brakeUse = Mathf.Min(brake / traction, MaxGripUse);
            return Mathf.Max(MinSidewaysGrip, 1f - config.DriveGripLoss * driveUse * driveUse - config.BrakeGripLoss * brakeUse * brakeUse);
        }

        /// <summary>Mean load of the grounded tyres on each axle, newtons (0 when none is on the ground).</summary>
        private void AxleMeanLoads(out float front, out float rear)
        {
            front = rear = 0f;
            int frontCount = 0;
            int rearCount = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (!wheels[i].IsGrounded)
                    continue;

                if (wheels[i].IsFront)
                {
                    front += contacts[i].Load;
                    frontCount++;
                }
                else
                {
                    rear += contacts[i].Load;
                    rearCount++;
                }
            }

            front = frontCount > 0 ? front / frontCount : 0f;
            rear = rearCount > 0 ? rear / rearCount : 0f;
        }

        /// <summary>
        /// The load a tyre's grip works from: with load sensitivity, a tyre carrying more than its axle's mean grips less
        /// than in proportion and one carrying less grips more, so the load moved across an axle costs it grip while the
        /// load on the whole axle (downforce, braking) still counts in full.
        /// </summary>
        private float SensitiveLoad(float load, float axleMean)
        {
            float sensitivity = config.LoadSensitivity;
            if (sensitivity <= 0f || axleMean <= 0f || load <= 0f)
                return load;
            return axleMean * Mathf.Pow(load / axleMean, 1f - sensitivity);
        }

        /// <summary>Weight on the front axle at rest, from where the centre of mass sits between the axles.</summary>
        private float StaticFrontShare() => Mathf.Clamp((config.CenterOfMass.z - rearAxle) / wheelbase, 0.2f, 0.8f);

        /// <summary>
        /// Factors on each axle's tyre load for the weight braking and accelerating move between them, beyond what the
        /// springs carry (<see cref="VehicleConfig.BrakingWeightTransfer"/>, <see cref="VehicleConfig.AccelerationWeightTransfer"/>).
        /// The car's total load is unchanged.
        /// </summary>
        private void AxleLoadScales(float frontShare, out float front, out float rear)
        {
            front = rear = 1f;
            float acceleration = LocalAcceleration.z;
            float transfer = acceleration >= 0f ? config.AccelerationWeightTransfer : config.BrakingWeightTransfer;
            if (!HasBothAxles || transfer <= 0f)
                return;

            // Load moved to the rear as a share of the car's weight: acceleration x centre of mass height / (g x wheelbase).
            float moved = transfer * acceleration * config.CenterOfMass.y / (Physics.gravity.magnitude * wheelbase);
            front = Mathf.Max(0f, 1f - moved / frontShare);
            rear = Mathf.Max(0f, 1f + moved / (1f - frontShare));
        }

        /// <summary>
        /// Load cornering moves from each inside tyre on an axle to the outside one, beyond what the springs carry
        /// (<see cref="VehicleConfig.LateralWeightTransfer"/>), newtons, with the axle's share set by
        /// <see cref="VehicleConfig.FrontRollShare"/>. Positive moves load onto the left tyres (a right-hand corner). No
        /// more than the least loaded tyre carries is moved, and none unless a tyre on each side is on the ground.
        /// </summary>
        private float SideLoadTransfer(bool front)
        {
            float halfTrack = front ? frontHalfTrack : rearHalfTrack;
            int axleWheels = front ? frontWheelCount : rearWheelCount;
            if (!HasBothAxles || config.LateralWeightTransfer <= 0f || halfTrack < 0.1f)
                return 0f;

            bool left = false;
            bool right = false;
            float least = float.MaxValue;
            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                if (wheel.IsFront != front || !wheel.IsGrounded || wheel.Side == 0)
                    continue;
                left |= wheel.Side < 0;
                right |= wheel.Side > 0;
                least = Mathf.Min(least, contacts[i].Load);
            }

            if (!left || !right)
                return 0f;

            // The whole car moves mass x sideways acceleration x centre of mass height / track of load to its outside tyres;
            // this axle takes its roll share of that, spread over its tyres on each side.
            float rollShare = front ? config.FrontRollShare : 1f - config.FrontRollShare;
            float moved = config.LateralWeightTransfer * body.mass * LocalAcceleration.x * config.CenterOfMass.y * rollShare
                          / (2f * halfTrack) / Mathf.Max(1, axleWheels / 2);
            return Mathf.Clamp(moved, -least, least);
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
            // Yawing with it (steering against the slide) catches the drift, with its own strength.
            float slip = Drift.SlipAngle;
            if (yawAcceleration * slip < 0f)
                yawAcceleration *= Mathf.Clamp01(1f - Mathf.Abs(slip) / config.MaxDriftAngle);
            else
                yawAcceleration *= config.CounterSteerAssist;

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

        /// <summary>
        /// At speed, removes part of the yaw beyond what the steering asks for, so crests, bumps and lifts don't turn the
        /// car on their own. Slides the driver starts (handbrake, drifts) are left alone.
        /// </summary>
        private void ApplyStabilityAssist(Vector3 up, float deltaTime)
        {
            if (config.StabilityAssist <= 0f || input.Handbrake || Drift.IsDrifting || IsReversing)
                return;

            float strength = Mathf.InverseLerp(config.StabilitySpeed, 1f, ForwardSpeed / config.MaxSpeed);
            if (strength <= 0f)
                return;

            // Only yaw faster than the steering asks for, or against it, is corrected: an understeering car is left alone.
            float yawRate = Vector3.Dot(body.angularVelocity, up);
            float excess = yawRate - SteeredYawRate();
            if (excess * yawRate <= 0f)
                return;

            float removed = 1f - Mathf.Pow(1f - config.StabilityAssist * strength, deltaTime / StiffnessReferenceTimestep);
            body.AddTorque(-up * (excess * removed / deltaTime), ForceMode.Acceleration);
        }

        /// <summary>Yaw rate the front wheels' angle asks for at the current speed, rad/s: the car rolling where its wheels point.</summary>
        private float SteeredYawRate() =>
            HasBothAxles ? ForwardSpeed * Mathf.Tan(SteerAngle * Mathf.Deg2Rad) / wheelbase : 0f;

        private void ApplyDownforce(Vector3 up, Vector3 forward, Vector3 velocity)
        {
            // Acting behind the centre of mass it loads the rear tyres more than the front, for stability at speed.
            float speedRatio = velocity.magnitude / config.MaxSpeed;
            float force = config.Downforce * Physics.gravity.magnitude * body.mass * speedRatio * speedRatio;
            Vector3 point = body.worldCenterOfMass + forward * (HasBothAxles ? config.DownforceBalance * wheelbase : 0f);
            body.AddForceAtPosition(-up * force, point);
        }

        private void ApplyAirControl(Quaternion rotation, Vector3 up, Vector3 velocity)
        {
            // A critically damped spring levels pitch and roll towards the ground the car will land on. It builds up over
            // the first moment after take-off, so the car keeps the pitch and roll the ramp gave it for a while. Steering
            // sets the yaw rate up to a limit, and without steering the spin the car took off with fades. The brake dips
            // the nose and the throttle lifts it.
            float build = config.AirLevelDelay > 0f ? Mathf.Clamp01(AirborneTime / config.AirLevelDelay) : 1f;
            float stiffness = config.AirAutoLevel * build;
            Vector3 angularVelocity = body.angularVelocity;
            float yawRate = Vector3.Dot(angularVelocity, up);
            Vector3 pitchRollVelocity = angularVelocity - up * yawRate;
            float yawAcceleration = Mathf.Clamp((input.Steer * config.AirYawRate - yawRate) / AirYawResponseTime, -config.AirControl, config.AirControl);
            float pitchAcceleration = input.Brake * config.AirPitchControl - input.Throttle * config.AirThrottlePitch;

            Vector3 torque = Vector3.Cross(up, LandingUp(velocity)) * stiffness
                             - pitchRollVelocity * (2f * Mathf.Sqrt(stiffness))
                             + up * yawAcceleration
                             + rotation * Vector3.right * pitchAcceleration;
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

        /// <summary>
        /// After a landing or a hard knock, damps pitch and roll for a moment so the car settles instead of rocking or
        /// rolling. After a knock it also damps the spin the hit gave it beyond what the steering asks for.
        /// </summary>
        private void ApplySettling(Vector3 up, float deltaTime)
        {
            if (settleTimeLeft > 0f)
            {
                settleTimeLeft -= deltaTime;
                Vector3 angularVelocity = body.angularVelocity;
                Vector3 pitchRollVelocity = angularVelocity - up * Vector3.Dot(angularVelocity, up);
                float removed = 1f - Mathf.Exp(-config.SettleDamping * deltaTime);
                body.AddTorque(-pitchRollVelocity * (removed / deltaTime), ForceMode.Acceleration);
            }

            if (knockTimeLeft > 0f)
            {
                knockTimeLeft -= deltaTime;
                float excess = Vector3.Dot(body.angularVelocity, up) - (IsGrounded ? SteeredYawRate() : 0f);
                float removed = 1f - Mathf.Exp(-config.ImpactYawDamping * deltaTime);
                body.AddTorque(-up * (excess * removed / deltaTime), ForceMode.Acceleration);
            }
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

        /// <summary>One grounded tyre's contact for the current physics step, worked out before any tyre force is applied.</summary>
        private struct TyreContact
        {
            public Vector3 Forward;
            public Vector3 Right;
            public float LateralSpeed;
            public float RollingSpeed;
            public float PointSpeed;

            /// <summary>Normal load with the weight transfer, newtons.</summary>
            public float Load;

            /// <summary>The load the tyre's grip works from: <see cref="Load"/> after load sensitivity and the grip load limit.</summary>
            public float GripLoad;

            /// <summary>Drive force the tyre can put down before it spins, newtons.</summary>
            public float Traction;

            /// <summary>Share of the car's drive the tyre can put down before it spins (0 in the air).</summary>
            public float DriveCap;

            /// <summary>Share of the car's drive sent to the tyre, after the differentials.</summary>
            public float DriveShare;

            /// <summary>Drive force sent to the tyre, newtons, before it is limited to its traction (negative in reverse).</summary>
            public float Drive;

            /// <summary>Brake force on the tyre, newtons.</summary>
            public float Brake;
        }
    }
}
