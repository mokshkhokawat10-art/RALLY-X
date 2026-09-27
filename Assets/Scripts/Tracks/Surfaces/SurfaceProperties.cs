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

        /// <summary>
        /// Share of the grip-by-slip drop the tyres suffer when sliding: 1 = the car's full drop, below 1 the surface
        /// keeps more grip in a slide (loose ground the tyres dig into), so slides build up and die down progressively.
        /// </summary>
        public readonly float SlideFalloff;

        /// <summary>Multiplier on the drive the tyres can put down before they spin, on top of <see cref="Grip"/>.</summary>
        public readonly float Traction;

        public SurfaceProperties(float grip, float topSpeed, float acceleration, float rollingResistance, float slideFalloff = 1f,
            float traction = 1f)
        {
            Grip = grip;
            TopSpeed = topSpeed;
            Acceleration = acceleration;
            RollingResistance = rollingResistance;
            SlideFalloff = slideFalloff;
            Traction = traction;
        }
    }
}
