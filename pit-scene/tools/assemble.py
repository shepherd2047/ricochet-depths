"""Headless Blender: lay the Theme 1 treadmill corridor (spec.md section 1.3a) around a board and render a
preview with the in-game camera, lighting and fog.

Usage (from pit-scene/, after tools/build_kit.py):
  B=/opt/homebrew/bin/blender
  $B -b kit/t1_kit.blend -P tools/assemble.py -- --cols 7 --rows 9 --turn 1 --out preview/t1_corridor_7x9_start.png
  $B -b kit/t1_kit.blend -P tools/assemble.py -- --cols 9 --rows 11 --turn 30 --out preview/t1_corridor_9x11_mid.png
  $B -b kit/t1_kit.blend -P tools/assemble.py -- --cols 13 --rows 15 --turn 60 --out preview/t1_corridor_13x15_final.png
  $B -b kit/t1_kit.blend -P tools/assemble.py -- --kitsheet --out preview/t1_kit_sheet.png
Options: --final 60 (final boss turn) --tilt 30 --fov 65 --balls (placeholder emissive balls) --seed N

Pit frame (Blender): floor = XY plane at z = 0, board centred on the origin, columns along X, rows along Y
(player at -Y, the far end / enemy side at +Y), +Z = out of the floor toward the camera.
The layout rules here are the ones ASSEMBLY.md describes for Unity (pick_segments etc.); keep them in sync.
"""
import argparse
import math
import sys

import bpy
from mathutils import Euler, Vector

CELL = 1.125
GROUP = 4                         # rows per recycling group = one wall segment length (4.5 units)
G_MIN, G_MAX = -4, 4              # 9 groups alive: cells k = 4g .. 4g+3, i.e. k -16 .. 19 (screen bottom .. fog end)
SETS = {
    "start": ["T1_Start_A", "T1_Start_B", "T1_Start_C"],
    "mid": ["T1_Mid_A", "T1_Mid_B", "T1_Mid_C"],
    "end": ["T1_End_A", "T1_End_B", "T1_End_C"],
}
LANES = {k: [f"T1_Lane_{k.capitalize()}_{c}" for c in "ABC"] for k in ("start", "mid", "end")}
LANE_W = CELL                     # prop lane between the board edge (kerb) and the wall face; not part of the board
SET_EDGES = (0.3, 0.7)            # progress < 0.3 start, < 0.7 mid, else end
SET_BLEND = 0.05                  # +/- jitter on progress per group so set changes are not a hard line
GATE_K_AFTER_BOARD = 3            # gate front stops this many cells beyond the board's far row
TOWER_OFF = 1.2                   # gate tower centre outside the wall face

# lighting / fog (see LIGHTING.md; Unity values there)
FOG_RGB = (38, 41, 55)
AMB_SKY, AMB_EQ, AMB_GROUND = (54, 58, 66), (29, 32, 34), (12, 11, 9)
COPPER = (217, 136, 65)
KEY_YAW = 0.0    # deg; style.md key (10 deg off the view axis, no yaw). A yaw >0 lights the left wall face but casts a wall shadow onto the board - keep 0 for readability
WALL_FILL_RGB = (150, 172, 190)   # cool grey-blue
WALL_FILL_INT = 0.8               # Unity intensity, each
WALL_FILL_TILT = 80               # deg from straight down toward the lit wall (L light rakes the left wall)
FIT_MARGIN = 1.5                  # cells of wall shown beside the near row's outer columns (--margin)
FOG_STEP, FOG_CAP, FOG_RAMP = 0.45, 0.8, 9.0   # pit-axis fog past the far-wall line, see fog_factor()


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
    ap.add_argument("--turn", type=int, default=1)
    ap.add_argument("--final", type=int, default=60)
    ap.add_argument("--seed", type=int, default=12345)
    ap.add_argument("--tilt", type=float, default=40.0)   # style.md section 2 as ruled 2026-10-19 (second ruling)
    ap.add_argument("--fov", type=float, default=65.0)
    ap.add_argument("--margin", type=float, default=1.5)   # cells beside the near row: prop lane + half a cell of wall (ruled 2026-10-19)
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


