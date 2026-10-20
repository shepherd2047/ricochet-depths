// Reference unit shader for the lab (the pit-art enemy models): albedo + emissive atlas, one main light with
// shadows, a three-colour ambient (style.md §3), and RicochetHitFx for the hit flash, element tint, burn, ice and
// shock. In the game the same include goes into pit-unity's PitUnit shader (see INTEGRATION.md).
Shader "RicochetVfx/Unit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _EmisMap ("Emission map", 2D) = "black" {}
        _EmisGain ("Emission gain", Float) = 2
        _AmbSky ("Ambient sky", Color) = (0.212, 0.227, 0.259, 1)
        _AmbEquator ("Ambient horizon", Color) = (0.114, 0.125, 0.133, 1)
        _AmbGround ("Ambient ground", Color) = (0.047, 0.043, 0.035, 1)
        _UpWS ("Up (out of the floor)", Vector) = (0, 0, -1, 0)
        [Header(Hit feedback (VfxHitFeedback))]
        _Flash ("Flash", Range(0, 1)) = 0
        _HitTint ("Hit tint (a = amount)", Color) = (1, 1, 1, 0)
        _Ice ("Ice", Range(0, 1)) = 0
        _Burn ("Burn", Range(0, 1)) = 0
        _Shock ("Shock", Range(0, 1)) = 0
        _VfxSeed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RicochetHitFx.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _EmisMap_ST, _Color, _AmbSky, _AmbEquator, _AmbGround, _UpWS, _HitTint;
                float _EmisGain, _Flash, _Ice, _Burn, _Shock, _VfxSeed;
            CBUFFER_END
            float _VfxTime;
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_EmisMap); SAMPLER(sampler_EmisMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = saturate(dot(n, light.direction));
                float up = dot(n, normalize(_UpWS.xyz));
                float3 amb = up >= 0 ? lerp(_AmbEquator.rgb, _AmbSky.rgb, up) : lerp(_AmbEquator.rgb, _AmbGround.rgb, -up);
                float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * _Color.rgb;
                float3 col = albedo * (amb + light.color * ndl * light.shadowAttenuation);
                float3 glow = SAMPLE_TEXTURE2D(_EmisMap, sampler_EmisMap, i.uv).rgb * _EmisGain;
                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                RicochetHitFx(col, glow, n, view, i.positionOS, ndl, _Flash, _HitTint, _Ice, _Burn, _Shock, _VfxSeed, _VfxTime);
                return half4(col + glow, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack Off
}
