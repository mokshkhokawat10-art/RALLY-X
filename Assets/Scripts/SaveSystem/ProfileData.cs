using System;
using System.Collections.Generic;
using UnityEngine;

namespace RallyX.SaveSystem
{
    /// <summary>
    /// The player profile as saved: one section per area of the game, so each can grow without touching the others.
    /// Ids (cars, upgrades, tracks) are stored as strings, so reordering game data never scrambles a save.
    /// </summary>
    [Serializable]
    internal sealed class ProfileData
    {
        /// <summary>1 = Phase 7 (flat), 2 = sections.</summary>
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public CareerData career = new CareerData();
        public GarageData garage = new GarageData();
        public TrackData tracks = new TrackData();

        /// <summary>Reads a saved profile of any version. Throws if the text isn't a profile.</summary>
        public static ProfileData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("the file is empty");

            var header = JsonUtility.FromJson<VersionHeader>(json);
            var data = header.version <= 1 ? FromVersion1(JsonUtility.FromJson<Version1>(json)) : JsonUtility.FromJson<ProfileData>(json);
            if (header.version > CurrentVersion)
                Debug.LogWarning($"[RALLY X] The player profile is from a newer version ({header.version}); unknown data is ignored.");

            data.version = CurrentVersion;
            data.Repair();
            return data;
        }

        /// <summary>Fills in anything missing and clamps values a hand-edited or damaged file could have made invalid.</summary>
        private void Repair()
        {
            career ??= new CareerData();
            garage ??= new GarageData();
            tracks ??= new TrackData();
            career.credits = Mathf.Max(0, career.credits);
            garage.selectedVehicle ??= string.Empty;
            garage.unlockedVehicles ??= new List<string>();
            garage.upgrades ??= new List<UpgradeData>();
            garage.upgrades.RemoveAll(upgrade => upgrade == null || string.IsNullOrEmpty(upgrade.vehicle) || string.IsNullOrEmpty(upgrade.upgrade));
            tracks.selectedTrack ??= string.Empty;
            tracks.unlockedTracks ??= new List<string>();
            tracks.records ??= new List<TrackRecordData>();
            tracks.records.RemoveAll(record => record == null || string.IsNullOrEmpty(record.track));
        }

        private static ProfileData FromVersion1(Version1 old)
        {
            var data = new ProfileData();
            data.garage.selectedVehicle = old.selectedVehicle;
            data.garage.unlockedVehicles = old.unlockedVehicles ?? new List<string>();
            data.career.racesFinished = old.racesFinished;
            data.career.wins = old.wins;
            return data;
        }

        [Serializable]
        private sealed class VersionHeader
        {
            public int version = 1;
        }

        /// <summary>The Phase 7 profile: everything at the top level.</summary>
        [Serializable]
        private sealed class Version1
        {
            public string selectedVehicle;
            public List<string> unlockedVehicles;
            public int racesFinished;
            public int wins;
        }
    }

    [Serializable]
    internal sealed class CareerData
    {
        /// <summary>True once the starting credits have been given.</summary>
        public bool started;
        public int credits;
        public int creditsEarned;
        public int racesFinished;
        public int wins;
    }

    [Serializable]
    internal sealed class GarageData
    {
        public string selectedVehicle = string.Empty;
        public List<string> unlockedVehicles = new List<string>();
        public List<UpgradeData> upgrades = new List<UpgradeData>();
    }

    [Serializable]
    internal sealed class UpgradeData
    {
        public string vehicle;
        public string upgrade;
        public int level;
    }

    [Serializable]
    internal sealed class TrackData
    {
        public string selectedTrack = string.Empty;
        public List<string> unlockedTracks = new List<string>();
        public List<TrackRecordData> records = new List<TrackRecordData>();
    }

    [Serializable]
    internal sealed class TrackRecordData
    {
        public string track;
        public float bestLap;
        public string bestLapVehicle;
        public int bestPosition;
        public int finishes;
    }
}
