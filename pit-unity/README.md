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
- **No meta.** m = 1. Rerolls cost gold (or one rewarded ad, see Ads). There are 3 level-up options, 4 ball slots and 4 item slots.
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
- **Hero size.** The hero model is drawn 1.8 cells tall at 7×9 (scaled by height; the hit circle is unchanged) and keeps that on-screen size as the board grows: its scale is multiplied by the camera distance over the 7×9 fit (×1.61 at 13×15). It stands 0.35 cells (scaled the same way) back toward the camera from its true position. Under it: a cobalt ground ring 1.3 cells across (Tinker's cloak; other heroes use their colour) on the true position and a soft drop shadow, drawn just above the floor slabs. The aim line and launch point are drawn on the HUD layer over the 3D view, so the model never hides them. `PitCapture` logs the hero's drawn size.
- **Foe animation.** Shambler, Brute and Spitter are pit-art's rigged models (aa3955e, `monster_lib.py`: 6 bones, rigid skin, one shared `enemy_theme1` atlas with an emissive map, which `PitUnit` uses as the glow with `_TexEmit = 2`). `Tools/bake_hero_anim.py … 12` bakes their Idle/Move/Attack/Hit/Death at the keyed 12 fps (about 0.6 MB raw for the three). Board3D plays Attack on a melee wind-up, or 1/3 s before a shot so the projectile leaves on the release frame; Hit on damage; Move while stepping; Idle otherwise; Death plays through instead of the shrink. These three skip the code-side breath, hop and crouch. Shots still start from the foe's centre, not the flue mouth. `PitCapture -pitScreen clips -pitHero enemy_spitter` renders a sheet.
- **Hit feedback (unit shader).** White flash, ice overlay, and bleed noise seeded per unit.
- **Hero animation (layered).** The layered Tinker comes from pit-art `tools/rig_hero_layered.py` (ANIM.md, method A). `Tools/bake_hero_rig.py` bakes it into two files:
  - `hero_tinker.bytes`: the bind-pose mesh, 993 vertices.
  - `hero_tinker_rig.bytes`: 21 bones and 16 per-bone clips at 10 fps, 68 KB raw. This replaces the 580 KB per-frame `hero_tinker_anim.bytes`.

  `Game/HeroRig.cs` slerps the layers bone by bone, runs FK and skins the mesh on the CPU on each 10 fps step. Board3D drives the layers:
  - **Body yaw.** It follows the aim within ±35° and re-centres at 180°/s.
  - **Upper body.** It covers the rest of the aim with AimIdle or AimWalk `_L/_F/_R`. Shoot restarts from frame 0 on each launch (`Run.Launches`), but not again within 0.1 s.
  - **Lower body.** The move is taken into body space and blends the two nearest of WalkForward, StrafeRight, WalkBack and StrafeLeft on one walk clock at 1.25×. Stand plays when not moving.
  - **Full body.** Death and Victory take over the whole body.
  - **Hit.** There is no Hit clip; it is a white flash plus a 0.25-cell knockback.

  The hero-pick preview plays AimIdle_F over Stand. `PitCapture -pitScreen clips` renders the layer combinations and an aim sweep. StubCheck checks every clip and skins one mixed pose.
- **Lighting.** Per LIGHTING.md: a white key light tilted 10° (shadow strength 0.8, distance 40), two blue-grey wall fills and a copper furnace spot (per-pixel additional lights, no shadows), a trilight ambient, and pit-axis fog in the background colour (38, 41, 55). Built-in fog is off: `PitUnit` and `PitBeam` fog by distance along the pit past the far-wall line (0.45 one cell past it, 0.8 nine units on, 1 at the treadmill end), from the `_PitLineY` / `_PitFarZ` globals that follow the board through an expansion. Units take the same fog, so waves above the top row come out of the distance; their own glow (eyes, markings) takes only half, the window panes all of it. The light pools lose alpha with it.
- **Pixel art.** Balls, items, gems, coins, shots and VFX are 16×16 sprites in a 256² point-filtered atlas, baked at startup.
  - Each element has a 64-step ramp from very dark to near white.
  - Numbers on the board use a 3×5 pixel font.
- **Bloom.** Balls, shots, beams and particles sit on the `Glow` layer, drawn ×2.4 in HDR. Bloom's threshold (1.05) is reached only by that layer and emissive eyes.
- **Ball VFX (pit-vfx).** Ember, Rime (`frost`), Spark and Ray use `Assets/RicochetVfx`, imported unchanged from `pit-vfx/RicochetVfx.unitypackage` (565a1f3; it needs the Particle System module, now in the manifest). `Board3D.Vfx.cs` makes the calls from what the core reports:
  - a proxy transform per field ball for `VfxBank.Ball`, and `StopBall` when the ball leaves the field;
  - `Run.BallHits` for `Hit` plus `Flash`;
  - the `burn` and `freeze` statuses for `Status`, only when one of these balls put them on (a Cinderquake or Hailstorm status keeps the plain look); a frozen foe's baked clip holds still under the ice shell; statuses end when the foe dies and `ClearStatus` runs when its view is pooled;
  - the core's arc beams for `Chain`, flashing each target 0.07 s apart, and its row beam for `RowBeam` at the inner wall faces, flashing the row 0.14 s later;
  - `Beam.Src`, `Target` and `Targets` tag what made each beam.

  WorldView skips those balls' sprites, trails and beams. The other 22 balls are unchanged. `PitUnit` includes `RicochetHitFx.hlsl` from the package for `_Flash`, `_HitTint`, `_Ice`, `_Burn` and `_Shock`, replacing its own flash and ice. The hero and other freezes use the same code, so their ice now has a faint rim glow. `PitCapture -pitBall <id>` gives the run only that ball, ticks the VFX clock by hand and waits for the effect before the shot. WebGL build: 7.22 → 7.50 MiB (+247 KiB wasm for the Particle System module, +35 KiB data).
- **UI.** The UI stays on its own orthographic camera, stacked on top as a URP overlay.

## Ads (from `../ballpit-spec/ads.md`)

- **Service.** The game talks only to `IAdService` (`Game/Ads.cs`); `AdConfig` holds every cap and interval. `PlaceholderAds` is the current implementation: a black full-screen "AD" panel with the placement and a 2 s countdown. Rewarded ads have Skip during the countdown (not watched) and × after it (watched); interstitials close themselves. A TikTok implementation replaces it later.
- **Rewarded.** Each reward is given only when the ad was watched to the end; closing it early leaves the button there. Buttons carry `ic_play` as the ad mark (pit-ui has no AD icon yet), hide at their cap, and grey out with "Ad not available" when no ad loads.
  - **R1 revive.** At 0 HP the run enters `Mode.Revive`: Watch ad · Revive or Give up. A watched ad restores 50% HP and clears the 2 occupied rows nearest the player (no rewards). Once per run.
  - **R2 free reroll.** "FREE" next to the paid reroll on level-up, 3 per run.
  - **R3 +1 upgrade.** A "+1" ad button on the Crucible's Free Upgrades card, once per Crucible: a watched ad adds one piece to that Crucible's count (capped at 5, drawn by the same rules); the card then reads "FREE UPGRADES +1, level up 2-5". The player still chooses freely. Hidden when nothing can level (the gold fallback).
- **Interstitial.** Only on the results screen's Retry or Home: never after the first run, never in the first 180 s, at least 180 s apart, and not in a run where a rewarded ad was watched.
- **While an ad shows** the run does not step, input goes only to the ad, and `AudioListener.pause` is set.
- **Testing.** F8 (Editor and development builds) makes every load fail. The dev bot always gives up. `PitCapture -pitScreen revive|ad|crucibleplus` renders the prompt, the placeholder and the Crucible after the +1 ad.
- **Not done:** gold doubling (ads.md leaves it out), and the real TikTok ad calls.

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

The HUD speed button cycles x1, x1.5, x2, x3 and x4. Under that, the whole game runs at `GameRoot.GameSpeed` (1.44), and the map comes down at `GameRoot.Descent` (1.25). Descent scales the scroll, foe speed and wave timer. The sims keep 1, so balance sweeps don't change.

**Dev panel.** Open it with the DEV button at the top of the HUD, or with F1 / backquote. It pauses the game. It is left out of `-pitTikTok` builds unless `PIT_DEV` is defined.
- **RUN tab:**
  - level +1 / +5 / +10 / +25 (picks are automatic, with a toggle to pick by hand);
  - full HP, god mode and gold;
  - a jump to each boss, plus kill boss and clear foes;
  - live **GAME SPEED** and **MAP DESCENT** steppers, the bot and the speed setting.
- **BALLS / ITEMS tabs:** add, level and remove balls and items.
- The hooks are in `Core/Run.Dev.cs`. StubCheck prints `dev: bosses 3/3 ...`.

## Fast builds

`-executeMethod Pit.EditorTools.PitBuild.BuildCli -pitFast` writes `Builds/WebGLFast`. It turns compression off, which saves the roughly 65 s of brotli, and asks for IL2CPP Debug / BuildTimes. The log still names `release_WebGL_wasm` and the wasm is the same size, so that second part may not take effect. Tune the pace with the dev panel steppers, not with rebuilds.
