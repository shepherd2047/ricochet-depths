# Ricochet Depths: UI spec for the Unity build

Status: mockup approved 2026-10-19. This document tells the Unity implementer how to build the approved screens in uGUI.

- Figma (approved mockup and greybox wireframes): https://www.figma.com/design/o3fThRsqxzhSi7yzDbovIO, pages **Wireframes** and **Mockup**, 10 frames.
- Exact coordinates for every element: [LAYOUT-TABLES.md](LAYOUT-TABLES.md), generated from `tools/layout.py`.
- Contact sheet: [mockup/contact.png](mockup/contact.png).

Target: a TikTok native mini game made with Unity. It is portrait only, English UI, single level-1 run, no ads, no login. Assets are loaded remotely and the whole game must stay under 30 MB.

---

## 0. Deliverables in this folder

| path | what |
|---|---|
| `assets/atlas/ui_atlas.png` | 2048×1024 RGBA UI atlas with all 138 sprites at native size (1 art px = 1 texel). It has 1 px edge extrusion and a 2 px gutter. 18 KB PNG. |
| `assets/atlas/ui_atlas.json` | Sprite rects with a top-left origin (`sprites`) and a Unity bottom-left origin (`list`), 9-slice borders as L,B,R,T, and glyph advances. |
| `assets/atlas/ui_atlas_preview.png` | The used strip of the atlas at ×4, for review. |
| `assets/sprites/*.png` | The same 138 sprites as separate PNGs, if you prefer Unity's Sprite Atlas (V2) packer. |
| `assets/unity/Editor/PitUiAtlasImporter.cs` | Slices the atlas automatically on import: borders, point filter, no compression. |
| `assets/unity/Runtime/PitPixelUi.cs` | Reference components `PixelCanvasScaler`, `SafeAreaRoot` and `PixelNumber`. **They have not been compiled or tested in Unity yet.** |
| `assets/fonts/PixelifySans-Regular.ttf`, `-Bold.ttf` | Static instances made from the variable font. |
| `assets/fonts/PixelifySans[wght].ttf` | The upstream variable font (google/fonts). |
| `assets/fonts/OFL.txt`, `FONT-LICENSE.md` | The SIL OFL 1.1 licence and attribution. Ship `OFL.txt` with the build. |
| `tools/*.py` | Generators: `sprites.py` (all pixel art), `layout.py` (screen layouts), `export_atlas.py`, `build_figma.py`, `dump_layout.py`. |

To regenerate everything, run this from `tools/`: `python3 export_atlas.py ../assets && python3 dump_layout.py`. It needs numpy and Pillow.

---

## 1. Pixel grid

- **Design grid: 180×320 art px (9:16).** All coordinates in this spec and in LAYOUT-TABLES.md use art px, with the origin at the top-left and y pointing down.
- Figma frames are 360×640, so 1 art px = 2 Figma units. The **pt sizes of text in Figma are therefore 2× the canvas size** (see §6).
- Sprites are authored at 1 texel per art px and use `pixelsPerUnit = 16` (style.md §7).
- **Integer scale only:** `s = max(2, floor(Screen.width / 180))`. Examples:

| device width (px) | s | canvas width (art px) |
|---|---|---|
| 720 | 4 | 180 |
| 1080 | 6 | 180 |
| 1170 | 6 | 195 |
| 1284 | 7 | 183 |
| 1440 | 8 | 180 |

  The design is 180 wide. Any extra width is margin; see §4 for how it is handled.
- 44 pt minimum touch target = 22 art px. Every tappable element in the design is ≥ 24×24 art px. The slot icons (20×20) are **display only**. If you later make them tappable for tooltips, give the raycast area 1 px of padding so it reaches 22×22.

---

## 2. Rendering settings

