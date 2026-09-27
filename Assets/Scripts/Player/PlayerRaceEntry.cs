using System;
using RallyX.Audio;
using RallyX.Cameras;
using RallyX.Garage;
using RallyX.Progression;
using RallyX.Racing;
using RallyX.SaveSystem;
using RallyX.Tracks;
using RallyX.Vehicle;
using RallyX.VFX;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RallyX.Player
{
    /// <summary>
    /// Puts the player in a track scene's race in the car chosen in the garage (the roster's default if none is
    /// chosen or it is locked), with its installed upgrades applied to its handling, on pole with the chase camera
    /// behind it. Records laps in the player's profile, and pays and records the result when they finish
    /// (<see cref="RaceReward"/>). Sets the race to the track's laps. Runs before the race and AI set up, so they find the car.
    /// The player's car gets gamepad rumble (<see cref="VehicleHaptics"/>) and the sounds meant for the car the camera
    /// follows; the camera gets the ambient dust (<see cref="AmbientDust"/>).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerRaceEntry : MonoBehaviour
    {
        [SerializeField] private VehicleRoster roster;
        [SerializeField] private TrackSpawnGrid grid;
        [SerializeField] private RaceManager race;
        [SerializeField] private ChaseCamera chaseCamera;

        [Tooltip("Rewards, upgrades and tracks. Without it the car races stock and results earn nothing.")]
        [SerializeField] private ProgressionSettings progression;

        [Tooltip("This track, for best laps and unlocks.")]
        [SerializeField] private TrackInfo track;

        [Tooltip("Name shown in positions and results.")]
        [SerializeField] private string displayName = "You";

        [Tooltip("Show the F2 telemetry panel from the start.")]
        [SerializeField] private bool showTelemetry;

        private VehicleConfig tunedConfig;
        private bool resultPending;
        private bool lapRecordThisRace;

        /// <summary>The player's race was paid and recorded.</summary>
        public event Action<RaceReward> ResultRecorded;

        /// <summary>The car the player is racing.</summary>
        public VehicleDefinition Vehicle { get; private set; }

        public VehicleController Car { get; private set; }

        /// <summary>The upgrades applied to the car (stock when none are installed).</summary>
        public VehicleTuning Tuning { get; private set; }

        public TrackInfo Track => track;

        /// <summary>What the player's last finished race earned, or null before they finish.</summary>
        public RaceReward LastReward { get; private set; }

        public void Configure(VehicleRoster newRoster, TrackSpawnGrid newGrid, RaceManager newRace, ChaseCamera newCamera,
            ProgressionSettings newProgression, TrackInfo newTrack)
        {
            roster = newRoster;
            grid = newGrid;
            race = newRace;
            chaseCamera = newCamera;
            progression = newProgression;
            track = newTrack;
        }

        private void Awake()
        {
            string problem = roster == null ? "it has no VehicleRoster"
                : grid == null || grid.SlotCount == 0 ? "it has no TrackSpawnGrid with slots"
                : roster.FindProblem();
            if (problem != null)
            {
                Debug.LogError($"{name}: PlayerRaceEntry can't put the player in the race: {problem}.", this);
                enabled = false;
                return;
            }

            var profile = PlayerProfile.Current;
            if (progression != null)
                profile.EnsureStarted(progression.StartingCredits);

            // The track's own race length; runs before the race manager sets up the first race.
            if (race != null && track != null)
                race.SetLaps(track.Laps);

            Vehicle = roster.Resolve(profile);
            var slot = grid.GetSlot(0);
            Car = Instantiate(Vehicle.Prefab, slot.position, slot.rotation);
            SceneManager.MoveGameObjectToScene(Car.gameObject, gameObject.scene);
            Car.name = "PlayerCar";

            // Installed upgrades change this car's own copy of its handling; the car's config asset stays as built.
            Tuning = progression != null && progression.Upgrades != null ? progression.Upgrades.TuningFor(profile, Vehicle.Id) : default;
            if (!Tuning.IsStock)
            {
                tunedConfig = Car.Config.CreateTuned(Tuning);
                Car.SetConfig(tunedConfig);
            }

            var carObject = Car.gameObject;
            carObject.AddComponent<PlayerVehicleInput>();
            carObject.AddComponent<VehicleDebugPanel>().Visible = showTelemetry;
            carObject.AddComponent<RaceParticipant>().Configure(displayName, player: true);
            carObject.AddComponent<VehicleHaptics>();
            if (carObject.TryGetComponent(out VehicleAudio carAudio))
                carAudio.IsListenerCar = true;

            if (chaseCamera != null)
            {
                chaseCamera.Target = Car;
                chaseCamera.SnapToTarget();
                if (carObject.TryGetComponent(out VehicleEffects effects) && effects.Config != null)
                    chaseCamera.gameObject.AddComponent<AmbientDust>().Configure(effects.Config, Car);
            }
        }

        private void OnEnable()
        {
            if (race == null)
                return;
            race.RaceRestarted += OnRaceRestarted;
            race.LapCompleted += OnLapCompleted;
            race.ParticipantFinished += OnParticipantFinished;
        }

        private void OnDisable()
        {
            if (race == null)
                return;
            race.RaceRestarted -= OnRaceRestarted;
            race.LapCompleted -= OnLapCompleted;
            race.ParticipantFinished -= OnParticipantFinished;
        }

        private void OnDestroy()
        {
            if (tunedConfig != null)
                Destroy(tunedConfig);
        }

        private bool IsPlayer(RaceParticipant participant) => Car != null && participant.gameObject == Car.gameObject;

        private void OnRaceRestarted()
        {
            resultPending = false;
            lapRecordThisRace = false;
            LastReward = null;
        }

        private void OnLapCompleted(RaceParticipant participant, int lap, float lapTime)
        {
            if (IsPlayer(participant) && track != null && PlayerProfile.Current.RecordLap(track.Id, lapTime, Vehicle.Id))
                lapRecordThisRace = true;
        }

        // Positions are settled once every racer has been moved on this step, so read the result a frame later.
        private void OnParticipantFinished(RaceParticipant participant)
        {
            if (IsPlayer(participant))
                resultPending = true;
        }

        private void Update()
        {
            if (!resultPending)
                return;

            resultPending = false;
            var participant = Car.GetComponent<RaceParticipant>();
            var tracker = participant.Tracker;
            var profile = PlayerProfile.Current;
            LastReward = RaceReward.Record(profile, progression, roster, track, participant.Position, race.Participants.Count,
                tracker.FinishTime, tracker.BestLapTime, lapRecordThisRace);
            Debug.Log($"[RALLY X] Finished {participant.Position} of {race.Participants.Count} in the {Vehicle.DisplayName}: " +
                      $"+{LastReward.CreditsEarned} credits ({profile.Credits} total); {profile.Wins} wins from {profile.RacesFinished} races.", this);
            ResultRecorded?.Invoke(LastReward);
        }
    }
}
