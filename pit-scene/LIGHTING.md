# Theme 1 "The Sunken Yard": floor, fog and lighting setup

Values for Unity URP. The Blender previews use the same numbers (`tools/assemble.py`). The base values come from `ballpit-spec/style.md` §3. Theme colours come from `pit-art/art-bible.md` §3.

Frame: the board stands in Unity's XY plane and the camera looks along +Z. Pieces sit under a `PitRoot` (see ASSEMBLY.md), so the pit floor faces the camera and the far end of the pit is screen-up (+Y).

## Camera (style.md §2 as ruled 2026-10-19: perspective, about 30° down, from behind and above the player)
| Setting | Value |
|---|---|
| Projection | Perspective, vertical FOV 65° |
| Clear | Solid colour **#262937** (38, 41, 55), the same as the fog |
| Pitch | **30° from looking straight down at the board**, tipped toward the far end: the camera sits behind and above the player. In Unity's XY board frame that is Euler X = −30°. The board stays a flat 2D plane. |
| Fit | Under perspective the **near row** is the widest row on screen, so it is the binding constraint. Fit the near row's corners, plus half a cell each side, to the screen width. The far row then fills about 71 % of the width (style.md's "far row fits 7 columns" holds automatically). Centre the board vertically on screen. |
| Look-at point / distance along the view axis / distance to board centre | 7×9: z = −0.85 / **14.66** / 15.1 · 9×11: −1.02 / 18.28 / 18.8 · 11×13: −1.19 / 21.90 / 22.5 · 13×15: −1.36 / **25.51** / 26.2 (PitRoot-local z along the pit) |
| Camera position (PitRoot local: x, y = height above floor, z) | 7×9: (0, 12.70, −8.18) · 13×15: (0, 22.10, −14.12) |
| Clip | Near 0.3, far 1000 |
| Bloom / HDR | Bloom on, threshold about 1.0 (linear). Scenery never exceeds V 33 %, so only balls, VFX and enemy eyes bloom. |

**Distance vs style.md's 10–12 units:** at FOV 65° the 7×9 board with half-cell margins needs 15.1 units from the board centre (14.0 at FOV 70°). At 10–12 the outer columns of the near rows fall off-screen. Either accept about 15, or drop the near-row margin to about 0 and use FOV 70° (that gives about 12.5). `fit_camera()` in `tools/assemble.py` solves this for any FOV and board size.

## Fog
| Setting | Value |
|---|---|
| Mode | Linear |
| Colour | **#262937** (38, 41, 55) |
| Start / end | **camera distance (to the look-at point) + 2 / + 14**: 7×9 → **16.7 / 28.7**; 9×11 → 20.3 / 32.3; 11×13 → 23.9 / 35.9; 13×15 → **27.5 / 39.5** |
| On board expansion | Tween start and end together with the camera pull-back (3 s) |

style.md §3 still says 30/50. With the pitched camera at 15–26 units, 30/50 fogs nothing at 7×9 and half the board at 13×15. Offsets that follow the camera keep the board clear and dissolve the far trench at every size.

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
| Shadows | Soft. Strength 0.8, distance **40**: style.md says 30, but at 13×15 the camera is 26 away. Main light only. |

Inner wall faces lie parallel to this light, so they get almost no direct light (they show ambient and the copper spot). This is intended: walls stay darker than the board.

A yaw of 10–20° would light one wall, but that wall then throws a shadow about 1 cell wide onto the board. Don't add a yaw.

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
- The floor is one `T1_FloorStrip_W{cols}` per 4-row group (ASSEMBLY.md §4). It scrolls and recycles with the walls. About 1 strip in 3 is the more-cracked B variant.
- Each cell shows 2×2 slabs separated by a hairline, with a wide joint on the cell border. The cell grid therefore reads at exactly 1.125 units, and strips join without a seam.
- The floor albedo is V 12–17 %. Lit, it reads lighter than the walls and darker than every unit.

## Corridor depth and the gate
- No ceiling and no far wall. The linear fog above ends the corridor: at 7×9 it is fully fogged from about k = 12, and the live run reaches k = 19.
- The end-of-corridor gate sits 3 cells past the board's far row on the final boss turn, in the fog's first third. It reads as a dim arch with hanging chains.
- **Gate renderers: Cast Shadows Off.** The key light runs along the corridor, so the lintel would throw a shadow band across the whole board.
- The copper spot stays put as the corridor scrolls. It lights whatever segment passes, so it needs no animation.

## Not in this scene (on purpose)
- No emissive surfaces on the scenery. The copper strap plate on the pillars is albedo only (#543A26, V 33 %).
- No ambient occlusion bake. The wall soot gradient is painted into the atlas.