def ghash(*xs):
    """Small deterministic integer hash (same in C#: unchecked uint math)."""
    h = 2166136261
    for x in xs:
        h = ((h ^ (x & 0xFFFFFFFF)) * 16777619) & 0xFFFFFFFF
    return h


def group_progress(g, turn, final, rows):
    """Progress 0..1 of the rows in group g: the turn at which those rows reach the board's far row."""
    k_centre = g * GROUP + (GROUP - 1) / 2
    row_turn = turn + (k_centre - rows / 2)
    return min(max(row_turn / final, 0.0), 1.0)


def pick_set(p, h):
    p = p + ((h % 1000) / 1000 - 0.5) * 2 * SET_BLEND
    return "start" if p < SET_EDGES[0] else "mid" if p < SET_EDGES[1] else "end"


def pick_segments(g, p, seed, prev):
    """Left and right segment names for group g. Left != right; a side avoids repeating its previous segment
    when the set has 3+ pieces. prev = {'L': name, 'R': name} of group g-1 (updated in place)."""
    out = {}
    for side in ("L", "R"):
        h = ghash(seed, g, ord(side))
        names = SETS[pick_set(p, h)]
        n = len(names)
        i = (h >> 10) % n
        for _ in range(n):
            bad = names[i] == out.get("L") or (n >= 3 and names[i] == prev.get(side))
            if not bad:
                break
            i = (i + 1) % n
        out[side] = names[i]
    prev.update(out)
    return out


def layout_corridor(cols, rows, turn, final, seed, coll, balls=False):
    k = kit()
    half_w = cols * CELL / 2
    wall_x = half_w + LANE_W                                      # wall face (segment + lane origin)
    count = {}
    prev = {}
    for g in range(G_MIN, G_MAX + 1):
        y = (g * GROUP + (GROUP - 1) / 2) * CELL                 # group centre
        p = group_progress(g, turn, final, rows)
        segs = pick_segments(g, p, seed, prev)
        for side, x, rot in (("L", -wall_x, 90), ("R", wall_x, -90)):   # piece -Y (inner face) -> board centre
            place(k[segs[side]], (x, y, 0), rot, coll)
            count[segs[side]] = count.get(segs[side], 0) + 1
            set_name = segs[side].split("_")[1].lower()
            ln = LANES[set_name][(ghash(seed, g, ord(side), 99) >> 7) % 3]   # same set as the wall beside it
            place(k[ln], (x, y, 0), rot, coll)
            count[ln] = count.get(ln, 0) + 1
        fl = f"T1_FloorStrip_W{cols + 2}_{'B' if ghash(seed, g, 7) % 3 == 0 else 'A'}"
        place(k[fl], (0, y, 0), 0, coll)
        count[fl] = count.get(fl, 0) + 1
        print("GROUP %+d p=%.2f %s | %s" % (g, p, segs["L"], segs["R"]))
    # end-of-corridor gate: scrolls in with the corridor and stops GATE_K_AFTER_BOARD cells past the far row
    k_gate = rows / 2 + GATE_K_AFTER_BOARD + (final - turn)
    if k_gate <= G_MAX * GROUP + GROUP:
        yg = k_gate * CELL
        gate = [place(k["T1_Gate_Tower"], (sx * (wall_x + TOWER_OFF), yg, 0), 0, coll) for sx in (-1, 1)]
        lt = place(k["T1_Gate_Lintel"], (0, yg, 0), 0, coll)
        lt.scale.x = (2 * wall_x + 2 * TOWER_OFF) / 17.0
        for ob in gate + [lt]:
            ob.visible_shadow = False   # Unity: Cast Shadows Off; its shadow band would cross the board
        count["T1_Gate"] = 1
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


