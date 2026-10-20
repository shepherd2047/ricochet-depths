"""Rig a low-poly A-pose hero for Unity (Humanoid-compatible bone names) without Mixamo.

blender -b -P tools/rig_hero.py -- --in models/lowpoly/hero_tinker.glb --out models/rigged/hero_tinker \
    [--prop-move dx,dy,dz] [--views DIR]

Steps: import → merge UV-seam duplicates (geometry only; UVs live on loops, so nothing is lost) →
optional "prop" fix (the smaller disconnected island is moved by --prop-move and bound to the right hand) →
joints estimated from the mesh (legs and arms by side, spine from heights) → armature → bone-heat weights
(prop 100 % RightHand) → Idle / Walk / Run / Shoot / Hit / Death / Victory actions → <out>.fbx (Unity) + <out>.glb + preview renders.

Conventions as everywhere in pit-art: Z-up, the character faces −Y, origin at the feet. Character's
right side is −X. Bone names follow Unity's Humanoid auto-mapping (Hips, Spine, Chest, Neck, Head,
Left/RightUpperArm, …) so Mixamo or other humanoid clips retarget in Unity as well.
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
import monster_lib as ml  # noqa: E402  (render helpers only)

FPS = 30


def parse():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="src", required=True)
    ap.add_argument("--out", required=True, help="path without extension")
    ap.add_argument("--prop-move", default="", help="dx,dy,dz applied to the detached prop island")
    ap.add_argument("--views", default="", help="folder for check renders (rest + test poses)")
    ap.add_argument("--clip-frames", default="", help="folder: render every clip frame by frame (for GIFs)")
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def log(*a):
    print("[rig]", *a, flush=True)


# --------------------------------------------------------------------------------- mesh prep
def load(src):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    obj = [o for o in bpy.data.objects if o.type == "MESH"][0]
    for m in obj.data.materials:
        for n in (m.node_tree.nodes if m and m.node_tree else []):
            if n.type == "BSDF_PRINCIPLED":
                n.inputs["Metallic"].default_value = 0.0
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    n0 = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bm.to_mesh(obj.data)
    bm.free()
    log(f"merged seam duplicates {n0} -> {len(obj.data.vertices)} verts")
    return obj


def islands(me):
    adj = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        adj[a].append(b)
        adj[b].append(a)
    comp = [-1] * len(me.vertices)
    out = []
    for s in range(len(me.vertices)):
        if comp[s] >= 0:
            continue
        stack, cur = [s], []
        comp[s] = len(out)
        while stack:
            v = stack.pop()
            cur.append(v)
            for w in adj[v]:
                if comp[w] < 0:
                    comp[w] = len(out)
                    stack.append(w)
        out.append(cur)
    out.sort(key=len, reverse=True)
    return out


# ---------------------------------------------------------------------------------- joints
def estimate_joints(P):
    """P: (n,3) body verts. Returns {name: Vector} for an A-pose biped facing −Y."""
    zmax = P[:, 2].max()
    cx = np.median(P[:, 0])
    J = {}
    # legs: below the crotch the body splits in two columns; find the crotch as the highest z where
    # a thin slice around cx is empty. Only the back half counts (aprons/coats hang between the legs).
    back = P[P[:, 1] > np.median(P[:, 1])]
    crotch = 0.3 * zmax
    for z in np.linspace(0.05 * zmax, 0.6 * zmax, 60):
        sl = back[np.abs(back[:, 2] - z) < 0.01 * zmax]
        if len(sl) and np.any(np.abs(sl[:, 0] - cx) < 0.012 * zmax):
            break
        crotch = z
    hip_z = crotch + 0.02 * zmax
    for side, sgn in (("Left", 1), ("Right", -1)):
        leg = P[(P[:, 2] < crotch) & ((P[:, 0] - cx) * sgn > 0)]
        lx, ly = np.median(leg[:, 0]), np.median(leg[:, 1])
        ymin = leg[leg[:, 2] < 0.08 * zmax][:, 1].min()
        J[f"{side}UpperLeg"] = Vector((lx, ly, hip_z))
        J[f"{side}LowerLeg"] = Vector((lx, ly - 0.01 * zmax, hip_z * 0.5))
        J[f"{side}Foot"] = Vector((lx, ly, 0.08 * zmax))
        J[f"{side}Toes"] = Vector((lx, (ly + ymin) / 2, 0.025 * zmax))
        J[f"{side}ToesEnd"] = Vector((lx, ymin, 0.02 * zmax))
    # torso: shoulders = the height where the silhouette is widest above the hips, minus arm reach
    torso = P[np.abs(P[:, 0] - cx) < 0.12 * zmax]
    ty = np.median(torso[:, 1])
    head_bottom = None
    # neck: narrowest torso slice between 60 % and 85 % of the height
    best = (1e9, 0.72 * zmax)
    for z in np.linspace(0.6 * zmax, 0.85 * zmax, 40):
        sl = P[np.abs(P[:, 2] - z) < 0.012 * zmax]
        if len(sl) < 3:
            continue
        w = np.ptp(sl[np.abs(sl[:, 0] - cx) < 0.3 * zmax][:, 0]) if np.any(np.abs(sl[:, 0] - cx) < 0.3 * zmax) else 1e9
        if w < best[0]:
            best = (w, z)
    neck_z = best[1]
    head_bottom = neck_z
    J["Hips"] = Vector((cx, ty, hip_z))
    J["Spine"] = Vector((cx, ty, hip_z + (neck_z - hip_z) * 0.3))
    J["Chest"] = Vector((cx, ty, hip_z + (neck_z - hip_z) * 0.65))
    J["Neck"] = Vector((cx, ty, neck_z - 0.03 * zmax))
    J["Head"] = Vector((cx, ty, head_bottom + 0.02 * zmax))
    J["HeadEnd"] = Vector((cx, ty, zmax))
    # arms: verts outside the torso column and between hips and neck, per side; PCA axis
    shoulder_half = 0.0
    for side, sgn in (("Left", 1), ("Right", -1)):
        arm = P[((P[:, 0] - cx) * sgn > 0.17 * zmax) & (P[:, 2] > hip_z - 0.15 * zmax) & (P[:, 2] < neck_z)]
        c = arm.mean(0)
        u, s, vt = np.linalg.svd(arm - c)
        d = vt[0] if vt[0][0] * sgn > 0 else -vt[0]
        t = (arm - c) @ d
        tip = c + d * t.max()
        # shoulder joint: walk the axis back to where it meets the torso edge (|x - cx| ≈ 0.13 h)
        k = ((cx + sgn * 0.13 * zmax) - c[0]) / d[0]
        sh = c + d * k
        sh[2] = min(sh[2], neck_z - 0.06 * zmax)
        L = np.linalg.norm(tip - sh)
        dd = (tip - sh) / L
        J[f"{side}Shoulder"] = Vector((cx + sgn * 0.05 * zmax, ty, sh[2]))
        J[f"{side}UpperArm"] = Vector(sh)
        J[f"{side}LowerArm"] = Vector(sh + dd * L * 0.42)
        J[f"{side}Hand"] = Vector(sh + dd * L * 0.72)
        J[f"{side}HandEnd"] = Vector(tip)
        shoulder_half = max(shoulder_half, abs(sh[0] - cx))
    return J


BONES = [  # name, head joint, tail joint, parent
    ("Hips", "Hips", "Spine", None),
    ("Spine", "Spine", "Chest", "Hips"),
    ("Chest", "Chest", "Neck", "Spine"),
    ("Neck", "Neck", "Head", "Chest"),
    ("Head", "Head", "HeadEnd", "Neck"),
]
for S in ("Left", "Right"):
    BONES += [
        (f"{S}Shoulder", f"{S}Shoulder", f"{S}UpperArm", "Chest"),
        (f"{S}UpperArm", f"{S}UpperArm", f"{S}LowerArm", f"{S}Shoulder"),
        (f"{S}LowerArm", f"{S}LowerArm", f"{S}Hand", f"{S}UpperArm"),
        (f"{S}Hand", f"{S}Hand", f"{S}HandEnd", f"{S}LowerArm"),
        (f"{S}UpperLeg", f"{S}UpperLeg", f"{S}LowerLeg", "Hips"),
        (f"{S}LowerLeg", f"{S}LowerLeg", f"{S}Foot", f"{S}UpperLeg"),
        (f"{S}Foot", f"{S}Foot", f"{S}Toes", f"{S}LowerLeg"),
        (f"{S}Toes", f"{S}Toes", f"{S}ToesEnd", f"{S}Foot"),
    ]


def build_armature(J, name):
    arm = bpy.data.armatures.new(name + "_Armature")
    rig = bpy.data.objects.new(name + "_Rig", arm)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for bn, h, t, parent in BONES:
        b = arm.edit_bones.new(bn)
        b.head, b.tail = J[h], J[t]
        if (b.tail - b.head).length < 1e-3:
            b.tail = b.head + Vector((0, 0, 0.02))
        b.roll = 0.0
        if parent:
            b.parent = arm.edit_bones[parent]
            b.use_connect = (b.head - b.parent.tail).length < 1e-4
    # rolls: limbs bend around world X (legs) / the forward axis; keep Z of every bone pointing −Y (forward)
    for b in arm.edit_bones:
        b.align_roll(Vector((0, -1, 0)))
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def skin(obj, rig, prop_verts):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    vg = {g.name: g for g in obj.vertex_groups}
    unweighted = [v.index for v in obj.data.vertices if not any(g.weight > 1e-4 for g in v.groups)]
    if prop_verts:
        for g in obj.vertex_groups:
            g.remove(prop_verts)
        vg["RightHand"].add(prop_verts, 1.0, "REPLACE")
    unweighted = [i for i in unweighted if i not in set(prop_verts)]
    if unweighted:  # bone heat failed for some islands: nearest-bone fallback
        log(f"{len(unweighted)} verts without heat weights -> nearest bone")
        segs = [(b.name, b.head_local, b.tail_local) for b in rig.data.bones if not b.name.endswith("Shoulder")]
        for i in unweighted:
            p = obj.data.vertices[i].co
            best = min(segs, key=lambda s: _seg_dist(p, s[1], s[2]))
            vg[best[0]].add([i], 1.0, "REPLACE")


def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (a + ab * t - p).length


# ------------------------------------------------------------------------------- animation
def _rot_local(pb, axis_world, deg):
    """Rotation about a world (armature-space) axis through the joint, as the bone's local quaternion."""
    m = pb.bone.matrix_local.to_3x3()
    q = Quaternion(axis_world, math.radians(deg))
    return (m.inverted() @ q.to_matrix() @ m).to_quaternion()


