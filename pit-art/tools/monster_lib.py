"""Shared helpers for hand-built low-poly enemies (art-bible.md §5), run inside headless Blender.

A monster script does:
    import sys, os; sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    import monster_lib as ml
    ml.reset()
    body = ml.lathe("body", [(z, r), ...], sides=8, paint=ml.tile("rock"), jitter=0.05)
    ml.box("eye_l", center, size, paint=ml.swatch("glow_amber"), rot=(rx, ry, rz))
    ml.finish("shambler")          # joins parts, writes GLB + preview, prints Unity verts

Conventions: metres, Z-up, origin at the feet centre, the creature faces −Y (same as the
TRELLIS/TripoSR models, so the GLB lands in Unity facing +Z like the other assets).
Flat shading: Unity verts = sum of face corners. Budget ≤ 400 (style.md).
All enemies share one 512 atlas + one 512 emissive map (theme 1), drawn here.
"""
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Quaternion, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # pit-art/
OUT_DIR = os.path.join(ROOT, "models", "lowpoly")
ATLAS = 512
MARGIN = 3

# --- palette (sRGB hex). Body values stay within style.md's enemy 0–30 %, accents 55–85 %. ---
SWATCHES = [
    ("body_dark", "#1F1815"), ("body_mid", "#38302A"), ("body_light", "#4C413D"),
    ("accent", "#D1BC7D"), ("accent_dark", "#B29562"), ("iron", "#2B2A2C"),
    ("iron_light", "#46444A"), ("glow_amber", "#FFB04A"), ("glow_hot", "#FF7A2E"),
    ("socket", "#0E0B0A"), ("soot", "#151110"), ("tar", "#1A1210"),
    ("brick", "#3E2C24"), ("rust", "#4A3326"), ("ash", "#57504B"), ("ember", "#C4622D"),
]
GLOWING = {"glow_amber", "glow_hot", "ember"}
SW = {name: i for i, (name, _) in enumerate(SWATCHES)}
# Detail tiles: (x, y, w, h) in pixels, origin bottom-left (same as UV space).
TILES = {
    "rivets": (0, 128, 256, 64),     # iron band with two rivets per 128 px, fit per face
    "rivets_v": (256, 128, 64, 128), # same idea, vertical (one rivet column)
    "brick": (0, 256, 256, 256),     # 4 courses of running-bond bricks
    "rock": (256, 256, 256, 256),    # slag plates with dark cracks
}
TILE_WORLD = {"brick": 0.36, "rock": 0.42}  # metres covered by one tile in "scale" mode


def _rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], dtype=np.float32)


