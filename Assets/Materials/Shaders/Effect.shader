// Particles and decals such as tyre smoke, dust, thrown gravel, sparks and skid marks. Multiplies the texture by the
// vertex colour (particle or mark colour) and the tint. Alpha-blended effects are lit softly by the sun and sky so dust
// in a sunny canyon and dust in a shaded forest match their surroundings; additive ones (sparks) glow and fade to black
// in fog. The depth offset lets marks lie on the road without flickering.
Shader "RallyX/Effect"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Lit ("Lit by Sun and Sky", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10
        [Toggle] _FogToBlack ("Fade to Black in Fog (additive)", Float) = 0
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Lit;
                half _FogToBlack;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half3 lighting : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                // Smoke and dust scatter light every way, so ignore the sun's direction: sky light plus some sunlight.
                Light mainLight = GetMainLight();
                half3 lit = SampleSH(half3(0, 1, 0)) + mainLight.color * 0.7;
                output.lighting = lerp(half3(1, 1, 1), lit, _Lit);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color * _Color;
                color.rgb *= input.lighting;
                half3 fogged = MixFog(color.rgb, input.fogFactor);
                half3 faded = MixFogColor(color.rgb, half3(0, 0, 0), input.fogFactor);
                color.rgb = lerp(fogged, faded, _FogToBlack);
                return color;
            }
            ENDHLSL
        }
    }
}
