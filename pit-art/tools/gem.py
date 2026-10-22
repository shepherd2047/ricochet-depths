"""XP gem concepts: low-poly flat-shaded crystals, three value tiers, coloured per facet.

blender -b -P tools/gem.py -- --out DIR [--loud] [--rim] [--options ABCD]  # render concept options
blender -b -P tools/gem.py -- --out DIR --loud --export B2  # export the chosen design as models/lowpoly/gem_xp.glb

The concept sheets were: concept/gem_options.png (quiet) and concept/gem_options_loud.png (--loud --rim).
The user picked B (cluster); B2 is its final cut, --loud without the rim to stay near 36 tris.

Units are cells. Z-up, standing on z=0 (the lowest point). Colour lives in the face-corner
attribute "Col" (linear) and per-facet emissive strength in the face attribute "Emit" (0-1,
the game multiplies by 3), matching the untextured PitUnit shader (vertex colour + uv.x).
"""
import argparse
import math
import os
import sys

import bpy
import bmesh
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(HERE)

# tier hues (sRGB hex). Green / violet / pink: violet replaces the old cyan, which sat too close
# to the teal-grey floor.
TIERS = [
    {"name": "t1", "hex": "#3BEA62", "h": 0.34},
    {"name": "t2", "hex": "#9C5BFF", "h": 0.46},
    {"name": "t3", "hex": "#FF4BAE", "h": 0.60},
]
LOUD = False  # louder variant: bigger, chunkier, brighter, with an outline rim and a floor ring
RIM = False   # inverted-hull outline (doubles the tris; the concept sheet used it, the final B2 does not)
RIM_W = 0.032  # outline shell thickness in cells
LIGHT = Vector((-0.35, -0.55, 0.76)).normalized()  # key light direction used to pick the glint facet


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


# ---------------------------------------------------------------- shapes (lists of rings)

def shard(bm, h, r, sides, bot=0.25, top=0.25, twist=0.0, lean=(0, 0), at=(0, 0, 0), spin=0.0, cap=True):
    """Bipyramid with a prism (or antiprism when twist != 0) middle: 4*sides tris.
    cap=False leaves the bottom open (3*sides tris) for a crystal whose base is buried in another."""
    m = Matrix.Translation(at) @ Euler((lean[0], lean[1], spin)).to_matrix().to_4x4()
    v0 = bm.verts.new(m @ Vector((0, 0, 0)))
    v1 = bm.verts.new(m @ Vector((0, 0, h)))
    ring = lambda z, off: [bm.verts.new(m @ Vector((r * math.cos(off + 2 * math.pi * i / sides),
                                                     r * math.sin(off + 2 * math.pi * i / sides), z)))
                           for i in range(sides)]
    a = ring(h * bot, 0.0)
    b = ring(h * (1 - top), twist)
    for i in range(sides):
        j = (i + 1) % sides
        if cap:
            bm.faces.new((v0, a[j], a[i]))
        bm.faces.new((v1, b[i], b[j]))
        if twist:
            bm.faces.new((a[i], a[j], b[i]))
            bm.faces.new((a[j], b[j], b[i]))
        else:
            bm.faces.new((a[i], a[j], b[j]))
            bm.faces.new((a[i], b[j], b[i]))


def nugget(bm, h, rot):
    """Icosahedron (20 tris) squashed into a chunky pebble-cut stone."""
    tmp = bmesh.new()
    bmesh.ops.create_icosphere(tmp, subdivisions=1, radius=0.5)
    mat = Euler(rot).to_matrix().to_4x4()
    for v in tmp.verts:
        v.co = mat @ v.co
        v.co.z *= 1.15
        v.co.x *= 0.95
    lo = min(v.co.z for v in tmp.verts)
    hi = max(v.co.z for v in tmp.verts)
    s = h / (hi - lo)
    vm = {}
    for v in tmp.verts:
        vm[v] = bm.verts.new(((v.co.x) * s, v.co.y * s, (v.co.z - lo) * s))
    for f in tmp.faces:
        bm.faces.new([vm[v] for v in f.verts])
    tmp.free()


