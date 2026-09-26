using System.IO;
using RallyX.SaveSystem;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>Development menu commands for the player profile (the save file with the chosen car, wins and unlocks).</summary>
    public static class ProfileDevTools
    {
        [MenuItem("RALLY X/Dev/Unlock All Cars")]
        public static void UnlockAllCars()
        {
            var roster = GarageContentBuilder.LoadRoster();
            if (roster == null)
            {
                Debug.LogError("[RALLY X] No vehicle roster. Run RALLY X > Build > Create Missing Garage Content first.");
                return;
            }

            var profile = PlayerProfile.Current;
            foreach (var vehicle in roster.Vehicles)
            {
                if (vehicle != null)
                    profile.Unlock(vehicle.Id);
            }

            Debug.Log($"[RALLY X] Unlocked all {roster.Count} cars in {profile.FilePath}.");
        }

        [MenuItem("RALLY X/Dev/Add 10,000 Credits")]
        public static void AddCredits()
        {
            var profile = PlayerProfile.Current;
            profile.AddCredits(10000);
            Debug.Log($"[RALLY X] Added 10,000 credits: {profile.Credits} in {profile.FilePath}.");
        }

        [MenuItem("RALLY X/Dev/Reset Player Profile")]
        public static void ResetProfile()
        {
            string path = PlayerProfile.DefaultPath;
            if (!EditorUtility.DisplayDialog("Reset player profile",
                    $"Delete {path}? Credits, upgrades, the chosen car, wins, unlocks and best laps are lost.", "Reset", "Cancel"))
                return;

            SaveFile.Delete(path);
            PlayerProfile.Current = null;
            Debug.Log($"[RALLY X] Player profile reset ({path}).");
        }

        [MenuItem("RALLY X/Dev/Show Player Profile File")]
        public static void ShowProfile()
        {
            string path = PlayerProfile.DefaultPath;
            if (File.Exists(path))
                EditorUtility.RevealInFinder(path);
            else
                Debug.Log($"[RALLY X] No player profile yet; it is created at {path} when something is saved.");
        }
    }
}
