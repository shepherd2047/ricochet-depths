// Low-poly units and scenery (style.md §3, §4, §6): flat vertex colours, one main light with soft shadows, a
// three-colour ambient (sky / equator / ground from _Pit* globals set by Board3D), linear fog, and the hit feedback:
// _Flash (white), _Ice (frozen overlay), _Bleed (noise patches seeded per unit). uv.x marks emissive faces, which are
// pushed over the bloom threshold and tinted by _EmisTint. Written for URP.
// Delivered models (_Textured = 1) take their colour from _MainTex instead and use uv as texture coordinates; with
// _TexEmit = 1 their light painted accents (the foes' eyes, seams and flues, there is no emissive map yet) glow in
// _EmisColor.
Shader "Pit/Unit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Textured ("Textured model", Float) = 0
        _TexEmit ("Glow from light albedo", Float) = 0
        _EmisColor ("Glow colour", Color) = (1, 0.62, 0.29, 1)
        _Flash ("Flash", Range(0, 1)) = 0
        _Ice ("Ice", Range(0, 1)) = 0
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Flash, _Ice, _Bleed, _Seed, _EmisBoost, _Textured, _TexEmit;
                float4 _EmisTint, _EmisColor, _MainTex_ST;
            CBUFFER_END
            float4 _PitSky, _PitEquator, _PitGround;
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

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
                float fog : TEXCOORD4;
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
                o.fog = ComputeFogFactor(p.positionCS.z);
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
                if (_Textured > 0.5)
                {
                    albedo *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                    emit = _TexEmit * smoothstep(0.26, 0.42, dot(albedo, float3(0.3, 0.59, 0.11)));
                }
                float bn = noise3(i.positionOS * 7.0 + _Seed * 13.17);
                albedo = lerp(albedo, float3(0.32, 0.02, 0.05), _Bleed * smoothstep(0.52, 0.66, bn));

                float3 col = albedo * (amb + light.color * ndl * light.shadowAttenuation);

                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                float rim = pow(1.0 - saturate(dot(n, view)), 2.0);
                float3 ice = lerp(float3(0.16, 0.42, 0.5), float3(0.8, 0.98, 0.92), rim) * (0.6 + 0.4 * ndl);
                col = lerp(col, ice, _Ice * 0.8);

                float3 glow = _Textured > 0.5 ? _EmisColor.rgb : albedo;
                col += glow * emit * _EmisBoost * _EmisTint.rgb;
                col = lerp(col, float3(1, 1, 1), _Flash * 0.85);
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack Off
}
