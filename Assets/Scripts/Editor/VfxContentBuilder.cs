using RallyX.VFX;
using UnityEditor;
using UnityEngine;
using static RallyX.EditorTools.ProceduralTextures;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Creates the vehicle effects: generated particle textures, their materials (the RallyX/Effect shader) and the
    /// shared <see cref="VehicleEffectsConfig"/>. The config is created once and never overwritten, so tuning is kept;
    /// the textures and materials are builder-owned and brought up to date on every build.
    /// </summary>
    public static class VfxContentBuilder
    {
        public const string ConfigPath = "Assets/Settings/VFX/VehicleEffects_Default.asset";

        private const string ShaderPath = "Assets/Materials/Shaders/Effect.shader";
        private const string TextureFolder = "Assets/Textures/Generated/VFX";
        private const string MaterialFolder = "Assets/Materials/VFX";
        private const string Revision = "vfx-1";
        private const int TextureSize = 128;

        [MenuItem("RALLY X/Build/Create Missing Effects Content")]
        public static void BuildFromMenu() => LoadOrCreateConfig();

        /// <summary>The effects config, with its materials made or updated. Null (and an error) if the shader is missing.</summary>
        public static VehicleEffectsConfig LoadOrCreateConfig()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[RALLY X Build] {ShaderPath} is missing.");
                return null;
            }

            var puff = EffectMaterial("Effect_Puff", shader, LoadOrCreate($"{TextureFolder}/Puff.png", TextureSize, PaintPuff, false, Revision, true),
                BlendMode.Alpha, lit: 1f);
            var debris = EffectMaterial("Effect_Debris", shader, LoadOrCreate($"{TextureFolder}/Chunk.png", TextureSize, PaintChunk, false, Revision, true),
                BlendMode.Alpha, lit: 1f);
            var spark = EffectMaterial("Effect_Spark", shader, LoadOrCreate($"{TextureFolder}/Spark.png", TextureSize, PaintSpark, false, Revision, true),
                BlendMode.Additive, lit: 0f);
            var skid = EffectMaterial("Effect_Skid", shader, LoadOrCreate($"{TextureFolder}/Skid.png", TextureSize, PaintSkid, true, Revision, true),
                BlendMode.Alpha, lit: 0.5f, depthOffset: -2f);

            var config = EditorAssetUtility.LoadOrCreate(ConfigPath, ScriptableObject.CreateInstance<VehicleEffectsConfig>);
            if (config.PuffMaterial != puff || config.DebrisMaterial != debris || config.SparkMaterial != spark || config.SkidMaterial != skid)
            {
                config.SetMaterials(puff, debris, spark, skid);
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            return config;
        }

        private enum BlendMode
        {
            Alpha,
            Additive,
        }

        private static Material EffectMaterial(string materialName, Shader shader, Texture2D texture, BlendMode blend, float lit, float depthOffset = 0f)
        {
            var material = EditorAssetUtility.LoadOrCreate($"{MaterialFolder}/{materialName}.mat", () => new Material(shader));
            material.shader = shader;
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Lit", lit);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", blend == BlendMode.Additive
                ? (float)UnityEngine.Rendering.BlendMode.One
                : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_FogToBlack", blend == BlendMode.Additive ? 1f : 0f);
            material.SetFloat("_OffsetFactor", depthOffset);
            material.SetFloat("_OffsetUnits", depthOffset);
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- textures (white, shaped by alpha)

        private static float Radius(float u, float v) => Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;

        /// <summary>A soft, lumpy cloud.</summary>
        private static Color PaintPuff(float u, float v)
        {
            float r = Radius(u, v);
            float lumps = TileableNoise(u, v, 4, 3, 3f);
            float alpha = (1f - SmoothStep(0.25f, 1f, r + (lumps - 0.5f) * 0.35f)) * (0.7f + 0.3f * lumps);
            float shade = 0.86f + 0.14f * v;
            return new Color(shade, shade, shade, Mathf.Clamp01(alpha));
        }

        /// <summary>An irregular lump, lit from above: a stone, a clump of mud or a clod.</summary>
        private static Color PaintChunk(float u, float v)
        {
            float angle = Mathf.Atan2(v - 0.5f, u - 0.5f);
            float edge = 0.72f + 0.2f * Mathf.PerlinNoise(Mathf.Cos(angle) * 1.6f + 20f, Mathf.Sin(angle) * 1.6f + 20f);
            float r = Radius(u, v);
            float alpha = 1f - SmoothStep(edge - 0.1f, edge, r);
            float shade = 0.7f + 0.45f * (v * 0.6f + (1f - u) * 0.4f) - 0.15f * TileableNoise(u, v, 8, 2, 5f);
            return new Color(shade, shade, shade, alpha);
        }

        /// <summary>A hot point with a soft glow.</summary>
        private static Color PaintSpark(float u, float v)
        {
            float r = Radius(u, v);
            float glow = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
            return new Color(1f, 1f, 1f, glow);
        }

        /// <summary>A tyre mark: soft edges across (u), a faint tread pattern along (v).</summary>
        private static Color PaintSkid(float u, float v)
        {
            float across = SmoothStep(0f, 0.16f, u) * (1f - SmoothStep(0.84f, 1f, u));
            float tread = 0.78f + 0.22f * Mathf.Sin((v * 8f + (u > 0.5f ? 0.5f : 0f)) * Mathf.PI * 2f);
            float grain = 0.8f + 0.2f * TileableNoise(u, v, 16, 2, 9f);
            return new Color(1f, 1f, 1f, across * tread * grain);
        }
    }
}
