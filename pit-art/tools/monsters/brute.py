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
