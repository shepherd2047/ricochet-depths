# Ricochet VFX: integration

This package holds the demo VFX for four balls: **Ember** (`ember`, burn), **Rime** (`frost`, freeze), **Spark** (`spark`, chain lightning) and **Ray** (`ray`, row laser). It is built for Unity 6000.6 with URP 17.6 and uses only built-in tools: Shuriken, a few small custom shaders, one atlas and no third-party packages.

| What | Where |
|---|---|
| Package to import | `pit-vfx/RicochetVfx.unitypackage` (contains `Assets/RicochetVfx/` only) |
| Source project | `pit-vfx/VfxLab` (preview scene `Assets/VfxLabPreview/VfxLab.unity`) |
| Previews | `pit-vfx/Previews/{Ember,Rime,Spark,Ray}.mp4` (540×960, 30 fps), `.gif` (270×480, 15 fps), `overview.png` |

## 1. Setup

1. Import `RicochetVfx.unitypackage`. It needs URP; the runtime asmdef is `RicochetVfx` and the editor one is `RicochetVfx.Editor`.
2. Nothing has to go in the scene. The first API call creates a `VfxBank` object, which loads `Resources/RicochetVfx/VfxBank.asset` and uses `Camera.main`. Alternatively, put a `VfxBank` on a GameObject yourself and set `viewCamera`, `up`, `scale` and `beamHeight`.
3. The board convention is pit-unity `Board3D`: the board lies in the world XY plane (x right, y toward the far wall), 1 cell = 1 unit, and **up out of the floor is −Z** (`VfxBank.up = (0,0,-1)`). If the board is scaled, set `VfxBank.scale` to the world size of one cell.
4. Enemies need no setup. The first `Flash`/`Status` call on an enemy adds `VfxHitFeedback`, which collects every renderer under it except VFX. For the flash, tint and statuses to show, the enemy's shader must carry the hit-feedback properties (§3).
5. Bloom: see §4.

## 2. API (`using RicochetVfx;`, all static on `VfxBank`)

```csharp
VfxBall h = VfxBank.Ball("ember", ballTransform);   // sprite + glowing halo + element trail, follows the transform
VfxBank.StopBall(h);                                 // ball gone: trail fades out, handle returns to the pool

VfxBank.Hit("frost", contactPos, bounceNormal);      // hit effect (any surface: enemy, wall)
VfxBank.Flash(enemyGO, "spark");                     // white flash (2 steps, 0.1 s) + element tint (0.45 s) on the enemy

VfxBank.Status(enemyGO, "ember", true/false);        // burn: char + crawling embers + pixel flames
VfxBank.Status(enemyGO, "frost", true/false);        // freeze: ice overlay + faceted ice shell; false = shatter burst
VfxBank.Status(enemyGO, "spark", true/false);        // shock: blinking electric rim (Ray has no status)
VfxBank.ClearStatus(enemyGO);                        // ends everything (call before destroying/pooling the enemy)

VfxBank.Chain(fromPos, toPos);                       // one jagged, flickering arc (0.1 s hold + flicker-out)
VfxBank.Chain(new List<Vector3>{a, b, c}, 0.07f);    // chain through several enemies, one jump every 0.07 s

VfxBank.RowBeam(rowY, xMin, xMax, targets, hitX);    // row laser across the board at world y = rowY
                                                     // charge 0.14 s -> beam 0.22 s -> fade with shimmer 0.3 s
                                                     // spawns a hit spark on every position in targets when it fires
VfxBank.Delay(0.14f, () => ...);                     // run something on the VFX clock (e.g. flash row targets when the beam fires)
```

* The ball ids are Content.cs ids. `"frost"` and `"rime"` both map to Rime. An unknown id is a no-op, and `Ball` returns null for it.
* `VfxElement` overloads exist for `Hit` and `Status`.
* For the full board width, pass xMin/xMax at the inner wall faces (e.g. `-3.5 / 3.5` on the 7-wide board). The beam is lifted `beamHeight` (0.45 cells) off the floor.
* Everything is pooled, so there are no per-hit allocations after warm-up. `VfxBank.ReleaseAll()` clears every effect, e.g. on a room change.
* Clock: `VfxBank` ticks itself in `Update` with `Time.deltaTime` (so it follows `timeScale`), after other scripts (execution order 1000). For deterministic capture or replay, set `manualTick = true` and call `Tick(dt)` yourself. `VfxLabCapture` does this.
* Shader animation (burn flicker, ice glints, shock blink, ribbon jitter) runs on the global `_VfxTime`, which `Tick` sets. Everything moves on stepped frames at 12–20 fps on purpose.

