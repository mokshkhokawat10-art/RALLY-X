using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RallyX.SaveSystem;
using UnityEngine;
using UnityEngine.TestTools;

namespace RallyX.Tests
{
    /// <summary>
    /// The player profile and its save file: everything persists, damaged files fall back to the backup, Phase 7
    /// saves are upgraded, and purchases are all-or-nothing. Uses a temporary file.
    /// </summary>
    public sealed class PlayerProfileTests
    {
        private string path;

        [SetUp]
        public void ChooseFile() => path = Path.Combine(Path.GetTempPath(), $"rallyx-profile-test-{System.Guid.NewGuid():N}.json");

        [TearDown]
        public void DeleteFile() => SaveFile.Delete(path);

        [Test]
        public void NewProfile_HasNothingAndNoFile()
        {
            var profile = PlayerProfile.Load(path);
            Assert.AreEqual(string.Empty, profile.SelectedVehicleId);
            Assert.AreEqual(0, profile.RacesFinished);
            Assert.AreEqual(0, profile.Wins);
            Assert.AreEqual(0, profile.Credits);
            Assert.IsEmpty(profile.UnlockedVehicles);
            Assert.IsEmpty(profile.UnlockedTracks);
            Assert.AreEqual(0f, profile.GetTrackRecord("pine-valley").BestLap);
            Assert.IsFalse(File.Exists(path), "Nothing is written until something changes.");
        }

        [Test]
        public void Everything_IsSavedStraightAwayAndLoadsBack()
        {
            var profile = PlayerProfile.Load(path);
            int changes = 0;
            profile.Changed += () => changes++;
            profile.EnsureStarted(5000);
            profile.SelectVehicle("kestrel-s");
            profile.RecordRaceFinish(1, 1500, "pine-valley");
            profile.RecordRaceFinish(3, 700, "pine-valley");
            Assert.IsTrue(profile.TryBuyUpgrade("kestrel-s", "Engine", 1500, 3));
            Assert.IsTrue(profile.TryBuyUpgrade("kestrel-s", "Engine", 3000, 3));
            Assert.IsTrue(profile.TryBuyUpgrade("falcon-rx", "Brakes", 800, 3));
            profile.Unlock("titan-gt");
            profile.UnlockTrack("forest-ridge");
            profile.SelectTrack("forest-ridge");
            Assert.IsTrue(profile.RecordLap("pine-valley", 61.5f, "kestrel-s"));
            Assert.IsTrue(profile.RecordLap("pine-valley", 58.25f, "falcon-rx"));
            Assert.IsFalse(profile.RecordLap("pine-valley", 59f, "kestrel-s"), "Only a faster lap is a record.");
            Assert.AreEqual(12, changes);
            Assert.IsTrue(File.Exists(path));
            Assert.IsFalse(File.Exists(path + ".tmp"), "The temporary file is swapped in.");

            var loaded = PlayerProfile.Load(path);
            Assert.AreEqual(5000 + 1500 + 700 - 1500 - 3000 - 800, loaded.Credits);
            Assert.AreEqual(2200, loaded.CreditsEarned);
            Assert.AreEqual("kestrel-s", loaded.SelectedVehicleId);
            Assert.AreEqual(2, loaded.RacesFinished);
            Assert.AreEqual(1, loaded.Wins);
            Assert.AreEqual(2, loaded.GetUpgradeLevel("kestrel-s", "Engine"));
            Assert.AreEqual(1, loaded.GetUpgradeLevel("falcon-rx", "Brakes"));
            Assert.AreEqual(0, loaded.GetUpgradeLevel("falcon-rx", "Engine"), "Upgrades belong to one car.");
            Assert.IsTrue(loaded.HasUnlocked("titan-gt"));
            Assert.IsTrue(loaded.HasUnlockedTrack("forest-ridge"));
            Assert.AreEqual("forest-ridge", loaded.SelectedTrackId);
            var record = loaded.GetTrackRecord("pine-valley");
            Assert.AreEqual(58.25f, record.BestLap);
            Assert.AreEqual("falcon-rx", record.BestLapVehicle);
            Assert.AreEqual(1, record.BestPosition);
            Assert.AreEqual(2, record.Finishes);

            loaded.EnsureStarted(2000);
            Assert.AreEqual(profile.Credits, loaded.Credits, "Starting credits are given once per career.");
        }