# ------------------------------------------------------------------------------------ atlas
def _draw_atlas():
    rng = np.random.default_rng(7)
    alb = np.zeros((ATLAS, ATLAS, 3), np.float32)
    emi = np.zeros((ATLAS, ATLAS, 3), np.float32)
    alb[:] = _rgb("#1F1815")
    for i, (name, hx) in enumerate(SWATCHES):
        x, y = (i % 8) * 64, (i // 8) * 64
        alb[y:y + 64, x:x + 64] = _rgb(hx)
        if name in GLOWING:
            emi[y:y + 64, x:x + 64] = _rgb(hx)
    # rivets: iron band, darker edges, accent-brass rivet heads
    x0, y0, w, h = TILES["rivets"]
    band = alb[y0:y0 + h, x0:x0 + w]
    band[:] = _rgb("#2B2A2C")
    band[:6] = band[-6:] = _rgb("#1A191B")
    band[8:12] = _rgb("#3A393D")
    yy, xx = np.mgrid[0:h, 0:w]
    for cx in range(32, w, 64):
        d = np.hypot(xx - cx, yy - h / 2)
        band[d < 11] = _rgb("#151413")
        band[d < 9] = _rgb("#B29562")
        band[(d < 5) & (yy > h / 2)] = _rgb("#D1BC7D")
    x0, y0, w, h = TILES["rivets_v"]
    col = alb[y0:y0 + h, x0:x0 + w]
    col[:] = _rgb("#2B2A2C")
    col[:, :5] = col[:, -5:] = _rgb("#1A191B")
    yy, xx = np.mgrid[0:h, 0:w]
    for cy in (32, 96):
        d = np.hypot(xx - w / 2, yy - cy)
        col[d < 11] = _rgb("#151413")
        col[d < 9] = _rgb("#B29562")
        col[(d < 5) & (yy > cy)] = _rgb("#D1BC7D")
    # bricks: 4 courses, running bond, per-brick value jitter, dark mortar
    x0, y0, w, h = TILES["brick"]
    t = alb[y0:y0 + h, x0:x0 + w]
    t[:] = _rgb("#171210")
    ch = h // 4
    for row in range(4):
        off = 0 if row % 2 == 0 else 32
        for bx in range(-64, w, 64):
            c = _rgb("#3E2C24") * rng.uniform(0.8, 1.2) + rng.uniform(-0.01, 0.01, 3)
            xa, xb = max(bx + off + 4, 0), min(bx + off + 64 - 4, w)
            if xb > xa:
                t[row * ch + 5: row * ch + ch - 5, xa:xb] = c
                t[row * ch + ch - 12: row * ch + ch - 5, xa:xb] = c * 0.8  # lower lip
    # rock: a few big Voronoi slag plates with dark crack borders
    CELLS = 6
    x0, y0, w, h = TILES["rock"]
    pts = rng.uniform(0, w, (CELLS, 2))
    pts = np.concatenate([pts + [dx, dy] for dx in (-w, 0, w) for dy in (-h, 0, h)])  # tileable
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.hypot(xx[..., None] - pts[:, 0], yy[..., None] - pts[:, 1])
    order = np.sort(d, axis=-1)
    idx = np.argmin(d, axis=-1) % CELLS
    shade = rng.uniform(0.75, 1.25, CELLS)
    base = _rgb("#38302A")
    t = alb[y0:y0 + h, x0:x0 + w]
    t[:] = base * shade[idx][..., None]
    edge = order[..., 1] - order[..., 0]
    t[edge < 5] = _rgb("#120E0C")
    t[(edge >= 5) & (edge < 9)] *= 0.8
    return alb, emi


def _image(name, px):
    img = bpy.data.images.get(name) or bpy.data.images.new(name, ATLAS, ATLAS, alpha=False)
    rgba = np.ones((ATLAS, ATLAS, 4), np.float32)
    rgba[..., :3] = np.clip(px, 0, 1)
    img.pixels[:] = rgba.ravel()
    return img


# ----------------------------------------------------------------------------------- paints
def swatch(name):
    return ("swatch", SW[name])


def tile(name, mode="scale"):
    """mode 'scale': world-scaled per-face planar map (TILE_WORLD); 'fit': stretch each face to the tile."""
    return ("tile", name, mode)


def _rect_uv(rect, fx, fy):
    x, y, w, h = rect
    return ((x + MARGIN + fx * (w - 2 * MARGIN)) / ATLAS, (y + MARGIN + fy * (h - 2 * MARGIN)) / ATLAS)


def _face_axes(n):
    up = Vector((0, 0, 1))
    if abs(n.dot(up)) > 0.9:
        return Vector((1, 0, 0)), Vector((0, 1, 0))
    t = up.cross(n).normalized()
    return t, n.cross(t).normalized()


def _paint_face(face, uv_layer, paint):
    if paint[0] == "swatch":
        i = paint[1]
        u, v = ((i % 8) * 64 + 32) / ATLAS, ((i // 8) * 64 + 32) / ATLAS
        for loop in face.loops:
            loop[uv_layer].uv = (u, v)
        return
    _, name, mode = paint
    t, b = _face_axes(face.normal)
    st = [(l.vert.co.dot(t), l.vert.co.dot(b)) for l in face.loops]
    s0, q0 = min(p[0] for p in st), min(p[1] for p in st)
    sw = max(p[0] for p in st) - s0 or 1e-6
    qh = max(p[1] for p in st) - q0 or 1e-6
    if mode == "fit":
        sx, sy = 1 / sw, 1 / qh
    else:
        world = TILE_WORLD.get(name, 0.4)
        k = max(1.0, sw / world, qh / world)  # shrink faces bigger than one tile
        sx = sy = 1 / (world * k)
    for loop, (s, q) in zip(face.loops, st):
        loop[uv_layer].uv = _rect_uv(TILES[name], (s - s0) * sx, (q - q0) * sy)


# ------------------------------------------------------------------------------------ parts
_PARTS = []


def reset(seed=1):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _PARTS.clear()
    random.seed(seed)


def _new_obj(name, bm, paint, paint_fn=None):
    uv = bm.loops.layers.uv.verify()
    bm.normal_update()
    for f in bm.faces:
        _paint_face(f, uv, paint_fn(f) if paint_fn else paint)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    _PARTS.append(obj)
    return obj


def lathe(name, profile, sides=8, paint=None, top=None, bottom=None, jitter=0.0, z_jitter=0.0,
          rot=None, offset=(0, 0, 0), scale_xy=(1, 1), cap_top=True, cap_bottom=True, phase=None):
    """Revolve [(z, r), ...] (bottom→top) around Z. phase defaults to π/sides so a flat face points at −Y.
    paint: sides; top/bottom: caps (default = paint). jitter: random radial factor per vertex (rocky)."""
    paint = paint or swatch("body_mid")
    phase = math.pi / sides - math.pi / 2 if phase is None else phase
    bm = bmesh.new()
    rings = []
    for k, (z, r) in enumerate(profile):
        ring = []
        for j in range(sides):
            a = phase + 2 * math.pi * j / sides
            rr = r * (1 + random.uniform(-jitter, jitter))
            zz = z + (random.uniform(-z_jitter, z_jitter) if 0 < k < len(profile) - 1 else 0)
            ring.append(bm.verts.new((rr * math.cos(a) * scale_xy[0], rr * math.sin(a) * scale_xy[1], zz)))
        rings.append(ring)
    roles = {}
    for k in range(len(rings) - 1):
        for j in range(sides):
            a, b = rings[k][j], rings[k][(j + 1) % sides]
            c, d = rings[k + 1][(j + 1) % sides], rings[k + 1][j]
            roles[bm.faces.new((a, b, c, d))] = "side"
    if cap_bottom and profile[0][1] > 1e-6:
        roles[bm.faces.new(list(reversed(rings[0])))] = "bottom"
    if cap_top and profile[-1][1] > 1e-6:
        roles[bm.faces.new(rings[-1])] = "top"
    _xform(bm, rot, offset)
    pick = {"side": paint, "top": top or paint, "bottom": bottom or paint}
    return _new_obj(name, bm, None, lambda f: pick[roles[f]])


def box(name, center, size, paint=None, rot=None, taper=1.0):
    """Axis box of size (x, y, z) at center, optional Euler rot (radians). taper < 1 shrinks the top face."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if v.co.z > 0:
            v.co.x *= taper
            v.co.y *= taper
    _xform(bm, rot, center)
    return _new_obj(name, bm, paint or swatch("body_mid"))


def wedge(name, center, size, paint=None, rot=None):
    """Triangular prism (ridge along X at the top) — cheap crust plate / brow. 5 faces, 18 corners."""
    bm = bmesh.new()
    sx, sy, sz = (s / 2 for s in size)
    v = [bm.verts.new(p) for p in [(-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
                                   (-sx, 0, sz), (sx, 0, sz)]]
    for f in [(0, 3, 2, 1), (0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5)]:
        bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    _xform(bm, rot, center)
    return _new_obj(name, bm, paint or swatch("body_mid"))


def mesh(name, verts, faces, paint=None, rot=None, offset=(0, 0, 0)):
    """Arbitrary polygon soup (face winding is recalculated outward)."""
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in verts]
    for f in faces:
        bm.faces.new([vs[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    _xform(bm, rot, offset)
    return _new_obj(name, bm, paint or swatch("body_mid"))


def _xform(bm, rot, offset):
    m = Matrix.Translation(Vector(offset))
    if rot:
        m = m @ Euler(rot).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)


def mirror_x(obj, name=None):
    """Copy a part mirrored across X (UVs kept, winding fixed)."""
    me = obj.data.copy()
    bm = bmesh.new()
    bm.from_mesh(me)
    for v in bm.verts:
        v.co.x = -v.co.x
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name or obj.name + "_r", me)
    bpy.context.scene.collection.objects.link(o)
    _PARTS.append(o)
    return o


# ----------------------------------------------------------------------------------- output
def unity_verts(obj):
    """Flat shading: every face corner is its own vertex in Unity."""
    return sum(len(p.vertices) for p in obj.data.polygons)


def _material(alb, emi):
    mat = bpy.data.materials.new("enemy_theme1")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.9
    ta = nt.nodes.new("ShaderNodeTexImage")
    ta.image = alb
    ta.interpolation = "Closest"
    nt.links.new(ta.outputs["Color"], bsdf.inputs["Base Color"])
    te = nt.nodes.new("ShaderNodeTexImage")
    te.image = emi
    te.interpolation = "Closest"
    nt.links.new(te.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 1.0
    return mat


def finish(key, preview=True, budget=400):
    """Join parts, apply the shared atlas, export models/lowpoly/enemy_<key>.glb (+ preview png)."""
    alb_px, emi_px = _draw_atlas()
    alb, emi = _image("enemy_theme1_albedo", alb_px), _image("enemy_theme1_emissive", emi_px)
    os.makedirs(OUT_DIR, exist_ok=True)
    for img, suffix in ((alb, "albedo"), (emi, "emissive")):
        img.filepath_raw = os.path.join(OUT_DIR, f"enemy_theme1_{suffix}.png")
        img.file_format = "PNG"
        img.save()
    bpy.ops.object.select_all(action="DESELECT")
    for o in _PARTS:
        o.select_set(True)
        vg = o.vertex_groups.new(name="part:" + o.name)  # survives the join; used by make_rig()
        vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    bpy.context.view_layer.objects.active = _PARTS[0]
    if len(_PARTS) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = obj.data.name = f"enemy_{key}"
    for p in obj.data.polygons:
        p.use_smooth = False
    obj.data.materials.clear()
    obj.data.materials.append(_material(alb, emi))
    zmin = min(v.co.z for v in obj.data.vertices)
    for v in obj.data.vertices:
        v.co.z -= zmin  # feet on the ground
    path = os.path.join(OUT_DIR, f"enemy_{key}.glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True,
                              export_normals=True, export_texcoords=True, export_materials="EXPORT")
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    uv = unity_verts(obj)
    d = obj.dimensions
    print(f"[monster] {key}: {tris} tris, {uv} Unity verts (budget {budget}) "
          f"{'OK' if uv <= budget else 'OVER'}, size {d.x:.2f}×{d.y:.2f}×{d.z:.2f} m -> {path}")
    if preview:
        render_preview(obj, os.path.join(OUT_DIR, f"enemy_{key}_preview.png"))
    return obj


def _setup_render(res_x, res_y):
    sc = bpy.context.scene
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            continue
    sc.render.resolution_x, sc.render.resolution_y = res_x, res_y
    sc.view_settings.view_transform = "Standard"
    w = sc.world or bpy.data.worlds.new("W")
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.13, 0.13, 0.14, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    sc.collection.objects.link(sun)
    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "SUN"))
    fill.data.energy = 0.8
    fill.rotation_euler = (math.radians(70), 0, math.radians(140))
    sc.collection.objects.link(fill)
    return sc


def _camera(sc, target, dist, height_frac=0.45, lens=50):
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.lens = lens
    sc.collection.objects.link(cam)
    cam.location = (dist * 0.55, -dist * 0.9, dist * 0.45)
    tgt = bpy.data.objects.new("T", None)
    sc.collection.objects.link(tgt)
    tgt.location = target
    c = cam.constraints.new("TRACK_TO")
    c.target = tgt
    sc.camera = cam


def render_preview(obj, path):
    """Three-quarter front view like the concept images (camera front-right, slightly above)."""
    sc = _setup_render(512, 512)
    h = max(obj.dimensions)
    _camera(sc, (0, 0, obj.dimensions.z * 0.45), h * 2.3)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("[monster] preview", path)


# ------------------------------------------------------------------------------- rig + clips
RIG_DIR = os.path.join(ROOT, "models", "rigged")
CLIP_FPS = 24          # scene rate; keys every KEY_STEP frames with constant interpolation
KEY_STEP = 2           # → 12 poses per second, the chunky 8–12 fps look of style.md §6
WX, WY, WZ = (1, 0, 0), (0, 1, 0), (0, 0, 1)


def part_names(obj):
    """Names of the parts inside a finished monster (from finish())."""
    return [g.name[5:] for g in obj.vertex_groups if g.name.startswith("part:")]


def part_bounds(obj, *parts):
    """(min Vector, max Vector) of the given parts' verts after finish() (feet at z=0)."""
    idx = {obj.vertex_groups["part:" + p].index for p in parts}
    ps = [v.co for v in obj.data.vertices if any(g.group in idx for g in v.groups)]
    lo = Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps)))
    hi = Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps)))
    return lo, hi


