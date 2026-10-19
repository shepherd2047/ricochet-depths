"""Headless Blender: high-poly GLB/OBJ from image-to-3D -> low-poly, flat-shaded, small-texture GLB/FBX for Unity.

Usage:
  blender -b -P decimate.py -- --in raw.glb --out lowpoly.glb --tris 800 [--tex 256] [--quantize 12]
         [--height 1.0] [--normals flat|smooth] [--up=+z --front=-y]
         [--vrange 0.0,0.30 --accent-vrange 0.55,0.85] [--preview preview.png]

TripoSR GLBs come in with up = -y and front = +x: pass --up=-y --front=+x (use = so argparse accepts the minus sign).

Budgets (art-bible.md): heroes ~800 tris, enemies 200-400, boss ~1500.

Steps: import -> join all meshes -> clean (merge by distance, drop loose bits) -> optional rescale so the
model is --height tall, base at z=0 -> collapse-decimate to --tris -> new UVs (smart project) -> bake the
original's base colour (image texture or vertex colours) onto the low-poly at --tex px -> optional
k-means posterise to --quantize flat colours -> optional value remap into style.md ranges
(--vrange for the body; colours with saturation >= --accent-sat go to --accent-vrange) -> flat or smooth normals -> export (.glb or .fbx by extension).

Vertex count note: hard flat normals split every vertex per face, so Unity's vertex count is ~3x tris.
With --normals smooth the mesh keeps shared vertices; do the faceted look in the shader
(normal = normalize(cross(ddy(posWS), ddx(posWS)))) to stay inside style.md's vertex budgets.
"""
import argparse
import os
import sys

import bmesh
import bpy
import numpy as np


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="decimate.py")
    ap.add_argument("--in", dest="src", required=True)
    ap.add_argument("--out", required=True, help=".glb or .fbx")
    ap.add_argument("--tris", type=int, required=True, help="target triangle count")
    ap.add_argument("--tex", type=int, default=256, help="baked texture size (px)")
    ap.add_argument("--quantize", type=int, default=0, help="posterise texture to N colours (0 = off)")
    ap.add_argument("--height", type=float, default=0.0, help="rescale to this height in metres (0 = keep)")
    ap.add_argument("--normals", choices=["flat", "smooth"], default="flat")
    ap.add_argument("--min-part", type=float, default=0.02,
                    help="drop loose parts smaller than this fraction of total faces")
    ap.add_argument("--up", default="+z", help="source up axis after import (+x,-x,+y,-y,+z,-z)")
    ap.add_argument("--front", default="-y", help="source facing axis after import")
    ap.add_argument("--vrange", default="", help="lo,hi: remap texture value (HSV V) into this range")
    ap.add_argument("--accent-vrange", default="", help="lo,hi: value range for saturated accent colours")
    ap.add_argument("--accent-sat", type=float, default=0.25, help="saturation that marks an accent colour")
    ap.add_argument("--preview", default="", help="optional PNG turntable-style preview render")
    return ap.parse_args(argv)


