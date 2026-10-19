# Ricochet Depths — Art Bible (art side, clean room)

Sources used: `ballpit-spec/style.md` (camera, light, value/saturation ranges, poly budgets) and `ballpit-spec/spec.md` (which enemy archetypes, footprints and ball mechanics exist). Nothing else. Names below are either invented here or taken from the implementation prototype (`pit-game/`, itself clean room) so code and art share keys. No name, silhouette or item list comes from the spec's reference tables.

Moodboard sources (unrelated to any game): Victorian bell foundries and casting pits, flooded quarries, slag heaps, miners' lamps, brass orreries, salt mines, museum archives.

---

## 1. Setting

Under the hill town of **Peal** the old bell foundry slid into a sinkhole a century ago. The furnaces never quite went out. Down there the cooled slag, soot and broken moulds have started to walk, and they climb toward the light every night. The town sends small crews of misfits down the shaft with launchers that throw enchanted shot. Each deeper level is an older, stranger layer of what the sinkhole swallowed.

Tone: cosy-grim. Chunky, toy-like silhouettes; nothing gory. Enemies read as *made things gone wrong* (slag, soot, moulds, wax, bellows), not animals or undead.

Shape language:
- **Heroes**: tall-ish (head ≈ 1/3 of height), rounded tops, one oversized prop (launcher, lantern, staff, sling). Cool hues plus one saturated signature block.
- **Enemies**: squat, wider than tall, heavy bottoms, one or two glowing points (eyes / vents). Warm-dark bodies, small amber accents.
- **Boss**: the largest single "made thing" of each layer.

---

## 2. Global value rules (from style.md §4, kept as-is)

| Layer | V (value) | S (saturation) | Notes |
|---|---|---|---|
| Scenery | 10–33 %, mostly 10–20 % | 15–45 % | theme hue only |
| Enemies | body 0–30 %, accents 55–85 % | accents 35–45 % | warm-dark + amber accent; emissive only on eyes / vents |
| Heroes | **bold: 30–90 %** (user override, 2026-10-19) | 60–95 % colour blocks | bright toy-like blocks + signature colour; deliberately above style.md's 12–40 % |
| Balls / VFX | 60–100 % | 40–80 % | the only things that bloom |

Order of brightness: scenery < enemies < heroes < balls. **Colour split (user decision):** heroes use bold, saturated colours; enemies and the boss keep the dark warm palette below, so the two always read apart. Shared enemy body ramp (all themes): `#1F1815` `#38302A` `#4C413D`; enemy accent `#D1BC7D` / `#B29562`.

---

## 3. Level themes and palettes

Each theme swaps scenery hue and one accent; value ranges stay fixed. Fog/background stays near style.md's dark blue-grey, nudged per theme.

| # | Theme | Idea | Scenery ramp (dark→light) | Accent (lights, props) |
|---|---|---|---|---|
| 1 | **The Sunken Yard** | Collapsed foundry yard: casting pits, chain hoists, cracked moulds, cooling troughs | `#141C1F` `#202A2E` `#323E42` `#324C54` (slate-teal, H195) | `#D98841` furnace copper |
| 2 | **The Salt Gallery** | Abandoned salt mine below the foundry: carved pillars, crystal crusts | `#1D171F` `#2C252E` `#3F3642` `#4E3B54` (dusk mauve, H285) | `#62D9CF` brine cyan |
| 3 | **The Root Archive** | Town records vault split by giant roots: shelving, ledgers, roots | `#1C1611` `#2B241C` `#3D342B` `#523F2D` (umber, H30) | `#90CC66` lichen lime |
| 4 | **The Drowned Orrery** | Flooded observatory chamber at the bottom: brass rings, still water | `#12131F` `#1E1F2E` `#2E3042` `#2E3154` (indigo, H235) | `#D96CA3` orchid glow |

Theme 1 local light: one copper spot light (`#D98841`, low intensity) from a "furnace mouth" off-screen top-left. Wall kit (style.md §1): 6–8 tileable wall chunks (brick lining, cast-iron ribs, chain bundle, broken mould half, trough, corner pillar) + 3 props (hanging hook, bell shard, ladle).

