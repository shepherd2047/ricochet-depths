"""Headless Blender: lay the Theme 1 kit around a board and render a preview with the in-game lighting/fog.

Usage (from pit-scene/, after tools/build_kit.py):
  /opt/homebrew/bin/blender -b kit/t1_kit.blend -P tools/assemble.py -- --cols 7 --rows 9 --out preview/t1_board_7x9.png
  /opt/homebrew/bin/blender -b kit/t1_kit.blend -P tools/assemble.py -- --cols 13 --rows 15 --out preview/t1_board_13x15.png
  /opt/homebrew/bin/blender -b kit/t1_kit.blend -P tools/assemble.py -- --kitsheet --out preview/t1_kit_sheet.png
Options: --tilt 30 (camera pitch into the pit, 0 = style.md straight-on) --balls (placeholder emissive balls)

Pit frame (Blender): floor = XY plane at z = 0, board centred on the origin, columns along X, rows along Y
(player at -Y, the far end / enemy side at +Y), +Z = out of the floor toward the camera.
The layout rules here are the same ones ASSEMBLY.md describes for Unity; keep them in sync.
"""
import argparse
import math
import sys

import bpy
from mathutils import Euler, Vector

CELL = 1.125
K_NEAR, K_FAR = -16, 21          # wall + floor run in cells along Y, same for every board size (covers the 13x15 camera)
PATTERN = ["T1_Pillar", "T1_Wall_BrickA", "T1_Wall_IronRib", "T1_Wall_BrickB",
           "T1_Wall_Chain", "T1_Wall_BrickA", "T1_Wall_Trough", "T1_Wall_BrickB"]
RIGHT_OFFSET = 4                 # right wall reads the pattern 4 slots later
RIGHT_SWAP = {"T1_Wall_Chain": "T1_Wall_Mould"}

# lighting / fog (see LIGHTING.md; Unity values there)
FOG_RGB = (38, 41, 55)
AMB_SKY, AMB_EQ, AMB_GROUND = (54, 58, 66), (29, 32, 34), (12, 11, 9)
COPPER = (217, 136, 65)
KEY_YAW = 0.0    # deg; style.md key (10 deg off the view axis, no yaw). A yaw >0 lights the left wall face but casts a wall shadow onto the board - keep 0 for readability
FOG_START_OFF, FOG_END_OFF = 2.0, 14.0   # fog start/end = camera-to-board distance + these


def srgb_to_lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(rgb, a=1.0):
    return tuple(srgb_to_lin(c) for c in rgb) + (a,)


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--cols", type=int, default=7)
    ap.add_argument("--rows", type=int, default=9)
    ap.add_argument("--tilt", type=float, default=30.0)
    ap.add_argument("--fov", type=float, default=65.0)
    ap.add_argument("--balls", action="store_true")
    ap.add_argument("--kitsheet", action="store_true")
    ap.add_argument("--out", required=True)
    ap.add_argument("--res", type=int, default=720, help="output width; height = width * 16/9")
    return ap.parse_args(argv)


def kit():
    return {o.name: o for o in bpy.data.collections["Kit"].objects}


def place(src, loc, rot_z_deg, coll, name=None):
    ob = bpy.data.objects.new(name or src.name, src.data)   # linked duplicate: shares the mesh
    ob.location = loc
    ob.rotation_euler = Euler((0, 0, math.radians(rot_z_deg)))
    coll.objects.link(ob)
    return ob


