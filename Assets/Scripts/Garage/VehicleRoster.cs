using System;
using System.Collections.Generic;
using RallyX.SaveSystem;
using UnityEngine;

namespace RallyX.Garage
{
    /// <summary>
    /// Every car in the garage, in the order shown, and how the garage's stat bars are scaled (wide enough for fully
    /// upgraded cars). The first car is the default: the one raced before the player has chosen, or when the chosen
    /// car is locked or gone.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Vehicle Roster", fileName = "VehicleRoster")]
    public sealed class VehicleRoster : ScriptableObject
    {
        [Tooltip("Cars in garage order. The first is the default and should be unlocked from the start.")]
        [SerializeField] private VehicleDefinition[] vehicles = Array.Empty<VehicleDefinition>();

        [Header("Stat Bars")]
        [SerializeField] private VehicleStatScale topSpeed = new VehicleStatScale(120f, 220f);

        [Tooltip("0-100 km/h time, seconds.")]
        [SerializeField] private VehicleStatScale acceleration = new VehicleStatScale(5f, 2f);

        [Tooltip("Turn-in at 100 km/h, 1/m (see VehicleStats.TurnIn).")]
        [SerializeField] private VehicleStatScale handling = new VehicleStatScale(0.09f, 0.17f);

        [Tooltip("Cornering grip on asphalt, g.")]
        [SerializeField] private VehicleStatScale grip = new VehicleStatScale(0.9f, 1.7f);

        [Tooltip("Braking distance from 100 km/h, metres.")]
        [SerializeField] private VehicleStatScale braking = new VehicleStatScale(30f, 15f);

        public IReadOnlyList<VehicleDefinition> Vehicles => vehicles;
        public int Count => vehicles.Length;
        public VehicleDefinition Default => vehicles.Length > 0 ? vehicles[0] : null;

        public void SetVehicles(VehicleDefinition[] newVehicles) => vehicles = newVehicles;

        public VehicleDefinition Find(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId))
                return null;
            foreach (var vehicle in vehicles)
            {
                if (vehicle != null && vehicle.Id == vehicleId)
                    return vehicle;
            }

            return null;
        }

        public int IndexOf(VehicleDefinition vehicle) => Array.IndexOf(vehicles, vehicle);

        /// <summary>The car the player races: their chosen car if it exists and is unlocked, otherwise the default.</summary>
        public VehicleDefinition Resolve(PlayerProfile profile)
        {
            var chosen = Find(profile.SelectedVehicleId);
            return chosen != null && chosen.IsUnlocked(profile) ? chosen : Default;
        }

        public VehicleStatScale Scale(VehicleStat stat) => stat switch
        {
            VehicleStat.TopSpeed => topSpeed,
            VehicleStat.Acceleration => acceleration,
            VehicleStat.Handling => handling,
            VehicleStat.Grip => grip,
            VehicleStat.Braking => braking,
            _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
        };

        /// <summary>How full a stat's bar is for a car, 0..1.</summary>
        public float Rate(VehicleStats stats, VehicleStat stat) => Scale(stat).Rate(stats.Get(stat));

        /// <summary>Why the roster can't be used, or null when it is complete.</summary>
        public string FindProblem()
        {
            if (vehicles.Length == 0)
                return "it has no vehicles";

            var ids = new HashSet<string>();
            foreach (var vehicle in vehicles)
            {
                if (vehicle == null)
                    return "a vehicle slot is empty";
                string problem = vehicle.FindProblem();
                if (problem != null)
                    return problem;
                if (!ids.Add(vehicle.Id))
                    return $"two vehicles share the id '{vehicle.Id}'";
            }

            return Default.WinsToUnlock > 0 ? $"the default vehicle {Default.name} is locked" : null;
        }
    }
}
