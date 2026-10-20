// The 16×16 ball sprite: point sampled, camera-facing, alpha-tested. Its grey levels index the element ramp, so the
// 1 px outline is the ramp's dark end and the highlight pixels its near-white end; those top pixels go over 1 (bloom).
// _Flicker shakes the brightest pixels at 12 fps (Ember's crack, Spark's arc).
Shader "RicochetVfx/BallSprite"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Ramp ("Ramp row", Float) = 0
        _Boost ("HDR boost on the brightest pixels", Float) = 2.2
        _Flicker ("Flicker of the bright pixels", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "Transparent+1" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "RicochetVfxCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Ramp, _Boost, _Flicker;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = VfxBillboard(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 m = VfxAtlas(i.uv);
                clip(m.a - 0.5);
                float t = m.r;
                float hot = smoothstep(0.6, 1.0, t);
                t -= hot * _Flicker * 0.25 * VfxHash(float2(floor(_VfxTime * 12), _Ramp));
                float3 c = VfxRamp(t, _Ramp) * lerp(1, _Boost, hot);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