        [Test]
        public void Purchases_AreAllOrNothing()
        {
            var profile = PlayerProfile.Load(path);
            profile.AddCredits(1000);
            int changes = 0;
            profile.Changed += () => changes++;

            Assert.IsFalse(profile.TryBuyUpgrade("falcon-rx", "Engine", 1500, 3), "Not enough credits.");
            Assert.IsFalse(profile.TrySpend(1001));
            Assert.AreEqual(0, changes, "A refused purchase changes nothing.");
            Assert.AreEqual(1000, profile.Credits);
            Assert.AreEqual(0, profile.GetUpgradeLevel("falcon-rx", "Engine"));

            Assert.IsTrue(profile.TryBuyUpgrade("falcon-rx", "Engine", 1000, 1));
            Assert.AreEqual(0, profile.Credits);
            Assert.AreEqual(1, profile.GetUpgradeLevel("falcon-rx", "Engine"));
            profile.AddCredits(5000);
            Assert.IsFalse(profile.TryBuyUpgrade("falcon-rx", "Engine", 1000, 1), "Past the last stage nothing more can be bought.");
            Assert.AreEqual(5000, profile.Credits);
        }

        [Test]
        public void DamagedFile_FallsBackToTheBackupOfTheSaveBefore()
        {
            var profile = PlayerProfile.Load(path);
            profile.AddCredits(1234);
            profile.AddCredits(1000);
            File.WriteAllText(path, "{ this is not json");

            LogAssert.Expect(LogType.Warning, new Regex("could not be read"));
            LogAssert.Expect(LogType.Warning, new Regex("restored the backup"));
            var loaded = PlayerProfile.Load(path);
            Assert.AreEqual(1234, loaded.Credits, "The backup holds the save before the last one.");
        }

        [Test]
        public void DamagedFileWithoutBackup_GivesAFreshProfileThatCanBeSavedAgain()
        {
            File.WriteAllText(path, "{ this is not json");
            LogAssert.Expect(LogType.Warning, new Regex("could not be read:"));
            LogAssert.Expect(LogType.Warning, new Regex("could not be read and was reset"));
            var profile = PlayerProfile.Load(path);
            Assert.AreEqual(string.Empty, profile.SelectedVehicleId);
            Assert.AreEqual(0, profile.Wins);

            profile.SelectVehicle("falcon-rx");
            Assert.AreEqual("falcon-rx", PlayerProfile.Load(path).SelectedVehicleId);
        }

        [Test]
        public void Phase7Save_IsUpgradedKeepingTheCarWinsAndUnlocks()
        {
            File.WriteAllText(path, "{\n    \"version\": 1,\n    \"selectedVehicle\": \"kestrel-s\",\n    \"unlockedVehicles\": [\n        \"titan-gt\"\n    ],\n" +
                                    "    \"racesFinished\": 3,\n    \"wins\": 1\n}");
            var profile = PlayerProfile.Load(path);
            Assert.AreEqual("kestrel-s", profile.SelectedVehicleId);
            Assert.IsTrue(profile.HasUnlocked("titan-gt"));
            Assert.AreEqual(3, profile.RacesFinished);
            Assert.AreEqual(1, profile.Wins);
            Assert.AreEqual(0, profile.Credits);

            profile.EnsureStarted(2000);
            Assert.AreEqual(2000, profile.Credits, "An upgraded Phase 7 career gets its starting credits.");
            StringAssert.Contains("\"version\": 2", File.ReadAllText(path));
            Assert.AreEqual("kestrel-s", PlayerProfile.Load(path).SelectedVehicleId);
        }

        [Test]
        public void InMemoryProfile_NeverTouchesTheDisk()
        {
            var profile = PlayerProfile.InMemory();
            profile.SelectVehicle("kestrel-s");
            profile.RecordRaceFinish(1, 1500);
            Assert.IsNull(profile.FilePath);
            Assert.AreEqual("kestrel-s", profile.SelectedVehicleId);
            Assert.AreEqual(1, profile.Wins);
            Assert.AreEqual(1500, profile.Credits);
        }
    }
}
