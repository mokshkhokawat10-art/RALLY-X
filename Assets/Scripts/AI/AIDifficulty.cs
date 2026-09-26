using System;
using UnityEngine;

namespace RallyX.AI
{
    /// <summary>How hard the AI drivers race. Tuned in <see cref="AISettings"/>.</summary>
    public enum AIDifficulty
    {
        Easy,
        Normal,
        Hard,
    }

    /// <summary>
    /// How one difficulty scales every driver's own pace (<see cref="AIDriverProfile"/>). It stays the same for the
    /// whole race, wherever the player is: drivers never speed up to catch the player or slow down to wait.
    /// </summary>
    [Serializable]
    public struct AIDifficultyLevel
    {
        [Tooltip("Scales the sideways grip each driver dares in corners, so how fast they take them. 1 = the driver's own pace.")]
        [SerializeField, Range(0.5f, 1.5f)] private float cornering;

        [Tooltip("Scales how hard each driver plans to brake, so how late they brake for corners. 1 = the driver's own braking.")]
        [SerializeField, Range(0.5f, 1.5f)] private float braking;

        [Tooltip("Scales each driver's share of the car's top speed. Never more than the car's top speed.")]
        [SerializeField, Range(0.5f, 1.2f)] private float topSpeed;

        public AIDifficultyLevel(float cornering, float braking, float topSpeed)
        {
            this.cornering = cornering;
            this.braking = braking;
            this.topSpeed = topSpeed;
        }

        public float Cornering => cornering;
        public float Braking => braking;
        public float TopSpeed => topSpeed;
    }
}
