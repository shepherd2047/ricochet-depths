"""Spitter (art-bible §5, shooter, 1×1): squat brick chimney-pot with a flue for a head; glowing flue mouth.

Run: blender -b -P pit-art/tools/monsters/spitter.py
Set SPITTER_VIEWS=<dir> to also render a front view and a high 3/4 game-camera view into <dir>.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bmesh  # noqa: E402
import bpy  # noqa: E402
import monster_lib as ml  # noqa: E402

ml.reset(seed=3)

BRICK = ml.tile("brick")
ROCK = ml.tile("rock")


def quad(name, pts, paint):
    """Single quad with the winding given (CCW seen from outside)."""
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    bm.faces.new(vs)
    return ml._new_obj(name, bm, paint)


def open_box(name, center, size, paint, taper=1.0):
    """Box without the bottom face (sits on the ground / inside the body): 5 faces, 20 corners."""
    cx, cy, cz = center
    sx, sy, sz = (s / 2 for s in size)
    tx, ty = sx * taper, sy * taper
    bm = bmesh.new()
    b = [bm.verts.new((cx + x, cy + y, cz - sz)) for x, y in ((-sx, -sy), (sx, -sy), (sx, sy), (-sx, sy))]
    t = [bm.verts.new((cx + x, cy + y, cz + sz)) for x, y in ((-tx, -ty), (tx, -ty), (tx, ty), (-tx, ty))]
    bm.faces.new(t)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((b[i], b[j], t[j], t[i]))
    return ml._new_obj(name, bm, paint)


def retile(obj, tile_name, unit_w, fy_span, rows=None):
    """Re-map every face onto a tile: horizontally one tile-quarter per unit_w metres (bricks keep
    their real width on narrow and wide faces), vertically each face spans fy_span of the tile."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.verify()
    bm.normal_update()
    for f in bm.faces:
        if rows is not None and not rows(f):
            continue
        t, b = ml._face_axes(f.normal)
        st = [(l.vert.co.dot(t), l.vert.co.dot(b)) for l in f.loops]
        s0, s1 = min(p[0] for p in st), max(p[0] for p in st)
        q0, q1 = min(p[1] for p in st), max(p[1] for p in st)
        k = min(1.0, 0.25 * (s1 - s0) / unit_w) / ((s1 - s0) or 1e-6)
        for l, (s_, q_) in zip(f.loops, st):
            l[uv].uv = ml._rect_uv(ml.TILES[tile_name], (s_ - s0) * k, (q_ - q0) / ((q1 - q0) or 1e-6) * fy_span)
    bm.to_mesh(obj.data)
    bm.free()


# --- barrel body: brick, 8 sides, no bottom cap (never seen); 2 brick courses per band ---
body = ml.lathe("body", [(0.17, 0.29), (0.29, 0.375), (0.43, 0.395), (0.56, 0.37), (0.65, 0.325)],
                sides=8, paint=BRICK, cap_top=False, cap_bottom=False)
retile(body, "brick", 0.13, 0.5)
# rim: slopes inward/down to the soot ledge the flue rises from
ml.lathe("body_rim", [(0.65, 0.325), (0.62, 0.24)], sides=8, paint=ml.swatch("brick"),
         top=ml.swatch("body_dark"), cap_bottom=False)

# --- flue neck (rock), lip (iron band) and the hot mouth ---
ml.lathe("flue_neck", [(0.60, 0.15), (0.84, 0.15)], sides=8, paint=ROCK,
         cap_top=False, cap_bottom=False)
ml.lathe("flue_lip_under", [(0.84, 0.15), (0.87, 0.20)], sides=8, paint=ml.swatch("body_dark"),
         cap_top=False, cap_bottom=False)
lip = ml.lathe("flue_lip", [(0.87, 0.20), (0.97, 0.20)], sides=8, paint=ml.tile("rivets", "fit"),
               cap_top=False, cap_bottom=False)
retile(lip, "rivets", 0.075, 1.0)
ml.lathe("flue_rim", [(0.97, 0.20), (0.97, 0.135)], sides=8, paint=ml.swatch("iron_light"),
         cap_top=False, cap_bottom=False)
ml.lathe("flue_throat", [(0.97, 0.135), (0.91, 0.135)], sides=8, paint=ml.swatch("ember"),
         top=ml.swatch("glow_amber"), cap_bottom=False)

# --- eyes: amber dots on the flue front face ---
fy = -0.15 * math.cos(math.pi / 8) - 0.004
for sx in (-1, 1):
    x0, x1 = sx * 0.035, sx * 0.075
    lo, hi = min(x0, x1), max(x0, x1)
    quad(f"eye_{sx}", [(lo, fy, 0.72), (hi, fy, 0.72), (hi, fy, 0.76), (lo, fy, 0.76)],
         ml.swatch("glow_amber"))

# --- a tar drip running from the rim down the brick shoulder (tetra, 12 corners) ---
ml.mesh("tar_drip", [(0.055, -0.312, 0.662), (0.105, -0.312, 0.662), (0.08, -0.28, 0.662),
                     (0.082, -0.351, 0.54)], [(0, 1, 2), (0, 1, 3), (1, 2, 3), (2, 0, 3)],
        paint=ml.swatch("tar"))

# --- stubby block legs ---
leg = open_box("leg_l", (-0.14, -0.01, 0.11), (0.16, 0.18, 0.22), ROCK, taper=0.95)
ml.mirror_x(leg, "leg_r")

obj = ml.finish("spitter")

views = os.environ.get("SPITTER_VIEWS")
if views:
    os.makedirs(views, exist_ok=True)
    sc = bpy.context.scene
    cam = sc.camera
    cam.constraints.clear()
    tgt_z = obj.dimensions.z * 0.5
    for tag, elev, azim, dist in (("front", 8, 0, 3.0), ("high34", 50, 30, 3.2), ("top", 80, 10, 3.0),
                                  ("back", 25, 200, 3.0)):
        e, a = math.radians(elev), math.radians(azim)
        cam.location = (dist * math.cos(e) * math.sin(a), -dist * math.cos(e) * math.cos(a),
                        tgt_z + dist * math.sin(e))
        d = bpy.data.objects["T"].location = (0, 0, tgt_z)
        c = cam.constraints.new("TRACK_TO")
        c.target = bpy.data.objects["T"]
        sc.render.filepath = os.path.join(views, f"spitter_{tag}.png")
        bpy.ops.render.render(write_still=True)
        cam.constraints.clear()
        print("[monster] view", sc.render.filepath)