---

## 4. Heroes (4) — shared 1024 atlas, ≤ 1000 verts each, target ~800 tris

| Hero | Look | Signature block (S90 V12 deep / S80 V85 bright) | Cloth ramp | Rule twist (from spec §6.3 patterns) |
|---|---|---|---|---|
| **Tinker** | Young bell-mender. Round welding goggles pushed up on a knit cap, oversized canvas apron, a stubby brass launcher shaped like a hand-cranked tuning fork on the right arm, tool roll on the belt. | cobalt `#030F1F` / `#2B74D9` (scarf + launcher grip) | `#2D3238` `#525861` | Standard |
| **Wren Halloway** | Lamplighter scout. Long hooded oilskin coat, tall thin lamp-pole carried like a spear with a caged lantern at the tip, rolled-up trousers, high boots. | violet `#11031F` / `#822BD9` (lantern glass + hood lining) | `#322D38` `#5A5261` | Pierces until first wall hit |
| **Brother Osk** | Stout ferryman-monk from the flooded levels. Barrel-shaped, short cape, wide-brim rain hat, a short oar with a stone ring set in the blade, bead string across chest. | jade `#031F18` / `#2BD9AD` (sash + ring stone) | `#2D3835` `#52615D` | No minor balls; extra copies of one ball |
| **Juno Quill** | Surveyor-cartographer. Short cropped jacket with many pockets, map tube on back, brass sighting-compass monocle, Y-shaped surveyor's sling with a plumb-bob pouch. | magenta `#1F0315` / `#D92B9F` (map-tube cap + sling bands) | `#382D34` `#61525C` | Lobbed shots (arcing) |

**Bold pass (2026-10-19):** concepts were regenerated with bold blocks. Tinker = red knit cap, cobalt scarf, mustard apron, teal shirt, navy trousers, red boots, gold launcher (`concept/hero_tinker.jpg`, model `models/lowpoly/hero_tinker.glb`). Bold Wren / Osk / Juno concepts are in `concept/v2/` but not yet converted; their current models are the old dark v1 and are parked. The hex ramps in the table above are the v1 values; the signature hue per hero still holds.

Hero rules: skin tones in V 35–50 %; no pure black; the signature colour must cover ≥ 10 % of the silhouette's front so heroes read against warm enemies.

---

## 5. Theme 1 enemies — 512 atlas + 512 emissive, ≤ 400 verts, target 200–400 tris

Keys match the prototype's enemy names where one exists.

| Key | Archetype (spec §3.3) | Footprint | Look | Emissive |
|---|---|---|---|---|
| **Shambler** | basic | 1×1 | A walking lump of cooled slag the shape of an upturned pot, two stubby legs, crust plates on top. | two amber eye slits |
| **Mite** | mini | 0.5×0.5 | Ash flea: a coal pebble with six needle legs. | one pin-prick eye |
| **Brute** | tough | 1×1 | Shambler wearing a riveted cast-iron collar plate on its front, thicker legs. | eye slit behind plate gap |
| **Slab** | wide/long | 2×1 | A broken half-mould lying sideways, crawling on four short pegs; casting channels on its back. | molten seam along the channels |
| **Spitter** | shooter | 1×1 | Squat chimney-pot creature, flue for a head, spits tar blobs. | glowing flue mouth |
| **Lobber** | thrower | 2×1 | Wheel-less crucible cart with two arms and a ladle; flings cinders. | crucible rim glow |
| **Mender** | healer | 1×1 | A melted candle-stub creature with a wick on its head and wax drips for arms; drips heal neighbours. | wick flame (small, amber) |
| **Broodpot** | spawner | 1×1 | Lidded clay cooking pot on three legs; lid lifts to drop Mites. | glow under the lid |

**Boss — The Bellwarden** (7×7, ≤ 1500 verts, target ~1500 tris): the foundry's last unfinished great bell, cracked down one side, walking on four chain-link legs gathered under its lip like a skirt. A bronze yoke on top bears a soot-black hoist hook. Through the crack you see the molten core; the **clapper** hanging inside is the weak point (glows when exposed). Bronze body `#3A2E22`→`#6B5640` (darker than heroes, inside enemy range), amber accent `#D1BC7D` on rim inscriptions, emissive core `#FF9E4A` (only on clapper and crack).

