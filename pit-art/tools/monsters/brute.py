"""Brute (art-bible §5, archetype "tough", 1x1): a bigger, heavier Shambler-type slag lump with a
riveted cast-iron collar band, an iron brow plate over the face and one amber eye slit glowing in
the gap between plate and collar. Thick block legs.

Run:  blender -b -P pit-art/tools/monsters/brute.py            (GLB + preview)
      BRUTE_VIEWS=<dir> blender -b -P ...                       (+ front / high 3/4 check renders)
"""
import sys, os; sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # noqa: E401,E702
import math

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

import monster_lib as ml

ml.reset(seed=11)

SY = 0.86          # body depth squash (wider than deep, a bit boxy)
SIDES = 8
PHASE = math.pi / SIDES - math.pi / 2  # flat face toward -Y (same as ml.lathe default)


def blk(name, center, size, paint, rot=None, taper=1.0, drop=(), taper_y=None, shift_top=(0, 0)):
    """Box without the faces named in drop ('bottom','top','back','front','left','right').
    taper shrinks the top in X (and Y unless taper_y given); shift_top moves the top face (x, y)."""
    sx, sy, sz = (s / 2 for s in size)
    ty = taper if taper_y is None else taper_y
    bm = bmesh.new()
    p = []
    for z in (-sz, sz):
        kx, ky = (taper, ty) if z > 0 else (1, 1)
        dx, dy = shift_top if z > 0 else (0, 0)
        for x, y in ((-sx, -sy), (sx, -sy), (sx, sy), (-sx, sy)):
            p.append(bm.verts.new((x * kx + dx, y * ky + dy, z)))
    faces = {"bottom": (0, 3, 2, 1), "top": (4, 5, 6, 7), "front": (0, 1, 5, 4),
             "right": (1, 2, 6, 5), "back": (2, 3, 7, 6), "left": (3, 0, 4, 7)}
    for k, f in faces.items():
        if k not in drop:
            bm.faces.new([p[i] for i in f])
    m = Matrix.Translation(Vector(center))
    if rot:
        m = m @ Euler(rot).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return ml._new_obj(name, bm, paint)


def ring_pt(r, j, z):
    a = PHASE + 2 * math.pi * j / SIDES
    return (r * math.cos(a), r * math.sin(a) * SY, z)


# ---------------------------------------------------------------------------------- body
# Pot shape: narrow-ish base, belly bulge under the collar, slightly narrower boxy shoulders.
body = ml.lathe("body", [(0.14, 0.34), (0.27, 0.48), (0.44, 0.48), (0.66, 0.43), (0.80, 0.38)],
                sides=SIDES, paint=ml.tile("rock"), top=ml.swatch("body_dark"),
                scale_xy=(1, SY), jitter=0.025, cap_bottom=False)

# Two heavy bumps on top (tapered slag blocks, tilted outward)
for s in (-1, 1):
    blk(f"bump_{'l' if s < 0 else 'r'}", (s * 0.15, 0.04, 0.84), (0.36, 0.54, 0.22),
        ml.tile("rock"), rot=(0.0, s * 0.22, 0.0), taper=0.58, taper_y=0.7, drop=("bottom",))

# ---------------------------------------------------------------------------- iron collar
# Custom ring: riveted outer faces + an iron lip at top and bottom that tucks into the body.
Z0, Z1, ROUT, RIN = 0.50, 0.63, 0.50, 0.42
bm = bmesh.new()
lo_in = [bm.verts.new(ring_pt(RIN + 0.02, j, Z0 - 0.02)) for j in range(SIDES)]
lo = [bm.verts.new(ring_pt(ROUT, j, Z0)) for j in range(SIDES)]
hi = [bm.verts.new(ring_pt(ROUT, j, Z1)) for j in range(SIDES)]
hi_in = [bm.verts.new(ring_pt(RIN, j, Z1 + 0.015)) for j in range(SIDES)]
role = {}
for j in range(SIDES):
    k = (j + 1) % SIDES
    role[bm.faces.new((lo[j], lo[k], hi[k], hi[j]))] = "out"
    role[bm.faces.new((hi[j], hi[k], hi_in[k], hi_in[j]))] = "lip"
    role[bm.faces.new((lo_in[j], lo_in[k], lo[k], lo[j]))] = "lip"
