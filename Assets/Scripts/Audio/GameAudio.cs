using RallyX.Racing;
using RallyX.SaveSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RallyX.Audio
{
    /// <summary>
    /// The game's own sound: music, menu sounds and race announcements, and the volumes the player set. Created when the
    /// game starts and kept across scenes, so music carries on from one menu to the next and nothing needs placing in a
    /// scene. Menu scenes (<see cref="GameAudioConfig.IsMenuScene"/>) play the menu music; a scene with a
    /// <see cref="RaceManager"/> gets the countdown, GO, lap and finish sounds, with the race music from GO. The master
    /// volume is applied to the <see cref="AudioListener"/>; everything else multiplies its own level by
    /// <see cref="Volume"/> of its channel. Sounds come from <see cref="GameAudioConfig"/> in a Resources folder; without it
    /// the game is silent but works.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameAudio : MonoBehaviour
    {
        private const int CueVoices = 4;

        private static GameAudio instance;

        private readonly AudioSource[] cueSources = new AudioSource[CueVoices];
        private readonly AudioSource[] musicSources = new AudioSource[2];
        private readonly float[] musicLevels = new float[2];
        private readonly float[] musicTargets = new float[2];
        private GameAudioConfig config;
        private int nextCue;
        private int activeMusic;
        private float musicDuck = 1f;
        private RaceManager race;
        private int lastCountdown;
        private bool playerFinished;

        /// <summary>The running instance, or null before the game has started (edit mode, some tests).</summary>
        public static GameAudio Instance => instance;

        /// <summary>The config the sounds come from, or null if there is none.</summary>
        public GameAudioConfig Config => config;

        /// <summary>The music playing, or fading in, now (null for none).</summary>
        public AudioClip Music => musicTargets[activeMusic] > 0f ? musicSources[activeMusic].clip : null;

        /// <summary>
        /// The player's volume for a channel, 0..1 (the defaults until the profile has been loaded). For the music, effects and
        /// vehicle channels this does not include the master volume, which the listener applies to everything.
        /// </summary>
        public static float Volume(AudioChannel channel)
        {
            var profile = PlayerProfile.Loaded;
            return profile != null ? profile.GetVolume(channel) : PlayerProfile.DefaultVolume(channel);
        }

        /// <summary>Plays a menu sound (it keeps playing while the game is paused and across a scene change).</summary>
        public static void Play(UiCue cue)
        {
            if (instance != null && instance.config != null)
                instance.PlayCue(instance.config.Get(cue));
        }

        /// <summary>Plays a race announcement.</summary>
        public static void Play(RaceCue cue)
        {
            if (instance != null && instance.config != null)
                instance.PlayCue(instance.config.Get(cue));
        }

        /// <summary>Fades to another piece of music (null fades the music out).</summary>
        public void PlayMusic(AudioClip clip)
        {
            var current = musicSources[activeMusic];
            if (clip != null && current.clip == clip && musicTargets[activeMusic] > 0f)
                return;

            musicTargets[activeMusic] = 0f;
            if (clip == null)
                return;

            // The other voice starts the new piece while this one fades out.
            activeMusic = 1 - activeMusic;
            var next = musicSources[activeMusic];
            if (next.clip != clip || !next.isPlaying)
            {
                next.clip = clip;
                musicLevels[activeMusic] = 0f;
                next.volume = 0f;
                next.Play();
            }

            musicTargets[activeMusic] = 1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            var host = new GameObject("GameAudio");
            DontDestroyOnLoad(host);
            host.AddComponent<GameAudio>();
        }

        // Entering Play Mode without a domain reload keeps statics.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => instance = null;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            config = Resources.Load<GameAudioConfig>(GameAudioConfig.ResourcePath);
            for (int i = 0; i < cueSources.Length; i++)
                cueSources[i] = CreateSource($"Cue {i + 1}", loop: false);
            for (int i = 0; i < musicSources.Length; i++)
                musicSources[i] = CreateSource($"Music {i + 1}", loop: true);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            DetachRace();
            instance = null;
        }

        private AudioSource CreateSource(string sourceName, bool loop)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.priority = loop ? 32 : 64;
            return source;
        }

        private void PlayCue(SoundCue cue)
        {
            if (!cue.IsSet)
                return;

            var source = cueSources[nextCue];
            nextCue = (nextCue + 1) % cueSources.Length;
            source.pitch = cue.PickPitch();
            source.PlayOneShot(cue.Clip, cue.Volume * Volume(AudioChannel.Effects));
        }

        private void Update()
        {
            AudioListener.volume = Volume(AudioChannel.Master);
            if (config == null)
                return;

            UpdateRace();
            UpdateMusic(Time.unscaledDeltaTime);
        }

        private void UpdateMusic(float deltaTime)
        {
            float duck = AudioListener.pause ? config.PausedMusicLevel : playerFinished ? config.FinishedMusicLevel : 1f;
            musicDuck = Mathf.MoveTowards(musicDuck, duck, deltaTime * 2f);
            float fadeStep = config.MusicFadeTime > 0f ? deltaTime / config.MusicFadeTime : 1f;
            float level = config.MusicLevel * Volume(AudioChannel.Music) * musicDuck;
            for (int i = 0; i < musicSources.Length; i++)
            {
                musicLevels[i] = Mathf.MoveTowards(musicLevels[i], musicTargets[i], fadeStep);
                var source = musicSources[i];
                source.volume = musicLevels[i] * level;
                if (musicLevels[i] <= 0f && musicTargets[i] <= 0f && source.isPlaying)
                    source.Stop();
            }
        }

        // ---------------------------------------------------------------- scenes and races

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (config == null)
                return;

            var sceneRace = FindRace(scene);
            if (sceneRace != null)
            {
                AttachRace(sceneRace);
                PlayMusic(null);
                return;
            }

            if (mode == LoadSceneMode.Single)
                PlayMusic(config.IsMenuScene(scene.name) ? config.MenuMusic : null);
        }

        private static RaceManager FindRace(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<RaceManager>(true);
                if (found != null)
                    return found;
            }

            return null;
        }

        private void AttachRace(RaceManager newRace)
        {
            DetachRace();
            race = newRace;
            lastCountdown = 0;
            playerFinished = false;
            race.RaceRestarted += OnRaceRestarted;
            race.RaceStarted += OnRaceStarted;
            race.LapCompleted += OnLapCompleted;
            race.ParticipantFinished += OnParticipantFinished;
        }

        private void DetachRace()
        {
            // The race may already be destroyed (its scene unloaded); its events can still be unsubscribed from.
            if (ReferenceEquals(race, null))
                return;

            race.RaceRestarted -= OnRaceRestarted;
            race.RaceStarted -= OnRaceStarted;
            race.LapCompleted -= OnLapCompleted;
            race.ParticipantFinished -= OnParticipantFinished;
            race = null;
            playerFinished = false;
        }

        private void UpdateRace()
        {
            if (ReferenceEquals(race, null))
                return;

            if (race == null)
            {
                DetachRace();
                return;
            }

            // 3, 2, 1: a tick each time the number changes.
            int number = race.State == RaceState.Countdown ? race.CountdownNumber : 0;
            if (number > 0 && number != lastCountdown)
                Play(RaceCue.CountdownTick);
            lastCountdown = number;
        }

        private void OnRaceRestarted()
        {
            lastCountdown = 0;
            playerFinished = false;
            PlayMusic(null);
        }

        private void OnRaceStarted()
        {
            Play(RaceCue.Go);
            PlayMusic(config.RaceMusic);
        }

        private void OnLapCompleted(RaceParticipant participant, int lap, float lapTime)
        {
            if (participant != race.Player || participant.Tracker.Finished)
                return;

            Play(lap + 1 == race.Laps ? RaceCue.FinalLap : RaceCue.Lap);
        }

        private void OnParticipantFinished(RaceParticipant participant)
        {
            if (participant != race.Player)
                return;

            playerFinished = true;
            Play(RaceCue.Finish);
        }
    }
}