def fog_factor(nt, y_line):
    """Pit-axis fog (LIGHTING.md, spec section 4.4 as changed 2026-10-19): 0 on the board, FOG_STEP within the
    first cell past the far-wall line (smoothstep), then linear up to FOG_CAP over FOG_RAMP more units, and to 1
    over the last live group so the treadmill's cut never shows. Height independent; the corridor and the gate
    beyond the line stay readable as 'distance'."""
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Position"], sep.inputs[0])
    a = nt.nodes.new("ShaderNodeMapRange")
    a.interpolation_type = "SMOOTHSTEP"
    a.inputs["From Min"].default_value, a.inputs["From Max"].default_value = y_line, y_line + CELL
    a.inputs["To Min"].default_value, a.inputs["To Max"].default_value = 0.0, FOG_STEP
    b = nt.nodes.new("ShaderNodeMapRange")
    b.inputs["From Min"].default_value, b.inputs["From Max"].default_value = y_line + CELL, y_line + CELL + FOG_RAMP
    b.inputs["To Min"].default_value, b.inputs["To Max"].default_value = 0.0, FOG_CAP - FOG_STEP
    for n in (a, b):
        n.clamp = True
        nt.links.new(sep.outputs["Y"], n.inputs["Value"])
    c = nt.nodes.new("ShaderNodeMapRange")     # last group before the treadmill's end: close to 1, hides the cut
    y_end = (G_MAX + 1) * GROUP * CELL
    c.inputs["From Min"].default_value, c.inputs["From Max"].default_value = y_end - GROUP * CELL, y_end
    c.inputs["To Min"].default_value, c.inputs["To Max"].default_value = 0.0, 1.0 - FOG_CAP
    c.clamp = True
    nt.links.new(sep.outputs["Y"], c.inputs["Value"])
    add = nt.nodes.new("ShaderNodeMath")
    nt.links.new(a.outputs["Result"], add.inputs[0])
    nt.links.new(b.outputs["Result"], add.inputs[1])
    add2 = nt.nodes.new("ShaderNodeMath")
    nt.links.new(add.outputs[0], add2.inputs[0])
    nt.links.new(c.outputs["Result"], add2.inputs[1])
    return add2.outputs[0]


