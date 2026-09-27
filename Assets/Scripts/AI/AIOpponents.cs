using System;
using System.Collections.Generic;
using RallyX.Core;
using RallyX.Racing;
using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RallyX.AI
{
    /// <summary>
    /// Fills a race with AI drivers. On load it works out the AI routes for the track once, then spawns a car for
    /// each driver profile, paints it, gives it an <see cref="AIDriver"/> at the chosen difficulty and enters it in
    /// the race behind the player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AIOpponents : MonoBehaviour
    {
        // Door numbers for the AI cars, in grid order (the player's cars carry their own).
        private static readonly int[] RaceNumbers = { 12, 23, 44, 5, 9 };

        [SerializeField] private RaceManager race;
        [SerializeField] private GameObject carPrefab;
        [SerializeField] private AISettings settings;

        [Tooltip("One car per profile, starting from the grid slot behind the player's.")]
        [SerializeField] private AIDriverProfile[] drivers = Array.Empty<AIDriverProfile>();

        [Tooltip("How hard the AI drivers race. Can be changed during Play; restart the race (Backspace) for a fair start.")]
        [SerializeField] private AIDifficulty difficulty = AIDifficulty.Normal;

        private readonly List<AIDriver> spawned = new List<AIDriver>();

        public IReadOnlyList<AIDriver> Drivers => spawned;

        /// <summary>How hard every AI driver races. Changing it applies to the spawned drivers straight away.</summary>
        public AIDifficulty Difficulty
        {
            get => difficulty;
            set
            {
                difficulty = value;
                ApplyDifficulty();
            }
        }

        /// <summary>Routes the drivers share: the main loop first, then one through each branch.</summary>
        public IReadOnlyList<AIRoute> Routes { get; private set; } = Array.Empty<AIRoute>();

        public AISettings Settings => settings;

        public void Configure(RaceManager newRace, GameObject newCarPrefab, AISettings newSettings, AIDriverProfile[] newDrivers)
        {
            race = newRace;
            carPrefab = newCarPrefab;
            settings = newSettings;
            drivers = newDrivers;
        }

        private void Awake()
        {
            var circuit = race != null ? race.Circuit : null;
            string problem = race == null ? "it has no RaceManager"
                : carPrefab == null ? "it has no car prefab"
                : settings == null ? "it has no AISettings"
                : circuit == null ? "the race has no RaceCircuit"
                : circuit.FindProblem();
            if (problem != null)
            {
                Debug.LogError($"{name}: AIOpponents can't spawn drivers: {problem}.", this);
                return;
            }

            int vehicleLayer = LayerMask.NameToLayer(GameLayers.Vehicle);
            int groundMask = vehicleLayer >= 0 ? ~(1 << vehicleLayer) : ~0;
            var branches = FindObjectsByType<TrackBranch>(FindObjectsSortMode.None);
            Array.Sort(branches, (a, b) => string.CompareOrdinal(a.name, b.name));
            Routes = AIRouteBuilder.Build(circuit.Path, branches, settings, groundMask);

            var grid = circuit.Grid;
            for (int i = 0; i < drivers.Length; i++)
            {
                var profile = drivers[i];
                if (profile == null)
                    continue;

                var slot = grid.GetSlot(Mathf.Min(i + 1, grid.SlotCount - 1));
                var car = Object.Instantiate(carPrefab, slot.position, slot.rotation);
                SceneManager.MoveGameObjectToScene(car, gameObject.scene);
                car.name = $"AI_{profile.DisplayName}";
                if (car.TryGetComponent(out VehicleLivery livery))
                {
                    livery.SetPaint(profile.Paint);
                    livery.SetRaceNumber(RaceNumbers[i % RaceNumbers.Length]);
                }

                var participant = car.AddComponent<RaceParticipant>();
                participant.Configure(profile.DisplayName, player: false);
                var driver = car.AddComponent<AIDriver>();
                driver.Configure(race, settings, profile, Routes, difficulty);
                race.AddParticipant(participant);
                spawned.Add(driver);
            }

            Debug.Log($"{name}: {spawned.Count} AI drivers on {difficulty}.", this);
        }

        // Picks up a difficulty chosen in the Inspector during Play.
        private void OnValidate()
        {
            if (Application.isPlaying)
                ApplyDifficulty();
        }

        private void ApplyDifficulty()
        {
            bool changed = false;
            foreach (var driver in spawned)
            {
                if (driver == null || driver.Difficulty == difficulty)
                    continue;
                driver.Difficulty = difficulty;
                changed = true;
            }

            if (changed)
                Debug.Log($"{name}: AI difficulty set to {difficulty}.", this);
        }
    }
}
