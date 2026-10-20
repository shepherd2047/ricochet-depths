// Particles and halo quads. The atlas cell is a grey mask (r = brightness, a = coverage); the brightness times the
// vertex colour's red channel ("heat") picks a colour on the element's 64×2 ramp, posterised to _Steps levels so it
// reads as pixel art. Only the top of the ramp is pushed over 1, so Bloom (threshold ≈ 1) catches the hot parts.
// Vertex colour: r = heat, a = alpha. Additive. _Billboard = 1 turns a ±0.5 quad mesh into a camera-facing sprite.
Shader "RicochetVfx/Particle"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Ramp ("Ramp row (0 Ember, 1 Rime, 2 Spark, 3 Ray)", Float) = 0
        _Boost ("HDR boost at the hot end", Float) = 2.5
        _Steps ("Ramp steps", Float) = 8
        _Billboard ("Billboard quad", Float) = 0
        _Flicker ("Flicker at 12 fps", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
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
                float _Ramp, _Boost, _Steps, _Billboard, _Flicker;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = _Billboard > 0.5 ? VfxBillboard(v.positionOS.xyz) : TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 m = VfxAtlas(i.uv);
                float fl = 1 - _Flicker * VfxHash(float2(floor(_VfxTime * 12), _Ramp + 0.37));
                float t = saturate(m.r * i.color.r * fl);
                t = floor(t * _Steps + 0.5) / _Steps;
                float a = m.a * i.color.a * step(0.01, t);
                float3 c = VfxRamp(t, _Ramp) * lerp(1, _Boost, t * t);
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
