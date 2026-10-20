# Tinker: layered motion set (style.md §6.1)

Built from ballpit-spec/style.md §6.1 only. Every pose is scripted keyframes in Blender 5.2 (free). No Mixamo and no reference-game material.

**Files**
- `models/lowpoly/hero_tinker_layered.glb` (354 KB) and `.fbx` (2.5 MB, texture embedded). Both hold the skinned mesh, 22 bones (21 deforming + `Muzzle`) and 16 clips.
- `models/lowpoly/hero_tinker_layered_sheet_game.png` and `_sheet_front.png`: contact sheets with every key of every clip, in the game view and a 3/4 front view.
- `models/lowpoly/hero_tinker_layered_walkback_shoot.gif` and `_strafeleft_aimright.gif`: two layer-combination demos.
- `tools/rig_hero_layered.py` builds everything; `tools/hero_layered_sheet.py` assembles the sheets and GIFs. The rebuild command is at the end of this file.
- The source mesh `models/lowpoly/hero_tinker.glb` (A-pose) is unchanged. The old single-layer rig `models/rigged/hero_tinker.*` (Idle/Walk/Run/Shoot/Hit/Death/Victory) is also left as it is, because pit-unity still reads it. This file replaces it.

## Model and bind pose

- The **bind/rest pose is the launcher stance**: the launcher rests on the right shoulder, held out front and pitched 30° up so the barrel clears the cap from the 40°-behind camera, right hand on the pistol grip and left hand under the fore-grip. The torso is bladed 35° to the right (left shoulder forward) and the head is turned back to face front. Hips and legs face straight ahead. There is no A-pose or T-pose anywhere in the file.
- **Why the shoulder:** the board camera sits behind and above the hero, and Tinker's big cap hides anything held in front of the chest. On the shoulder, the launcher shows beside the head in every aim (see the game-view sheet).
- **Launcher:** a new 62-vertex prop that replaces the old 27-vertex tuning fork. It's a gold hex barrel with a bell muzzle and two tuning-fork prongs, plus cobalt grips. Its UVs sit on solid gold, brass and cobalt texels of the **existing albedo**, so the texture is untouched. It is skinned 100 % to `RightHand`.
- **Cap:** the head (cap included) is scaled ×0.86 about the neck, then the whole model is rescaled ×1.051 back to 0.994 m. The cap no longer swallows the launcher and feet from behind.
- **Barrel:** 20 % longer than the first version (BL = 1.2).
- **`Muzzle` bone:** a non-deforming bone, child of `RightHand`, never keyed. Its head sits at the bell mouth and its Y axis runs along the barrel. Rest position in Blender coordinates is (−0.161, −0.293, 0.732): 0.16 m right, 0.29 m forward, 0.73 m up. The barrel axis is (0, −0.866, 0.5), i.e. 30° up. Read its world transform to get the ball spawn point and direction.
- **Size:** 389 vertices / 700 triangles, about 968 Unity vertices (under 1000). Height 0.994 m, feet at the origin, facing −Y in Blender (+Z in Unity). Smooth normals, as on the other heroes.
- **Bones:** 21 deforming, with Unity Humanoid names: `Hips` (root), `Spine`, `Chest`, `Neck`, `Head`, `Left/RightShoulder`, `…UpperArm`, `…LowerArm`, `…Hand`, `…UpperLeg`, `…LowerLeg`, `…Foot`, `…Toes`. There is also a 22nd, `Muzzle` (no weights). A per-bone baker puts it in the upper layer, where it always stays at identity.
- **Import as Generic, not Humanoid.** The bind pose isn't a T-pose, so Humanoid retargeting would re-solve and distort the keyed poses.

## Bone mask

| Layer | Bones |
|---|---|
| **Lower** (walk) | `Hips` (translation + rotation), `LeftUpperLeg`, `LeftLowerLeg`, `LeftFoot`, `LeftToes`, `RightUpperLeg`, `RightLowerLeg`, `RightFoot`, `RightToes` |
| **Upper** (aim) | `Spine`, `Chest`, `Neck`, `Head`, `LeftShoulder`, `LeftUpperArm`, `LeftLowerArm`, `LeftHand`, `RightShoulder`, `RightUpperArm`, `RightLowerArm`, `RightHand` (launcher) |
| **Full body** | all 21 |

`Hips` is the parent of `Spine`, so the lower layer's hip bob and sway carry the upper body. That's intended. Hip yaw is always 0, so the walk never turns the aim.

Each action **keys only its own layer's bones**. In the GLB and FBX, the other bones are written as constant rest-pose channels. Always apply the mask; never play an upper clip on the lower bones or the other way round.

## Clips

The timeline is 30 fps with one key every 3 frames (0.1 s) and linear interpolation. Sampling at 10 fps lands exactly on the keys. Sampling at 12 fps shows linear in-betweens, which is fine. Loops store their end key equal to the start key: sample `round(length × 10)` frames and drop the end frame. One-shots include the end frame.

