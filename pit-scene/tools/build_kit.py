"""Headless Blender: script-model the Theme 1 ("The Sunken Yard") pit kit, UV it into the shared atlas,
check budgets, export one FBX + one GLB per piece, save kit/t1_kit.blend and render a kit sheet.

Usage (from pit-scene/):
  /opt/homebrew/bin/blender -b -P tools/build_kit.py
  (run tools/make_atlas.py first; it writes textures/T1_Scenery_Atlas.png)

Piece frame (Blender; Unity in brackets, same for FBX and GLB):
  origin  = floor level, centre of the piece along the wall, ON the collision face (board edge line)
  -Y      = into the board, the face the player sees            [Unity +Z, transform.forward]
  +X      = along the wall                                      [Unity -X]
  +Z      = up, out of the floor                                [Unity +Y]
  Wall modules are one 1.125-unit cell (x in [-0.5625, +0.5625]). They are merged 4 at a time into corridor
  segments (SEGMENTS: start / mid / end sets, 4.5 units = one 4-row recycling group, x in [-2.25, +2.25]).
  The first and last module of every segment is plain brick, so any segment joins any other. Every face stays
  at y >= 0 below z = 1.5, so nothing covers the board edge where balls and units are. The body extends to +Y
  (outside the board).
  Also built: floor strips (4 rows x cols, per board width), the end-of-corridor gate (tower + lintel), and the
  loose props. See ASSEMBLY.md.
"""
import json
import math
import os
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
ATLAS = os.path.join(ROOT, "textures", "T1_Scenery_Atlas.png")
CELL = 1.125
HALF = CELL / 2
H = 4.5          # brick face height (atlas brick regions map exactly 1 cell x 4.5)
D = 0.75         # wall body depth behind the collision face
PLINTH = 0.35
COPE_Z0, COPE_Z1 = 4.4, 4.7
VERT_BUDGET = 2000
ATLAS_PX = 1024

# name: (x0, y0, x1, y1) pixel rect (top-left origin), mode, (units across, units down)
REGIONS = {
    "brick_a": ((0, 0, 256, 1024), "wall", (CELL, H)),
    "brick_b": ((256, 0, 512, 1024), "wall", (CELL, H)),
    "stone":   ((512, 0, 768, 512), "fit", (CELL, 2 * CELL)),
    "floor_a": ((512, 512, 768, 768), "fit", (CELL, CELL)),
    "floor_b": ((512, 768, 768, 1024), "fit", (CELL, CELL)),
    "iron":    ((768, 0, 1024, 256), "fit", (CELL, CELL)),
    "clay":    ((768, 256, 1024, 512), "fit", (CELL, CELL)),
    "bronze":  ((768, 512, 1024, 768), "fit", (CELL, CELL)),
    "pilaster": ((768, 832, 832, 1024), "fit", (0.28, 1.1)),
    "floor_c": ((864, 864, 1024, 1024), "fit", (CELL, CELL)),
    "glow":    ((832, 832, 864, 864), "swatch", (1, 1)),
}
EMIT = os.path.join(ROOT, "textures", "T1_Scenery_Emit.png")
BEAM_REG = "beam"        # faces tagged with this go to material slot 1 (T1_Beam, additive, vertex alpha)
SWATCH_NAMES = ["iron", "iron_hi", "chain", "soot", "mortar", "wood", "copper", "rust",
                "stone_dk", "stone", "teal", "deep", "clay", "bronze", "patina", "water"]
for i, n in enumerate(SWATCH_NAMES):
    x, y = 768 + (i % 8) * 32, 768 + (i // 8) * 32
    REGIONS["sw_" + n] = ((x, y, x + 32, y + 32), "swatch", (1, 1))
REGIONS[BEAM_REG] = ((0, 0, 1, 1), "swatch", (1, 1))
REG_IDS = {n: i for i, n in enumerate(REGIONS)}
REG_NAMES = list(REGIONS)


# ---------------------------------------------------------------- geometry helpers

class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.reg = self.bm.faces.layers.int.new("reg")
        self.beam_alpha = None   # fn(co) -> alpha for beam faces; set when the piece has a light shaft

    def face(self, pts, region, want_normal=None):
        vs = [self.bm.verts.new(p) for p in pts]
        f = self.bm.faces.new(vs)
        f.normal_update()
        if want_normal is not None and f.normal.dot(Vector(want_normal)) < 0:
            f.normal_flip()
        f[self.reg] = REG_IDS[region]
        return f

    def box(self, lo, hi, region, skip=(), regions=None):
        """Axis-aligned box. skip: subset of '-x +x -y +y -z +z' faces to leave out (hidden faces).
        regions: optional per-face override, e.g. {'+z': 'stone'}."""
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        quads = {
            "-x": ([(x0, y0, z0), (x0, y1, z0), (x0, y1, z1), (x0, y0, z1)], (-1, 0, 0)),
            "+x": ([(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)], (1, 0, 0)),
            "-y": ([(x0, y0, z0), (x0, y0, z1), (x1, y0, z1), (x1, y0, z0)], (0, -1, 0)),
            "+y": ([(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], (0, 1, 0)),
            "-z": ([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)], (0, 0, -1)),
            "+z": ([(x0, y0, z1), (x0, y1, z1), (x1, y1, z1), (x1, y0, z1)], (0, 0, 1)),
        }
        for k, (pts, n) in quads.items():
            if k in skip:
                continue
            self.face(pts, (regions or {}).get(k, region), n)

    def sweep(self, path, radius, sides, region, closed=False, ref=(0, 0, 1), caps=True, twist=0.0):
        """Tube along a polyline. radius: float or per-point list."""
        n = len(path)
        P = [Vector(p) for p in path]
        rads = radius if isinstance(radius, (list, tuple)) else [radius] * n
        rings = []
        for i in range(n):
            if closed:
                t = (P[(i + 1) % n] - P[(i - 1) % n])
            else:
                t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)])
            t.normalize()
            r = Vector(ref)
            if abs(t.dot(r)) > 0.95:
                r = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 1, 0))
            nrm = t.cross(r).normalized()
            bin_ = t.cross(nrm).normalized()
            ring = []
            for s in range(sides):
                a = 2 * math.pi * s / sides + twist
                ring.append(P[i] + (nrm * math.cos(a) + bin_ * math.sin(a)) * rads[i])
            rings.append(ring)
        segs = n if closed else n - 1
        for i in range(segs):
            a, b = rings[i], rings[(i + 1) % n]
            for s in range(sides):
                s2 = (s + 1) % sides
                f = self.face([a[s], a[s2], b[s2], b[s]], region)
                mid = (P[i] + P[(i + 1) % n]) / 2
                if f.normal.dot(f.calc_center_median() - mid) < 0:
                    f.normal_flip()
        if caps and not closed and sides >= 3:
            for ring, d in ((rings[0], P[0] - P[1]), (rings[-1], P[-1] - P[-2])):
                self.face(ring, region, tuple(d))

    def lathe_shell(self, profile, a0, a1, seg, thick, region, rims=True, jitter=0.0, seed=0):
        """Surface of revolution about Z, as a solid shell of given thickness.
        Inner surface at r, outer at r + thick. Open angular range gets cut rims."""
        rnd = random.Random(seed)
        full = abs(a1 - a0 - 2 * math.pi) < 1e-6
        cols = seg if full else seg + 1

        def ring_pts(off):
            grid = []
            for j, (r, z) in enumerate(profile):
                row = []
                for i in range(cols):
                    a = a0 + (a1 - a0) * i / seg
                    jr = rnd.uniform(-jitter, jitter) if jitter and (j == len(profile) - 1 or i in (0, cols - 1)) else 0
                    jz = rnd.uniform(-jitter, jitter) * 2 if jitter and j == len(profile) - 1 else 0
                    rr = r + off + jr
                    row.append(Vector((rr * math.cos(a), rr * math.sin(a), z + jz)))
                grid.append(row)
            return grid
        rnd_state = rnd.getstate()
        inner = ring_pts(0.0)
        rnd.setstate(rnd_state)
        outer = ring_pts(thick)
        nj = len(profile)
        cseg = seg if full else seg
        for j in range(nj - 1):
            for i in range(cseg):
                i2 = (i + 1) % cols
                # inner faces point toward the axis, outer faces away
                fi = self.face([inner[j][i], inner[j][i2], inner[j + 1][i2], inner[j + 1][i]], region)
                c = fi.calc_center_median()
                if fi.normal.dot(Vector((c.x, c.y, 0))) > 0:
                    fi.normal_flip()
                fo = self.face([outer[j][i], outer[j + 1][i], outer[j + 1][i2], outer[j][i2]], region)
                c = fo.calc_center_median()
                if fo.normal.dot(Vector((c.x, c.y, 0))) < 0:
                    fo.normal_flip()
        if rims:
            for j in (0, nj - 1):  # bottom and top rims
                for i in range(cseg):
                    i2 = (i + 1) % cols
                    f = self.face([inner[j][i], outer[j][i], outer[j][i2], inner[j][i2]], region)
                    want = Vector((0, 0, -1 if j == 0 else 1))
                    if f.normal.dot(want) < 0:
                        f.normal_flip()
            if not full:
                for i in (0, cols - 1):
                    for j in range(nj - 1):
                        f = self.face([inner[j][i], inner[j + 1][i], outer[j + 1][i], outer[j][i]], region)
                        a = a0 if i == 0 else a1
                        tang = Vector((-math.sin(a), math.cos(a), 0)) * (-1 if i == 0 else 1)
                        if f.normal.dot(tang) < 0:
                            f.normal_flip()

    def to_object(self, name):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        uv = bm.loops.layers.uv.new("UVMap")
        bm.faces.ensure_lookup_table()
        self.poly_reg = [f[self.reg] for f in bm.faces]
        for f in bm.faces:
            assign_uv(f, uv, REG_NAMES[f[self.reg]])
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = False
        ob = bpy.data.objects.new(name, me)
        ob.data.materials.append(scenery_material())
        if self.beam_alpha:
            ob.data.materials.append(beam_material())
            col = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
            for poly in me.polygons:
                is_beam = REG_NAMES[self.poly_reg[poly.index]] == BEAM_REG
                poly.material_index = 1 if is_beam else 0
                for li in poly.loop_indices:
                    a = self.beam_alpha(me.vertices[me.loops[li].vertex_index].co) if is_beam else 1.0
                    col.data[li].color = (1, 1, 1, a)
        return ob