def add_fog_to_material(rows):
    y_line = rows / 2 * CELL      # far-wall line = far edge of the board's top row
    m = bpy.data.materials["T1_Scenery"]
    nt = m.node_tree
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    bsdf = nt.nodes.get("Principled BSDF")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = lin(FOG_RGB)
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(fog_factor(nt, y_line), mix.inputs["Fac"])
    nt.links.new(bsdf.outputs[0], mix.inputs[1])
    nt.links.new(em.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    bm_ = bpy.data.materials.get("T1_Beam")    # the light pools fade with the same fog factor
    if bm_:
        bt = bm_.node_tree
        mul = next(n for n in bt.nodes if n.type == "MATH")
        inv = bt.nodes.new("ShaderNodeMapRange")
        inv.inputs["To Min"].default_value, inv.inputs["To Max"].default_value = 1.2, 0.0
        bt.links.new(fog_factor(bt, y_line), inv.inputs["Value"])
        bt.links.new(inv.outputs["Result"], mul.inputs[1])


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
    # wall fill: two weak cool directionals, no shadows, one raking across each wall's inner face. The key runs
    # parallel to the walls, so without these the wall faces only get ambient (Unity: 2 additional lights)
    for side, ry in (("L", WALL_FILL_TILT), ("R", -WALL_FILL_TILT)):
        fl = bpy.data.lights.new("WallFill" + side, "SUN")
        fl.color = lin(WALL_FILL_RGB)[:3]
        fl.energy = math.pi * WALL_FILL_INT
        fl.use_shadow = False
        fo = bpy.data.objects.new("WallFill" + side, fl)
        fo.rotation_euler = Euler((math.radians(30), math.radians(ry), 0), "XYZ")   # +X tip: travels toward the far end, so it also lights the post sides that face the player
        bpy.context.scene.collection.objects.link(fo)
    # theme 1 local light: copper spot from an off-screen furnace mouth, top-left (far end, left wall, high)
    sp = bpy.data.lights.new("FurnaceSpot", "SPOT")
    sp.color = lin(COPPER)[:3]
    sp.energy = 2500
    sp.spot_size = math.radians(55)
    sp.spot_blend = 0.6
    sp.shadow_soft_size = 0.8
    spo = bpy.data.objects.new("FurnaceSpot", sp)
    half_w = cols * CELL / 2 + LANE_W                    # wall face
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

    half_x, half_y = (cols / 2 + FIT_MARGIN) * CELL, rows / 2 * CELL
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
    """Front view of every piece: one row per wall set, then floor strips, props and the gate."""
    sc = bpy.context.scene
    coll = bpy.data.collections.new("Sheet")
    sc.collection.children.link(coll)
    k = kit()
    rows = [SETS["start"], SETS["mid"], SETS["end"]]
    for r, names in enumerate(rows):
        for i, n in enumerate(names):
            place(k[n], (i * 5.0, 0, (2 - r) * 6.5), 0, coll)
    for r, key in enumerate(("start", "mid", "end")):
        for i, n in enumerate(LANES[key]):
            place(k[n], (i * 5.0, -0.2, (2 - r) * 6.5), 0, coll)
    place(k["T1_FloorStrip_W9_A"], (5.0, -2.6, -1.2), 0, coll)
    for i, n in enumerate(("T1_Prop_Hook", "T1_Prop_BellShard", "T1_Prop_Ladle")):
        place(k[n], (16.0 + i * 1.8, -0.6, 4.2 if n == "T1_Prop_Hook" else 13.6), 0, coll)
    for sx in (-1, 1):
        place(k["T1_Gate_Tower"], (25.5 + sx * 7.4, 0, 0), 0, coll)
    lt = place(k["T1_Gate_Lintel"], (25.5, 0, 0), 0, coll)
    lt.scale.x = 14.8 / 17.0
    world_trilight()
    sun = bpy.data.lights.new("Key", "SUN"); sun.energy = 3.5; sun.angle = math.radians(5)
    so = bpy.data.objects.new("Key", sun); so.rotation_euler = Euler((math.radians(55), 0, math.radians(-25)))
    sc.collection.objects.link(so)
    cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"; cd.ortho_scale = 40
    co = bpy.data.objects.new("Cam", cd)
    co.location = (16.2, -30, 9.2); co.rotation_euler = Euler((math.radians(88), 0, 0))
    sc.collection.objects.link(co); sc.camera = co
    render_setup(out, res)
    sc.render.resolution_x, sc.render.resolution_y = 2400, 1400
    bpy.data.worlds["T1_World"].node_tree.nodes["Background.001"].inputs["Color"].default_value = lin((70, 74, 84))
    bpy.ops.render.render(write_still=True)


def main():
    a = args()
    global FIT_MARGIN
    FIT_MARGIN = a.margin
    if a.kitsheet:
        kitsheet(a.out, a.res)
        return
    sc = bpy.context.scene
    coll = bpy.data.collections.new(f"Board_{a.cols}x{a.rows}")
    sc.collection.children.link(coll)
    count = layout_corridor(a.cols, a.rows, a.turn, a.final, a.seed, coll, a.balls)
    dist = camera(a.cols, a.rows, a.tilt, a.fov)
    add_fog_to_material(a.rows)
    world_trilight()
    lights(a.cols, a.rows)
    render_setup(a.out, a.res)
    bpy.ops.render.render(write_still=True)
    print("ASSEMBLE", a.cols, a.rows, "cam_dist %.2f fog line y %.2f" % (dist, a.rows / 2 * CELL), count)


if __name__ == "__main__":
    main()
