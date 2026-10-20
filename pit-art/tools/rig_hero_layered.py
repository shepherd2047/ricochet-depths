"""Tinker in the two-handed launcher stance, with the layered motion set of style.md §6.1 (no Mixamo).

blender -b -P tools/rig_hero_layered.py -- --in models/lowpoly/hero_tinker.glb \
    --out models/lowpoly/hero_tinker_layered [--views DIR] [--sheet DIR] [--stance-only]

Steps: import the A-pose mesh (rig_hero.load) → drop the old tuning-fork prop island → joints (rig_hero.estimate_joints,
then a symmetric shoulder line and a centred spine column) → armature + bone-heat weights → IK the arms onto the grips of
a new two-handed launcher → apply that pose to the mesh and make it the rest/bind pose → add the launcher (UVs on solid
texels of the existing albedo, 100 % RightHand) → clips → <out>.fbx + <out>.glb (+ check renders, contact sheets, GIFs).

Clips (30 fps timeline, one key every 3 frames = 0.1 s, linear; the game samples at 10–12 fps, so every key is a pose):
  upper layer (Spine and up): AimIdle_L/F/R 0.9 s loop, AimWalk_L/F/R 0.8 s loop, Shoot_L/F/R 0.2 s once
  lower layer (Hips and legs): Stand 0.9 s loop, WalkForward / WalkBack / StrafeLeft / StrafeRight 0.8 s loop
  full body: Death 0.8 s (holds), Victory 0.9 s loop
Each action keys only the bones of its layer (see UPPER / LOWER). pit-art/ANIM.md is the hand-over.

Conventions as everywhere in pit-art: Z-up, faces −Y, origin at the feet, character's right is −X, height 1.0 m.
"""
import argparse
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import monster_lib as ml  # noqa: E402  (render helpers)
import rig_hero as rh  # noqa: E402  (mesh prep, joints, armature, skin)

FPS = 30
STEP = 3  # frames per key (0.1 s)
NAME = "hero_tinker"

UPPER = ["Spine", "Chest", "Neck", "Head"] + [f"{s}{b}" for s in ("Left", "Right")
                                               for b in ("Shoulder", "UpperArm", "LowerArm", "Hand")]
LOWER = ["Hips"] + [f"{s}{b}" for s in ("Left", "Right") for b in ("UpperLeg", "LowerLeg", "Foot", "Toes")]
X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)


def parse():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="src", required=True)
    ap.add_argument("--out", required=True, help="path without extension")
    ap.add_argument("--views", default="", help="folder for check renders")
    ap.add_argument("--sheet", default="", help="folder: contact-sheet cells and GIF frames (PNG)")
    ap.add_argument("--stance-only", action="store_true")
    ap.add_argument("--only", default="", help="comma list: sheet rows to render (skips the GIFs)")
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def log(*a):
    print("[layered]", *a, flush=True)


# ------------------------------------------------------------------------------------ joints
def joints(P):
    J = rh.estimate_joints(P)
    cx = 0.5 * (J["LeftUpperLeg"].x + J["RightUpperLeg"].x)
    ty = -0.025  # spine column: middle of the torso slices (apron front −0.14, back +0.08)
    for k in ("Hips", "Spine", "Chest", "Neck", "Head", "HeadEnd"):
        J[k] = Vector((cx, ty, J[k].z))
    # symmetric shoulder line: both UpperArm heads share y and z, so a rotation of both upper arms about X moves
    # arms and launcher as one rigid piece (hands stay on the grips)
    for part in ("UpperArm", "LowerArm", "Hand", "HandEnd"):
        l, r = J[f"Left{part}"], J[f"Right{part}"]
        dx = 0.5 * ((l.x - cx) + (cx - r.x))
        y, z = 0.5 * (l.y + r.y), 0.5 * (l.z + r.z)
        J[f"Left{part}"], J[f"Right{part}"] = Vector((cx + dx, y, z)), Vector((cx - dx, y, z))
    J["LeftUpperArm"].y = J["RightUpperArm"].y = 0.0
    for s, sg in (("Left", 1), ("Right", -1)):
        J[f"{s}Shoulder"] = Vector((cx + sg * 0.045, ty, J[f"{s}UpperArm"].z))
    return J, cx


# ---------------------------------------------------------------------------------- launcher
def _env(k, d):
    v = os.environ.get(k)
    return d if v is None else (tuple(float(x) for x in v.split(",")) if "," in v else float(v))


LX, LZ, Y0, GR, GL = _env("LX", 0.185), _env("LZ", 0.62), _env("Y0", 0.03), _env("GR", 0.15), _env("GL", 0.27)
DR, DL = _env("DR", (0.8, -0.4, 0.25)), _env("DL", (-0.8, -0.45, 0.3))
TWIST = _env("TWIST", 35)  # rest stance: torso bladed this many degrees to the right (left shoulder forward)
BL = _env("BL", 1.2)  # barrel length factor (bell + prongs move out with it)
PITCH = _env("PITCH", 30)  # rest stance: muzzle this many degrees up, so the barrel rises past the cap seen from behind
GAME_DEG = _env("GAME_DEG", 40)  # check-render camera: behind the hero, this many degrees down (pit-unity: 40)
HEAD = _env("HEAD", 0.86)  # head + cap scale (pit-unity close-up: the cap hid the body); the model is rescaled to 1 m


