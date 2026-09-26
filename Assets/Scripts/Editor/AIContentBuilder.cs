using RallyX.AI;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>Creates the shared AI settings and the default AI driver profiles if they are missing.</summary>
    public static class AIContentBuilder
    {
        public const string SettingsPath = "Assets/Settings/AI/AISettings_Default.asset";
        private const string DriversFolder = "Assets/Settings/AI/Drivers";

        // Fastest first. Different lanes spread the field out; different shortcut habits vary the races.
        private static readonly DriverDefinition[] DefaultDrivers =
        {
            new DriverDefinition("Lind", new Color(0.12f, 0.33f, 0.85f), cornerGrip: 8.5f, braking: 11f, topSpeed: 0.97f, laneOffset: 0f, shortcutChance: 0.6f),
            new DriverDefinition("Petrov", new Color(0.1f, 0.55f, 0.22f), cornerGrip: 7.8f, braking: 10f, topSpeed: 0.94f, laneOffset: 0.7f, shortcutChance: 0.35f),
            new DriverDefinition("Moreno", new Color(0.95f, 0.78f, 0.12f), cornerGrip: 7f, braking: 9f, topSpeed: 0.9f, laneOffset: -0.7f, shortcutChance: 0.2f),
        };

        [MenuItem("RALLY X/Build/Create Missing AI Content")]
        public static void BuildMissingFromMenu() => BuildMissing();

        public static bool BuildMissing()
        {
            EditorAssetUtility.LoadOrCreate(SettingsPath, ScriptableObject.CreateInstance<AISettings>);
            foreach (var driver in DefaultDrivers)
            {
                EditorAssetUtility.LoadOrCreate(DriverPath(driver.Name), () => AIDriverProfile.Create(driver.Name, driver.Paint,
                    driver.CornerGrip, driver.Braking, driver.TopSpeed, driver.LaneOffset, driver.ShortcutChance));
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        public static AISettings LoadSettings() => AssetDatabase.LoadAssetAtPath<AISettings>(SettingsPath);

        /// <summary>The default drivers in grid order, or null if any is missing.</summary>
        public static AIDriverProfile[] LoadDrivers()
        {
            var drivers = new AIDriverProfile[DefaultDrivers.Length];
            for (int i = 0; i < drivers.Length; i++)
            {
                drivers[i] = AssetDatabase.LoadAssetAtPath<AIDriverProfile>(DriverPath(DefaultDrivers[i].Name));
                if (drivers[i] == null)
                    return null;
            }

            return drivers;
        }

        private static string DriverPath(string driverName) => $"{DriversFolder}/AIDriver_{driverName}.asset";

        private readonly struct DriverDefinition
        {
            public readonly string Name;
            public readonly Color Paint;
            public readonly float CornerGrip;
            public readonly float Braking;
            public readonly float TopSpeed;
            public readonly float LaneOffset;
            public readonly float ShortcutChance;

            public DriverDefinition(string name, Color paint, float cornerGrip, float braking, float topSpeed, float laneOffset,
                float shortcutChance)
            {
                Name = name;
                Paint = paint;
                CornerGrip = cornerGrip;
                Braking = braking;
                TopSpeed = topSpeed;
                LaneOffset = laneOffset;
                ShortcutChance = shortcutChance;
            }
        }
    }
}
