// The window light pools on the prop lane (pit-scene T1_Beam, ASSEMBLY §5): unlit, additive, vertex colour × alpha
// (0.42 at the pool's centre, 0 at the rim) × _Color, fading to nothing in the fog. Written for URP.
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float fog : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 col = _Color.rgb * i.color.rgb * i.color.a;
                col = MixFogColor(col, float3(0, 0, 0), i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
