using System;
using System.Collections.Generic;
using RallyX.SaveSystem;
using UnityEngine;

namespace RallyX.Progression
{
    /// <summary>
    /// A track in the career: its id and name, the scene raced, and what unlocks it. A track with no requirements is
    /// open from the start; otherwise it opens once the player has enough wins and, if set, a good enough finish on
    /// the track before it. Once met, the unlock is kept in the profile.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Track Info", fileName = "Track")]
    public sealed class TrackInfo : ScriptableObject
    {
        [Tooltip("Stable id saved in the player's profile (unlocks and records).")]
        [SerializeField] private string id = "track";

        [SerializeField] private string displayName = "Track";

        [Tooltip("Scene loaded to race the track. It must be in the build scene list.")]
        [SerializeField] private string sceneName = string.Empty;

        [Header("Unlock")]
        [Tooltip("Race wins needed in total. 0 = no wins needed.")]
        [SerializeField, Min(0)] private int winsToUnlock;

        [Tooltip("Track that must be finished first, or none.")]
        [SerializeField] private TrackInfo previousTrack;

        [Tooltip("Position the previous track must be finished in or better (1 = won it). 0 = any finish.")]
        [SerializeField, Min(0)] private int positionOnPreviousTrack;

        public string Id => id;
        public string DisplayName => displayName;
        public string SceneName => sceneName;
        public int WinsToUnlock => winsToUnlock;
        public TrackInfo PreviousTrack => previousTrack;
        public int PositionOnPreviousTrack => positionOnPreviousTrack;

        /// <summary>True for a track open from the start.</summary>
        public bool OpenFromStart => winsToUnlock <= 0 && previousTrack == null;

        public static TrackInfo Create(string id, string displayName, string sceneName, int winsToUnlock = 0, TrackInfo previousTrack = null,
            int positionOnPreviousTrack = 0)
        {
            var track = CreateInstance<TrackInfo>();
            track.name = displayName;
            track.id = id;
            track.displayName = displayName;
            track.sceneName = sceneName;
            track.winsToUnlock = winsToUnlock;
            track.previousTrack = previousTrack;
            track.positionOnPreviousTrack = positionOnPreviousTrack;
            return track;
        }

        public bool IsUnlocked(PlayerProfile profile) => OpenFromStart || profile.HasUnlockedTrack(id) || MeetsRequirements(profile);

        /// <summary>True when the player has done what the track asks for.</summary>
        public bool MeetsRequirements(PlayerProfile profile)
        {
            if (profile.Wins < winsToUnlock)
                return false;
            if (previousTrack == null)
                return true;

            var record = profile.GetTrackRecord(previousTrack.Id);
            return record.Finishes > 0 && (positionOnPreviousTrack <= 0 || record.BestPosition <= positionOnPreviousTrack);
        }

        /// <summary>What the player still has to do to unlock the track, or empty when it is unlocked.</summary>
        public string UnlockHint(PlayerProfile profile)
        {
            if (IsUnlocked(profile))
                return string.Empty;

            var needs = new List<string>();
            if (profile.Wins < winsToUnlock)
                needs.Add($"WIN {winsToUnlock} {(winsToUnlock == 1 ? "RACE" : "RACES")} ({profile.Wins}/{winsToUnlock})");
            if (previousTrack != null)
            {
                string how = positionOnPreviousTrack == 1 ? "WIN AT" : positionOnPreviousTrack > 1 ? $"FINISH TOP {positionOnPreviousTrack} AT" : "FINISH";
                needs.Add($"{how} {previousTrack.DisplayName.ToUpperInvariant()}");
            }

            return string.Join(" AND ", needs);
        }
    }
}
