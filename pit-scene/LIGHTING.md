# Theme 1 "The Sunken Yard": floor, fog and lighting setup

Values for Unity URP. The Blender previews use the same numbers (`tools/assemble.py`). The base values come from `ballpit-spec/style.md` §3. Theme colours come from `pit-art/art-bible.md` §3.

Frame: the board stands in Unity's XY plane and the camera looks along +Z. Pieces sit under a `PitRoot` (see ASSEMBLY.md), so the pit floor faces the camera and the far end of the pit is screen-up (+Y).

## Camera (style.md §2 as ruled 2026-10-19, second ruling: perspective, 40° tilt, prop lane in frame)
| Setting | Value |
|---|---|
| Projection | Perspective, vertical FOV **65°** |
| Clear | Solid colour **#262937** (38, 41, 55), the same as the fog |
| Pitch | **40° from looking straight down at the board**, tipped toward the far end: the camera sits behind and above the player. In Unity's XY board frame that is camera rotation **Euler (−40, 0, 0)**. The board stays a flat 2D plane. |
| Fit | Under perspective the **near row** is the widest row on screen, so it is the binding constraint. Fit the near row's corners plus **1.0 cell each side** (the prop lane) to the screen width. Centre the board vertically on screen. |
| Clip | Near 0.3, far 1000 |
| Bloom / HDR | Bloom on, threshold about 1.0 (linear). Scenery albedo never exceeds V 33 %, so only balls, VFX, enemy eyes and (slightly) the window panes bloom. |

Fitted values (PitRoot-local; z runs along the pit, y is height above the floor):

| Board | Look-at z | Distance along view axis | Distance to board centre | Camera position (x, y, z) | Fog start / end |
|---|---|---|---|---|---|
| 7×9 | −0.95 | **16.77** | 17.40 | (0, 12.85, −11.73) | 18.8 / 30.8 |
| 9×11 | −1.16 | 20.50 | 21.26 | (0, 15.70, −14.34) | 22.5 / 34.5 |
| 11×13 | −1.37 | 24.23 | 25.13 | (0, 18.56, −16.94) | 26.2 / 38.2 |
| 13×15 | −1.58 | **27.95** | 28.99 | (0, 21.41, −19.55) | 30.0 / 42.0 |

`fit_camera()` in `tools/assemble.py` (defaults `--tilt 40 --margin 1.0 --fov 65`) solves this for any board size. At margin 1.0 the near rows show the lane fully, and the wall starts just at the screen edge. The wall comes into view further up the screen.

## Fog
| Setting | Value |
|---|---|
| Mode | Linear |
| Colour | **#262937** (38, 41, 55) |
| Start / end | **camera distance (to the look-at point) + 2 / + 14**: see the camera table (7×9 → **18.8 / 30.8**; 13×15 → **30.0 / 42.0**) |
| On board expansion | Tween start and end together with the camera pull-back (3 s) |

style.md §3 still says 30/50. With the pitched camera at 17–28 units, 30/50 fogs nothing at 7×9 and half the board at 13×15. Offsets that follow the camera keep the board clear and dissolve the far trench at every size.

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
| Shadows | Soft. Strength 0.8, distance **45**: at 13×15 the camera is 29 away. Main light only. |

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
- No ceiling and no far wall. The linear fog above ends the corridor: at 7×9 it is fully fogged from about k = 12, and the live run reaches k = 19.
- The end-of-corridor gate sits 3 cells past the board's far row on the final boss turn, in the fog's first third. It reads as a dim arch with hanging chains.
- **Gate renderers: Cast Shadows Off.** The key light runs along the corridor, so the lintel would throw a shadow band across the whole board.
- The copper spot stays put as the corridor scrolls. It lights whatever segment passes, so it needs no animation.

## Windows (Theme 1 furnace glow, added 2026-10-19 at the user's request)
- Three wall segments have a pier with a pointed-arch window. The pane, sill and reveals emit warm #E8A456 through `T1_Scenery_Emit.png` (Unity: emission colour white, HDR intensity about +1).
- Below each window is an additive pool of light on the lane floor (`T1_Beam`, alpha 0.42 → 0). It is a mesh, not a real light, so it costs nothing.
- The copper spot stays as it is. The windows read as its source.

## Not in this scene (on purpose)
- No emissive surfaces on the scenery apart from the window panes. The copper strap plates are albedo only (#543A26, V 33 %).
- No ambient occlusion bake. The wall soot gradient is painted into the atlas.