| Clip | Layer | Length | Loop | Blend position | What it does |
|---|---|---|---|---|---|
| `AimIdle_L` | upper | 0.9 s | yes | aim = −1 (35° left) | breathing: chest rises, shoulders lift 8 mm, muzzle dips 2° |
| `AimIdle_F` | upper | 0.9 s | yes | aim = 0 | same |
| `AimIdle_R` | upper | 0.9 s | yes | aim = +1 (35° right) | same |
| `AimWalk_L` | upper | 0.8 s | yes | aim = −1 | leans in 5°, two bobs per cycle (down on frames 0 and 4, lagging the hips), chest sways toward the stance foot, launcher bobs 3.5° |
| `AimWalk_F` | upper | 0.8 s | yes | aim = 0 | same |
| `AimWalk_R` | upper | 0.8 s | yes | aim = +1 | same |
| `Shoot_L` | upper | 0.2 s | no | aim = −1 | frame 0 **squash**: hunch forward (chest +8°, spine +4°), muzzle dips 8°, shoulders drop 1 cm, cap ducks 10°. Frame 1 **big kick**: spine and chest snap back 30° in total, arms another 30° up, shoulders shrug 1.4 cm, cap bobs back 14°. Frame 2 is the aim pose. Measured on Shoot_F: the muzzle travels 0.22 m back toward the camera (0.26 m from squash to kick) and the cap 7 cm |
| `Shoot_F` | upper | 0.2 s | no | aim = 0 | same |
| `Shoot_R` | upper | 0.2 s | no | aim = +1 | same |
| `Stand` | lower | 0.9 s | yes | move (0, 0) | feet planted, knees soft (hips 2 cm down), each foot 3.5 cm wider than the bind pose so it shows outside the cap, hips dip 8 mm |
| `WalkForward` | lower | 0.8 s | yes | move (0, +1) | 2 steps, 0.26 m per step, foot lift 9 cm, stance widened 3.5 cm per foot, hips bob 1.8 cm and sway 1.2 cm |
| `WalkBack` | lower | 0.8 s | yes | move (0, −1) | backward steps (toe lands first) |
| `StrafeLeft` | lower | 0.8 s | yes | move (−1, 0) | side-steps toward the character's left, feet never cross (stance widened 3.5 + 2.5 cm per foot) |
| `StrafeRight` | lower | 0.8 s | yes | move (+1, 0) | mirror |
| `Death` | full | 0.8 s | no, **hold last frame** | — | kick back, knees buckle, topples backward (toward the camera), lands flat with a small bounce on frame 7; the left hand lets go and the launcher is flung up and out |
| `Victory` | full | 0.9 s | yes | — | one hop per loop (crouch, launch, airborne frames 2–5, land, crouch), launcher pumped skyward in both hands (30° up at the crouch, 75° up at the top of the hop), chest open, head up |

- **Aim direction:** "Left" means the character's own left. In Blender that's +Z yaw, toward +X. The 35° is split Spine 15° / Chest 20°, with the head adding 8°.
- **Move vectors** are in **body space**: y = the way the body faces (the aim direction), x = the character's right. Unity's 2D blend-tree convention matches that.
- **Walk length:** the spec says about 0.9 s for AimWalk. It's 0.8 s here because a 9-key cycle can't split into two equal steps. An 8-key cycle gives four keys per step: contact, down, passing, up. All four walks and AimWalk share that 0.8 s and that phase.
- **Phase:** frame 0 is left-foot contact (left foot at its forward-most point for WalkForward), and AimWalk bobs on frames 0 and 4. Drive every walk and AimWalk from **one walk clock**. Changing direction then never pops the feet, and the bob stays on the step.
- **Hit has no clip** (§6.1: white flash plus knockback, engine side). The optional Throw clip isn't made.

### Why the two hands never leave the launcher

Every upper-layer motion is rigid for "both arms + launcher". There are only three kinds:
1. Spine, Chest, Neck and Head rotations.
2. Both upper arms rotating about the measured shoulder line, the line through both shoulder joints. It's turned 35° with the bladed torso, so the axis is computed, not world X.
3. A small shoulder shrug: the clavicles rotate up and the upper arms counter-rotate, so the arm chains translate.

**Measured on the exported GLB:** across every key of AimIdle, AimWalk, Shoot (L/F/R) and Victory, the left hand moves at most **2.1 mm** relative to the right hand and launcher (re-measured after the Shoot rework: 2.3 mm). Blends between L/F/R are rigid too, and so is any lower clip under them, so the left hand stays on the fore-grip. Death is the only clip where the arms move independently: the left hand deliberately lets go.

## Engine side

The game draws the hero at 10 fps with no blending. Here are three ways to get the layered motion.

### A. Recommended for the current baked pipeline: bake per bone, skin at runtime

1. **Bake per bone.** For every clip and every 0.1 s frame, store the local transform of each of the 21 deforming bones: a quaternion, plus a translation for Hips.
   - Size: 9×3 (AimIdle) + 8×3 (AimWalk) + 3×3 (Shoot) + 9 + 8×4 + 9 + 9 = **119 frames**. 119 × 21 bones × 28 B is about 70 KB raw, or about 25 KB with quantised quaternions.
   - This is the same Blender pass as `pit-unity/Tools/bake_hero_anim.py`. Read `pb.matrix_basis` per bone instead of the deformed vertices. Also store the bind matrices and the vertex weights: at most 4 per vertex, and every vertex here has 4 or fewer.