def assign_uv(f, uvl, region):
    (x0, y0, x1, y1), mode, (su, sv) = REGIONS[region]
    n = f.normal
    ax = max(range(3), key=lambda i: abs(n[i]))
    cos = [l.vert.co for l in f.loops]
    if mode == "swatch":
        cu, cv = (x0 + x1) / 2, (y0 + y1) / 2
        for l in f.loops:
            l[uvl].uv = (cu / ATLAS_PX, 1 - cv / ATLAS_PX)
        return
    if mode == "wall" and ax != 2:
        a_idx = 0 if ax == 1 else 1
        uvs = []
        # which cell slot this face lies in (segments are 4 modules side by side): u restarts at each cell
        cell0 = math.floor((min(c[0] for c in cos) + HALF) / CELL + 1e-4)
        for c in cos:
            u = (c[a_idx] + HALF) / CELL - cell0 if a_idx == 0 else (c[1] % CELL) / CELL
            uvs.append((min(max(u, 0.0), 1.0), min(max(c.z / H, 0.0), 1.0)))
        ins = (0, 0)
    else:
        a_idx, b_idx = {0: (1, 2), 1: (0, 2), 2: (0, 1)}[ax]
        A = [c[a_idx] for c in cos]
        B = [c[b_idx] for c in cos]
        amin, bmin = min(A), min(B)
        span_u = (max(A) - amin) / su
        span_v = (max(B) - bmin) / sv
        k = 1.0 / max(1.0, span_u, span_v)
        uvs = [((a - amin) / su * k, (b - bmin) / sv * k) for a, b in zip(A, B)]
        ins = (2, 2)
    for l, (u, v) in zip(f.loops, uvs):
        px = x0 + ins[0] + u * (x1 - x0 - 2 * ins[0])
        py = y1 - ins[1] - v * (y1 - y0 - 2 * ins[1])
        l[uvl].uv = (px / ATLAS_PX, 1 - py / ATLAS_PX)


_MAT = None


