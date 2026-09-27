using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.Rendering;

namespace RallyX.VFX
{
    /// <summary>
    /// Faint dust hanging in the air and drifting on the breeze around the camera, while the car it follows is on dusty
    /// ground (surfaces that throw dust or gravel and trail dust when rolling: dirt, gravel, sand), in that ground's
    /// colour. Nothing is emitted on asphalt, mud or grass, or in the air, and the particle system stays idle then.
    /// Settings are in <see cref="VehicleEffectsConfig"/>. Visual only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientDust : MonoBehaviour
    {
        // The rolling spray of the dustiest surface (sand); a surface with this much makes dust at the full rate.
        private const float DustiestRollingSpray = 0.35f;

        [SerializeField] private VehicleEffectsConfig config;
        [SerializeField] private VehicleController target;

        private ParticleSystem dust;
        private float carry;

        /// <summary>Puffs emitted so far. For tests and tuning.</summary>
        public int Emitted { get; private set; }

        public void Configure(VehicleEffectsConfig newConfig, VehicleController newTarget)
        {
            config = newConfig;
            target = newTarget;
        }

        private void Start()
        {
            if (config == null || config.PuffMaterial == null || config.AmbientDustRate <= 0f)
            {
                enabled = false;
                return;
            }

            var systemObject = new GameObject("AmbientDust");
            systemObject.transform.SetParent(transform, false);
            dust = systemObject.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = dust.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = config.MaxAmbientDust;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;

            var emission = dust.emission;
            emission.enabled = false;
            var shape = dust.shape;
            shape.enabled = false;

            // Fades in and out slowly and swells a little, so it reads as haze rather than puffs.
            var colour = dust.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var size = dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.2f)));
            var rotation = dust.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

            var particleRenderer = systemObject.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = config.PuffMaterial;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            particleRenderer.lightProbeUsage = LightProbeUsage.Off;
            particleRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            dust.Play();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (dust == null || target == null || deltaTime <= 0f || !target.IsGrounded)
                return;

            float dustiness = Dustiness(out var surface);
            if (dustiness <= 0f)
                return;

            carry += config.AmbientDustRate * dustiness * deltaTime;
            int count = (int)carry;
            carry -= count;
            if (count == 0)
                return;

            // Ahead of and around the camera, near the ground the car is on, drifting with a steady breeze.
            Vector3 origin = transform.position;
            Vector3 ahead = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, ahead);
            float ground = target.Body.position.y;
            float range = config.AmbientDustRange;
            Color color = SurfacePalette.SprayColor(surface);
            for (int i = 0; i < count; i++)
            {
                Vector3 position = origin + ahead * Random.Range(range * 0.2f, range) + side * Random.Range(-range * 0.6f, range * 0.6f);
                position.y = ground + Random.Range(0.3f, 2.5f);
                var particle = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = new Vector3(0.8f, 0.05f, 0.35f) * (config.AmbientDustDrift * Random.Range(0.6f, 1.3f)),
                    startSize = Random.Range(config.AmbientDustSize.x, config.AmbientDustSize.y),
                    startLifetime = Random.Range(config.AmbientDustLifetime.x, config.AmbientDustLifetime.y),
                    rotation = Random.Range(0f, 360f),
                    startColor = new Color(color.r, color.g, color.b, config.AmbientDustOpacity * Random.Range(0.6f, 1f)),
                };
                dust.Emit(particle, 1);
            }

            Emitted += count;
        }

        /// <summary>How dusty the ground under the car is, 0..1, and the dustiest surface it is on.</summary>
        private float Dustiness(out SurfaceType dustiest)
        {
            dustiest = null;
            float most = 0f;
            var wheels = target.Wheels;
            for (int i = 0; i < wheels.Count; i++)
            {
                var surface = wheels[i].IsGrounded ? wheels[i].SurfaceType : null;
                if (surface == null || (surface.Spray != SurfaceSpray.Dust && surface.Spray != SurfaceSpray.Gravel))
                    continue;

                if (surface.RollingSpray > most)
                {
                    most = surface.RollingSpray;
                    dustiest = surface;
                }
            }

            return Mathf.Clamp01(most / DustiestRollingSpray);
        }
    }
}