2. **Compose each displayed frame.**
   - Lower clip at the walk clock → Hips and legs.
   - Upper clip → Spine and up. Blend L/F/R by the aim parameter with a per-bone quaternion slerp between the two neighbouring variants.
   - Full-body clip, when active → all bones.
3. **Skin.** Run FK over 21 bones, then linear-blend skinning of about 968 vertices. On the CPU at 10 Hz this is trivial. Alternatively upload 21 matrices to a skinning vertex shader.
4. **Why A:** any lower × upper × aim combination works, Shoot can start on any walk frame, and the data stays tiny.

### B. Unity Animator with an AvatarMask (if the hero becomes a SkinnedMeshRenderer)

1. **Import** the FBX with Rig = Generic and Root node = none. The takes are named as in the clip table. Tick Loop Time on the looping clips.
2. **Base layer "Lower":**
   - a `Stand` state;
   - a `Walk` state: a 2D Freeform Directional blend tree on (MoveX, MoveY) with WalkForward (0,1), WalkBack (0,−1), StrafeLeft (−1,0) and StrafeRight (1,0). Add Stand at (0,0) too if you want it inside the tree.
3. **Layer "Upper":** Override, weight 1, with an AvatarMask (Transform tab, import the skeleton, tick only the Upper bones listed above). It has three states, each a 1D blend tree on Aim (−1 → `_L`, 0 → `_F`, +1 → `_R`):
   - AimIdle and AimWalk, picked by "moving";
   - Shoot, entered by trigger from Any State with "can transition to self" and a 0 s transition, exiting after 0.2 s with a 0 s transition.
4. **Layer "Full":** no mask, weight 0. Set it to 1 and play Death (clamped) or Victory.
5. **Stepped 10 fps:** disable the Animator component and call `animator.Update(acc)` only when 0.1 s has accumulated.

### C. Pre-composed vertex frames (the current per-frame-mesh path): not recommended

At about 968 vertices × 10 B, each frame costs about 10 KB.
- AimIdle×Stand: 27 frames.
- AimWalk × 4 walks × 3 aims: 96 frames.
- Shoot × 5 lower × 3 aims × (8–9 walk phases) × 2 frames: about 250 frames.
- Total: about 390 frames, or about 3.9 MB. That's over half the 7.5 MB build.

If you must use this path:
- snap aim to the nearest of the three variants;
- bake Shoot only over `Stand` (the legs freeze for 0.2 s);
- bake AimWalk over each walk direction with the shared 0.8 s phase.

That cuts it to about 27 + 96 + 9 + 18 frames, about 1.5 MB.

### Driving the parameters (all variants)

- **Facing:** the body yaw follows the aim. With yaw measured positive to the character's right, keep `aim = clamp((aimYaw − bodyYaw) / 35°, −1, 1)`, so −1 plays `_L` and +1 plays `_R`. When the aim leaves ±35°, rotate the body so the aim sits at the edge. Optionally re-centre the body toward the aim at about 180°/s, which shows the L/R poses during quick aim swings. The body never turns toward the move direction.
- **Move:** take the world move vector into body space, then pick or blend Stand / WalkForward / WalkBack / StrafeLeft / StrafeRight. Diagonals blend two neighbours at 50/50 (method A or B), or use the nearest one (method C).
- **Walk clock:** natural speed is 0.5 m/s in model units, which is 0.9 cells/s at 1.8 cells per metre. spec §4.10 runs at 2.5 board units/s, much faster, so the feet will skate whatever the clip does. Play the cycle at 1.0–1.5× rather than matching speed. Faster than that turns into a blur at 10 fps.
- **Upper state:** no move input → AimIdle. Move input → AimWalk, using the walk clock. On every ball launch → Shoot from frame 0, restarted even mid-Shoot. Frame 0 is the squash and frame 1 the kick, so every shot in a rapid burst shows a fresh squash and kick, with the kick landing 0.1 s after launch. After 0.2 s it goes back to AimIdle/AimWalk.
- **Game over:** play Death once and hold the last frame. **Win:** loop Victory. Both override the two layers.

## Rebuild

```bash
blender -b -P tools/rig_hero_layered.py -- --in models/lowpoly/hero_tinker.glb --out models/lowpoly/hero_tinker_layered --sheet /tmp/tinker_sheet
python tools/hero_layered_sheet.py /tmp/tinker_sheet models/lowpoly/hero_tinker_layered
```
Run both from pit-art/. The second step needs Pillow. `--only AimIdle_F,Shoot_F` renders just those sheet rows, and `SHEET_VIEWS=fronto,side` switches cameras. The game-view camera looks down 40° from behind with an 80 mm lens, matching the board camera's 40° tilt (`GAME_DEG=47` gives the steeper ray to the bottom rows). Env overrides: `PITCH` (launcher pitch, default 30), `BL` (barrel length, 1.2), `HEAD` (cap scale, 0.86).
