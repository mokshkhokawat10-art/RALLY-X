using System.IO;
using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Generates placeholder textures as PNG assets until real art exists. A texture made with a revision is repainted
    /// in place (same asset, same GUID) when its painter's revision changes, so improving the art never breaks references.
    /// </summary>
    internal static class ProceduralTextures
    {
        public delegate Color Painter(float u, float v);

        /// <summary>Height 0..1 at a point of the unit square, for normal maps.</summary>
        public delegate float HeightPainter(float u, float v);

        // Keeps Perlin inputs positive and away from the origin, where Unity's noise is symmetric.
        private const float NoiseOrigin = 100f;

        /// <summary>
        /// Loads the texture at a path, painting and importing it if it doesn't exist yet, or if it was painted with a
        /// different <paramref name="revision"/> (null = never repaint an existing texture).
        /// </summary>
        public static Texture2D LoadOrCreate(string path, int size, Painter paint, bool repeat = true, string revision = null,
            bool alphaIsTransparency = false)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null && (revision == null || RevisionOf(path) == revision))
                return existing;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = paint((x + 0.5f) / size, (y + 0.5f) / size);
            }

            return Save(path, size, pixels, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = alphaIsTransparency;
                importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            }, revision, existing != null);
        }

        /// <summary>
        /// A tangent-space normal map worked out from a height painter (which must tile when <paramref name="repeat"/>),
        /// imported as a normal map. Repainted when the revision changes, like <see cref="LoadOrCreate"/>.
        /// </summary>
        public static Texture2D LoadOrCreateNormalMap(string path, int size, HeightPainter height, float strength, string revision,
            bool repeat = true)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null && RevisionOf(path) == revision)
                return existing;

            var heights = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    heights[y * size + x] = height((x + 0.5f) / size, (y + 0.5f) / size);
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float left = Sample(heights, size, x - 1, y, repeat);
                    float right = Sample(heights, size, x + 1, y, repeat);
                    float down = Sample(heights, size, x, y - 1, repeat);
                    float up = Sample(heights, size, x, y + 1, repeat);
                    var normal = new Vector3((left - right) * strength, (down - up) * strength, 1f).normalized;
                    pixels[y * size + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
                }
            }

            return Save(path, size, pixels, importer =>
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            }, revision, existing != null);
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

        /// <summary>
        /// Tileable cellular noise over the unit square: distance (in cells) from the point to the nearest random
        /// feature point, and to the second nearest, on a grid of <paramref name="cells"/> x cells. Good for stones.
        /// </summary>
        public static void TileableCells(float u, float v, int cells, float seed, out float nearest, out float second, out int cellId)
        {
            float x = u * cells;
            float y = v * cells;
            int cx = Mathf.FloorToInt(x);
            int cy = Mathf.FloorToInt(y);
            nearest = second = float.MaxValue;
            cellId = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cx + dx;
                    int gy = cy + dy;
                    int wx = ((gx % cells) + cells) % cells;
                    int wy = ((gy % cells) + cells) % cells;
                    float fx = gx + Hash(wx, wy, seed);
                    float fy = gy + Hash(wy, wx, seed + 7.1f);
                    float distance = Mathf.Sqrt((fx - x) * (fx - x) + (fy - y) * (fy - y));
                    if (distance < nearest)
                    {
                        second = nearest;
                        nearest = distance;
                        cellId = wy * cells + wx;
                    }
                    else if (distance < second)
                    {
                        second = distance;
                    }
                }
            }
        }

        /// <summary>A repeatable pseudo-random 0..1 for a whole number and a seed.</summary>
        public static float Hash(int a, int b, float seed)
        {
            float h = Mathf.Sin(a * 127.1f + b * 311.7f + seed * 74.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>Scales a colour's brightness.</summary>
        public static Color Shade(Color color, float factor) =>
            new Color(color.r * factor, color.g * factor, color.b * factor, color.a);

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float Sample(float[] heights, int size, int x, int y, bool repeat)
        {
            x = repeat ? (x % size + size) % size : Mathf.Clamp(x, 0, size - 1);
            y = repeat ? (y % size + size) % size : Mathf.Clamp(y, 0, size - 1);
            return heights[y * size + x];
        }

        private static Texture2D Save(string path, int size, Color32[] pixels, System.Action<TextureImporter> configure, string revision,
            bool replacing)
        {
            EditorAssetUtility.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            configure(importer);
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.userData = revision ?? string.Empty;
            importer.SaveAndReimport();

            Debug.Log($"[RALLY X Build] {(replacing ? "Repainted" : "Created")} {path}.");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static string RevisionOf(string path) => AssetImporter.GetAtPath(path)?.userData ?? string.Empty;

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