def launcher_frame(cx):
    """Barrel axis along −Y, held right of centre at chest height."""
    lx, lz, y0 = cx - LX, LZ, Y0
    grip_r = Vector((lx + 0.01, y0 - GR, lz - 0.08))  # pistol grip under the barrel
    grip_l = Vector((lx + 0.01, y0 - GL, lz - 0.055))  # fore-grip: palm under the barrel
    dir_r = Vector((DR[0], DR[1], DR[2])).normalized()  # mitt direction (fingers) per hand
    dir_l = Vector((DL[0], DL[1], DL[2])).normalized()
    # the whole launcher (and both grips) pitched muzzle-up about the right grip
    R = Matrix.Rotation(math.radians(-PITCH), 3, "X")  # about +X, −Y (muzzle) turns toward −Z; negative = up
    piv = grip_r.copy()
    rot = lambda p: piv + R @ (p - piv)  # noqa: E731
    muzzle = rot(Vector((lx, y0 - 0.33 * BL, lz)))
    return dict(lx=lx, lz=lz, y0=y0, grip_r=grip_r, grip_l=rot(grip_l), dir_r=R @ dir_r, dir_l=R @ dir_l,
                R=R, piv=piv, rot=rot, muzzle=muzzle, axis=(R @ Vector((0, -1, 0))).normalized())


def find_texel(img, rgb):
    w, h = img.size
    px = np.array(img.pixels[:]).reshape(h, w, 4)[:, :, :3]
    d = ((px - np.array(rgb)) ** 2).sum(-1)
    iy, ix = np.unravel_index(np.argmin(d), d.shape)
    log(f"texel for {rgb}: {tuple(round(float(c), 2) for c in px[iy, ix])} at {ix},{iy}")
    return ((ix + 0.5) / w, (iy + 0.5) / h)


def add_launcher(obj, L):
    """Stubby brass launcher: hex barrel with a bell muzzle and tuning-fork prongs, cobalt grips (≈60 verts)."""
    img = next(n.image for m in obj.data.materials for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    uv_gold = find_texel(img, (0.95, 0.70, 0.15))
    uv_dark = find_texel(img, (0.55, 0.36, 0.10))
    uv_cobalt = find_texel(img, (0.17, 0.45, 0.85))
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uvl = bm.loops.layers.uv.active
    n0 = len(bm.verts)
    lx, lz, y0 = L["lx"], L["lz"], L["y0"]

    def ring(y, r, n=6, ox=0.0, oz=0.0):
        return [bm.verts.new((lx + ox + r * math.cos(2 * math.pi * (i + 0.5) / n), y,
                              lz + oz + r * math.sin(2 * math.pi * (i + 0.5) / n))) for i in range(n)]

    def face(vs, uv):
        f = bm.faces.new(vs)
        for lp in f.loops:
            lp[uvl].uv = uv
        return f

    def tube(rings, uvs, caps=True):
        for a, b, uv in zip(rings, rings[1:], uvs):
            n = len(a)
            for i in range(n):
                face([a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]], uv)
        if caps:
            face(list(reversed(rings[0])), uvs[0])
            face(rings[-1], uvs[-1])

    # barrel (rear → muzzle along −Y): rear cap, body, cobalt band, body, bell flare
    rs = [ring(y0, 0.034), ring(y0 - 0.02, 0.044), ring(y0 - 0.25 * BL, 0.042), ring(y0 - 0.29 * BL, 0.068),
          ring(y0 - 0.33 * BL, 0.064)]
    tube(rs, [uv_dark, uv_gold, uv_gold, uv_gold, uv_dark])

    def box(c, size, uv):
        cx_, cy_, cz_ = c
        sx, sy, sz = size
        v = [bm.verts.new((cx_ + dx * sx / 2, cy_ + dy * sy / 2, cz_ + dz * sz / 2))
             for dz in (-1, 1) for dy in (-1, 1) for dx in (-1, 1)]
        for q in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            face([v[i] for i in q], uv)

    # tuning-fork prongs out of the bell
    for sx in (-1, 1):
        box((lx + sx * 0.034, y0 - 0.33 * BL - 0.035, lz), (0.026, 0.08, 0.03), uv_gold)
    # cobalt pistol grip and fore-grip block
    box((lx, y0 - GR, lz - 0.06), (0.034, 0.04, 0.07), uv_cobalt)
    box((lx, y0 - GL, lz - 0.045), (0.05, 0.06, 0.03), uv_cobalt)
    bm.verts.ensure_lookup_table()
    for v in bm.verts[n0:]:
        v.co = L["rot"](v.co)
    bm.to_mesh(obj.data)
    bm.free()
    new = list(range(n0, len(obj.data.vertices)))
    obj.vertex_groups["RightHand"].add(new, 1.0, "REPLACE")
    log(f"launcher: {len(new)} verts")


def shrink_head(obj):
    """Scale the head and cap by HEAD about the neck (smooth over a 6 cm band), then scale the model back to its height."""
    if abs(HEAD - 1.0) < 1e-6:
        return
    P = np.array([v.co[:] for v in obj.data.vertices])
    J = rh.estimate_joints(P)
    zt = P[:, 2].max()
    neck = J["Neck"].z + 0.02
    piv = Vector((J["Head"].x, -0.025, neck))
    for v in obj.data.vertices:
        w = min(1.0, max(0.0, (v.co.z - (neck - 0.03)) / 0.06))
        w = w * w * (3 - 2 * w)
        v.co = piv + (v.co - piv) * (1 + (HEAD - 1) * w)
    k = zt / max(v.co.z for v in obj.data.vertices)
    for v in obj.data.vertices:
        v.co = Vector((v.co.x * k, v.co.y * k, v.co.z * k))
    log(f"head x{HEAD}, body rescaled x{k:.3f} to keep the height")


