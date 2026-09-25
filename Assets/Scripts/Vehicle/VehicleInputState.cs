using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>Driver commands for one physics step. Produced by the player or an AI driver.</summary>
    public readonly struct VehicleInputState
    {
        /// <summary>0..1</summary>
        public readonly float Throttle;

        /// <summary>0..1. Engages reverse once the vehicle has stopped.</summary>
        public readonly float Brake;

        /// <summary>-1 (left) .. 1 (right)</summary>
        public readonly float Steer;

        public readonly bool Handbrake;

        public VehicleInputState(float throttle, float brake, float steer, bool handbrake)
        {
            Throttle = Mathf.Clamp01(throttle);
            Brake = Mathf.Clamp01(brake);
            Steer = Mathf.Clamp(steer, -1f, 1f);
            Handbrake = handbrake;
        }
    }
}