def make_action(rig, name, frames, keys):
    """keys(f) -> {bone: [(axis, deg), ...] or ('loc', Vector)} evaluated at each frame in frames."""
    act = bpy.data.actions.new(name)
    rig.animation_data_create()
    rig.animation_data.action = act
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    for f in frames:
        pose = keys(f)
        for pb in rig.pose.bones:
            q = Quaternion()
            for ax, deg in pose.get(pb.name, {}).get("rot", []):
                q = _rot_local(pb, Vector(ax), deg) @ q
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=f)
            loc = pose.get(pb.name, {}).get("loc")
            if pb.name == "Hips":
                m = pb.bone.matrix_local.to_3x3()
                pb.location = m.inverted() @ Vector(loc or (0, 0, 0))
                pb.keyframe_insert("location", frame=f)
    act.use_fake_user = True
    track = rig.animation_data.nla_tracks.new()
    track.name = name
    track.strips.new(name, frames[0], act)
    rig.animation_data.action = None
    return act


X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)


def idle(f):  # 60 frames: breathing bob and a little arm sway
    s = math.sin(2 * math.pi * f / 60)
    return {
        "Hips": {"loc": (0, 0, -0.006 * (1 - s) / 2)},
        "Spine": {"rot": [(X, -1.5 * s)]},
        "Chest": {"rot": [(X, -1.5 * s)]},
        "Head": {"rot": [(X, 1.5 * s), (Z, 2 * math.sin(2 * math.pi * f / 120))]},
        "LeftUpperArm": {"rot": [(Y, -3 * s)]},
        "RightUpperArm": {"rot": [(Y, 3 * s)]},
        "LeftLowerArm": {"rot": [(X, -5 - 3 * s)]},
        "RightLowerArm": {"rot": [(X, -8 - 3 * s)]},
    }


