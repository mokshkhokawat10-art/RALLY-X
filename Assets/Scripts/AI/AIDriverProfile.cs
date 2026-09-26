using UnityEngine;

namespace RallyX.AI
{
    /// <summary>One AI driver's name, paint, pace and style.</summary>
    [CreateAssetMenu(menuName = "RALLY X/AI Driver Profile", fileName = "AIDriver")]
    public sealed class AIDriverProfile : ScriptableObject
    {
        [SerializeField] private string displayName = "Driver";
        [SerializeField] private Color paint = new Color(0.15f, 0.35f, 0.85f);

        [Header("Pace")]
        [Tooltip("Sideways acceleration the driver dares in corners on full grip, m/s². Scaled down on loose surfaces.")]
        [SerializeField, Range(2f, 15f)] private float cornerGrip = 7.5f;

        [Tooltip("Deceleration the driver plans on when braking on full grip, m/s².")]
        [SerializeField, Range(2f, 20f)] private float braking = 10f;

        [Tooltip("Share of the car's top speed the driver uses.")]
        [SerializeField, Range(0.5f, 1f)] private float topSpeed = 0.95f;

        [Header("Style")]
        [Tooltip("Preferred distance to the right of the racing line, metres (negative = left). Spreads the field out.")]
        [SerializeField, Range(-2f, 2f)] private float laneOffset;

        [Tooltip("Chance of taking a shortcut on each lap.")]
        [SerializeField, Range(0f, 1f)] private float shortcutChance = 0.5f;

        public string DisplayName => displayName;
        public Color Paint => paint;
        public float CornerGrip => cornerGrip;
        public float Braking => braking;
        public float TopSpeed => topSpeed;
        public float LaneOffset => laneOffset;
        public float ShortcutChance => shortcutChance;

        public static AIDriverProfile Create(string displayName, Color paint, float cornerGrip, float braking, float topSpeed,
            float laneOffset, float shortcutChance)
        {
            var profile = CreateInstance<AIDriverProfile>();
            profile.name = displayName;
            profile.displayName = displayName;
            profile.paint = paint;
            profile.cornerGrip = cornerGrip;
            profile.braking = braking;
            profile.topSpeed = topSpeed;
            profile.laneOffset = laneOffset;
            profile.shortcutChance = shortcutChance;
            return profile;
        }
    }
}
