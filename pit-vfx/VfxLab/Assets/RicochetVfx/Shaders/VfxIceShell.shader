// Freeze shell: a copy of the enemy mesh pushed out from its centre (_ShellScale) and drawn as faceted, see-through ice.
// Facet normals come from screen-space derivatives, every facet gets its own tone from the Rime ramp, the rim is
// bright and a few facets glint at 12 fps. _Amount 0..1 grows it on; 1..2 is the break (it flashes white and thins).
Shader "RicochetVfx/IceShell"
{
    Properties
    {
        _MainTex ("Atlas (ramps)", 2D) = "white" {}
        _Ramp ("Ramp row", Float) = 1
        _Boost ("HDR boost", Float) = 2.2
        _Amount ("Amount", Range(0, 2)) = 1
        _ShellScale ("Shell scale", Float) = 1.14
        _ShellCenter ("Shell centre (object space)", Vector) = (0, 0, 0, 0)
        _Opacity ("Opacity", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-2" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "RicochetVfxCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _ShellCenter;
                float _Ramp, _Boost, _Amount, _ShellScale, _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float grow = saturate(_Amount);
                float brk = saturate(_Amount - 1);
                float s = lerp(0.9, _ShellScale, grow) + 0.08 * brk;
                float3 p = _ShellCenter.xyz + (v.positionOS.xyz - _ShellCenter.xyz) * s;
                // push out along the normal in world units: imported models often carry a large object scale
                o.positionWS = TransformObjectToWorld(p) + TransformObjectToWorldNormal(v.normalOS) * 0.015;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float facing = abs(dot(n, v));
                float rim = 1 - facing;
                float3 q = floor(n * 3.0 + 0.5);                                  // facet id from its normal
                float facet = VfxHash(q.xy + q.z * 7.13);
                float frame = floor(_VfxTime * 12);
                float glint = step(0.9, VfxHash(float2(facet * 91.7, frame)));
                float brk = saturate(_Amount - 1);
                float t = saturate(0.3 + 0.3 * facet + 0.55 * rim * rim + glint * 0.6 + brk);
                t = floor(t * 8 + 0.5) / 8;
                float3 c = VfxRamp(t, _Ramp) * lerp(1, _Boost, t * t * t);
                float a = (_Opacity * (0.55 + 0.45 * rim) + glint * 0.3) * step(0.01, saturate(_Amount)) * (1 - brk * 0.7);
                return half4(c, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