def make_rig(obj, bones, assign):
    """Rigid skin: every part follows exactly one bone (low-poly creatures built from parts).

    bones:  [(name, head xyz, tail xyz, parent or None), ...]  — the first bone should be "Root" at the
            feet so whole-body moves/turns are clean; armature space = the finished mesh's space.
    assign: {part name: bone name}; parts not listed go to the second bone (usually "Body").
    """
    arm = bpy.data.armatures.new(obj.name + "_Armature")
    rig = bpy.data.objects.new(obj.name + "_Rig", arm)
    bpy.context.scene.collection.objects.link(rig)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, parent in bones:
        b = arm.edit_bones.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        if parent:
            b.parent = arm.edit_bones[parent]
        b.align_roll(Vector((0, -1, 0)))
    bpy.ops.object.mode_set(mode="OBJECT")
    default = bones[1][0] if len(bones) > 1 else bones[0][0]
    bone_vg = {name: obj.vertex_groups.new(name=name) for name, *_ in bones}
    for pname in part_names(obj):
        g = obj.vertex_groups["part:" + pname]
        verts = [v.index for v in obj.data.vertices if any(x.group == g.index for x in v.groups)]
        bone_vg[assign.get(pname, default)].add(verts, 1.0, "REPLACE")
    for g in [g for g in obj.vertex_groups if g.name.startswith("part:")]:
        obj.vertex_groups.remove(g)
    obj.parent = rig
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    rig.animation_data_create()
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return rig