def layout_board(cols, rows, coll, balls=False):
    k = kit()
    half_w = cols * CELL / 2
    count = {}
    for side in (-1, 1):
        x = side * half_w
        rot = 90 if side < 0 else -90        # piece -Y (inner face) -> toward the board centre
        for kk in range(K_NEAR, K_FAR + 1):
            idx = (kk + (RIGHT_OFFSET if side > 0 else 0)) % len(PATTERN)
            name = PATTERN[idx]
            if side > 0:
                name = RIGHT_SWAP.get(name, name)
            if kk == K_NEAR:
                name = "T1_Pillar"          # close the near end of each run
            y = kk * CELL
            place(k[name], (x, y, 0), rot, coll)
            count[name] = count.get(name, 0) + 1
            # props attached to wall slots (positions in the piece's local frame, rotated to world)
            local = None
            if name == "T1_Wall_IronRib" and (kk // 8) % 2 == 0:
                local, prop, prot = (0.0, -0.2, 3.86), "T1_Prop_Hook", 0
            elif name == "T1_Wall_Trough" and side < 0:
                local, prop, prot = (0.3, 0.28, 0.45), "T1_Prop_Ladle", 200
            if local:
                lx, ly, lz = local
                c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
                wpos = (x + lx * c - ly * s, y + lx * s + ly * c, lz)
                ob = place(k[prop], wpos, rot + prot, coll)
                if prop == "T1_Prop_Ladle":
                    ob.rotation_euler = Euler((0, math.radians(-12), math.radians(rot + prot)))
                count[prop] = count.get(prop, 0) + 1
    # floor: one tile per cell, cols wide, over the whole run
    for i in range(cols):
        for kk in range(K_NEAR, K_FAR + 1):
            fx = (i - (cols - 1) / 2) * CELL
            variant = "T1_Floor_B" if (i * 7 + kk * 13) % 6 == 0 else "T1_Floor_A"
            place(k[variant], (fx, kk * CELL, 0), 0, coll)
            count[variant] = count.get(variant, 0) + 1
    # bell shards on the floor, outside the largest board's rows (|row| > 7.5 cells)
    for (sx, kk, r) in ((-1, 12, 25), (1, 16, -160), (1, -10, 200)):
        x = sx * (half_w - 0.7)
        place(k["T1_Prop_BellShard"], (x, kk * CELL, 0), r + (90 if sx < 0 else -90), coll)
        count["T1_Prop_BellShard"] = count.get("T1_Prop_BellShard", 0) + 1
    if balls:
        for (bx, by, col) in ((-1.2, -2.0, (255, 210, 120)), (0.9, 0.6, (140, 240, 235)), (2.0, 2.6, (255, 160, 70))):
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.18, location=(bx, by, 0.25), segments=12, ring_count=6)
            s = bpy.context.active_object
            m = bpy.data.materials.new("BallPreview")
            m.use_nodes = True
            nt = m.node_tree
            nt.nodes.clear()
            em = nt.nodes.new("ShaderNodeEmission")
            em.inputs["Color"].default_value = lin(col)
            em.inputs["Strength"].default_value = 4.0
            out = nt.nodes.new("ShaderNodeOutputMaterial")
            nt.links.new(em.outputs[0], out.inputs[0])
            s.data.materials.append(m)
    return count


def add_fog_to_material(cam_dist):
    m = bpy.data.materials["T1_Scenery"]
    nt = m.node_tree
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    bsdf = nt.nodes.get("Principled BSDF")
    camd = nt.nodes.new("ShaderNodeCameraData")
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = cam_dist + FOG_START_OFF
    mr.inputs["From Max"].default_value = cam_dist + FOG_END_OFF
    mr.clamp = True
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = lin(FOG_RGB)
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(camd.outputs["View Distance"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], mix.inputs["Fac"])
    nt.links.new(bsdf.outputs[0], mix.inputs[1])
    nt.links.new(em.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])


