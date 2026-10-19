# pit-scene report: Theme 1 environment (2026-10-19)

Clean room: I read only `ballpit-spec/style.md`, `ballpit-spec/spec.md` §0–1, `pit-art/art-bible.md`, the header of `pit-art/tools/decimate.py`, and the pit-unity camera code (for its board frame). I did not open `ballpit-re/`, `pit-art/concept/`, `pit-art/models/`, or any commercial game material.

## Delivered
- **Concept:** `concept/t1_pit_mood_v1.png` (768×1376, 9:16)
  - One Figma AI call (gemini-3.1-flash-image), in the new Figma file "Pit Scene – Theme 1 Environment" (https://www.figma.com/design/DedEFonmVR7fukYkXLPyUz) under Shepherd Coulson's team.
  - **Approved by the user on 2026-10-19** as the Theme 1 mood. No other images were generated.
- **Kit (corridor version, after the spec §1.3a map change):** each piece as FBX and GLB, all sharing one 1024 atlas.
  - 9 wall segments in three sets:
    - start, the town-side upper foundry: `T1_Start_A/B/C`;
    - mid: `T1_Mid_A/B/C`;
    - end, the approach to the boss lair: `T1_End_A/B/C`. These are deeper and taller, with buttresses, a collapsed breach and heavy chains.
  - Each segment is 4 cells, i.e. one 4-row recycling group. Any segment joins any other, because every segment ends in plain brick with the same profile.
  - Floor strips `T1_FloorStrip_W7…W13_A/B`: 4 rows each, seamless along the scroll.
  - End-of-corridor gate: `T1_Gate_Tower` ×2 and `T1_Gate_Lintel`. It docks 3 cells past the board in the fog; the boss emerges from it.
  - The 3 loose props are kept.
  - Scripted low-poly geometry in headless Blender 5.2; no image-to-3D was used.
  - Largest piece: 1885 Unity verts (`T1_Start_A`). All pieces are ≤ 2000.
- **Previews (30° camera, fitted at about 15 units for 7×9):**
  - `t1_corridor_7x9_start.png` (turn 1);
  - `t1_corridor_9x11_mid.png` (turn 30);
  - `t1_corridor_13x15_final.png` (turn 60, gate docked);
  - `t1_kit_sheet.png`.
  - Each shows the open corridor receding into fog, with no ceiling.
- **Docs:**
  - `ASSEMBLY.md`: pieces, grid, scroll and recycling, set picking by progress, expansion, gate, material/atlas.
  - `LIGHTING.md`: fog, ambient, key light, copper spot, floor, gate shadows.

## Decisions and open points
1. **Camera: resolved 2026-10-19.** The user ruled for the 30° downward camera (style.md §2 updated). The previews were re-rendered from it.
   - I read "about 30° down" as 30° away from looking straight down, which matches the approved concept.
   - The fit is driven by the near row, because perspective makes it the widest row on screen. The 7×9 board with half-cell margins needs about 15 units at FOV 65°; style.md says 10–12. The trade-off is in LIGHTING.md, and analysis should pick one.
2. **Fog values differ from style.md** (30/50 there). With the pitched camera I use camera distance + 2 / + 14 (16.7/28.7 at 7×9, 27.5/39.5 at 13×15), tweened with the pull-back.
3. **Left wall is dark.** The key light runs parallel to the wall faces, so only the copper spot lights a wall (the right one), and the left wall gets ambient only.
   - Giving the key a yaw lights the left wall, but it throws a wall shadow about 1 cell wide onto the board, so I didn't.
4. **Corridor rules I chose (spec §1.3a leaves them open):**
   - Set thresholds: progress 0.3 / 0.7, with a ±0.05 per-group jitter.
   - Progress is measured when a group's rows reach the far row.
   - The gate docks 3 cells past the far row.
   - The gate casts no shadows.
   - All of this is in `tools/assemble.py`; tune it there and in ASSEMBLY.md §4.
5. **The gate is mostly framed out at 13×15.** The camera fits the board, so only the lintel and its chains show at the top of the frame, and the towers stand behind the wall line. If analysis wants the full gate on screen at the boss turn, the camera would have to pull back or tilt up for the boss intro. That is a camera call, not a kit change.
6. **Props never sit on the board.** In the concept image they stand in the play area. In the kit they hang on walls, sit in niches, or lie on the floor beyond the 15-row board.

7. **Wall detail pass (user review: the walls showed no detail and were far from the concept):**
   - Recessed brick panels between riveted iron posts, with big cap blocks.
   - Chain links about 1.6× bigger.
   - Bevelled floor slabs, one per cell.
   - Two cool wall-fill lights with no shadows (LIGHTING.md).
   - Still unlike the concept:
     - The board-fit camera shows the walls nearly edge-on.
     - The concept's floor props stand in play cells.
     - The concept is brighter than the art bible's scenery values.

## Proposals for art-bible.md (not applied)
- §3 Theme 1 local light: add "optional cool fill (#5A6B78, point light, low intensity, near end, no shadows), so the wall opposite the furnace reads". This is the only way I see to light both walls without a board shadow.
- §3 wall kit list: replace the per-cell wall list with "three 4-cell segment sets (start / mid / end) for the scrolling corridor, plus an end-of-corridor gate". The pillar is now a pier inside segments, not a run-end cap.

## Not verified
- The FBX axis conversion in a real Unity import. The GLB axes were checked numerically; for FBX, ASSEMBLY.md §3 has the check.
- The URP intensity of the copper spot (about 60) is converted from the Blender preview; tune it by eye.
