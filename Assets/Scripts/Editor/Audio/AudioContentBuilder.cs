using System;
using System.Collections.Generic;
using System.IO;
using RallyX.Audio;
using RallyX.Tracks;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Creates the game's audio content: the placeholder sounds (<see cref="PlaceholderSounds"/>, written as WAV files and
    /// regenerated in place when their recipe's revision changes, so references survive), the game audio config in a
    /// Resources folder (<see cref="GameAudioConfig"/>), a <see cref="VehicleAudioConfig"/> per car, the tyre sounds of each
    /// surface, and a <see cref="VehicleAudio"/> on each car prefab. Configs and surface sounds are only set when missing,
    /// so tuning and licensed replacements are never overwritten.
    /// </summary>
    public static class AudioContentBuilder
    {
        public const string ClipFolder = "Assets/Audio/Generated";
        public const string ConfigFolder = "Assets/Settings/Audio";
        public const string GameConfigPath = "Assets/Resources/RallyX/GameAudio.asset";

        private const string Revision = "audio-2";
        private const string LogPrefix = "[RALLY X Build] ";

        private static readonly (string name, Func<GeneratedSound> make)[] Recipes =
        {
            ("Engine_Low", () => PlaceholderSounds.Engine(high: false)),
            ("Engine_High", () => PlaceholderSounds.Engine(high: true)),
            ("Tyre_Asphalt_Roll", PlaceholderSounds.AsphaltRoll),
            ("Tyre_Asphalt_Slide", PlaceholderSounds.AsphaltSlide),
            ("Tyre_Gravel_Roll", PlaceholderSounds.GravelRoll),
            ("Tyre_Gravel_Slide", PlaceholderSounds.GravelSlide),
            ("Tyre_Dirt_Roll", PlaceholderSounds.DirtRoll),
            ("Tyre_Dirt_Slide", PlaceholderSounds.DirtSlide),
            ("Tyre_Mud_Roll", PlaceholderSounds.MudRoll),
            ("Tyre_Mud_Slide", PlaceholderSounds.MudSlide),
            ("Tyre_Grass_Roll", PlaceholderSounds.GrassRoll),
            ("Tyre_Grass_Slide", PlaceholderSounds.GrassSlide),
            ("Wind", PlaceholderSounds.Wind),
            ("Scrape", PlaceholderSounds.Scrape),
            ("Impact_Light", PlaceholderSounds.LightImpact),
            ("Impact_Heavy", PlaceholderSounds.HeavyImpact),
            ("Landing", PlaceholderSounds.Landing),
            ("GearShift", PlaceholderSounds.GearShift),
            ("Backfire", PlaceholderSounds.Backfire),
            ("UI_Navigate", PlaceholderSounds.Navigate),
            ("UI_Confirm", PlaceholderSounds.Confirm),
            ("UI_Back", PlaceholderSounds.Back),
            ("UI_Error", PlaceholderSounds.Error),
            ("UI_Purchase", PlaceholderSounds.Purchase),
            ("UI_Switch", PlaceholderSounds.Switch),
            ("Race_Countdown", PlaceholderSounds.CountdownTick),
            ("Race_Go", PlaceholderSounds.Go),
            ("Race_Lap", PlaceholderSounds.Lap),
            ("Race_FinalLap", PlaceholderSounds.FinalLap),
            ("Race_Finish", PlaceholderSounds.Finish),
            ("Race_Results", PlaceholderSounds.Results),
            ("Music_Menu", PlaceholderSounds.MenuMusic),
            ("Music_Race", PlaceholderSounds.RaceMusic),
        };

        // Each car prefab and the name of its audio config, with how its engine differs from the Falcon's.
        private static readonly (string prefab, string config, Action<SerializedObject> tune)[] Cars =
        {
            ("Assets/Prefabs/Vehicles/RallyCar.prefab", "RallyCar_Audio", null),

            // A small, eager engine: revs higher, sounds lighter, six gears.
            ("Assets/Prefabs/Vehicles/Kestrel.prefab", "Kestrel_Audio", config => Set(config,
                ("idleRpm", 1000f), ("redlineRpm", 8200f), ("launchRpm", 4300f), ("engineLowRpm", 1750f), ("engineHighRpm", 4500f),
                ("engineVolume", 0.7f), ("gearTopSpeeds", new[] { 0.22f, 0.36f, 0.5f, 0.65f, 0.82f, 1f }))),

            // A big, lazy engine: lower revs, deeper, louder, long gears.
            ("Assets/Prefabs/Vehicles/Titan.prefab", "Titan_Audio", config => Set(config,
                ("idleRpm", 850f), ("redlineRpm", 7000f), ("launchRpm", 3500f), ("engineLowRpm", 2350f), ("engineHighRpm", 5700f),
                ("engineVolume", 0.95f), ("offThrottleVolume", 0.5f), ("gearTopSpeeds", new[] { 0.3f, 0.48f, 0.66f, 0.84f, 1f }))),
        };

        [MenuItem("RALLY X/Build/Create Missing Audio Content")]
        public static void BuildFromMenu() => Build();

        [MenuItem("RALLY X/Build/Regenerate Placeholder Sounds")]
        public static void RegenerateFromMenu()
        {
            if (EditorUtility.DisplayDialog("Regenerate placeholder sounds",
                    $"Rewrite every generated sound in {ClipFolder}? Configs keep pointing at them; replaced (licensed) sounds elsewhere are untouched.",
                    "Regenerate", "Cancel"))
                Build(regenerate: true);
        }

        /// <summary>Everything missing, and any generated sound whose recipe changed. Returns false (and logs why) on failure.</summary>
        public static bool Build(bool regenerate = false)
        {
            var clips = BuildClips(regenerate);
            if (clips == null)
                return false;

            BuildGameConfig(clips);
            var vehicleConfigs = BuildVehicleConfigs(clips);
            AssignSurfaceSounds(clips);
            bool carsOk = AddToCars(vehicleConfigs);
            AssetDatabase.SaveAssets();
            return carsOk;
        }

        /// <summary>A car's audio config by its prefab's name (RallyCar, Kestrel, Titan), or null if there is none yet.</summary>
        public static VehicleAudioConfig LoadVehicleConfig(string carName) =>
            AssetDatabase.LoadAssetAtPath<VehicleAudioConfig>($"{ConfigFolder}/{carName}_Audio.asset");

        // ---------------------------------------------------------------- clips

        private static Dictionary<string, AudioClip> BuildClips(bool regenerate)
        {
            EditorAssetUtility.EnsureFolder(ClipFolder);
            var clips = new Dictionary<string, AudioClip>();
            foreach (var (clipName, make) in Recipes)
            {
                string path = $"{ClipFolder}/{clipName}.wav";
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (regenerate || importer == null || importer.userData != Revision)
                {
                    var sound = make();
                    File.WriteAllBytes(Path.GetFullPath(path), AudioSynth.Wav(sound.Data, sound.Rate));
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer == null)
                    {
                        Debug.LogError(LogPrefix + $"Could not import {path}.");
                        return null;
                    }

                    Configure(importer, music: clipName.StartsWith("Music_", StringComparison.Ordinal));
                    Debug.Log(LogPrefix + $"Generated {path} ({sound.Data.Length / (float)sound.Rate:0.00} s).");
                }

                clips[clipName] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }

            return clips;
        }

        private static void Configure(AudioImporter importer, bool music)
        {
            importer.forceToMono = true;
            importer.loadInBackground = music;
            var settings = importer.defaultSampleSettings;
            settings.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = music ? 0.6f : 0.75f;
            importer.defaultSampleSettings = settings;
            importer.userData = Revision;
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- configs

        private static void BuildGameConfig(Dictionary<string, AudioClip> clips)
        {
            if (AssetDatabase.LoadAssetAtPath<GameAudioConfig>(GameConfigPath) != null)
                return;

            var config = EditorAssetUtility.LoadOrCreate(GameConfigPath, ScriptableObject.CreateInstance<GameAudioConfig>);
            var ui = new SoundCue[Enum.GetValues(typeof(UiCue)).Length];
            ui[(int)UiCue.Navigate] = new SoundCue(clips["UI_Navigate"], 0.35f, 0.97f, 1.03f);
            ui[(int)UiCue.Confirm] = new SoundCue(clips["UI_Confirm"], 0.5f);
            ui[(int)UiCue.Back] = new SoundCue(clips["UI_Back"], 0.45f);
            ui[(int)UiCue.Error] = new SoundCue(clips["UI_Error"], 0.5f);
            ui[(int)UiCue.Purchase] = new SoundCue(clips["UI_Purchase"], 0.6f);
            ui[(int)UiCue.Switch] = new SoundCue(clips["UI_Switch"], 0.4f, 0.95f, 1.05f);

            var race = new SoundCue[Enum.GetValues(typeof(RaceCue)).Length];
            race[(int)RaceCue.CountdownTick] = new SoundCue(clips["Race_Countdown"], 0.7f);
            race[(int)RaceCue.Go] = new SoundCue(clips["Race_Go"], 0.8f);
            race[(int)RaceCue.Lap] = new SoundCue(clips["Race_Lap"], 0.55f);
            race[(int)RaceCue.FinalLap] = new SoundCue(clips["Race_FinalLap"], 0.6f);
            race[(int)RaceCue.Finish] = new SoundCue(clips["Race_Finish"], 0.8f);
            race[(int)RaceCue.Results] = new SoundCue(clips["Race_Results"], 0.5f);

            config.SetSounds(clips["Music_Menu"], clips["Music_Race"], ui, race);
            EditorUtility.SetDirty(config);
        }

        private static Dictionary<string, VehicleAudioConfig> BuildVehicleConfigs(Dictionary<string, AudioClip> clips)
        {
            var configs = new Dictionary<string, VehicleAudioConfig>();
            foreach (var (prefab, configName, tune) in Cars)
            {
                string path = $"{ConfigFolder}/{configName}.asset";
                var config = AssetDatabase.LoadAssetAtPath<VehicleAudioConfig>(path);
                if (config == null)
                {
                    config = EditorAssetUtility.LoadOrCreate(path, ScriptableObject.CreateInstance<VehicleAudioConfig>);
                    config.SetSounds(clips["Engine_Low"], clips["Engine_High"],
                        new SoundCue(clips["GearShift"], 0.35f, 0.95f, 1.05f), new SoundCue(clips["Backfire"], 0.7f, 0.9f, 1.1f),
                        new LoopSound(clips["Tyre_Asphalt_Roll"], 0.7f), new LoopSound(clips["Tyre_Asphalt_Slide"], 0.8f),
                        new LoopSound(clips["Wind"], 0.6f), new SoundCue(clips["Impact_Light"], 0.7f, 0.9f, 1.1f),
                        new SoundCue(clips["Impact_Heavy"], 0.9f, 0.9f, 1.05f), new LoopSound(clips["Scrape"], 0.7f),
                        new SoundCue(clips["Landing"], 0.8f, 0.9f, 1.1f));
                    if (tune != null)
                    {
                        var serialized = new SerializedObject(config);
                        tune(serialized);
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }

                    EditorUtility.SetDirty(config);
                }

                configs[prefab] = config;
            }

            return configs;
        }

        private static void Set(SerializedObject config, params (string property, object value)[] values)
        {
            foreach (var (property, value) in values)
            {
                var field = config.FindProperty(property);
                if (value is float[] array)
                {
                    field.arraySize = array.Length;
                    for (int i = 0; i < array.Length; i++)
                        field.GetArrayElementAtIndex(i).floatValue = array[i];
                }
                else
                {
                    field.floatValue = (float)value;
                }
            }
        }

        // ---------------------------------------------------------------- surfaces and cars

        /// <summary>Each surface's tyre sounds, unless it already has some. Sand shares dirt's, a little lower.</summary>
        private static void AssignSurfaceSounds(Dictionary<string, AudioClip> clips)
        {
            (string surface, string sounds, float rolling, float slide, float pitch)[] surfaces =
            {
                ("Asphalt", "Asphalt", 0.7f, 0.8f, 1f),
                ("Dirt", "Dirt", 0.8f, 0.9f, 1f),
                ("Gravel", "Gravel", 0.9f, 1f, 1f),
                ("Mud", "Mud", 0.8f, 0.9f, 1f),
                ("Grass", "Grass", 0.7f, 0.8f, 1f),
                ("Sand", "Dirt", 0.8f, 0.9f, 0.88f),
            };

            foreach (var (surfaceName, sounds, rolling, slide, pitch) in surfaces)
            {
                var surface = VehicleContentBuilder.LoadSurface(surfaceName);
                if (surface == null || surface.RollingSound.IsSet || surface.SlideSound.IsSet)
                    continue;

                surface.SetSounds(new LoopSound(clips[$"Tyre_{sounds}_Roll"], rolling, pitch), new LoopSound(clips[$"Tyre_{sounds}_Slide"], slide, pitch));
                EditorUtility.SetDirty(surface);
                Debug.Log(LogPrefix + $"Gave {surfaceName} its tyre sounds.");
            }
        }

        /// <summary>A <see cref="VehicleAudio"/> with the car's config on each car prefab that has none; the prefab is otherwise left as it is.</summary>
        private static bool AddToCars(Dictionary<string, VehicleAudioConfig> configs)
        {
            bool ok = true;
            foreach (var (prefabPath, config) in configs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    continue;

                var existing = prefab.GetComponent<VehicleAudio>();
                if (existing != null && existing.Config != null)
                    continue;

                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var carAudio = root.GetComponent<VehicleAudio>();
                    if (carAudio == null)
                        carAudio = root.AddComponent<VehicleAudio>();
                    if (carAudio.Config == null)
                        carAudio.Configure(config);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                    if (!saved)
                    {
                        Debug.LogError(LogPrefix + $"Failed to save {prefabPath}.");
                        ok = false;
                        continue;
                    }

                    Debug.Log(LogPrefix + $"Added VehicleAudio to {prefabPath}.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return ok;
        }
    }
}
