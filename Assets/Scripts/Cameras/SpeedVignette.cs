using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RallyX.Cameras
{
    /// <summary>
    /// A touch more vignette at speed, on top of the scene's own look. A global volume made at runtime, above the scene's
    /// volumes, overrides only the vignette's intensity with the scene's intensity plus the extra, and its weight follows
    /// the speed, so at low speed the scene looks exactly as authored.
    /// </summary>
    internal sealed class SpeedVignette
    {
        // Above the track's colour-grade volume, so this blend is applied on top of it.
        private const float Priority = 100f;

        private GameObject host;
        private VolumeProfile profile;
        private Vignette vignette;
        private Volume volume;
        private float sceneIntensity;
        private float weight;

        /// <summary>The volume's weight now, 0..1.</summary>
        public float Weight => weight;

        public void Update(ChaseCameraConfig config, Transform parent, float speedFactor, float deltaTime)
        {
            float extra = config.SpeedVignette;
            if (extra <= 0f)
            {
                if (volume != null)
                    volume.weight = weight = 0f;
                return;
            }

            if (volume == null)
                Create(parent);

            // While the volume adds nothing, the blended vignette is the scene's own; remember it.
            if (weight <= 0.001f)
            {
                var sceneVignette = VolumeManager.instance.stack?.GetComponent<Vignette>();
                if (sceneVignette != null)
                    sceneIntensity = sceneVignette.intensity.value;
            }

            vignette.intensity.Override(Mathf.Clamp01(sceneIntensity + extra));
            float target = Mathf.InverseLerp(config.SpeedVignetteStart, 1f, speedFactor);
            weight = Mathf.MoveTowards(weight, target, deltaTime * 1.5f);
            volume.weight = weight;
        }

        public void Destroy()
        {
            if (host != null)
                Object.Destroy(host);
            if (profile != null)
                Object.Destroy(profile);
            host = null;
            profile = null;
            volume = null;
        }

        private void Create(Transform parent)
        {
            host = new GameObject("SpeedVignette");
            host.transform.SetParent(parent, false);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "SpeedVignette (runtime)";
            vignette = profile.Add<Vignette>();
            volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = Priority;
            volume.sharedProfile = profile;
            volume.weight = weight = 0f;
        }
    }
}
