using System;
using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>
    /// Drift state for one vehicle: slip angle, rear grip loss from the handbrake and drift start/end.
    /// Scoring, boost, smoke and audio subscribe to the events instead of reaching into the controller.
    /// </summary>
    public sealed class VehicleDrift
    {
        // The angle must fall well below the entry threshold before a drift ends, so it doesn't flicker.
        private const float ExitThresholdFactor = 0.5f;
        private const float MinSpeedForSlipAngle = 1f;

        // Past this the car travels backwards relative to its nose: a spin, not a drift.
        private const float SpinOutAngle = 90f;

        /// <summary>Raised when a drift begins.</summary>
        public event Action DriftStarted;

        /// <summary>Raised when a drift ends, with its duration in seconds.</summary>
        public event Action<float> DriftEnded;

        public bool IsDrifting { get; private set; }

        /// <summary>
        /// Signed angle between heading and travel direction, -180..180 degrees. Positive = travelling to the right of the nose.
        /// 0 while nearly stopped or deliberately reversing.
        /// </summary>
        public float SlipAngle { get; private set; }

        /// <summary>Speed in the ground plane, m/s.</summary>
        public float PlanarSpeed { get; private set; }

        /// <summary>Multiplier on rear tyre grip: drops while the handbrake is held, then recovers.</summary>
        public float RearGripScale { get; private set; } = 1f;

        /// <summary>Seconds the current drift has lasted (0 when not drifting).</summary>
        public float CurrentDriftDuration { get; private set; }

        internal void Step(VehicleConfig config, bool handbrake, bool reversing, bool grounded, Vector3 velocity, Vector3 forward,
            Vector3 up, float deltaTime)
        {
            Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, up);
            PlanarSpeed = planarVelocity.magnitude;

            SlipAngle = PlanarSpeed > MinSpeedForSlipAngle && !reversing
                ? Vector3.SignedAngle(Vector3.ProjectOnPlane(forward, up), planarVelocity, up)
                : 0f;

            if (!config.DriftEnabled)
            {
                RearGripScale = 1f;
                SetDrifting(false);
                return;
            }

            // Grip drops the moment the handbrake is pulled and recovers gradually after release,
            // which lets a drift be held on the throttle.
            if (handbrake)
                RearGripScale = Mathf.Min(RearGripScale, config.DriftGrip);
            else
                RearGripScale = Mathf.MoveTowards(RearGripScale, 1f, deltaTime / config.DriftRecoveryTime);

            float threshold = IsDrifting ? config.DriftAngleThreshold * ExitThresholdFactor : config.DriftAngleThreshold;
            float slip = Mathf.Abs(SlipAngle);
            bool drifting = grounded && PlanarSpeed >= config.MinDriftSpeed && slip >= threshold && slip < SpinOutAngle;
            if (drifting)
                CurrentDriftDuration += deltaTime;
            SetDrifting(drifting);
        }

        internal void Reset()
        {
            RearGripScale = 1f;
            SlipAngle = 0f;
            PlanarSpeed = 0f;
            SetDrifting(false);
        }

        private void SetDrifting(bool drifting)
        {
            if (drifting == IsDrifting)
                return;

            IsDrifting = drifting;
            if (drifting)
            {
                DriftStarted?.Invoke();
            }
            else
            {
                float duration = CurrentDriftDuration;
                CurrentDriftDuration = 0f;
                DriftEnded?.Invoke(duration);
            }
        }
    }
}
