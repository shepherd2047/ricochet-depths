"""Cells from rig_hero_layered.py --sheet → contact sheets and GIFs (any Python with Pillow).

python tools/hero_layered_sheet.py <sheet_dir> <out_prefix>
writes <out_prefix>_sheet_game.png, <out_prefix>_sheet_front.png and <out_prefix>_<combo>.gif (10 fps).
"""
import os
import sys

from PIL import Image, ImageDraw

src, prefix = sys.argv[1], sys.argv[2]
ORDER = ["AimIdle_L", "AimIdle_F", "AimIdle_R", "AimWalk_L", "AimWalk_F", "AimWalk_R", "Shoot_L", "Shoot_F", "Shoot_R",
         "Stand", "WalkForward", "WalkBack", "StrafeLeft", "StrafeRight", "Death", "Victory"]
NOTE = {  # what each row is composed with (the other layer)
    "AimIdle": "upper, 0.9 s loop  (+ Stand)", "AimWalk": "upper, 0.8 s loop  (+ WalkForward)",
    "Shoot": "upper, 0.2 s once  (+ Stand)", "Stand": "lower, 0.9 s loop  (+ AimIdle_F)",
    "Walk": "lower, 0.8 s loop  (+ AimWalk_F)", "Strafe": "lower, 0.8 s loop  (+ AimWalk_F)",
    "Death": "full body, 0.8 s, holds last", "Victory": "full body, 0.9 s loop",
}
CELL, LABEL, COLS = 150, 190, 9


def note(name):
    return next(v for k, v in NOTE.items() if name.startswith(k))


for view in ("game", "front"):
    rows = [c for c in ORDER if os.path.isdir(os.path.join(src, "sheet", view, c))]
    sheet = Image.new("RGB", (LABEL + COLS * CELL, 40 + len(rows) * CELL), (24, 24, 27))
    d = ImageDraw.Draw(sheet)
    d.text((10, 12), f"Tinker layered motion set, every key at 10 fps ({view} view). Columns = frames 0.0, 0.1, 0.2 s ...",
           fill=(230, 230, 230))
    for r, c in enumerate(rows):
        y = 40 + r * CELL
        d.text((10, y + 55), c, fill=(255, 220, 120))
        d.text((10, y + 75), note(c), fill=(180, 180, 180))
        fr = sorted(f for f in os.listdir(os.path.join(src, "sheet", view, c)) if f.endswith(".png"))
        for i, f in enumerate(fr):
            im = Image.open(os.path.join(src, "sheet", view, c, f)).convert("RGB")
            w, h = im.size
            im = im.crop((int(w * 0.11), int(h * 0.0), int(w * 0.89), int(h * 0.78))).resize((CELL, CELL), Image.LANCZOS)
            sheet.paste(im, (LABEL + i * CELL, y))
            d.text((LABEL + i * CELL + 4, y + 3), f"{i / 10:.1f}", fill=(220, 220, 220))
    out = f"{prefix}_sheet_{view}.png"
    sheet.save(out)
    print("wrote", out, sheet.size)

TITLE = {"walkback_shoot": "WalkBack (lower) + AimWalk_F / Shoot_F (upper): backing off, firing forward",
         "strafeleft_aimright": "StrafeLeft (lower) + AimWalk_R / Shoot_R (upper): strafing left, aiming right"}
for combo in sorted(os.listdir(os.path.join(src, "gif"))) if os.path.isdir(os.path.join(src, "gif")) else []:
    views = ("game", "front")
    files = sorted(os.listdir(os.path.join(src, "gif", combo, "game")))
    frames = []
    for f in files:
        ims = [Image.open(os.path.join(src, "gif", combo, v, f)).convert("RGB") for v in views]
        w, h = ims[0].size
        fr = Image.new("RGB", (w * 2, h + 26), (24, 24, 27))
        for k, im in enumerate(ims):
            fr.paste(im, (k * w, 26))
        ImageDraw.Draw(fr).text((8, 7), TITLE.get(combo, combo), fill=(235, 235, 235))
        frames.append(fr.quantize(colors=128, method=Image.MEDIANCUT, dither=Image.NONE))
    out = f"{prefix}_{combo}.gif"
    frames[0].save(out, save_all=True, append_images=frames[1:], duration=100, loop=0)
    print("wrote", out, len(frames), "frames")