def walk(f):  # 24 frames per cycle (0.8 s at 30 fps). Sign rule: +deg about X swings a downward bone
    # (leg, arm) backward (+Y) and leans an upward bone (spine, head) forward (−Y).
    p = 2 * math.pi * f / 24
    s, c = math.sin(p), math.cos(p)
    knee = lambda ph: max(0.0, math.sin(ph)) * 45  # noqa: E731
    return {
        "Hips": {"loc": (0, 0, -0.02 * abs(c)), "rot": [(Z, 6 * s)]},
        "Spine": {"rot": [(Z, -4 * s)]},
        "Chest": {"rot": [(X, 4), (Z, -4 * s)]},  # +X on an upward bone = lean forward
        "LeftUpperLeg": {"rot": [(X, -28 * s)]},
        "RightUpperLeg": {"rot": [(X, 28 * s)]},
        "LeftLowerLeg": {"rot": [(X, knee(p + math.pi / 2))]},
        "RightLowerLeg": {"rot": [(X, knee(p - math.pi / 2))]},
        "LeftFoot": {"rot": [(X, -10 * c)]},
        "RightFoot": {"rot": [(X, 10 * c)]},
        # arms first drop from the A-pose toward the body (about Y), then swing opposite to the legs
        "LeftUpperArm": {"rot": [(Y, 28), (X, 34 * s)]},
        "RightUpperArm": {"rot": [(Y, -28), (X, -34 * s)]},
        "LeftLowerArm": {"rot": [(X, -18 - 10 * max(0.0, -s))]},
        "RightLowerArm": {"rot": [(X, -18 - 10 * max(0.0, s))]},
        "Head": {"rot": [(Z, 3 * s)]},
    }