def scenery_material():
    global _MAT
    if _MAT:
        return _MAT
    m = bpy.data.materials.new("T1_Scenery")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(ATLAS, check_existing=True)
    tex.image.colorspace_settings.name = "sRGB"
    tex.interpolation = "Linear"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    em = nt.nodes.new("ShaderNodeTexImage")       # emission mask (window panes only)
    em.image = bpy.data.images.load(EMIT, check_existing=True)
    em.image.colorspace_settings.name = "sRGB"
    nt.links.new(em.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = EMIT_STRENGTH
    bsdf.inputs["Roughness"].default_value = 0.9
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.2
    _MAT = m
    return m


EMIT_STRENGTH = 4.0        # Blender preview; Unity: Emission colour white x map, HDR intensity about +1
BEAM_RGB = (232, 164, 86)  # same warm as the pane
_BEAM = None


def beam_material():
    """Light shaft: additive, colour x vertex alpha (Unity: URP Particles/Unlit, Additive, vertex colour on)."""
    global _BEAM
    if _BEAM:
        return _BEAM
    m = bpy.data.materials.new("T1_Beam")
    m.use_nodes = True
    m.surface_render_method = "BLENDED"
    m.use_backface_culling = False
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    emis = nt.nodes.new("ShaderNodeEmission")
    emis.inputs["Color"].default_value = tuple((c / 255) ** 2.2 for c in BEAM_RGB) + (1,)
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.inputs[1].default_value = 1.2
    nt.links.new(attr.outputs["Alpha"], mul.inputs[0])
    nt.links.new(mul.outputs[0], emis.inputs["Strength"])
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    add = nt.nodes.new("ShaderNodeAddShader")
    nt.links.new(emis.outputs[0], add.inputs[0])
    nt.links.new(tr.outputs[0], add.inputs[1])
    nt.links.new(add.outputs[0], out.inputs["Surface"])
    _BEAM = m
    return m


# ---------------------------------------------------------------- shared wall parts

def plinth_and_cope(b, plinth=True, face_y=0.04, cope_skip=()):
    if plinth:
        b.box((-HALF, 0, 0), (HALF, D, PLINTH), "stone", skip=("-x", "+x", "-z", "+y"))
    b.box((-HALF, -0.12, COPE_Z0), (HALF, D + 0.1, COPE_Z1), "stone", skip=("-x", "+x") + tuple(cope_skip))


def brick_face(b, region, x0=-HALF, x1=HALF, z0=PLINTH, z1=COPE_Z0, y=0.04):
    b.box((x0, y, z0), (x1, D, z1), region, skip=("-x", "+x", "-z", "+z", "+y"))


def alcove(b, x0, x1, z1, depth, back_region, lintel=True):
    """Brick wall with a recessed alcove x0..x1, from plinth top to z1, back face at y=depth."""
    brick_face(b, "brick_a", -HALF, x0)
    brick_face(b, "brick_a", x1, HALF)
    top = z1 + (0.3 if lintel else 0)
    if lintel:
        b.box((x0, 0.0, z1), (x1, D, top), "stone", skip=("-x", "+x", "+y", "+z"))
    brick_face(b, "brick_a", x0, x1, top, COPE_Z0)
    b.box((x0, depth, PLINTH), (x1, D, z1), back_region, skip=("-x", "+x", "-z", "+z", "+y"))
    # jambs and sill
    b.face([(x0, 0.04, PLINTH), (x0, depth, PLINTH), (x0, depth, z1), (x0, 0.04, z1)], "brick_a", (1, 0, 0))
    b.face([(x1, 0.04, PLINTH), (x1, 0.04, z1), (x1, depth, z1), (x1, depth, PLINTH)], "brick_a", (-1, 0, 0))
    b.face([(x0, 0.0, PLINTH), (x1, 0.0, PLINTH), (x1, depth, PLINTH), (x0, depth, PLINTH)], "stone", (0, 0, 1))


def diamond_link(b, centre, turned, half_len=0.13, half_w=0.08, r=0.034):
    cx, cy, cz = centre
    if turned:
        pts = [(cx, cy, cz + half_len), (cx + half_w, cy, cz), (cx, cy, cz - half_len), (cx - half_w, cy, cz)]
        ref = (0, 1, 0)
    else:
        pts = [(cx, cy, cz + half_len), (cx, cy + half_w, cz), (cx, cy, cz - half_len), (cx, cy - half_w, cz)]
        ref = (1, 0, 0)
    b.sweep(pts, r, 3, "sw_chain", closed=True, ref=ref)


def chain(b, x, y, z_top, links, pitch=0.2, **link):
    for i in range(links):
        diamond_link(b, (x, y, z_top - 0.13 - i * pitch), i % 2 == 1, **link)


def post_face(b, x0, x1, y, z0, z1, n=4):
    """Front face of an iron post, split into n stacked quads that each map the riveted 'pilaster' strip."""
    for i in range(n):
        za, zb = z0 + (z1 - z0) * i / n, z0 + (z1 - z0) * (i + 1) / n
        b.face([(x0, y, za), (x0, y, zb), (x1, y, zb), (x1, y, za)], "pilaster", (0, -1, 0))


# ---------------------------------------------------------------- pieces

def wall_brick_a():
    b = Builder()
    plinth_and_cope(b)
    brick_face(b, "brick_a")
    return b


def wall_brick_b():
    b = Builder()
    plinth_and_cope(b, cope_skip=())
    brick_face(b, "brick_b")
    # two loose bricks pushed out of the face (only above z 1.5, so the board edge stays clear)
    for (x, z) in ((-0.2, 2.62), (0.24, 3.46)):
        b.box((x - 0.14, -0.05, z), (x + 0.14, 0.04, z + 0.14), "sw_stone_dk", skip=("+y",))
    # a chipped block sitting on the coping
    b.box((0.1, 0.05, COPE_Z1), (0.42, 0.4, COPE_Z1 + 0.16), "stone", skip=("-z",))
    return b


def wall_iron_rib():
    b = Builder()
    plinth_and_cope(b)
    rec = 0.2
    brick_face(b, "brick_a", y=rec)
    # side returns of the recess so neighbours' faces (at y = 0.04) meet cleanly
    for x, n in ((-HALF, 1), (HALF, -1)):
        pts = [(x, 0.04, PLINTH), (x, rec, PLINTH), (x, rec, COPE_Z0), (x, 0.04, COPE_Z0)]
        b.face(pts, "brick_a", (n, 0, 0))
    b.face([(-HALF, 0.04, PLINTH), (HALF, 0.04, PLINTH), (HALF, rec, PLINTH), (-HALF, rec, PLINTH)], "stone", (0, 0, 1))
    # I-beam: flange flush with the collision face, web back into the recess
    post_face(b, -0.16, 0.16, 0.0, PLINTH, COPE_Z0)
    b.box((-0.16, 0.0, PLINTH), (0.16, 0.07, COPE_Z0), "iron", skip=("-z", "+z", "+y", "-y"))
    b.box((-0.04, 0.07, PLINTH), (0.04, rec, COPE_Z0), "sw_iron", skip=("-z", "+z", "+y", "-y"))
    # tie band near the top + rivets
    b.box((-HALF, -0.02, 3.86), (HALF, rec, 4.1), "iron", skip=("+y", "-x", "+x"))
    for x in (-0.42, -0.26, 0.26, 0.42):
        b.box((x - 0.025, -0.05, 3.955), (x + 0.025, -0.02, 4.005), "sw_iron_hi", skip=("+y",))
    return b


def wall_chain(strands=((-0.2, 5), (0.02, 8), (0.22, 4)), slack=False):
    b = Builder()
    plinth_and_cope(b)
    alcove(b, -0.42, 0.42, 3.75, 0.34, "brick_b")
    # hoist bracket across the alcove top + chain strands
    b.box((-0.4, 0.06, 3.58), (0.4, 0.34, 3.72), "iron", skip=("+y",))
    for x, n in strands:
        chain(b, x, 0.12, 3.58, n)
    if slack:  # slack loop: a short strand hooked across
        for i, x in enumerate((-0.14, -0.07, 0.0, 0.07, 0.14)):
            diamond_link(b, (x, 0.1, 1.6 - abs(x) * 1.2), i % 2 == 0, half_len=0.07, half_w=0.045, r=0.024)
    return b


def wall_mould():
    b = Builder()
    plinth_and_cope(b)
    depth = 0.52
    alcove(b, -0.46, 0.46, 3.4, depth, "brick_b")
    # broken half of a bell-casting mould, standing in the alcove with its hollow facing the board
    prof = [(0.40, PLINTH), (0.39, 0.9), (0.33, 1.55), (0.27, 2.1), (0.2, 2.5), (0.12, 2.72)]
    s = Builder()
    s.lathe_shell(prof, 0.0, math.pi, 6, 0.07, "clay", jitter=0.03, seed=7)
    # move shell into this piece: axis at y = depth - 0.47 so the outer back touches the alcove wall
    for v in s.bm.verts:
        v.co.y += depth - 0.47
    _merge(b, s)
    # cracked-off piece lying on the sill
    b.box((0.12, 0.02, PLINTH), (0.34, 0.2, PLINTH + 0.1), "clay", skip=("-z",))
    return b


def wall_trough():
    b = Builder()
    plinth_and_cope(b, plinth=False)
    nx, nz, nd = 0.46, 1.3, 0.62
    # brick face around a low niche, stone lintel above it
    brick_face(b, "brick_a", -HALF, -nx, 0.0)
    brick_face(b, "brick_a", nx, HALF, 0.0)
    b.box((-nx, 0.0, nz), (nx, D, nz + 0.3), "stone", skip=("-x", "+x", "+y", "+z"))
    brick_face(b, "brick_a", -nx, nx, nz + 0.3)
    b.box((-nx, nd, 0), (nx, D, nz), "brick_b", skip=("-x", "+x", "-z", "+z", "+y"))
    b.face([(-nx, 0.04, 0), (-nx, nd, 0), (-nx, nd, nz), (-nx, 0.04, nz)], "brick_a", (1, 0, 0))
    b.face([(nx, 0.04, 0), (nx, 0.04, nz), (nx, nd, nz), (nx, nd, 0)], "brick_a", (-1, 0, 0))
    # cooling trough: stone rims around dark water, inside the niche (y >= 0)
    tx, ty0, ty1, th, w = 0.43, 0.02, 0.6, 0.55, 0.07
    b.box((-tx, ty0, 0), (tx, ty0 + w, th), "stone", skip=("-z",))
    b.box((-tx, ty1 - w, 0), (tx, ty1, th), "stone", skip=("-z", "+y"))
    b.box((-tx, ty0 + w, 0), (-tx + w, ty1 - w, th), "stone", skip=("-z", "-y", "+y"))
    b.box((tx - w, ty0 + w, 0), (tx, ty1 - w, th), "stone", skip=("-z", "-y", "+y"))
    b.face([(-tx + w, ty0 + w, th - 0.1), (-tx + w, ty1 - w, th - 0.1), (tx - w, ty1 - w, th - 0.1), (tx - w, ty0 + w, th - 0.1)], "sw_water", (0, 0, 1))
    # spout pipe out of the back wall
    b.sweep([(0.18, nd, 1.0), (0.18, 0.36, 1.0), (0.18, 0.3, 0.9)], 0.045, 5, "sw_iron", ref=(0, 0, 1))
    return b


def corner_pillar():
    """Pier: fills one wall slot (1 cell), deeper and taller than a segment. Also closes run ends / corners."""
    b = Builder()
    depth = D + 0.35
    blocks = [(0.0, 1.2), (1.2, 2.4), (2.4, 3.6), (3.6, 4.85)]
    for i, (z0, z1) in enumerate(blocks):
        inset = 0.0 if i % 2 == 0 else 0.03
        bm_before = len(b.bm.faces)
        b.box((-HALF + inset, 0.0 + inset, z0), (HALF - inset, depth, z1), "stone", skip=("+y", "-z"))
    # chamfer the two front vertical edges of every block
    bm = b.bm
    bm.verts.ensure_lookup_table()
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    edges = [e for e in bm.edges
             if abs(e.verts[0].co.x - e.verts[1].co.x) < 1e-6 and abs(e.verts[0].co.y - e.verts[1].co.y) < 1e-6
             and e.verts[0].co.y < 0.05 and abs(abs(e.verts[0].co.x) - HALF) < 0.05]
    bmesh.ops.bevel(bm, geom=edges, offset=0.09, segments=1, affect="EDGES", profile=0.5)
    for f in bm.faces:  # bevel faces inherit layer 0 -> force stone
        if f[b.reg] != REG_IDS["stone"]:
            f[b.reg] = REG_IDS["stone"]
    # cap stone
    b.box((-HALF - 0.06, -0.14, 4.85), (HALF + 0.06, depth + 0.1, 5.15), "stone", skip=("-z",))
    # iron strap with a copper-tinted plate (the only accent on the walls)
    b.box((-HALF - 0.01, -0.03, 3.0), (HALF + 0.01, depth, 3.16), "iron", skip=("+y", "-z", "+z"))
    b.box((-0.12, -0.06, 2.9), (0.12, -0.02, 3.26), "sw_copper", skip=("+y",))
    return b


def floor_tile(name, region):
    b = Builder()
    b.face([(-HALF, -HALF, 0), (HALF, -HALF, 0), (HALF, HALF, 0), (-HALF, HALF, 0)], region, (0, 0, 1))
    return b.to_object(name)


def prop_hook():
    """Hoist hook on three links. Origin = the hanging point (top of the chain)."""
    b = Builder()
    chain(b, 0, 0, 0, 3, pitch=0.15, half_len=0.085, half_w=0.05, r=0.02)
    top = -0.45
    path = [(0, 0, top), (0, 0, top - 0.35)]
    cx, cz, r = 0.2, top - 0.35, 0.2
    for i in range(1, 9):
        a = math.pi + i * (math.pi + 0.7) / 8
        path.append((cx + r * math.cos(a), 0, cz + r * math.sin(a)))
    rads = [0.055, 0.06] + [0.06 - 0.04 * i / 8 for i in range(1, 9)]
    b.sweep(path, rads, 4, "sw_iron", ref=(0, 1, 0), twist=math.pi / 4)
    # eye block where the chain meets the shank
    b.box((-0.06, -0.06, top - 0.04), (0.06, 0.06, top + 0.05), "sw_iron_hi")
    return b


def prop_bell_shard():
    """Curved shard of a cracked bell. Origin = floor contact, centre of the bottom edge."""
    b = Builder()
    prof = [(1.0, 0.0), (0.93, 0.22), (0.8, 0.55), (0.72, 0.9), (0.7, 1.12)]
    b.lathe_shell(prof, -0.55, 0.55, 4, 0.08, "bronze", jitter=0.06, seed=3)
    for v in b.bm.verts:  # recentre: bottom-edge centre to origin, convex side toward -Y
        v.co.x -= 1.0
    rot = Matrix.Rotation(math.radians(90), 4, "Z")
    bmesh.ops.transform(b.bm, matrix=rot, verts=b.bm.verts)
    return b


def prop_ladle():
    """Long-handled foundry ladle. Origin = bottom of the bowl."""
    b = Builder()
    prof = [(0.03, 0.0), (0.15, 0.04), (0.21, 0.12), (0.23, 0.24)]
    b.lathe_shell(prof, 0, 2 * math.pi, 7, 0.025, "sw_rust")
    b.sweep([(0.2, 0, 0.22), (0.45, 0, 0.6), (0.9, 0, 1.45)], 0.032, 5, "sw_iron", ref=(0, 1, 0))
    b.box((0.86, -0.05, 1.4), (0.98, 0.05, 1.62), "sw_wood")
    return b


def _merge(dst, src):
    """Copy src builder's faces into dst (keeping region tags)."""
    vmap = {}
    for v in src.bm.verts:
        vmap[v] = dst.bm.verts.new(v.co)
    for f in src.bm.faces:
        nf = dst.bm.faces.new([vmap[v] for v in f.verts])
        nf[dst.reg] = f[src.reg]
        nf.normal_update()
    src.bm.free()


def merge_into(dst, src, mat=None):
    """Copy src builder's faces into dst (keeping region tags), optionally transformed. No negative scales."""
    vmap = {}
    for v in src.bm.verts:
        vmap[v] = dst.bm.verts.new(mat @ v.co if mat is not None else v.co)
    for f in src.bm.faces:
        try:
            nf = dst.bm.faces.new([vmap[v] for v in f.verts])
        except ValueError:  # duplicate face
            continue
        nf[dst.reg] = f[src.reg]
        nf.normal_update()
    src.bm.free()


def mod_buttress():
    """End-set pier: deeper, taller, stepped back as it rises, iron-strapped. Front stays at y = 0."""
    b = Builder()
    depth = D + 0.85
    for z0, z1, ins in ((0.0, 1.7, 0.0), (1.7, 3.6, 0.05), (3.6, 5.4, 0.1)):
        b.box((-HALF + ins, 0.0, z0), (HALF - ins, depth - ins * 3, z1), "stone", skip=("+y", "-z"))
    b.box((-HALF - 0.06, -0.12, 5.4), (HALF + 0.06, depth - 0.2, 5.75), "stone", skip=("-z",))
    for z in (2.0, 4.0):
        b.box((-HALF - 0.01, -0.03, z), (HALF + 0.01, depth - 0.3, z + 0.18), "iron", skip=("+y", "-z"))
        b.box((-0.13, -0.07, z - 0.08), (0.13, -0.03, z + 0.26), "sw_copper", skip=("+y",))
    return b


def mod_collapse():
    """End/mid module: the wall top has fallen in. Left part keeps its coping, the rest is a ragged stub."""
    b = Builder()
    stub, mid_top, cut = 3.2, 3.85, -0.2
    b.box((-HALF, 0, 0), (HALF, D, PLINTH), "stone", skip=("-x", "+x", "-z", "+y"))
    brick_face(b, "brick_b", -HALF, cut, PLINTH, COPE_Z0)
    b.box((-HALF, -0.12, COPE_Z0), (cut, D + 0.1, COPE_Z1), "stone", skip=("-x",))
    brick_face(b, "brick_b", cut, 0.18, PLINTH, mid_top)
    brick_face(b, "brick_b", 0.18, HALF, PLINTH, stub)
    for x0, x1, z in ((cut, 0.18, mid_top), (0.18, HALF, stub)):  # broken tops
        b.face([(x0, 0.04, z), (x0, D, z), (x1, D, z), (x1, 0.04, z)], "stone", (0, 0, 1))
    # exposed brick ends where the wall steps down
    b.face([(cut, 0.04, mid_top), (cut, D, mid_top), (cut, D, COPE_Z0), (cut, 0.04, COPE_Z0)], "brick_b", (1, 0, 0))
    b.face([(0.18, 0.04, stub), (0.18, D, stub), (0.18, D, mid_top), (0.18, 0.04, mid_top)], "brick_b", (1, 0, 0))
    # close the right neighbour's open wall end and coping end
    b.face([(HALF, 0.04, stub), (HALF, 0.04, COPE_Z0), (HALF, D, COPE_Z0), (HALF, D, stub)], "brick_b", (-1, 0, 0))
    b.face([(HALF, -0.12, COPE_Z0), (HALF, -0.12, COPE_Z1), (HALF, D + 0.1, COPE_Z1), (HALF, D + 0.1, COPE_Z0)], "stone", (-1, 0, 0))
    # rubble lying on the stub
    for (x, y, z, sx, sy, sz, rz) in ((0.0, 0.3, mid_top, 0.22, 0.16, 0.12, 20), (0.42, 0.55, stub, 0.2, 0.14, 0.14, -35)):
        r = Builder()
        r.box((-sx / 2, -sy / 2, 0), (sx / 2, sy / 2, sz), "sw_stone_dk", skip=("-z",))
        merge_into(b, r, Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, "Z"))
    return b


