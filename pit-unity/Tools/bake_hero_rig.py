# Layered hero rig (pit-art tools/rig_hero_layered.py, ANIM.md "Engine side" A) -> rest mesh + per-bone clips for
# Game/HeroRig.cs, which composes the upper / lower / full-body layers and skins the mesh on the CPU at 10 fps.
# Writes <name>.bytes (the v2 layout of glb_to_bytes.py, bind pose, one submesh) and <name>_rig.bytes:
#   "RIG1", int32 nbones, int32 nverts, float32 fps
#   per bone: uint8 name length, name, int32 parent (-1 root), uint8 layer (0 lower, 1 upper), float32[12] rest matrix
#     (bone.matrix_local, armature space, rows 0-2 of the 4x4)
#   per vertex (the order of <name>.bytes): float32 pos xyz, float32 normal xyz (armature space = Blender world here),
#     uint8 bone[4], uint8 weight[4] (sum 255)
#   int32 nclips, per clip: uint8 name length, name, uint8 loop, int32 frames, then frames * nbones *
#     (int16 quaternion w x y z * 32767, int16 location xyz * 4096): pose_bone.matrix_basis, sampled every 1 / fps s.
# Loops get round(L * fps) frames (their end key equals the start and is dropped), one-shots one more (ANIM.md).
# The script re-skins a few frames with the same maths as HeroRig.cs and fails if any vertex is off by more than 1 mm.
# Run: blender -b -P Tools/bake_hero_rig.py -- <layered glb> <out dir> <out name>
import bpy, sys, os, struct, math
from mathutils import Matrix, Quaternion, Vector
a = sys.argv[sys.argv.index("--")+1:]; src, dst, name = a[0], a[1], a[2]
FPS = 10.0
LOOP = {"AimIdle": True, "AimWalk": True, "Shoot": False, "Stand": True, "WalkForward": True, "WalkBack": True,
        "StrafeLeft": True, "StrafeRight": True, "Death": False, "Victory": True}