def world_trilight():
    """Unity-style trilight ambient (sky / equator / ground) with Unity's 'up' = pit +Y
    (the board stands in Unity's XY plane, so world up there is the far end of the pit here).
    Camera rays see the fog colour, like Unity's solid-colour camera background."""
    w = bpy.data.worlds.new("T1_World")
    bpy.context.scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    up = nt.nodes.new("ShaderNodeMapRange")       # y in [0,1] -> equator..sky
    dn = nt.nodes.new("ShaderNodeMapRange")       # y in [0,-1] -> equator..ground
    dn.inputs["From Min"].default_value, dn.inputs["From Max"].default_value = 0.0, -1.0
    for n in (up, dn):
        n.clamp = True
        nt.links.new(sep.outputs["Y"], n.inputs["Value"])
    m1 = nt.nodes.new("ShaderNodeMix"); m1.data_type = "RGBA"
    m2 = nt.nodes.new("ShaderNodeMix"); m2.data_type = "RGBA"
    m1.inputs["A"].default_value, m1.inputs["B"].default_value = lin(AMB_EQ), lin(AMB_SKY)
    nt.links.new(up.outputs["Result"], m1.inputs["Factor"])
    nt.links.new(m1.outputs["Result"], m2.inputs["A"])
    m2.inputs["B"].default_value = lin(AMB_GROUND)
    nt.links.new(dn.outputs["Result"], m2.inputs["Factor"])
    amb = nt.nodes.new("ShaderNodeBackground")
    nt.links.new(m2.outputs["Result"], amb.inputs["Color"])
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Color"].default_value = lin(FOG_RGB)
    lp = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(lp.outputs["Is Camera Ray"], mix.inputs["Fac"])
    nt.links.new(amb.outputs[0], mix.inputs[1])
    nt.links.new(bg.outputs[0], mix.inputs[2])
    out = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(mix.outputs[0], out.inputs["Surface"])