def add_muzzle_bone(rig, L):
    """Non-deforming 'Muzzle' bone under RightHand at the bell mouth, Y axis along the barrel (flash attach point)."""
    pose_mode(rig)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = rig.data.edit_bones.new("Muzzle")
    eb.head = L["muzzle"]
    eb.tail = L["muzzle"] + L["axis"] * 0.05
    eb.parent = rig.data.edit_bones["RightHand"]
    eb.use_deform = False
    eb.align_roll(Vector(Z))
    bpy.ops.object.mode_set(mode="OBJECT")
    log(f"Muzzle bone at {tuple(round(c, 3) for c in L['muzzle'])}, axis {tuple(round(c, 3) for c in L['axis'])}")


# ------------------------------------------------------------------------------------ stance
def empty(name, loc):
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.location = loc
    return e


def pose_mode(rig):
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")


def stance(obj, rig, L, cx):
    """IK the arms onto the launcher grips, then bake that pose into the mesh and the rest pose."""
    tmp = []
    for b, deg in (("Spine", -0.45 * TWIST), ("Chest", -0.55 * TWIST), ("Neck", 0.5 * TWIST), ("Head", 0.5 * TWIST)):
        pb = rig.pose.bones[b]
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = rot_local(pb, Vector(Z), deg)
    bpy.context.view_layer.update()
    for s, sg, grip, d in (("Right", -1, L["grip_r"], L["dir_r"]), ("Left", 1, L["grip_l"], L["dir_l"])):
        wrist = grip - d * 0.06
        sh = rig.pose.bones[f"{s}UpperArm"].head
        reach = rig.data.bones[f"{s}UpperArm"].length + rig.data.bones[f"{s}LowerArm"].length
        log(f"{s} wrist {tuple(round(c, 3) for c in wrist)} distance {(wrist - sh).length:.3f} of reach {reach:.3f}")
        t = empty(f"IK_{s}", wrist)
        p = empty(f"Pole_{s}", Vector((cx + sg * 0.45, 0.10, 0.30)))
        a = empty(f"Aim_{s}", grip + d * 0.2)
        tmp += [t, p, a]
        pb = rig.pose.bones[f"{s}LowerArm"]
        c = pb.constraints.new("IK")
        c.target, c.pole_target, c.chain_count = t, p, 2
        c.pole_angle = math.radians(-90)
        c2 = rig.pose.bones[f"{s}Hand"].constraints.new("DAMPED_TRACK")
        c2.target, c2.track_axis = a, "TRACK_Y"
    bpy.context.view_layer.update()
    pose_mode(rig)
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.pose.visual_transform_apply()
    for pb in rig.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
    for e in tmp:
        bpy.data.objects.remove(e)
    bpy.ops.object.mode_set(mode="OBJECT")
    for s in ("Right", "Left"):
        hb = rig.pose.bones[f"{s}Hand"]
        log(f"{s} hand head {tuple(round(c, 3) for c in hb.head)} tail {tuple(round(c, 3) for c in hb.tail)}")
    # the posed mesh becomes the mesh; the posed skeleton becomes the rest skeleton
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    mod = next(m for m in obj.modifiers if m.type == "ARMATURE")
    bpy.ops.object.modifier_apply(modifier=mod.name)
    pose_mode(rig)
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    m = obj.modifiers.new("Armature", "ARMATURE")
    m.object = rig


# --------------------------------------------------------------------------------- keying
def rot_local(pb, axis_world, deg):
    m = pb.bone.matrix_local.to_3x3()
    return (m.inverted() @ Quaternion(axis_world, math.radians(deg)).to_matrix() @ m).to_quaternion()


def set_pose(rig, pose, bones):
    """pose: {bone: {"rot": [(axis, deg), …] applied in order about rest-frame axes, "loc": world offset (Hips)}}."""
    for name in bones:
        pb = rig.pose.bones[name]
        q = Quaternion()
        for ax, deg in pose.get(name, {}).get("rot", []):
            q = rot_local(pb, Vector(ax), deg) @ q
        pb.rotation_quaternion = q
        loc = pose.get(name, {}).get("loc")
        pb.location = pb.bone.matrix_local.to_3x3().inverted() @ Vector(loc or (0, 0, 0))


def key_bones(rig, bones, frame, loc=False):
    for name in bones:
        pb = rig.pose.bones[name]
        pb.keyframe_insert("rotation_quaternion", frame=frame, group=name)
        if loc or name == "Hips":
            pb.keyframe_insert("location", frame=frame, group=name)


