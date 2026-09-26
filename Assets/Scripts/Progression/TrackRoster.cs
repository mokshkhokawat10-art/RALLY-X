using System;
using System.Collections.Generic;
using RallyX.SaveSystem;
using UnityEngine;

namespace RallyX.Progression
{
    /// <summary>The career's tracks in order. The first is raced until the player picks another.</summary>
    [CreateAssetMenu(menuName = "RALLY X/Track Roster", fileName = "TrackRoster")]
    public sealed class TrackRoster : ScriptableObject
    {
        [SerializeField] private TrackInfo[] tracks = Array.Empty<TrackInfo>();

        public IReadOnlyList<TrackInfo> Tracks => tracks;

        public void SetTracks(TrackInfo[] newTracks) => tracks = newTracks;

        public TrackInfo Find(string trackId) => string.IsNullOrEmpty(trackId) ? null : Array.Find(tracks, track => track != null && track.Id == trackId);

        /// <summary>The track to race: the chosen one if it is unlocked, otherwise the first unlocked track.</summary>
        public TrackInfo Resolve(PlayerProfile profile)
        {
            var chosen = Find(profile.SelectedTrackId);
            if (chosen != null && chosen.IsUnlocked(profile))
                return chosen;
            return Array.Find(tracks, track => track != null && track.IsUnlocked(profile));
        }

        /// <summary>Why the roster can't be used, or null when it is complete.</summary>
        public string FindProblem()
        {
            if (tracks.Length == 0)
                return "it has no tracks";
            var ids = new HashSet<string>();
            foreach (var track in tracks)
            {
                if (track == null)
                    return "a track slot is empty";
                if (string.IsNullOrEmpty(track.SceneName))
                    return $"{track.name} has no scene";
                if (!ids.Add(track.Id))
                    return $"two tracks share the id '{track.Id}'";
            }

            return tracks[0].OpenFromStart ? null : $"the first track {tracks[0].name} is locked";
        }
    }
}
