# XP gem: integration note

The user picked design **B Cluster** from `concept/gem_options_loud.png`: one crystal for the small tier, two for the medium and three for the large. The final cut ("B2" in `tools/gem.py`) keeps the loud look: bigger, brighter, a floor ring and, after the user asked "需要描边" ("it needs an outline"), an outline shell in separate meshes.

Previews: `_gem_xp_close.png`, `_gem_xp_game.png`. Rebuild with:

```
blender -b -P tools/gem.py -- --out <tmp> --loud --rim --export B2
```

## File

`models/lowpoly/gem_xp.glb` holds nine meshes, all untextured:

| Mesh | Tris | Unity verts | Height (cells) | Max radius (cells) |
|---|---|---|---|---|
| gem_t1 (value < 10) | 16 | 48 | 0.435 | 0.125 |
| gem_t2 (value < 100) | 25 | 75 | 0.600 | 0.283 |
| gem_t3 (value ≥ 100) | 34 | 102 | 0.784 | 0.369 |
| gem_rim_t1 | 16 | 48 | 0.525 | 0.168 |
| gem_rim_t2 | 25 | 75 | 0.679 | 0.326 |
| gem_rim_t3 | 34 | 102 | 0.859 | 0.413 |
| gem_ring_t1 | 24 | 72 | flat, z 0.006 | outer 0.197 |
| gem_ring_t2 | 24 | 72 | flat | outer 0.232 |
| gem_ring_t3 | 24 | 72 | flat | outer 0.251 |

All meshes share one origin. The rim shell's lowest point is z = 0; the crystal inside it starts at z = 0.045 / 0.033 / 0.030.

- **Units are cells.** Blender is Z-up with the lowest point at z = 0. The glTF is Y-up, as for the enemies, so apply the same axis mapping you use for `enemy_*.glb`: the game's up is −z, standing on z = 0.
- **Faces are already split** per facet; every vertex belongs to one face.
- **COLOR_0** holds the facet colour (linear in glTF). **uv.x** holds the emissive strength 0–1, which PitUnit multiplies by 3. uv.y is unused.

## Colours (sRGB)

| Tier | Base hue |
|---|---|
| t1 | #3BEA62 green |
| t2 | #9C5BFF violet. It replaces cyan, which blended into the teal floor. |
| t3 | #FF4BAE pink |

Facet scheme for the crystals, with the emissive value after the ×1.6 loud boost:

| Facet | Colour | Emissive |
|---|---|---|
| Glint: the one face toward the key light | base mixed 72 % to white | 0.88 |
| Crown: faces with normal.z > 0.35 | base mixed 38 % to white | 0.64 |
| Girdle | base × 1.0 and base × 0.68, alternating | 0.40 |
| Pavilion: faces with normal.z < −0.35 | base × 0.42 | 0.16 |

The rings are flat tier colour with emissive 0.5.

## Engine side

- **Spin and bob:** keep the current values, 90°/s and 0.05 cell. Spin the crystal mesh only.
- **Floor ring:** draw it at the gem's floor position. It must not bob with the gem. It may spin, since it is a circle. It replaces the dark contact disc. If you keep the disc as well, draw the ring on top of it.
- **Visibility:** the loud look relies on brightness, not bloom. If bloom is on, a faint rim is fine, per style.md. The gem should still stay dimmer than the balls.
- **Outline:** `gem_rim_tN` is an inverted hull. Draw it with exactly the same transform as `gem_tN`, so it spins and bobs with the crystal.
  - **Geometry:** a copy of the crystal pushed out 0.045 cells along the averaged vertex normals, about 2–3 px at phone size.
  - **Winding:** every triangle is reversed. Its normals point inward and agree with the reversed winding, so a loader that flips meshes whose winding disagrees with their normals leaves it alone.
  - **Culling:** with normal back-face culling, only the far side of the shell draws, which shows as a band around the silhouette.
  - **Colour:** tier hue mixed 70 % to white. t1 #C4F9D0, t2 #E1CEFF, t3 #FFC9E7.
  - **Emissive:** uv.x = 1.0. After ×3 and the game's ×0.4, that is 1.2. If the rim needs to pop more, leave the rim meshes out of the 0.4 scale.
  - **Check image:** `_gem_xp_rim_check.png`, rendered with emissive ×1.2. Left is a close-up; right is the game view shrunk to about phone size.