def mod_window_pier():
    """Tall pier with a pointed-arch window high up; the pane glows (emission mask) and a light shaft falls
    from it into the corridor. Front on the collision line like the other piers."""
    b = Builder()
    depth = D + 0.35
    wx, z0, z1, apex, top, rec = 0.3, 3.5, 5.45, 5.8, 6.0, 0.3
    b.box((-HALF, 0.0, 0.0), (HALF, depth, z0), "stone", skip=("+y", "-z"))
    b.box((-HALF, 0.0, z0), (-wx, depth, top), "stone", skip=("+y", "-z"))
    b.box((wx, 0.0, z0), (HALF, depth, top), "stone", skip=("+y", "-z"))
    b.box((-wx, 0.0, apex), (wx, depth, top), "stone", skip=("+y", "-z", "-x", "+x"))
    for sx in (-1, 1):   # arch spandrels and soffits
        b.face([(sx * wx, 0, z1), (0, 0, apex), (sx * wx, 0, apex)], "stone", (0, -1, 0))
        b.face([(sx * wx, 0, z1), (sx * wx, rec, z1), (0, rec, apex), (0, 0, apex)], "stone", (0, 0, -1))
    b.face([(-wx, rec, z0), (wx, rec, z0), (wx, rec, z1), (0, rec, apex), (-wx, rec, z1)], "glow", (0, -1, 0))
    b.box((-HALF - 0.08, -0.16, top), (HALF + 0.08, depth + 0.1, top + 0.32), "stone", skip=("-z",))
    b.box((-HALF - 0.01, -0.03, 3.0), (HALF + 0.01, depth, 3.16), "iron", skip=("+y", "-z", "+z"))
    # glowing reveals: sill (seen from above) and jambs carry the pane colour, so the opening reads as lit
    b.face([(-wx, 0.0, z0), (-wx, rec, z0), (wx, rec, z0), (wx, 0.0, z0)], "glow", (0, 0, 1))
    b.face([(-wx, 0.0, z0), (-wx, 0.0, z1), (-wx, rec, z1), (-wx, rec, z0)], "glow", (1, 0, 0))
    b.face([(wx, 0.0, z0), (wx, rec, z0), (wx, rec, z1), (wx, 0.0, z1)], "glow", (-1, 0, 0))
    # warm light pool on the lane floor under the window (additive fan, alpha 0 at the rim), off the board
    cx, cy, rx, ry, n, pz = -0.3, -0.55, 1.15, 0.48, 12, 0.05   # pz above the raised floor slabs
    rim = [(cx + rx * math.cos(2 * math.pi * i / n), cy + ry * math.sin(2 * math.pi * i / n), pz) for i in range(n)]
    for i in range(n):
        b.face([(cx, cy, pz), rim[i], rim[(i + 1) % n]], BEAM_REG, (0, 0, 1))
    b.beam_alpha = lambda co: max(0.0, 0.42 * (1 - math.hypot((co.x - cx) / rx, (co.y - cy) / ry)))
    return b


