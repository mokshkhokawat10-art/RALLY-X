using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RallyX.EditorTools
{
    internal static class EditorAssetUtility
    {
        public static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string parent = ParentFolder(folderPath);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folderPath));
        }

        /// <summary>Loads the asset at a path, creating it only when missing so designer edits are kept.</summary>
        public static T LoadOrCreate<T>(string path, Func<T> create) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;

            EnsureFolder(ParentFolder(path));
            asset = create();
            AssetDatabase.CreateAsset(asset, path);
            Debug.Log($"[RALLY X Build] Created {path}.");
            return asset;
        }

        /// <summary>URP Lit material, copied from the pipeline's default material.</summary>
        public static Material LoadOrCreateLitMaterial(string path, Color color, float smoothness)
        {
            return LoadOrCreate(path, () =>
            {
                var material = new Material(GraphicsSettings.defaultRenderPipeline.defaultMaterial);
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", smoothness);
                return material;
            });
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private static string ParentFolder(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/');
    }
}