Death: enemies crack into 4–6 flat-shaded shards (style.md §6), plus ash puff in theme accent.

---

## 6. Ball elements (12) — 16×16 pixel sprites + 64×2 gradient ramps

Each ramp: very dark → mid → near white (bloom). Sprite = 16×16, point-sampled, 1 px dark outline from the ramp's dark stop, highlight pixel top-left.

| Key | Mechanic it represents | Sprite look | Ramp (dark → mid → bright) | Trail |
|---|---|---|---|---|
| **Ember** | burn | coal with a crack of fire | `#380E06` → `#BF581D` → `#FFE699` | rising sparks |
| **Rime** | freeze | faceted frost pebble | `#0A2533` → `#47AAB2` → `#D9FFF9` | thin frost streak |
| **Spark** | chain lightning | tiny brass bead with a spark arc | `#221638` → `#708FCC` → `#FFFBCC` | zig-zag segments |
| **Venom** | poison | dripping seed pod | `#0D2608` → `#5DA629` → `#E9FFA6` | drip dots |
| **Anvil** | heavy / pierce | flat iron rivet head | `#15181F` → `#77828C` → `#F2DEB6` | none, motion smear |
| **Thorn** | bleed | burr ball with spikes | `#2E070D` → `#A62949` → `#FFCCB2` | short barbs |
| **Wisp** | ignores shields | hollow ring with a soft core | `#1F3333` → `#8FBFAF` → `#EBFFF1` | long ghost tail, 50 % alpha |
| **Ray** | row/column beam | lens disc with cross glint | `#403813` → `#E6CA5C` → `#FFFDE6` | straight line |
| **Cluster** | splits into minis | three fused beads, one larger | `#2E122E` → `#B2599C` → `#FFD9EC` | 3 dotted strands |
| **Lodestone** | pull / area | magnet horseshoe stone | `#0F0F33` → `#8659B2` → `#D9F2FF` | inward ripple ring |
| **Gale** | push / knockback | spiral shell | `#172E28` → `#6BB28F` → `#ECFFD9` | curling wisps |
| **Sap** | life steal | amber droplet with a dark heart | `#29081E` → `#A63232` → `#FFDFBF` | droplet beads |

Evolved balls reuse the two parents' ramps (split sprite, ramp blended 50/50) until bespoke art exists.

---

## 7. Image-to-3D concept images

One subject per image, A-pose (heroes) or neutral stance (enemies), three-quarter front view, plain mid-grey background `#808080`, even studio light, **low-poly flat-shaded 3D render** look with large flat faces, no outline, no text, no ground clutter. Saved in `pit-art/concept/` as `hero_<key>.png`, `enemy_<key>.png`, `boss_<key>.png`.

