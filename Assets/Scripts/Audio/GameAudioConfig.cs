using System;
using UnityEngine;

namespace RallyX.Audio
{
    /// <summary>Menu sounds.</summary>
    public enum UiCue
    {
        /// <summary>The highlight moved to another item.</summary>
        Navigate,
        Confirm,
        Back,

        /// <summary>Something can't be done (locked car, not enough credits).</summary>
        Error,
        Purchase,

        /// <summary>Another car or track on show.</summary>
        Switch,
    }

    /// <summary>Race announcements.</summary>
    public enum RaceCue
    {
        /// <summary>Each of 3, 2, 1.</summary>
        CountdownTick,
        Go,
        Lap,
        FinalLap,
        Finish,

        /// <summary>The results screen opening.</summary>
        Results,
    }

    /// <summary>
    /// The game's music and its menu and race sounds, played by <see cref="GameAudio"/>. Loaded from a Resources folder
    /// (<see cref="ResourcePath"/>), so every scene has it without a reference. Every sound is a slot: replace a clip
    /// to change it, or clear it to silence it.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Game Audio Config", fileName = "GameAudio")]
    public sealed class GameAudioConfig : ScriptableObject
    {
        /// <summary>Where <see cref="GameAudio"/> loads the config from: Assets/Resources/RallyX/GameAudio.asset.</summary>
        public const string ResourcePath = "RallyX/GameAudio";

        [Header("Music")]
        [Tooltip("Plays in the menu scenes, carrying on from one to the next.")]
        [SerializeField] private AudioClip menuMusic;

        [Tooltip("Plays in a race from GO to the end.")]
        [SerializeField] private AudioClip raceMusic;

        [Tooltip("Scenes that play the menu music. A scene with a race plays the race music; any other scene none.")]
        [SerializeField] private string[] menuScenes = { "MainMenu", "Garage" };

        [Tooltip("Level of the music before the music volume setting, 0..1.")]
        [SerializeField, Range(0f, 1f)] private float musicLevel = 0.55f;

        [Tooltip("Seconds for music to fade in or out.")]
        [SerializeField, Min(0f)] private float musicFadeTime = 1.5f;

        [Tooltip("Music level while the race is paused, as a share of the normal level.")]
        [SerializeField, Range(0f, 1f)] private float pausedMusicLevel = 0.35f;

        [Tooltip("Music level once the player has finished, as a share of the normal level, so the finish and results stand out.")]
        [SerializeField, Range(0f, 1f)] private float finishedMusicLevel = 0.45f;

        [Header("Menu Sounds")]
        [SerializeField] private SoundCue navigate;
        [SerializeField] private SoundCue confirm;
        [SerializeField] private SoundCue back;
        [SerializeField] private SoundCue error;
        [SerializeField] private SoundCue purchase;
        [SerializeField] private SoundCue switchItem;

        [Header("Race Sounds")]
        [SerializeField] private SoundCue countdownTick;
        [SerializeField] private SoundCue go;
        [SerializeField] private SoundCue lap;
        [SerializeField] private SoundCue finalLap;
        [SerializeField] private SoundCue finish;
        [SerializeField] private SoundCue results;

        public AudioClip MenuMusic => menuMusic;
        public AudioClip RaceMusic => raceMusic;
        public float MusicLevel => musicLevel;
        public float MusicFadeTime => musicFadeTime;
        public float PausedMusicLevel => pausedMusicLevel;
        public float FinishedMusicLevel => finishedMusicLevel;

        public bool IsMenuScene(string sceneName) => Array.IndexOf(menuScenes, sceneName) >= 0;

        public SoundCue Get(UiCue cue) => cue switch
        {
            UiCue.Navigate => navigate,
            UiCue.Confirm => confirm,
            UiCue.Back => back,
            UiCue.Error => error,
            UiCue.Purchase => purchase,
            UiCue.Switch => switchItem,
            _ => default,
        };

        public SoundCue Get(RaceCue cue) => cue switch
        {
            RaceCue.CountdownTick => countdownTick,
            RaceCue.Go => go,
            RaceCue.Lap => lap,
            RaceCue.FinalLap => finalLap,
            RaceCue.Finish => finish,
            RaceCue.Results => results,
            _ => default,
        };

#if UNITY_EDITOR
        /// <summary>Sets every sound, for the content builder.</summary>
        public void SetSounds(AudioClip newMenuMusic, AudioClip newRaceMusic, SoundCue[] uiCues, SoundCue[] raceCues)
        {
            menuMusic = newMenuMusic;
            raceMusic = newRaceMusic;
            navigate = uiCues[(int)UiCue.Navigate];
            confirm = uiCues[(int)UiCue.Confirm];
            back = uiCues[(int)UiCue.Back];
            error = uiCues[(int)UiCue.Error];
            purchase = uiCues[(int)UiCue.Purchase];
            switchItem = uiCues[(int)UiCue.Switch];
            countdownTick = raceCues[(int)RaceCue.CountdownTick];
            go = raceCues[(int)RaceCue.Go];
            lap = raceCues[(int)RaceCue.Lap];
            finalLap = raceCues[(int)RaceCue.FinalLap];
            finish = raceCues[(int)RaceCue.Finish];
            results = raceCues[(int)RaceCue.Results];
        }
#endif
    }
}
