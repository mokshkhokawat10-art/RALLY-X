using System;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Every generated track, in career order, and building their scenes. A new track needs its authoring class (like
    /// <see cref="PineValleyTrack"/>) listed in <see cref="LoadOrCreateAll"/>, a TrackInfo in the career's track roster
    /// (<see cref="ProgressionContentBuilder"/>) and, for convenience, a menu item here.
    /// </summary>
    public static class TrackCatalog
    {
        private const string LogPrefix = "[RALLY X Build] ";

        /// <summary>Every track's definition, creating any that are missing.</summary>
        public static TrackDefinition[] LoadOrCreateAll() => new[]
        {
            PineValleyTrack.LoadOrCreate(),
            RedCanyonTrack.LoadOrCreate(),
        };

        /// <summary>The definition of the track with this scene name, or null.</summary>
        public static TrackDefinition Find(string sceneName) =>
            Array.Find(LoadOrCreateAll(), definition => definition.SceneName == sceneName);

        [MenuItem("RALLY X/Build/Rebuild Pine Valley Track")]
        public static void RebuildPineValleyFromMenu() => TrackSceneBuilder.Build(PineValleyTrack.LoadOrCreate());

        [MenuItem("RALLY X/Build/Rebuild Red Canyon Track")]
        public static void RebuildRedCanyonFromMenu() => TrackSceneBuilder.Build(RedCanyonTrack.LoadOrCreate());

        [MenuItem("RALLY X/Build/Rebuild All Tracks")]
        public static void RebuildAllFromMenu() => BuildAll();

        /// <summary>Regenerates every track scene. Stops at the first that fails.</summary>
        public static bool BuildAll()
        {
            foreach (var definition in LoadOrCreateAll())
            {
                if (!TrackSceneBuilder.Build(definition))
                    return false;
            }

            return true;
        }

        /// <summary>Builds the scenes of tracks that don't have one yet; existing scenes are left alone.</summary>
        public static bool BuildMissing()
        {
            foreach (var definition in LoadOrCreateAll())
            {
                if (TrackSceneBuilder.SceneExists(definition))
                    continue;

                Debug.Log(LogPrefix + $"{definition.DisplayName} has no scene yet; building it.");
                if (!TrackSceneBuilder.Build(definition))
                    return false;
            }

            return true;
        }
    }
}
