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

## Heroes via TRELLIS (blocked, 2026-10-19)

- Licence verified: `trellis-community/TRELLIS` is tagged MIT and loads `JeffreyXiang/TRELLIS-image-large`, which redirects to the official `microsoft/TRELLIS-image-large` (MIT). Recorded in art-bible.md §8.
- **The browser pane can't feed it a local image.** The upload button opens a native file chooser. Clipboard paste and in-page clipboard writes are denied ("Write permission denied"). The pane can't reach a localhost file server from a public page. The HF login can't be carried to the app at top level, because the iframe gets its ZeroGPU auth from the parent page.
- The headless API (`tools/trellis_space.py`) works up to generation, then stops at "exceeded your ZeroGPU quota (120s requested vs. 0s left)" when anonymous.
- **To unblock:** the user runs `hf auth login` in a terminal (their own token, typed by them). Then run `uv run --with gradio_client python tools/trellis_space.py models/raw concept/hero_*.jpg`, then decimate.py at ~800 tris. Free accounts get a small daily ZeroGPU allowance, so 4 heroes may need to be spread over 2 days.

## Next

1. Done: the 7 remaining concepts are generated (13 Figma AI calls in total). Mender's wax is lighter than the enemy value range; decimate.py's `--vrange` pulls it down.
2. Sign in to HF, then run TRELLIS.2 on the 4 heroes and decimate them to ~800 tris.
3. Analysis side: run the similarity check on `concept/` and `models/lowpoly/`.