# ---------------------------------------------------------------- prop lane (one cell between board and wall)
LANE_W = CELL          # lane width; the kerb front is the board edge / collision line
KERB_W, KERB_H = 0.2, 0.14


def lane_trough(b, x, ln=1.5):
    y0, y1, th, w = -0.86, -0.22, 0.44, 0.08
    x0, x1 = x - ln / 2, x + ln / 2
    b.box((x0, y0, 0), (x1, y0 + w, th), "stone", skip=("-z",))
    b.box((x0, y1 - w, 0), (x1, y1, th), "stone", skip=("-z", "+y"))
    b.box((x0, y0 + w, 0), (x0 + w, y1 - w, th), "stone", skip=("-z", "-y", "+y"))
    b.box((x1 - w, y0 + w, 0), (x1, y1 - w, th), "stone", skip=("-z", "-y", "+y"))
    b.face([(x0 + w, y0 + w, th - 0.1), (x1 - w, y0 + w, th - 0.1), (x1 - w, y1 - w, th - 0.1), (x0 + w, y1 - w, th - 0.1)], "sw_water", (0, 0, 1))


MOULD_PROF = [(0.40, 0.0), (0.39, 0.55), (0.33, 1.2), (0.27, 1.75), (0.2, 2.15), (0.12, 2.37)]