def add_clip(rig, name, length, pose_fn, loop=True):
    """Key a clip. pose_fn(t) with t in [0, 1) (loop) or [0, 1] (one-shot) returns
    {bone: {"rot": [(world_axis, deg), ...], "loc": (dx, dy, dz) metres, "scale": (sx, sy, sz)}}.
    Rotations are about world axes through the bone's head (applied in list order).
    Sign rule: +deg about X tips an upward bone forward (toward −Y, the facing direction);
    +deg about Z turns the creature to its left (counter-clockwise seen from above).
    Keys every KEY_STEP frames with CONSTANT interpolation; length in scene frames at CLIP_FPS."""
    act = bpy.data.actions.new(name)
    rig.animation_data.action = act
    frames = list(range(0, length + (0 if loop else 1), KEY_STEP))
    if not loop and frames[-1] != length:
        frames.append(length)
    for f in frames:
        t = f / length
        pose = pose_fn(t)
        for pb in rig.pose.bones:
            spec = pose.get(pb.name, {})
            m = pb.bone.matrix_local.to_3x3()
            q = Quaternion()
            for ax, deg in spec.get("rot", []):
                q = (m.inverted() @ Quaternion(Vector(ax), math.radians(deg)).to_matrix() @ m).to_quaternion() @ q
            pb.rotation_quaternion = q
            pb.location = m.inverted() @ Vector(spec.get("loc", (0, 0, 0)))
            sx, sy, sz = spec.get("scale", (1, 1, 1))
            pb.scale = (sx, sz, sy)  # bone Y is the bone's length axis (world Z for upward bones)
            for path in ("rotation_quaternion", "location", "scale"):
                pb.keyframe_insert(path, frame=f)
    for fc in _fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "CONSTANT"
    act.use_fake_user = True
    tr = rig.animation_data.nla_tracks.new()
    tr.name = name
    tr.strips.new(name, 0, act)
    rig.animation_data.action = None
    return act


