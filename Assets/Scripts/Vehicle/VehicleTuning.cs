using System;
using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>
    /// Performance changes applied on top of a <see cref="VehicleConfig"/>, such as installed upgrades. Each value is
    /// a fraction of the car's own figure (0.1 = 10% more), except stability, which is added to the anti-roll.
    /// All zero is the car as built.
    /// </summary>
    [Serializable]
    public struct VehicleTuning
    {
        [Tooltip("Extra acceleration at every speed, fraction.")]
        [SerializeField, Range(0f, 1f)] private float acceleration;

        [Tooltip("Extra acceleration that grows with speed, up to this fraction near top speed (a turbo's pull).")]
        [SerializeField, Range(0f, 1f)] private float highSpeedPull;

        [Tooltip("Higher top speed, fraction.")]
        [SerializeField, Range(0f, 0.5f)] private float topSpeed;

        [Tooltip("More tyre grip, fraction.")]
        [SerializeField, Range(0f, 0.5f)] private float grip;

        [Tooltip("Harder braking, fraction.")]
        [SerializeField, Range(0f, 1f)] private float braking;

        [Tooltip("More steering at speed, fraction of the steering the car keeps at speed.")]
        [SerializeField, Range(0f, 1f)] private float steering;

        [Tooltip("Added to the anti-roll (0..1): the body leans less and the car settles sooner.")]
        [SerializeField, Range(0f, 0.3f)] private float stability;

        public VehicleTuning(float acceleration = 0f, float highSpeedPull = 0f, float topSpeed = 0f, float grip = 0f, float braking = 0f,
            float steering = 0f, float stability = 0f)
        {
            this.acceleration = acceleration;
            this.highSpeedPull = highSpeedPull;
            this.topSpeed = topSpeed;
            this.grip = grip;
            this.braking = braking;
            this.steering = steering;
            this.stability = stability;
        }

        public float Acceleration => acceleration;
        public float HighSpeedPull => highSpeedPull;
        public float TopSpeed => topSpeed;
        public float Grip => grip;
        public float Braking => braking;
        public float Steering => steering;
        public float Stability => stability;

        /// <summary>True when nothing is changed: the car as built.</summary>
        public bool IsStock => acceleration == 0f && highSpeedPull == 0f && topSpeed == 0f && grip == 0f && braking == 0f
                               && steering == 0f && stability == 0f;

        /// <summary>Both sets of changes together.</summary>
        public static VehicleTuning operator +(VehicleTuning a, VehicleTuning b) => new VehicleTuning(
            a.acceleration + b.acceleration, a.highSpeedPull + b.highSpeedPull, a.topSpeed + b.topSpeed, a.grip + b.grip,
            a.braking + b.braking, a.steering + b.steering, a.stability + b.stability);
    }
}
