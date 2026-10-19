# GLB -> compact .bytes mesh (Blender coords: x right, -y front, z up; uv bottom-left). Layout:
#   int32 nv, int32 nsub, nsub * int32 index count,
#   nv * (px py pz nx ny nz u v float32, r g b a uint8), all indices as uint16, submesh after submesh
# One submesh per material slot (pit-scene: slot 0 T1_Scenery, slot 1 T1_Beam). Vertex colour comes from the colour
# attribute that carries alpha (the beam's light pool), else white.
# Run: blender -b -P Tools/glb_to_bytes.py -- <glb dir> <out dir> [name prefix filter]
import bpy, sys, glob, os, struct
a = sys.argv[sys.argv.index("--")+1:]; src, dst = a[0], a[1]
pre = a[2] if len(a) > 2 else ""
for f in sorted(glob.glob(os.path.join(src, pre + "*.glb"))):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=f)
    o = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
    m = o.data; m.calc_loop_triangles()
    uv = m.uv_layers.active
    cn = m.corner_normals if hasattr(m, 'corner_normals') else None
    ca = None
    for c in m.color_attributes:
        if c.domain == 'CORNER' and any(d.color[3] < 0.999 for d in c.data): ca = c
    nm = o.matrix_world.to_3x3().inverted().transposed()
    nsub = max(1, len(o.material_slots))
    verts, subs, key = [], [[] for _ in range(nsub)], {}
    for t in m.loop_triangles:
        for li in t.loops:
            l = m.loops[li]
            p = o.matrix_world @ m.vertices[l.vertex_index].co
            n = (nm @ (cn[li].vector if cn else l.normal)).normalized()
            u = uv.data[li].uv
            c = tuple(int(round(max(0, min(1, x)) * 255)) for x in ca.data[li].color) if ca else (255, 255, 255, 255)
            k = (l.vertex_index, round(n.x,3), round(n.y,3), round(n.z,3), round(u.x,4), round(u.y,4), c)
            if k not in key:
                key[k] = len(verts); verts.append((p.x,p.y,p.z,n.x,n.y,n.z,u.x,u.y) + c)
            subs[min(t.material_index, nsub - 1)].append(key[k])
    name = os.path.splitext(os.path.basename(f))[0]
    with open(os.path.join(dst, name + ".bytes"), "wb") as w:
        w.write(struct.pack("<ii", len(verts), nsub))
        for s in subs: w.write(struct.pack("<i", len(s)))
        for v in verts: w.write(struct.pack("<8f4B", *v))
        for s in subs: w.write(struct.pack("<%dH" % len(s), *s))
    print(name, len(verts), [len(s)//3 for s in subs])