- Atlas texture: Sprite (2D and UI), Multiple, PPU 16, **Point** filter, no mipmaps, Clamp, **Uncompressed** RGBA32. Block compression smears 1 px outlines, and at 2048×1024 the texture costs 8 MB of GPU memory and 18 KB to download. Max size 2048, NPOT scale None. `PitUiAtlasImporter` applies all of these.
- Only the top 53 rows of the atlas are used today. The rest is free room for later art. If memory gets tight, it is safe to crop the atlas to 2048×64 (or repack to 512×128): every rect has its origin at the top-left, and only the `unity_y` values would change. Re-run `export_atlas.py` after changing `ATLAS_H`.
- Canvas: Screen Space – Overlay. Tick **Pixel Perfect**.
- `Image.pixelsPerUnitMultiplier = 1` on every sliced Image.
- All UI Images use the default UI material. The dim layer is the `white` sprite tinted `#06080A` at alpha 0.72 (or 0.55 / 0.78 / 0.80, depending on the screen; see the tables).
- Pixel snapping: with `scaleFactor` an integer and every RectTransform position and size an integer number of canvas units, everything lands on device pixels. **Keep anchoredPositions integral**, and round them at the end of tweens.

---

## 3. Safe areas and the TikTok capsule

The design reserves these zones (art px):

| zone | rect (x, y, w, h) | rule |
|---|---|---|
| status bar / notch | 0, 0, 180, 22 | nothing interactive |
| TikTok capsule (… / ◉ menu) | 124, 22, 52, 22 | **nothing at all**: no buttons and no text |
| home indicator | 0, 303, 180, 17 | nothing interactive |

At runtime:

1. Fit a `SafeArea` RectTransform to `Screen.safeArea` (`SafeAreaRoot` does this).
2. Read the capsule rect from the TikTok SDK at startup and set `SafeAreaRoot.CapsuleScreenRect`, converted to Unity screen pixels with a bottom-left origin. The mini-game JS API exposes a menu-button bounding rect (WeChat names it `getMenuButtonBoundingClientRect`). **The exact TikTok and Unity SDK call is unverified**; check it with the TikTok Unity SDK the ops rep provides. It returns CSS px from the top-left, so multiply by the device pixel ratio and flip y.
3. Fallback when the call is not available: a capsule 52×22 art px, flush right, directly under the safe-area top. This is exactly the zone the design reserves.
4. Keep these rules:
   - top HUD row 1 (pause, speed, gold) must end ≥ 4 art px left of the capsule;
   - row 2 (level badge, XP bar, turn text or boss bar) must start ≥ 2 art px below the capsule bottom.

   `SafeAreaRoot.leftOfCapsule` and `belowCapsule` enforce both. With the design capsule, gold ends at x = 110 and the capsule starts at x = 124, so nothing moves.

---

## 4. Canvas hierarchy and anchoring

```
UICanvas            Canvas (Overlay, pixelPerfect) + CanvasScaler + PixelCanvasScaler + GraphicRaycaster
├─ Backdrop         full-screen Image `white` tinted, raycast off (only used by the Title / Hero pick fallback bg)
└─ SafeArea         RectTransform fitted to Screen.safeArea (SafeAreaRoot)
   └─ Design        width 180, stretch vertically, centred horizontally (anchorMin .5,0 anchorMax .5,1, sizeDelta.x 180)
      ├─ TopBar     anchored top-stretch, height 52  (rows 1–2 of the HUD)
      │  ├─ Row1    (leftOfCapsule) pause, speed, gold
      │  └─ Row2    (belowCapsule)  level badge, XP bar, turn text | boss name + bar
      ├─ Middle     anchored centre, 180×(available) (modals, banners, floating numbers layer)
      ├─ BottomBar  anchored bottom-stretch, height 41 (HP bar + slot strip)
      └─ Modal      full stretch; one child per screen (LevelUp, Crucible, Combine, Pause, Results), each with its own Dim
```

Everything is under `Design`, so on wider screens (for example 195 units at 1170 px) the UI keeps 180 units and is centred. The 3D view behind is full-bleed and unaffected. Full-screen dims, the backdrop and the boss-banner stripes stretch across the **whole canvas** (anchor them to `UICanvas`, not `Design`), so no seam shows at the edges.

### Converting design coordinates

The design frame assumes a safe area that starts at y = 22 and ends at y = 303, which is 281 tall. Real phones are taller (for example 1080×2340 at s = 6 gives 390 units, minus insets). Place each element by **its group**:

| group | elements (prefix in LAYOUT-TABLES) | anchor and pivot | position |
|---|---|---|---|
| top | `hud/btn_*`, `hud/gold*`, `hud/level_badge*`, `hud/xp/*`, `hud/turn`, `hud/boss/*`, `pick/btn_back`, `pick/header` | top-left (0,1) | `(x, -(y - 22))` |
| bottom | `hud/hp/*`, `slots/*`, `title/btn_sound`, `title/btn_music`, `title/version` | bottom-left (0,0) | `(x, 303 - (y + h))` |
| modal / centre | everything else | centre (.5,.5) of `Middle`, pivot top-left | `(x - 90, -(y - 160))` |

Modal content was laid out to fit between the top HUD (ending at y = 74) and the HP bar (starting at y = 262). On taller phones it stays centred on y = 160, which is fine. On a phone with fewer than 281 usable units, which should not happen at 9:16 or taller, shrink by lowering `s` by one.

`num` entries give the string's anchor point:
- `l` means the left edge;
- `c` means the centre;
- `r` means the right edge;
- y is always the top of the glyphs.

`PixelNumber` takes `align` and positions itself the same way.

---

## 5. Atlas and 9-slice reference

Borders are in Unity order **L, B, R, T**, in texels. Sizes are the source sprite size. Scale sliced Images by changing their RectTransform size, never their localScale.

