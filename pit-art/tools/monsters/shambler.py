"""Shambler (art-bible §5, basic 1x1): walking lump of cooled slag shaped like an upturned pot,
two stubby legs, crust plates on top, two amber eye slits.

Run:  blender -b -P pit-art/tools/monsters/shambler.py
Set SHAMBLER_VIEWS=<dir> to also render a front view and a high 3/4 (game camera) view there.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import monster_lib as ml  # noqa: E402

ml.reset(seed=3)
ROCK = ml.tile("rock")
ROCK_FIT = ml.tile("rock", "fit")


def plate(name, center, normal, pts, thick=0.05, inset=0.75, spin=0.0, paint=ROCK):
    """Crust plate: polygon top (pts in local XY), sides sloping down to an inset rim, no bottom.
    Local +Z is aimed along `normal`; the part's bottom sinks into the body."""
    bm = bmesh.new()
    top = [bm.verts.new((x, y, thick * 0.5)) for x, y in pts]
    bot = [bm.verts.new((x * inset, y * inset, -thick * 0.5)) for x, y in pts]
    n = len(pts)
    bm.faces.new(top)
    for j in range(n):
        k = (j + 1) % n
        bm.faces.new((bot[j], bot[k], top[k], top[j]))
    q = Vector(normal).normalized().to_track_quat("Z", "Y")
    m = Matrix.Translation(Vector(center)) @ q.to_matrix().to_4x4() @ Matrix.Rotation(spin, 4, "Z")
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return ml._new_obj(name, bm, paint)


