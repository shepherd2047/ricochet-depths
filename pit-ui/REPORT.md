# pit-ui: report (2026-10-19)

**Done:**
- the Figma mockup (approved);
- greybox wireframes;
- a sliced sprite set and a 2048×1024 atlas;
- the Unity UI spec;
- the font licence.

## Where things are
- **Figma:** https://www.figma.com/design/o3fThRsqxzhSi7yzDbovIO
  - page *Wireframes*: greybox, Inter;
  - page *Mockup*: final pixel art with live Pixelify Sans text;
  - 10 portrait frames: Title, Hero pick, Battle HUD, Level up, Crucible, Crucible–Combine, Pause, Win, Lose, Boss warning + number styles.
- **Spec:** [UI-SPEC.md](UI-SPEC.md) covers scaling, safe area and capsule, hierarchy and anchors, 9-slice table, typography, components, screens and placeholders. Element coordinates are in [LAYOUT-TABLES.md](LAYOUT-TABLES.md).
- **Assets:**
  - `assets/atlas/ui_atlas.png` and `.json`: 138 sprites, 18 KB;
  - `assets/sprites/` and `assets/fonts/`;
  - `assets/unity/` holds the Editor importer and the reference runtime components.
- **Sources:** `tools/`. Everything is generated from Python, so the art can be changed and re-exported.

## Notes
- **No AI image generation was used.** All art is drawn in code (`tools/sprites.py`) from the art-bible palettes, so no credits were spent.
- **Clean room:** I read only `ballpit-spec/style.md`, `ballpit-spec/spec.md`, `pit-art/art-bible.md` and the sibling notes. I didn't open `ballpit-re/`, commercial screenshots, the "Pit Game – Concept Art" Figma file, or pit-art images and code.
- **Font:** Pixelify Sans (SIL OFL 1.1), from google/fonts. I made static Regular and Bold instances from it. The licence and attribution are in `assets/fonts/FONT-LICENSE.md`; ship `OFL.txt`.
- **Not verified:**
  - The C# helpers have not been compiled in Unity.
  - The TikTok capsule-rect API name and its Unity SDK binding are unconfirmed. A fallback rect matching the design's reserved zone is built in.
- **Placeholders that pit-unity must replace:**
  - item names and icons (6 invented items);
  - sample card copy;
  - the hero preview, which must become a 3D RenderTexture.
- The atlas only fills its top 53 px today. It can be cropped if GPU memory matters (see UI-SPEC §2).