def lane_mould(b, x, y=-0.5, tipped=False, rz=0.0, s=0.55, seed=11):
    m = Builder()
    m.lathe_shell(MOULD_PROF, 0.0, math.pi, 6, 0.07, "clay", jitter=0.03, seed=seed)
    if tipped:   # lying on its side along the lane, hollow toward the board
        mat = Matrix.Translation((x, y, 0.40 * s)) @ Matrix.Rotation(math.radians(rz), 4, "Z") \
            @ Matrix.Rotation(math.radians(90), 4, "Y") @ Matrix.Rotation(math.radians(180), 4, "Z") @ Matrix.Scale(s, 4)
    else:        # standing, hollow toward the board
        mat = Matrix.Translation((x, y + 0.2, 0)) @ Matrix.Rotation(math.radians(rz), 4, "Z") @ Matrix.Scale(s, 4)
    merge_into(b, m, mat)


def lane_bell(b, x, y=-0.5, broken=False, tilt=0.0, rz=0.0):
    m = Builder()
    prof = [(0.42, 0.0), (0.40, 0.08), (0.31, 0.3), (0.26, 0.58), (0.26, 0.82), (0.18, 0.95), (0.06, 1.0)]
    a1 = 2 * math.pi - (1.3 if broken else 0.0)
    m.lathe_shell(prof, 0.0, a1, 8, 0.045, "bronze", rims=True)
    m.box((-0.07, -0.03, 1.0), (0.07, 0.03, 1.12), "sw_bronze", skip=("-z",))
    merge_into(b, m, Matrix.Translation((x, y, 0)) @ Matrix.Rotation(math.radians(rz), 4, "Z")
               @ Matrix.Rotation(math.radians(tilt), 4, "X") @ Matrix.Scale(0.85, 4))


def lane_rubble(b, x, y=-0.45, n=4, seed=1):
    rnd = random.Random(seed)
    for i in range(n):
        sx, sy, sz = rnd.uniform(0.14, 0.3), rnd.uniform(0.12, 0.24), rnd.uniform(0.08, 0.18)
        r = Builder()
        r.box((-sx / 2, -sy / 2, 0), (sx / 2, sy / 2, sz), rnd.choice(("stone", "sw_stone_dk")), skip=("-z",))
        merge_into(b, r, Matrix.Translation((x + rnd.uniform(-0.35, 0.35), y + rnd.uniform(-0.25, 0.25), 0))
                   @ Matrix.Rotation(math.radians(rnd.uniform(0, 90)), 4, "Z"))


def lane_chain_pile(b, x, y=-0.5):
    for (dx, dy, ang, n) in ((-0.5, 0.05, 10, 5), (0.15, -0.12, -25, 4), (0.55, 0.12, 60, 3)):
        c = Builder()
        chain(c, 0, 0, 0, n)
        merge_into(b, c, Matrix.Translation((x + dx, y + dy, 0.07)) @ Matrix.Rotation(math.radians(ang), 4, "Z")
                   @ Matrix.Rotation(math.radians(90), 4, "Y"))


def lane_ladle(b, x, y=-0.32, lying=False):
    m = prop_ladle()
    if lying:
        mat = Matrix.Translation((x, y - 0.1, 0.03)) @ Matrix.Rotation(math.radians(-8), 4, "Z") @ Matrix.Rotation(math.radians(-74), 4, "Y")
    else:        # leaning on the wall, handle up toward +x
        mat = Matrix.Translation((x, y, 0)) @ Matrix.Rotation(math.radians(12), 4, "Z") @ Matrix.Rotation(math.radians(-8), 4, "X")
    merge_into(b, m, mat)


def lane_shard(b, x, y=-0.5, rz=0.0):
    merge_into(b, prop_bell_shard(), Matrix.Translation((x, y, 0)) @ Matrix.Rotation(math.radians(rz), 4, "Z")
               @ Matrix.Rotation(math.radians(-18), 4, "X") @ Matrix.Scale(0.55, 4))


LANES = {
    "T1_Lane_Start_A": ("start", lambda b: (lane_trough(b, -0.9), lane_ladle(b, 1.0))),
    "T1_Lane_Start_B": ("start", lambda b: (lane_trough(b, -1.2, 1.4), lane_trough(b, 1.05, 1.4))),
    "T1_Lane_Start_C": ("start", lambda b: (lane_mould(b, -1.25), lane_rubble(b, 0.4, seed=3), lane_ladle(b, 1.4, lying=True))),
    "T1_Lane_Mid_A": ("mid", lambda b: (lane_mould(b, -1.35), lane_mould(b, 0.25, tipped=True, rz=8, seed=5), lane_shard(b, 1.6, rz=20))),
    "T1_Lane_Mid_B": ("mid", lambda b: (lane_trough(b, -0.95), lane_bell(b, 1.25))),
    "T1_Lane_Mid_C": ("mid", lambda b: (lane_mould(b, -1.0, tipped=True, rz=-6), lane_ladle(b, 0.9), lane_rubble(b, 1.75, n=3, seed=8))),
    "T1_Lane_End_A": ("end", lambda b: (lane_bell(b, -0.9, broken=True, tilt=-8, rz=30), lane_shard(b, 0.55, rz=-30), lane_shard(b, 1.5, rz=50), lane_rubble(b, 0.1, n=3, seed=9))),
    "T1_Lane_End_B": ("end", lambda b: (lane_mould(b, -1.3, tipped=True, rz=12, seed=4), lane_rubble(b, 0.4, n=6, seed=12), lane_rubble(b, 1.5, n=4, seed=13))),
    "T1_Lane_End_C": ("end", lambda b: (lane_chain_pile(b, -0.6), lane_rubble(b, 1.2, n=5, seed=14))),
}


def lane(name, dress):
    """Prop lane for one 4-row group, same origin and orientation as the wall segment beside it (origin on the
    wall line). Kerb front at y = -LANE_W = the board edge; props stay in -LANE_W + KERB_W .. 0."""
    b = Builder()
    b.box((-SEG_LEN / 2, -LANE_W, 0), (SEG_LEN / 2, -LANE_W + KERB_W, KERB_H), "stone", skip=("-x", "+x", "-z"))
    dress(b)
    return b.to_object(name)


MODULES = {
    "brick_a": wall_brick_a, "brick_b": wall_brick_b, "rib": wall_iron_rib,
    "chain": wall_chain,
    "chain_heavy": lambda: wall_chain(((-0.2, 6), (0.2, 7)), slack=True),
    "mould": wall_mould, "trough": wall_trough, "pillar": corner_pillar,
    "buttress": mod_buttress, "collapse": mod_collapse, "window": mod_window_pier,
}

