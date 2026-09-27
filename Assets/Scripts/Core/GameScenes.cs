using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RallyX.Core
{
    /// <summary>
    /// Names of the game's front-end scenes and moving between scenes. A scene change goes through <see cref="Loader"/> when
    /// one is set (the loading screen), which covers the screen before the scene is swapped.
    /// </summary>
    public static class GameScenes
    {
        public const string MainMenu = "MainMenu";
        public const string Garage = "Garage";

        /// <summary>
        /// Takes over a scene change (the loading screen): returns true if it will load the scene, false to leave it to a plain
        /// load. Set by the UI when the game starts.
        /// </summary>
        public static Func<string, bool> Loader { get; set; }

        /// <summary>True if the scene is in the build scene list and can be loaded.</summary>
        public static bool CanLoad(string sceneName) => !string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);

        /// <summary>
        /// Replaces the open scenes with <paramref name="sceneName"/> (behind the loading screen when there is one). Returns false
        /// (and logs why) if it isn't in the build.
        /// </summary>
        public static bool Load(string sceneName)
        {
            if (!CanLoad(sceneName))
            {
                Debug.LogError($"[RALLY X] Scene '{sceneName}' is not in the build scene list. Run RALLY X > Build > Rebuild Menu Scenes.");
                return false;
            }

            if (Loader == null || !Loader(sceneName))
                SceneManager.LoadScene(sceneName);
            return true;
        }
    }
}
