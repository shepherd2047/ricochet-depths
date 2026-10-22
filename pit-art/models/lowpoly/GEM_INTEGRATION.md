# XP gem: integration note

The user picked design **B Cluster** from `concept/gem_options_loud.png`: one crystal for the small tier, two for the medium and three for the large. The final cut ("B2" in `tools/gem.py`) keeps the loud look (bigger, brighter, floor ring) but drops the outline shell to stay near 36 tris.

Previews: `_gem_xp_close.png`, `_gem_xp_game.png`. Rebuild with:

```
blender -b -P tools/gem.py -- --out <tmp> --loud --export B2
```

## File

`models/lowpoly/gem_xp.glb` holds six meshes, all untextured:

| Mesh | Tris | Unity verts | Height (cells) | Max radius (cells) |
|---|---|---|---|---|
| gem_t1 (value < 10) | 16 | 48 | 0.435 | 0.125 |
| gem_t2 (value < 100) | 25 | 75 | 0.600 | 0.283 |
| gem_t3 (value ≥ 100) | 34 | 102 | 0.784 | 0.369 |
| gem_ring_t1 | 24 | 72 | flat, z 0.006 | outer 0.188 |
| gem_ring_t2 | 24 | 72 | flat | outer 0.222 |
| gem_ring_t3 | 24 | 72 | flat | outer 0.241 |

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
- **Outline (optional):** the concept sheet showed a light outline. It is left out because an inverted-hull shell doubles the tris. If you want it anyway, `--rim` in gem.py adds it: about 32 / 50 / 68 tris.
