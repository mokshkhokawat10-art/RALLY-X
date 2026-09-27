using System;
using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>
    /// Designer-tunable handling for one vehicle. The controller reads it live,
    /// so edits made in Play Mode apply immediately (and persist, as with any asset).
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Vehicle Config", fileName = "VehicleConfig")]
    public sealed class VehicleConfig : ScriptableObject
    {
        [Header("Body")]
        [SerializeField, Min(100f)] private float mass = 1250f;

        [Tooltip("Centre of mass in the vehicle's local space. Lower is more stable.")]
        [SerializeField] private Vector3 centerOfMass = new Vector3(0f, 0.45f, 0.05f);

        [SerializeField, Min(0f)] private float angularDamping = 0.6f;

        [Tooltip("Raises tyre forces towards the centre of mass. 1 = no body roll or pitch, 0 = full roll (can tip over).")]
        [SerializeField, Range(0f, 1f)] private float antiRoll = 0.7f;

        [Header("Engine")]
        [Tooltip("Top forward speed on a neutral surface, km/h.")]
        [SerializeField, Min(1f)] private float maxSpeedKmh = 165f;

        [Tooltip("Forward acceleration from standstill, m/s².")]
        [SerializeField, Min(0f)] private float acceleration = 10f;

        [Tooltip("Acceleration multiplier over normalised speed (0 = standstill, 1 = top speed). A soft launch, full pull " +
                 "through the mid-range, then fading as the engine runs out of power. The AI plans fast corners at the car's top " +
                 "speed, so a stronger top end makes it arrive at them faster.")]
        [SerializeField] private AnimationCurve accelerationCurve = new AnimationCurve(
            new Keyframe(0f, 0.8f, 1.0833f, 1.0833f), new Keyframe(0.12f, 0.93f, 0.5333f, 0.5333f), new Keyframe(0.3f, 0.96f, 0f, 0f),
            new Keyframe(0.55f, 0.74f, -1.1667f, -1.1667f), new Keyframe(0.78f, 0.4f, -1.7714f, -1.7714f),
            new Keyframe(0.9f, 0.12f, -1.8182f, -1.8182f), new Keyframe(1f, 0f, 0f, 0f));

        [SerializeField, Min(1f)] private float maxReverseSpeedKmh = 40f;
        [SerializeField, Min(0f)] private float reverseAcceleration = 6f;

        [Tooltip("Share of drive force sent to the front wheels. 0 = rear-wheel drive, 1 = front-wheel drive.")]
        [SerializeField, Range(0f, 1f)] private float frontDriveShare = 0.4f;

        [Tooltip("Deceleration when neither throttle nor brake is pressed, m/s².")]
        [SerializeField, Min(0f)] private float coastDeceleration = 1.5f;

        [Tooltip("Drive force a tyre can put down, as a multiple of its grip limit (tyre friction x surface grip x load). " +
                 "Beyond it the wheel spins: on loose ground, or where drive is concentrated on few wheels.")]
        [SerializeField, Min(0.1f)] private float driveTraction = 1.8f;

        [Header("Brakes")]
        [Tooltip("Brake deceleration on a full-grip surface, m/s².")]
        [SerializeField, Min(0f)] private float brakeDeceleration = 18f;

        [Tooltip("Share of the braking done by the front wheels. Above 0.5 the car stays straight under hard braking; " +
                 "below it the tail steps out more readily when braking into a corner. The stopping distance is the same.")]
        [SerializeField, Range(0.2f, 0.8f)] private float brakeBias = 0.55f;

        [Tooltip("Deceleration from the locked rear wheels while the handbrake is held, m/s².")]
        [SerializeField, Min(0f)] private float handbrakeDeceleration = 5f;

        [Header("Weight Transfer")]
        [Tooltip("Extra share of the full load transfer between the axles under braking and acceleration that the tyres feel " +
                 "(on top of what the suspension already carries). Braking loads the front tyres for sharper turn-in; " +
                 "accelerating loads the rear. 0 = off.")]
        [SerializeField, Range(0f, 1f)] private float weightTransfer = 0.15f;

        [Tooltip("Seconds for the load to shift, like the body settling onto its springs. Also smooths the accelerations the " +
                 "body lean and camera read.")]
        [SerializeField, Min(0.01f)] private float weightTransferTime = 0.12f;

        [Header("Steering")]
        [SerializeField, Range(1f, 60f)] private float maxSteerAngle = 32f;

        [Tooltip("Steering sensitivity: how fast the front wheels turn towards the input, degrees per second.")]
        [SerializeField, Min(1f)] private float steerSpeed = 200f;

        [Tooltip("Steering angle multiplier over normalised speed. Keeps high-speed steering stable.")]
        [SerializeField] private AnimationCurve steerBySpeed =
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f));

        [Header("Grip")]
        [Tooltip("Peak tyre friction coefficient. Max cornering force per wheel = friction x grip multipliers x wheel load.")]
        [SerializeField, Min(0f)] private float tyreFriction = 1.3f;

        [Tooltip("Front axle grip multiplier.")]
        [SerializeField, Min(0f)] private float frontGrip = 1f;

        [Tooltip("Rear axle grip multiplier. Below the front value the car turns in more eagerly.")]
        [SerializeField, Min(0f)] private float rearGrip = 0.95f;

        [Tooltip("Fraction of sideways sliding the tyres correct per 0.02 s step, before the friction limit. Higher feels crisper.")]
        [SerializeField, Range(0.05f, 1f)] private float lateralStiffness = 0.85f;

        [Tooltip("Grip multiplier over slip (0 = rolling straight, 1 = fully sideways). Falling values let slides carry on.")]
        [SerializeField] private AnimationCurve gripBySlip =
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.9f), new Keyframe(1f, 0.7f));

        [Header("Drift")]
        [SerializeField] private bool driftEnabled = true;

        [Tooltip("Rear grip multiplier while the handbrake is held.")]
        [SerializeField, Range(0f, 1f)] private float driftGrip = 0.3f;

        [Tooltip("Yaw assist while drifting, rad/s² at full steering. Higher = the car rotates into and holds drifts more eagerly. 0 disables it.")]
        [SerializeField, Min(0f)] private float driftMultiplier = 2.5f;

        [Tooltip("Seconds for rear grip to fully recover after the handbrake is released.")]
        [SerializeField, Min(0.01f)] private float driftRecoveryTime = 0.6f;

        [Tooltip("Rear grip multiplier held at full throttle during a drift started with the handbrake, so the slide can be " +
                 "kept going on the throttle; lifting off lets the grip recover. 1 = off.")]
        [SerializeField, Range(0f, 1f)] private float driftSustainGrip = 0.65f;

        [Tooltip("Slip angle, in degrees, above which the car counts as drifting.")]
        [SerializeField, Range(1f, 60f)] private float driftAngleThreshold = 12f;

        [Tooltip("Largest drift angle, degrees. Anti-spin starts correcting 10° before it to prevent spin-outs.")]
        [SerializeField, Range(30f, 89f)] private float maxDriftAngle = 70f;

        [Tooltip("Yaw acceleration turning the nose back towards the direction of travel at the drift limit, rad/s².")]
        [SerializeField, Min(0f)] private float antiSpin = 6f;

        [Tooltip("Fraction of spin-deepening rotation removed per 0.02 s step at the drift limit. Higher holds the limit more firmly.")]
        [SerializeField, Range(0f, 1f)] private float antiSpinDamping = 0.25f;

        [SerializeField, Min(1f)] private float minDriftSpeedKmh = 25f;

        [Header("Suspension")]
        [SerializeField, Min(0.05f)] private float suspensionTravel = 0.3f;
        [SerializeField, Min(0.1f)] private float wheelRadius = 0.36f;

        [Tooltip("Spring natural frequency, Hz. Higher = stiffer. Independent of mass.")]
        [SerializeField, Range(0.5f, 5f)] private float springFrequency = 1.8f;

        [Tooltip("1 = critically damped. Lower = bouncier.")]
        [SerializeField, Range(0f, 2f)] private float dampingRatio = 0.55f;

        [Tooltip("Damping multiplier while the spring extends (rebound). Above 1 the body settles after bumps and landings " +
                 "instead of bouncing, while bumps are still soaked up softly.")]
        [SerializeField, Range(0.5f, 3f)] private float reboundDamping = 1.5f;

        [Tooltip("The last part of the travel, metres, where a stiff bump stop takes over so hard landings don't bottom out.")]
        [SerializeField, Range(0f, 0.15f)] private float bumpStopTravel = 0.06f;

        [Tooltip("Bump stop stiffness as a multiple of the spring rate.")]
        [SerializeField, Min(0f)] private float bumpStopStiffness = 8f;

        [Header("Aerodynamics")]
        [Tooltip("Extra downward force at top speed as a multiple of gravity. Scales with speed squared and adds grip.")]
        [SerializeField, Min(0f)] private float downforce = 0.5f;

        [Header("Air Control")]
        [Tooltip("Yaw acceleration from steering while airborne, rad/s².")]
        [SerializeField, Min(0f)] private float airControl = 2f;

        [Tooltip("Fastest the car can be turned by steering in the air, rad/s. Without steering the spin it took off with fades out.")]
        [SerializeField, Min(0f)] private float airYawRate = 1.2f;

        [Tooltip("Strength of automatic pitch and roll levelling while airborne, for clean landings.")]
        [SerializeField, Min(0f)] private float airAutoLevel = 4f;

        [Tooltip("How far the levelling lines the car up with the ground it will land on rather than with the horizon (0..1).")]
        [SerializeField, Range(0f, 1f)] private float landingAlignment = 0.8f;

        [Tooltip("Nose-down pitch from the brake while airborne, rad/s², to save a landing that would come in tail first.")]
        [SerializeField, Min(0f)] private float airPitchControl = 1.5f;

        [Header("Landings and Impacts")]
        [Tooltip("Seconds after a landing or a hard knock during which extra damping calms pitch and roll.")]
        [SerializeField, Min(0f)] private float settleTime = 0.4f;

        [Tooltip("Pitch and roll damping while settling, 1/s. Higher stops the car rocking sooner.")]
        [SerializeField, Min(0f)] private float settleDamping = 5f;

        [Tooltip("Change of speed from a collision, m/s, that counts as a hard knock and starts the settling.")]
        [SerializeField, Min(0.1f)] private float knockSpeed = 2f;

        /// <summary>Raised in the Editor when a value changes, so body settings can be re-applied live.</summary>
        public event Action Changed;

        public float Mass => mass;
        public Vector3 CenterOfMass => centerOfMass;
        public float AngularDamping => angularDamping;
        public float AntiRoll => antiRoll;

        public float MaxSpeedKmh => maxSpeedKmh;
        public float MaxSpeed => maxSpeedKmh / 3.6f;
        public float Acceleration => acceleration;
        public AnimationCurve AccelerationCurve => accelerationCurve;
        public float MaxReverseSpeedKmh => maxReverseSpeedKmh;
        public float MaxReverseSpeed => maxReverseSpeedKmh / 3.6f;
        public float ReverseAcceleration => reverseAcceleration;
        public float FrontDriveShare => frontDriveShare;
        public float CoastDeceleration => coastDeceleration;
        public float DriveTraction => driveTraction;

        public float BrakeDeceleration => brakeDeceleration;
        public float BrakeBias => brakeBias;
        public float HandbrakeDeceleration => handbrakeDeceleration;

        public float WeightTransfer => weightTransfer;
        public float WeightTransferTime => weightTransferTime;

        public float MaxSteerAngle => maxSteerAngle;
        public float SteerSpeed => steerSpeed;
        public AnimationCurve SteerBySpeed => steerBySpeed;

        public float TyreFriction => tyreFriction;
        public float FrontGrip => frontGrip;
        public float RearGrip => rearGrip;
        public float LateralStiffness => lateralStiffness;
        public AnimationCurve GripBySlip => gripBySlip;

        public bool DriftEnabled => driftEnabled;
        public float DriftGrip => driftGrip;
        public float DriftMultiplier => driftMultiplier;
        public float DriftRecoveryTime => driftRecoveryTime;
        public float DriftSustainGrip => driftSustainGrip;
        public float DriftAngleThreshold => driftAngleThreshold;
        public float MaxDriftAngle => maxDriftAngle;
        public float AntiSpin => antiSpin;
        public float AntiSpinDamping => antiSpinDamping;
        public float MinDriftSpeed => minDriftSpeedKmh / 3.6f;

        public float SuspensionTravel => suspensionTravel;
        public float WheelRadius => wheelRadius;
        public float SpringFrequency => springFrequency;
        public float DampingRatio => dampingRatio;
        public float ReboundDamping => reboundDamping;
        public float BumpStopTravel => bumpStopTravel;
        public float BumpStopStiffness => bumpStopStiffness;

        public float Downforce => downforce;

        public float AirControl => airControl;
        public float AirYawRate => airYawRate;
        public float AirAutoLevel => airAutoLevel;
        public float LandingAlignment => landingAlignment;
        public float AirPitchControl => airPitchControl;

        public float SettleTime => settleTime;
        public float SettleDamping => settleDamping;
        public float KnockSpeed => knockSpeed;

        /// <summary>
        /// A copy of this config with tuning applied, for one car at runtime (installed upgrades). The asset itself is
        /// never changed. Destroy the copy when the car is gone.
        /// </summary>
        public VehicleConfig CreateTuned(VehicleTuning tuning)
        {
            var tuned = Instantiate(this);
            tuned.name = $"{name} (Tuned)";
            tuned.acceleration *= 1f + tuning.Acceleration;
            tuned.maxSpeedKmh *= 1f + tuning.TopSpeed;

            // Downforce is set as a share of the weight at top speed; keep the same downforce at every speed, so a
            // higher top speed never costs grip in fast corners.
            tuned.downforce *= (1f + tuning.TopSpeed) * (1f + tuning.TopSpeed);
            tuned.tyreFriction *= 1f + tuning.Grip;
            tuned.brakeDeceleration *= 1f + tuning.Braking;
            tuned.antiRoll = Mathf.Clamp01(antiRoll + tuning.Stability);

            // Turbo pull: nothing extra at a standstill, growing to the full fraction at top speed, where the curve
            // is zero, so it adds pull without raising the top speed on its own.
            tuned.accelerationCurve = ScaleCurve(accelerationCurve, time => 1f + tuning.HighSpeedPull * time, time => tuning.HighSpeedPull);

            // More steering at speed; full lock at a standstill stays the same.
            float steer = 1f + tuning.Steering;
            tuned.steerBySpeed = ScaleCurve(steerBySpeed, time => time > 0f ? steer : 1f, _ => 0f, maxValue: 1f);
            return tuned;
        }

        /// <summary>
        /// A curve with every key's value multiplied by <paramref name="factor"/> at its time. Slopes follow the product
        /// rule, with <paramref name="factorSlope"/> the factor's rate of change.
        /// </summary>
        private static AnimationCurve ScaleCurve(AnimationCurve curve, Func<float, float> factor, Func<float, float> factorSlope,
            float maxValue = float.MaxValue)
        {
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                float f = factor(key.time);
                float slope = factorSlope(key.time);
                float value = key.value;
                key.value = Mathf.Min(value * f, maxValue);
                key.inTangent = key.inTangent * f + value * slope;
                key.outTangent = key.outTangent * f + value * slope;
                keys[i] = key;
            }

            return new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        }

#if UNITY_EDITOR
        private void OnValidate() => Changed?.Invoke();
#endif
    }
}
