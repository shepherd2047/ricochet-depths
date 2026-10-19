# Rigged hero GLB (pit-art tools/rig_hero.py) -> rest mesh + stepped vertex animation for Models.Model.
# Writes <name>.bytes (the v2 layout of glb_to_bytes.py, rest pose, one submesh) and <name>_anim.bytes:
#   int32 nv, int32 nclip, float32 fps, float32 min xyz, float32 max xyz, then per clip:
#   uint8 name length, name (ascii), uint8 loop, int32 frames, frames * nv * (int16 x y z quantised to min..max,
#   int8 nx ny nz), padded to a multiple of 4 bytes per frame.
# Vertex order matches <name>.bytes. Clips are sampled at FPS (style.md §6 stepped playback, no blending): a looping
# clip of length L gets round(L * FPS) frames (the end frame equals the start); a one-shot clip gets one more and holds it.
# Run: blender -b -P Tools/bake_hero_anim.py -- <rigged glb> <out dir>
import bpy, sys, os, struct, math
a = sys.argv[sys.argv.index("--")+1:]; src, dst = a[0], a[1]
FPS = 10
LOOP = {"Idle": True, "Walk": True, "Shoot": False, "Hit": False, "Death": False, "Victory": True}
ORDER = ["Idle", "Walk", "Shoot", "Hit", "Death", "Victory"]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scn = bpy.context.scene
arm = [o for o in scn.objects if o.type == 'ARMATURE'][0]
o = [o for o in scn.objects if o.type == 'MESH' and o.parent == arm][0]
ad = arm.animation_data
for t in ad.nla_tracks: t.mute = True
name = os.path.splitext(os.path.basename(src))[0]

def sample():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    m = ev.to_mesh()
    m.calc_loop_triangles()
    mw = ev.matrix_world; nm = mw.to_3x3().inverted().transposed()
    pos = [mw @ v.co for v in m.vertices]
    nor = [(nm @ cn.vector).normalized() for cn in m.corner_normals]
    tris = [tuple(t.loops) for t in m.loop_triangles]
    uv = [tuple(d.uv) for d in m.uv_layers.active.data]
    vi = [l.vertex_index for l in m.loops]
    ev.to_mesh_clear()
    return pos, nor, tris, uv, vi

# rest pose: no action
ad.action = None
for pb in arm.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
scn.frame_set(0)
pos, nor, tris, uv, vi = sample()
key, rep, idx = {}, [], []   # rep: one loop per output vertex
for t in tris:
    for li in t:
        n = nor[li]; u = uv[li]
        k = (vi[li], round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u[0], 4), round(u[1], 4))
        if k not in key: key[k] = len(rep); rep.append(li)
        idx.append(key[k])
nv = len(rep)
with open(os.path.join(dst, name + ".bytes"), "wb") as w:
    w.write(struct.pack("<iii", nv, 1, len(idx)))
    for li in rep:
        p = pos[vi[li]]; n = nor[li]; u = uv[li]
        w.write(struct.pack("<8f4B", p.x, p.y, p.z, n.x, n.y, n.z, u[0], u[1], 255, 255, 255, 255))
    w.write(struct.pack("<%dH" % len(idx), *idx))

clips = []
for cn in ORDER:
    act = bpy.data.actions[cn]
    ad.action = act
    f0, f1 = act.frame_range
    L = (f1 - f0) / scn.render.fps
    n = max(1, round(L * FPS)) + (0 if LOOP[cn] else 1)
    frames = []
    for k in range(n):
        f = f0 + min(k / FPS, L) * scn.render.fps
        scn.frame_set(int(math.floor(f)), subframe=f - math.floor(f))
        p, nr, _, _, _ = sample()
        frames.append([(p[vi[li]], nr[li]) for li in rep])
    clips.append((cn, LOOP[cn], frames))
    print(cn, "%.2fs" % L, n, "frames")

allp = [p for _, _, fr in clips for f in fr for p, _ in f]
mn = [min(p[i] for p in allp) for i in range(3)]; mx = [max(p[i] for p in allp) for i in range(3)]
q = lambda x, i: int(round((x - mn[i]) / max(1e-6, mx[i] - mn[i]) * 65535)) - 32768
with open(os.path.join(dst, name + "_anim.bytes"), "wb") as w:
    w.write(struct.pack("<iif3f3f", nv, len(clips), FPS, *mn, *mx))
    for cn, loop, frames in clips:
        w.write(struct.pack("<B", len(cn)) + cn.encode() + struct.pack("<Bi", 1 if loop else 0, len(frames)))
        for f in frames:
            b = bytearray()
            for p, n in f:
                b += struct.pack("<3h3b", q(p.x, 0), q(p.y, 1), q(p.z, 2),
                                 *[max(-127, min(127, int(round(c * 127)))) for c in (n.x, n.y, n.z)])
            b += bytes((-len(b)) % 4)
            w.write(b)
img = [i for i in bpy.data.images if i.packed_file or i.has_data]
if img:
    img[0].filepath_raw = os.path.join(dst, name + "_albedo.png"); img[0].file_format = 'PNG'; img[0].save()
print("baked", name, nv, "verts", len(idx) // 3, "tris", sum(len(c[2]) for c in clips), "frames")
