using System;
using UnityEngine;

namespace RallyX.Audio
{
    /// <summary>A looped sound: a clip with its level and base pitch. Whatever plays it raises the level and pitch from there.</summary>
    [Serializable]
    public struct LoopSound
    {
        [SerializeField] private AudioClip clip;
        [SerializeField, Range(0f, 2f)] private float volume;
        [SerializeField, Range(0.25f, 3f)] private float pitch;

        public LoopSound(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            this.clip = clip;
            this.volume = volume;
            this.pitch = pitch;
        }

        public AudioClip Clip => clip;
        public float Volume => volume;
        public float Pitch => pitch > 0f ? pitch : 1f;
        public bool IsSet => clip != null && volume > 0f;
    }
}
