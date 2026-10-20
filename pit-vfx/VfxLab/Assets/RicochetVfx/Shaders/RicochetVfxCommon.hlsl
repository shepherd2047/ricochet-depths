// Shared by the Ricochet VFX shaders: the atlas (point sampled), the element ramps in it and the VFX clock.
// Atlas layout (RicochetVfxAtlas.png, 256×256): 16×16 px cells; the bottom two pixel rows hold the four 64×2 ramps,
// left to right Ember, Rime, Spark, Ray. Ramp t = 0 is the very dark end, t = 1 near white.
#ifndef RICOCHET_VFX_COMMON
#define RICOCHET_VFX_COMMON

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_MainTex);
SamplerState vfx_point_clamp_sampler;
#define VFX_ATLAS_TEXEL (1.0 / 256.0)
float _VfxTime;     // set by VfxBank.Tick

float3 VfxRamp(float t, float row)
{
    float2 uv = float2((row * 64.0 + 0.5 + saturate(t) * 63.0) * VFX_ATLAS_TEXEL, 0.5 * VFX_ATLAS_TEXEL);
    return SAMPLE_TEXTURE2D_LOD(_MainTex, vfx_point_clamp_sampler, uv, 0).rgb;
}

float4 VfxAtlas(float2 uv)
{
    return SAMPLE_TEXTURE2D(_MainTex, vfx_point_clamp_sampler, uv);
}

float VfxHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// Expand a quad mesh (vertices ±0.5 in xy) around the object's origin in view space: camera-facing billboard.
float4 VfxBillboard(float3 positionOS)
{
    float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
    float sx = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
    float sy = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y));
    float3 vs = TransformWorldToView(originWS) + float3(positionOS.x * sx, positionOS.y * sy, 0);
    return TransformWViewToHClip(vs);
}

#endif