| sprite | size | border | use |
|---|---|---|---|
| `panel` | 24×24 | 7,7,7,7 | Pause panel, Results stats panel, Combine preview |
| `inset` | 12×12 | 3,3,3,3 | gold counter, stat rows, hero preview well, option icon well, legend panel |
| `btn_primary` / `_pressed` | 24×24 | 6,8,6,6 | copper call-to-action (PLAY, START RUN, RESUME, COMBINE, PLAY AGAIN) |
| `btn_secondary` / `_pressed` | 24×24 | 6,8,6,6 | slate buttons (pause, speed, back, reroll, skip, restart, quit, home, sound, music) |
| `btn_disabled` | 24×24 | 6,8,6,6 | any button that can't be used now (reroll without gold, combine without a valid pair) |
| `btn_danger` | 24×24 | 6,8,6,6 | spare (for example a confirm-quit dialog) |
| `card` | 32×32 | 8,8,8,8 | level-up card, hero tile |
| `card_copper` | 32×32 | 8,8,8,8 | Crucible option card |
| `card_disabled` | 32×32 | 8,8,8,8 | Crucible option that isn't available (no Lv 3 pair or no recipe); text #7C8488 |
| `select` | 12×12 | 4,4,4,4 | selection outline, drawn 2 px outside the selected card or slot |
| `slot_ball` / `slot_item` / `slot_empty` / `slot_fused` | 12×12 | 3,3,3,3 | loadout slots (`slot_fused` = a combined ball, which can't level) |
| `tag_gold` / `tag_green` / `tag_red` | 8×8 | 3,3,3,3 | LV n / NEW / MAX chips, HOST / PARTNER |
| `ribbon` | 24×16 | 6,5,6,5 | modal headers (LEVEL UP!, THE CRUCIBLE, COMBINE, PAUSED) |
| `bar_frame` | 12×10 | 3,3,3,3 | XP, HP and boss bar frames |
| `bar_hp`, `bar_hp_lag`, `bar_xp`, `bar_boss` | 4×4 | 1,1,1,1 | bar fills (see §7.2) |
| `banner_body` | 12×12 | 0,3,0,3 | boss warning body (stretched full width) |
| `banner_stripe` | 16×8 | — | **Tiled** Image, hazard stripes above and below the banner |
| `badge_level` | 18×18 | — | level badge (simple) |
| `pip_on` / `pip_off` / `pip_next` | 5×5 | — | level pips, 7 px pitch |
| `bar_tick` | 1×4 | — | optional HP tick every 25 % |
| `white` | 4×4 | — | dims and solid tints |
| `ic_*` | 16×16 | — | UI glyphs: pause play restart home reroll back lock check plus arrow skull clock sword star speed sound mute music close gem coin |
| `ic_coin_s`, `ic_heart_s` | 9×9 | — | inline coin (gold counter, button costs) and HP heart |
| `ball_*` | 16×16 | — | 12 balls + `ball_powder_keg` (the evolution) |
| `item_*` | 16×16 | — | **placeholder** item icons (see §10) |
| `hero_*` | 24×24 | — | hero emblems for Tinker, Wren, Osk and Juno (tile icon; ×3 as the preview placeholder) |
| `font_hud_*`, `font_dmg_*`, `font_crit_*` | 5–10 × 8/9/11 | — | bitmap digit glyphs (see §6.2) |

Icons are shown at ×1 in slots and ×2 inside level-up cards and combine slots. Always use integer multiples.

---

## 6. Typography

### 6.1 Pixelify Sans (TextMeshPro SDF): labels and body

- Font files: `PixelifySans-Regular.ttf` (400) and `PixelifySans-Bold.ttf` (700), both under the SIL OFL 1.1 (see `assets/fonts/FONT-LICENSE.md`).
- Create two TMP Font Assets:
  - Sampling 48, Padding 5, Atlas 512×512, Render Mode SDFAA;
  - Character Set: ASCII 32–126 plus `· × • ◉ – …`;
  - Atlas Population Mode **Static**, which keeps the font atlas in the build with no runtime font file.
  - Each atlas is ~256 KB uncompressed (Alpha8). Bold can be dropped by using Regular with Face Dilate 0.25 if size matters.
- **Size: Figma pt ÷ 2 = TMP font size in canvas units.** For example, a 12 pt tagline is size 6 and a 46 pt logo is size 23. Used sizes are 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 24, 28, 34 and 46 pt.
- **Drop shadow:** most text has a hard 1 art px ink shadow straight down. Make a material preset `Pixelify_Shadow` with Underlay on, colour `#0B0F11`, Offset X 0, Offset Y ≈ −0.5 (tune until the shadow is exactly 1 canvas unit), Dilate 0 and Softness 0. Text marked "no shadow" (tags, notes) uses the plain material.
- Line height 115 % for multi-line descriptions. Wrap on, overflow Truncate. Card descriptions must fit 2 lines and option descriptions 3 lines; write content to that limit.
- Colours used:

| token | hex | use |
|---|---|---|
| cream | `#F2EEE4` | primary text |
| body | `#D9D4C7` | descriptions and stat labels |
| muted | `#8FB0B8` | meta text (turn counter, LOADOUT, unselected hero names) |
| faint | `#5E7F89` | version and notes |
| ink | `#0B0F11` | text on tags and all shadows |
| copper | `#D98841` | logo "DEPTHS" |
| copper hi | `#F2B872` | boss name |
| gold | `#E6CA5C` | win title |
| red hi | `#FF7A7A` | lose title, WARNING |
| hero colour | per hero | hero role line: Tinker `#2B74D9`, Wren `#822BD9`, Osk `#2BD9AD`, Juno `#D92B9F` |

### 6.2 Bitmap digit fonts: all numbers that change during play

Original 5×7 digit glyphs live in the atlas as single sprites (`PixelNumber` draws them). The characters are `0-9 + - / x . ! : % k space`, and sprite names spell out the symbols: `font_hud_plus`, `font_dmg_slash`, `font_crit_bang`, and so on. `crit` has no space glyph.

| font | look | height | advance | used for |
|---|---|---|---|---|
| `hud` | cream with a 1 px ink drop shadow | 8 | glyph w + 1 | gold, level, HP "84/120", reroll cost, skip reward, speed "x1", result values |
| `dmg` | white with a 1 px ink outline; **tinted at runtime** with Image colour | 9 | glyph w − 1 (outlines overlap) | floating damage numbers |
| `crit` | bold, baked gold→copper gradient, outline and shadow; shown at **×2** | 11 | glyph w − 1 | critical hits |

Numbers are never set with TMP. That keeps them crisp and lets them change every frame without generating text meshes.

---

## 7. Components

### 7.1 Buttons

- Hierarchy: `Button` (Image Sliced `btn_*`, Button component, Transition = **Sprite Swap**, Pressed sprite = `btn_*_pressed`, Disabled sprite = `btn_disabled`) → `Label` (TMP) and/or `Icon` (Image) → optional `Coin` and `Cost` (PixelNumber).
- The face sits 1 px above the bottom lip: centre labels at `(h − 2) / 2`. **On press, move the label and icon down by 2 art px** (the pressed sprite drops the face by 2). A small `ButtonPressOffset` script on PointerDown and PointerUp is enough.
- Min size 24×24. Cost buttons (Reroll, Skip) put the label on the left, the coin at `w − 32` and the number right-aligned at `w − 7`.
- Reroll cost follows `5 × (n+1)(n+2) / 2`, giving 5, 15, 30, 50 for n = rerolls used this level-up. Show `btn_disabled` and a dimmed coin when gold < cost.
- Skip shows `+50` (the spec gives 50 gold for skipping the Crucible).

### 7.2 Bars

uGUI's Filled mode can't 9-slice. Instead:
- each bar is a `bar_frame` sliced Image with a **fill child**: a sliced Image of `bar_*`, anchors `(0,0)`–`(value,1)`, offsets 2 px inside the frame;
- set `fill.anchorMax.x = value` (0..1) and round the resulting width to whole units;
- hide the fill when width < 2.

| bar | rect | fill | extra |
|---|---|---|---|
| XP | 24,54,152,10 | `bar_xp` | level-up at full. Ease the fill over 0.25 s. On level-up, flash it cream for 2 frames and reset. |
| HP | 15,262,161,12 | `bar_hp` over `bar_hp_lag` | the lag bar (cream) holds the old value for 0.35 s, then shrinks over 0.25 s. The heart `ic_heart_s` sits at 4,263 and the value "84/120" is centred (`hud` font). |
| Boss | 24,76,152,10 | `bar_boss` | appears instead of the turn text when the boss spawns. The name "THE BELLWARDEN" goes above it at y = 66. |

### 7.3 Level-up card (×3)

- 160×52 `card`.
  - Icon frame: 36×36 `slot_ball` or `slot_item` at +7,+10, with the icon ×2 at +9,+12.
  - Name: Bold 14 pt at +50,+6.
  - Pips: at +50,+21, 7 px pitch.
  - Tag: 30×11 at `w − 38`, +7. Use `tag_gold` with "LV n" for an upgrade or `tag_green` with "NEW" for a new pick, and `tag_red` with "MAX" if needed.
  - Description: 11 pt at +50,+29, 102×22.
- Pips: `pip_on` for current levels, `pip_next` for the level this pick would add, `pip_off` for the rest (levels 1–3).
- Cards stack at y = 66, 120, 174, with the reroll button at 46,232. Selection is a `select` outline 2 px outside the card. Tapping a card picks it straight away; there is no confirm step. Keep the outline as the press highlight.

### 7.4 Crucible (fuser) option card (×3)

- 160×52 `card_copper`.
  - Icon well: `inset` 62×36 at +6,+10, holding the icon recipe row (16 px icons, 9 px for `ic_plus` and `ic_arrow`).
  - Title: Bold 13 pt at +74,+7.
  - Description: 10 pt at +74,+21, 80×30.
- Options:
  - FREE UPGRADES shows `+N` in `hud` at the well's bottom right;
  - COMBINE opens the Combine sub-screen;
  - EVOLVE shows the recipe, for example `ball_ember + ball_anvil → ball_powder_keg`.
- An unavailable option uses `card_disabled` and grey text, and is not interactable.
- Skip goes at 46,232 with `+50` and a coin.

### 7.5 Combine sub-screen

- Four 36×36 ball slots at x = 12 + 40i, y = 94, with icons ×2 and pips below.
- Balls below Lv 3 get a 55 % ink dim and can't be selected.
- The first tap marks HOST (`tag_gold`) and the second marks PARTNER (`tag_green`), each with a `select` outline. Tapping a selected ball unselects it.
- Preview panel at 14,158,152,70 shows `A + B → result` and a 2-line description.
- BACK (secondary) and COMBINE (primary, disabled until both are picked) go at y = 232.

### 7.6 Slot strip (always visible in battle)

- 4 ball slots at x = 4 + 21i and 4 item slots at x = 93 + 21i, y = 278, each 20×20, with ×1 icons at +2,+2.
- Frames: `slot_ball` (copper) or `slot_item` (slate) when filled, `slot_empty` when empty, `slot_fused` for a combined ball.

### 7.7 Floating damage numbers

- Pool about 40 `PixelNumber` objects under a `FX` layer in `Middle`, with raycast off.
- Spawn them at the hit point projected to the canvas: `RectTransformUtility.ScreenPointToLocalPointInRectangle`.
- Motion, at 1× game speed (scale durations by the game speed):
  - rise 10 art px over 0.6 s with ease-out;
  - hold full alpha for 0.35 s, then fade over 0.25 s;
  - add an x jitter of ±3 px;
  - snap positions to integers.
- **Crit:**
  - font `crit` at scale 2, punch-in: scale 3 → 2 over 0.08 s (integer steps only: show ×3 for 2 frames, then ×2);
  - rise 14 px, life 0.9 s;
  - a crit is always drawn above normal numbers.
- Tints for the `dmg` font, applied as Image colour:

| type | tint |
|---|---|
| normal hit | `#FFFFFF` |
| burn tick | `#D98841` |
| poison tick | `#5DA629` |
| frozen bonus | `#9FF2EE` |
| hero hurt (shown as −n) | `#FF7A7A` |
| heal (+n) | `#B8F27A` |

- Merge rule: if the same enemy takes more than 3 ticks within 0.2 s, sum them into one number, so the screen stays readable at 2× speed.

### 7.8 Boss warning banner

- Full canvas width (anchor to `UICanvas`, not `Design`):
  - stripe tile 8 px at y = 104;
  - body 40 px at y = 112 (`banner_body`);
  - stripe tile 8 px at y = 152;
  - "WARNING" Bold 24 pt `#FF7A7A` and "THE BELLWARDEN RISES" Bold 12 pt cream.
- Sequence (unscaled time):
  - stripes slide in from the left and right (tiled-image UV scroll or anchoredPosition) over 0.2 s;
  - the text blinks 3× (0.15 s on / 0.1 s off);
  - hold 1.2 s, then collapse the body height to 0 over 0.15 s;
  - **the game is paused during the banner**.
- Show it when the boss turn arrives (turn 60 per the pit-game notes) and then show the boss bar.

---

## 8. Screens

The full element lists are in LAYOUT-TABLES.md. Notes on behaviour:

1. **Title.**
   - The logo is two TMP lines: "RICOCHET" cream 34 pt and "DEPTHS" copper 46 pt.
   - The glow behind the logo is a soft copper blob at 22 % alpha. In Unity use a small pre-blurred radial sprite or skip it; it is the only non-pixel element.
   - Decorative balls drift ±1 px.
   - PLAY 100×30 primary.
   - Sound and music toggles go bottom-left; swap `ic_sound`↔`ic_mute`, and dim `ic_music` at 40 % when off.
   - Version text goes bottom-right. Nothing goes in the capsule zone.
2. **Hero pick.**
   - Back button and header go top-left.
   - The preview well holds a **RenderTexture of the 3D hero**. The emblem ×3 and the note are mockup placeholders only.
   - Name (Bold 18), role (hero colour), and the one-line rule twist.
   - Hero lines:

| hero | role | rule |
|---|---|---|
| Tinker | Bell-mender | Standard shots. A steady, simple start. |
| Wren | Lamplighter scout | Shots pierce foes until they hit a wall. |
| Brother Osk | Ferryman-monk | No minor balls. Extra copies of one ball. |
| Juno | Surveyor | Lobs every shot in a high arc. |

   - Four 38×38 `card` tiles with emblems. The selected tile gets a `select` outline and a cream name; the others are muted.
   - START RUN 120×28 primary.
3. **Battle HUD.**
   - Row 1: pause (24²), speed (24², label `x1` / `x1.5` / `x2` in `hud`; the `x1.5` string needs the `dot` glyph and fits the 24 px button), gold counter (inset 52×16, coin plus right-aligned value).
   - Row 2: level badge and XP bar, with muted turn text under the bar ("Turn 34 · Boss at 60").
   - Bottom: HP bar and slot strip.
   - Tapping speed cycles 1 → 1.5 → 2 → 1. Keep the last choice for the run.
4. **Level up.**
   - Dim 72 %, then the ribbon "LEVEL UP!", 3 cards and Reroll.
   - The top HUD and slot strip stay visible: the top HUD under the dim, the slot strip **above** the dim, so the player sees their loadout.
   - Pause and speed are not interactive while the modal is open.
5. **Crucible.** The same frame as Level up, with ribbon "THE CRUCIBLE", 3 option cards and Skip (+50).
6. **Crucible – Combine.** See §7.5.
7. **Pause.**
   - Dim 78 %, then a `panel` at 14,54,152,238 with the ribbon "PAUSED" overlapping its top edge.
   - LOADOUT grid: 4 balls and 4 items at 30×30 slots, with pips.
   - RESUME (primary), RESTART and QUIT TO TITLE (secondary), then the sound and music toggles.
   - RESTART and QUIT should ask for confirmation. A small `panel` dialog with Cancel (secondary) and Confirm (`btn_danger`) is enough; it is not drawn in the mockup.
8. **Win.** Dim 80 %, "PIT CLEARED!" in gold, a subtitle, and a stats panel with 6 rows. Then Home (icon-only secondary 60×26) and PLAY AGAIN (primary).
9. **Lose.** The same layout, with "CREW LOST" in red, the subtitle "Fell on turn N of 60." and RETRY.
   - Stat rows, each `inset` 136×20 with icon, label and right-aligned `hud` value:
     - Turns (`ic_clock`)
     - Foes broken (`ic_skull`)
     - Damage dealt (`ic_sword`)
     - Best hit (`ic_star`)
     - Gold earned (`ic_coin`)
     - Top ball (its ball icon, "Top ball: <name>")
   - Use `k` above 9 999 (for example `48.2k`).
10. **Boss warning + numbers.** This frame shows the banner (§7.8), the boss bar (§7.2) and a legend of the floating-number styles (§7.7). The legend is documentation only and is not built into the game.

Transitions: modals fade the dim in over 0.12 s and drop the panel or cards from 6 px above with ease-out over 0.15 s. Snap to integers.

---

## 9. Input and gameplay pausing

- While any modal (Level up, Crucible, Pause, Results, boss banner) is open, set `Time.timeScale = 0` or the game's own pause flag. UI tweens use unscaled time.
- Only one modal is open at a time. Queue Level up and Crucible events if they arrive together, with Level up first.
- Android back / TikTok back: close Combine → Crucible root. Otherwise open Pause, and on Pause it acts as Resume.

---

## 10. Placeholders and open points

- **Item names and icons are invented placeholders:** Bellows, Soot Lens, Thimble, Slag Magnet, Furnace Coal and Chain Link (`item_*`). pit-unity must map them to the real item list and swap the art. Item frames and slots don't need to change.
- Ball names and colours follow art-bible §6, with one exception: the ball listed there as "Brood Pod" is called **Cluster** (`ball_cluster`), a placeholder until the art side picks a new name. Don't use "Brood" in any UI text. The level-up descriptions ("Hits ignite…", "Crit chance +6%") are sample copy; take the real values from the game data.
- The hero preview is a RenderTexture slot; the emblem art is only a placeholder there.
- The TikTok capsule API name and the Unity SDK binding are unverified (§3).
- The 3D battlefield in the mockups (enemy blocks, balls, hero) is a placeholder scene, not UI.

## 11. Size budget

| asset | download |
|---|---|
| ui_atlas.png | 18 KB |
| 2 TMP font atlases (Alpha8 512²) | about 2×60 KB compressed in the bundle |

The whole UI is well under 0.5 MB of the 30 MB budget. Load the atlas and fonts from the remote bundle with the UI scene. Nothing has to be fetched while a run is in progress.
