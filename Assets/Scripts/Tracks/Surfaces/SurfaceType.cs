using UnityEngine;

namespace RallyX.Tracks
{
    /// <summary>What a surface throws up from under a sliding or spinning tyre.</summary>
    public enum SurfaceSpray
    {
        /// <summary>Nothing but tyre smoke, and only when the tyre slides hard (asphalt, bridge decks).</summary>
        TyreSmoke,

        /// <summary>A cloud of fine dust (dry dirt, sand).</summary>
        Dust,

        /// <summary>Flying stones and some dust (gravel).</summary>
        Gravel,

        /// <summary>Wet clumps and no dust (mud).</summary>
        Mud,

        /// <summary>Torn-up clods and a little dust (grass).</summary>
        Grass,
    }

    /// <summary>
    /// A kind of ground (asphalt, dirt, mud...) and how it changes vehicle handling and the effects the wheels make on it.
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

        [Tooltip("Share of the car's grip-by-slip drop felt when sliding. 1 = the full drop; below 1 the tyres dig in and keep " +
                 "more grip in a slide, so slides on this surface build up and recover progressively.")]
        [SerializeField, Range(0f, 1.5f)] private float slideFalloff = 1f;

        [Header("Effects")]
        [Tooltip("What sliding and spinning tyres throw up.")]
        [SerializeField] private SurfaceSpray spray = SurfaceSpray.TyreSmoke;

        [Tooltip("Colour of the dust, spray or smoke.")]
        [SerializeField] private Color sprayColor = new Color(0.82f, 0.82f, 0.8f, 1f);

        [Tooltip("Colour and strength (alpha) of the marks tyres leave when they slide.")]
        [SerializeField] private Color markColor = new Color(0.05f, 0.05f, 0.05f, 0.7f);

        [Tooltip("Dust or spray raised by a wheel just rolling at speed, 0..1 of the sliding amount. Dry dirt trails a dust cloud.")]
        [SerializeField, Range(0f, 1f)] private float rollingSpray;

        [Tooltip("How bumpy the surface looks from the car: body and camera vibration at speed. 0 = smooth.")]
        [SerializeField, Range(0f, 1f)] private float roughness;

        public string DisplayName => displayName;

        public SurfaceProperties Properties => new SurfaceProperties(grip, topSpeed, acceleration, rollingResistance, slideFalloff);

        public SurfaceSpray Spray => spray;
        public Color SprayColor => sprayColor;
        public Color MarkColor => markColor;
        public float RollingSpray => rollingSpray;
        public float Roughness => roughness;
    }
}
