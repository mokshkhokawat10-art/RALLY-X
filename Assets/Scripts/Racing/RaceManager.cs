using System;
using System.Collections.Generic;
using UnityEngine;

namespace RallyX.Racing
{
    public enum RaceState
    {
        /// <summary>Cars held on the grid: the wait before the countdown, then 3, 2, 1.</summary>
        Countdown,

        /// <summary>From GO until every racer has finished.</summary>
        Racing,

        /// <summary>Every racer has crossed the finish line.</summary>
        Finished,
    }

    /// <summary>
    /// Runs a race on a <see cref="RaceCircuit"/>: grid placement, countdown, race clock, checkpoints, laps and
    /// wrong-way warnings for every racer, positions, the finish and restarts. Racers are the scene's
    /// <see cref="RaceParticipant"/>s plus any added with <see cref="AddParticipant"/>. Disabling the manager
    /// releases the cars for free driving.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceManager : MonoBehaviour
    {
        [SerializeField] private RaceSettings settings;
        [SerializeField] private RaceCircuit circuit;

        private static readonly Predicate<RaceParticipant> IsMissing = participant => participant == null;

        private readonly List<RaceParticipant> participants = new List<RaceParticipant>();
        private readonly List<RaceParticipant> standings = new List<RaceParticipant>();
        private Comparison<RaceParticipant> compareStandings;
        private float countdownElapsed;
        private bool started;
        private int lapsOverride;

        /// <summary>A new race was set up on the grid and its countdown is about to begin.</summary>
        public event Action RaceRestarted;

        /// <summary>GO: the race clock is running and the cars are released.</summary>
        public event Action RaceStarted;

        /// <summary>A racer passed a gate (index 0 is the start/finish line).</summary>
        public event Action<RaceParticipant, int> CheckpointPassed;

        /// <summary>A racer completed a lap: the lap number (from 1) and its time in seconds.</summary>
        public event Action<RaceParticipant, int, float> LapCompleted;

        public event Action<RaceParticipant> ParticipantFinished;

        public event Action<RaceParticipant, bool> WrongWayChanged;

        /// <summary>Every racer has finished.</summary>
        public event Action RaceFinished;

        public RaceSettings Settings => settings;
        public RaceCircuit Circuit => circuit;
        public RaceState State { get; private set; }
        /// <summary>Laps in the race: the track's own count if it set one (<see cref="SetLaps"/>), otherwise the race settings'.</summary>
        public int Laps => lapsOverride > 0 ? lapsOverride : settings.Laps;

        /// <summary>Seconds since GO; 0 during the countdown. Stops once every racer has finished.</summary>
        public float RaceTime { get; private set; }

        /// <summary>Countdown number to show (3, 2, 1), or 0 while waiting on the grid and once the race is on.</summary>
        public int CountdownNumber =>
            State == RaceState.Countdown && countdownElapsed >= 0f
                ? Mathf.Max(1, settings.CountdownFrom - Mathf.FloorToInt(countdownElapsed / settings.CountdownStep))
                : 0;

        /// <summary>True for the moment after the start when GO is shown.</summary>
        public bool ShowingGo => State != RaceState.Countdown && RaceTime < settings.GoDisplayTime;

        public IReadOnlyList<RaceParticipant> Participants => participants;

        /// <summary>Racers in race order, leader first.</summary>
        public IReadOnlyList<RaceParticipant> Standings => standings;

        /// <summary>The player's racer, or null if there is none.</summary>
        public RaceParticipant Player { get; private set; }

        public void Configure(RaceSettings newSettings, RaceCircuit newCircuit)
        {
            settings = newSettings;
            circuit = newCircuit;
        }

        /// <summary>
        /// Sets the race length for this track (0 = the race settings' laps). Takes effect when a race is set up, so call
        /// it before the scene starts (the first race) or follow it with <see cref="Restart"/>.
        /// </summary>
        public void SetLaps(int laps) => lapsOverride = Mathf.Max(0, laps);

        /// <summary>Adds a racer, for example one spawned at runtime. It takes part from the next <see cref="Restart"/>.</summary>
        public void AddParticipant(RaceParticipant participant)
        {
            if (participant == null || participants.Contains(participant))
                return;

            participants.Add(participant);
            if (participant.IsPlayer && Player == null)
                Player = participant;
        }

        /// <summary>Takes a racer out of the race. Its car is released and left where it is.</summary>
        public void RemoveParticipant(RaceParticipant participant)
        {
            if (!participants.Remove(participant))
                return;

            standings.Remove(participant);
            if (Player == participant)
                Player = participants.Find(other => other.IsPlayer);
            if (participant != null)
                participant.Vehicle.InputLocked = false;

            // Racers get their trackers when a race is set up; before the first one there are no standings to update.
            if (participants.TrueForAll(other => other.Tracker != null))
                UpdateStandings();
        }

        /// <summary>Puts every racer back on the grid and starts a new race with a fresh countdown.</summary>
        public void Restart()
        {
            // A disabled manager doesn't run the countdown, so it would leave the cars locked on the grid.
            if (!isActiveAndEnabled)
                return;

            participants.RemoveAll(IsMissing);

            // The player starts on pole; everyone else keeps the order they joined in.
            if (Player != null && participants.Remove(Player))
                participants.Insert(0, Player);

            State = RaceState.Countdown;
            countdownElapsed = -settings.GridDelay;
            RaceTime = 0f;

            var grid = circuit.Grid;
            if (participants.Count > grid.SlotCount)
                Debug.LogWarning($"{name}: {participants.Count} racers but only {grid.SlotCount} grid slots; extra racers share the last slot.", this);

            for (int i = 0; i < participants.Count; i++)
            {
                var participant = participants[i];
                var slot = grid.GetSlot(Mathf.Min(i, grid.SlotCount - 1));
                participant.GridSlot = i;
                participant.Vehicle.PlaceAt(slot.position, slot.rotation);
                participant.Vehicle.InputLocked = true;
                participant.Tracker ??= new RaceTracker(circuit, settings);
                participant.Tracker.Laps = Laps;
                participant.Tracker.Reset(slot.position);
            }

            UpdateStandings();
            RaceRestarted?.Invoke();
        }

        private void Awake()
        {
            compareStandings = CompareStandings;
            string problem = settings == null ? "it has no RaceSettings"
                : circuit == null ? "it has no RaceCircuit"
                : circuit.FindProblem();
            if (problem != null)
            {
                Debug.LogError($"{name}: RaceManager can't run: {problem}.", this);
                enabled = false;
                return;
            }

            // The player takes pole position; everyone else follows in a stable order.
            var found = FindObjectsByType<RaceParticipant>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Array.Sort(found, (a, b) => a.IsPlayer != b.IsPlayer ? (a.IsPlayer ? -1 : 1) : string.CompareOrdinal(a.DisplayName, b.DisplayName));
            foreach (var participant in found)
                AddParticipant(participant);
        }

        private void OnEnable()
        {
            if (started)
                Restart();
        }

        private void Start()
        {
            started = true;
            Restart();
        }

        private void OnDisable()
        {
            foreach (var participant in participants)
            {
                if (participant != null)
                    participant.Vehicle.InputLocked = false;
            }
        }

        private void FixedUpdate()
        {
            float deltaTime = Time.fixedDeltaTime;
            switch (State)
            {
                case RaceState.Countdown:
                    countdownElapsed += deltaTime;
                    if (countdownElapsed >= settings.CountdownDuration)
                        StartRacing();
                    break;

                case RaceState.Racing:
                    RaceTime += deltaTime;
                    StepRacers(deltaTime);
                    break;
            }
        }

        private void StartRacing()
        {
            State = RaceState.Racing;
            RaceTime = 0f;
            foreach (var participant in participants)
                participant.Vehicle.InputLocked = false;
            RaceStarted?.Invoke();
        }

        private void StepRacers(float deltaTime)
        {
            participants.RemoveAll(IsMissing);
            bool allFinished = true;
            foreach (var participant in participants)
            {
                var vehicle = participant.Vehicle;
                var tracker = participant.Tracker;
                var body = vehicle.Body;
                var events = tracker.Step(body.position, body.rotation * Vector3.forward, vehicle.ForwardSpeed, RaceTime, deltaTime);
                allFinished &= tracker.Finished;
                if (events == RaceEvents.None)
                    continue;

                if ((events & RaceEvents.Checkpoint) != 0)
                    CheckpointPassed?.Invoke(participant, tracker.LastCheckpoint);
                if ((events & RaceEvents.LapCompleted) != 0)
                    LapCompleted?.Invoke(participant, tracker.CompletedLaps, tracker.LapTimes[tracker.LapTimes.Count - 1]);
                if ((events & RaceEvents.WrongWayChanged) != 0)
                    WrongWayChanged?.Invoke(participant, tracker.IsWrongWay);
                if ((events & RaceEvents.Finished) != 0)
                {
                    // Finished cars coast to a stop and wait while the rest of the field finishes.
                    vehicle.InputLocked = true;
                    ParticipantFinished?.Invoke(participant);
                }
            }

            UpdateStandings();
            if (allFinished && participants.Count > 0)
            {
                State = RaceState.Finished;
                RaceFinished?.Invoke();
            }
        }

        private void UpdateStandings()
        {
            standings.Clear();
            standings.AddRange(participants);
            standings.Sort(compareStandings);
            for (int i = 0; i < standings.Count; i++)
                standings[i].Position = i + 1;
        }

        private static int CompareStandings(RaceParticipant a, RaceParticipant b)
        {
            int order = a.Tracker.Progress.CompareTo(b.Tracker.Progress);
            return order != 0 ? order : a.GridSlot.CompareTo(b.GridSlot);
        }
    }
}