def _fcurves(act):
    if hasattr(act, "fcurves") and len(getattr(act, "fcurves", [])):
        return list(act.fcurves)
    out = []  # Blender 5 layered actions
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                out += list(bag.fcurves)
    return out


def export_rigged(obj, rig, key):
    """models/rigged/enemy_<key>.fbx (Unity: Rig = Generic) + .glb with every clip."""
    os.makedirs(RIG_DIR, exist_ok=True)
    bpy.context.scene.render.fps = CLIP_FPS
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    out = os.path.join(RIG_DIR, f"enemy_{key}")
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
    print(f"[monster] rigged {key}: {len(rig.data.bones)} bones, clips "
          f"{[t.name for t in rig.animation_data.nla_tracks]} -> {out}.fbx/.glb")


def render_clips(rig, folder, res=256, cam_loc=(1.6, -2.6, 1.6), target_z=0.4, ortho=1.6):
    """Every clip frame by frame into folder/<clip>/NNN.png (high 3/4 view like the game camera).
    Turn them into GIFs with tools/make_gifs.py."""
    sc = _setup_render(res, res)
    sc.render.fps = CLIP_FPS
    cam = bpy.data.objects.new("ClipCam", bpy.data.cameras.new("ClipCam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = ortho
    sc.collection.objects.link(cam)
    cam.location = cam_loc
    tgt = bpy.data.objects.new("ClipT", None)
    sc.collection.objects.link(tgt)
    tgt.location = (0, 0, target_z)
    con = cam.constraints.new("TRACK_TO")
    con.target = tgt
    sc.camera = cam
    for tr in rig.animation_data.nla_tracks:
        tr.mute = True
    for tr in rig.animation_data.nla_tracks:
        act = bpy.data.actions[tr.name]
        rig.animation_data.action = act
        lo, hi = (int(x) for x in act.frame_range)
        os.makedirs(os.path.join(folder, tr.name), exist_ok=True)
        for f in range(lo, hi + 1):
            sc.frame_set(f)
            sc.render.filepath = os.path.join(folder, tr.name, f"{f:03d}.png")
            bpy.ops.render.render(write_still=True)
    rig.animation_data.action = None
    for tr in rig.animation_data.nla_tracks:
        tr.mute = False
    print("[monster] clip frames in", folder)


if __name__ == "__main__":
    sys.exit("import this from a monster script; see the module docstring")
