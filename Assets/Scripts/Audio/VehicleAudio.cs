using RallyX.Tracks;
using RallyX.Vehicle;
using RallyX.VFX;
using UnityEngine;

namespace RallyX.Audio
{
    /// <summary>
    /// A car's sounds, driven by what it is actually doing: the engine (two layers pitched to the revs of a sound-only
    /// gearbox, <see cref="EngineRevs"/>, louder on the throttle), gear changes, backfires, tyre noise rolling and
    /// sliding with the sounds of the surface under the wheels, wind at speed and in the air for the car the camera
    /// follows, impacts, scraping and landings. Every loop is its own 3D source that is stopped while silent, and a car
    /// beyond the config's range is silent altogether. Levels follow the player's vehicle volume. Sound only: nothing
    /// here changes how the car drives.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleAudio : MonoBehaviour
    {
        private const float ImpactCooldown = 0.08f;
        private const float ScrapeHold = 0.12f;

        // Seconds between looks for the listener while there is none (it is usually on the camera).
        private const float ListenerSearchInterval = 1f;

        [SerializeField] private VehicleAudioConfig config;

        private readonly EngineRevs engine = new EngineRevs();
        private VehicleController controller;
        private VehicleEffects effects;
        private LoopVoice engineLow;
        private LoopVoice engineHigh;
        private LoopVoice rolling;
        private LoopVoice slide;
        private LoopVoice wind;
        private LoopVoice scrape;
        private AudioSource oneShots;
        private Transform listener;
        private float nextListenerSearch;
        private bool listenerCar;
        private bool silenced;
        private float lastImpactTime = -1f;
        private float scrapeAmount;
        private float scrapeHoldLeft;

        public VehicleAudioConfig Config => config;

        /// <summary>The sound-only engine: revs, gear and load.</summary>
        public EngineRevs Engine => engine;

        /// <summary>True once the sounds are set up (on the first frame the car is active).</summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// The car the camera follows: it gets wind noise, no Doppler shift and priority over the other cars' sounds.
        /// </summary>
        public bool IsListenerCar
        {
            get => listenerCar;
            set
            {
                listenerCar = value;
                if (IsReady)
                    ApplySpatial();
            }
        }

        public void Configure(VehicleAudioConfig newConfig) => config = newConfig;

        private void Awake()
        {
            controller = GetComponent<VehicleController>();
            effects = GetComponent<VehicleEffects>();
        }

        private void OnEnable()
        {
            controller.Impact += OnImpact;
            controller.Landed += OnLanded;
            engine.GearChanged += OnGearChanged;
            if (effects != null)
                effects.Backfired += OnBackfired;
        }

        private void OnDisable()
        {
            controller.Impact -= OnImpact;
            controller.Landed -= OnLanded;
            engine.GearChanged -= OnGearChanged;
            if (effects != null)
                effects.Backfired -= OnBackfired;
            SilenceAll();
        }

        // Set up on the first frame rather than in Awake, so a car put on display (garage) that switches this off stays silent.
        private void Start()
        {
            if (config == null)
            {
                Debug.LogWarning($"{name}: VehicleAudio has no VehicleAudioConfig; the car is silent.", this);
                enabled = false;
                return;
            }

            engineLow = new LoopVoice(transform, "EngineLow", 0);
            engineHigh = new LoopVoice(transform, "EngineHigh", 0);
            rolling = new LoopVoice(transform, "Rolling", 0);
            slide = new LoopVoice(transform, "Slide", 0);
            wind = new LoopVoice(transform, "Wind", 0);
            scrape = new LoopVoice(transform, "Scrape", 0);

            var oneShotObject = new GameObject("OneShots");
            oneShotObject.transform.SetParent(transform, false);
            oneShots = oneShotObject.AddComponent<AudioSource>();
            oneShots.playOnAwake = false;

            engine.Reset(config);
            IsReady = true;
            ApplySpatial();
        }

        private void ApplySpatial()
        {
            float doppler = listenerCar ? 0f : config.Doppler;
            int priority = listenerCar ? 40 : 140;
            foreach (var voice in Voices())
            {
                voice.SetSpatial(config.MinDistance, config.MaxDistance, doppler);
                voice.Source.priority = priority;
            }

            oneShots.spatialBlend = 1f;
            oneShots.rolloffMode = AudioRolloffMode.Logarithmic;
            oneShots.minDistance = config.MinDistance;
            oneShots.maxDistance = config.MaxDistance;
            oneShots.dopplerLevel = 0f;
            oneShots.priority = priority - 10;
        }

        private LoopVoice[] Voices() => new[] { engineLow, engineHigh, rolling, slide, wind, scrape };

        private void Update()
        {
            if (!IsReady || !controller.isActiveAndEnabled || config == null)
            {
                SilenceAll();
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                return;

            if (listener == null && Time.unscaledTime >= nextListenerSearch)
            {
                listener = FindListener();
                nextListenerSearch = Time.unscaledTime + ListenerSearchInterval;
            }

            if (listener != null && (listener.position - transform.position).sqrMagnitude > config.MaxDistance * config.MaxDistance)
            {
                SilenceAll();
                return;
            }

            silenced = false;
            float volume = GameAudio.Volume(AudioChannel.Vehicle);
            UpdateEngine(volume, deltaTime);
            UpdateTyres(volume, deltaTime);
            UpdateWind(volume, deltaTime);
            UpdateScrape(volume, deltaTime);
        }

        private static Transform FindListener()
        {
            var found = FindAnyObjectByType<AudioListener>();
            return found != null ? found.transform : null;
        }

        private void SilenceAll()
        {
            if (silenced || !IsReady)
                return;

            silenced = true;
            foreach (var voice in Voices())
                voice.Silence();
        }

        // ---------------------------------------------------------------- engine

        private void UpdateEngine(float volume, float deltaTime)
        {
            // On the grid the driver's own input revs the engine, although the car is held.
            bool held = controller.InputLocked && Mathf.Abs(controller.ForwardSpeed) < 1f;
            var input = held ? controller.CurrentInput : controller.AppliedInput;
            float throttle = controller.IsReversing ? Mathf.Max(input.Throttle, input.Brake) : input.Throttle;
            var carConfig = controller.Config;
            engine.Step(config, controller.ForwardSpeed, carConfig.MaxSpeed, carConfig.MaxReverseSpeed, throttle,
                held || !controller.IsGrounded, DrivenWheelSpin(), controller.IsReversing, deltaTime);

            float revs = Mathf.InverseLerp(config.IdleRpm, config.RedlineRpm, engine.Rpm);
            float high = SmoothStep(config.LayerBlend.x, config.LayerBlend.y, revs);
            float level = config.EngineVolume * Mathf.Lerp(config.OffThrottleVolume, 1f, engine.Load) * volume;
            engineLow.Play(config.EngineLow, level * (1f - high), engine.Rpm / config.EngineLowRpm, 30f, deltaTime);
            engineHigh.Play(config.EngineHigh, level * high, engine.Rpm / config.EngineHighRpm, 30f, deltaTime);
        }

        private float DrivenWheelSpin()
        {
            float spin = 0f;
            var wheels = controller.Wheels;
            for (int i = 0; i < wheels.Count; i++)
                spin = Mathf.Max(spin, wheels[i].WheelSpin);
            return spin;
        }

        // ---------------------------------------------------------------- tyres

        private void UpdateTyres(float volume, float deltaTime)
        {
            if (!controller.IsGrounded)
            {
                rolling.Play(rolling.Source.clip, 0f, rolling.Source.pitch, 8f, deltaTime);
                slide.Play(slide.Source.clip, 0f, slide.Source.pitch, 8f, deltaTime);
                return;
            }

            // Rolling noise from the surface most wheels are on; slide noise from the wheel working hardest.
            var wheels = controller.Wheels;
            SurfaceType rollingSurface = null;
            int rollingCount = 0;
            SurfaceType slideSurface = null;
            float slideAmount = 0f;
            int grounded = 0;
            for (int i = 0; i < wheels.Count; i++)
            {
                var wheel = wheels[i];
                if (!wheel.IsGrounded)
                    continue;

                grounded++;
                int count = CountOn(wheels, wheel.SurfaceType);
                if (count > rollingCount)
                {
                    rollingCount = count;
                    rollingSurface = wheel.SurfaceType;
                }

                float amount = Mathf.Max(Mathf.InverseLerp(config.SlideSpeeds.x, config.SlideSpeeds.y, wheel.SidewaysSpeed),
                    wheel.WheelSpin, wheel.Locked ? 0.8f : 0f);
                if (amount > slideAmount)
                {
                    slideAmount = amount;
                    slideSurface = wheel.SurfaceType;
                }
            }

            float speed = Mathf.Abs(controller.ForwardSpeed);
            float speedShare = Mathf.Clamp01(speed / config.RollingFullSpeed);
            float onGround = grounded / (float)Mathf.Max(1, wheels.Count);
            var rollingSound = SoundFor(rollingSurface, rolling: true);
            rolling.Play(rollingSound.Clip, rollingSound.Volume * config.TyreVolume * Mathf.Sqrt(speedShare) * onGround * volume,
                rollingSound.Pitch * Mathf.Lerp(0.8f, 1.2f, speedShare), 6f, deltaTime);

            var slideSound = SoundFor(slideSurface, rolling: false);
            slide.Play(slideSound.Clip, slideSound.Volume * config.TyreVolume * slideAmount * volume,
                slideSound.Pitch * Mathf.Lerp(0.94f, 1.08f, slideAmount), 10f, deltaTime);
        }

        private static int CountOn(System.Collections.Generic.IReadOnlyList<VehicleWheel> wheels, SurfaceType surface)
        {
            int count = 0;
            for (int i = 0; i < wheels.Count; i++)
            {
                if (wheels[i].IsGrounded && wheels[i].SurfaceType == surface)
                    count++;
            }

            return count;
        }

        private LoopSound SoundFor(SurfaceType surface, bool rolling)
        {
            if (surface != null)
            {
                var own = rolling ? surface.RollingSound : surface.SlideSound;
                if (own.IsSet)
                    return own;
            }

            return rolling ? config.DefaultRolling : config.DefaultSlide;
        }

        // ---------------------------------------------------------------- wind and scraping

        private void UpdateWind(float volume, float deltaTime)
        {
            var sound = config.Wind;
            if (!listenerCar || !sound.IsSet)
            {
                wind.Play(sound.Clip, 0f, sound.Pitch, 4f, deltaTime);
                return;
            }

            float topSpeed = controller.Config.MaxSpeed;
            float amount = Mathf.InverseLerp(config.WindStart * topSpeed, topSpeed, controller.Body.linearVelocity.magnitude);
            if (!controller.IsGrounded)
                amount *= config.AirborneWind;
            wind.Play(sound.Clip, sound.Volume * amount * amount * volume, sound.Pitch * Mathf.Lerp(0.85f, 1.25f, Mathf.Clamp01(amount)),
                2f, deltaTime);
        }

        private void UpdateScrape(float volume, float deltaTime)
        {
            scrapeHoldLeft -= deltaTime;
            if (scrapeHoldLeft <= 0f)
                scrapeAmount = 0f;

            var sound = config.Scrape;
            scrape.Play(sound.Clip, sound.Volume * scrapeAmount * volume, sound.Pitch * Mathf.Lerp(0.9f, 1.15f, scrapeAmount), 12f, deltaTime);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!IsReady || !isActiveAndEnabled || collision.contactCount == 0)
                return;

            // Sliding along something, not resting on the ground: the contact must be steep and the car moving along it.
            var contact = collision.GetContact(0);
            if (contact.normal.y > 0.6f)
                return;

            float sliding = Vector3.ProjectOnPlane(collision.relativeVelocity, contact.normal).magnitude;
            float amount = Mathf.InverseLerp(1.5f, config.ScrapeFullSpeed, sliding);
            if (amount <= 0f)
                return;

            scrapeAmount = Mathf.Max(scrapeAmount, amount);
            scrapeHoldLeft = ScrapeHold;
        }

        // ---------------------------------------------------------------- one-shots

        private void OnImpact(float impulse, Vector3 point)
        {
            if (!IsReady || !isActiveAndEnabled || Time.time - lastImpactTime < ImpactCooldown)
                return;

            float knock = impulse / controller.Body.mass;
            var speeds = config.ImpactSpeeds;
            if (knock < speeds.x)
                return;

            lastImpactTime = Time.time;
            float strength = Mathf.InverseLerp(speeds.x, speeds.z, knock);
            PlayOneShot(knock >= speeds.y ? config.HeavyImpact : config.LightImpact, Mathf.Lerp(0.35f, 1f, strength));
        }

        private void OnLanded(float airTime, float downSpeed)
        {
            if (!IsReady || !isActiveAndEnabled || airTime < config.LandingMinAirTime)
                return;

            PlayOneShot(config.Landing, Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(downSpeed / config.LandingFullSpeed)));
        }

        private void OnGearChanged(int from, int to)
        {
            // Changing up is a firm clunk; changing down, a softer one under the blip of revs.
            if (IsReady && isActiveAndEnabled && from > 0 && to > 0)
                PlayOneShot(config.GearShift, to > from ? 1f : 0.6f);
        }

        private void OnBackfired(Vector3 position)
        {
            if (IsReady && isActiveAndEnabled)
                PlayOneShot(config.Backfire, 1f);
        }

        private void PlayOneShot(SoundCue cue, float scale)
        {
            if (!cue.IsSet || silenced)
                return;

            oneShots.pitch = cue.PickPitch();
            oneShots.PlayOneShot(cue.Clip, cue.Volume * scale * GameAudio.Volume(AudioChannel.Vehicle));
        }

        private static float SmoothStep(float from, float to, float value)
        {
            float t = Mathf.InverseLerp(from, to, value);
            return t * t * (3f - 2f * t);
        }
    }
}