def build(option, tier):
    h = TIERS[tier]["h"] * (1.28 if LOUD else 1.0)
    bm = bmesh.new()
    if option == "A":  # single twisted hex shard, scales up
        shard(bm, h, h * 0.22, 6, bot=0.30, top=0.30, twist=math.pi / 6, lean=(0, math.radians(10)))
    elif option == "B":  # cluster: 1 / 2 / 3 square-section shards
        shard(bm, h, h * 0.24, 4, bot=0.2, top=0.28, spin=math.radians(20))
        if tier >= 1:
            shard(bm, h * 0.62, h * 0.2, 4, bot=0.2, top=0.3, lean=(0.0, math.radians(32)),
                  at=(0.03, 0, 0.0), spin=math.radians(-10))
        if tier >= 2:
            shard(bm, h * 0.52, h * 0.18, 4, bot=0.2, top=0.3, lean=(math.radians(-30), math.radians(-18)),
                  at=(-0.02, 0.02, 0.0), spin=math.radians(35))
    elif option == "B2":  # final cluster within ~36 tris: 4-sided main + open-based 3-sided side crystals
        shard(bm, h, h * 0.24, 4, bot=0.2, top=0.28, spin=math.radians(20))  # 16
        if tier >= 1:
            shard(bm, h * 0.64, h * 0.19, 3, bot=0.05, top=0.32, lean=(0.0, math.radians(34)),
                  at=(0.0, 0, h * 0.06), spin=math.radians(-10), cap=False)  # +9
        if tier >= 2:
            shard(bm, h * 0.54, h * 0.17, 3, bot=0.05, top=0.32, lean=(math.radians(-32), math.radians(-20)),
                  at=(0.0, 0.0, h * 0.06), spin=math.radians(35), cap=False)  # +9
    elif option == "C":  # chunky icosahedral nugget
        nugget(bm, h * 0.82, (0.35, 0.2, 0.15 + tier))
    elif option == "D":  # escalating cut: octahedron -> hex shard -> shard with two satellites
        if tier == 0:
            shard(bm, h, h * 0.36, 4, bot=0.5, top=0.5, spin=math.radians(45))
        else:
            shard(bm, h, h * 0.23, 6, bot=0.30, top=0.30, twist=math.pi / 6)
        if tier == 2:
            for k, ang in enumerate((40, 220)):
                a = math.radians(ang)
                shard(bm, h * 0.62, h * 0.17, 3, bot=0.25, top=0.3,
                      lean=(math.radians(38) * math.sin(a), -math.radians(38) * math.cos(a)),
                      at=(0.0, 0.0, 0.02), spin=a)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if LOUD:
        for v in bm.verts:  # chunkier: 20 % wider
            v.co.x *= 1.2
            v.co.y *= 1.2
        if RIM:
            add_rim(bm)
    me = bpy.data.meshes.new(f"gem_{option}_{TIERS[tier]['name']}")
    bm.to_mesh(me)
    bm.free()
    lo = min(v.co.z for v in me.vertices)
    for v in me.vertices:
        v.co.z -= lo
    paint(me, tier)
    return me


def add_rim(bm):
    """Inverted-hull outline: a copy pushed out along the vertex normals with flipped faces, so with
    normal back-face culling only its far side shows, as a bright rim around the silhouette."""
    bm.normal_update()
    rim = bm.faces.layers.int.new("rim")  # add the layer first: it invalidates face refs
    src = list(bm.faces)
    vmap = {v: bm.verts.new(v.co + v.normal * RIM_W) for v in list(bm.verts)}
    for f in src:
        nf = bm.faces.new([vmap[v] for v in reversed(f.verts)])
        nf[rim] = 1


def paint(me, tier):
    """Facet colours: crown light, girdle alternating, pavilion dark, one white glint facing the key light."""
    base = hex_rgb(TIERS[tier]["hex"])
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    emit = me.attributes.new("Emit", "FLOAT", "FACE")
    rim = me.attributes.get("rim")
    is_rim = [bool(rim and rim.data[p.index].value) for p in me.polygons]
    glint = max((p for p in me.polygons if not is_rim[p.index]), key=lambda p: p.normal.dot(LIGHT)).index
    loud = 1.6 if LOUD else 1.0
    for p in me.polygons:
        nz = p.normal.z
        if is_rim[p.index]:
            c, e = mix(base, (1, 1, 1), 0.40), 1.0
        elif p.index == glint:
            c, e = mix(base, (1, 1, 1), 0.72), 0.55
        elif nz > 0.35:
            c, e = mix(base, (1, 1, 1), 0.38), 0.40
        elif nz < -0.35:
            c, e = tuple(x * 0.42 for x in base), 0.10
        else:
            k = 1.0 if p.index % 2 == 0 else 0.68
            c, e = tuple(x * k for x in base), 0.25
        lin = tuple(srgb_to_lin(x) for x in c) + (1.0,)
        for li in p.loop_indices:
            col.data[li].color = lin
        emit.data[p.index].value = min(1.0, e * loud)


