using UnityEngine;

namespace RallyX.Cameras
{
    /// <summary>
    /// Trauma-based camera shake: events add trauma (0..1), which decays over time.
    /// Shake strength is trauma squared, so small hits stay subtle and big ones still read clearly.
    /// Smooth Perlin noise is used instead of random jitter to avoid a nauseating buzz.
    /// </summary>
    public sealed class CameraShake
    {
        // Separate noise rows per axis so the axes don't move in lockstep.
        private const float NoiseRowSpacing = 17.3f;

        public float Trauma { get; private set; }
        public Vector3 PositionOffset { get; private set; }
        public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

        private float noiseTime;

        public void AddTrauma(float amount) => Trauma = Mathf.Clamp01(Trauma + amount);

        public void Clear()
        {
            Trauma = 0f;
            PositionOffset = Vector3.zero;
            RotationOffset = Quaternion.identity;
        }

        public void Update(ChaseCameraConfig config, float deltaTime)
        {
            Trauma = Mathf.MoveTowards(Trauma, 0f, config.TraumaDecay * deltaTime);
            noiseTime += deltaTime * config.ShakeFrequency;

            float strength = Trauma * Trauma;
            if (strength <= 0f)
            {
                PositionOffset = Vector3.zero;
                RotationOffset = Quaternion.identity;
                return;
            }

            float offset = config.MaxShakeOffset * strength;
            float angle = config.MaxShakeAngle * strength;

            // No forward/back offset and half-strength roll: both are the most disorienting axes.
            PositionOffset = new Vector3(Noise(0) * offset, Noise(1) * offset, 0f);
            RotationOffset = Quaternion.Euler(Noise(2) * angle, Noise(3) * angle, Noise(4) * angle * 0.5f);
        }

        private float Noise(int row) => Mathf.PerlinNoise(row * NoiseRowSpacing, noiseTime) * 2f - 1f;
    }
}
