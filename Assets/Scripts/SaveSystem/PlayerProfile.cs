using System;
using System.Collections.Generic;
using System.IO;
using RallyX.Audio;
using UnityEngine;

namespace RallyX.SaveSystem
{
    /// <summary>A track's records in the player's profile.</summary>
    public readonly struct TrackRecord
    {
        public TrackRecord(float bestLap, string bestLapVehicle, int bestPosition, int finishes)
        {
            BestLap = bestLap;
            BestLapVehicle = bestLapVehicle;
            BestPosition = bestPosition;
            Finishes = finishes;
        }

        /// <summary>Fastest lap, seconds, or 0 if no lap has been completed.</summary>
        public float BestLap { get; }

        /// <summary>Id of the car the fastest lap was set in.</summary>
        public string BestLapVehicle { get; }

        /// <summary>Best finishing position, or 0 if the track has never been finished.</summary>
        public int BestPosition { get; }

        public int Finishes { get; }
    }

    /// <summary>
    /// What the game remembers about the player between sessions: credits, the chosen car, installed upgrades, unlocked
    /// cars and tracks, race results and best laps, and the settings. Each change is saved straight away through <see cref="SaveFile"/>
    /// (atomic, with a backup). Game code uses <see cref="Current"/>; tests swap in a profile kept in memory only.
    /// The profile only stores progress; the rules (prices, rewards, unlock requirements) live in the game's data.
    /// </summary>
    public sealed class PlayerProfile
    {
        public const string FileName = "profile.json";

        private static PlayerProfile current;

        private readonly string path;
        private readonly ProfileData data;

        private PlayerProfile(string path, ProfileData data)
        {
            this.path = path;
            this.data = data;
        }

        /// <summary>Something in the profile changed (and has been saved).</summary>
        public event Action Changed;

        /// <summary>The player's profile, loaded from disk on first use. Set it to swap profiles; null reloads from disk.</summary>
        public static PlayerProfile Current
        {
            get => current ??= Load(DefaultPath);
            set => current = value;
        }

        /// <summary>
        /// The current profile if one has been loaded or set, without loading it (null otherwise). For always-on systems
        /// such as audio, which follow the player's settings but must not be the first to read the save.
        /// </summary>
        public static PlayerProfile Loaded => current;

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>File the profile is saved to, or null for a profile kept in memory only.</summary>
        public string FilePath => path;

        // Career

        public int Credits => data.career.credits;

        /// <summary>Credits earned from races over the whole career.</summary>
        public int CreditsEarned => data.career.creditsEarned;

        /// <summary>Races the player has finished.</summary>
        public int RacesFinished => data.career.racesFinished;

        /// <summary>Races the player has finished first.</summary>
        public int Wins => data.career.wins;

        // Garage

        /// <summary>Id of the car chosen in the garage, or empty before the player has chosen one.</summary>
        public string SelectedVehicleId => data.garage.selectedVehicle;

        /// <summary>Cars unlocked outright or recorded as unlocked, on top of those every player starts with.</summary>
        public IReadOnlyList<string> UnlockedVehicles => data.garage.unlockedVehicles;

        // Tracks

        /// <summary>Id of the track chosen to race, or empty before the player has chosen one.</summary>
        public string SelectedTrackId => data.tracks.selectedTrack;

        public IReadOnlyList<string> UnlockedTracks => data.tracks.unlockedTracks;

        // Settings

        /// <summary>Gamepad rumble on or off.</summary>
        public bool Vibration => data.settings.vibration;

        /// <summary>A fresh profile that is never written to disk.</summary>
        public static PlayerProfile InMemory() => new PlayerProfile(null, new ProfileData());

        /// <summary>
        /// Loads a profile, falling back to its backup if the file is damaged. Profiles saved by earlier versions are
        /// upgraded. With neither readable, a fresh profile is used and saved to the same path on its first change.
        /// </summary>
        public static PlayerProfile Load(string filePath)
        {
            if (SaveFile.TryRead(filePath, ProfileData.Parse, out var data, out bool fromBackup))
            {
                if (fromBackup)
                    Debug.LogWarning($"[RALLY X] Player profile at {filePath} was damaged or missing; restored the backup.");
                return new PlayerProfile(filePath, data);
            }

            if (File.Exists(filePath) || File.Exists(SaveFile.BackupPath(filePath)))
                Debug.LogWarning($"[RALLY X] Player profile at {filePath} could not be read and was reset.");
            return new PlayerProfile(filePath, new ProfileData());
        }

        /// <summary>Gives a new career its starting credits, once.</summary>
        public void EnsureStarted(int startingCredits)
        {
            if (data.career.started)
                return;

            data.career.started = true;
            data.career.credits += Mathf.Max(0, startingCredits);
            SaveAndNotify();
        }

        public void AddCredits(int amount)
        {
            if (amount <= 0)
                return;

            data.career.credits += amount;
            SaveAndNotify();
        }

        /// <summary>Takes credits if there are enough. Returns false (changing nothing) otherwise.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0 || amount > data.career.credits)
                return false;

            data.career.credits -= amount;
            SaveAndNotify();
            return true;
        }

        public void SelectVehicle(string vehicleId)
        {
            if (SelectedVehicleId == vehicleId)
                return;

            data.garage.selectedVehicle = vehicleId ?? string.Empty;
            SaveAndNotify();
        }

        public bool HasUnlocked(string vehicleId) => data.garage.unlockedVehicles.Contains(vehicleId);

        public void Unlock(string vehicleId)
        {
            if (HasUnlocked(vehicleId))
                return;

            data.garage.unlockedVehicles.Add(vehicleId);
            SaveAndNotify();
        }

