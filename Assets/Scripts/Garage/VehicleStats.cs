using System;
using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.Garage
{
    /// <summary>The five figures the garage compares cars on.</summary>
    public enum VehicleStat
    {
        TopSpeed,
        Acceleration,
        Handling,
        Grip,
        Braking,
    }

    /// <summary>
    /// A car's performance worked out from its own <see cref="VehicleConfig"/> and wheel layout, so the garage always
    /// shows what the car really does: top speed, 0-100 km/h time, steering response at 100 km/h, cornering grip on
    /// asphalt and the braking distance from 100 km/h. Figures are for flat asphalt without drag or rolling resistance.
    /// </summary>
    public readonly struct VehicleStats
    {
        /// <summary>Speed the acceleration and braking figures are measured from, m/s (100 km/h).</summary>
        public const float ReferenceSpeed = 100f / 3.6f;

        private const float IntegrationStep = 0.05f;

        public VehicleStats(float topSpeedKmh, float zeroToHundredSeconds, float turnIn, float gripG, float brakingDistance)
        {
            TopSpeedKmh = topSpeedKmh;
            ZeroToHundredSeconds = zeroToHundredSeconds;
            TurnIn = turnIn;
            GripG = gripG;
            BrakingDistance = brakingDistance;
        }

        public float TopSpeedKmh { get; }

        /// <summary>Time from standstill to 100 km/h at full throttle, seconds (infinite if the car can't reach it).</summary>
        public float ZeroToHundredSeconds { get; }

        /// <summary>
        /// How sharply full steering lock turns the car at 100 km/h: curvature of the path the front wheels point
        /// along, 1/m. More steering lock at speed and a shorter wheelbase make a car turn in more eagerly.
        /// </summary>
        public float TurnIn { get; }

        /// <summary>Sideways grip of the weaker axle on asphalt, g.</summary>
        public float GripG { get; }

        /// <summary>Distance to stop from 100 km/h under full braking, metres.</summary>
        public float BrakingDistance { get; }

        /// <summary>The figure behind a stat. Bigger is better for every stat except acceleration time and braking distance.</summary>
        public float Get(VehicleStat stat) => stat switch
        {
            VehicleStat.TopSpeed => TopSpeedKmh,
            VehicleStat.Acceleration => ZeroToHundredSeconds,
            VehicleStat.Handling => TurnIn,
            VehicleStat.Grip => GripG,
            VehicleStat.Braking => BrakingDistance,
            _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
        };

        public static VehicleStats Measure(VehicleController car)
        {
            if (car.Config == null)
                throw new ArgumentException($"{car.name} has no VehicleConfig.", nameof(car));
            return Measure(car.Config, Wheelbase(car));
        }

        /// <summary>The figures for a config on a car with this wheelbase, e.g. an upgraded copy of a car's config.</summary>
        public static VehicleStats Measure(VehicleConfig config, float wheelbase)
        {
            float lockAtReference = config.MaxSteerAngle * config.SteerBySpeed.Evaluate(Mathf.Clamp01(ReferenceSpeed / config.MaxSpeed));
            float turnIn = Mathf.Tan(lockAtReference * Mathf.Deg2Rad) / wheelbase;
            float grip = config.TyreFriction * Mathf.Min(config.FrontGrip, config.RearGrip);
            float braking = config.BrakeDeceleration > 0f ? ReferenceSpeed * ReferenceSpeed / (2f * config.BrakeDeceleration) : float.PositiveInfinity;
            return new VehicleStats(config.MaxSpeedKmh, TimeToReferenceSpeed(config), turnIn, grip, braking);
        }

        /// <summary>The display name of a stat, as shown in the garage.</summary>
        public static string Label(VehicleStat stat) => stat switch
        {
            VehicleStat.TopSpeed => "TOP SPEED",
            VehicleStat.Acceleration => "ACCELERATION",
            VehicleStat.Handling => "HANDLING",
            VehicleStat.Grip => "GRIP",
            VehicleStat.Braking => "BRAKING",
            _ => stat.ToString().ToUpperInvariant(),
        };

        /// <summary>A stat's figure with its unit. Handling has no everyday unit, so it shows as a rating out of 10.</summary>
        public string Describe(VehicleStat stat, float rating) => stat switch
        {
            VehicleStat.TopSpeed => $"{TopSpeedKmh:0} km/h",
            VehicleStat.Acceleration => float.IsInfinity(ZeroToHundredSeconds) ? "0-100  --" : $"0-100  {ZeroToHundredSeconds:0.0} s",
            VehicleStat.Handling => $"{rating * 10f:0.0} / 10",
            VehicleStat.Grip => $"{GripG:0.00} g",
            VehicleStat.Braking => $"100-0  {BrakingDistance:0} m",
            _ => Get(stat).ToString("0.0"),
        };

        /// <summary>Distance between the front and rear axles, metres.</summary>
        public static float Wheelbase(VehicleController car)
        {
            float front = 0f;
            float rear = 0f;
            int frontCount = 0;
            int rearCount = 0;
            foreach (var wheel in car.Wheels)
            {
                if (wheel == null)
                    continue;

                float along = car.transform.InverseTransformPoint(wheel.transform.position).z;
                if (wheel.IsFront)
                {
                    front += along;
                    frontCount++;
                }
                else
                {
                    rear += along;
                    rearCount++;
                }
            }

            return frontCount > 0 && rearCount > 0 ? Mathf.Max(0.5f, front / frontCount - rear / rearCount) : 2.6f;
        }

        private static float TimeToReferenceSpeed(VehicleConfig config)
        {
            if (config.MaxSpeed <= ReferenceSpeed)
                return float.PositiveInfinity;

            // Same drive law as the car: acceleration shaped by the curve over the share of top speed reached.
            float time = 0f;
            for (float speed = 0f; speed < ReferenceSpeed; speed += IntegrationStep)
            {
                float middle = speed + IntegrationStep * 0.5f;
                float acceleration = config.Acceleration * config.AccelerationCurve.Evaluate(middle / config.MaxSpeed);
                if (acceleration <= 1e-4f)
                    return float.PositiveInfinity;
                time += IntegrationStep / acceleration;
            }

            return time;
        }
    }

    /// <summary>How a stat's figure maps onto the garage's bars: the figure for an empty bar and for a full one.</summary>
    [Serializable]
    public struct VehicleStatScale
    {
        [Tooltip("Figure shown as an empty bar.")]
        [SerializeField] private float worst;

        [Tooltip("Figure shown as a full bar. Lower than the worst figure for stats where less is better (times, distances).")]
        [SerializeField] private float best;

        public VehicleStatScale(float worst, float best)
        {
            this.worst = worst;
            this.best = best;
        }

        public float Worst => worst;
        public float Best => best;

        /// <summary>0 (worst) .. 1 (best).</summary>
        public float Rate(float figure) => Mathf.Approximately(worst, best) ? 1f : Mathf.Clamp01(Mathf.InverseLerp(worst, best, figure));
    }
}
