using System.Collections.Generic;
using System.IO;
using System.Linq;
using RallyX.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Idempotent project configuration for RALLY X. Every step checks the current state first,
    /// so it is safe to run repeatedly from the menu or from batch mode:
    /// Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod RallyX.EditorTools.ProjectSetup.RunBatch -quit
    /// </summary>
    public static class ProjectSetup
    {
        internal const string SandboxScenePath = "Assets/Scenes/Dev/Sandbox.unity";

        private const string LogPrefix = "[RALLY X Setup] ";
        private const string ProductName = "RALLY X";

        // Serialized values of PlayerSettings.activeInputHandler: 0 = Input Manager (old), 1 = Input System, 2 = Both.
        private const string ActiveInputHandlerProperty = "activeInputHandler";
        private const int InputSystemOnly = 1;

        // Layers 0-7 are reserved by Unity; user layers start at 8.
        private const int FirstUserLayer = 8;
        private const int VehicleLayerIndex = 8;

        [MenuItem("RALLY X/Setup/Run Project Setup")]
        public static void RunFromMenu() => Run();

        public static void RunBatch()
        {
            if (!Run())
                EditorApplication.Exit(1);
        }

        public static bool Run()
        {
            ConfigureVersionControlFriendlySettings();
            ConfigurePlayerSettings();
            bool inputOk = ConfigureInputHandling();
            bool layersOk = EnsureLayer(GameLayers.Vehicle, VehicleLayerIndex);
            bool urpOk = VerifyUniversalRenderPipeline();

            // The sandbox's primitives take their default material from the active pipeline,
            // so creating it without URP would bake in built-in materials that render pink.
            bool sandboxOk = urpOk && EnsureSandboxScene();

            AssetDatabase.SaveAssets();

            bool success = inputOk && layersOk && urpOk && sandboxOk;
            if (success)
                Debug.Log(LogPrefix + "Completed successfully.");
            else
                Debug.LogError(LogPrefix + "Completed with errors. See messages above.");
            return success;
        }

        private static void ConfigureVersionControlFriendlySettings()
        {
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
            {
                EditorSettings.serializationMode = SerializationMode.ForceText;
                Debug.Log(LogPrefix + "Asset serialization set to Force Text.");
            }

            if (VersionControlSettings.mode != "Visible Meta Files")
            {
                VersionControlSettings.mode = "Visible Meta Files";
                Debug.Log(LogPrefix + "Version control mode set to Visible Meta Files.");
            }

            if (EditorSettings.lineEndingsForNewScripts != LineEndingsMode.Unix)
            {
                EditorSettings.lineEndingsForNewScripts = LineEndingsMode.Unix;
                Debug.Log(LogPrefix + "Line endings for new scripts set to Unix (LF).");
            }
        }

        private static void ConfigurePlayerSettings()
        {
            if (PlayerSettings.productName != ProductName)
            {
                PlayerSettings.productName = ProductName;
                Debug.Log(LogPrefix + $"Product name set to '{ProductName}'.");
            }

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                Debug.Log(LogPrefix + "Color space set to Linear.");
            }
        }

        private static bool ConfigureInputHandling()
        {
            // activeInputHandler has no public API; the Input System package edits it the same way.
            var playerSettings = Resources.FindObjectsOfTypeAll<PlayerSettings>().FirstOrDefault();
            if (playerSettings == null)
            {
                Debug.LogError(LogPrefix + "Could not load PlayerSettings to configure input handling.");
                return false;
            }

            var serializedSettings = new SerializedObject(playerSettings);
            var handler = serializedSettings.FindProperty(ActiveInputHandlerProperty);
            if (handler == null)
            {
                Debug.LogError(LogPrefix + $"PlayerSettings has no '{ActiveInputHandlerProperty}' property in this Unity version.");
                return false;
            }

            if (handler.intValue != InputSystemOnly)
            {
                handler.intValue = InputSystemOnly;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                Debug.LogWarning(LogPrefix + "Active Input Handling set to 'Input System Package (New)'. Restart the Unity Editor for it to take effect.");
            }

            return true;
        }

        private static bool EnsureLayer(string layerName, int preferredIndex)
        {
            var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets.Length == 0)
            {
                Debug.LogError(LogPrefix + "Could not load the TagManager to configure physics layers.");
                return false;
            }

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                    return true;
            }

            int index = string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferredIndex).stringValue) ? preferredIndex : -1;
            for (int i = FirstUserLayer; index < 0 && i < layers.arraySize; i++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                    index = i;
            }

            if (index < 0)
            {
                Debug.LogError(LogPrefix + $"No free user layer for '{layerName}'.");
                return false;
            }

            layers.GetArrayElementAtIndex(index).stringValue = layerName;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log(LogPrefix + $"Created physics layer '{layerName}' at index {index}.");
            return true;
        }

        private static bool VerifyUniversalRenderPipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline is not UniversalRenderPipelineAsset)
            {
                Debug.LogError(LogPrefix + "URP is not the default render pipeline. Assign a Universal Render Pipeline Asset in Project Settings > Graphics.");
                return false;
            }

            string[] qualityLevels = QualitySettings.names;
            for (int i = 0; i < qualityLevels.Length; i++)
            {
                // A null override means the quality level falls back to the default (URP) pipeline.
                var levelPipeline = QualitySettings.GetRenderPipelineAssetAt(i);
                if (levelPipeline != null && levelPipeline is not UniversalRenderPipelineAsset)
                {
                    Debug.LogError(LogPrefix + $"Quality level '{qualityLevels[i]}' overrides the pipeline with a non-URP asset.");
                    return false;
                }
            }

            return true;
        }

        private static bool EnsureSandboxScene()
        {
            if (!File.Exists(SandboxScenePath) && !CreateSandboxScene())
                return false;

            AddSceneToBuildSettings(SandboxScenePath);
            return true;
        }

        private static bool CreateSandboxScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogError(LogPrefix + "Sandbox scene not created: unsaved changes in the open scene were not saved.");
                return false;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            new GameObject(nameof(DevOverlay), typeof(DevOverlay));

            if (!EditorSceneManager.SaveScene(scene, SandboxScenePath))
            {
                Debug.LogError(LogPrefix + $"Failed to save {SandboxScenePath}.");
                return false;
            }

            Debug.Log(LogPrefix + $"Created {SandboxScenePath}.");
            return true;
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Any(s => s.path == scenePath))
                return;

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log(LogPrefix + $"Added {scenePath} to the build scene list.");
        }
    }
}
