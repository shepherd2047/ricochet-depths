"""Render GLBs side by side at true scale (hero vs enemies), three-quarter view from above.

blender -b -P tools/lineup.py -- --out models/lowpoly/_starter_set.png models/lowpoly/hero_tinker.glb models/lowpoly/enemy_*.glb
"""
import argparse
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import monster_lib as ml  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--gap", type=float, default=0.35)
    ap.add_argument("glbs", nargs="+")
    a = ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    groups = []
    for path in a.glbs:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        objs = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
        for o in objs:
            for m in o.data.materials:
                if m and m.node_tree:  # TRELLIS-baked heroes: keep metallic off like decimate.py
                    for n in m.node_tree.nodes:
                        if n.type == "BSDF_PRINCIPLED":
                            n.inputs["Metallic"].default_value = 0.0
        groups.append(objs)
    bpy.context.view_layer.update()
    x = 0.0
    widths = []
    for objs in groups:
        xs = [(o.matrix_world @ v.co).x for o in objs for v in o.data.vertices]
        widths.append((min(xs), max(xs)))
    total = sum(b - a for a, b in widths) + a.gap * (len(groups) - 1)
    x = -total / 2
    zmax = 0
    for objs, (lo, hi) in zip(groups, widths):
        for o in objs:
            top = o.parent or o
            top.location.x += x - lo
        x += (hi - lo) + a.gap
        zmax = max(zmax, max((o.matrix_world @ v.co).z for o in objs for v in o.data.vertices))
    sc = ml._setup_render(1600, 640)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = max(total * 1.15, zmax * 2.5 * 1.7)
    sc.collection.objects.link(cam)
    cam.location = (total * 0.3, -total * 1.2, total * 0.75)
    tgt = bpy.data.objects.new("T", None)
    sc.collection.objects.link(tgt)
    tgt.location = (0, 0, zmax * 0.5)
    c = cam.constraints.new("TRACK_TO")
    c.target = tgt
    sc.camera = cam
    sc.render.filepath = os.path.abspath(a.out)
    bpy.ops.render.render(write_still=True)
    print("[lineup]", a.out)


main()