ml._new_obj("collar", bm, None,
            lambda f: ml.tile("rivets", "fit") if role[f] == "out" else ml.swatch("iron"))

# ------------------------------------------------------------------------ face plate + eye
# Face plate: thin cast-iron slab over the upper face, leaning back with the body so the eye
# gap under it stays visible from the high game camera.
FY = -0.43 * math.cos(math.pi / SIDES) * SY  # body front face y around z 0.75
PT = 0.05                                    # plate thickness
blk("plate", (0.0, FY - PT / 2 + 0.01, 0.765), (0.52, PT, 0.13), ml.swatch("iron_light"),
    rot=(-0.22, 0, 0), taper=0.9, drop=("back",))

# Eye slit: dark socket strip in the gap + amber hexagonal slit, tilted up toward the camera.
sock_y = FY - 0.012
ml.mesh("socket", [(-0.19, sock_y - 0.012, 0.635), (0.19, sock_y - 0.012, 0.635),
                   (0.19, sock_y + 0.01, 0.712), (-0.19, sock_y + 0.01, 0.712)],
        [(0, 1, 2, 3)], paint=ml.swatch("socket"))
ey = sock_y - 0.022
ml.mesh("eye", [(-0.12, ey, 0.672), (-0.09, ey - 0.004, 0.648), (0.09, ey - 0.004, 0.648),
                (0.12, ey, 0.672), (0.09, ey + 0.01, 0.698), (-0.09, ey + 0.01, 0.698)],
        [(0, 1, 2, 3, 4, 5)], paint=ml.swatch("glow_amber"))

# ---------------------------------------------------------------------------------- legs
for s in (-1, 1):
    n = "l" if s < 0 else "r"
    blk(f"leg_{n}", (s * 0.21, 0.03, 0.15), (0.21, 0.23, 0.26), ml.swatch("body_dark"),
        taper=1.1, drop=("bottom", "top"))
    blk(f"foot_{n}", (s * 0.22, -0.04, 0.05), (0.25, 0.34, 0.10), ml.swatch("body_mid"),
        taper=0.85, taper_y=0.8, shift_top=(0, 0.02), drop=("bottom",))

obj = ml.finish("brute")


# --------------------------------------------------------------------------- check renders
def extra_views(obj, out_dir):
    sc = bpy.context.scene
    os.makedirs(out_dir, exist_ok=True)
    tgt = bpy.data.objects.new("T2", None)
    sc.collection.objects.link(tgt)
    tgt.location = (0, 0, obj.dimensions.z * 0.45)
    views = {"front": (0, -3.0, 0.7), "high34": (1.1, -1.75, 2.2), "top": (0.0, -0.9, 3.0),
             "back": (-1.4, 2.4, 1.2)}
    for name, loc in views.items():
        cam = bpy.data.objects.new("C_" + name, bpy.data.cameras.new("C_" + name))
        cam.data.lens = 50
        sc.collection.objects.link(cam)
        cam.location = loc
        c = cam.constraints.new("TRACK_TO")
        c.target = tgt
        sc.camera = cam
        sc.render.filepath = os.path.join(out_dir, f"brute_{name}.png")
        bpy.ops.render.render(write_still=True)
        print("[monster] view", sc.render.filepath)


if os.environ.get("BRUTE_VIEWS"):
    extra_views(obj, os.environ["BRUTE_VIEWS"])