def log(*a):
    print("[decimate]", *a, flush=True)


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def import_model(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ext = os.path.splitext(path)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        sys.exit(f"unsupported input: {ext}")
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        sys.exit("no mesh in input")
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    # Unparent and bake transforms so the exported origin/scale are clean.
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o != obj:
            bpy.data.objects.remove(o, do_unlink=True)
    obj.name = "Source"
    return obj


def ensure_color_material(obj):
    """Make sure the source's colour is readable by a DIFFUSE bake: if no material samples an image,
    wire the first colour attribute (TripoSR/vertex-colour output) into the base colour.
    Metallic is forced to 0: Cycles' diffuse-colour pass is black for metals (TRELLIS exports metallic=1)."""
    for m in obj.data.materials:
        if not (m and m.node_tree):
            continue
        for n in m.node_tree.nodes:
            if n.type == "BSDF_PRINCIPLED":
                for link in list(n.inputs["Metallic"].links):
                    m.node_tree.links.remove(link)
                n.inputs["Metallic"].default_value = 0.0
    has_image = any(
        n.type == "TEX_IMAGE" and n.image
        for m in obj.data.materials if m and m.node_tree for n in m.node_tree.nodes
    )
    if has_image or not obj.data.color_attributes:
        return
    attr = obj.data.color_attributes[0].name
    mat = bpy.data.materials.new("VertexColour")
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = attr
    nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    log("source colour from vertex attribute", attr)


def clean(obj, min_part):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    # Drop floating specks that image-to-3D tools leave around the subject.
    bm.faces.ensure_lookup_table()
    seen, islands = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, island = [f], []
        seen.add(f.index)
        while stack:
            cur = stack.pop()
            island.append(cur)
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf.index not in seen:
                        seen.add(nf.index)
                        stack.append(nf)
        islands.append(island)
    total = len(bm.faces)
    drop = [f for isl in islands if len(isl) < total * min_part for f in isl]
    if drop and len(drop) < total:
        bmesh.ops.delete(bm, geom=drop, context="FACES")
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    log(f"clean: {len(islands)} parts, dropped {len(drop)} faces in small parts")


AXES = {"+x": (1, 0, 0), "-x": (-1, 0, 0), "+y": (0, 1, 0), "-y": (0, -1, 0), "+z": (0, 0, 1), "-z": (0, 0, -1)}


def reorient(obj, up, front):
    """Rotate so the source's `up` becomes +Z and its `front` becomes -Y (Blender front view)."""
    u, f = np.array(AXES[up], float), np.array(AXES[front], float)
    if abs(u @ f) > 0.5:
        sys.exit("--up and --front must be perpendicular")
    src = np.stack([u, f, np.cross(u, f)])           # rows: source basis
    dst = np.array([[0, 0, 1], [0, -1, 0], np.cross([0, 0, 1], [0, -1, 0])], float)
    rot = dst.T @ src                                 # maps source axes onto target axes
    if np.allclose(rot, np.eye(3)):
        return
    for v in obj.data.vertices:
        v.co = rot @ np.array(v.co)
    obj.data.update()
    log(f"reoriented up={up} front={front}")


def normalise(obj, height):
    bpy.context.view_layer.update()
    co = np.array([obj.matrix_world @ v.co for v in obj.data.vertices])
    lo, hi = co.min(0), co.max(0)
    centre = np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]])
    s = height / (hi[2] - lo[2]) if height > 0 else 1.0
    for v in obj.data.vertices:
        v.co = (np.array(v.co) - centre) * s
    obj.location = (0, 0, 0)
    log(f"bounds {np.round((hi - lo) * s, 3).tolist()} m")


def duplicate(obj, name):
    new = obj.copy()
    new.data = obj.data.copy()
    new.name = name
    bpy.context.collection.objects.link(new)
    return new


