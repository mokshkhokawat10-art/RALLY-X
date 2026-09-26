using System;
using System.Collections.Generic;
using RallyX.SaveSystem;
using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.Garage
{
    public enum UpgradeType
    {
        Engine,
        Turbo,
        Tires,
        Brakes,
        Suspension,
        Gearbox,
    }

    /// <summary>One stage of an upgrade: its price and the car's total improvement once it is installed.</summary>
    [Serializable]
    public sealed class UpgradeStage
    {
        [SerializeField, Min(0)] private int cost;

        [Tooltip("Improvement with this stage installed (in total, not on top of the stage before).")]
        [SerializeField] private VehicleTuning tuning;

        public UpgradeStage(int cost, VehicleTuning tuning)
        {
            this.cost = cost;
            this.tuning = tuning;
        }

        public int Cost => cost;
        public VehicleTuning Tuning => tuning;
    }

    /// <summary>A part that can be upgraded in stages, each dearer and better than the last.</summary>
    [Serializable]
    public sealed class UpgradeDefinition
    {
        [SerializeField] private UpgradeType type;
        [SerializeField] private string displayName = "Upgrade";
        [SerializeField, TextArea(1, 3)] private string description = string.Empty;
        [SerializeField] private UpgradeStage[] stages = Array.Empty<UpgradeStage>();

        public UpgradeDefinition(UpgradeType type, string displayName, string description, UpgradeStage[] stages)
        {
            this.type = type;
            this.displayName = displayName;
            this.description = description;
            this.stages = stages;
        }

        public UpgradeType Type => type;

        /// <summary>Id saved in the player's profile.</summary>
        public string Id => type.ToString();

        public string DisplayName => displayName;
        public string Description => description;
        public int MaxLevel => stages.Length;
        public IReadOnlyList<UpgradeStage> Stages => stages;

        /// <summary>Price of a stage (1 = the first stage).</summary>
        public int CostOf(int level) => stages[level - 1].Cost;

        /// <summary>The car's improvement with the upgrade at a stage (0 = not installed).</summary>
        public VehicleTuning TuningAt(int level) => level <= 0 ? default : stages[Mathf.Min(level, stages.Length) - 1].Tuning;
    }

    /// <summary>
    /// The upgrades on sale in the garage, with each stage's price and effect. Upgrades are bought per car; a car's
    /// installed upgrades together make the <see cref="VehicleTuning"/> applied to it on the track.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Upgrade Catalog", fileName = "UpgradeCatalog")]
    public sealed class UpgradeCatalog : ScriptableObject
    {
        [SerializeField] private UpgradeDefinition[] upgrades = Array.Empty<UpgradeDefinition>();

        public IReadOnlyList<UpgradeDefinition> Upgrades => upgrades;

        public void SetUpgrades(UpgradeDefinition[] newUpgrades) => upgrades = newUpgrades;

        public UpgradeDefinition Find(UpgradeType type) => Array.Find(upgrades, upgrade => upgrade.Type == type);

        /// <summary>Everything installed on a car.</summary>
        public VehicleTuning TuningFor(PlayerProfile profile, string vehicleId)
        {
            var tuning = default(VehicleTuning);
            foreach (var upgrade in upgrades)
                tuning += upgrade.TuningAt(profile.GetUpgradeLevel(vehicleId, upgrade.Id));
            return tuning;
        }

        /// <summary>Everything installed on a car, with one upgrade at another stage (to preview buying it).</summary>
        public VehicleTuning TuningWith(PlayerProfile profile, string vehicleId, UpgradeDefinition changed, int level)
        {
            var tuning = default(VehicleTuning);
            foreach (var upgrade in upgrades)
                tuning += upgrade.TuningAt(upgrade == changed ? level : profile.GetUpgradeLevel(vehicleId, upgrade.Id));
            return tuning;
        }

        /// <summary>Why the catalogue can't be used, or null when it is complete.</summary>
        public string FindProblem()
        {
            var seen = new HashSet<UpgradeType>();
            foreach (var upgrade in upgrades)
            {
                if (upgrade == null)
                    return "an upgrade slot is empty";
                if (!seen.Add(upgrade.Type))
                    return $"{upgrade.Type} is listed twice";
                if (upgrade.MaxLevel == 0)
                    return $"{upgrade.DisplayName} has no stages";
                for (int level = 2; level <= upgrade.MaxLevel; level++)
                {
                    if (upgrade.CostOf(level) <= upgrade.CostOf(level - 1))
                        return $"{upgrade.DisplayName} stage {level} should cost more than stage {level - 1}";
                }
            }

            return null;
        }
    }

    /// <summary>Why an upgrade can or can't be bought.</summary>
    public enum PurchaseCheck
    {
        Available,
        CarLocked,
        Maxed,
        NotEnoughCredits,
    }

    /// <summary>The garage's buying rules: a car must be unlocked, the upgrade not maxed, and the credits there.</summary>
    public static class UpgradeShop
    {
        public static int NextLevel(PlayerProfile profile, VehicleDefinition vehicle, UpgradeDefinition upgrade) =>
            profile.GetUpgradeLevel(vehicle.Id, upgrade.Id) + 1;

        /// <summary>Price of the next stage, or 0 when the upgrade is maxed.</summary>
        public static int NextCost(PlayerProfile profile, VehicleDefinition vehicle, UpgradeDefinition upgrade)
        {
            int next = NextLevel(profile, vehicle, upgrade);
            return next <= upgrade.MaxLevel ? upgrade.CostOf(next) : 0;
        }

        public static PurchaseCheck Check(PlayerProfile profile, VehicleDefinition vehicle, UpgradeDefinition upgrade)
        {
            if (!vehicle.IsUnlocked(profile))
                return PurchaseCheck.CarLocked;
            if (NextLevel(profile, vehicle, upgrade) > upgrade.MaxLevel)
                return PurchaseCheck.Maxed;
            return profile.Credits < NextCost(profile, vehicle, upgrade) ? PurchaseCheck.NotEnoughCredits : PurchaseCheck.Available;
        }

        /// <summary>Buys the next stage if <see cref="Check"/> allows it. Returns the check that decided.</summary>
        public static PurchaseCheck Buy(PlayerProfile profile, VehicleDefinition vehicle, UpgradeDefinition upgrade)
        {
            var check = Check(profile, vehicle, upgrade);
            if (check == PurchaseCheck.Available &&
                !profile.TryBuyUpgrade(vehicle.Id, upgrade.Id, NextCost(profile, vehicle, upgrade), upgrade.MaxLevel))
                return PurchaseCheck.NotEnoughCredits;
            return check;
        }
    }
}
