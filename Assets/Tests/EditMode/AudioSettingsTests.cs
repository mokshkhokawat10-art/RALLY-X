using System.IO;
using NUnit.Framework;
using RallyX.Audio;
using RallyX.SaveSystem;
using UnityEngine;

namespace RallyX.Tests
{
    /// <summary>
    /// The Phase 12 settings and the sound-only engine: a save from before the settings existed loads with the default
    /// volumes and keeps everything else, volumes are clamped and saved straight away, a hand-edited file can't set
    /// volumes out of range, and the engine's revs and gears follow the car's speed like a real gearbox would.
    /// </summary>
    public sealed class AudioSettingsTests
    {
        private string path;
        private VehicleAudioConfig engineConfig;

        [SetUp]
        public void SetUp()
        {
            path = Path.Combine(Path.GetTempPath(), $"rallyx-settings-test-{System.Guid.NewGuid():N}.json");
            engineConfig = ScriptableObject.CreateInstance<VehicleAudioConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete(path);
            Object.DestroyImmediate(engineConfig);
        }

        [Test]
        public void SaveFromBeforeSettings_LoadsWithDefaultVolumesAndKeepsProgress()
        {
            File.WriteAllText(path, "{\n    \"version\": 2,\n    \"career\": { \"started\": true, \"credits\": 4200, \"creditsEarned\": 0, " +
                                    "\"racesFinished\": 3, \"wins\": 1 },\n    \"garage\": { \"selectedVehicle\": \"kestrel-s\", " +
                                    "\"unlockedVehicles\": [], \"upgrades\": [] },\n    \"tracks\": { \"selectedTrack\": \"\", " +
                                    "\"unlockedTracks\": [], \"records\": [] }\n}");

            var profile = PlayerProfile.Load(path);

            Assert.AreEqual(4200, profile.Credits, "Progress must survive.");
            Assert.AreEqual("kestrel-s", profile.SelectedVehicleId);
            foreach (AudioChannel channel in System.Enum.GetValues(typeof(AudioChannel)))
                Assert.AreEqual(PlayerProfile.DefaultVolume(channel), profile.GetVolume(channel), 1e-5f, $"{channel} should have its default.");
            Assert.IsTrue(profile.Vibration);
        }

        [Test]
        public void Volumes_AreClampedAndSavedStraightAway()
        {
            var profile = PlayerProfile.Load(path);
            profile.SetVolume(AudioChannel.Music, 0.3f);
            profile.SetVolume(AudioChannel.Vehicle, 1.7f);
            profile.SetVolume(AudioChannel.Master, -2f);
            profile.SetVibration(false);

            var reloaded = PlayerProfile.Load(path);
            Assert.AreEqual(0.3f, reloaded.GetVolume(AudioChannel.Music), 1e-5f);
            Assert.AreEqual(1f, reloaded.GetVolume(AudioChannel.Vehicle), 1e-5f);
            Assert.AreEqual(0f, reloaded.GetVolume(AudioChannel.Master), 1e-5f);
            Assert.AreEqual(PlayerProfile.DefaultVolume(AudioChannel.Effects), reloaded.GetVolume(AudioChannel.Effects), 1e-5f);
            Assert.IsFalse(reloaded.Vibration);
        }

        [Test]
        public void HandEditedVolumes_AreClampedOnLoad()
        {
            File.WriteAllText(path, "{ \"version\": 2, \"settings\": { \"initialized\": true, \"masterVolume\": 5, \"musicVolume\": -1, " +
                                    "\"effectsVolume\": 0.5, \"vehicleVolume\": 0.25, \"vibration\": false } }");

            var profile = PlayerProfile.Load(path);
            Assert.AreEqual(1f, profile.GetVolume(AudioChannel.Master), 1e-5f);
            Assert.AreEqual(0f, profile.GetVolume(AudioChannel.Music), 1e-5f);
            Assert.AreEqual(0.5f, profile.GetVolume(AudioChannel.Effects), 1e-5f);
            Assert.AreEqual(0.25f, profile.GetVolume(AudioChannel.Vehicle), 1e-5f);
            Assert.IsFalse(profile.Vibration);
        }

        [Test]
        public void Engine_ChangesUpThroughTheGearsAndDownAgain()
        {
            var engine = new EngineRevs();
            engine.Reset(engineConfig);
            const float topSpeed = 45f;
            const float step = 0.02f;
            int highestGear = 1;
            int lastGear = 1;
            bool gearsOnlyRose = true;

            // Full throttle from standstill to top speed over 20 s.
            for (float t = 0f; t < 20f; t += step)
            {
                engine.Step(engineConfig, topSpeed * t / 20f, topSpeed, 11f, 1f, false, 0f, false, step);
                Assert.That(engine.Rpm, Is.InRange(engineConfig.IdleRpm, engineConfig.RedlineRpm), "Revs stay between idle and the redline.");
                gearsOnlyRose &= engine.Gear >= lastGear;
                lastGear = engine.Gear;
                highestGear = Mathf.Max(highestGear, engine.Gear);
            }

            float rpmAtTop = engine.Rpm;
            Assert.IsTrue(gearsOnlyRose, "Accelerating only changes up.");
            Assert.AreEqual(engineConfig.GearTopSpeeds.Length, highestGear, "It reaches top gear.");
            Assert.Greater(rpmAtTop, engineConfig.RedlineRpm * 0.85f, "At top speed the engine is near the redline.");

            // Coasting back down to walking pace.
            for (float t = 0f; t < 20f; t += step)
                engine.Step(engineConfig, topSpeed * (1f - t / 20f) + 1f, topSpeed, 11f, 0f, false, 0f, false, step);
            Assert.AreEqual(1, engine.Gear, "Slowing down changes back down to first.");
        }

        [Test]
        public void Engine_RevsFreelyWhenHeld_AndEngagesReverse()
        {
            var engine = new EngineRevs();
            engine.Reset(engineConfig);
            for (int i = 0; i < 100; i++)
                engine.Step(engineConfig, 0f, 45f, 11f, 1f, true, 0f, false, 0.02f);
            Assert.Greater(engine.Rpm, engineConfig.RedlineRpm * 0.9f, "Held on the grid, full throttle revs the engine to the limiter.");

            for (int i = 0; i < 100; i++)
                engine.Step(engineConfig, 0f, 45f, 11f, 0f, true, 0f, false, 0.02f);
            Assert.Less(engine.Rpm, engineConfig.IdleRpm * 1.05f, "Off the throttle it drops back to idle.");

            engine.Step(engineConfig, -3f, 45f, 11f, 1f, false, 0f, true, 0.02f);
            Assert.AreEqual(-1, engine.Gear, "Reversing selects reverse.");
        }
    }
}