def decimate(obj, target):
    for _ in range(4):
        cur = tri_count(obj)
        if cur <= target * 1.05:
            break
        mod = obj.modifiers.new("Dec", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.use_collapse_triangulate = True
        mod.ratio = max(target / cur, 1e-4)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    log(f"decimated to {tri_count(obj)} tris (target {target})")


def unwrap(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for uv in list(obj.data.uv_layers):
        obj.data.uv_layers.remove(uv)
    obj.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake(src, low, size):
    img = bpy.data.images.new("Albedo", size, size, alpha=False)
    mat = bpy.data.materials.new("LowPoly")
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.9
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.nodes.active = tex
    low.data.materials.clear()
    low.data.materials.append(mat)

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    bk = sc.render.bake
    bk.use_selected_to_active = True
    bk.use_pass_direct = False
    bk.use_pass_indirect = False
    bk.use_pass_color = True
    dims = max(low.dimensions)
    bk.cage_extrusion = dims * 0.02
    bk.max_ray_distance = dims * 0.06
    bk.margin = 2
    bpy.ops.object.select_all(action="DESELECT")
    src.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.bake(type="DIFFUSE")
    log(f"baked {size}px albedo")
    return img


def _kmeans(rgb, k, iters=12):
    rng = np.random.default_rng(0)
    centres = rgb[rng.choice(len(rgb), min(k, len(rgb)), replace=False)].copy()
    for _ in range(iters):
        lab = ((rgb[:, None, :] - centres[None]) ** 2).sum(-1).argmin(1)
        for i in range(len(centres)):
            m = lab == i
            if m.any():
                centres[i] = rgb[m].mean(0)
    return centres[lab]


def quantize(img, k, accent_sat):
    """Posterise to k flat colours. Saturated accent pixels (eyes, signature colours) get two clusters
    of their own so a few percent of the texture is not averaged away into the body colours."""
    px = np.array(img.pixels[:], dtype=np.float32).reshape(-1, 4)
    rgb = px[:, :3]
    mx, mn = rgb.max(1), rgb.min(1)
    acc = (mx - mn) / np.maximum(mx, 1e-6) >= accent_sat
    n_acc = 2 if acc.mean() > 0.002 and k > 4 else 0
    if n_acc:
        rgb[acc] = _kmeans(rgb[acc], n_acc)
        rgb[~acc] = _kmeans(rgb[~acc], k - n_acc)
    else:
        rgb[:] = _kmeans(rgb, k)
    px[:, :3] = rgb
    img.pixels[:] = px.ravel()
    log(f"posterised to {k} colours ({n_acc} reserved for accents, {acc.mean() * 100:.1f}% px)")


def remap_values(img, vrange, accent_vrange, accent_sat):
    """Linearly remap HSV value of every pixel into vrange (accents into accent_vrange), keeping hue
    and saturation, so AI textures obey style.md's per-layer value ranges."""
    import colorsys
    px = np.array(img.pixels[:], dtype=np.float32).reshape(-1, 4)
    hsv = np.array([colorsys.rgb_to_hsv(*c) for c in px[:, :3]])
    acc = hsv[:, 1] >= accent_sat if accent_vrange else np.zeros(len(hsv), bool)
    for mask, rng in ((~acc, vrange), (acc, accent_vrange)):
        if not rng or not mask.any():
            continue
        lo, hi = (float(x) for x in rng.split(","))
        v = hsv[mask, 2]
        vmin, vmax = v.min(), v.max()
        hsv[mask, 2] = lo + (v - vmin) / max(vmax - vmin, 1e-6) * (hi - lo)
    px[:, :3] = np.array([colorsys.hsv_to_rgb(*c) for c in hsv])
    img.pixels[:] = px.ravel()
    log(f"value remap body={vrange or '-'} accent={accent_vrange or '-'} ({acc.mean() * 100:.1f}% accent px)")


def set_normals(obj, mode):
    for p in obj.data.polygons:
        p.use_smooth = mode == "smooth"
    if mode == "flat" and hasattr(obj.data, "set_sharp_from_angle"):
        obj.data.set_sharp_from_angle(angle=0.0)


def export(obj, path, img):
    os.makedirs(os.path.dirname(os.path.abspath(path)) or ".", exist_ok=True)
    tex_path = os.path.splitext(path)[0] + "_albedo.png"
    img.filepath_raw = tex_path
    img.file_format = "PNG"
    img.save()
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if path.lower().endswith(".fbx"):
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={"MESH"},
            apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
            path_mode="COPY", embed_textures=True, mesh_smooth_type="FACE",
        )
    else:
        bpy.ops.export_scene.gltf(
            filepath=path, export_format="GLB", use_selection=True,
            export_normals=True, export_texcoords=True, export_materials="EXPORT",
        )
    log(f"wrote {path} ({os.path.getsize(path) / 1024:.0f} KB) + {os.path.basename(tex_path)}")


def unity_vertex_count(obj, mode):
    """Vertices after splitting on UV seams and (for flat) per-face normals, as a GPU/Unity would."""
    uv = obj.data.uv_layers.active.data
    keys = set()
    for p in obj.data.polygons:
        for li in p.loop_indices:
            vi = obj.data.loops[li].vertex_index
            u = tuple(round(c, 5) for c in uv[li].uv)
            keys.add((vi, u, p.index) if mode == "flat" else (vi, u))
    return len(keys)


def preview(obj, path):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.render.resolution_x = sc.render.resolution_y = 512
    sc.render.film_transparent = False
    sc.world = sc.world or bpy.data.worlds.new("W")
    bpy.context.view_layer.update()
    h = max(obj.dimensions)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    cam.location = (h * 1.4, -h * 2.2, h * 1.0)
    target = bpy.data.objects.new("T", None)
    sc.collection.objects.link(target)
    target.location = (0, 0, obj.dimensions.z * 0.45)
    c = cam.constraints.new("TRACK_TO")
    c.target = target
    sc.camera = cam
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    log("preview", path)


def main():
    a = parse_args()
    src = import_model(a.src)
    ensure_color_material(src)
    log(f"input {tri_count(src)} tris, {len(src.data.vertices)} verts")
    clean(src, a.min_part)
    reorient(src, a.up, a.front)
    normalise(src, a.height)
    low = duplicate(src, os.path.splitext(os.path.basename(a.out))[0])
    low.data.materials.clear()
    decimate(low, a.tris)
    unwrap(low)
    img = bake(src, low, a.tex)
    if a.quantize:
        quantize(img, a.quantize, a.accent_sat)
    if a.vrange or a.accent_vrange:
        remap_values(img, a.vrange, a.accent_vrange, a.accent_sat)
    set_normals(low, a.normals)
    bpy.data.objects.remove(src, do_unlink=True)
    export(low, a.out, img)
    log(f"final {tri_count(low)} tris, {len(low.data.vertices)} mesh verts, "
        f"~{unity_vertex_count(low, a.normals)} Unity verts ({a.normals} normals)")
    if a.preview:
        preview(low, a.preview)


main()
