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
