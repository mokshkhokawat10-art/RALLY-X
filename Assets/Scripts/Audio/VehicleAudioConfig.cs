using UnityEngine;

namespace RallyX.Audio
{
    /// <summary>
    /// How one car sounds (<see cref="VehicleAudio"/>): its engine (two recorded layers pitched to the revs, the rev range
    /// and gearing of the sound-only gearbox), gear changes and backfires, the tyre sounds used where a surface has none of
    /// its own, wind, impacts, scraping and landings, and how far it carries. Read live, so edits in Play Mode apply at once.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Vehicle Audio Config", fileName = "VehicleAudio")]
    public sealed class VehicleAudioConfig : ScriptableObject
    {
        [Header("Engine")]
        [Tooltip("Low-revs engine loop, and the revs it was recorded at: it plays at pitch revs / recorded revs.")]
        [SerializeField] private AudioClip engineLow;
        [SerializeField, Min(100f)] private float engineLowRpm = 2000f;

        [Tooltip("High-revs engine loop and the revs it was recorded at.")]
        [SerializeField] private AudioClip engineHigh;
        [SerializeField, Min(100f)] private float engineHighRpm = 5000f;

        [Tooltip("Share of the rev range (0 = idle, 1 = redline) over which the low layer crossfades into the high one.")]
        [SerializeField] private Vector2 layerBlend = new Vector2(0.3f, 0.7f);

        [SerializeField, Range(0f, 2f)] private float engineVolume = 0.8f;

        [Tooltip("Engine level off the throttle, as a share of its level on it.")]
        [SerializeField, Range(0f, 1f)] private float offThrottleVolume = 0.45f;

        [Header("Revs and Gears (sound only)")]
        [SerializeField, Min(100f)] private float idleRpm = 900f;
        [SerializeField, Min(500f)] private float redlineRpm = 7200f;

        [Tooltip("Revs the engine holds while the clutch slips pulling away at full throttle.")]
        [SerializeField, Min(100f)] private float launchRpm = 3800f;

        [Tooltip("Top speed of each gear at the redline, as a share of the car's top speed, lowest gear first. The last should be 1.")]
        [SerializeField] private float[] gearTopSpeeds = { 0.27f, 0.43f, 0.6f, 0.79f, 1f };

        [Tooltip("Share of a gear's top speed at which it changes up.")]
        [SerializeField, Range(0.5f, 1f)] private float upshiftAt = 0.96f;

        [Tooltip("Share of the redline the next gear down would give below which it changes down (a blip of revs).")]
        [SerializeField, Range(0.2f, 0.95f)] private float downshiftAt = 0.68f;

        [Tooltip("Seconds a gear change cuts the throttle.")]
        [SerializeField, Min(0f)] private float shiftTime = 0.16f;

        [Tooltip("How fast the revs rise and fall, per second.")]
        [SerializeField, Min(100f)] private float rpmRiseRate = 9000f;
        [SerializeField, Min(100f)] private float rpmFallRate = 5500f;

        [Tooltip("How far the rev limiter knocks the revs back, rpm.")]
        [SerializeField, Min(0f)] private float limiterDrop = 300f;

        [SerializeField] private SoundCue gearShift;
        [SerializeField] private SoundCue backfire;

        [Header("Tyres")]
        [Tooltip("Rolling tyre noise where the surface has none of its own.")]
        [SerializeField] private LoopSound defaultRolling;

        [Tooltip("Sliding tyre noise where the surface has none of its own.")]
        [SerializeField] private LoopSound defaultSlide;

        [SerializeField, Range(0f, 2f)] private float tyreVolume = 1f;

        [Tooltip("Speed at which rolling noise is full, km/h.")]
        [SerializeField, Min(1f)] private float rollingFullSpeedKmh = 110f;

        [Tooltip("Sideways tyre speed where slide sounds start and where they are full, m/s. Wheelspin and locked wheels count too.")]
        [SerializeField] private Vector2 slideSpeeds = new Vector2(1.2f, 6f);

        [Header("Wind (the car the camera follows)")]
        [SerializeField] private LoopSound wind;

        [Tooltip("Share of top speed where wind noise starts; it is full at top speed.")]
        [SerializeField, Range(0f, 1f)] private float windStart = 0.25f;

        [Tooltip("Wind level in the air, as a multiple of its level at the same speed on the ground.")]
        [SerializeField, Min(0f)] private float airborneWind = 1.5f;

