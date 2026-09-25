using UnityEngine;

namespace RallyX.Tracks
{
    /// <summary>
    /// A kind of ground (asphalt, dirt, mud...) and how it changes vehicle handling.
    /// Add new surfaces by creating more assets; no code changes are needed.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Surface Type", fileName = "Surface_New")]
    public sealed class SurfaceType : ScriptableObject
    {
        [SerializeField] private string displayName = "Surface";

        [Tooltip("Multiplier on tyre friction: sideways grip and braking.")]
        [SerializeField, Min(0f)] private float grip = 1f;

        [Tooltip("Multiplier on the vehicle's top speed.")]
        [SerializeField, Range(0.1f, 1.5f)] private float topSpeed = 1f;

        [Tooltip("Multiplier on drive force.")]
        [SerializeField, Range(0.1f, 1.5f)] private float acceleration = 1f;

        [Tooltip("Extra deceleration from surface drag, m/s².")]
        [SerializeField, Min(0f)] private float rollingResistance;

        public string DisplayName => displayName;

        public SurfaceProperties Properties => new SurfaceProperties(grip, topSpeed, acceleration, rollingResistance);
    }
}