        /// <summary>Stage of an upgrade installed on a car (0 = none).</summary>
        public int GetUpgradeLevel(string vehicleId, string upgradeId) => FindUpgrade(vehicleId, upgradeId)?.level ?? 0;

        /// <summary>
        /// Buys the next stage of an upgrade for a car: takes the cost and installs it, in one save. Returns false,
        /// changing nothing, if the upgrade is already at <paramref name="maxLevel"/> or there aren't enough credits.
        /// </summary>
        public bool TryBuyUpgrade(string vehicleId, string upgradeId, int cost, int maxLevel)
        {
            var upgrade = FindUpgrade(vehicleId, upgradeId);
            int level = upgrade?.level ?? 0;
            if (level >= maxLevel || cost < 0 || cost > data.career.credits)
                return false;

            if (upgrade == null)
            {
                upgrade = new UpgradeData { vehicle = vehicleId, upgrade = upgradeId };
                data.garage.upgrades.Add(upgrade);
            }

            data.career.credits -= cost;
            upgrade.level = level + 1;
            SaveAndNotify();
            return true;
        }

        public void SelectTrack(string trackId)
        {
            if (SelectedTrackId == trackId)
                return;

            data.tracks.selectedTrack = trackId ?? string.Empty;
            SaveAndNotify();
        }

        public bool HasUnlockedTrack(string trackId) => data.tracks.unlockedTracks.Contains(trackId);

        public void UnlockTrack(string trackId)
        {
            if (HasUnlockedTrack(trackId))
                return;

            data.tracks.unlockedTracks.Add(trackId);
            SaveAndNotify();
        }

        /// <summary>A volume from the settings, 0..1.</summary>
        public float GetVolume(AudioChannel channel) => channel switch
        {
            AudioChannel.Music => data.settings.musicVolume,
            AudioChannel.Effects => data.settings.effectsVolume,
            AudioChannel.Vehicle => data.settings.vehicleVolume,
            _ => data.settings.masterVolume,
        };

        /// <summary>A volume a profile starts with, 0..1.</summary>
        public static float DefaultVolume(AudioChannel channel)
        {
            var defaults = new SettingsData();
            return channel switch
            {
                AudioChannel.Music => defaults.musicVolume,
                AudioChannel.Effects => defaults.effectsVolume,
                AudioChannel.Vehicle => defaults.vehicleVolume,
                _ => defaults.masterVolume,
            };
        }

        /// <summary>Sets a volume (clamped to 0..1) and saves it.</summary>
        public void SetVolume(AudioChannel channel, float volume)
        {
            volume = Mathf.Clamp01(volume);
            if (Mathf.Approximately(GetVolume(channel), volume))
                return;

            switch (channel)
            {
                case AudioChannel.Music:
                    data.settings.musicVolume = volume;
                    break;
                case AudioChannel.Effects:
                    data.settings.effectsVolume = volume;
                    break;
                case AudioChannel.Vehicle:
                    data.settings.vehicleVolume = volume;
                    break;
                default:
                    data.settings.masterVolume = volume;
                    break;
            }

            SaveAndNotify();
        }

        public void SetVibration(bool on)
        {
            if (Vibration == on)
                return;

            data.settings.vibration = on;
            SaveAndNotify();
        }

        public TrackRecord GetTrackRecord(string trackId)
        {
            var record = FindRecord(trackId, create: false);
            return record == null
                ? default
                : new TrackRecord(record.bestLap, record.bestLapVehicle, record.bestPosition, record.finishes);
        }

        /// <summary>Records a completed lap. Returns true if it is the track's new best lap.</summary>
        public bool RecordLap(string trackId, float lapTime, string vehicleId)
        {
            if (string.IsNullOrEmpty(trackId) || lapTime <= 0f)
                return false;

            var record = FindRecord(trackId, create: true);
            if (record.bestLap > 0f && lapTime >= record.bestLap)
                return false;

            record.bestLap = lapTime;
            record.bestLapVehicle = vehicleId;
            SaveAndNotify();
            return true;
        }

        /// <summary>
        /// Counts a finished race and pays its credits, in one save: finishing first (position 1) counts as a win,
        /// and the track's finish count and best position are kept when a track id is given.
        /// </summary>
        public void RecordRaceFinish(int position, int creditsEarned = 0, string trackId = null)
        {
            data.career.racesFinished++;
            if (position == 1)
                data.career.wins++;
            creditsEarned = Mathf.Max(0, creditsEarned);
            data.career.credits += creditsEarned;
            data.career.creditsEarned += creditsEarned;

            if (!string.IsNullOrEmpty(trackId))
            {
                var record = FindRecord(trackId, create: true);
                record.finishes++;
                if (position > 0 && (record.bestPosition == 0 || position < record.bestPosition))
                    record.bestPosition = position;
            }

            SaveAndNotify();
        }

        /// <summary>Writes the profile to its file (nothing for a profile kept in memory).</summary>
        public void Save()
        {
            if (!string.IsNullOrEmpty(path))
                SaveFile.Write(path, JsonUtility.ToJson(data, true));
        }

        private UpgradeData FindUpgrade(string vehicleId, string upgradeId) =>
            data.garage.upgrades.Find(upgrade => upgrade.vehicle == vehicleId && upgrade.upgrade == upgradeId);

        private TrackRecordData FindRecord(string trackId, bool create)
        {
            var record = data.tracks.records.Find(candidate => candidate.track == trackId);
            if (record == null && create)
            {
                record = new TrackRecordData { track = trackId };
                data.tracks.records.Add(record);
            }

            return record;
        }

        private void SaveAndNotify()
        {
            Save();
            Changed?.Invoke();
        }

        // Entering Play Mode without a domain reload keeps statics; start every session from the saved file.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => current = null;
    }
}