LOWER = {"Hips", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes"}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scn = bpy.context.scene
arm = [o for o in scn.objects if o.type == 'ARMATURE'][0]
o = [o for o in scn.objects if o.type == 'MESH' and o.parent == arm][0]
ad = arm.animation_data
for t in ad.nla_tracks: t.mute = True
bones = list(arm.data.bones)
bi = {b.name: i for i, b in enumerate(bones)}
pbs = [arm.pose.bones[b.name] for b in bones]
Aw = arm.matrix_world.copy(); AwI = Aw.inverted()

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

def rest():
    ad.action = None
    for pb in arm.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
    scn.frame_set(0)

rest()
pos, nor, tris, uv, vi = sample()
key, rep, idx = {}, [], []
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

# skin weights, top 4, quantised to 255 in total
groups = {g.index: g.name for g in o.vertex_groups}
wts = []
for li in rep:
    v = o.data.vertices[vi[li]]
    ws = sorted([(g.weight, bi[groups[g.group]]) for g in v.groups if groups[g.group] in bi and g.weight > 1e-4], reverse=True)[:4]
    s = sum(x for x, _ in ws)
    q = [int(round(x / s * 255)) for x, _ in ws]
    q[0] += 255 - sum(q)
    wts.append([(b, qq) for (_, b), qq in zip(ws, q)] + [(0, 0)] * (4 - len(ws)))

# rest positions / normals in armature space
restP = [AwI @ pos[vi[li]] for li in rep]
restN = [(AwI.to_3x3() @ nor[li]).normalized() for li in rep]

clips = []
for cn in sorted(bpy.data.actions.keys()):
    base = cn.split("_")[0]
    act = bpy.data.actions[cn]
    ad.action = act
    f0, f1 = act.frame_range
    L = (f1 - f0) / scn.render.fps
    n = max(1, round(L * FPS)) + (0 if LOOP[base] else 1)
    frames = []
    for k in range(n):
        f = f0 + min(k / FPS, L) * scn.render.fps
        scn.frame_set(int(math.floor(f)), subframe=f - math.floor(f))
        fr = []
        for pb in pbs:
            mb = pb.matrix_basis
            loc, rot, sc = mb.decompose()
            assert abs(sc.x - 1) < 1e-3 and abs(sc.y - 1) < 1e-3 and abs(sc.z - 1) < 1e-3, (cn, pb.name, sc)
            if rot.w < 0: rot = -rot
            fr.append((rot, loc))
        frames.append(fr)
    clips.append((cn, LOOP[base], frames))
    print(cn, "%.2fs" % L, n, "frames")

# the same maths as HeroRig.cs: world_i = world_parent * inv(rest_parent) * rest_i * T(loc) * R(q); skin = world * inv(rest)
def skin(fr):
    world = []
    for i, b in enumerate(bones):
        q, l = fr[i]
        basis = Matrix.Translation(l) @ q.to_matrix().to_4x4()
        rel = b.matrix_local if b.parent is None else b.parent.matrix_local.inverted() @ b.matrix_local
        world.append((Matrix.Identity(4) if b.parent is None else world[bi[b.parent.name]]) @ rel @ basis)
    sk = [world[i] @ bones[i].matrix_local.inverted() for i in range(len(bones))]
    out = []
    for j in range(nv):
        p = Vector((0, 0, 0))
        for bb, ww in wts[j]:
            if ww: p += (sk[bb] @ restP[j]) * (ww / 255)
        out.append(Aw @ p)
    return out

worst = 0
for cn, loop, frames in clips:
    if cn not in ("AimWalk_L", "Shoot_R", "Death", "WalkBack", "Victory"): continue
    ad.action = bpy.data.actions[cn]
    for k in (0, len(frames) // 2):
        f = bpy.data.actions[cn].frame_range[0] + k / FPS * scn.render.fps
        scn.frame_set(int(math.floor(f)), subframe=f - math.floor(f))
        ref, _, _, _, _ = sample()
        mine = skin(frames[k])
        e = max((mine[j] - ref[vi[rep[j]]]).length for j in range(nv))
        worst = max(worst, e)
print("skin check: worst vertex error %.3f mm" % (worst * 1000))
assert worst < 1e-3, "skinning mismatch"

with open(os.path.join(dst, name + "_rig.bytes"), "wb") as w:
    w.write(b"RIG1" + struct.pack("<iif", len(bones), nv, FPS))
    for i, b in enumerate(bones):
        nm = b.name.encode()
        m = b.matrix_local
        w.write(struct.pack("<B", len(nm)) + nm + struct.pack("<iB", bi[b.parent.name] if b.parent else -1, 0 if b.name in LOWER else 1))
        w.write(struct.pack("<12f", *[m[r][c] for r in range(3) for c in range(4)]))
    for j in range(nv):
        p, n = restP[j], restN[j]
        w.write(struct.pack("<6f", p.x, p.y, p.z, n.x, n.y, n.z))
        w.write(struct.pack("<4B4B", *[b for b, _ in wts[j]], *[x for _, x in wts[j]]))
    w.write(struct.pack("<i", len(clips)))
    for cn, loop, frames in clips:
        w.write(struct.pack("<B", len(cn)) + cn.encode() + struct.pack("<Bi", 1 if loop else 0, len(frames)))
        for fr in frames:
            for q, l in fr:
                w.write(struct.pack("<4h3h", *[int(round(c * 32767)) for c in (q.w, q.x, q.y, q.z)],
                                    *[max(-32768, min(32767, int(round(c * 4096)))) for c in (l.x, l.y, l.z)]))
img = [i for i in bpy.data.images if i.packed_file or i.has_data]
for i in img[:1]:
    i.filepath_raw = os.path.join(dst, name + "_albedo.png"); i.file_format = 'PNG'; i.save()
print("baked", name, nv, "verts", len(idx) // 3, "tris", len(bones), "bones", sum(len(c[2]) for c in clips), "frames")
