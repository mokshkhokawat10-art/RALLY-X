using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.Rendering;

namespace RallyX.VFX
{
    /// <summary>
    /// A car's tyre, impact, landing, exhaust and brake effects, driven by what the car is actually doing: tyre smoke,
    /// dust, gravel, mud or clods from sliding, locked, spinning or hard-braking wheels (by the surface under each wheel),
    /// dust trailed on dry ground at speed, skid marks, sparks from hard knocks and scraping, dust on landing, exhaust
    /// smoke with the odd backfire on lift-off, and brake discs that glow after long, hard stops. Each kind of effect is
    /// one world-space particle system per car, fed with <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>,
    /// so nothing is created or destroyed while driving; a car far from the camera makes fewer effects and, beyond the
    /// config's cull distance, none. Visual only: nothing here changes how the car drives.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleEffects : MonoBehaviour
    {
        // Colours for ground without a surface asset.
        private static readonly Color DefaultSpray = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color DefaultMark = new Color(0.04f, 0.04f, 0.04f, 0.75f);
        private static readonly Color ExhaustColor = new Color(0.42f, 0.42f, 0.44f);
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // One copy of each brake disc material with emission switched on, shared by every car: the glow itself is set per
        // disc with a property block. The asset is left alone (a material with black emission may have the keyword stripped).
        private static readonly System.Collections.Generic.Dictionary<Material, Material> GlowingDiscs =
            new System.Collections.Generic.Dictionary<Material, Material>();

        // Speed at which braking heats the discs at the configured rate, m/s (100 km/h).
        private const float BrakeHeatSpeed = 100f / 3.6f;

        [SerializeField] private VehicleEffectsConfig config;

        [Tooltip("Exhaust outlets. Smoke and backfires leave along each one's forward (blue) axis.")]
        [SerializeField] private Transform[] exhausts = System.Array.Empty<Transform>();

        private VehicleController controller;
        private ParticleSystem puffs;
        private ParticleSystem debris;
        private ParticleSystem sparks;
        private ParticleSystem exhaustSmoke;
        private SkidMarks marks;
        private float[] puffCarry = System.Array.Empty<float>();
        private float[] debrisCarry = System.Array.Empty<float>();
        private float exhaustCarry;
        private float scrapeCarry;
        private float previousThrottle;
        private float backfireCooldown;
        private Collider lastCollider;
        private bool lastColliderSparks;
        private SurfaceType lastColliderSurface;
        private Renderer[] brakeDiscs = System.Array.Empty<Renderer>();
        private float[] brakeHeat = System.Array.Empty<float>();
        private float[] brakeGlowShown = System.Array.Empty<float>();
        private MaterialPropertyBlock brakeBlock;
        private Transform viewer;

        /// <summary>A backfire popped from the exhaust, at the first pipe's position. Sound listens for it.</summary>
        public event System.Action<Vector3> Backfired;

        public VehicleEffectsConfig Config => config;

        /// <summary>True once the effects are set up (on the first frame the car is active).</summary>
        public bool IsReady { get; private set; }

        /// <summary>Particles emitted since the car started, by kind. For tests and tuning.</summary>
        public int PuffsEmitted { get; private set; }
        public int DebrisEmitted { get; private set; }
        public int SparksEmitted { get; private set; }
        public int ExhaustEmitted { get; private set; }
        public int LandingBursts { get; private set; }
        public int Backfires { get; private set; }

        /// <summary>Skid mark pieces on the ground now (older ones are reused past the capacity).</summary>
        public int SkidMarkCount => marks?.Count ?? 0;

        /// <summary>How much of the full effect rate the car makes at its distance from the camera, 0..1.</summary>
        public float Detail { get; private set; } = 1f;

        /// <summary>A wheel's brake heat, 0 (cold) upwards; its disc glows above the config's glow start.</summary>
        public float BrakeHeat(int wheel) => wheel >= 0 && wheel < brakeHeat.Length ? brakeHeat[wheel] : 0f;

        public void Configure(VehicleEffectsConfig newConfig, Transform[] newExhausts)
        {
            config = newConfig;
            exhausts = newExhausts ?? System.Array.Empty<Transform>();
        }

        private void Awake() => controller = GetComponent<VehicleController>();

        private void OnEnable() => controller.Landed += OnLanded;

        private void OnDisable() => controller.Landed -= OnLanded;

        // Set up on the first frame rather than in Awake, so a car put on display (garage) that switches this off never builds it.
        private void Start()
        {
            if (config == null || config.PuffMaterial == null || config.DebrisMaterial == null || config.SparkMaterial == null)
            {
                Debug.LogWarning($"{name}: VehicleEffects has no complete VehicleEffectsConfig; the car has no tyre effects.", this);
                enabled = false;
                return;
            }

            int wheelCount = controller.Wheels.Count;
            puffCarry = new float[wheelCount];
            debrisCarry = new float[wheelCount];

            puffs = CreateSystem("Puffs", config.PuffMaterial, config.MaxPuffs, gravity: -0.02f, config.PuffDrag,
                new AnimationCurve(new Keyframe(0f, 0.45f, 0f, 2f), new Keyframe(1f, 1f)), Fade(0.12f, 0.35f), stretch: false, spin: true);
            debris = CreateSystem("Debris", config.DebrisMaterial, config.MaxDebris, gravity: 1f, drag: 0.4f, null, Fade(0f, 0.85f),
                stretch: false, spin: true);
            sparks = CreateSystem("Sparks", config.SparkMaterial, config.MaxSparks, gravity: 0.6f, drag: 1.2f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)), Fade(0f, 0.5f), stretch: true, spin: false);
            if (exhausts.Length > 0)
                exhaustSmoke = CreateSystem("Exhaust", config.PuffMaterial, config.MaxExhaust, gravity: -0.04f, drag: 1.5f,
                    new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1f)), Fade(0.1f, 0.3f), stretch: false, spin: true);

            if (config.SkidMaterial != null)
                marks = new SkidMarks($"SkidMarks_{name}", config.MarkCapacity, wheelCount, config.SkidMaterial, gameObject.scene);

            FindBrakeDiscs();
            IsReady = true;
        }

        private void OnDestroy() => marks?.Destroy();

        private void Update()
        {
            if (!IsReady || !controller.isActiveAndEnabled)
                return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                return;

            Vector3 velocity = controller.Body.linearVelocity;
            float speed = velocity.magnitude;
            var input = controller.AppliedInput;
            var wheels = controller.Wheels;
            UpdateBrakes(speed, input, deltaTime);

            // Fewer effects the further the car is from the camera, and none beyond the cull distance.
            Detail = ViewDetail();
            if (Detail <= 0f)
            {
                for (int i = 0; i < wheels.Count; i++)
                    marks?.Break(i);
                previousThrottle = input.Throttle;
                marks?.Upload();
                return;
            }

            float effectTime = deltaTime * Detail;
            for (int i = 0; i < wheels.Count; i++)
                UpdateWheel(i, wheels[i], velocity, speed, input, effectTime);

            UpdateExhaust(velocity, input.Throttle, effectTime);
            marks?.Upload();
        }

        private float ViewDetail()
        {
            if (viewer == null)
            {
                var view = Camera.main;
                if (view == null)
                    return 1f;
                viewer = view.transform;
            }

            float distance = Vector3.Distance(viewer.position, transform.position);
            return 1f - Mathf.InverseLerp(config.FullDetailDistance, config.CullDistance, distance);
        }

        // ---------------------------------------------------------------- brakes

        /// <summary>The disc part of each wheel's brake (its first material), which glows when hot.</summary>
        private void FindBrakeDiscs()
        {
            var wheels = controller.Wheels;
            brakeDiscs = new Renderer[wheels.Count];
            brakeHeat = new float[wheels.Count];
            brakeGlowShown = new float[wheels.Count];
            brakeBlock = new MaterialPropertyBlock();
            for (int i = 0; i < wheels.Count; i++)
            {
                var brake = wheels[i].transform.Find("Visual/Brake");
                var disc = brake != null ? brake.GetComponent<Renderer>() : null;
                if (disc == null || config.BrakeGlowColor.maxColorComponent <= 0f)
                    continue;

                var materials = disc.sharedMaterials;
                if (materials.Length == 0 || materials[0] == null)
                    continue;

                materials[0] = GlowingDisc(materials[0]);
                disc.sharedMaterials = materials;
                brakeDiscs[i] = disc;
            }
        }

        private static Material GlowingDisc(Material source)
        {
            if (GlowingDiscs.TryGetValue(source, out var glowing) && glowing != null)
                return glowing;

            glowing = new Material(source) { name = source.name + " (Glow)" };
            glowing.EnableKeyword("_EMISSION");
            glowing.SetColor(EmissionColor, Color.black);
            glowing.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            GlowingDiscs[source] = glowing;
            return glowing;
        }

        /// <summary>Hard braking at speed heats the discs (more on the axle doing more of the braking); they cool all the time.</summary>
        private void UpdateBrakes(float speed, VehicleInputState input, float deltaTime)
        {
            bool braking = input.Brake > 0f && !controller.IsReversing && controller.ForwardSpeed > 1f;
            float bias = controller.Config.BrakeBias;
            Color glowColor = config.BrakeGlowColor;
            var wheels = controller.Wheels;
            for (int i = 0; i < brakeDiscs.Length; i++)
            {
                float heating = braking
                    ? input.Brake * (speed / BrakeHeatSpeed) * (wheels[i].IsFront ? bias : 1f - bias) * 2f * config.BrakeHeating
                    : 0f;
                brakeHeat[i] = Mathf.Clamp(brakeHeat[i] + (heating - config.BrakeCooling) * deltaTime, 0f, 1.2f);

                var disc = brakeDiscs[i];
                if (disc == null)
                    continue;

                float glow = Mathf.InverseLerp(config.BrakeGlowStart, 1f, brakeHeat[i]);
                glow *= glow;
                if (Mathf.Abs(glow - brakeGlowShown[i]) < 0.01f && (glow > 0f || brakeGlowShown[i] == 0f))
                    continue;

                brakeGlowShown[i] = glow;
                disc.GetPropertyBlock(brakeBlock, 0);
                brakeBlock.SetColor(EmissionColor, glowColor * glow);
                disc.SetPropertyBlock(brakeBlock, 0);
            }
        }

        // ---------------------------------------------------------------- tyres

        private void UpdateWheel(int index, VehicleWheel wheel, Vector3 velocity, float speed, VehicleInputState input, float deltaTime)
        {
            if (!wheel.IsGrounded)
            {
                marks?.Break(index);
                return;
            }

            SurfaceType surface = wheel.SurfaceType;
            SurfaceSpray spray = surface != null ? surface.Spray : SurfaceSpray.TyreSmoke;
            float slide = SlideAmount(wheel, speed, input);
            float rolling = surface != null ? surface.RollingSpray * Mathf.Clamp01(speed / config.RollingSprayFullSpeed) : 0f;

            // Where the tyre meets the ground as drawn this frame (the car is interpolated between physics steps).
            Vector3 up = transform.up;
            Vector3 contact = wheel.transform.position - up * (wheel.SpringLength + controller.Config.WheelRadius);
            Vector3 back = -transform.forward;
            Color color = surface != null ? SurfacePalette.SprayColor(surface) : DefaultSpray;

            float puffAmount;
            float debrisAmount;
            float debrisRate;
            switch (spray)
            {
                case SurfaceSpray.Dust:
                    puffAmount = Mathf.Max(slide, rolling);
                    debrisAmount = 0f;
                    debrisRate = 0f;
                    break;
                case SurfaceSpray.Gravel:
                    puffAmount = Mathf.Max(slide * 0.6f, rolling);
                    debrisAmount = slide + rolling * 0.3f;
                    debrisRate = config.GravelRate;
                    break;
                case SurfaceSpray.Mud:
                    puffAmount = 0f;
                    debrisAmount = slide + rolling * 0.5f;
                    debrisRate = config.MudRate;
                    break;
                case SurfaceSpray.Grass:
                    puffAmount = slide * 0.3f;
                    debrisAmount = slide;
                    debrisRate = config.GrassRate;
                    break;
                default:
                    puffAmount = slide;
                    debrisAmount = 0f;
                    debrisRate = 0f;
                    break;
            }

            EmitPuffs(ref puffCarry[index], puffAmount, contact, velocity, back, color, deltaTime);
            EmitDebris(ref debrisCarry[index], Mathf.Clamp01(debrisAmount), debrisRate, spray, contact, velocity, back, speed, color, deltaTime);
            LayMark(index, wheel, surface, slide, contact, velocity);
        }

        /// <summary>
        /// How hard a tyre is working the ground, 0..1: sliding sideways, locked by the handbrake, spinning, braking hard,
        /// or pulling away on loose ground.
        /// </summary>
        private float SlideAmount(VehicleWheel wheel, float speed, VehicleInputState input)
        {
            float slide = Mathf.InverseLerp(config.SlideStart, config.SlideFull, wheel.SidewaysSpeed);
            float locked = wheel.Locked ? Mathf.Clamp01(speed / config.LockedFullSpeed) : 0f;
            float braking = input.Brake > 0f && !controller.IsReversing && controller.ForwardSpeed > 0f
                ? input.Brake * Mathf.InverseLerp(config.BrakeEffectSpeed, config.BrakeEffectSpeed + 20f, speed) * config.BrakeEffect
                : 0f;

            float driveShare = wheel.IsFront ? controller.Config.FrontDriveShare : 1f - controller.Config.FrontDriveShare;
            float looseness = Mathf.Clamp01((1f - wheel.Surface.Grip) * 2f);
            float launch = driveShare > 0.1f
                ? input.Throttle * (1f - Mathf.Clamp01(speed / config.LaunchSpeed)) * looseness * config.LaunchSpray
                : 0f;

            return Mathf.Clamp01(slide + wheel.WheelSpin + locked + braking + launch);
        }

        private void EmitPuffs(ref float carry, float amount, Vector3 position, Vector3 carVelocity, Vector3 back, Color color, float deltaTime)
        {
            if (amount <= 0.01f)
                return;

            carry += amount * config.PuffRate * deltaTime;
            int count = (int)carry;
            carry -= count;
            float opacity = config.PuffOpacity * Mathf.Lerp(0.45f, 1f, amount);
            for (int k = 0; k < count; k++)
            {
                var particle = new ParticleSystem.EmitParams
                {
                    position = position + Random.insideUnitSphere * 0.2f + Vector3.up * 0.25f,
                    velocity = carVelocity * config.PuffInherit + back * (1f + amount * 2f) + Random.insideUnitSphere * 1.2f
                               + Vector3.up * Random.Range(0.3f, 1.2f),
                    startSize = Mathf.Lerp(config.PuffSize.x, config.PuffSize.y, Random.value) * (0.6f + 0.4f * amount),
                    startLifetime = Random.Range(config.PuffLifetime.x, config.PuffLifetime.y),
                    rotation = Random.Range(0f, 360f),
                    startColor = Shade(color, Random.Range(0.88f, 1.08f), opacity),
                };
                puffs.Emit(particle, 1);
            }

            PuffsEmitted += count;
        }

        private void EmitDebris(ref float carry, float amount, float rate, SurfaceSpray spray, Vector3 position, Vector3 carVelocity,
            Vector3 back, float speed, Color color, float deltaTime)
        {
            if (amount <= 0.01f || rate <= 0f)
                return;

            carry += amount * rate * deltaTime;
            int count = (int)carry;
            carry -= count;
            Vector2 size = spray == SurfaceSpray.Mud ? config.MudSize : config.DebrisSize;
            Vector3 side = transform.right;
            for (int k = 0; k < count; k++)
            {
                // Stones vary in tone; clods mix the grass colour with earth.
                Color tone = spray == SurfaceSpray.Grass && Random.value < 0.5f
                    ? Color.Lerp(color, new Color(0.3f, 0.22f, 0.14f), 0.7f)
                    : Shade(color, Random.Range(0.6f, 1.15f), 1f);
                var particle = new ParticleSystem.EmitParams
                {
                    position = position + side * Random.Range(-0.12f, 0.12f) + Vector3.up * 0.1f,
                    velocity = carVelocity * 0.55f + back * (2f + amount * config.DebrisThrowSpeed + speed * 0.1f)
                               + Vector3.up * Random.Range(1.2f, 3.6f) + side * Random.Range(-1.5f, 1.5f),
                    startSize = Mathf.Lerp(size.x, size.y, Random.value),
                    startLifetime = Random.Range(config.DebrisLifetime.x, config.DebrisLifetime.y),
                    rotation = Random.Range(0f, 360f),
                    startColor = tone,
                };
                debris.Emit(particle, 1);
            }

            DebrisEmitted += count;
        }

        private void LayMark(int index, VehicleWheel wheel, SurfaceType surface, float slide, Vector3 contact, Vector3 velocity)
        {
            if (marks == null)
                return;

            if (slide < config.MarkThreshold)
            {
                marks.Break(index);
                return;
            }

            // Marks follow the way the tyre is travelling, which in a drift is not the way it points.
            Vector3 normal = wheel.Hit.normal;
            Vector3 direction = Vector3.ProjectOnPlane(velocity, normal);
            if (direction.sqrMagnitude < 0.25f)
                direction = Vector3.ProjectOnPlane(transform.forward, normal);

            Color color = surface != null ? SurfacePalette.MarkColor(surface) : DefaultMark;
            color.a *= Mathf.Clamp01(slide * 1.25f);
            marks.Add(index, contact, normal, direction.normalized, config.MarkWidth, color, config.MarkSegmentLength);
        }

        // ---------------------------------------------------------------- landings

        private void OnLanded(float airTime, float downSpeed)
        {
            if (!IsReady || airTime < config.LandingAirTime)
                return;

            float strength = Mathf.Clamp01(downSpeed / config.LandingFullSpeed);
            int perWheel = Mathf.RoundToInt(Mathf.Lerp(config.LandingPuffs.x, config.LandingPuffs.y, strength));
            Vector3 up = transform.up;
            Vector3 center = transform.position;
            Vector3 carVelocity = controller.Body.linearVelocity;
            var wheels = controller.Wheels;
            for (int w = 0; w < wheels.Count; w++)
            {
                var wheel = wheels[w];
                Vector3 contact = wheel.transform.position - up * (wheel.SpringLength + controller.Config.WheelRadius);
                Vector3 outward = Vector3.ProjectOnPlane(contact - center, Vector3.up).normalized;
                SurfaceType surface = wheel.SurfaceType;
                Color color = surface != null ? SurfacePalette.SprayColor(surface) : DefaultSpray;
                for (int k = 0; k < perWheel; k++)
                {
                    var particle = new ParticleSystem.EmitParams
                    {
                        position = contact + Random.insideUnitSphere * 0.3f + Vector3.up * 0.2f,
                        velocity = carVelocity * 0.3f + (outward + Random.insideUnitSphere * 0.6f) * Random.Range(1.5f, 3.5f) * (0.5f + strength)
                                   + Vector3.up * Random.Range(0.2f, 1f),
                        startSize = Mathf.Lerp(config.PuffSize.x, config.PuffSize.y, Random.value) * (0.8f + 0.5f * strength),
                        startLifetime = Random.Range(config.PuffLifetime.x, config.PuffLifetime.y),
                        rotation = Random.Range(0f, 360f),
                        startColor = Shade(color, Random.Range(0.9f, 1.05f), config.PuffOpacity * (0.5f + 0.5f * strength)),
                    };
                    puffs.Emit(particle, 1);
                }

                PuffsEmitted += perWheel;
            }

            LandingBursts++;
        }

        // ---------------------------------------------------------------- impacts

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsReady || !isActiveAndEnabled || collision.contactCount == 0)
                return;

            float knock = collision.impulse.magnitude / controller.Body.mass;
            if (knock < config.SparkImpactSpeed || !Sparks(collision.collider))
                return;

            var contact = collision.GetContact(0);
            float strength = Mathf.InverseLerp(config.SparkImpactSpeed, config.SparkFullImpactSpeed, knock);
            int count = Mathf.RoundToInt(Mathf.Lerp(config.SparkBurst.x, config.SparkBurst.y, strength));
            EmitSparks(contact.point, contact.normal, collision.relativeVelocity, count, 0.6f + strength);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!IsReady || !isActiveAndEnabled || collision.contactCount == 0)
                return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < config.ScrapeSpeed || !Sparks(collision.collider))
                return;

            scrapeCarry += config.ScrapeRate * (speed / 10f) * Time.fixedDeltaTime;
            int count = (int)scrapeCarry;
            if (count == 0)
                return;

            scrapeCarry -= count;
            var contact = collision.GetContact(0);
            EmitSparks(contact.point, contact.normal, collision.relativeVelocity, count, 0.7f);
        }

        /// <summary>Hard things spark: walls, rails, rocks, cars and paved ground. Earth and loose road surfaces don't.</summary>
        private bool Sparks(Collider other)
        {
            if (other == lastCollider)
                return lastColliderSparks;

            lastCollider = other;
            lastColliderSurface = other.TryGetComponent(out SurfaceMarker marker) ? marker.Surface : null;
            lastColliderSparks = other is not TerrainCollider
                                 && (lastColliderSurface == null || lastColliderSurface.Spray == SurfaceSpray.TyreSmoke);
            return lastColliderSparks;
        }

        private void EmitSparks(Vector3 point, Vector3 normal, Vector3 relativeVelocity, int count, float strength)
        {
            Vector3 along = Vector3.ProjectOnPlane(relativeVelocity, normal);
            along = along.sqrMagnitude > 0.01f ? along.normalized : Vector3.zero;
            Vector3 carVelocity = controller.Body.linearVelocity;
            for (int k = 0; k < count; k++)
            {
                Vector3 direction = normal * Random.Range(0.3f, 1.4f) - along * Random.Range(0.2f, 1.2f) + Random.insideUnitSphere * 0.8f;
                var particle = new ParticleSystem.EmitParams
                {
                    position = point + Random.insideUnitSphere * 0.08f,
                    velocity = carVelocity * 0.4f + direction * (config.SparkSpeed * Random.Range(0.4f, 1f) * strength),
                    startSize = Random.Range(0.03f, 0.08f),
                    startLifetime = Random.Range(config.SparkLifetime.x, config.SparkLifetime.y),
                    startColor = config.SparkColor,
                };
                sparks.Emit(particle, 1);
            }

            SparksEmitted += count;
        }

        // ---------------------------------------------------------------- exhaust

        private void UpdateExhaust(Vector3 carVelocity, float throttle, float deltaTime)
        {
            bool lifted = previousThrottle > 0.7f && throttle < 0.15f;
            previousThrottle = throttle;
            if (exhaustSmoke == null)
                return;

            exhaustCarry += (config.ExhaustIdleRate + throttle * config.ExhaustThrottleRate) * deltaTime;
            int count = (int)exhaustCarry;
            exhaustCarry -= count;
            for (int k = 0; k < count; k++)
            {
                var pipe = exhausts[k % exhausts.Length];
                if (pipe == null)
                    continue;

                var particle = new ParticleSystem.EmitParams
                {
                    position = pipe.position,
                    velocity = carVelocity * 0.85f + pipe.forward * (0.8f + throttle * 2f) + Vector3.up * 0.25f + Random.insideUnitSphere * 0.2f,
                    startSize = Mathf.Lerp(config.ExhaustSize.x, config.ExhaustSize.y, Random.value),
                    startLifetime = Random.Range(0.6f, 1.2f),
                    rotation = Random.Range(0f, 360f),
                    startColor = Shade(ExhaustColor, Random.Range(0.9f, 1.1f), config.ExhaustOpacity * (0.5f + 0.5f * throttle)),
                };
                exhaustSmoke.Emit(particle, 1);
            }

            ExhaustEmitted += count;

            // Lifting off sharply at speed can pop a flame from the exhaust.
            backfireCooldown -= deltaTime;
            if (!lifted || backfireCooldown > 0f || controller.ForwardSpeed < config.BackfireSpeed * controller.Config.MaxSpeed
                || Random.value > config.BackfireChance)
                return;

            backfireCooldown = 0.5f;
            Backfires++;
            Backfired?.Invoke(exhausts[0] != null ? exhausts[0].position : transform.position);
            foreach (var pipe in exhausts)
            {
                if (pipe == null)
                    continue;

                var flash = new ParticleSystem.EmitParams
                {
                    position = pipe.position,
                    velocity = carVelocity + pipe.forward * 2f,
                    startSize = Random.Range(0.25f, 0.4f),
                    startLifetime = 0.08f,
                    startColor = config.BackfireColor,
                };
                sparks.Emit(flash, 1);
                EmitSparks(pipe.position, pipe.forward, -carVelocity, 3, 0.5f);
            }
        }

        // ---------------------------------------------------------------- setup

        private ParticleSystem CreateSystem(string systemName, Material material, int maxParticles, float gravity, float drag,
            AnimationCurve size, Gradient fade, bool stretch, bool spin)
        {
            var systemObject = new GameObject(systemName);
            systemObject.transform.SetParent(transform, false);
            var system = systemObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            if (drag > 0f)
            {
                var limit = system.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 1000f;
                limit.dampen = 0f;
                limit.drag = drag;
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = false;
            }

            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = fade;

            if (size != null)
            {
                var sizeOverLifetime = system.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, size);
            }

            if (spin)
            {
                var rotation = system.rotationOverLifetime;
                rotation.enabled = true;
                rotation.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            }

            var particleRenderer = systemObject.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = material;
            particleRenderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretch)
            {
                particleRenderer.velocityScale = 0.03f;
                particleRenderer.lengthScale = 1.5f;
            }

            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            particleRenderer.lightProbeUsage = LightProbeUsage.Off;
            particleRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            system.Play();
            return system;
        }

        /// <summary>Opacity over a particle's life: fades in over the first part, holds, then fades out from <paramref name="fadeOutFrom"/>.</summary>
        private static Gradient Fade(float fadeIn, float fadeOutFrom)
        {
            var gradient = new Gradient();
            var alphas = fadeIn > 0f
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOutFrom), new GradientAlphaKey(0f, 1f) }
                : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeOutFrom), new GradientAlphaKey(0f, 1f) };
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas);
            return gradient;
        }

        private static Color Shade(Color color, float brightness, float alpha) =>
            new Color(color.r * brightness, color.g * brightness, color.b * brightness, alpha);
    }
}
