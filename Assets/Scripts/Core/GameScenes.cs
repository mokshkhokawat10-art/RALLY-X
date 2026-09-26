using UnityEngine;
using UnityEngine.SceneManagement;

namespace RallyX.Core
{
    /// <summary>Names of the game's front-end scenes and moving between scenes.</summary>
    public static class GameScenes
    {
        public const string MainMenu = "MainMenu";
        public const string Garage = "Garage";

        /// <summary>True if the scene is in the build scene list and can be loaded.</summary>
        public static bool CanLoad(string sceneName) => !string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);

        /// <summary>Replaces the open scenes with <paramref name="sceneName"/>. Returns false (and logs why) if it isn't in the build.</summary>
        public static bool Load(string sceneName)
        {
            if (!CanLoad(sceneName))
            {
                Debug.LogError($"[RALLY X] Scene '{sceneName}' is not in the build scene list. Run RALLY X > Build > Rebuild Menu Scenes.");
                return false;
            }

            SceneManager.LoadScene(sceneName);
            return true;
        }
    }
}