def block(name, center, size, paint, rot=(0, 0, 0), taper=1.0, skip="top"):
    """Box without the hidden face (`skip`: 'top' for legs, 'back' for eyes). 5 faces, 20 corners."""
    bm = bmesh.new()
    sx, sy, sz = (s / 2 for s in size)
    v = [bm.verts.new(p) for p in [(-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
                                   (-sx * taper, -sy * taper, sz), (sx * taper, -sy * taper, sz),
                                   (sx * taper, sy * taper, sz), (-sx * taper, sy * taper, sz)]]
    faces = {"bottom": (0, 3, 2, 1), "top": (4, 5, 6, 7), "front": (0, 1, 5, 4),
             "back": (2, 3, 7, 6), "left": (3, 0, 4, 7), "right": (1, 2, 6, 5)}
    for key, f in faces.items():
        if key != skip:
            bm.faces.new([v[i] for i in f])
    from mathutils import Euler
    m = Matrix.Translation(Vector(center)) @ Euler(rot).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return ml._new_obj(name, bm, paint)


# --- body: upturned pot. Round belly, pinched neck, flat lip on top ----------------------------
PROFILE = [(0.15, 0.25), (0.28, 0.41), (0.55, 0.41), (0.73, 0.315), (0.77, 0.375), (0.86, 0.35)]
ml.lathe("body", PROFILE, sides=8, paint=ROCK, top=ROCK_FIT, bottom=ml.swatch("body_dark"),
         jitter=0.03)

# --- legs: two short blocks, slightly splayed --------------------------------------------------
leg = block("leg_l", (-0.15, 0.0, 0.11), (0.19, 0.21, 0.22), ml.swatch("body_mid"),
            rot=(0, math.radians(-4), 0), taper=0.9)
ml.mirror_x(leg)

# --- face: two angry amber slits, inner ends lower, under heavy brow plates -----------------------
EYE_Z, EYE_X, TILT = 0.49, 0.12, math.radians(15)
eye = block("eye_l", (-EYE_X, -0.355, EYE_Z), (0.16, 0.05, 0.06), ml.swatch("glow_amber"),
            rot=(0, TILT, 0), skip="back")
ml.mirror_x(eye)
brow = ml.wedge("brow_l", (-EYE_X, -0.36, EYE_Z + 0.06), (0.18, 0.09, 0.07), ml.swatch("body_mid"),
                rot=(math.radians(80), TILT, 0))
ml.mirror_x(brow)

# --- crust plates on the shoulders / top ------------------------------------------------------
QUAD = [(-0.12, -0.08), (0.10, -0.11), (0.13, 0.06), (-0.04, 0.11)]
PENT = [(-0.10, -0.08), (0.07, -0.11), (0.13, 0.03), (0.02, 0.12), (-0.11, 0.06)]
for i, (ang, z, r, tilt, shape, sc, spin) in enumerate([
        (225, 0.36, 0.385, 1.35, QUAD, 0.95, 0.2),   # front-left cheek
        (315, 0.36, 0.385, 1.35, QUAD, 0.95, -0.3),  # front-right cheek
        (180, 0.63, 0.345, 1.12, QUAD, 1.05, 0.3),    # left shoulder
        (0, 0.63, 0.345, 1.12, QUAD, 1.05, -0.4)]):    # right shoulder
    a = math.radians(ang)
    n = (math.cos(a) * math.sin(tilt), math.sin(a) * math.sin(tilt), math.cos(tilt))
    plate(f"crust_{i}", (math.cos(a) * r, math.sin(a) * r, z), n,
          [(px * sc, py * sc) for px, py in shape], spin=spin)
plate("crust_top", (-0.03, 0.04, 0.862), (-0.05, 0.04, 1), [(px * 1.5, py * 1.5) for px, py in QUAD],
      thick=0.04, spin=0.5)

obj = ml.finish("shambler")

# --- optional check renders ----------------------------------------------------------------------
views = os.environ.get("SHAMBLER_VIEWS")
if views:
    os.makedirs(views, exist_ok=True)
    sc = bpy.context.scene
    tgt = bpy.data.objects.new("T2", None)
    sc.collection.objects.link(tgt)
    tgt.location = (0, 0, 0.42)
    for vname, loc in [("front", (0, -3.0, 0.45)),
                       ("high34", (1.0, -1.7, 2.35)),  # ~50 deg down
                       ("top", (0, -0.3, 3.2)),
                       ("back", (-1.2, 2.4, 1.4))]:
        cam = bpy.data.objects.new("Cam_" + vname, bpy.data.cameras.new("Cam_" + vname))
        cam.data.lens = 50
        sc.collection.objects.link(cam)
        cam.location = loc
        c = cam.constraints.new("TRACK_TO")
        c.target = tgt
        sc.camera = cam
        sc.render.filepath = os.path.join(views, f"shambler_{vname}.png")
        bpy.ops.render.render(write_still=True)
        print("[monster] view", sc.render.filepath)


# --- rig + clips (MONSTER_RIG=1; MONSTER_CLIPS=<dir> also renders every clip frame) ----------------
def _ease(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def _track(t, keys):
    """keys [(t, value or tuple), ...] sorted by t → eased value at t (held outside the range)."""
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            u = _ease((t - t0) / (t1 - t0))
            if isinstance(v0, tuple):
                return tuple(a + (b - a) * u for a, b in zip(v0, v1))
            return v0 + (v1 - v0) * u
    return keys[-1][1]


def _leg(deg, lift=0.0):
    """Leg swing about the hip + the rise that keeps the block's lowest corner on the floor."""
    a = math.radians(abs(deg))
    rise = 0.22 * math.cos(a) + 0.105 * math.sin(a) - 0.22
    return {"rot": [(ml.WX, deg)], "loc": (0, 0, max(0.0, rise) + lift)}


def _sq(z):
    """Volume-keeping squash/stretch: z scale → (x, y, z)."""
    xy = 1.0 / math.sqrt(z)
    return (xy, xy, z)


def _floor_check(obj, rig):
    """Print each clip's lowest vertex z (should stay ~0: nothing sinks under the floor)."""
    sc = bpy.context.scene
    dg = bpy.context.evaluated_depsgraph_get()
    for tr in rig.animation_data.nla_tracks:
        tr.mute = True
    for tr in rig.animation_data.nla_tracks:
        rig.animation_data.action = bpy.data.actions[tr.name]
        lo, hi = (int(x) for x in rig.animation_data.action.frame_range)
        worst = []
        for f in range(lo, hi + 1, ml.KEY_STEP):
            sc.frame_set(f)
            me = obj.evaluated_get(dg).to_mesh()
            worst.append(round(min((obj.matrix_world @ v.co).z for v in me.vertices), 3))
            obj.evaluated_get(dg).to_mesh_clear()
        print(f"[monster] floor {tr.name}: min z per pose {worst}")
    rig.animation_data.action = None
    for tr in rig.animation_data.nla_tracks:
        tr.mute = False


if os.environ.get("MONSTER_RIG"):
    print("[monster] parts", ml.part_names(obj))
    for p in ("body", "leg_l", "eye_l", "brow_l", "crust_top"):
        lo, hi = ml.part_bounds(obj, p)
        print(f"[monster] {p}: {tuple(round(v, 3) for v in lo)} .. {tuple(round(v, 3) for v in hi)}")
    HIP = 0.22
    bones = [
        ("Root", (0, 0, 0), (0, 0.15, 0), None),
        ("Body", (0, 0, HIP), (0, 0, 0.86), "Root"),
        ("LegL", (-0.15, 0, HIP), (-0.15, 0, 0.0), "Root"),
        ("LegR", (0.15, 0, HIP), (0.15, 0, 0.0), "Root"),
        ("Face", (0, -0.33, 0.47), (0, -0.33, 0.6), "Body"),
        ("Crust", (0, 0, 0.6), (0, 0, 0.86), "Body"),
    ]
    assign = {"leg_l": "LegL", "leg_l_r": "LegR"}
    for p in ml.part_names(obj):
        if p.startswith(("eye", "brow")):
            assign[p] = "Face"
        elif p.startswith("crust"):
            assign[p] = "Crust"
    rig = ml.make_rig(obj, bones, assign)
    TAU = 2 * math.pi

    def idle(t):
        s = math.sin(TAU * t)
        z = 1 + 0.06 * s
        return {
            "Body": {"scale": _sq(z), "rot": [(ml.WY, 2.0 * math.sin(TAU * t + 0.8)), (ml.WX, 1.5 + 1.5 * s)]},
            "Face": {"loc": (0, 0, -0.012 * math.sin(TAU * t - 0.9)),
                     "rot": [(ml.WX, 3 * math.sin(TAU * t - 0.9))]},
            "Crust": {"loc": (0, 0, 0.01 * math.sin(TAU * t - 1.3))},
        }

    def move(t):
        p = TAU * t
        fl, fr = math.sin(p), -math.sin(p)              # + = leg forward
        ll, lr = max(0.0, math.cos(p)), max(0.0, -math.cos(p))  # lift while swinging forward
        plant = math.sin(p) ** 2                         # 1 at each foot plant
        z = 1 - 0.08 * plant                             # squash on each plant, stretch while passing
        return {
            "Body": {"scale": _sq(z), "loc": (0, 0, 0.03 * (1 - plant) - 0.012),
                     "rot": [(ml.WX, 5), (ml.WY, -7 * math.cos(p)), (ml.WZ, -6 * math.sin(p))]},
            "LegL": _leg(-26 * fl, 0.04 * ll),
            "LegR": _leg(-26 * fr, 0.04 * lr),
            "Face": {"loc": (0, 0, -0.012 * plant)},
            "Crust": {"loc": (0, 0, 0.018 * plant)},
        }

    def attack(t):
        lean = _track(t, [(0, 0), (0.35, -16), (0.5, 32), (0.68, 30), (1, 0)])
        z = _track(t, [(0, 1), (0.35, 0.82), (0.5, 1.16), (0.6, 0.9), (0.75, 0.96), (1, 1)])
        dy = _track(t, [(0, 0), (0.35, 0.05), (0.5, -0.14), (0.7, -0.12), (1, 0)])
        dz = _track(t, [(0, 0), (0.35, -0.03), (0.5, 0.02), (0.6, -0.03), (1, 0)])
        legs = _track(t, [(0, 0), (0.35, -8), (0.5, 18), (0.7, 14), (1, 0)])  # + = swing back (push)
        brow = _track(t, [(0, 0), (0.3, -0.03), (0.5, 0.0), (1, 0)])
        crust = _track(t, [(0, 0), (0.5, 0), (0.58, 0.05), (0.7, 0), (1, 0)])
        return {
            "Body": {"scale": _sq(z), "loc": (0, dy, dz), "rot": [(ml.WX, lean)]},
            "LegL": _leg(legs),
            "LegR": _leg(legs * 0.6),
            "Face": {"loc": (0, 0, brow), "scale": _sq(1 + brow * 3)},
            "Crust": {"loc": (0, 0, crust)},
        }

    def hit(t):
        lean = _track(t, [(0, 0), (0.2, -18), (0.45, 8), (0.7, -4), (1, 0)])
        z = _track(t, [(0, 1), (0.2, 0.8), (0.45, 1.08), (0.7, 0.97), (1, 1)])
        dy = _track(t, [(0, 0), (0.2, 0.07), (0.6, 0.01), (1, 0)])
        crust = _track(t, [(0, 0), (0.2, 0.04), (0.45, -0.01), (1, 0)])
        return {
            "Body": {"scale": _sq(z), "loc": (0, dy, 0), "rot": [(ml.WX, lean), (ml.WY, lean * 0.3)]},
            "Face": {"loc": (0, 0, _track(t, [(0, 0), (0.2, 0.02), (0.5, 0), (1, 0)]))},
            "Crust": {"loc": (0, 0, crust)},
        }

    def death(t):
        lean = _track(t, [(0, 0), (0.15, -14), (0.3, 6), (0.6, 30), (1, 8)])
        roll = _track(t, [(0, 0), (0.15, 8), (0.3, -10), (0.6, -14), (1, -5)])
        z = _track(t, [(0, 1), (0.15, 0.88), (0.3, 1.05), (0.6, 0.6), (0.85, 0.36), (1, 0.34)])
        sink = _track(t, [(0, 0), (0.3, 0), (0.6, 0.06), (1, -0.02)])
        shrink = _track(t, [(0, 1), (0.5, 1), (1, 0.5)])
        legs = _track(t, [(0, 0), (0.15, -10), (0.3, 6), (0.65, 0), (1, 0)])
        pop = _track(t, [(0, 0), (0.3, 0), (0.45, 0.16), (0.65, 0.05), (1, -0.02)])
        crust_s = _track(t, [(0, 1), (0.45, 1.15), (0.75, 0.5), (1, 0.3)])
        face = _track(t, [(0, 1), (0.6, 1), (0.85, 0.2), (1, 0.05)])
        xy = 1 / math.sqrt(z)
        return {
            "Root": {"scale": (shrink, shrink, shrink)},
            "Body": {"scale": (xy, xy, z), "loc": (0, -0.04 * _ease(t / 0.65), sink),
                     "rot": [(ml.WX, lean), (ml.WY, roll)]},
            "LegL": _leg(legs),
            "LegR": _leg(-legs),
            "Face": {"scale": (face, face, face)},
            "Crust": {"loc": (0, 0, pop), "scale": (crust_s, crust_s, crust_s),
                      "rot": [(ml.WX, -25 * _ease((t - 0.3) / 0.3))]},
        }

    ml.add_clip(rig, "Idle", 40, idle, loop=True)
    ml.add_clip(rig, "Move", 16, move, loop=True)
    ml.add_clip(rig, "Attack", 16, attack, loop=False)
    ml.add_clip(rig, "Hit", 10, hit, loop=False)
    ml.add_clip(rig, "Death", 20, death, loop=False)
    _floor_check(obj, rig)
    ml.export_rigged(obj, rig, "shambler")
    clips_dir = os.environ.get("MONSTER_CLIPS")
    if clips_dir:
        ml.render_clips(rig, clips_dir)