def run(f):  # 16 frames per cycle (0.53 s at 30 fps): forward lean, high knees, big bent-elbow arm pump
    p = 2 * math.pi * f / 16
    s, c = math.sin(p), math.cos(p)
    knee = lambda ph: 20 + max(0.0, math.sin(ph)) * 70  # noqa: E731
    return {
        "Hips": {"loc": (0, 0, 0.035 * abs(s) - 0.03), "rot": [(Z, 9 * s)]},
        "Spine": {"rot": [(X, 6), (Z, -6 * s)]},
        "Chest": {"rot": [(X, 8), (Z, -8 * s)]},
        "Neck": {"rot": [(X, -6)]},
        "Head": {"rot": [(X, -4), (Z, 4 * s)]},
        "LeftUpperLeg": {"rot": [(X, -42 * s - 8)]},
        "RightUpperLeg": {"rot": [(X, 42 * s - 8)]},
        "LeftLowerLeg": {"rot": [(X, knee(p + math.pi / 2))]},
        "RightLowerLeg": {"rot": [(X, knee(p - math.pi / 2))]},
        "LeftFoot": {"rot": [(X, -18 * c)]},
        "RightFoot": {"rot": [(X, 18 * c)]},
        "LeftUpperArm": {"rot": [(Y, 40), (X, 55 * s)]},
        "RightUpperArm": {"rot": [(Y, -40), (X, -55 * s)]},
        "LeftLowerArm": {"rot": [(X, -70 - 15 * max(0.0, -s))]},
        "RightLowerArm": {"rot": [(X, -70 - 15 * max(0.0, s))]},
    }


def shoot(f):  # 18 frames: raise the launcher arm forward, recoil, settle
    t = f / 18
    raise_ = math.sin(min(t / 0.35, 1) * math.pi / 2) if t < 0.7 else max(0.0, 1 - (t - 0.7) / 0.3)
    recoil = math.exp(-((t - 0.4) / 0.06) ** 2)
    return {
        "Chest": {"rot": [(Z, 10 * raise_), (X, -4 * recoil)]},
        "RightUpperArm": {"rot": [(X, -70 * raise_ + 12 * recoil), (Y, -25 * raise_)]},
        "RightLowerArm": {"rot": [(X, -15 * raise_)]},
        "LeftUpperArm": {"rot": [(X, 10 * raise_)]},
        "Head": {"rot": [(Z, 8 * raise_)]},
    }


