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

        [Tooltip("Acceleration multiplier over normalised speed (0 = standstill, 1 = top speed).")]
        [SerializeField] private AnimationCurve accelerationCurve =
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0f));

        [SerializeField, Min(1f)] private float maxReverseSpeedKmh = 40f;
        [SerializeField, Min(0f)] private float reverseAcceleration = 6f;

        [Tooltip("Share of drive force sent to the front wheels. 0 = rear-wheel drive, 1 = front-wheel drive.")]
        [SerializeField, Range(0f, 1f)] private float frontDriveShare = 0.4f;

        [Tooltip("Deceleration when neither throttle nor brake is pressed, m/s².")]
        [SerializeField, Min(0f)] private float coastDeceleration = 1.5f;

        [Header("Brakes")]
        [Tooltip("Brake deceleration on a full-grip surface, m/s².")]
        [SerializeField, Min(0f)] private float brakeDeceleration = 18f;

        [Tooltip("Deceleration from the locked rear wheels while the handbrake is held, m/s².")]
        [SerializeField, Min(0f)] private float handbrakeDeceleration = 5f;

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

        [Header("Aerodynamics")]
        [Tooltip("Extra downward force at top speed as a multiple of gravity. Scales with speed squared and adds grip.")]
        [SerializeField, Min(0f)] private float downforce = 0.5f;

        [Header("Air Control")]
        [Tooltip("Yaw acceleration from steering while airborne, rad/s².")]
        [SerializeField, Min(0f)] private float airControl = 2f;

        [Tooltip("Strength of automatic pitch and roll levelling while airborne, for clean landings.")]
        [SerializeField, Min(0f)] private float airAutoLevel = 4f;

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

        public float BrakeDeceleration => brakeDeceleration;
        public float HandbrakeDeceleration => handbrakeDeceleration;

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
        public float DriftAngleThreshold => driftAngleThreshold;
        public float MaxDriftAngle => maxDriftAngle;
        public float AntiSpin => antiSpin;
        public float AntiSpinDamping => antiSpinDamping;
        public float MinDriftSpeed => minDriftSpeedKmh / 3.6f;

        public float SuspensionTravel => suspensionTravel;
        public float WheelRadius => wheelRadius;
        public float SpringFrequency => springFrequency;
        public float DampingRatio => dampingRatio;

        public float Downforce => downforce;

        public float AirControl => airControl;
        public float AirAutoLevel => airAutoLevel;

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
