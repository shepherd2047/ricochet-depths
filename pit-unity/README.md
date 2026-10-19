# Ricochet Depths (Unity)

A portrait bouncing-ball roguelite demo for TikTok, written in Unity C#. This is a clean-room build: it was made from `../ballpit-spec/spec.md` alone. All names, art and copy are original. `../pit-game/` is the earlier plain-JS slice, kept as a reference.

## Layout

| Path | What |
|---|---|
| `Assets/Scripts/Core/` | The whole game simulation. Pure C# (asmdef `noEngineReferences`, C# 9), no UnityEngine. |
| `Assets/Scripts/Game/` | The Unity view layer: 3D board (`Board3D`, `MeshKit`), pixel sprites (`PixelArt`), shape batcher (`Gfx`), TextMesh labels, input/UI, world overlay, HUD, menus, `IPlatform`. |
| `Assets/Scripts/Editor/PitBuild.cs` | **Pit** menu: create the boot scene, apply the WebGL size settings, build. |
| `Assets/Scripts/Editor/PitUrp.cs` | **Pit/Setup URP**: creates and assigns the URP pipeline asset. |
| `Assets/Resources/PitShape.shader` | Unlit vertex colour × pixel atlas, for every 2D shape and sprite. |
| `Assets/Resources/PitUnit.shader` | URP lit shader for the low-poly units and scenery. |
| `Tools/SimHarness/` | Headless dotnet runner: plays full runs with the dev bot. |
| `Tools/StubCheck/` | Compiles `Game/` against a hand-written UnityEngine stub, then pushes bot runs through the real draw code. |
| `TikTok/README.md` | TikTok packaging plan, from TikTok's official Unity docs. |

The game builds itself at runtime (`GameRoot.Boot`, `[RuntimeInitializeOnLoadMethod]`), so the scene can be empty.

## Demo content

- **Run flow.** Title → hero select → waves → Fusers on turns 15/30/45/75/90/105/140/160 → **Bellwarden** at turn 60 → **Hivekeeper** at turn 120 → **Mawking**, the final boss (about 15 min). After that come victory or game over, then Play again or Change hero.
- **No meta.** m = 1 and there's no revive. Rerolls cost gold. There are 3 level-up options, 4 ball slots and 4 item slots.
- **Balls.** 14 base balls and 12 evolutions, with burn, frostfire, poison, bleed, freeze, slow, blind, charm, leech, chain, row and column beams, quake, bomb, nuke, flash, aura, egg and split. Recall, catch and side balls follow the spec.
- **Foes.** 22 types, covering:
  - stacking riders;
  - melee wind-up and lunge;
  - shooters (arrow, icicle root, fireball, venom);
  - mortar and laser telegraphs;
  - healer, hatcher, flyer, burrower, side-stepper;
  - front and side shields, spikes, haste aura, a ball-swallower;
  - elites.
- **Heroes.** 6 heroes, each with its own six-stat spread (diminishing curves), starting ball and rule twist: Ricochet, Scatter, Pierce-to-wall, Paddle swat, Fan volley, or none.

Demo tuning that is mine rather than the spec's:

- `XpMult = 3`;
- wave density `0.17 + 0.18·min(1, turn/100)`;
- +50% HP after each mid boss;
- softer Bellwarden bolt fans.

## Look (from `../ballpit-spec/style.md`)

The simulation is still 2D. The look follows style.md:

- **Camera.** The board is shown in 3D through a perspective camera that looks down 40° from behind and above the player (style.md §2 as ruled): 65° vertical FOV, near 0.3, far 1000.
  - The fit keeps the near row plus 1.5 cells each side (the prop lane and half a cell of wall) inside the screen width, and the board between the HUD bands.
  - That gives 19.1 kit units for 7×9 and 30.7 for 13×15, about 4% further than LIGHTING.md's width-only 18.39 / 29.57, because the HUD bands bind.
  - The camera follows the board size every frame, so it pulls back over the 3 s expansion.
  - Touches are raycast onto the board plane (`Board3D.ScreenToBoard`) for taps and the joystick.
- **Scenery: a treadmill corridor** (spec §1.3a; pit-scene `ASSEMBLY.md`, `LIGHTING.md`). The player and camera stay put while the floor, walls and pickups scroll toward the player at 1 cell per turn.
  - The corridor is pit-scene's T1 kit (`Resources/Scene`, converted by `Tools/glb_to_bytes.py`; one 1024² atlas and an emission map for the window panes): 9 recycled 4-row groups.
  - The walls stand one prop lane (1 cell) outside the board edge; the lane pieces (troughs, moulds, bells) share the wall's transform, and their kerb marks the collision line. Floor strips are W{cols+2}.
  - The three window segments have a second material, `PitBeam.shader`: additive light pools in vertex alpha.
  - Wall segments come from the start, mid or end set by run progress, left ≠ right, no repeat on one side; floor strips have A/B variants per board width.
  - The scroll freezes with the battle clock, stops when the final boss arrives (the casting-pit gate docks then), and decays on win or loss.
  - The top wall is the far wall at the farthest floor line on screen (spec §4.4).
