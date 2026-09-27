using UnityEngine;

namespace RallyX.VFX
{
    /// <summary>
    /// Designer-tunable tyre, impact, landing, exhaust and brake effects shared by every car, how far from the camera
    /// they are made, and the ambient dust around the camera. Read live, so edits in Play Mode apply at once (particle
    /// budgets and materials apply to cars spawned afterwards).
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Vehicle Effects Config", fileName = "VehicleEffects")]
    public sealed class VehicleEffectsConfig : ScriptableObject
    {
        [Header("Materials")]
        [Tooltip("Soft puffs: tyre smoke, dust, exhaust.")]
        [SerializeField] private Material puffMaterial;
        [Tooltip("Stones, mud and clods thrown by the wheels.")]
        [SerializeField] private Material debrisMaterial;
        [Tooltip("Additive: sparks and backfire flashes.")]
        [SerializeField] private Material sparkMaterial;
        [SerializeField] private Material skidMaterial;

        [Header("When a Tyre Counts as Sliding")]
        [Tooltip("Sideways tyre speed where slide effects start, m/s. Normal cornering stays below it.")]
        [SerializeField, Min(0f)] private float slideStart = 1.2f;
        [Tooltip("Sideways tyre speed where slide effects are full, m/s.")]
        [SerializeField, Min(0.1f)] private float slideFull = 6f;
        [Tooltip("Speed at which a wheel locked by the handbrake makes full effects, m/s.")]
        [SerializeField, Min(0.1f)] private float lockedFullSpeed = 15f;
        [Tooltip("How much hard braking counts as sliding (0..1), from the brake speed upwards.")]
        [SerializeField, Range(0f, 1f)] private float brakeEffect = 0.4f;
        [Tooltip("Speed above which braking makes effects, m/s.")]
        [SerializeField, Min(0f)] private float brakeEffectSpeed = 8f;
        [Tooltip("Spray from the driven wheels pulling away on loose ground, fading out by the launch speed.")]
        [SerializeField, Range(0f, 2f)] private float launchSpray = 1f;
        [Tooltip("Speed by which the pull-away spray has faded, m/s.")]
        [SerializeField, Min(0.1f)] private float launchSpeed = 12f;

        [Header("Tyre Smoke and Dust")]
        [Tooltip("Puffs per second per wheel at full slide (tyre smoke on asphalt, dust on dirt and sand).")]
        [SerializeField, Min(0f)] private float puffRate = 36f;
        [Tooltip("Dust trailed by a wheel rolling on dusty ground is full at this speed, km/h.")]
        [SerializeField, Min(1f)] private float rollingSprayFullSpeedKmh = 90f;
        [SerializeField] private Vector2 puffSize = new Vector2(1.1f, 3f);
        [SerializeField] private Vector2 puffLifetime = new Vector2(1.2f, 2.6f);
        [Tooltip("Opacity of a puff at full slide.")]
        [SerializeField, Range(0f, 1f)] private float puffOpacity = 0.5f;
        [Tooltip("How much of the car's velocity a puff keeps (0..1).")]
        [SerializeField, Range(0f, 1f)] private float puffInherit = 0.25f;
        [Tooltip("Slowing of puffs in the air, 1/s.")]
        [SerializeField, Min(0f)] private float puffDrag = 1.6f;

        [Header("Thrown Debris")]
        [Tooltip("Stones, clumps or clods per second per wheel at full slide, by what the surface throws: gravel, mud, grass.")]
        [SerializeField, Min(0f)] private float gravelRate = 55f;
        [SerializeField, Min(0f)] private float mudRate = 40f;
        [SerializeField, Min(0f)] private float grassRate = 26f;
        [SerializeField] private Vector2 debrisSize = new Vector2(0.05f, 0.13f);
        [SerializeField] private Vector2 mudSize = new Vector2(0.08f, 0.22f);
        [SerializeField] private Vector2 debrisLifetime = new Vector2(0.5f, 1.1f);
        [Tooltip("Speed the wheels throw debris backwards at full slide, m/s.")]
        [SerializeField, Min(0f)] private float debrisThrowSpeed = 6f;

