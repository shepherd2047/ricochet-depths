"""Frames from render_clips() / rig_hero.py --clip-frames → GIFs (run with any Python that has Pillow).

python tools/make_gifs.py <frames_dir> <out_prefix> [--fps 24] [--hold Attack,Hit,Death]
writes <out_prefix>_<clip>.gif for every clip folder plus <out_prefix>_all.gif (grid, labelled).
"""
import argparse
import os

from PIL import Image, ImageDraw

ap = argparse.ArgumentParser()
ap.add_argument("frames")
ap.add_argument("prefix")
ap.add_argument("--fps", type=float, default=24)
ap.add_argument("--hold", default="Attack,Hit,Death,Shoot", help="one-shot clips: hold the last frame 0.5 s")
ap.add_argument("--cell", type=int, default=200)
a = ap.parse_args()
hold = set(a.hold.split(","))
clips = sorted(d for d in os.listdir(a.frames) if os.path.isdir(os.path.join(a.frames, d)))
order = ["Idle", "Walk", "Move", "Attack", "Shoot", "Hit", "Death", "Victory"]
clips.sort(key=lambda c: order.index(c) if c in order else 99)
dur = round(1000 / a.fps)
seqs = {}
for c in clips:
    d = os.path.join(a.frames, c)
    fr = [Image.open(os.path.join(d, f)).convert("RGB") for f in sorted(os.listdir(d)) if f.endswith(".png")]
    if c in hold:
        fr += [fr[-1]] * int(a.fps / 2)
    seqs[c] = fr
    fr[0].save(f"{a.prefix}_{c.lower()}.gif", save_all=True, append_images=fr[1:], duration=dur, loop=0)
cols = 3
rows = (len(clips) + cols - 1) // cols
n = max(len(s) for s in seqs.values())
W = a.cell
grid = []
for i in range(n):
    g = Image.new("RGB", (W * cols, W * rows), (34, 34, 36))
    dr = ImageDraw.Draw(g)
    for k, c in enumerate(clips):
        s = seqs[c]
        g.paste(s[i % len(s)].resize((W, W)), ((k % cols) * W, (k // cols) * W))
        dr.text(((k % cols) * W + 8, (k // cols) * W + 6), c, fill=(230, 230, 230))
    grid.append(g.quantize(64))
grid[0].save(f"{a.prefix}_all.gif", save_all=True, append_images=grid[1:], duration=dur, loop=0)
print("wrote", f"{a.prefix}_all.gif", "+", len(clips), "clip GIFs")
