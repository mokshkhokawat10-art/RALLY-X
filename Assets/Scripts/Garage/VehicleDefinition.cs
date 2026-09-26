using RallyX.SaveSystem;
using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.Garage
{
    /// <summary>
    /// One car the player can drive: its name and description for the garage, the prefab raced and shown on the
    /// turntable, and what unlocks it. Handling lives in the prefab's <see cref="VehicleConfig"/>, so a real model
    /// only needs a new prefab with a <see cref="VehicleController"/>, its config and wheels.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Vehicle Definition", fileName = "Vehicle")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        [Tooltip("Stable id saved in the player's profile. Don't change it once players may have chosen the car.")]
        [SerializeField] private string id = "car";

        [SerializeField] private string displayName = "Car";

        [Tooltip("Short line under the name, e.g. the car's class and character.")]
        [SerializeField] private string category = string.Empty;

        [SerializeField, TextArea(2, 5)] private string description = string.Empty;

        [Tooltip("The car: a prefab with a VehicleController, its VehicleConfig and wheels. Used for racing and the garage turntable.")]
        [SerializeField] private VehicleController prefab;

        [Tooltip("Races the player must win to unlock the car. 0 = available from the start.")]
        [SerializeField, Min(0)] private int winsToUnlock;

        public string Id => id;
        public string DisplayName => displayName;
        public string Category => category;
        public string Description => description;
        public VehicleController Prefab => prefab;
        public int WinsToUnlock => winsToUnlock;

        public static VehicleDefinition Create(string id, string displayName, string category, string description,
            VehicleController prefab, int winsToUnlock)
        {
            var definition = CreateInstance<VehicleDefinition>();
            definition.name = displayName;
            definition.id = id;
            definition.displayName = displayName;
            definition.category = category;
            definition.description = description;
            definition.prefab = prefab;
            definition.winsToUnlock = winsToUnlock;
            return definition;
        }

        /// <summary>The car's performance figures, worked out from its prefab.</summary>
        public VehicleStats MeasureStats() => VehicleStats.Measure(prefab);

        /// <summary>The car's performance figures with upgrades applied.</summary>
        public VehicleStats MeasureStats(VehicleTuning tuning)
        {
            if (tuning.IsStock)
                return MeasureStats();

            var tuned = prefab.Config.CreateTuned(tuning);
            try
            {
                return VehicleStats.Measure(tuned, VehicleStats.Wheelbase(prefab));
            }
            finally
            {
                if (Application.isPlaying)
                    Destroy(tuned);
                else
                    DestroyImmediate(tuned);
            }
        }

        public bool IsUnlocked(PlayerProfile profile) => winsToUnlock <= 0 || profile.Wins >= winsToUnlock || profile.HasUnlocked(id);

        /// <summary>What the player still has to do to unlock the car, or empty when it is unlocked.</summary>
        public string UnlockHint(PlayerProfile profile)
        {
            if (IsUnlocked(profile))
                return string.Empty;
            string races = winsToUnlock == 1 ? "RACE" : "RACES";
            return $"WIN {winsToUnlock} {races} TO UNLOCK  ({Mathf.Min(profile.Wins, winsToUnlock)}/{winsToUnlock})";
        }

        /// <summary>Why the car can't be used, or null when it is complete.</summary>
        public string FindProblem()
        {
            if (string.IsNullOrWhiteSpace(id))
                return $"{name} has no id";
            if (prefab == null)
                return $"{name} has no car prefab";
            if (prefab.Config == null)
                return $"{name}'s prefab has no VehicleConfig";
            return prefab.Wheels.Count == 0 ? $"{name}'s prefab has no wheels" : null;
        }
    }
}
