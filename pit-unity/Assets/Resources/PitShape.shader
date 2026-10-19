// Unlit, alpha-blended vertex colour times the pixel-art atlas (point filtered, set on the texture). Used for every
// shape and sprite Gfx draws. _Intensity lifts the Glow layer above the bloom threshold; _ZTest lets world shapes be
// hidden by 3D units while UI always draws on top. Has no LightMode tag, so URP draws it in its unlit pass.
Shader "Pit/Shape"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Intensity ("Intensity", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest [_ZTest]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv) * i.color;
                c.rgb *= _Intensity;
                return c;
            }
            ENDCG
        }
    }
}
