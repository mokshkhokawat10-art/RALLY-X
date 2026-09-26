using System;
using System.Collections.Generic;
using RallyX.Garage;
using RallyX.SaveSystem;
using UnityEngine;

namespace RallyX.Progression
{
    /// <summary>The career's rules: credits paid for each finishing position, starting credits, the upgrades on sale and the tracks.</summary>
    [CreateAssetMenu(menuName = "RALLY X/Progression Settings", fileName = "ProgressionSettings")]
    public sealed class ProgressionSettings : ScriptableObject
    {
        [Tooltip("Credits for finishing 1st, 2nd, 3rd... Positions past the end of the list earn the last amount.")]
        [SerializeField] private int[] creditsByPosition = { 1500, 1000, 700, 400 };

        [Tooltip("Credits a new career starts with.")]
        [SerializeField, Min(0)] private int startingCredits = 2000;

        [SerializeField] private UpgradeCatalog upgrades;
        [SerializeField] private TrackRoster tracks;

        public int StartingCredits => startingCredits;
        public UpgradeCatalog Upgrades => upgrades;
        public TrackRoster Tracks => tracks;

        public void Configure(UpgradeCatalog newUpgrades, TrackRoster newTracks)
        {
            upgrades = newUpgrades;
            tracks = newTracks;
        }

        public int CreditsFor(int position)
        {
            if (creditsByPosition.Length == 0 || position <= 0)
                return 0;
            return Mathf.Max(0, creditsByPosition[Mathf.Min(position, creditsByPosition.Length) - 1]);
        }

        /// <summary>Why the settings can't be used, or null when they are complete.</summary>
        public string FindProblem()
        {
            if (upgrades == null)
                return "they have no UpgradeCatalog";
            if (tracks == null)
                return "they have no TrackRoster";
            for (int i = 1; i < creditsByPosition.Length; i++)
            {
                if (creditsByPosition[i] > creditsByPosition[i - 1])
                    return $"position {i + 1} pays more than position {i}";
            }

            return upgrades.FindProblem() ?? tracks.FindProblem();
        }
    }

    /// <summary>What a finished race earned the player, for the results screen.</summary>
    public sealed class RaceReward
    {
        public int Position { get; set; }
        public int Racers { get; set; }
        public float RaceTime { get; set; }

        /// <summary>The player's fastest lap this race, seconds.</summary>
        public float BestLap { get; set; }

        /// <summary>True if a lap this race set the track's best lap.</summary>
        public bool NewLapRecord { get; set; }

        public int CreditsEarned { get; set; }
        public int TotalCredits { get; set; }
        public List<string> UnlockedVehicles { get; } = new List<string>();
        public List<string> UnlockedTracks { get; } = new List<string>();

        /// <summary>
        /// Pays and records a finished race: credits for the position, the win and track finish, and any cars or
        /// tracks it unlocked (kept in the profile from then on).
        /// </summary>
        public static RaceReward Record(PlayerProfile profile, ProgressionSettings settings, VehicleRoster roster, TrackInfo track,
            int position, int racers, float raceTime, float bestLap, bool newLapRecord)
        {
            var lockedCars = new List<VehicleDefinition>();
            if (roster != null)
            {
                foreach (var vehicle in roster.Vehicles)
                {
                    if (vehicle != null && !vehicle.IsUnlocked(profile))
                        lockedCars.Add(vehicle);
                }
            }

            var reward = new RaceReward
            {
                Position = position,
                Racers = racers,
                RaceTime = raceTime,
                BestLap = bestLap,
                NewLapRecord = newLapRecord,
                CreditsEarned = settings != null ? settings.CreditsFor(position) : 0,
            };
            profile.RecordRaceFinish(position, reward.CreditsEarned, track != null ? track.Id : null);

            foreach (var vehicle in lockedCars)
            {
                if (!vehicle.IsUnlocked(profile))
                    continue;
                profile.Unlock(vehicle.Id);
                reward.UnlockedVehicles.Add(vehicle.DisplayName);
            }

            if (settings != null && settings.Tracks != null)
            {
                foreach (var other in settings.Tracks.Tracks)
                {
                    if (other == null || other.OpenFromStart || profile.HasUnlockedTrack(other.Id) || !other.MeetsRequirements(profile))
                        continue;
                    profile.UnlockTrack(other.Id);
                    reward.UnlockedTracks.Add(other.DisplayName);
                }
            }

            reward.TotalCredits = profile.Credits;
            return reward;
        }
    }
}