        [Header("Sparks")]
        [Tooltip("Change of speed from a hit that makes sparks, m/s.")]
        [SerializeField, Min(0.1f)] private float sparkImpactSpeed = 2.5f;
        [SerializeField] private Vector2Int sparkBurst = new Vector2Int(10, 45);
        [Tooltip("Hit (change of speed, m/s) that gives the largest burst.")]
        [SerializeField, Min(0.1f)] private float sparkFullImpactSpeed = 10f;
        [Tooltip("Sliding speed along a wall or car where scraping makes sparks, m/s.")]
        [SerializeField, Min(0f)] private float scrapeSpeed = 3f;
        [Tooltip("Sparks per second while scraping at 10 m/s.")]
        [SerializeField, Min(0f)] private float scrapeRate = 70f;
        [SerializeField] private Vector2 sparkLifetime = new Vector2(0.18f, 0.5f);
        [SerializeField, Min(0f)] private float sparkSpeed = 7f;
        [SerializeField, ColorUsage(false, true)] private Color sparkColor = new Color(1f, 0.62f, 0.2f) * 3f;

        [Header("Landings")]
        [Tooltip("Air time before a landing throws up dust, seconds.")]
        [SerializeField, Min(0f)] private float landingAirTime = 0.3f;
        [Tooltip("Landing speed (downwards) that makes the biggest dust burst, m/s.")]
        [SerializeField, Min(0.1f)] private float landingFullSpeed = 7f;
        [Tooltip("Puffs per wheel for the softest and hardest landing.")]
        [SerializeField] private Vector2Int landingPuffs = new Vector2Int(2, 7);

        [Header("Skid Marks")]
        [SerializeField, Min(0.05f)] private float markWidth = 0.24f;
        [Tooltip("Slide amount (0..1) from which the tyres leave marks.")]
        [SerializeField, Range(0f, 1f)] private float markThreshold = 0.2f;
        [Tooltip("Length of each piece of a mark, metres. Longer uses fewer pieces.")]
        [SerializeField, Min(0.05f)] private float markSegmentLength = 0.4f;
        [Tooltip("Mark pieces kept per car; the oldest are reused.")]
        [SerializeField, Range(64, 4096)] private int markCapacity = 1024;

        [Header("Exhaust")]
        [SerializeField, Min(0f)] private float exhaustIdleRate = 3f;
        [SerializeField, Min(0f)] private float exhaustThrottleRate = 12f;
        [SerializeField, Range(0f, 1f)] private float exhaustOpacity = 0.18f;
        [SerializeField] private Vector2 exhaustSize = new Vector2(0.2f, 0.7f);
        [Tooltip("Share of top speed above which lifting off the throttle can pop a backfire.")]
        [SerializeField, Range(0f, 1f)] private float backfireSpeed = 0.5f;
        [Tooltip("Chance a lift-off at speed backfires.")]
        [SerializeField, Range(0f, 1f)] private float backfireChance = 0.6f;
        [SerializeField, ColorUsage(false, true)] private Color backfireColor = new Color(1f, 0.45f, 0.12f) * 4f;

        [Header("Brake Glow")]
        [Tooltip("Colour the brake discs glow when hot. Black turns the glow off.")]
        [SerializeField, ColorUsage(false, true)] private Color brakeGlowColor = new Color(1f, 0.26f, 0.04f) * 2.5f;
        [Tooltip("Heat a disc gains per second of full braking from 100 km/h (1 = glowing fully). Heat grows with speed.")]
        [SerializeField, Min(0f)] private float brakeHeating = 0.5f;
        [Tooltip("Heat a disc loses per second.")]
        [SerializeField, Min(0f)] private float brakeCooling = 0.2f;
        [Tooltip("Heat (0..1) at which a disc starts to glow, so only long, hard stops make it glow.")]
        [SerializeField, Range(0f, 1f)] private float brakeGlowStart = 0.35f;

        [Header("Distance From the Camera")]
        [Tooltip("Within this distance a car makes its tyre and exhaust effects at the full rate, metres.")]
        [SerializeField, Min(1f)] private float fullDetailDistance = 60f;
        [Tooltip("Beyond this distance a car makes no tyre or exhaust effects or skid marks, metres; in between the rate " +
                 "thins out. Sparks from hits are always made.")]
        [SerializeField, Min(1f)] private float cullDistance = 170f;

        [Header("Ambient Dust")]
        [Tooltip("Faint dust drifting through the air around the camera on dusty ground (the car it follows on dirt, gravel " +
                 "or sand), puffs per second on the dustiest ground. 0 = off.")]
        [SerializeField, Min(0f)] private float ambientDustRate = 4f;
        [SerializeField, Range(0f, 1f)] private float ambientDustOpacity = 0.1f;
        [SerializeField] private Vector2 ambientDustSize = new Vector2(3f, 7f);
        [SerializeField] private Vector2 ambientDustLifetime = new Vector2(4f, 7f);
        [Tooltip("How far from the camera the dust appears, metres.")]
        [SerializeField, Min(2f)] private float ambientDustRange = 24f;
        [Tooltip("Speed the dust drifts on the breeze, m/s.")]
        [SerializeField, Min(0f)] private float ambientDustDrift = 0.8f;
        [SerializeField, Range(1, 256)] private int maxAmbientDust = 40;