# props baked into a module: (builder fn, matrix in the module's local frame)
PROP_MOUNTS = {
    "hook": (prop_hook, lambda: Matrix.Translation((0, -0.2, 3.86))),
    "ladle": (prop_ladle, lambda: Matrix.Translation((0.3, 0.28, 0.45)) @ Matrix.Rotation(math.radians(200), 4, "Z")
              @ Matrix.Rotation(math.radians(-12), 4, "Y")),
    "shard": (prop_bell_shard, lambda: Matrix.Translation((0.34, 0.42, 3.2)) @ Matrix.Rotation(math.radians(10), 4, "Z")
              @ Matrix.Rotation(math.radians(-15), 4, "X") @ Matrix.Scale(0.6, 4)),
}

SEG_CELLS = 4                     # one segment = one recycling group = 4 rows along the corridor
SEG_LEN = SEG_CELLS * CELL        # 4.5

# Wall segments. First and last slot are always a plain brick module, so every segment ends in the same
# profile (plinth, brick face at y = 0.04, coping) and any segment joins any other, across sets too.
SEGMENTS = {
    # start: the town-side upper foundry, intact brick, iron ribs, hoists
    "T1_Start_A": ("start", ["brick_a", ("rib", "hook"), "chain", "brick_a"]),
    "T1_Start_B": ("start", ["brick_a", "window", "rib", "brick_a"]),
    "T1_Start_C": ("start", ["brick_a", ("trough", "ladle"), ("rib", "hook"), "brick_a"]),
    # mid: the working yard, sooty and damaged
    "T1_Mid_A": ("mid", ["brick_b", "mould", "window", "brick_a"]),
    "T1_Mid_B": ("mid", ["brick_a", ("trough", "ladle"), "brick_b", "brick_b"]),
    "T1_Mid_C": ("mid", ["brick_b", "chain", "pillar", "brick_b"]),
    # end: the approach to the casting pit, buttressed, collapsing, heavy chains
    "T1_End_A": ("end", ["brick_b", "window", "mould", "brick_b"]),
    "T1_End_B": ("end", ["brick_b", ("collapse", "shard"), "buttress", "brick_b"]),
    "T1_End_C": ("end", ["brick_b", "chain_heavy", "buttress", "brick_b"]),
}


REC = 0.16           # brick panels sit this far behind the collision line; iron posts stand on it
POST_HW = 0.14       # post half width
PIERS = {"pillar", "buttress", "window"}               # full-depth modules: stay on the line, no post beside them
NO_POST = PIERS | {"collapse"}


def post(b, x, half=None):
    """Riveted iron post at a cell boundary x, front on the collision line, with a big cap block on top.
    half: None = full post, -1 / +1 = only the half toward -x / +x (segment ends; two halves make one post)."""
    x0 = x - POST_HW if half != 1 else x
    x1 = x + POST_HW if half != -1 else x
    skip_x = ("+x",) if half == -1 else ("-x",) if half == 1 else ()
    top = COPE_Z1 + 0.25
    post_face(b, x0, x1, 0.0, 0.0, COPE_Z0)
    b.box((x0, 0.0, 0.0), (x1, REC + 0.04, COPE_Z0), "iron", skip=("-y", "+y", "-z", "+z") + skip_x)
    cx0 = x - 0.3 if half != 1 else x
    cx1 = x + 0.3 if half != -1 else x
    b.box((cx0, -0.2, COPE_Z0 - 0.1), (cx1, D + REC + 0.2, top), "stone", skip=("+y", "-z") + skip_x)


def segment(name, mods):
    b = Builder()
    keys = [m if isinstance(m, str) else m[0] for m in mods]
    for i, m in enumerate(mods):
        key, prop = (m, None) if isinstance(m, str) else m
        mb = MODULES[key]()
        if prop:
            fn, mat = PROP_MOUNTS[prop]
            merge_into(mb, fn(), mat())
        rec = 0.0 if key in PIERS else REC
        if mb.beam_alpha:   # alpha is defined in the module frame: undo the cell shift
            dx = (i - (SEG_CELLS - 1) / 2) * CELL
            b.beam_alpha = lambda co, f=mb.beam_alpha, dx=dx, dy=rec: f(Vector((co.x - dx, co.y - dy, co.z)))
        merge_into(b, mb, Matrix.Translation(((i - (SEG_CELLS - 1) / 2) * CELL, rec, 0)))
    # iron posts on cell boundaries (half posts at both ends, so every segment joint gets one full post)
    xs = [(j - SEG_CELLS / 2) * CELL for j in range(SEG_CELLS + 1)]
    for j, x in enumerate(xs):
        if j == 0:
            post(b, x, half=1)
        elif j == SEG_CELLS:
            post(b, x, half=-1)
        elif keys[j - 1] not in NO_POST and keys[j] not in NO_POST:
            post(b, x)
    return b.to_object(name)


SLAB_H, SLAB_BEV = 0.03, 0.04    # floor slab: raised top, chamfered edge (reads as the concept's chunky flagstones)


def floor_strip(cols, variant):
    """One recycling group of floor: SEG_CELLS rows x cols bevelled slabs, origin at the group centre, floor
    level. Seamless along the corridor: every slab is the same, chamfered down to z = 0 at the cell edge."""
    b = Builder()
    rnd = random.Random(cols * 31 + variant)
    p_b = 0.1 if variant == 0 else 0.3
    for i in range(cols):
        for j in range(SEG_CELLS):
            cx, cy = (i - (cols - 1) / 2) * CELL, (j - (SEG_CELLS - 1) / 2) * CELL
            reg = "floor_b" if rnd.random() < p_b else ("floor_a" if (i + j) % 2 == 0 else "floor_c")
            h = SLAB_H + rnd.uniform(-0.012, 0.012)
            o, t = HALF, HALF - SLAB_BEV
            lo = [(cx - o, cy - o, 0), (cx + o, cy - o, 0), (cx + o, cy + o, 0), (cx - o, cy + o, 0)]
            hi = [(cx - t, cy - t, h), (cx + t, cy - t, h), (cx + t, cy + t, h), (cx - t, cy + t, h)]
            b.face(hi, reg, (0, 0, 1))
            for e in range(4):
                a, c = e, (e + 1) % 4
                mid = ((lo[a][0] + lo[c][0]) / 2 - cx, (lo[a][1] + lo[c][1]) / 2 - cy, 0.5)
                b.face([lo[a], lo[c], hi[c], hi[a]], "sw_mortar", mid)
    return b.to_object(f"T1_FloorStrip_W{cols}_{'AB'[variant]}")


GATE_SPAN = 17.0   # lintel length = tower centre to tower centre for a 13-column corridor (14.625 + 2 x 1.2)


