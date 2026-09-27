using RallyX.Progression;
using RallyX.UI;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Bakes each track's map for the menus: the lap's centreline from its <see cref="TrackDefinition"/> (the same road the
    /// track builder lays), thinned to a few hundred points and fitted into a unit square, plus the lap length, stored on the
    /// matching <see cref="TrackInfo"/> (by scene name). Reads the definitions only; no scene is opened or changed.
    /// </summary>
    public static class TrackMapBaker
    {
        private const int MapPoints = 220;
        private const string LogPrefix = "[RALLY X Build] ";

        [MenuItem("RALLY X/Build/Bake Track Maps")]
        public static void BakeFromMenu() => Bake();

        /// <summary>Returns false (and logs why) if there are no progression settings or a track has no TrackInfo.</summary>
        public static bool Bake()
        {
            var settings = ProgressionContentBuilder.LoadSettings();
            if (settings == null || settings.Tracks == null)
            {
                Debug.LogError(LogPrefix + "No progression settings with a track roster. Run RALLY X > Build > Create Missing Progression Content.");
                return false;
            }

            bool ok = true;
            foreach (var definition in TrackCatalog.LoadOrCreateAll())
            {
                var info = FindInfo(settings, definition.SceneName);
                if (info == null)
                {
                    Debug.LogError(LogPrefix + $"No TrackInfo raced in scene '{definition.SceneName}'.");
                    ok = false;
                    continue;
                }

                var road = TrackLayout.Build(definition).Main;
                var points = new Vector2[Mathf.Min(MapPoints, road.Count)];
                for (int i = 0; i < points.Length; i++)
                {
                    var sample = road.Get(Mathf.RoundToInt(i * road.Count / (float)points.Length));
                    points[i] = new Vector2(sample.Position.x, sample.Position.z);
                }

                info.SetMap(TrackMap.Normalise(points, out _, out _), road.Length);
                EditorUtility.SetDirty(info);
                Debug.Log(LogPrefix + $"Baked the map of {info.DisplayName}: {points.Length} points, lap {road.Length / 1000f:0.00} km.");
            }

            AssetDatabase.SaveAssets();
            return ok;
        }

        private static TrackInfo FindInfo(ProgressionSettings settings, string sceneName)
        {
            foreach (var track in settings.Tracks.Tracks)
            {
                if (track != null && track.SceneName == sceneName)
                    return track;
            }

            return null;
        }
    }
}