Generated with Figma AI `generate_image` (user's credits) in a new Figma file "Pit Art – Original Concepts" (not the withdrawn concept file).

---

## 8. Free image-to-3D path and licences

| Tool | How we run it | Licence (check the official text before shipping) | Use |
|---|---|---|---|
| **Hunyuan3D-2 / 2.1** (Tencent) | HF Space `tencent/Hunyuan3D-2` / `tencent/Hunyuan3D-2.1`, in the browser or via `gradio_client`; ZeroGPU free quota, larger if the user signs in to HF themselves | **Tencent Hunyuan 3D 2.0/2.1 Community License**: territory **excludes the EU, UK and South Korea** (and outputs may not be used/distributed there); products with > 1 M MAU need a separate licence from Tencent; acceptable-use policy applies. A TikTok game distributed in the EU/UK is a problem → **avoid for shipped assets unless the game is geo-limited**. | reference / fallback only |
| **TRELLIS / TRELLIS.2** (Microsoft) | HF Space `trellis-community/TRELLIS` (`microsoft/TRELLIS` now redirects there) / `microsoft/TRELLIS.2`, browser or `gradio_client` with the user's own HF login | **MIT** (code + weights). Checked 2026-10-19: the community Space is tagged MIT and its `app.py` loads `JeffreyXiang/TRELLIS-image-large`, which redirects to `microsoft/TRELLIS-image-large` (MIT, Microsoft-owned), i.e. the official weights. The Space code is community-maintained; it runs non-MIT helpers server-side (e.g. nvdiffrast for rendering), which don't attach terms to the GLBs we download. TRELLIS.2 conditions on DINOv3 features; the DINOv3 licence governs that model, outputs are not restricted by MIT. | **preferred** |
| **TripoSR** (Stability AI + Tripo) | local, CPU/MPS on the M1 (≈ 1 GB weights) | **MIT** | small enemies, offline fallback |
| **Blender** | local, headless (`tools/decimate.py`) | GPL for Blender itself; your output files are yours | decimation, flat shading, atlas bake |
| **Mixamo** (Adobe) | browser, user logs in | Free with an Adobe ID; rigged characters and animations may be used royalty-free in commercial games; you may not redistribute raw Mixamo animations/characters as standalone assets | rigging heroes |

## 9. Pipeline

1. Concept PNG (`concept/`) → image-to-3D (TRELLIS preferred) → `models/raw/<key>.glb`.
2. `blender -b -P tools/decimate.py -- --in raw.glb --out models/lowpoly/<key>.glb --tris 800 --tex 256` (heroes 800, enemies 200–400, boss 1500; `--tex` 256 for enemies into a 512 atlas, 512 for heroes into a 1024 atlas). TripoSR output needs `--up=-y --front=+x`.
3. The script: joins meshes, drops floating specks, reorients, rescales (`--height`), decimates to the target, re-unwraps, bakes the base colour to a small texture, posterises (`--quantize N`, two clusters reserved for saturated accents) and remaps value into the layer's range (`--vrange` body, `--accent-vrange` accents). Example for an enemy: `--quantize 10 --vrange 0.04,0.30 --accent-vrange 0.55,0.85`.
   - **Vertex budget vs flat shading**: hard flat normals make Unity count ~3 verts per triangle (300-tri Shambler = ~900 verts; an 800-tri hero = ~2400, over style.md's 1000). Export with `--normals smooth` (300 tris = ~300 verts) and get the faceted look in the URP shader from screen-space derivatives instead. Or halve the triangle targets if hard normals are wanted.
   - Small emissive details (eyes, vents) come out tiny and dull from image-to-3D at these budgets; paint them into the 512 emissive map by hand.
   - **Enemies are hand-built** (2026-10-19): `tools/monsters/<key>.py` on top of `tools/monster_lib.py`, flat-shaded (Unity verts = face corners ≤ 400). All theme-1 enemies share one 512 albedo atlas + one 512 emissive map (`models/lowpoly/enemy_theme1_*.png`), which matches §5. Image-to-3D is kept for heroes.
4. Heroes → Mixamo (below) → FBX into Unity. Enemies → Unity directly; idle/walk/attack/death as baked vertex animation at 8–12 fps (style.md §6).
5. Atlas packing: in Unity or Blender, place each 256/512 texture into the theme atlas and offset UVs.

### Mixamo rigging (user does the login)

1. Export the decimated hero as **FBX** (`--out hero.fbx`), A-pose, facing +Y in Blender (= forward in Mixamo), ≤ 1000 verts, single mesh, texture embedded.
2. Go to mixamo.com → sign in with your Adobe ID (yourself) → **Upload Character** → drop the FBX.
3. Place markers: chin, wrists, elbows, knees, groin. Skeleton LOD "Standard (65)"; for low-poly heroes "No fingers" is cleaner.
4. Pick animations: an idle, a strafe/walk, a "throw"/"shoot" pose. Tick **In Place**.
5. Download: Format **FBX for Unity**, Skin **With Skin** for the first clip, **Without Skin** for the rest, 30 fps, keyframe reduction none.
6. In Unity: Rig → Humanoid, then set clip import sample rate / use a stepped-curve script to get the 8–12 fps "pixel" motion.
