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

# ======================================================================= rig + clips (MONSTER_RIG=1)
# Bones (all children of Root, so no non-uniform scale is ever inherited through a rotated bone —
# Unity and Blender then agree): Body (pivot at the barrel's bottom, so squash keeps it seated on the
# legs), Flue (pivot at the flue base inside the rim: bends/aims/puffs) and Drip ride on the Body pose
# via follow(); LegL/LegR hang from the hips. Release frame of Attack = frame 8.
if os.environ.get("MONSTER_RIG"):
    S = math.sin
    TAU = 2 * math.pi

    def sq(k):
        """Volume-keeping squash/stretch: height k, width 1/sqrt(k)."""
        w = 1 / math.sqrt(k)
        return (w, w, k)

    def keys(t, pts):
        """Piecewise-linear lookup over [(t, value), ...] (values float or tuples)."""
        if t <= pts[0][0]:
            return pts[0][1]
        for (t0, a), (t1, b) in zip(pts, pts[1:]):
            if t <= t1:
                u = (t - t0) / ((t1 - t0) or 1)
                if isinstance(a, tuple):
                    return tuple(x + (y - x) * u for x, y in zip(a, b))
                return a + (b - a) * u
        return pts[-1][1]

    from mathutils import Matrix, Quaternion, Vector

    PIVOT = Vector((0, 0, 0.17))

    def follow(head, body, own_rot=(), own_loc=(0, 0, 0)):
        """Rot/loc for a Root-child bone at `head` so it rides on the Body pose `body`
        ({"rot", "loc", "scale"}), then adds its own rotation/offset."""
        q = Quaternion()
        for ax, deg in body.get("rot", []):
            q = Quaternion(Vector(ax), math.radians(deg)) @ q
        sx, sy, sz = body.get("scale", (1, 1, 1))
        h = Vector(head)
        d = h - PIVOT
        new = PIVOT + q @ Vector((d.x * sx, d.y * sy, d.z * sz)) + Vector(body.get("loc", (0, 0, 0)))
        loc = new - h + Vector(own_loc)
        return {"rot": list(body.get("rot", [])) + list(own_rot), "loc": tuple(loc)}

    FH, DH = (0, 0, 0.62), (0.08, -0.31, 0.662)

    def ride(pose):
        """Turn per-clip 'Flue'/'Drip' entries (own motion) into Root-space poses riding on Body."""
        b = pose.get("Body", {})
        for name, head in (("Flue", FH), ("Drip", DH)):
            own = pose.get(name, {})
            out = follow(head, b, own.get("rot", []), own.get("loc", (0, 0, 0)))
            if "scale" in own:
                out["scale"] = own["scale"]
            if not own.get("world"):
                pose[name] = out
            else:
                own.pop("world")
        return pose

    parts = ml.part_names(obj)
    print("[monster] parts", parts)
    hip = 0.22
    bones = [
        ("Root", (0, 0, 0), (0, 0, 0.12), None),
        ("Body", (0, 0, 0.17), (0, 0, 0.60), "Root"),
        ("Flue", (0, 0, 0.62), (0, 0, 0.97), "Root"),
        ("Drip", (0.08, -0.31, 0.662), (0.082, -0.351, 0.54), "Root"),
        ("LegL", (-0.14, -0.01, hip), (-0.14, -0.01, 0.0), "Root"),
        ("LegR", (0.14, -0.01, hip), (0.14, -0.01, 0.0), "Root"),
    ]
    assign = {}
    for p in parts:
        if p.startswith("flue") or p.startswith("eye"):
            assign[p] = "Flue"
        elif p == "tar_drip":
            assign[p] = "Drip"
        elif p == "leg_l":
            assign[p] = "LegL"
        elif p == "leg_r":
            assign[p] = "LegR"
    rig = ml.make_rig(obj, bones, assign)
    X, Y, Z = ml.WX, ml.WY, ml.WZ

    # 1. Idle: the barrel breathes, the flue sways, and puffs once per cycle.
    def idle(t):
        b = S(TAU * t)
        puff = max(0.0, S(TAU * (t - 0.45) * 2.5)) if 0.45 <= t < 0.85 else 0.0
        return {
            "Body": {"scale": sq(1 + 0.05 * b)},
            "Flue": {"rot": [(X, -3 * S(TAU * t + 0.8)), (Y, 5 * S(TAU * t + 1.6))],
                     "scale": (1 + 0.16 * puff, 1 + 0.16 * puff, 1 + 0.10 * puff - 0.03 * b)},
            "Drip": {"scale": (1, 1, 1 + 0.25 * (0.5 - 0.5 * math.cos(TAU * t)))},
        }

    # 2. Move: waddling shuffle; the body rolls onto the planted leg, the other foot lifts and
    #    swings forward; the flue lags behind the body roll.
    def move(t):
        s = S(TAU * t)
        roll = 8 * s                       # + tips the top toward +X → weight on LegR, LegL lifts
        lag = 0.16
        flue_world = 8 * S(TAU * (t - lag))
        lift_l, lift_r = max(0.0, s), max(0.0, -s)
        bob = abs(s)
        return {
            "Body": {"rot": [(X, 6), (Y, roll)], "loc": (0, 0, 0.03 * bob),
                     "scale": sq(1 + 0.06 * bob - 0.03)},
            "Flue": {"rot": [(Y, 1.2 * (flue_world - roll)), (X, -4 * bob)]},
            "Drip": {"rot": [(Y, -6 * s)]},
            "LegL": {"rot": [(X, -28 * lift_l + 10 * lift_r)], "loc": (0, 0, 0.07 * lift_l)},
            "LegR": {"rot": [(X, -28 * lift_r + 10 * lift_l)], "loc": (0, 0, 0.07 * lift_r)},
        }

    # 3. Attack (16 f): inhale 0–6, RELEASE at frame 8 (t = 0.5), recover 10–16.
    A = 16
    def attack(t):
        f = t * A
        body_k = keys(f, [(0, 1.0), (2, 1.05), (6, 1.12), (8, 0.8), (10, 0.86), (12, 0.97),
                          (14, 1.03), (16, 1.0)])
        body_x = keys(f, [(0, 0), (6, -8), (8, 9), (10, 7), (14, 1), (16, 0)])
        flue_x = keys(f, [(0, 0), (2, -6), (6, -14), (8, 20), (10, 14), (12, 4), (14, -3), (16, 0)])
        flue_s = keys(f, [(0, (1, 1, 1)), (6, (0.9, 0.9, 0.95)), (8, (1.22, 1.22, 1.3)),
                          (10, (1.1, 1.1, 1.15)), (12, (1.0, 1.0, 1.0)), (16, (1, 1, 1))])
        return {
            "Body": {"rot": [(X, body_x)], "scale": sq(body_k)},
            "Flue": {"rot": [(X, flue_x)], "scale": flue_s},
            "Drip": {"rot": [(X, keys(f, [(0, 0), (6, -10), (8, 25), (12, 0)]))]},
            "LegL": {"rot": [(X, keys(f, [(0, 0), (8, 6), (12, 0)]))]},
            "LegR": {"rot": [(X, keys(f, [(0, 0), (8, 6), (12, 0)]))]},
        }

    # 4. Hit (10 f): knocked back on impact, the flue whips and wobbles.
    H = 10
    def hit(t):
        f = t * H
        body_x = keys(f, [(0, 0), (2, -18), (4, -10), (6, -3), (8, 1), (10, 0)])
        flue_x = keys(f, [(0, 0), (2, 14), (4, -8), (6, 7), (8, -3), (10, 0)])
        k = keys(f, [(0, 1), (2, 0.86), (4, 1.06), (6, 0.98), (10, 1)])
        return {
            "Body": {"rot": [(X, body_x)], "scale": sq(k)},
            "Flue": {"rot": [(X, flue_x), (Y, keys(f, [(0, 0), (2, -6), (4, 7), (6, -3), (10, 0)]))]},
            "Drip": {"rot": [(X, keys(f, [(0, 0), (2, 25), (4, -15), (6, 0)]))]},
            "LegL": {"rot": [(X, keys(f, [(0, 0), (2, -8), (6, 0)]))]},
            "LegR": {"rot": [(X, keys(f, [(0, 0), (2, -8), (6, 0)]))]},
        }

    # 5. Death (20 f): flue cracks loose, tips off to +X and drops beside the barrel; the barrel sags,
    #    then everything crumbles flat into the ground and holds still.
    D = 20
    def death(t):
        f = t * D
        flue_y = keys(f, [(0, 0), (2, 8), (4, 18), (6, 45), (8, 80), (10, 95), (12, 88), (20, 88)])
        flue_l = keys(f, [(0, (0, 0, 0)), (2, (0, 0, 0.06)), (4, (0.06, 0, 0.08)), (6, (0.2, 0, 0.0)),
                          (8, (0.36, 0, -0.25)), (10, (0.45, 0, -0.42)), (12, (0.45, 0, -0.40)),
                          (20, (0.45, 0, -0.40))])
        body_y = keys(f, [(0, 0), (4, -4), (8, -10), (12, -14), (20, -14)])
        body_x = keys(f, [(0, 0), (2, -6), (8, 6), (12, 10), (20, 10)])
        k = keys(f, [(0, 1), (2, 0.9), (4, 1.04), (8, 0.86), (12, 0.8), (20, 0.8)])
        root = keys(f, [(0, (1, 1, 1)), (12, (1, 1, 1)), (14, (1.05, 1.05, 0.7)),
                        (16, (1.1, 1.1, 0.4)), (18, (1.12, 1.12, 0.18)), (20, (1.12, 1.12, 0.12))])
        return {
            "Root": {"scale": root},
            "Body": {"rot": [(X, body_x), (Y, body_y)], "scale": sq(k)},
            "Flue": {"rot": [(Y, flue_y)], "loc": flue_l, "world": True},
            "Drip": {"rot": [(X, keys(f, [(0, 0), (4, 20), (8, 0)]))]},
            "LegL": {"rot": [(Y, keys(f, [(0, 0), (8, 6), (12, 10), (20, 10)]))]},
            "LegR": {"rot": [(Y, keys(f, [(0, 0), (8, -6), (12, -10), (20, -10)]))]},
        }

    ml.add_clip(rig, "Idle", 40, lambda t: ride(idle(t)), loop=True)
    ml.add_clip(rig, "Move", 16, lambda t: ride(move(t)), loop=True)
    ml.add_clip(rig, "Attack", A, lambda t: ride(attack(t)), loop=False)
    ml.add_clip(rig, "Hit", H, lambda t: ride(hit(t)), loop=False)
    ml.add_clip(rig, "Death", D, lambda t: ride(death(t)), loop=False)
    ml.export_rigged(obj, rig, "spitter")
    clips_dir = os.environ.get("MONSTER_CLIPS")
    if clips_dir:
        ml.render_clips(rig, clips_dir)
