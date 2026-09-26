using RallyX.Garage;
using RallyX.Progression;
using RallyX.Vehicle;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Creates the career data if it is missing: the progression settings (credits per position, starting credits),
    /// the upgrade catalogue with each stage's price and effect, and the track list with Pine Valley. Existing assets
    /// are never overwritten, so prices and rewards tuned in the Editor are kept.
    /// </summary>
    public static class ProgressionContentBuilder
    {
        public const string SettingsPath = "Assets/Settings/Progression/ProgressionSettings.asset";
        public const string UpgradesPath = "Assets/Settings/Progression/UpgradeCatalog.asset";
        public const string TracksPath = "Assets/Settings/Progression/TrackRoster.asset";
        public const string PineValleyTrackPath = "Assets/Settings/Progression/Tracks/Track_PineValley.asset";

        private const string LogPrefix = "[RALLY X Build] ";
        private const string PineValleyId = "pine-valley";

        // Three stages each, every stage dearer and better. Effects are totals with that stage installed.
        private static UpgradeDefinition[] DefaultUpgrades() => new[]
        {
            Upgrade(UpgradeType.Engine, "Engine", "More power everywhere: quicker off the line and out of every corner.",
                (1500, new VehicleTuning(acceleration: 0.06f)),
                (3000, new VehicleTuning(acceleration: 0.12f)),
                (5000, new VehicleTuning(acceleration: 0.2f))),
            Upgrade(UpgradeType.Turbo, "Turbo", "Boost that builds with speed: stronger pull at high speed and a little more top speed.",
                (2000, new VehicleTuning(highSpeedPull: 0.1f, topSpeed: 0.01f)),
                (3500, new VehicleTuning(highSpeedPull: 0.2f, topSpeed: 0.02f)),
                (6000, new VehicleTuning(highSpeedPull: 0.3f, topSpeed: 0.03f))),
            Upgrade(UpgradeType.Tires, "Tires", "Stickier compounds: more grip in corners on every surface.",
                (1000, new VehicleTuning(grip: 0.04f)),
                (2200, new VehicleTuning(grip: 0.08f)),
                (4000, new VehicleTuning(grip: 0.12f))),
            Upgrade(UpgradeType.Brakes, "Brakes", "Bigger discs and calipers: stop shorter and brake later.",
                (800, new VehicleTuning(braking: 0.08f)),
                (1800, new VehicleTuning(braking: 0.16f)),
                (3200, new VehicleTuning(braking: 0.25f))),
            Upgrade(UpgradeType.Suspension, "Suspension", "Stiffer, sharper set-up: more steering at speed and less body roll.",
                (1200, new VehicleTuning(steering: 0.08f, stability: 0.05f)),
                (2500, new VehicleTuning(steering: 0.16f, stability: 0.1f)),
                (4200, new VehicleTuning(steering: 0.25f, stability: 0.15f))),
            Upgrade(UpgradeType.Gearbox, "Gearbox", "Longer ratios: a higher top speed on the straights.",
                (1500, new VehicleTuning(topSpeed: 0.03f)),
                (3000, new VehicleTuning(topSpeed: 0.06f)),
                (5200, new VehicleTuning(topSpeed: 0.09f))),
        };

        [MenuItem("RALLY X/Build/Create Missing Progression Content")]
        public static void BuildMissingFromMenu() => BuildMissing();

        public static bool BuildMissing()
        {
            var upgrades = EditorAssetUtility.LoadOrCreate(UpgradesPath, () =>
            {
                var catalog = ScriptableObject.CreateInstance<UpgradeCatalog>();
                catalog.SetUpgrades(DefaultUpgrades());
                return catalog;
            });

            string pineValleyScene = PineValleyTrack.LoadOrCreate().SceneName;
            var pineValley = EditorAssetUtility.LoadOrCreate(PineValleyTrackPath,
                () => TrackInfo.Create(PineValleyId, "Pine Valley", pineValleyScene));
            var tracks = EditorAssetUtility.LoadOrCreate(TracksPath, () =>
            {
                var roster = ScriptableObject.CreateInstance<TrackRoster>();
                roster.SetTracks(new[] { pineValley });
                return roster;
            });

            var settings = EditorAssetUtility.LoadOrCreate(SettingsPath, () =>
            {
                var created = ScriptableObject.CreateInstance<ProgressionSettings>();
                created.Configure(upgrades, tracks);
                return created;
            });

            AssetDatabase.SaveAssets();
            string problem = settings.FindProblem();
            if (problem != null)
            {
                Debug.LogError(LogPrefix + $"{SettingsPath} is not usable: {problem}.");
                return false;
            }

            return true;
        }

        public static ProgressionSettings LoadSettings() => AssetDatabase.LoadAssetAtPath<ProgressionSettings>(SettingsPath);

        public static TrackInfo LoadPineValley() => AssetDatabase.LoadAssetAtPath<TrackInfo>(PineValleyTrackPath);

        private static UpgradeDefinition Upgrade(UpgradeType type, string displayName, string description, params (int cost, VehicleTuning tuning)[] stages)
        {
            var built = new UpgradeStage[stages.Length];
            for (int i = 0; i < stages.Length; i++)
                built[i] = new UpgradeStage(stages[i].cost, stages[i].tuning);
            return new UpgradeDefinition(type, displayName, description, built);
        }
    }
}
