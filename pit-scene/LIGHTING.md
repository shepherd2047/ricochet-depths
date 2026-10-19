# Theme 1 "The Sunken Yard": floor, fog and lighting setup

Values for Unity URP. The Blender previews use the same numbers (`tools/assemble.py`). The base values come from `ballpit-spec/style.md` §3. Theme colours come from `pit-art/art-bible.md` §3.

Frame: the board stands in Unity's XY plane and the camera looks along +Z. Pieces sit under a `PitRoot` (see ASSEMBLY.md), so the pit floor faces the camera and the far end of the pit is screen-up (+Y).

## Camera (style.md §2 as ruled 2026-10-19, second ruling: perspective, 40° tilt, prop lane in frame)
| Setting | Value |
|---|---|
| Projection | Perspective, vertical FOV **65°** |
| Clear | Solid colour **#262937** (38, 41, 55), the same as the fog |
| Pitch | **40° from looking straight down at the board**, tipped toward the far end: the camera sits behind and above the player. In Unity's XY board frame that is camera rotation **Euler (−40, 0, 0)**. The board stays a flat 2D plane. |
| Fit | Under perspective the **near row** is the widest row on screen, so it is the binding constraint. Fit the near row's corners plus **1.5 cells each side** (the prop lane plus half a cell of wall) to the screen width. Centre the board vertically on screen. |
| Clip | Near 0.3, far 1000 |
| Bloom / HDR | Bloom on, threshold about 1.0 (linear). Scenery albedo never exceeds V 33 %, so only balls, VFX, enemy eyes and (slightly) the window panes bloom. |

Fitted values (PitRoot-local; z runs along the pit, y is height above the floor):

| Board | Look-at z | Distance along view axis | Distance to board centre | Camera position (x, y, z) |
|---|---|---|---|---|
| 7×9 | −0.87 | **18.39** | 18.96 | (0, 14.09, −12.69) |
| 9×11 | −1.08 | 22.12 | 22.83 | (0, 16.94, −15.30) |
| 11×13 | −1.29 | 25.85 | 26.69 | (0, 19.80, −17.90) |
| 13×15 | −1.50 | **29.57** | 30.56 | (0, 22.66, −20.51) |

`fit_camera()` in `tools/assemble.py` (defaults `--tilt 40 --margin 1.5 --fov 65`) solves this for any board size. At margin 1.5 the near rows show the whole lane plus the foot of the wall.

## Fog (pit-axis fog from the far-wall line; replaces the camera-distance fog, 2026-10-19)
spec §4.4 as changed in 309ff06 says the invisible far wall sits at the **far edge of the board's top row**: z_line = rows/2 × 1.125 (PitRoot-local z, measured from the board centre). Everything beyond that line must read as "distance": clearly darker, not erased.

Unity's built-in fog can't do this. Linear/exp fog runs on view depth, so:
- it never caps below 1;
- it fogs the tall walls beside the line far less than the floor. At 40° tilt the top of a 6-unit wall is 4.6 units closer in depth than its foot.

So: **turn the built-in fog off** (Lighting → Environment → Fog unchecked) and fog in the shader by PitRoot-local z. It doesn't depend on height or camera distance.

