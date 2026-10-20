// Trails, chain bolts, crackle arcs and the row beam (VfxRibbon meshes). Across the strip (uv.y) a hard bright core
// sits inside a soft glow; along it (uv.x) a stepped noise flickers at _Fps, so the line shimmers like pixel art.
// Vertex colour: r = heat, g = per-point random, b = extra shimmer, a = alpha. Additive, ramp-coloured like particles.
Shader "RicochetVfx/Ribbon"
{
    Properties
    {
        _MainTex ("Atlas (ramps)", 2D) = "white" {}
        _Ramp ("Ramp row", Float) = 0
        _Boost ("HDR boost at the hot end", Float) = 2.5
        _Steps ("Ramp steps", Float) = 8
        _Core ("Core width (fraction)", Range(0, 1)) = 0.35
        _Glow ("Glow amount", Range(0, 1)) = 0.6
        _Flicker ("Flicker", Range(0, 1)) = 0.2
        _Fps ("Flicker fps", Float) = 12
        _Segments ("Flicker cells along the strip", Float) = 12
        _TailFade ("Fade toward uv.x = 1", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "RicochetVfxCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Ramp, _Boost, _Steps, _Core, _Glow, _Flicker, _Fps, _Segments, _TailFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float p = 1 - abs(i.uv.y * 2 - 1);                          // 1 on the centre line, 0 at the edges
                float core = step(1 - _Core, p);
                float glow = p * p * _Glow;
                float frame = floor(_VfxTime * _Fps);
                float n = VfxHash(float2(floor(i.uv.x * _Segments + i.color.g * 3), frame));
                float fl = 1 - max(_Flicker, i.color.b) * n;
                float heat = i.color.r * fl * (1 - _TailFade * i.uv.x);
                float t = saturate(max(core, glow) * heat);
                t = floor(t * _Steps + 0.5) / _Steps;
                float a = i.color.a * step(0.01, t);
                float3 c = VfxRamp(t, _Ramp) * lerp(1, _Boost, t * t);
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