def hit(f):  # 12 frames: flinch back, recover
    e = math.sin(min(f / 3, 1) * math.pi / 2) * (1 - max(0, f - 3) / 9)
    return {
        "Hips": {"loc": (0, 0.02 * e, -0.01 * e)},
        "Spine": {"rot": [(X, -8 * e)]},
        "Chest": {"rot": [(X, -10 * e), (Z, -6 * e)]},
        "Head": {"rot": [(X, -14 * e)]},
        "LeftUpperArm": {"rot": [(Y, -20 * e), (X, -10 * e)]},
        "RightUpperArm": {"rot": [(Y, 20 * e), (X, -10 * e)]},
        "LeftUpperLeg": {"rot": [(X, -6 * e)]},
        "RightUpperLeg": {"rot": [(X, 8 * e)]},
    }


def death(f):  # 30 frames: stagger, fall backwards, settle flat (last frame holds)
    t = min(f / 22, 1.0)
    fall = t * t * (3 - 2 * t)
    sag = math.sin(min(f / 6, 1) * math.pi / 2)
    bounce = 0.015 * math.sin(max(0, f - 22) / 8 * math.pi) if f > 22 else 0
    return {
        "Hips": {"loc": (0, 0.1 * fall, -0.12 * fall + bounce), "rot": [(X, -86 * fall)]},
        "Spine": {"rot": [(X, 6 * sag - 6 * fall)]},
        "Chest": {"rot": [(X, 6 * sag - 4 * fall)]},
        "Head": {"rot": [(X, 14 * sag - 18 * fall), (Z, 25 * fall)]},
        "LeftUpperLeg": {"rot": [(X, -12 * fall)]},
        "RightUpperLeg": {"rot": [(X, -25 * fall)]},
        "LeftLowerLeg": {"rot": [(X, 30 * sag - 10 * fall)]},
        "RightLowerLeg": {"rot": [(X, 35 * fall)]},
        "LeftUpperArm": {"rot": [(Y, -60 * fall)]},
        "RightUpperArm": {"rot": [(Y, 60 * fall)]},
    }


def victory(f):  # 30 frames: two hops with both arms up, launcher raised
    hop = abs(math.sin(2 * math.pi * f / 30))
    up = math.sin(min(f / 5, 1) * math.pi / 2)
    return {
        "Hips": {"loc": (0, 0, 0.07 * hop - 0.02 * (1 - hop))},
        "Chest": {"rot": [(X, -5 * up)]},
        "Head": {"rot": [(X, -10 * up)]},
        "LeftUpperArm": {"rot": [(Y, -105 * up), (X, -10 * hop)]},
        "RightUpperArm": {"rot": [(Y, 105 * up), (X, -10 * hop)]},
        "LeftLowerArm": {"rot": [(X, -25 * up)]},
        "RightLowerArm": {"rot": [(X, -25 * up)]},
        "LeftUpperLeg": {"rot": [(X, -15 * (1 - hop))]},
        "RightUpperLeg": {"rot": [(X, -15 * (1 - hop))]},
        "LeftLowerLeg": {"rot": [(X, 25 * (1 - hop))]},
        "RightLowerLeg": {"rot": [(X, 25 * (1 - hop))]},
        "LeftFoot": {"rot": [(X, -10 * (1 - hop))]},
        "RightFoot": {"rot": [(X, -10 * (1 - hop))]},
    }


CLIPS = [  # name, fn, frames (keys), loops
    ("Idle", idle, list(range(0, 61, 2)), True),
    ("Walk", walk, list(range(0, 25, 2)), True),
    ("Run", run, list(range(0, 17, 2)), True),
    ("Shoot", shoot, list(range(0, 19)), False),
    ("Hit", hit, list(range(0, 13)), False),
    ("Death", death, list(range(0, 31)), False),
    ("Victory", victory, list(range(0, 31, 2)), True),
]


