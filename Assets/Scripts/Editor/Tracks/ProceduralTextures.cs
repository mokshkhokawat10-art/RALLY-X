using System.IO;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>Generates placeholder textures as PNG assets until real art exists.</summary>
    internal static class ProceduralTextures
    {
        public delegate Color Painter(float u, float v);

        // Keeps Perlin inputs positive and away from the origin, where Unity's noise is symmetric.
        private const float NoiseOrigin = 100f;

        /// <summary>Loads the texture at a path, painting and importing it only if it doesn't exist yet.</summary>
        public static Texture2D LoadOrCreate(string path, int size, Painter paint, bool repeat = true)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            EditorAssetUtility.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = paint((x + 0.5f) / size, (y + 0.5f) / size);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();

            Debug.Log($"[RALLY X Build] Created {path}.");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Fractal Perlin noise (0..1) that tiles seamlessly over the unit square. Frequency must be whole.</summary>
        public static float TileableNoise(float u, float v, int frequency, int octaves, float seed)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            int octaveFrequency = frequency;
            for (int octave = 0; octave < octaves; octave++)
            {
                sum += amplitude * TileablePerlin(u, v, octaveFrequency, seed + octave * 31.7f);
                total += amplitude;
                amplitude *= 0.5f;
                octaveFrequency *= 2;
            }

            return sum / total;
        }

        /// <summary>Scales a colour's brightness.</summary>
        public static Color Shade(Color color, float factor) =>
            new Color(color.r * factor, color.g * factor, color.b * factor, color.a);

        private static float TileablePerlin(float u, float v, int frequency, float seed)
        {
            // Blending the noise with copies offset by one period makes opposite edges match.
            float x = u * frequency + NoiseOrigin + seed;
            float y = v * frequency + NoiseOrigin + seed;
            float a = Mathf.PerlinNoise(x, y);
            float b = Mathf.PerlinNoise(x - frequency, y);
            float c = Mathf.PerlinNoise(x, y - frequency);
            float d = Mathf.PerlinNoise(x - frequency, y - frequency);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }
    }
}