### Typical game hooks

| Event | Calls |
|---|---|
| Ball spawned / despawned | `Ball(id, t)` / `StopBall(h)` |
| Ball hits enemy | `Hit(id, p, n)` + `Flash(enemy, id)` |
| Burn applied / expired | `Status(enemy, "ember", true/false)` |
| Freeze applied / ends | `Status(enemy, "frost", true)` / `Status(enemy, "frost", false)` (shatters and flashes) |
| Chain lightning | `Chain(points)`, then `Flash` each target after `i * 0.07 s` |
| Ray laser | `RowBeam(rowY, xMin, xMax, targetPositions, hitX)`, then `Delay(0.14f, flash targets)` |
| Enemy dies | `ClearStatus(enemy)` before it is destroyed |

## 3. Enemy shader: flash and tint properties

`VfxHitFeedback` drives these through a **MaterialPropertyBlock**, so materials are never instanced:

| Property | Type | Meaning |
|---|---|---|
| `_Flash` | float 0..1 | white hit flash (1 → 0.5 → 0) |
| `_HitTint` | Color | rgb = element colour, a = amount (fades out in quarters over 0.45 s) |
| `_Ice` | float 0..1 | frozen overlay (pale cyan, bright rim) |
| `_Burn` | float 0..1 | charred surface + crawling glowing embers + 12 fps flicker |
| `_Shock` | float 0..1 | electric rim that blinks on random frames |
| `_VfxSeed` | float | per-enemy random, so patterns don't sync |
| `_VfxTime` | **global** float | VFX clock (set by `VfxBank.Tick`) |

pit-unity's `PitUnit` shader already has `_Flash` and `_Ice`. To add the rest:

1. Copy `Shaders/RicochetHitFx.hlsl` next to `PitUnit.shader` and `#include "RicochetHitFx.hlsl"`.
2. Add `_HitTint`, `_Burn`, `_Shock` and `_VfxSeed` to `Properties` and to the `UnityPerMaterial` CBUFFER so the SRP batcher stays compatible. Declare `float _VfxTime;` **outside** the CBUFFER.
3. The function needs the object-space position, so pass `positionOS` to the fragment stage. At the end of the fragment shader, split the colour into the lit surface (`col`) and the emission (`glow`), then call:
   ```hlsl
   RicochetHitFx(col, glow, normalWS, viewDirWS, positionOS, ndl,
                 _Flash, _HitTint, _Ice, _Burn, _Shock, _VfxSeed, _VfxTime);
   return half4(col + glow, 1);
   ```
   If PitUnit keeps its own `_Flash`/`_Ice` code, drop it so they don't apply twice. `Shaders/VfxUnit.shader` is a complete working example (lit, shadows, trilight ambient, albedo plus emission map).

The freeze shell is a separate mesh: a scaled copy of each enemy renderer, using `M_Rime_IceShell` and baked from SkinnedMeshRenderers. It needs nothing from the enemy shader.

## 4. Bloom (only balls and effects bloom)

Use URP Volume → Bloom with **threshold 1.05, intensity 0.9, scatter 0.65**, high-quality filtering off, the camera's post-processing on, and **HDR on** in the URP asset. The VfxLab project uses Gamma colour space and MSAA off, matching pit-unity.

Units stay below the threshold: lit colour and emission maps run at roughly 1.0 or less. Only the hot end of each ramp is boosted above 1 (`_Boost` on the VFX materials), so cores, sparks, beams and the burn embers bloom, while bodies and floor do not. The profile used for the previews is `VfxLabPreview/VfxLabBloom.asset`.

## 5. Contents

**Prefabs** (`Assets/RicochetVfx/Prefabs`), referenced by `VfxBank.asset`:

