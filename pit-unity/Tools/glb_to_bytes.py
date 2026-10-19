# GLB -> compact .bytes mesh (Blender coords: x right, -y front, z up; uv bottom-left). Layout:
# int32 nv, int32 ni, nv * (px py pz nx ny nz u v) float32, ni * uint16
import bpy, sys, glob, os, struct
a = sys.argv[sys.argv.index("--")+1:]; src, dst = a[0], a[1]
for f in sorted(glob.glob(os.path.join(src, "*.glb"))):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=f)
    o = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
    m = o.data; m.calc_loop_triangles()
    uv = m.uv_layers.active
    cn = m.corner_normals if hasattr(m, 'corner_normals') else None
    nm = o.matrix_world.to_3x3().inverted().transposed()
    verts, idx, key = [], [], {}
    for t in m.loop_triangles:
        for li in t.loops:
            l = m.loops[li]
            p = o.matrix_world @ m.vertices[l.vertex_index].co
            n = (nm @ (cn[li].vector if cn else l.normal)).normalized()
            u = uv.data[li].uv
            k = (l.vertex_index, round(n.x,3), round(n.y,3), round(n.z,3), round(u.x,4), round(u.y,4))
            if k not in key:
                key[k] = len(verts); verts.append((p.x,p.y,p.z,n.x,n.y,n.z,u.x,u.y))
            idx.append(key[k])
    name = os.path.splitext(os.path.basename(f))[0]
    with open(os.path.join(dst, name + ".bytes"), "wb") as w:
        w.write(struct.pack("<ii", len(verts), len(idx)))
        for v in verts: w.write(struct.pack("<8f", *v))
        w.write(struct.pack("<%dH" % len(idx), *idx))
    print(name, len(verts), len(idx)//3)