def new_action(rig, name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = act
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return act


def times(length, loop):
    n = round(length * 10)
    return [i * STEP for i in range(n + 1)]  # loops: last key == first (the engine drops it)


def finish(act):
    for fc in _fcurves(act):
        for k in fc.keyframe_points:
            k.interpolation = "LINEAR"


def _fcurves(act):
    if hasattr(act, "fcurves") and len(act.fcurves):
        return list(act.fcurves)
    out = []  # Blender 5 layered actions
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                out += list(bag.fcurves)
    return out


def fk_clip(rig, name, length, loop, fn, bones):
    """fn(t seconds, u phase 0..1) -> pose dict."""
    act = new_action(rig, name)
    for f in times(length, loop):
        t = f / FPS
        set_pose(rig, fn(t, (t / length) % 1.0 if loop else t / length), bones)
        key_bones(rig, bones, f)
    finish(act)
    rig.animation_data.action = None
    return act


# ----------------------------------------------------------------------------- upper layer
AIM = {"L": 1.0, "F": 0.0, "R": -1.0}  # +Z yaw turns toward the character's left (+X)
L_SWING = _env("L_SWING", (35.0, -20.0, 10.0))  # aim L only: extra chest yaw, chest roll, launcher drop (deg)
AIM_DEG = 35.0                           # Left/Right variant = 35° of yaw, split Spine 15 / Chest 20


GEOM = {}  # rest-pose shoulder geometry, filled by measure_shoulders() after the stance is baked


def measure_shoulders(rig):
    """The bladed stance turns the shoulder line, so pitch axis and girdle shifts are measured, not world X/Y/Z."""
    b = rig.data.bones
    lh, rh_ = b["LeftUpperArm"].head_local, b["RightUpperArm"].head_local
    A = (lh - rh_).normalized()                 # right shoulder → left shoulder
    GEOM["A"] = tuple(A)
    GEOM["B"] = tuple(Vector(Z).cross(A).normalized())  # backward, square to the shoulder line
    for s in ("Left", "Right"):
        GEOM[s] = b[f"{s}UpperArm"].head_local - b[f"{s}Shoulder"].head_local
    log(f"shoulder line {tuple(round(c, 3) for c in A)}")


def _girdle(side, back, up):
    """Clavicle rotation (axis, deg) that moves the shoulder joint by back·B + up·Z (arc, |o| kept)."""
    o = GEOM[side]
    d = Vector(GEOM["B"]) * back + Vector(Z) * up
    w = o.cross(d)
    if w.length < 1e-9:
        return (Z, 0.0)
    return (tuple(w.normalized()), math.degrees(math.atan2(d.length, o.length)))


def upper_pose(aim, chest_pitch=0.0, chest_roll=0.0, spine_pitch=0.0, arm_pitch=0.0, sh_back=0.0, sh_up=0.0,
               head_pitch=0.0, head_yaw=0.0):
    """Every motion here is rigid for arms + launcher: torso rotations, both upper arms about the shoulder line
    (arm_pitch, − = muzzle up), and the shoulder girdle shifted back/up (clavicles rotate, upper arms counter-rotate,
    so the arms translate). Hands stay on the grips to a few mm (girdle arcs pull the shoulders in slightly)."""
    yaw = aim * AIM_DEG
    # aiming left swings the shoulder-mounted launcher in line with the cap seen from behind: on the L side add
    # extra chest yaw (head counter-turns), lean the chest left and drop the launcher, so the barrel clears the cap
    l = max(0.0, aim)
    yaw_x, roll_x, drop_x = l * L_SWING[0], l * L_SWING[1], l * L_SWING[2]
    A = GEOM["A"]
    pose = {
        "Spine": {"rot": [(X, spine_pitch), (Z, 0.43 * yaw)]},
        "Chest": {"rot": [(X, chest_pitch), (Y, chest_roll + roll_x), (Z, 0.57 * yaw + yaw_x)]},
        "Neck": {"rot": [(X, -0.5 * (chest_pitch + spine_pitch)), (Y, -0.5 * roll_x), (Z, 0.12 * yaw - 0.5 * yaw_x)]},
        "Head": {"rot": [(X, head_pitch - 0.5 * (chest_pitch + spine_pitch)), (Y, -0.5 * roll_x),
                         (Z, head_yaw + 0.1 * yaw - 0.5 * yaw_x)]},
    }
    arm_pitch += drop_x
    for s in ("Left", "Right"):
        ax, deg = _girdle(s, sh_back, sh_up)
        pose[f"{s}Shoulder"] = {"rot": [(ax, deg)]}
        # arm pitch about the shared shoulder line (sign: − = muzzle up, as before with world X)
        pose[f"{s}UpperArm"] = {"rot": [(A, -arm_pitch if A[0] < 0 else arm_pitch), (ax, -deg)]}
    return pose


def aim_idle(aim):  # 0.9 s: breathe in (chest up, shoulders rise, muzzle dips a hair) and out
    def fn(t, u):
        b = 0.5 - 0.5 * math.cos(2 * math.pi * u)
        return upper_pose(aim, chest_pitch=-2.5 * b, sh_up=0.008 * b, arm_pitch=2.0 * b - 1.0, head_pitch=1.5 * b)
    return fn


def aim_walk(aim):  # 0.8 s, two steps: lean in, bob on each step (lagging the hips), sway toward the stance foot
    def fn(t, u):
        bob = math.cos(4 * math.pi * u)  # +1 at contact (frames 0 and 4)
        sway = math.sin(2 * math.pi * u)
        return upper_pose(aim, chest_pitch=5.0 + 2.5 * bob, chest_roll=-3.0 * sway, sh_up=-0.008 * bob,
                          arm_pitch=3.5 * bob, head_pitch=-3.0 * bob, head_yaw=2.0 * sway)
    return fn


def shoot(aim):
    """0.2 s, read from 40 deg behind: frame 0 squash (hunch forward, muzzle dips, cap ducks), frame 1 big kick (spine +
    chest snap back ~30 deg, throwing the launcher ~0.15 m back toward the camera, muzzle up 30, cap bobs back),
    frame 2 = aim pose."""
    def fn(t, u):
        sq, k = ((1.0, 0.0), (0.0, 1.0), (0.0, 0.0))[min(2, round(t * 10))]
        return upper_pose(aim, chest_pitch=8.0 * sq - 17.0 * k, spine_pitch=4.0 * sq - 13.0 * k,
                          arm_pitch=8.0 * sq - 30.0 * k, sh_up=-0.01 * sq + 0.014 * k,
                          head_pitch=10.0 * sq - 14.0 * k)
    return fn


# ----------------------------------------------------------------------------- lower layer
CROUCH = 0.02     # hips lowered in every lower clip: knees never lock straight
STRIDE = 0.26     # foot travel per step (m), all four walk directions
WALK_T = 0.8
LIFT = 0.09
WIDE = 0.035      # every lower clip: each foot this much further out than the bind pose, so feet clear the cap


class Legs:
    """Feet on IK targets (two-bone IK, pole in front of the knee) with world-level soles, baked to FK keys."""

    def __init__(self, rig):
        self.rig = rig
        self.base, self.rest_rot, self.e, self.r = {}, {}, {}, {}
        for s in ("Left", "Right"):
            b = rig.data.bones[f"{s}Foot"]
            self.base[s] = b.head_local.copy()
            self.rest_rot[s] = b.matrix_local.to_quaternion()

    def setup(self):
        rig = self.rig
        for s in ("Left", "Right"):
            knee = rig.data.bones[f"{s}LowerLeg"].head_local
            self.e[s] = empty(f"Foot_{s}", self.base[s])
            self.r[s] = empty(f"FootRot_{s}", self.base[s])
            self.r[s].rotation_mode = "QUATERNION"
            pole = empty(f"Knee_{s}", knee + Vector((0, -0.5, 0)))
            c = rig.pose.bones[f"{s}LowerLeg"].constraints.new("IK")
            c.target, c.pole_target, c.chain_count = self.e[s], pole, 2
            c2 = rig.pose.bones[f"{s}Foot"].constraints.new("COPY_ROTATION")
            c2.target = self.r[s]
            self.poles = getattr(self, "poles", []) + [pole]
            # pole angle depends on the bone rolls: pick the one that keeps the knee hinge untwisted (crouched test)
            hips = rig.pose.bones["Hips"]
            hips.location = hips.bone.matrix_local.to_3x3().inverted() @ Vector((0, 0, -0.04))
            best = None
            for deg in range(-180, 180, 15):
                c.pole_angle = math.radians(deg)
                bpy.context.view_layer.update()
                err = 0.0
                for b in (f"{s}UpperLeg", f"{s}LowerLeg"):
                    pb = rig.pose.bones[b]
                    xr = pb.bone.matrix_local.to_3x3().col[0]
                    xp = pb.matrix.to_3x3().col[0]
                    err += xr.angle(xp)
                best = min(best or (9e9, 0), (err, deg))
            c.pole_angle = math.radians(best[1])
            hips.location = Vector()

    def teardown(self):
        for pb in self.rig.pose.bones:
            for c in list(pb.constraints):
                pb.constraints.remove(c)
        for o in list(self.e.values()) + list(self.r.values()) + self.poles:
            bpy.data.objects.remove(o)
        self.e, self.r, self.poles = {}, {}, []

    def key(self, f, feet):
        """feet: {side: (offset Vector from base, sole pitch deg)}"""
        for s, (off, pitch) in feet.items():
            self.e[s].location = self.base[s] + Vector(off)
            self.e[s].keyframe_insert("location", frame=f)
            self.r[s].rotation_quaternion = Quaternion(Vector(X), math.radians(pitch)) @ self.rest_rot[s]
            self.r[s].keyframe_insert("rotation_quaternion", frame=f)

    def clip(self, name, length, loop, hips_fn, feet_fn, upper_fn=None):
        """hips_fn(t,u) -> pose dict for Hips; feet_fn(t,u) -> feet dict; optional FK upper body (full-body clips)."""
        rig = self.rig
        act = new_action(rig, name)
        self.setup()
        fr = times(length, loop)
        for f in fr:
            t = f / FPS
            u = (t / length) % 1.0 if loop else t / length
            set_pose(rig, hips_fn(t, u), ["Hips"])
            key_bones(rig, ["Hips"], f)
            self.key(f, feet_fn(t, u))
        for o in list(self.e.values()) + list(self.r.values()):
            for fc in _fcurves(o.animation_data.action):
                for k in fc.keyframe_points:
                    k.interpolation = "LINEAR"
        pose_mode(rig)
        bpy.ops.pose.select_all(action="DESELECT")
        for n in LOWER:
            pb = rig.pose.bones[n]
            if hasattr(pb, "select"):
                pb.select = True  # Blender 5
            else:
                rig.data.bones[n].select = True
        bpy.ops.nla.bake(frame_start=fr[0], frame_end=fr[-1], step=STEP, only_selected=True, visual_keying=True,
                         clear_constraints=False, use_current_action=True, bake_types={"POSE"},
                         channel_types={"LOCATION", "ROTATION"})
        bpy.ops.object.mode_set(mode="OBJECT")
        self.teardown()
        act = rig.animation_data.action
        act.name = name
        act.use_fake_user = True
        if upper_fn:
            for f in fr:
                t = f / FPS
                set_pose(rig, upper_fn(t, t / length if not loop else (t / length) % 1.0), UPPER)
                key_bones(rig, UPPER, f)
        finish(act)
        rig.animation_data.action = None
        return act


def stand_hips(t, u):
    return {"Hips": {"loc": (0, 0, -CROUCH - 0.008 * (0.5 - 0.5 * math.cos(2 * math.pi * u)))}}


def stand_feet(t, u):
    return {"Left": ((WIDE, 0, 0), 0.0), "Right": ((-WIDE, 0, 0), 0.0)}


def walk_feet(d, widen=0.0):
    d = Vector((d[0], d[1], 0))
    back = d.y > 0.5  # walking backward: land on the toes, push off the heel

    def foot(u, side_sign):
        if u < 0.5:  # stance: planted foot slides opposite to the motion
            s = u / 0.5
            off = d * STRIDE * (0.5 - s)
            pitch = 0.0
        else:        # swing: lift and carry forward along the motion
            s = (u - 0.5) / 0.5
            e = s * s * (3 - 2 * s)
            off = d * STRIDE * (-0.5 + e)
            off.z = LIFT * math.sin(math.pi * s)
            pitch = (-18.0 if not back else 14.0) * math.sin(math.pi * s)  # toe down while lifting (+ = toe up)
        off.x += side_sign * (WIDE + widen)
        return off, pitch

    def fn(t, u):
        return {"Left": foot(u % 1.0, 1), "Right": foot((u + 0.5) % 1.0, -1)}
    return fn


def walk_hips(d):
    side = abs(d[0]) > 0.5

    def fn(t, u):
        bob = math.cos(4 * math.pi * u)  # low at contact
        sway = math.sin(2 * math.pi * u)  # toward the stance foot (left stance first)
        rot = [(Y, 3.0 * sway)] if not side else [(Y, 2.0 * sway)]
        loc = (0.012 * sway if not side else 0.006 * sway, 0, -CROUCH - 0.018 * (bob + 1) / 2 + 0.006)
        return {"Hips": {"loc": loc, "rot": rot}}
    return fn


# ----------------------------------------------------------------------------- full body
def death_pose(t, u):
    """0.8 s: kick back (0.0), knees buckle (0.1–0.3), topple backward (0.4–0.6), flat with a bounce (0.7–0.8)."""
    k = [0.0, 0.15, 0.32, 0.5, 0.7, 0.88, 1.0, 1.0, 1.0][min(8, round(t * 10))]
    jolt = [1.0, 0.7, 0.3, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0][min(8, round(t * 10))]
    bounce = [0, 0, 0, 0, 0, 0, 0, 1, 0][min(8, round(t * 10))]
    return {
        "Hips": {"loc": (0, 0.10 * k, -0.20 * min(1, k * 1.4) + 0.025 * bounce), "rot": [(X, -80 * k ** 1.5)]},
        "Spine": {"rot": [(X, -10 * jolt + 8 * k)]},
        "Chest": {"rot": [(X, -12 * jolt + 6 * k)]},
        "Neck": {"rot": [(X, -8 * jolt)]},
        "Head": {"rot": [(X, -14 * jolt - 10 * k), (Z, 28 * k)]},
        "LeftShoulder": {"rot": []},
        "RightShoulder": {"rot": []},
        "LeftUpperArm": {"rot": [(X, -30 * jolt - 40 * k), (Y, 70 * k)]},   # left hand lets go, flops out
        "LeftLowerArm": {"rot": [(X, 30 * k)]},
        "RightUpperArm": {"rot": [(X, -40 * jolt - 70 * k), (Y, -35 * k)]},  # launcher flung up and out
        "LeftUpperLeg": {"rot": [(X, -55 * k + 45 * k ** 3)]},
        "RightUpperLeg": {"rot": [(X, -70 * k + 50 * k ** 3)]},
        "LeftLowerLeg": {"rot": [(X, 80 * k - 60 * k ** 3)]},
        "RightLowerLeg": {"rot": [(X, 95 * k - 55 * k ** 3)]},
        "LeftFoot": {"rot": [(X, -20 * k)]},
        "RightFoot": {"rot": [(X, -25 * k)]},
    }


VIC = [  # 9 keys: crouch, launch, rise, top, top, fall, land, crouch, rise-to-loop
    dict(h=-0.05, up=0.75, lift=0.00), dict(h=0.02, up=1.0, lift=0.00), dict(h=0.08, up=1.0, lift=0.04),
    dict(h=0.10, up=1.0, lift=0.06), dict(h=0.09, up=1.0, lift=0.055), dict(h=0.04, up=1.0, lift=0.03),
    dict(h=-0.02, up=0.9, lift=0.0), dict(h=-0.06, up=0.7, lift=0.0), dict(h=-0.055, up=0.72, lift=0.0)]


def _vic(t):
    return VIC[round(t * 10) % len(VIC)]  # t = 0.9 wraps to key 0 (loop)


def victory_hips(t, u):
    v = _vic(t)
    return {"Hips": {"loc": (0, 0, v["h"])}}


def victory_feet(t, u):
    v = _vic(t)
    return {"Left": ((WIDE, 0, v["lift"]), -15 * v["lift"] / 0.06), "Right": ((-WIDE, 0, v["lift"]), -15 * v["lift"] / 0.06)}


def victory_upper(t, u):
    """Both hands stay on the launcher: it is pumped skyward (rigid pitch about the shoulder line) on every hop,
    chest thrown open and head up; at the crouch it comes back down to the shoulder."""
    v = _vic(t)
    a = v["up"]
    return upper_pose(0.0, chest_pitch=-10 * a, spine_pitch=-4 * a, arm_pitch=-30 - 45 * (a - 0.7) / 0.3,
                      sh_up=0.01 * a, head_pitch=-14 * a, chest_roll=4 * a)


# ----------------------------------------------------------------------------------- build
def build_clips(rig):
    measure_shoulders(rig)
    acts = {}
    for k, aim in AIM.items():
        acts[f"AimIdle_{k}"] = fk_clip(rig, f"AimIdle_{k}", 0.9, True, aim_idle(aim), UPPER)
        acts[f"AimWalk_{k}"] = fk_clip(rig, f"AimWalk_{k}", WALK_T, True, aim_walk(aim), UPPER)
        acts[f"Shoot_{k}"] = fk_clip(rig, f"Shoot_{k}", 0.2, False, shoot(aim), UPPER)
    legs = Legs(rig)
    acts["Stand"] = legs.clip("Stand", 0.9, True, stand_hips, stand_feet)
    for name, d, w in (("WalkForward", (0, -1), 0.0), ("WalkBack", (0, 1), 0.0),
                       ("StrafeLeft", (1, 0), 0.025), ("StrafeRight", (-1, 0), 0.025)):
        acts[name] = legs.clip(name, WALK_T, True, walk_hips(d), walk_feet(d, w))
    acts["Death"] = fk_clip(rig, "Death", 0.8, False, death_pose, UPPER + LOWER)
    acts["Victory"] = legs.clip("Victory", 0.9, True, victory_hips, victory_feet, victory_upper)
    rig.animation_data.action = None
    for name, act in acts.items():
        tr = rig.animation_data.nla_tracks.new()
        tr.name = name
        tr.strips.new(name, 0, act)
    return acts


CLIP_INFO = {  # name: (layer, length, loop)
    **{f"AimIdle_{k}": ("upper", 0.9, True) for k in AIM},
    **{f"AimWalk_{k}": ("upper", WALK_T, True) for k in AIM},
    **{f"Shoot_{k}": ("upper", 0.2, False) for k in AIM},
    "Stand": ("lower", 0.9, True), "WalkForward": ("lower", WALK_T, True), "WalkBack": ("lower", WALK_T, True),
    "StrafeLeft": ("lower", WALK_T, True), "StrafeRight": ("lower", WALK_T, True),
    "Death": ("full", 0.8, False), "Victory": ("full", 0.9, True),
}


# ---------------------------------------------------------------------------------- layers
def sample_action(act, frame):
    vals = {}
    for fc in _fcurves(act):
        p = fc.data_path
        if not p.startswith('pose.bones["'):
            continue
        bone = p.split('"')[1]
        prop = p.rsplit(".", 1)[1]
        vals.setdefault(bone, {}).setdefault(prop, {})[fc.array_index] = fc.evaluate(frame)
    return vals


def apply_layers(rig, layers):
    """layers: [(action, frame, bones)] later layers win; bones outside every layer stay at rest."""
    ad = rig.animation_data
    if ad:
        ad.action = None
        for t in ad.nla_tracks:
            t.mute = True
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
    for act, frame, bones in layers:
        v = sample_action(act, frame)
        for b in bones:
            if b not in v:
                continue
            pb = rig.pose.bones[b]
            if "rotation_quaternion" in v[b]:
                q = v[b]["rotation_quaternion"]
                pb.rotation_quaternion = Quaternion((q.get(0, 1), q.get(1, 0), q.get(2, 0), q.get(3, 0))).normalized()
            if "location" in v[b]:
                lv = v[b]["location"]
                pb.location = Vector((lv.get(0, 0), lv.get(1, 0), lv.get(2, 0)))
    bpy.context.view_layer.update()


# ---------------------------------------------------------------------------------- renders
class Cams:
    def __init__(self, sc, res):
        self.sc = sc
        sc.render.resolution_x = sc.render.resolution_y = res
        sc.render.film_transparent = False
        self.tgt = empty("CamT", (0, -0.05, 0.42))
        self.cams = {}
        # game: perspective, behind the hero (+Y) and 40° above, looking at −Y (into the pit)
        # the board camera is tilted 40° (FOV 65°); the ray to the hero's rows near the bottom edge is ≈ 47° steep
        self.add("game", "PERSP", (0, 3.4 * math.cos(math.radians(GAME_DEG)), 0.42 + 3.4 * math.sin(math.radians(GAME_DEG))))
        self.add("front", "ORTHO", (1.7, -2.6, 1.3))
        self.add("side", "ORTHO", (-3.0, 0, 0.5))
        self.add("fronto", "ORTHO", (0, -3.0, 0.5))

    def add(self, key, kind, loc):
        cam = bpy.data.objects.new("Cam_" + key, bpy.data.cameras.new("Cam_" + key))
        cam.data.type = kind
        if kind == "ORTHO":
            cam.data.ortho_scale = 1.45
        else:
            cam.data.lens = 80
        self.sc.collection.objects.link(cam)
        cam.location = loc
        c = cam.constraints.new("TRACK_TO")
        c.target = self.tgt
        self.cams[key] = cam

    def shot(self, key, path):
        self.sc.camera = self.cams[key]
        self.sc.render.filepath = path
        bpy.ops.render.render(write_still=True)


def make_floor(sc):
    me = bpy.data.meshes.new("Floor")
    bm = bmesh.new()
    n, s = 12, 0.25
    for i in range(-n, n):
        for j in range(-n, n):
            vs = [bm.verts.new((i * s + dx * s, j * s + dy * s, 0)) for dx, dy in ((0, 0), (1, 0), (1, 1), (0, 1))]
            f = bm.faces.new(vs)
            f.material_index = (i + j) % 2
    bm.to_mesh(me)
    bm.free()
    for k, c in (("FloorA", (0.30, 0.33, 0.27, 1)), ("FloorB", (0.24, 0.27, 0.22, 1))):
        m = bpy.data.materials.new(k)
        m.use_nodes = True
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = c
        m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1.0
        me.materials.append(m)
    fl = bpy.data.objects.new("Floor", me)
    sc.collection.objects.link(fl)
    return fl


def render_views(rig, folder):
    os.makedirs(folder, exist_ok=True)
    sc = ml._setup_render(640, 640)
    cams = Cams(sc, 640)
    make_floor(sc)
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
    bpy.context.view_layer.update()
    for key in ("fronto", "side", "front", "game"):
        cams.shot(key, os.path.join(folder, f"rest_{key}.png"))
    log("views in", folder)
    return sc, cams


def walk_speed():
    return 2 * STRIDE / WALK_T  # m/s the in-place cycle implies


def render_sheet(rig, acts, folder, sc, cams, only=()):
    """Contact-sheet cells: every clip at 10 fps, composed with a sensible partner layer, in two views."""
    sc.render.resolution_x = sc.render.resolution_y = 240
    partner = {}
    for k in AIM:
        partner[f"AimIdle_{k}"] = ("Stand", 0.9)
        partner[f"AimWalk_{k}"] = ("WalkForward", WALK_T)
        partner[f"Shoot_{k}"] = ("Stand", 0.9)
    for n in ("WalkForward", "WalkBack", "StrafeLeft", "StrafeRight"):
        partner[n] = ("AimWalk_F", WALK_T)
    partner["Stand"] = ("AimIdle_F", 0.9)
    for name, (layer, length, loop) in CLIP_INFO.items():
        if only and name not in only:
            continue
        n = round(length * 10) + (0 if loop else 1)
        for i in range(n):
            f = i * STEP
            if layer == "full":
                apply_layers(rig, [(acts[name], f, UPPER + LOWER)])
            else:
                pn, pl = partner[name]
                pf = f % round(pl * FPS)
                mine = UPPER if layer == "upper" else LOWER
                other = LOWER if layer == "upper" else UPPER
                apply_layers(rig, [(acts[pn], pf, other), (acts[name], f, mine)])
            for key in os.environ.get("SHEET_VIEWS", "game,front").split(","):
                d = os.path.join(folder, "sheet", key, name)
                os.makedirs(d, exist_ok=True)
                cams.shot(key, os.path.join(d, f"{i:02d}.png"))
    log("sheet cells in", folder)


def render_combo(rig, acts, folder, sc, cams, name, lower, aim_k, move, shots, seconds=1.6):
    """GIF frames at 10 fps: lower clip + AimWalk_<aim>, Shoot_<aim> fired at the listed times; floor scrolls."""
    sc.render.resolution_x = sc.render.resolution_y = 360
    floor = bpy.data.objects["Floor"]
    v = walk_speed()
    for i in range(round(seconds * 10)):
        t = i / 10
        f = (i * STEP) % round(WALK_T * FPS)
        layers = [(acts[lower], f, LOWER), (acts[f"AimWalk_{aim_k}"], f, UPPER)]
        for s in shots:
            if 0 <= t - s < 0.2 - 1e-6:
                layers[1] = (acts[f"Shoot_{aim_k}"], round((t - s) * FPS), UPPER)
        apply_layers(rig, layers)
        floor.location = (-move[0] * v * t % 0.5, -move[1] * v * t % 0.5, 0)
        for key in ("game", "front"):
            d = os.path.join(folder, "gif", name, key)
            os.makedirs(d, exist_ok=True)
            cams.shot(key, os.path.join(d, f"{i:02d}.png"))
    floor.location = (0, 0, 0)


# ---------------------------------------------------------------------------------- output
def export(obj, rig, out):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
    bpy.context.scene.render.fps = FPS
    bpy.ops.export_scene.fbx(
        filepath=out + ".fbx", use_selection=True, object_types={"MESH", "ARMATURE"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", add_leaf_bones=False,
        path_mode="COPY", embed_textures=True, mesh_smooth_type="FACE",
        bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, bake_anim_step=1.0,
    )
    bpy.ops.export_scene.gltf(
        filepath=out + ".glb", export_format="GLB", use_selection=True, export_skins=True,
        export_animations=True, export_animation_mode="NLA_TRACKS", export_materials="EXPORT",
    )
    log("wrote", out + ".fbx", f"({os.path.getsize(out + '.fbx') / 1024:.0f} KB)",
        out + ".glb", f"({os.path.getsize(out + '.glb') / 1024:.0f} KB)")


def unity_verts(obj):
    me = obj.data
    keys = set()
    uv = me.uv_layers.active.data
    for p in me.polygons:
        for li in p.loop_indices:
            v = me.loops[li].vertex_index
            keys.add((v, round(uv[li].uv[0], 4), round(uv[li].uv[1], 4)))
    return len(keys)


def main():
    a = parse()
    obj = rh.load(a.src)
    isl = rh.islands(obj.data)
    if len(isl) > 1:  # the old tuning-fork prop: replaced by the two-handed launcher
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bm.verts.ensure_lookup_table()
        drop = [bm.verts[i] for c in isl[1:] for i in c]
        bmesh.ops.delete(bm, geom=drop, context="VERTS")
        bm.to_mesh(obj.data)
        bm.free()
        log(f"removed old prop ({sum(len(c) for c in isl[1:])} verts)")
    shrink_head(obj)
    P = np.array([v.co[:] for v in obj.data.vertices])
    J, cx = joints(P)
    rig = rh.build_armature(J, NAME)
    rh.skin(obj, rig, [])
    L = launcher_frame(cx)
    stance(obj, rig, L, cx)
    add_launcher(obj, L)
    add_muzzle_bone(rig, L)
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.name = NAME
    rig.name = NAME + "_Rig"
    zs = [v.co.z for v in obj.data.vertices]
    log(f"mesh {len(obj.data.vertices)} verts, {len(obj.data.polygons)} faces, ~{unity_verts(obj)} Unity verts, "
        f"z {min(zs):.3f}..{max(zs):.3f}")
    sc = cams = None
    if a.views:
        sc, cams = render_views(rig, a.views)
    if a.stance_only:
        return
    acts = build_clips(rig)
    bpy.context.scene.render.fps = FPS
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    export(obj, rig, a.out)
    if a.sheet:
        if sc is None:
            sc = ml._setup_render(240, 240)
            cams = Cams(sc, 240)
            make_floor(sc)
        render_sheet(rig, acts, a.sheet, sc, cams, [c for c in a.only.split(",") if c])
        if a.only:
            return
        render_combo(rig, acts, a.sheet, sc, cams, "walkback_shoot", "WalkBack", "F", (0, 1), [0.0, 0.4, 0.8, 1.2])
        render_combo(rig, acts, a.sheet, sc, cams, "strafeleft_aimright", "StrafeLeft", "R", (1, 0), [0.6, 1.2])
    log(f"{len(rig.data.bones)} bones, {len(acts)} clips")


if __name__ == "__main__":
    main()
