# Art side: status report (2026-10-19)

Clean room: I read only `ballpit-spec/style.md` and `ballpit-spec/spec.md` (archetype/footprint/ball-mechanic sections). I never opened `ballpit-re/` or the old Figma concept file. Names match the prototype's keys (`pit-game/`) where they exist; everything else is invented.

## Delivered

| Item | Where |
|---|---|
| Art bible: setting, 4 theme palettes, 4 heroes, 8 enemies + boss, 12 ball looks, licences, pipeline, Mixamo steps | `art-bible.md` |
| 13 concept images: 4 heroes, all 8 theme-1 enemies and the Bellwarden boss. Low-poly render look on a grey background. | `concept/` (+ `_contact_sheet.png`, `_contact_sheet_enemies.png`); Figma file "Pit Art – Original Concepts" |
| Decimation script (headless Blender 5.2) | `tools/decimate.py` |
| TRELLIS.2 Space client | `tools/trellis2_space.py` |
| Local TripoSR (MIT) + marching-cubes shim | `tools/vendor/TripoSR`, `tools/vendor/.venv` (1 GB), `tools/vendor/shim/` |
| Raw image-to-3D mesh (TripoSR, 211k tris) | `models/raw/enemy_shambler_triposr.glb` |
| All 8 theme-1 enemies, low-poly: 200–350 tris, ≤ 382 Unity verts (smooth normals), 256 px posterised albedo, GLB | `models/lowpoly/` (+ `_lowpoly_enemies.png`); rerun with `tools/batch_enemies.sh` |

## What worked

