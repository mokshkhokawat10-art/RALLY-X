namespace RallyX.Tracks
{
    /// <summary>Handling multipliers a ground surface applies to the tyres touching it.</summary>
    public readonly struct SurfaceProperties
    {
        public static readonly SurfaceProperties Neutral = new SurfaceProperties(1f, 1f, 1f, 0f);

        /// <summary>Multiplier on tyre friction (sideways grip and braking).</summary>
        public readonly float Grip;

        /// <summary>Multiplier on the vehicle's top speed.</summary>
        public readonly float TopSpeed;

        /// <summary>Multiplier on drive force.</summary>
        public readonly float Acceleration;

        /// <summary>Extra deceleration from surface drag, m/s².</summary>
        public readonly float RollingResistance;

        public SurfaceProperties(float grip, float topSpeed, float acceleration, float rollingResistance)
        {
            Grip = grip;
            TopSpeed = topSpeed;
            Acceleration = acceleration;
            RollingResistance = rollingResistance;
        }
    }
}