def render_clips(rig, folder):
    """Every clip as PNG frames (3/4 view from the front-right, like the concept images) for GIFs."""
    sc = ml._setup_render(320, 320)
    cam = bpy.data.objects.new("GifCam", bpy.data.cameras.new("GifCam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 1.5
    sc.collection.objects.link(cam)
    cam.location = (1.7, -2.6, 1.3)
    tgt = bpy.data.objects.new("GifT", None)
    sc.collection.objects.link(tgt)
    tgt.location = (0, 0.1, 0.42)
    con = cam.constraints.new("TRACK_TO")
    con.target = tgt
    sc.camera = cam
    for t in rig.animation_data.nla_tracks:
        t.mute = True
    for name, _, frames, _ in CLIPS:
        rig.animation_data.action = bpy.data.actions[name]
        os.makedirs(os.path.join(folder, name), exist_ok=True)
        for f in range(frames[0], frames[-1] + 1):
            sc.frame_set(f)
            sc.render.filepath = os.path.join(folder, name, f"{f:03d}.png")
            bpy.ops.render.render(write_still=True)
    rig.animation_data.action = None
    for t in rig.animation_data.nla_tracks:
        t.mute = False
    log("clip frames in", folder)


# ---------------------------------------------------------------------------------- output
def export(obj, rig, out):
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=out + ".fbx", use_selection=True, object_types={"MESH", "ARMATURE"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", add_leaf_bones=False,
        path_mode="COPY", embed_textures=True, mesh_smooth_type="FACE",
        bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    )
    bpy.ops.export_scene.gltf(
        filepath=out + ".glb", export_format="GLB", use_selection=True, export_skins=True,
        export_animations=True, export_animation_mode="NLA_TRACKS", export_materials="EXPORT",
    )
    log("wrote", out + ".fbx", f"({os.path.getsize(out + '.fbx') / 1024:.0f} KB)",
        out + ".glb", f"({os.path.getsize(out + '.glb') / 1024:.0f} KB)")


def render_views(obj, rig, folder):
    os.makedirs(folder, exist_ok=True)
    sc = ml._setup_render(640, 640)
    rig.show_in_front = True
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 1.15
    sc.collection.objects.link(cam)
    tgt = bpy.data.objects.new("T", None)
    sc.collection.objects.link(tgt)
    tgt.location = (0, 0, 0.5)
    con = cam.constraints.new("TRACK_TO")
    con.target = tgt
    sc.camera = cam
    shots = [("rest_front", None, 0, (0, -3, 0.5)), ("rest_side", None, 0, (-3, 0, 0.5)),
             ("walk_a", "Walk", 6, (1.6, -2.4, 1.2)), ("walk_b", "Walk", 18, (1.6, -2.4, 1.2)),
             ("walk_side", "Walk", 6, (-3, 0, 0.5)), ("shoot", "Shoot", 7, (-1.2, -2.6, 1.0)), ("shoot_side", "Shoot", 7, (-3, 0, 0.5)),
             ("idle", "Idle", 15, (1.6, -2.4, 1.2))]
    for name, act, frame, loc in shots:
        rig.animation_data.action = bpy.data.actions[act] if act else None
        for t in rig.animation_data.nla_tracks:
            t.mute = True
        if not act:
            for pb in rig.pose.bones:
                pb.rotation_quaternion = Quaternion()
                pb.location = Vector()
        sc.frame_set(frame)
        cam.location = loc
        sc.render.filepath = os.path.join(folder, f"{name}.png")
        bpy.ops.render.render(write_still=True)
    rig.animation_data.action = None
    for t in rig.animation_data.nla_tracks:
        t.mute = False
    log("views in", folder)


def main():
    a = parse()
    obj = load(a.src)
    isl = islands(obj.data)
    prop = []
    if len(isl) > 1 and a.prop_move:
        prop = [i for comp in isl[1:] for i in comp]
        dv = Vector([float(x) for x in a.prop_move.split(",")])
        for i in prop:
            obj.data.vertices[i].co += dv
        log(f"moved prop island ({len(prop)} verts) by {tuple(dv)}")
    body = np.array([obj.data.vertices[i].co[:] for i in isl[0]])
    J = estimate_joints(body)
    for k in ("Hips", "Neck", "Head", "RightUpperArm", "RightHand", "LeftUpperLeg", "LeftFoot"):
        log(f"joint {k:14s} {tuple(round(c, 3) for c in J[k])}")
    key = os.path.basename(a.out)
    rig = build_armature(J, key)
    skin(obj, rig, prop)
    for p in obj.data.polygons:
        p.use_smooth = True  # heroes: smooth normals, facets come from the shader (art-bible §9)
    for name, fn, frames, _ in CLIPS:
        make_action(rig, name, frames, fn)
    sc = bpy.context.scene
    sc.render.fps = FPS
    if a.views:
        render_views(obj, rig, a.views)
    if a.clip_frames:
        render_clips(rig, a.clip_frames)
    export(obj, rig, a.out)
    log(f"{len(rig.data.bones)} bones, {len(obj.data.polygons)} tris")


main()
