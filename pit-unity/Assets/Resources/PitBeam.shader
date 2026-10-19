// The window light pools on the prop lane (pit-scene T1_Beam, ASSEMBLY §5): unlit, additive, vertex colour × alpha
// (0.36 at the pool's centre, 0 at the rim) × _Color × (1 − pit fog), see PitUnit. Written for URP.
Shader "Pit/Beam"
{
    Properties
    {
        _Color ("Colour", Color) = (0.91, 0.64, 0.34, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
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

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float worldY : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.color = v.color;
                o.worldY = ws.y;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 col = _Color.rgb * i.color.rgb * i.color.a * (1 - PitFog(i.worldY));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