        [Header("Budgets")]
        [Tooltip("Most particles alive per car, per kind: puffs, debris, sparks, exhaust.")]
        [SerializeField] private int maxPuffs = 260;
        [SerializeField] private int maxDebris = 180;
        [SerializeField] private int maxSparks = 90;
        [SerializeField] private int maxExhaust = 50;

        public Material PuffMaterial => puffMaterial;
        public Material DebrisMaterial => debrisMaterial;
        public Material SparkMaterial => sparkMaterial;
        public Material SkidMaterial => skidMaterial;

        public float SlideStart => slideStart;
        public float SlideFull => Mathf.Max(slideFull, slideStart + 0.1f);
        public float LockedFullSpeed => lockedFullSpeed;
        public float BrakeEffect => brakeEffect;
        public float BrakeEffectSpeed => brakeEffectSpeed;
        public float LaunchSpray => launchSpray;
        public float LaunchSpeed => launchSpeed;

        public float PuffRate => puffRate;
        public float RollingSprayFullSpeed => rollingSprayFullSpeedKmh / 3.6f;
        public Vector2 PuffSize => puffSize;
        public Vector2 PuffLifetime => puffLifetime;
        public float PuffOpacity => puffOpacity;
        public float PuffInherit => puffInherit;
        public float PuffDrag => puffDrag;

        public float GravelRate => gravelRate;
        public float MudRate => mudRate;
        public float GrassRate => grassRate;
        public Vector2 DebrisSize => debrisSize;
        public Vector2 MudSize => mudSize;
        public Vector2 DebrisLifetime => debrisLifetime;
        public float DebrisThrowSpeed => debrisThrowSpeed;

        public float SparkImpactSpeed => sparkImpactSpeed;
        public Vector2Int SparkBurst => sparkBurst;
        public float SparkFullImpactSpeed => Mathf.Max(sparkFullImpactSpeed, sparkImpactSpeed + 0.1f);
        public float ScrapeSpeed => scrapeSpeed;
        public float ScrapeRate => scrapeRate;
        public Vector2 SparkLifetime => sparkLifetime;
        public float SparkSpeed => sparkSpeed;
        public Color SparkColor => sparkColor;

        public float LandingAirTime => landingAirTime;
        public float LandingFullSpeed => landingFullSpeed;
        public Vector2Int LandingPuffs => landingPuffs;

        public float MarkWidth => markWidth;
        public float MarkThreshold => markThreshold;
        public float MarkSegmentLength => markSegmentLength;
        public int MarkCapacity => markCapacity;

        public float ExhaustIdleRate => exhaustIdleRate;
        public float ExhaustThrottleRate => exhaustThrottleRate;
        public float ExhaustOpacity => exhaustOpacity;
        public Vector2 ExhaustSize => exhaustSize;
        public float BackfireSpeed => backfireSpeed;
        public float BackfireChance => backfireChance;
        public Color BackfireColor => backfireColor;

        public Color BrakeGlowColor => brakeGlowColor;
        public float BrakeHeating => brakeHeating;
        public float BrakeCooling => brakeCooling;
        public float BrakeGlowStart => brakeGlowStart;

        public float FullDetailDistance => fullDetailDistance;
        public float CullDistance => Mathf.Max(cullDistance, fullDetailDistance + 1f);

        public float AmbientDustRate => ambientDustRate;
        public float AmbientDustOpacity => ambientDustOpacity;
        public Vector2 AmbientDustSize => ambientDustSize;
        public Vector2 AmbientDustLifetime => ambientDustLifetime;
        public float AmbientDustRange => ambientDustRange;
        public float AmbientDustDrift => ambientDustDrift;
        public int MaxAmbientDust => maxAmbientDust;

        public int MaxPuffs => Mathf.Max(1, maxPuffs);
        public int MaxDebris => Mathf.Max(1, maxDebris);
        public int MaxSparks => Mathf.Max(1, maxSparks);
        public int MaxExhaust => Mathf.Max(1, maxExhaust);

        /// <summary>Sets the materials the builder made.</summary>
        public void SetMaterials(Material puff, Material debris, Material spark, Material skid)
        {
            puffMaterial = puff;
            debrisMaterial = debris;
            sparkMaterial = spark;
            skidMaterial = skid;
        }
    }
}