def gem_material():
    m = bpy.data.materials.new("GemFacet")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    ca = nt.nodes.new("ShaderNodeAttribute")
    ca.attribute_name = "Col"
    ea = nt.nodes.new("ShaderNodeAttribute")
    ea.attribute_name = "Emit"
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = 3.0
    nt.links.new(ca.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(ca.outputs["Color"], bsdf.inputs["Emission Color"])
    nt.links.new(ea.outputs["Fac"], mul.inputs[0])
    nt.links.new(mul.outputs[0], bsdf.inputs["Emission Strength"])
    bsdf.inputs["Roughness"].default_value = 0.35
    bsdf.inputs["Metallic"].default_value = 0.0
    m.use_backface_culling = True  # needed for the rim shell, harmless on the closed gem
    return m


def flat_material(name, rgb, alpha=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = tuple(srgb_to_lin(x) for x in rgb) + (1,)
    b.inputs["Roughness"].default_value = 0.9
    if alpha < 1:
        b.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = "BLENDED"
        except AttributeError:
            m.blend_method = "BLEND"
    return m


def ring_material(tier):
    m = bpy.data.materials.new(f"Ring{tier}")
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    c = tuple(srgb_to_lin(x) for x in hex_rgb(TIERS[tier]["hex"])) + (1,)
    b.inputs["Base Color"].default_value = c
    b.inputs["Emission Color"].default_value = c
    b.inputs["Emission Strength"].default_value = 1.5
    return m


# ---------------------------------------------------------------- scene

def setup(res):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.view_settings.view_transform = "Standard"
    w = bpy.data.worlds.new("W")
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.06, 0.07, 1)
    for nm, e, rot in (("Key", 2.6, (40, 0, -30)), ("Fill", 0.6, (70, 0, 150))):
        l = bpy.data.objects.new(nm, bpy.data.lights.new(nm, "SUN"))
        l.data.energy = e
        l.rotation_euler = tuple(math.radians(x) for x in rot)
        l.data.use_shadow = False  # the game draws only a contact disc
        sc.collection.objects.link(l)
    # floor: teal-grey stone cells (V ~30 %) with darker grout, 1 unit = 1 cell
    floor = flat_material("Floor", (0.25, 0.30, 0.32))
    grout = flat_material("Grout", (0.12, 0.15, 0.16))
    bpy.ops.mesh.primitive_plane_add(size=12)
    bpy.context.object.data.materials.append(floor)
    for i in range(-6, 7):
        for axis in (0, 1):
            bpy.ops.mesh.primitive_cube_add(size=1, location=(i - 0.5, 0, 0.001) if axis == 0 else (0, i - 0.5, 0.001))
            o = bpy.context.object
            o.scale = (0.04, 12, 0.002) if axis == 0 else (12, 0.04, 0.002)
            o.data.materials.append(grout)
    return sc


def place(sc, me, mat, shadow, x, y, lift=0.06, yaw=0.0, ring=None):
    o = bpy.data.objects.new(me.name, me)
    me.materials.append(mat)
    o.location = (x, y, lift)
    o.rotation_euler = (0, 0, yaw)
    sc.collection.objects.link(o)
    bpy.ops.mesh.primitive_circle_add(vertices=16, radius=0.11 + 0.18 * max(v.co.x for v in me.vertices),
                                      fill_type="NGON", location=(x, y, 0.004))
    bpy.context.object.data.materials.append(shadow)
    if LOUD and ring is not None:
        rm = ring_mesh(me, ring_tier(me))
        rm.materials.append(mat)
        ro = bpy.data.objects.new("ring", rm)
        ro.location = (x, y, 0)
        sc.collection.objects.link(ro)
    return o


def ring_tier(me):
    return int(me.name.rsplit("_t", 1)[1][0]) - 1


def ring_radius(me):
    return 0.16 + 0.22 * max(math.hypot(v.co.x, v.co.y) for v in me.vertices)


def ring_mesh(gem_me, tier, segs=12, width=0.04):
    """Flat glowing ring on the floor under the gem (2*segs tris), painted like the gem (Col + Emit)."""
    r = ring_radius(gem_me)
    bm = bmesh.new()
    pt = lambda rad, i: bm.verts.new((rad * math.cos(2 * math.pi * i / segs), rad * math.sin(2 * math.pi * i / segs), 0.006))
    a = [pt(r, i) for i in range(segs)]
    b = [pt(r - width, i) for i in range(segs)]
    for i in range(segs):
        j = (i + 1) % segs
        bm.faces.new((a[i], a[j], b[j]))
        bm.faces.new((a[i], b[j], b[i]))
    me = bpy.data.meshes.new(f"gem_ring_{TIERS[tier]['name']}")
    bm.to_mesh(me)
    bm.free()
    lin = tuple(srgb_to_lin(x) for x in hex_rgb(TIERS[tier]["hex"])) + (1.0,)
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    emit = me.attributes.new("Emit", "FLOAT", "FACE")
    for p in me.polygons:
        for li in p.loop_indices:
            col.data[li].color = lin
        emit.data[p.index].value = 0.5
    return me


def camera(sc, target, dist, elev_deg, lens=40):
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.lens = lens
    e = math.radians(elev_deg)
    cam.location = (target[0], target[1] - dist * math.cos(e), target[2] + dist * math.sin(e))
    cam.rotation_euler = (math.pi / 2 - e, 0, 0)
    sc.collection.objects.link(cam)
    sc.camera = cam


def render(path):
    bpy.context.scene.render.filepath = os.path.abspath(path)
    bpy.ops.render.render(write_still=True)


def concepts(out, options):
    for opt in options:
        for shot, (res, dist, elev, lens) in {"close": ((900, 520), 3.9, 35, 50),
                                               "game": ((900, 520), 6.2, 52, 40)}.items():
            sc = setup(res)
            mat = gem_material()
            shadow = flat_material("Shadow", (0.03, 0.04, 0.05), 0.55)
            for t in range(3):
                place(sc, build(opt, t), mat, shadow, (t - 1) * 1.0, 0.0, yaw=math.radians(15 + 25 * t),
                      ring=ring_material(t))
            camera(sc, (0, 0, 0.25), dist, elev, lens)
            render(os.path.join(out, f"gem_{opt}{'_loud' if LOUD else ''}_{shot}.png"))


def export(opt, out_dir):
    """One GLB holding six meshes: gem_t1..t3 (the crystals) and gem_ring_t1..t3 (floor rings).
    Vertex colour = COLOR_0 (per facet), uv.x = emissive strength 0-1 (PitUnit multiplies by 3)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    mat = bpy.data.materials.new("GemVertexColour")
    for t in range(3):
        gem = build(opt, t)
        gem.name = f"gem_{TIERS[t]['name']}"
        for me in (gem, ring_mesh(gem, t)):
            me.color_attributes.active_color = me.color_attributes["Col"]
            uv = me.uv_layers.new(name="UVMap")
            for p in me.polygons:
                e = me.attributes["Emit"].data[p.index].value
                for li in p.loop_indices:
                    uv.data[li].uv = (e, 0.0)
            me.attributes.remove(me.attributes["Emit"])
            if "rim" in me.attributes:
                me.attributes.remove(me.attributes["rim"])
            me.materials.append(mat)
            o = bpy.data.objects.new(me.name, me)
            sc.collection.objects.link(o)
            zs = [v.co.z for v in me.vertices]
            print(f"[gem] {me.name}: {len(me.polygons)} tris, {len(me.loops)} unity verts, "
                  f"h {max(zs) - min(zs):.3f}, r {max(math.hypot(v.co.x, v.co.y) for v in me.vertices):.3f}")
    path = os.path.join(out_dir, "gem_xp.glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_vertex_color="ACTIVE",
                              export_normals=True, export_materials="PLACEHOLDER")
    print("[gem]", path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--options", default="ABCD")
    ap.add_argument("--export")
    ap.add_argument("--loud", action="store_true")
    ap.add_argument("--rim", action="store_true")
    a = ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    os.makedirs(a.out, exist_ok=True)
    global LOUD, RIM
    LOUD, RIM = a.loud, a.rim
    if a.export:
        export(a.export, os.path.join(ART, "models", "lowpoly"))
    else:
        concepts(a.out, [o for o in a.options.split(",") if o] if "," in a.options else list(a.options))  # "ABCD" or "B2,"


main()
