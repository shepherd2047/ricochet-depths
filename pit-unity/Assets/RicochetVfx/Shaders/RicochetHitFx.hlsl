// Enemy hit feedback, written to drop into any unit shader (pit-unity's PitUnit, or RicochetVfx/Unit here).
// The host shader declares the properties (in UnityPerMaterial, so the SRP batcher stays happy) and passes them in:
//   _Flash 0..1, _HitTint (rgb colour, a amount), _Ice 0..1, _Burn 0..1, _Shock 0..1, _VfxSeed, and the global _VfxTime.
// VfxHitFeedback drives them through a MaterialPropertyBlock.
//   col  : the lit surface colour (stays under the Bloom threshold)
//   glow : additive emission (may go over the threshold: embers, frost rim, shock rim are effects)
#ifndef RICOCHET_HIT_FX
#define RICOCHET_HIT_FX

float rfx_hash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float rfx_noise3(float3 x)
{
    float3 i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(rfx_hash3(i), rfx_hash3(i + float3(1, 0, 0)), f.x),
                     lerp(rfx_hash3(i + float3(0, 1, 0)), rfx_hash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(rfx_hash3(i + float3(0, 0, 1)), rfx_hash3(i + float3(1, 0, 1)), f.x),
                     lerp(rfx_hash3(i + float3(0, 1, 1)), rfx_hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// nWS / viewWS normalised; posOS = object-space position (patterns stick to the model); ndl = main light N·L.
void RicochetHitFx(inout float3 col, inout float3 glow, float3 nWS, float3 viewWS, float3 posOS, float ndl,
                   float flash, float4 hitTint, float ice, float burn, float shock, float seed, float time)
{
    float frame = floor(time * 12.0);                 // pixel-rate animation, like the 8–12 fps unit clips
    float rim = pow(1.0 - saturate(dot(nWS, viewWS)), 2.0);

    // Burn: the surface chars, ember patches glow and crawl, the whole body flickers at 12 fps.
    if (burn > 0.001)
    {
        float n = rfx_noise3(posOS * 7.0 + float3(0, 0, frame * 0.35) + seed * 3.1);
        float ember = smoothstep(0.5, 0.72, n);
        float fl = 0.7 + 0.3 * rfx_hash3(float3(frame, seed, 1.7));
        col *= lerp(1.0, 0.45, burn);
        float3 hot = lerp(float3(0.75, 0.35, 0.11), float3(1.0, 0.9, 0.6), ember);
        glow += hot * (ember * 3.0 + rim * 1.2) * burn * fl;
    }

    // Freeze: a frosted, pale cyan overlay with a bright rim (the shell itself is a separate mesh).
    if (ice > 0.001)
    {
        float3 iceCol = lerp(float3(0.16, 0.42, 0.5), float3(0.85, 1.0, 0.97), rim) * (0.6 + 0.4 * ndl);
        col = lerp(col, iceCol, ice * 0.8);
        glow += float3(0.5, 0.85, 1.0) * rim * rim * ice * 0.9;
    }

    // Shock: an electric rim that blinks on random frames.
    if (shock > 0.001)
    {
        float on = step(0.35, rfx_hash3(float3(frame, seed, 4.2)));
        glow += float3(1.0, 0.93, 0.55) * (rim * 2.6 + 0.25) * shock * on;
    }

    // Element tint, then the white flash on top of everything.
    col = lerp(col, hitTint.rgb * (0.55 + 0.6 * ndl), hitTint.a * 0.35);
    glow += hitTint.rgb * hitTint.a * rim * 1.2;
    col = lerp(col, float3(1, 1, 1), flash * 0.9);
    glow *= 1.0 - flash * 0.85;
}

#endif