| Prefab | Script | Particles (max) |
|---|---|---|
| `Ball_Ember/Rime/Spark/Ray` | `VfxBall` (sprite quad, halo quad, ribbon trail, trail particles) | 40 / 30 / 20 / 12 |
| `Hit_Ember` | `VfxOneShot` (flash, sparks, embers) | 17 |
| `Hit_Rime` | `VfxOneShot` (ring, shards, dust) | 15 |
| `Hit_Spark` | `VfxCrackle` (flash, zaps, 4 jagged arcs) | 7 |
| `Hit_Ray` | `VfxOneShot` (flare, glints) | ≤ 6 |
| `Status_Burn` | `VfxStatus` (looping flames, embers) | ≤ 24 |
| `Status_Freeze` | `VfxIceShell` (shell meshes, glints) | ≤ 4 |
| `Shatter_Rime` | `VfxOneShot` (shards, puff, dust; box fitted to the enemy) | 27 |
| `Chain_Bolt` | `VfxBolt` (main and fork ribbons, end flash, zaps) | 6 |
| `Row_Beam` | `VfxRowBeam` (core and glow ribbons, charge, flare, sparkle, edge caps) | ≤ 35 |
| `RowHit_Ray` | `VfxOneShot`, one per enemy in the row | ≤ 6 |

**Materials** (`Materials/`): `M_<El>_Ball`, `M_<El>_Halo`, `M_<El>_Trail` and `M_<El>_Particle` for each element, plus `M_Spark_Bolt`, `M_Ray_BeamCore`, `M_Ray_BeamGlow` and `M_Rime_IceShell`.

**Shaders** (`Shaders/`):
* `RicochetVfx/Particle`: additive, ramp lookup.
* `RicochetVfx/Ribbon`: trails, bolts and beams.
* `RicochetVfx/BallSprite`: the ball sprite.
* `RicochetVfx/IceShell`
* `RicochetVfx/Unit`: example enemy shader.
* `RicochetHitFx.hlsl`
* `RicochetVfxCommon.hlsl`

**Atlas:** `Textures/RicochetVfxAtlas.png` is **256×256, about 6 KB**, point-sampled, uncompressed and without mips.
* The four **64×2 element ramps** are in the bottom two pixel rows: Ember, Rime, Spark, Ray from left to right.
* The 16×16 pixel cells hold the ball sprites, a 32×32 halo, flames, ice shards and flakes, zap glyphs, ray flares, and the flash flipbooks.
* All VFX shaders colour a grey mask through the element ramp with posterised steps.
* Each ramp has a very dark low end and a near-white top, and its middle passes through the Content.cs colour (ember `#ff7b39`, frost `#7fd8ff`, spark `#ffe14d`, ray `#ff9df0`).

## 6. Rebuilding and previews

Everything in `Assets/RicochetVfx` except scripts and shaders is generated, so edit the generators rather than the assets:
* `Editor/VfxAtlasBaker.cs`: ramps and pixel art.
* `Editor/VfxPrefabBuilder.cs`: materials, prefabs, the bank asset and all tuning values.

```sh
U=/Applications/Unity/Hub/Editor/6000.6.6f1/Unity.app/Contents/MacOS/Unity
$U -batchmode -quit -projectPath pit-vfx/VfxLab -executeMethod VfxLabBuilder.Build      # atlas, prefabs, preview scene
$U -batchmode -quit -projectPath pit-vfx/VfxLab -executeMethod VfxLabCapture.Run -vfxOut /tmp/cap -vfxSeconds 6
pit-vfx/tools/make_previews.sh /tmp/cap                                                  # MP4 + GIF + overview.png
$U -batchmode -quit -projectPath pit-vfx/VfxLab -executeMethod VfxLabExport.Run         # RicochetVfx.unitypackage
```

Build, bake and export are also in the editor menu **Ricochet VFX/…**. Capture needs a GPU (no `-nographics`). It ticks at exactly 1/30 s with fixed seeds, so the output is reproducible.

## 7. Notes and limits

* The ice shell copies the enemy mesh when the freeze starts. For an animated (skinned) enemy the copy is posed at that moment, so the frozen enemy should stop animating, which suits a freeze.
* `VfxHitFeedback` uses one MaterialPropertyBlock per renderer. It reads the renderer's current block before writing, so the game's own MPB values survive, but the game must not later overwrite the block without reading it first.
* Particle counts stay modest: the busiest case is a Ray beam over a full row, at roughly 35 + 6×4 particles plus two ribbons. All systems simulate in world space, with sizes in cells × `VfxBank.scale`.