- **Figma generate_image** (default flash-lite model): 6 calls, all usable on the first try. Wren came out faceless under the hood; that reads fine at game scale but can be regenerated.
- **Local TripoSR on the M1**: works on CPU with a PyMCubes shim (torchmcubes won't build) and the current xatlas (0.0.9 has no wheel). The model step takes ~46 s and mesh extraction ~13 s. The first run took 8.5 min in total because it downloads the weights (~1.7 GB) and rembg. Peak memory footprint is ~12.5 GB, so it swaps on 8 GB; close other apps. Output axes: up = −Y, front = +X.
- **decimate.py**: 211k → 299 tris, bakes vertex colours or image textures to 256 px, posterises, remaps value into the style.md ranges, and exports GLB and FBX. Preview: `models/lowpoly/enemy_shambler_preview.png`.

### Batch run of 7 enemies on the M1 (8 GB)

- One TripoSR process for all 7 images: **15 min total, no crash**. Model load took 9 s. Background removal (rembg) took 10.6 min and is the bottleneck. Inference took 7–44 s per image and mesh extraction 11–18 s.
- Peak memory footprint was 17.5 GB. Swap went from 6.7 GB to a peak of 13.2 GB, with the other sessions' work running at the same time. It's slow but works; run batches with other apps closed.
- Speed-up: the concept images already have a plain background, so a pre-cut PNG with alpha (or `--no-remove-bg`) would skip most of that 10 minutes.
- Decimating all 8 enemies takes 29 s. Lobber needed 280 tris to fit under 400 Unity verts because of UV seams.
- Quality: silhouettes hold up well. Thin parts (Mite's needle legs) mostly vanish. TripoSR desaturates the amber accents to beige, so eyes and glows must be hand-painted into the emissive map (planned anyway).

## What didn't, or isn't verified

- **TRELLIS.2 HF Space** (the preferred, MIT option): the API is reachable, but the anonymous ZeroGPU quota is 0 ("120s requested vs. 0s left"). It needs your own free HF login. Run `hf auth login` yourself, then:
  `uv run --with gradio_client python tools/trellis2_space.py concept/hero_tinker.jpg models/raw/hero_tinker.glb`
  It will then work for heroes, which TripoSR handles poorly (thin limbs, back side).
- `microsoft/TRELLIS` (v1) Space is in CONFIG_ERROR. Hunyuan3D-2.1's Space is up, but its licence excludes the EU/UK/South Korea, so it is not recommended for shipped assets.
- Not tested: Mixamo upload (needs your Adobe login), Unity import of the FBX, and heroes through any 3D tool.
- **Budget conflict:** an 800-tri hero with hard flat normals is ~2400 Unity verts, which breaks style.md's ≤ 1000. Use `--normals smooth` plus a derivative-normal flat shader, or lower the targets.

## Heroes via TRELLIS (done, 2026-10-19)

- Licence verified: `trellis-community/TRELLIS` is tagged MIT and loads `JeffreyXiang/TRELLIS-image-large`, which redirects to the official `microsoft/TRELLIS-image-large` (MIT). Recorded in art-bible.md §8.
- The browser pane couldn't feed the Space a local file (native file chooser; clipboard denied). After the user ran `hf auth login` themselves, `tools/trellis_space.py` used the cached token headless. **All 4 heroes took 2 min 12 s in total, with no quota error.**
- Raw output: 8–23k tris with a 1024 px texture, already Z-up and facing −Y (decimate.py defaults, no axis flags). Quality is much better than TripoSR: clean A-pose limbs, props kept (Tinker's launcher, Wren's lamp pole, Osk's hat).
- decimate.py fix: TRELLIS materials are metallic=1, which makes Cycles' diffuse-colour bake black. The script now forces Metallic to 0 before baking.
- Low-poly results (`models/lowpoly/hero_<key>.glb`, 512 px albedo, 1.0 m tall, smooth normals):

| Hero | Tris | Unity verts | Note |
|---|---|---|---|
| Tinker | 700 | ~960 | cobalt scarf and wristband read well |
| Wren | 650 | ~993 | violet hood lining and lantern read well |
| Osk | 550 | ~905 | jade sash came out grey-green; needs a paint pass |
| Juno | 700 | ~972 | magenta cap and sling bands mostly lost; needs a paint pass |

  800 tris would have meant 1085–1273 Unity verts (UV seams), over style.md's 1000, so the targets were stepped down until each hero fit. Settings: `--tex 512 --quantize 16 --height 1.0 --vrange 0.12,0.50 --accent-vrange 0.45,0.85 --accent-sat 0.5 --normals smooth`. With an accent cutoff of 0.4, skin counted as an accent and Tinker's blue was lost.
- Preview sheet: `models/lowpoly/_lowpoly_heroes.png`. Next for heroes: a signature-colour paint pass for Osk and Juno, then Mixamo (user login).

## Colour pass and starter set (2026-10-19)

- The user found the models colourless ("no colour at all"). Causes: style.md's low value ranges, muted concepts, and image-to-3D tools that wash colour out. `decimate.py` gained `--sat-boost`, and the bake cage/ray distance was widened (0.05 / 0.15 of the size), which fixed black blotches.
- **Decision (user):** heroes get bold saturated colours; monsters keep the original dark tones, so they're easy to tell apart.
- **Starter set for now (user: "one character is enough"):**

| Model | Source | Tris | Unity verts |
|---|---|---|---|
| `hero_tinker.glb` (bold) | TRELLIS from bold concept | 700 | ~975 |
| `enemy_shambler.glb` (dark) | hand-built in Blender (`tools/monsters/shambler.py`) | 198 | 392 |
| `enemy_brute.glb` (dark) | hand-built (`tools/monsters/brute.py`) | 190 | 374 |
| `enemy_spitter.glb` (dark) | hand-built (`tools/monsters/spitter.py`) | 200 | 396 |

  Tinker settings: `--tex 512 --quantize 16 --accent-sat 1.01 --sat-boost 1.2 --height 1.0 --normals smooth`. Preview sheet: `models/lowpoly/_starter_set.png` (true scale, rendered by `tools/lineup.py`).
- **Monsters hand-built (user: "can you redraw the three monsters yourself"):** the TRELLIS ZeroGPU quota ran out (resets around 2026-10-20 11:20), and the TripoSR meshes were dark blobs. So the three were modelled from primitives by script, one agent per monster, using the shared `tools/monster_lib.py`. That library provides lathe/box/wedge parts, per-face UVs onto one shared 512 theme atlas (`enemy_theme1_albedo.png` + `enemy_theme1_emissive.png`, made of colour swatches plus brick, slag-rock and rivet tiles), flat-shaded GLB export, Unity vert count and preview. Glow (eyes, flue mouth) lives only in the emissive map. To rebuild, run `blender -b -P tools/monsters/<key>.py`. Known gaps: plates are flat slabs, there are no glossy tar drips (one roughness for the whole material), and the Brute's face plate doesn't wrap around the cheeks.
- The bold Wren / Osk / Juno concepts (`concept/v2/`) aren't converted. Tinker's raw TRELLIS mesh has a launcher part floating off the right arm; it needs a fix before rigging.
- The test bold Shambler was discarded. The old dark Wren / Osk / Juno low-poly models are still in `models/lowpoly/` but parked.

## Tinker rigged and animated (2026-10-19)

- `tools/rig_hero.py` rigs a low-poly A-pose hero headless in Blender, **without Mixamo or any login**:
  - It merges UV-seam duplicates (975 → 354 positions; Unity still sees ~975 verts).
  - It moves Tinker's floating launcher into his right hand (`--prop-move=0.03,-0.02,-0.125`) and binds it 100 % to `RightHand`.
  - It estimates joints from the mesh. The crotch is found from the back half, because the apron hangs between the legs.
  - It builds 21 bones with Unity Humanoid auto-map names (Hips, Spine, Chest, Neck, Head, Left/Right Shoulder, UpperArm, LowerArm, Hand, UpperLeg, LowerLeg, Foot, Toes) and skins with bone-heat weights.
- **Clips** (30 fps, keyed in the script):

| Clip | Frames | Loop |
|---|---|---|
| Idle | 60 | yes |
| Walk | 24 | yes |
| Shoot | 18 | no |
| Hit | 12 | no |
| Death | 30 | no (holds lying on the back) |
| Victory | 30 | yes |

- Output: `models/rigged/hero_tinker.fbx` (Unity: set Rig = Humanoid, then split clips by the take names) and `hero_tinker.glb`. Previews are in `models/rigged/preview/` (`hero_tinker_all.gif` plus one GIF per clip).
- Rebuild: `blender -b -P tools/rig_hero.py -- --in models/lowpoly/hero_tinker.glb --out models/rigged/hero_tinker --prop-move=0.03,-0.02,-0.125 [--views DIR] [--clip-frames DIR]`.
- Sign rule (in the code): a positive X rotation swings a downward bone (leg, arm) backward and leans an upward bone (spine, head) forward.
- Not checked: import into Unity and Humanoid avatar validation. The launcher is a fork held upright in the hand; it isn't aimed down the arm.
- Enemies aren't animated yet. art-bible §6 says baked vertex animation at 8–12 fps.

## Enemies rigged and animated (2026-10-19)

- `monster_lib.py` now has a rig and clip toolkit:
  - `finish()` keeps each part's name as a vertex group.
  - `make_rig()` binds each part 100 % to one bone (rigid skin).
  - `add_clip()` keys poses about world axes, including squash/stretch scale. It writes keys every 2 frames at 24 fps with constant interpolation, which gives the 12 poses/s that style.md §6 asks for.
  - `export_rigged()` writes `models/rigged/enemy_<key>.fbx` (Unity: Rig = Generic) and `.glb`.
  - `render_clips()` and `tools/make_gifs.py` produce the previews.
- Each monster script appends its rig behind `MONSTER_RIG=1` (frame renders behind `MONSTER_CLIPS=<dir>`); a plain run still builds only the static model. One agent animated each monster.

| Enemy | Bones | Idle | Move | Attack | Hit | Death |
|---|---|---|---|---|---|---|
| Shambler | Root, Body, Face, Crust, LegL, LegR | 40 loop | 16 loop (stomp) | 16 (lunge-slam) | 10 | 20 (crust pops off, flattens) |
| Brute | Root, Body, Visor, Bumps, LegL, LegR | 44 loop | 20 loop (heavy stomp) | 20 (rear-up headbutt) | 10 (barely budges) | 24 (visor drops, topples forward) |
| Spitter | Root, Body, Flue, Drip, LegL, LegR | 40 loop (flue puff) | 16 loop (waddle) | 16, **projectile release on frame 8** at the flue mouth | 10 | 20 (flue falls off to +X) |

- Frame counts are at 24 fps. Verts are unchanged (392 / 374 / 396).
- Previews are in `models/rigged/preview/` (`enemy_<key>_<clip>.gif`, `_all.gif`, and the key-frame sheet `_enemy_keys.png`).
- Known issues:
  - From the high camera the legs are mostly hidden, so Move reads through the body roll, bob and squash.
  - Brute's Death drifts 0.3–0.4 m forward.
  - Spitter's Death flue lands about 0.45 m to the side, slightly outside the tile.
  - Death clips animate scale on Root/Body, so the engine must play the full hierarchy.
  - Not imported into Unity yet.
- Hero on-board size: per the user, the hero must fill at least one cell. The rule is in art-bible §4 (draw 1.35 cells tall, scaled by height). The Unity session was asked to change Board3D.Hero.

## Next

1. Done: the 7 remaining concepts are generated (13 Figma AI calls in total). Mender's wax is lighter than the enemy value range; decimate.py's `--vrange` pulls it down.
2. Remaining 5 enemies + Bellwarden: hand-build them with `monster_lib.py` the same way (faster and cleaner than image-to-3D at ≤ 400 verts). TRELLIS for the bold Wren / Osk / Juno when more heroes are wanted.
3. Analysis side: run the similarity check on `concept/` and `models/lowpoly/`.