        [Header("Impacts")]
        [SerializeField] private SoundCue lightImpact;
        [SerializeField] private SoundCue heavyImpact;

        [Tooltip("Change of speed from a hit, m/s: the least that makes a sound, where the heavy sound takes over, and full level.")]
        [SerializeField] private Vector3 impactSpeeds = new Vector3(1.2f, 6f, 12f);

        [Tooltip("Scraping along walls, rails and other cars.")]
        [SerializeField] private LoopSound scrape;

        [Tooltip("Sliding speed along what the car scrapes at which the scrape is full, m/s.")]
        [SerializeField, Min(0.1f)] private float scrapeFullSpeed = 12f;

        [Header("Landings")]
        [SerializeField] private SoundCue landing;

        [Tooltip("Shortest time in the air that makes a landing sound, seconds.")]
        [SerializeField, Min(0f)] private float landingMinAirTime = 0.2f;

        [Tooltip("Downward speed at landing that makes the loudest thump, m/s.")]
        [SerializeField, Min(0.1f)] private float landingFullSpeed = 8f;

        [Header("3D")]
        [Tooltip("Within this distance a car is at full level, metres.")]
        [SerializeField, Min(0.1f)] private float minDistance = 7f;

        [Tooltip("Beyond this distance a car is silent and its sounds are stopped, metres.")]
        [SerializeField, Min(1f)] private float maxDistance = 140f;

        [Tooltip("Pitch shift of other cars going by (0 = none). The car the camera follows has none.")]
        [SerializeField, Range(0f, 2f)] private float doppler = 0.5f;

        public AudioClip EngineLow => engineLow;
        public float EngineLowRpm => engineLowRpm;
        public AudioClip EngineHigh => engineHigh;
        public float EngineHighRpm => engineHighRpm;
        public Vector2 LayerBlend => layerBlend;
        public float EngineVolume => engineVolume;
        public float OffThrottleVolume => offThrottleVolume;

        public float IdleRpm => idleRpm;
        public float RedlineRpm => Mathf.Max(redlineRpm, idleRpm + 100f);
        public float LaunchRpm => launchRpm;
        public float[] GearTopSpeeds => gearTopSpeeds;
        public float UpshiftAt => upshiftAt;
        public float DownshiftAt => downshiftAt;
        public float ShiftTime => shiftTime;
        public float RpmRiseRate => rpmRiseRate;
        public float RpmFallRate => rpmFallRate;
        public float LimiterDrop => limiterDrop;
        public SoundCue GearShift => gearShift;
        public SoundCue Backfire => backfire;

        public LoopSound DefaultRolling => defaultRolling;
        public LoopSound DefaultSlide => defaultSlide;
        public float TyreVolume => tyreVolume;
        public float RollingFullSpeed => rollingFullSpeedKmh / 3.6f;
        public Vector2 SlideSpeeds => slideSpeeds;

        public LoopSound Wind => wind;
        public float WindStart => windStart;
        public float AirborneWind => airborneWind;

        public SoundCue LightImpact => lightImpact;
        public SoundCue HeavyImpact => heavyImpact;
        public Vector3 ImpactSpeeds => impactSpeeds;
        public LoopSound Scrape => scrape;
        public float ScrapeFullSpeed => scrapeFullSpeed;

        public SoundCue Landing => landing;
        public float LandingMinAirTime => landingMinAirTime;
        public float LandingFullSpeed => landingFullSpeed;

        public float MinDistance => minDistance;
        public float MaxDistance => Mathf.Max(maxDistance, minDistance + 1f);
        public float Doppler => doppler;

#if UNITY_EDITOR
        /// <summary>Sets the sounds, for the content builder. Tuning values are left alone.</summary>
        public void SetSounds(AudioClip newEngineLow, AudioClip newEngineHigh, SoundCue newGearShift, SoundCue newBackfire,
            LoopSound newRolling, LoopSound newSlide, LoopSound newWind, SoundCue newLightImpact, SoundCue newHeavyImpact,
            LoopSound newScrape, SoundCue newLanding)
        {
            engineLow = newEngineLow;
            engineHigh = newEngineHigh;
            gearShift = newGearShift;
            backfire = newBackfire;
            defaultRolling = newRolling;
            defaultSlide = newSlide;
            wind = newWind;
            lightImpact = newLightImpact;
            heavyImpact = newHeavyImpact;
            scrape = newScrape;
            landing = newLanding;
        }
#endif
    }
}
