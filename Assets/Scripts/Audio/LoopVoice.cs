using UnityEngine;

namespace RallyX.Audio
{
    /// <summary>
    /// One looped sound on its own <see cref="AudioSource"/>: the level eases towards what is asked, a new clip waits
    /// until the old one has faded out, and the source is stopped whenever it is silent, so an unused voice costs
    /// nothing. Each loop starts at a random point, so several cars playing the same clip don't phase.
    /// </summary>
    internal sealed class LoopVoice
    {
        private const float Silent = 0.0005f;

        private readonly AudioSource source;
        private float level;

        public LoopVoice(Transform parent, string name, int priority)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.priority = priority;
        }

        public AudioSource Source => source;
        public bool IsPlaying => source.isPlaying;

        /// <param name="volume">Level wanted, before the fade.</param>
        /// <param name="fadeSpeed">How fast the level can change, per second.</param>
        public void Play(AudioClip clip, float volume, float pitch, float fadeSpeed, float deltaTime)
        {
            if (clip != source.clip)
            {
                if (level > Silent && source.clip != null)
                {
                    volume = 0f;
                }
                else
                {
                    source.Stop();
                    source.clip = clip;
                    level = 0f;
                }
            }

            level = Mathf.MoveTowards(level, source.clip != null ? Mathf.Max(0f, volume) : 0f, fadeSpeed * deltaTime);
            source.volume = level;
            source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);

            bool audible = level > Silent && source.clip != null;
            if (audible && !source.isPlaying)
            {
                source.time = Random.value * source.clip.length * 0.95f;
                source.Play();
            }
            else if (!audible && source.isPlaying)
            {
                source.Stop();
            }
        }

        /// <summary>Stops at once.</summary>
        public void Silence()
        {
            level = 0f;
            source.volume = 0f;
            if (source.isPlaying)
                source.Stop();
        }

        public void SetSpatial(float minDistance, float maxDistance, float doppler)
        {
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = doppler;
        }
    }
}
