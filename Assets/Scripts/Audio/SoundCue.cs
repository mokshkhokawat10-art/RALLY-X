using System;
using UnityEngine;

namespace RallyX.Audio
{
    /// <summary>A one-shot sound: a clip with its level and a random pitch range, so repeats don't sound identical.</summary>
    [Serializable]
    public struct SoundCue
    {
        [SerializeField] private AudioClip clip;
        [SerializeField, Range(0f, 2f)] private float volume;
        [Tooltip("Pitch is picked at random between these each time.")]
        [SerializeField] private Vector2 pitch;

        public SoundCue(AudioClip clip, float volume = 1f, float minPitch = 1f, float maxPitch = 1f)
        {
            this.clip = clip;
            this.volume = volume;
            pitch = new Vector2(minPitch, maxPitch);
        }

        public AudioClip Clip => clip;
        public float Volume => volume;

        /// <summary>A pitch for one play (1 when no range is set).</summary>
        public float PickPitch() => pitch == Vector2.zero ? 1f : UnityEngine.Random.Range(pitch.x, pitch.y);

        public bool IsSet => clip != null && volume > 0f;
    }
}