def gate_tower():
    """Casting-pit gate tower (end-of-corridor backdrop). Origin: floor, centre of the front face. Symmetric."""
    b = Builder()
    dp = 3.0
    b.box((-1.3, 0, 0), (1.3, dp + 0.2, 1.2), "stone", skip=("-z", "+y"))
    b.box((-1.2, 0.1, 1.2), (-0.55, dp, 8.6), "stone", skip=("-z", "+y", "+z"))
    b.box((0.55, 0.1, 1.2), (1.2, dp, 8.6), "stone", skip=("-z", "+y", "+z"))
    b.box((-0.55, 0.7, 1.2), (0.55, dp, 7.4), "brick_b", skip=("-z", "+y", "+z", "-x", "+x"))
    b.face([(-0.55, 0.1, 1.2), (-0.55, 0.7, 1.2), (-0.55, 0.7, 7.4), (-0.55, 0.1, 7.4)], "stone", (1, 0, 0))
    b.face([(0.55, 0.1, 1.2), (0.55, 0.1, 7.4), (0.55, 0.7, 7.4), (0.55, 0.7, 1.2)], "stone", (-1, 0, 0))
    b.box((-0.55, 0.1, 7.4), (0.55, dp, 8.6), "stone", skip=("+y", "+z"))
    b.box((-1.25, 0.05, 5.0), (1.25, dp, 5.25), "iron", skip=("+y",))
    b.box((-1.35, -0.05, 8.6), (1.35, dp + 0.1, 9.2), "stone", skip=("+y",))
    for x in (-1.0, 0.0, 1.0):
        b.box((x - 0.27, 0.0, 9.2), (x + 0.27, 0.8, 9.8), "stone", skip=("-z",))
    return b.to_object("T1_Gate_Tower")


def gate_lintel():
    """Lintel across the corridor with a hanging, half-broken bell cradle. Origin: floor level, centre, front face.
    Built for a 13-column corridor; scale X by (cols*1.125 + 2.4) / 17 for narrower ones (it sits deep in fog)."""
    b = Builder()
    n = 8
    w = GATE_SPAN / n
    for i in range(n):
        x0 = -GATE_SPAN / 2 + i * w
        skip = ["+y"] + (["-x"] if i else []) + (["+x"] if i < n - 1 else [])
        b.box((x0, 0, 8.0), (x0 + w, 1.8, 9.4), "stone", skip=tuple(skip))
    b.box((-0.6, -0.12, 7.7), (0.6, 1.9, 9.8), "stone", skip=("+y",))
    for s in (-1, 1):
        x0, x1 = sorted((s * GATE_SPAN / 2, s * (GATE_SPAN / 2 - 1.3)))
        b.box((x0, 0.2, 7.2), (x1, 1.6, 8.0), "stone", skip=("+z", "+y"))
    # hanging cradle: two chains of big links, the left one shorter, so the yoke hangs tilted
    big = dict(half_len=0.24, half_w=0.14, r=0.05)
    for x, links in ((-1.8, 5), (1.8, 7)):
        for i in range(links):
            diamond_link(b, (x, 0.8, 8.0 - 0.24 - i * 0.38), i % 2 == 1, **big)
    yoke = Builder()
    yoke.box((-2.4, -0.25, -0.2), (2.4, 0.25, 0.2), "iron")
    merge_into(b, yoke, Matrix.Translation((0, 0.8, 8.0 - 0.38 * 6)) @ Matrix.Rotation(math.radians(-6), 4, "Y"))
    # snapped chain ends dangling near the towers
    for x, links in ((-5.6, 4), (5.2, 3)):
        for i in range(links):
            diamond_link(b, (x, 0.9, 8.0 - 0.24 - i * 0.38), i % 2 == 1, **big)
    return b.to_object("T1_Gate_Lintel")


def _prop(fn, name):
    return lambda: fn().to_object(name)


PIECES = (
    [("segment:" + SEGMENTS[n][0], (lambda n=n: segment(n, SEGMENTS[n][1]))) for n in SEGMENTS]
    + [("lane:" + LANES[n][0], (lambda n=n: lane(n, LANES[n][1]))) for n in LANES]
    + [("floor", (lambda c=c, v=v: floor_strip(c, v))) for c in (9, 11, 13, 15) for v in (0, 1)]
    + [("prop", _prop(prop_hook, "T1_Prop_Hook")), ("prop", _prop(prop_bell_shard, "T1_Prop_BellShard")),
       ("prop", _prop(prop_ladle, "T1_Prop_Ladle"))]
    + [("gate", gate_tower), ("gate", gate_lintel)]
)


def unity_vertex_count(me):
    """Unity splits a vertex wherever normal or UV differ. With flat shading: unique (vert, face normal, uv)."""
    me.calc_loop_triangles()
    uv = me.uv_layers.active.data
    keys = set()
    for p in me.polygons:
        n = tuple(round(c, 4) for c in p.normal)
        for li in p.loop_indices:
            l = me.loops[li]
            keys.add((l.vertex_index, n, tuple(round(c, 5) for c in uv[li].uv)))
    return len(keys), len(me.loop_triangles)


def build_all(link_to=None):
    coll = link_to or bpy.context.scene.collection
    objs = {}
    for kind, fn in PIECES:
        ob = fn()
        ob["kit_kind"] = kind
        coll.objects.link(ob)
        objs[ob.name] = ob
    return objs


def export_piece(ob, fbx_dir, glb_dir):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    loc = ob.location.copy()
    ob.location = (0, 0, 0)
    bpy.ops.export_scene.fbx(filepath=os.path.join(fbx_dir, ob.name + ".fbx"), use_selection=True,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", path_mode="STRIP",
                             embed_textures=False, add_leaf_bones=False)
    bpy.ops.export_scene.gltf(filepath=os.path.join(glb_dir, ob.name + ".glb"), use_selection=True,
                              export_format="GLB", export_image_format="NONE", export_apply=True)
    ob.location = loc


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit = bpy.data.collections.new("Kit")
    bpy.context.scene.collection.children.link(kit)
    objs = build_all(kit)
    fbx_dir = os.path.join(ROOT, "kit", "fbx")
    glb_dir = os.path.join(ROOT, "kit", "glb")
    for d_ in (fbx_dir, glb_dir):  # drop exports of pieces that no longer exist
        for fn in os.listdir(d_):
            if fn.startswith("T1_"):
                os.remove(os.path.join(d_, fn))
    report = {}
    for name, ob in objs.items():
        v, t = unity_vertex_count(ob.data)
        bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
        dims = [round(max(c[i] for c in bb) - min(c[i] for c in bb), 3) for i in range(3)]
        report[name] = {"kind": ob["kit_kind"], "unity_verts": v, "tris": t, "size_xyz_blender": dims,
                        "budget_ok": v <= VERT_BUDGET,
                        "modules": [m if isinstance(m, str) else "+".join(m) for m in SEGMENTS[name][1]] if name in SEGMENTS else None}
        export_piece(ob, fbx_dir, glb_dir)
        print(f"KIT {name:22s} verts {v:5d} tris {t:5d} size {dims}")
    # lay pieces out in a row for the kit sheet / .blend
    x = 0.0
    for name, ob in objs.items():
        w = report[name]["size_xyz_blender"][0]
        ob.location = (x + w / 2, 0, 0)
        x += w + 0.5
    # a single viewing GLB with the atlas embedded
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=os.path.join(ROOT, "kit", "T1_Kit_All_preview.glb"), use_selection=True,
                              export_format="GLB", export_image_format="AUTO")
    with open(os.path.join(ROOT, "kit", "kit_report.json"), "w") as fh:
        json.dump(report, fh, indent=2)
    for ob in objs.values():
        ob.location = (0, 0, 0)
        ob.hide_render = True
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "kit", "t1_kit.blend"))


if __name__ == "__main__":
    main()