def lights(cols, rows):
    # key: white directional, Unity intensity 1 (Blender sun strength pi), 10 deg off the view axis toward
    # the near end, soft shadows
    sun = bpy.data.lights.new("Key", "SUN")
    sun.energy = math.pi
    sun.angle = math.radians(4)
    so = bpy.data.objects.new("Key", sun)
    so.rotation_euler = Euler((math.radians(-10), math.radians(KEY_YAW), 0), "YXZ")
    bpy.context.scene.collection.objects.link(so)
    # theme 1 local light: copper spot from an off-screen furnace mouth, top-left (far end, left wall, high)
    sp = bpy.data.lights.new("FurnaceSpot", "SPOT")
    sp.color = lin(COPPER)[:3]
    sp.energy = 2500
    sp.spot_size = math.radians(55)
    sp.spot_blend = 0.6
    sp.shadow_soft_size = 0.8
    spo = bpy.data.objects.new("FurnaceSpot", sp)
    half_w = cols * CELL / 2
    spo.location = (-half_w - 0.5, (rows / 2 + 3) * CELL, 6.0)
    target = Vector((half_w, rows * CELL * 0.15, 1.5))   # right wall, mid-board
    spo.rotation_euler = (target - spo.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(spo)


def fit_camera(cols, rows, tilt_deg, fov_deg, aspect=9 / 16):
    """style.md section 2 (2026-10-19): perspective, tilted ~30 deg from straight-down toward the far end,
    from behind/above the player. Under perspective the NEAR row is the widest on screen, so it is the binding
    fit: near-row corners incl. half a cell of margin each side touch the screen edges. The board is centred
    vertically. Returns (target_y, distance camera->target along the view axis, distance camera->board centre)."""
    t, vf = math.radians(tilt_deg), math.radians(fov_deg)
    tx = math.tan(math.atan(math.tan(vf / 2) * aspect))
    ty_ = math.tan(vf / 2)
    st, ct = math.sin(t), math.cos(t)

    def proj(px, py, ty, d):
        cy, cz = ty - d * st, d * ct
        ry, rz = py - cy, -cz
        z = ry * st - rz * ct
        return px / (z * tx), (ry * ct + rz * st) / (z * ty_)

    half_x, half_y = (cols / 2 + 0.5) * CELL, rows / 2 * CELL
    best = None
    for i in range(-400, 401):
        ty = i / 100
        lo, hi = 1.0, 100.0
        for _ in range(50):
            d = (lo + hi) / 2
            lo, hi = (d, hi) if proj(half_x, -half_y, ty, d)[0] > 1 else (lo, d)
        d = hi
        bal = proj(0, -half_y, ty, d)[1] + proj(0, half_y, ty, d)[1]
        if best is None or abs(bal) < abs(best[2]):
            best = (ty, d, bal)
    ty, d, _ = best
    centre = math.hypot(ty - d * st, d * ct)
    return ty, d, centre


def camera(cols, rows, tilt, fov):
    sc = bpy.context.scene
    cd = bpy.data.cameras.new("Cam")
    cd.sensor_fit = "VERTICAL"
    cd.angle_y = math.radians(fov)
    cd.clip_start, cd.clip_end = 0.3, 1000
    ty, dist, centre = fit_camera(cols, rows, tilt, fov)
    co = bpy.data.objects.new("Cam", cd)
    t = math.radians(tilt)
    target = Vector((0, ty, 0))
    co.location = target + Vector((0, -math.sin(t), math.cos(t))) * dist
    co.rotation_euler = Euler((t, 0, 0))
    sc.collection.objects.link(co)
    sc.camera = co
    print("CAMERA %dx%d target_y %.2f dist_target %.2f dist_centre %.2f pos %s" % (cols, rows, ty, dist, centre, tuple(round(c, 2) for c in co.location)))
    return dist


def render_setup(out, res):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = res
    sc.render.resolution_y = int(res * 16 / 9)
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    try:
        sc.eevee.taa_render_samples = 32
        sc.eevee.use_shadows = True
    except AttributeError:
        pass
    sc.render.filepath = out
    sc.render.image_settings.file_format = "PNG"


def kitsheet(out, res):
    sc = bpy.context.scene
    coll = bpy.data.collections.new("Sheet")
    sc.collection.children.link(coll)
    k = kit()
    order = ["T1_Wall_BrickA", "T1_Wall_BrickB", "T1_Wall_IronRib", "T1_Wall_Chain", "T1_Wall_Mould",
             "T1_Wall_Trough", "T1_Pillar", "T1_Prop_Hook", "T1_Prop_BellShard", "T1_Prop_Ladle",
             "T1_Floor_A", "T1_Floor_B"]
    x = 0
    for i, n in enumerate(order):
        if n.startswith("T1_Prop"):
            z = 4.2 if n == "T1_Prop_Hook" else 0
            place(k[n], (x, -0.6, z), 0 if n != "T1_Prop_BellShard" else 0, coll)
        else:
            place(k[n], (x, 0, 0 if not n.startswith("T1_Floor") else 0.001), 0, coll)
        x += 1.6
    for i in range(-1, 13):
        for j in range(-2, 2):
            place(k["T1_Floor_A"], (i * CELL, j * CELL - 0.6, -0.002), 0, coll)
    world_trilight()
    sun = bpy.data.lights.new("Key", "SUN"); sun.energy = 3.5; sun.angle = math.radians(5)
    so = bpy.data.objects.new("Key", sun); so.rotation_euler = Euler((math.radians(55), 0, math.radians(-25)))
    sc.collection.objects.link(so)
    cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"; cd.ortho_scale = x + 0.5
    co = bpy.data.objects.new("Cam", cd)
    co.location = (x / 2 - 0.8, -14, 4.5); co.rotation_euler = Euler((math.radians(78), 0, 0))
    sc.collection.objects.link(co); sc.camera = co
    render_setup(out, res)
    sc.render.resolution_x, sc.render.resolution_y = 2400, 900
    bpy.data.worlds["T1_World"].node_tree.nodes["Background.001"].inputs["Color"].default_value = lin((70, 74, 84))
    bpy.ops.render.render(write_still=True)


def main():
    a = args()
    if a.kitsheet:
        kitsheet(a.out, a.res)
        return
    sc = bpy.context.scene
    coll = bpy.data.collections.new(f"Board_{a.cols}x{a.rows}")
    sc.collection.children.link(coll)
    count = layout_board(a.cols, a.rows, coll, a.balls)
    dist = camera(a.cols, a.rows, a.tilt, a.fov)
    add_fog_to_material(dist)
    world_trilight()
    lights(a.cols, a.rows)
    render_setup(a.out, a.res)
    bpy.ops.render.render(write_still=True)
    print("ASSEMBLE", a.cols, a.rows, "cam_dist %.2f fog %.1f-%.1f" % (dist, dist + FOG_START_OFF, dist + FOG_END_OFF), count)


if __name__ == "__main__":
    main()
