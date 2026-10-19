# pit-scene report: Theme 1 environment (2026-10-19)

Clean room: I read only `ballpit-spec/style.md`, `ballpit-spec/spec.md` §0–1, `pit-art/art-bible.md`, the header of `pit-art/tools/decimate.py`, and the pit-unity camera code (for its board frame). I did not open `ballpit-re/`, `pit-art/concept/`, `pit-art/models/`, or any commercial game material.

## Delivered
- **Concept:** `concept/t1_pit_mood_v1.png` (768×1376, 9:16)
  - One Figma AI call (gemini-3.1-flash-image), in the new Figma file "Pit Scene – Theme 1 Environment" (https://www.figma.com/design/DedEFonmVR7fukYkXLPyUz) under Shepherd Coulson's team.
  - **Approved by the user on 2026-10-19** as the Theme 1 mood. No other images were generated.
- **Kit:** 7 wall pieces, 2 floor tiles and 3 props, each as FBX and GLB, all sharing one 1024 atlas.
  - Scripted low-poly geometry in headless Blender 5.2; no image-to-3D was used.
  - Largest wall: 1710 Unity verts (Chain). All others are ≤ 472.
- **Previews:** `preview/t1_board_7x9.png` and `preview/t1_board_13x15.png`, plus `t1_kit_sheet.png`.
- **Docs:** `ASSEMBLY.md` (pieces, grid, pivots, expansion, material/atlas) and `LIGHTING.md` (fog, ambient, key light, copper spot).

## Decisions and open points
1. **Camera: resolved 2026-10-19.** The user ruled for the 30° downward camera (style.md §2 updated). The previews were re-rendered from it.
   - I read "about 30° down" as 30° away from looking straight down, which matches the approved concept.
   - The fit is driven by the near row, because perspective makes it the widest row on screen. The 7×9 board with half-cell margins needs about 15 units at FOV 65°; style.md says 10–12. The trade-off is in LIGHTING.md, and analysis should pick one.
2. **Fog values differ from style.md** (30/50 there). With the pitched camera I use camera distance + 2 / + 14 (16.7/28.7 at 7×9, 27.5/39.5 at 13×15), tweened with the pull-back.
3. **Left wall is dark.** The key light runs parallel to the wall faces, so only the copper spot lights a wall (the right one), and the left wall gets ambient only.
   - Giving the key a yaw lights the left wall, but it throws a wall shadow about 1 cell wide onto the board, so I didn't.
4. **Props never sit on the board.** In the concept image they stand in the play area. In the kit they hang on walls, sit in niches, or lie on the floor beyond the 15-row board.

## Proposals for art-bible.md (not applied)
- §3 Theme 1 local light: add "optional cool fill (#5A6B78, point light, low intensity, near end, no shadows), so the wall opposite the furnace reads". This is the only way I see to light both walls without a board shadow.
- §3 wall kit list: add "Pillar doubles as the run-end cap". The kit's 7th piece is the corner pillar, used as a pier every 8 slots and at the near end of each run.

## Not verified
- The FBX axis conversion in a real Unity import. The GLB axes were checked numerically; for FBX, ASSEMBLY.md §3 has the check.
- The URP intensity of the copper spot (about 60) is converted from the Blender preview; tune it by eye.
