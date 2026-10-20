// Low-poly units and scenery (style.md §3, §4, §6): flat vertex colours, one main light with soft shadows, a
// three-colour ambient (sky / equator / ground from _Pit* globals set by Board3D), the pit-axis fog, and the hit feedback:
// _Bleed (noise patches seeded per unit), and pit-vfx's RicochetHitFx (INTEGRATION.md §3): _Flash (white), _HitTint
// (element tint), _Ice (frozen overlay), _Burn, _Shock, on the _VfxSeed / _VfxTime clock; Board3D and VfxHitFeedback
// both write them through the renderer's MaterialPropertyBlock. uv.x marks emissive faces, which are
// pushed over the bloom threshold and tinted by _EmisTint. Written for URP.
// Delivered models (_Textured = 1) take their colour from _MainTex instead and use uv as texture coordinates; with
// _TexEmit = 1 their light painted accents (the foes' eyes, seams and flues, there is no emissive map yet) glow in
// _EmisColor; with _TexEmit = 2 the glow is _EmisMap's colour instead (foes with a delivered emissive map). Otherwise
// _EmisMap (scenery: the window panes) adds its colour × _EmisMapGain.
// Additional lights (the wall fills and the furnace spot) are per-pixel Lambert without shadows. Fog covers everything
// past the far-wall line; the units' own glow (eyes, markings) takes half of it so waves above the board glint through.
Shader "Pit/Unit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Textured ("Textured model", Float) = 0
        _TexEmit ("Glow from light albedo", Float) = 0
        _EmisColor ("Glow colour", Color) = (1, 0.62, 0.29, 1)
        _EmisMap ("Emission map", 2D) = "black" {}
        _EmisMapGain ("Emission map gain", Float) = 0
        _Flash ("Flash", Range(0, 1)) = 0
        _Ice ("Ice", Range(0, 1)) = 0
        _HitTint ("Hit tint (a = amount)", Color) = (1, 1, 1, 0)
        _Burn ("Burn", Range(0, 1)) = 0
        _Shock ("Shock", Range(0, 1)) = 0
        _VfxSeed ("VFX seed", Float) = 0
        _Bleed ("Bleed", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
        _EmisBoost ("Emission boost", Float) = 3
        _EmisTint ("Emission tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/RicochetVfx/Shaders/RicochetHitFx.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Flash, _Ice, _Bleed, _Seed, _EmisBoost, _Textured, _TexEmit, _EmisMapGain, _Burn, _Shock, _VfxSeed;
                float4 _EmisTint, _EmisColor, _MainTex_ST, _HitTint;
            CBUFFER_END
            float _VfxTime;
            float4 _PitSky, _PitEquator, _PitGround;
            // Pit-axis fog (pit-scene LIGHTING.md): by distance along the pit past the far-wall line (the board's top
            // edge, world y _PitLineY), not by view depth, so tall walls and the floor beyond the line fog alike.
            // z_local is the PitRoot-local z in kit units (board centre = 0), _PitFarZ = rows / 2 × 1.125.
            float _PitLineY, _PitFarZ;
            float4 _PitFogColor;
            float PitFog(float worldY)
            {
                float dz = (worldY - _PitLineY) * 1.125, z = dz + _PitFarZ;
                return 0.45 * smoothstep(0, 1.125, dz) + 0.35 * saturate((dz - 1.125) / 9) + 0.20 * saturate((z - 18) / 4.5);
            }
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_EmisMap); SAMPLER(sampler_EmisMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float4 color : COLOR;
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
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise3(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash3(i), hash3(i + float3(1, 0, 0)), f.x),
                                 lerp(hash3(i + float3(0, 1, 0)), hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash3(i + float3(0, 0, 1)), hash3(i + float3(1, 0, 1)), f.x),
                                 lerp(hash3(i + float3(0, 1, 1)), hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = saturate(dot(n, light.direction));

                // trilight: "up" is toward the camera (-z), "down" is into the pit
                float up = -n.z;
                float3 amb = up >= 0 ? lerp(_PitEquator.rgb, _PitSky.rgb, up) : lerp(_PitEquator.rgb, _PitGround.rgb, -up);

                float3 albedo = i.color.rgb;
                float emit = i.uv.x;
                float3 emisCol = _EmisColor.rgb;
                if (_Textured > 0.5)
                {
                    albedo *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                    if (_TexEmit > 1.5) { emisCol = SAMPLE_TEXTURE2D(_EmisMap, sampler_EmisMap, i.uv).rgb; emit = 1; }
                    else emit = _TexEmit * smoothstep(0.26, 0.42, dot(albedo, float3(0.3, 0.59, 0.11)));
                }
                float bn = noise3(i.positionOS * 7.0 + _Seed * 13.17);
                albedo = lerp(albedo, float3(0.32, 0.02, 0.05), _Bleed * smoothstep(0.52, 0.66, bn));

                float3 col = albedo * (amb + light.color * ndl * light.shadowAttenuation);
            #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light al = GetAdditionalLight(lightIndex, i.positionWS);
                    col += albedo * al.color * (saturate(dot(n, al.direction)) * al.distanceAttenuation);
                LIGHT_LOOP_END
            #endif

                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 glow = (_Textured > 0.5 ? emisCol : albedo) * emit * _EmisBoost * _EmisTint.rgb;
                if (_EmisMapGain > 0) col += SAMPLE_TEXTURE2D(_EmisMap, sampler_EmisMap, i.uv).rgb * _EmisMapGain;
                RicochetHitFx(col, glow, n, view, i.positionOS, ndl, _Flash, _HitTint, _Ice, _Burn, _Shock, _VfxSeed, _VfxTime);
                float f = PitFog(i.positionWS.y);
                col = lerp(col, _PitFogColor.rgb, f) + glow * (1 - 0.5 * f);
                return half4(col, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack Off
}