# ------------------------------------------------------------------------------ rig + clips
# MONSTER_RIG=1 → rig + Idle/Move/Attack/Hit/Death, export models/rigged/enemy_brute.fbx/.glb;
# MONSTER_CLIPS=<dir> also renders every clip frame for tools/make_gifs.py.
def _ease(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * (3 - 2 * x)


def _seg(t, a, b):
    """0 before a, eased 0→1 over [a, b], 1 after."""
    return _ease((t - a) / (b - a)) if b > a else float(t >= b)


def _sq(s):
    """Volume-preserving squash: s = vertical scale → (sx, sy, sz)."""
    w = 1.0 / math.sqrt(s)
    return (w, w, s)


def _pivot_x(deg, pivot, head=(0, 0, 0.16)):
    """loc that makes a rotation of deg about world X at the bone head look like it turns about pivot (y, z)."""
    a = math.radians(deg)
    vy, vz = pivot[0] - head[1], pivot[1] - head[2]
    ry, rz = vy * math.cos(a) - vz * math.sin(a), vy * math.sin(a) + vz * math.cos(a)
    return (0.0, vy - ry, vz - rz)


def build_rig(obj):
    lo, hi = ml.part_bounds(obj, "leg_l")
    hip = hi.z
    vlo, vhi = ml.part_bounds(obj, "plate", "socket", "eye")
    blo, bhi = ml.part_bounds(obj, "bump_l", "bump_r")
    bones = [
        ("Root", (0, 0, 0), (0, 0, 0.2), None),
        ("Body", (0, 0, 0.16), (0, 0, 0.78), "Root"),
        ("Visor", (0, vhi.y, vlo.z), (0, vhi.y, vhi.z + 0.08), "Body"),
        ("Bumps", (0, 0.04, blo.z), (0, 0.04, bhi.z), "Body"),
        ("LegL", (-0.21, 0.03, hip), (-0.21, 0.03, 0.0), "Root"),
        ("LegR", (0.21, 0.03, hip), (0.21, 0.03, 0.0), "Root"),
    ]
    assign = {"body": "Body", "collar": "Body", "plate": "Visor", "socket": "Visor", "eye": "Visor",
              "bump_l": "Bumps", "bump_r": "Bumps", "leg_l": "LegL", "foot_l": "LegL",
              "leg_r": "LegR", "foot_r": "LegR"}
    return ml.make_rig(obj, bones, assign)


X, Y, Z = ml.WX, ml.WY, ml.WZ


def idle(t):
    # two-beat heavy breath: inhale (rise + slight stretch), exhale settle with a visor clank
    b = 0.5 - 0.5 * math.cos(2 * math.pi * t)          # 0 → 1 → 0
    s = 1.0 + 0.05 * b - 0.02 * (1 - b)
    clank = 0.012 if 0.55 <= t < 0.65 else 0.0
    return {
        "Body": {"scale": _sq(s), "loc": (0, 0, 0.018 * b), "rot": [(X, -3.0 * b)]},
        "Visor": {"loc": (0, 0, 0.018 * b - clank), "rot": [(X, -4.0 * b + (3.0 if clank else 0))]},
        "Bumps": {"loc": (0, 0, 0.01 * b)},
    }


def move(t):
    # two stomps per loop: L lifts in [0, .35], lands at .35-.5; R the same half a loop later
    out = {}
    body_x = 0.0
    land = 0.0
    lift_amt = {}
    for leg, off, side in (("LegL", 0.0, -1), ("LegR", 0.5, 1)):
        p = (t - off) % 1.0
        if p < 0.4:
            u = p / 0.4
            # slow heave up to u 0.7, then a fast slam down (on the floor again at p 0.34)
            lift = math.sin(0.5 * math.pi * u / 0.7) if u < 0.7 else max(0.0, 1.0 - (u - 0.7) / 0.15)
        else:
            lift = 0.0
        lift_amt[leg] = lift
        swing = 28.0 * lift * (1 if p < 0.2 else 0.4)          # foot swings forward while lifting
        out[leg] = {"loc": (0, -0.08 * lift, 0.21 * lift), "rot": [(X, -swing)]}
        if 0.34 <= p < 0.5:
            land = max(land, 1.0 - (p - 0.34) / 0.16)
        # weight shifts over the planted leg (opposite side of the lifted one)
        body_x += -side * 0.07 * math.sin(math.pi * min(p / 0.4, 1.0)) if p < 0.4 else 0.0
    roll = body_x / 0.07 * 9.0
    s = 1.0 - 0.13 * land + 0.04 * max(lift_amt.values())
    out["Body"] = {"scale": _sq(s), "loc": (body_x, 0, -0.04 * land + 0.05 * max(lift_amt.values())),
                   "rot": [(Y, -roll), (X, 5.0)]}
    out["Visor"] = {"loc": (0, 0, -0.02 * land)}
    out["Bumps"] = {"loc": (0, 0, -0.025 * land), "scale": _sq(1.0 - 0.1 * land)}
    return out


def attack(t):
    wind = _seg(t, 0.0, 0.40)          # rear back + lift
    slam = _seg(t, 0.40, 0.52)         # snap forward
    hold = 1.0 if 0.52 <= t < 0.72 else 0.0
    rec = _seg(t, 0.72, 1.0)
    lean = -20.0 * wind * (1 - slam) + 30.0 * slam * (1 - rec)
    up = 0.08 * wind * (1 - slam)
    fwd = -0.12 * slam * (1 - rec)
    s = 1.0 + 0.10 * wind * (1 - slam) - 0.16 * slam * (1 - rec) * (1.0 if t < 0.62 else 0.6)
    vis = -8.0 * wind * (1 - slam) + 10.0 * slam * (1 - rec) * (1 if t < 0.6 else 0.5)
    leg_back = 8.0 * wind * (1 - slam)
    pv = _pivot_x(lean, (-0.36, 0.2) if lean > 0 else (0.34, 0.2))
    return {
        "Body": {"rot": [(X, lean)], "loc": (0, fwd + pv[1], up - 0.03 * slam * (1 - rec) + pv[2]),
                 "scale": _sq(s)},
        "Visor": {"rot": [(X, vis)], "loc": (0, 0, 0.03 * wind * (1 - slam))},
        "Bumps": {"scale": _sq(1.0 - 0.12 * slam * (1 - rec) + 0.05 * wind * (1 - slam))},
        "LegL": {"rot": [(X, -leg_back)], "loc": (0, 0, 0.03 * wind * (1 - slam))},
        "LegR": {"rot": [(X, -leg_back)], "loc": (0, 0, 0.03 * wind * (1 - slam))},
    }


def hit(t):
    k = 1.0 - _seg(t, 0.15, 1.0) if t >= 0.15 else t / 0.15
    rattle = [0, 4, -3, 2, -1, 0][min(int(t * 6), 5)]
    return {
        "Body": {"rot": [(X, -6.0 * k)], "loc": (0, 0.03 * k, 0), "scale": _sq(1.0 - 0.04 * k)},
        "Visor": {"rot": [(X, rattle * 1.5)], "loc": (0.008 * rattle / 4, 0, 0)},
    }


def death(t):
    pop = _seg(t, 0.0, 0.15)            # visor knocked loose
    fall = _seg(t, 0.15, 0.40)          # visor falls to the floor
    sag = _seg(t, 0.05, 0.35)           # body sags
    top = _seg(t, 0.30, 0.62)           # topples forward
    sink = _seg(t, 0.60, 1.0)           # crumbles into the ground
    s = 1.0 - 0.12 * sag
    sx, sy, sz = _sq(s)
    k = 1.0 - 0.7 * sink              # shrink toward the floor contact point
    ang = 48.0 * top
    pv = _pivot_x(ang, (-0.40, 0.16 - 0.06 * sag))
    return {
        "Body": {"rot": [(X, ang)],
                 "loc": (0, pv[1] * k, pv[2] - 0.05 * sag - 0.30 * sink),
                 "scale": (sx * k, sy * k, sz * k * (1 - 0.35 * sink))},
        "Visor": {"rot": [(X, -25.0 * pop - 50.0 * fall)],
                  "loc": (0, -0.12 * pop - 0.12 * fall, 0.06 * pop * (1 - fall) - 0.58 * fall)},
        "Bumps": {"scale": _sq(1.0 - 0.3 * sink)},
        "LegL": {"rot": [(Y, 14.0 * sag), (X, -20.0 * top)], "scale": (k, k, k * (1 - 0.5 * sink)),
                 "loc": (0.1 * sink, -0.18 * top, -0.2 * sink)},
        "LegR": {"rot": [(Y, -14.0 * sag), (X, -20.0 * top)], "scale": (k, k, k * (1 - 0.5 * sink)),
                 "loc": (-0.1 * sink, -0.18 * top, -0.2 * sink)},
    }


if os.environ.get("MONSTER_RIG"):
    rig = build_rig(obj)
    ml.add_clip(rig, "Idle", 44, idle, loop=True)
    ml.add_clip(rig, "Move", 20, move, loop=True)
    ml.add_clip(rig, "Attack", 20, attack, loop=False)
    ml.add_clip(rig, "Hit", 10, hit, loop=False)
    ml.add_clip(rig, "Death", 24, death, loop=False)
    ml.export_rigged(obj, rig, "brute")
    if os.environ.get("MONSTER_CLIPS"):
        ml.render_clips(rig, os.environ["MONSTER_CLIPS"], ortho=2.0)