| Setting | Value |
|---|---|
| Colour | **#262937** (38, 41, 55), the same as the camera clear |
| Input | `dz = z_local − z_line`, where z_local is the fragment's PitRoot-local z (along the pit). Pass `_PitFarZ = z_line` as a global shader float. |
| Factor | `f = 0.45 × smoothstep(0, 1.125, dz) + 0.35 × saturate((dz − 1.125) / 9) + 0.20 × saturate((z_local − 18) / 4.5)` |
| Meaning | 0 on the board. 0.45 within the first cell past the line (the "this is where the pit ends" drop). Rises to 0.8 over the next 9 units. The last term takes the last live group (z 18 … 22.5, fixed, the treadmill's end) to 1, so the recycling cut never shows. |
| Apply | `colour = lerp(lit colour incl. emission, fog colour, f)` in `T1_Scenery`. In `T1_Beam`, multiply the alpha by `(1 − f)`. |
| On board expansion | Tween `_PitFarZ` with the camera pull-back (3 s). The last term doesn't move. |

Numbers per board (PitRoot-local z):

| Board | z_line (far wall) | f = 0.45 at | f = 0.8 at | Gate front when docked (z_line + 3.375) | f at the gate front |
|---|---|---|---|---|---|
| 7×9 | **5.06** | 6.19 | 15.19 | 8.44 | 0.54 |
| 9×11 | **6.19** | 7.31 | 16.31 | 9.56 | 0.54 |
| 11×13 | **7.31** | 8.44 | 17.44 | 10.69 | 0.54 |
| 13×15 | **8.44** | 9.56 | 18.56 | 11.81 | 0.54 |

All sizes reach f = 1 at z = 22.5. The gate always docks 3 cells past the line, so it always sits at about 54 % fog. It reads as a dark arch with chains against the fog colour (`preview/t1_corridor_13x15_final.png`).

- **Units beyond the line:** new waves placed above the top row (spec §1.3 step 3) sit in this fog too, if their shader uses the same factor. Then they "emerge from the distance" as they roll over the line. That call belongs to the main/UI side. Balls never pass the line.
- **Fallback:** if a custom fog isn't possible, use built-in linear fog on view depth. Start = camera distance along the view axis + (z_line − look-at z) × sin 40°, which puts the line on the floor exactly. End = start + 5. The walls past the line will then stay brighter than the floor, and the far cut must be hidden by disabling groups at z > 18.

## Ambient (Environment Lighting → Source: Gradient)
| | sRGB | Hex |
|---|---|---|
| Sky | 54, 58, 66 | #363A42 |
| Equator | 29, 32, 34 | #1D2022 |
| Ground | 12, 11, 9 | #0C0B09 |
Intensity multiplier 1. No skybox and no reflection probe: set Environment Reflections to Custom with a black cubemap, or intensity 0.

## Key light
| Setting | Value |
|---|---|
| Type | Directional, white #FFFFFF, intensity **1** |
| Rotation | Euler **(10, 0, 0)** in Unity world: it shines almost straight into the screen, tipped 10° downward (toward the player end) |
| Shadows | Soft. Strength 0.8, distance **45**: at 13×15 the camera is 30.6 away. Main light only. |

Inner wall faces lie parallel to this light, so they get almost no direct light from it. The wall fill below lights them, so the walls still stay a little darker than the board.

A yaw of 10–20° would light one wall, but that wall then throws a shadow about 1 cell wide onto the board. Don't add a yaw.

## Wall fill (two lights, added 2026-10-19 after review: wall faces read too dark)
| Setting | Value |
|---|---|
| Type | 2 × Directional, colour **#96ACBE**, intensity **0.8** each, **no shadows** (URP additional lights, per-pixel) |
| Direction | One per wall. Each travels across the corridor into the wall it lights, about 80° from straight down. It is tipped 30° toward the far end, so the post sides facing the player get light too. Pit frame (Blender) Euler XYZ (30°, ±80°, 0): +80 lights the left wall, −80 the right. |
| Why | The key runs parallel to the walls, so without these the wall faces get ambient only and the bricks, posts and chains don't read. The fills add little to the floor, because they hit it nearly edge-on. |

## Theme light: furnace mouth (copper spot)
| Setting | Value |
|---|---|
| Type | Spot, colour **#D98841**, no shadows |
| Position (PitRoot local: x across, y out of the floor, z along the pit) | x = −(cols/2 × 1.125) − 0.5, y = 6.0, z = (rows/2 + 3) × 1.125 (behind the left wall at the far end, high: "off-screen top-left") |
| Aim | The right wall's face at mid board: (cols/2 × 1.125, 1.5, 0.15 × rows × 1.125) |
| Intensity / range | about **60** (URP inverse-square) / range 25. Tune by eye until the lit bricks on the right wall reach about V 30 % at most |
| Angle | Outer 55°, inner 30° |
| On board expansion | Recompute the position and aim from the new cols/rows and tween with the walls |

## Floor
- The floor is one `T1_FloorStrip_W{cols+2}` per 4-row group (board plus both prop lanes) (ASSEMBLY.md §4). It scrolls and recycles with the walls. About 1 strip in 3 is the more-cracked B variant.
- Each cell is one bevelled slab with a black seam. Tones alternate like a chessboard (floor A / floor C), so the cell grid reads at exactly 1.125 units. Strips join without a seam.
- The floor albedo is V 26–31 % (style.md §4 as ruled 2026-10-19: main scenery colours V 25–33 %, ceiling 33 %). Lit, it reads lighter than the walls and darker than every unit.

## Corridor depth and the gate
- No ceiling. The far wall is invisible, at the board's top edge (spec §4.4). Past it the pit-axis fog above darkens the corridor to 0.8 and reaches 1 only at the treadmill's end (z 22.5, k = 20).
- The end-of-corridor gate sits 3 cells past the board's far row on the final boss turn, at fog 0.54. It reads as a dark arch with hanging chains.
- **Gate renderers: Cast Shadows Off.** The key light runs along the corridor, so the lintel would throw a shadow band across the whole board.
- The copper spot stays put as the corridor scrolls. It lights whatever segment passes, so it needs no animation.

## Windows (Theme 1 furnace glow, added 2026-10-19 at the user's request)
- Three wall segments have a pier with a pointed-arch window. The pane, sill and reveals emit warm #E8A456 through `T1_Scenery_Emit.png` (Unity: emission colour white, HDR intensity about +1).
- Below each window is an additive pool of light (`T1_Beam`, alpha 0.36 → 0). It lies where the light would land: over the kerb and the board's edge column, clear of the lane props (it used to sit on the lane floor, where the props hid it). It is a mesh, not a real light, so it costs nothing, and it is too faint to read as a gameplay marker.
- The copper spot stays as it is. The windows read as its source.

## Not in this scene (on purpose)
- No emissive surfaces on the scenery apart from the window panes. The copper strap plates are albedo only (#543A26, V 33 %).
- No ambient occlusion bake. The wall soot gradient is painted into the atlas.