- **Units.** pit-art's delivered textured models where they exist (`Resources/Models`); the rest are original low-poly meshes built in code (`MeshKit`), flat-shaded and coloured by vertex.
- **Colour.** Foes are dark and desaturated, and only their eyes and boss weak spots glow; small yellow accents. Heroes are cool and dark, with one saturated hood in their colour.
- **Animation.** Stepped at 10 fps: idle breath, hop, wind-up crouch, flyer bob, and a shrink-and-flash death.
- **Hit feedback (unit shader).** White flash, ice overlay, and bleed noise seeded per unit.
- **Lighting.** Per LIGHTING.md: a white key light tilted 10° (shadow strength 0.8, distance 40), two blue-grey wall fills and a copper furnace spot (per-pixel additional lights, no shadows), a trilight ambient, and linear fog from camera distance +2 to +14 cells in the background colour (38, 41, 55).
- **Pixel art.** Balls, items, gems, coins, shots and VFX are 16×16 sprites in a 256² point-filtered atlas, baked at startup.
  - Each element has a 64-step ramp from very dark to near white.
  - Numbers on the board use a 3×5 pixel font.
- **Bloom.** Balls, shots, beams and particles sit on the `Glow` layer, drawn ×2.4 in HDR. Bloom's threshold (1.05) is reached only by that layer and emissive eyes.
- **UI.** The UI stays on its own orthographic camera, stacked on top as a URP overlay.

## Sound (from `../pit-audio/DELIVERY.md`, batch 1)

- **Clips.** The 24 SFX files and the battle loop are copied into `Assets/Resources/Audio/sfx` and `Assets/Resources/Audio/music` (0.9 MB). `Editor/PitAudioImport.cs` applies the import settings on import.
- **Cues.** The sim raises cues in `Run.Sounds` (`Sfx`): launch, wall, hit/crit, catch (including swat), death, hurt, level-up, XP gem.
- **Playback.** `Game/AudioBank.cs` drains the queue each frame and applies the delivery's rules:
  - random variation and ±3 % pitch (±1.5 % for level-up);
  - voice caps (wall 4, hit 6, death 3), the 30 ms wall gate, crit instead of hit;
  - deaths limited to 1 per 80 ms during the win sweep;
  - the XP chime rises a semitone per gem within 0.4 s, capped at +12.
- **Music and UI.** The battle loop plays at −6 dB (volume 0.5). Every UI widget click plays `ui_click`.
- **Autoplay and background.** Nothing plays before the first tap. Backgrounding the app sets `AudioListener.pause`.
- **Silent for now.** Events with no key yet, such as elements, enemy attacks, bosses, the Fuser and gold.

Model budgets are checked by the smoke test:

| Model | Budget | Actual max (vertices) |
|---|---|---|
| Foe | 400 | under 400 |
| Elite | 1500 | 618 |
| Boss | 1500 | 534 |
| Hero | 1000 | 534 |
| Wall module | 2000 | about 110 |

Setup in the Editor:

1. Let the package manager resolve `com.unity.render-pipelines.universal` (14.0.11 is pinned in `Packages/manifest.json`; take whatever 2022.3 offers if it complains).
2. Run **Pit/Setup URP**.
3. Run **Pit/Apply WebGL Size Settings**.

The asmdefs define `PIT_URP` when the package is present. Without it, the code falls back to the built-in pipeline with no bloom, and `PitUnit` will not compile.

## Verification

- `cd Tools/SimHarness && dotnet run -c Release -- 10 all 25`: 60 bot runs. Last result: 5 wins, 13 reached the final boss, 0 exceptions. The bot is weaker than a human.
- `cd Tools/StubCheck && dotnet run -c Release -- 2`: about 72k frames of title, hero select, HUD, world and every menu view, over 12 full runs including a win. Odd seeds go through `Board3D`, even seeds through the flat 2D path.
  - Result: 0 exceptions, all meshes valid, all model budgets met.
  - Add `-p:Urp=false` to the build to check the built-in-pipeline path.
- `dotnet run -c Release -- atlas out.ppm` dumps the pixel atlas.
- `dotnet run -c Release -- frame out.ppm 4` plays a bot run to turn 4 and software-renders that frame, plus one after the first expansion (`out-t4.ppm`, `out-wide.ppm`). It covers the tilted camera, models, fog, the overlay and the HUD shapes, with no TextMesh, shadows or bloom. It also prints the camera distance and checks the screen↔board ray round trip.
- `dotnet run -c Release -- audio` plays a bot run through `AudioBank` with the real clip list. It checks that nothing plays before unlock, that every main key fires and that the queue drains.
- `dotnet run -c Release -- models out.ppm 40` software-renders every model with the key light and trilight, tilted 40°. This checks winding and colours, not URP.
- **Not verified:** anything inside the real Unity Editor or player, including:
  - URP setup and the `PitUnit` shader (HLSL, shadow and fog keywords, `UsePass`);
  - camera stacking and bloom;
  - audio import settings, WebGL audio unlock and the mix;
  - rendering, TextMesh sizing and touch;
  - the build settings API and build size with URP;
  - anything TikTok.

  No Editor has been installed yet.

## Controls

Drag anywhere to move; the joystick floats under your finger. Balls fire on their own, and a tap focuses a foe. In the Editor: WASD or arrows to move, P/Esc to pause, **B** to toggle the dev autopilot. The HUD has a 1x/2x speed button.
