"""Paint the Theme 1 ("The Sunken Yard") scenery atlas: one 1024x1024 PNG shared by every kit piece.

Usage:  python3 tools/make_atlas.py [out.png]

Layout (pixels, origin top-left; Blender/Unity UV v = 1 - y/1024). Keep in sync with REGIONS in build_kit.py.
  brick_a   x   0-255  y    0-1023   one wall-segment face: 1.125 wide x 4.5 tall, tiles left/right
  brick_b   x 256-511  y    0-1023   same grid, cracked / sooty / missing bricks
  stone     x 512-767  y    0-511    dressed stone blocks (piers, coping, plinth, trough), tiles both ways
  floor_a   x 512-767  y  512-767    one 1.125 x 1.125 floor cell
  floor_b   x 512-767  y  768-1023   floor cell, cracked
  iron      x 768-1023 y    0-255    cast iron plate
  clay      x 768-1023 y  256-511    fired mould clay
  bronze    x 768-1023 y  512-767    bell bronze with patina
  swatches  x 768-1023 y  768-831    2 rows of 32 px flat colours (see SWATCHES)
  pilaster  x 768-831  y  832-1023   riveted iron post strip (0.28 x 1.1 units, stacked 4x up a post)

All values sit inside style.md section 4 scenery range (V 10-33 %, mostly 10-20 %; S 15-45 %),
hue from art-bible.md theme 1 (slate-teal H195); props use a dim copper/bronze near the top of the range.
"""
import colorsys
import sys

import numpy as np
from PIL import Image

S = 1024
rng = np.random.default_rng(1195)


def hexrgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32)


# Theme 1 scenery ramp (art-bible.md) and derived tones
R0, R1, R2, R3 = (hexrgb(c) for c in ("#141C1F", "#202A2E", "#323E42", "#324C54"))
MORTAR = hexrgb("#11171A")
SOOT = hexrgb("#0F1416")
IRON = hexrgb("#1A2124")
IRON_HI = hexrgb("#2C383C")
CLAY = hexrgb("#3D3028")   # V24 S35, dim warm grey-brown
BRONZE = hexrgb("#4A3828")  # V29 S46
PATINA = hexrgb("#24383A")
COPPER_DIM = hexrgb("#543A26")  # V33 S55 - props accent only, small areas

SWATCHES = [  # index -> colour; used by build_kit.py by name
    ("iron", IRON), ("iron_hi", IRON_HI), ("chain", hexrgb("#262E31")), ("soot", SOOT),
    ("mortar", MORTAR), ("wood", hexrgb("#2E2620")), ("copper", COPPER_DIM), ("rust", hexrgb("#3A2A22")),
    ("stone_dk", R1), ("stone", R2), ("teal", R3), ("deep", R0),
    ("clay", CLAY), ("bronze", BRONZE), ("patina", PATINA), ("water", hexrgb("#0E1A1E")),
]

img = np.zeros((S, S, 3), np.float32)


def noise(h, w, scale, amp):
    """Blocky value noise (nearest-upsampled) for a chunky low-poly look."""
    gh, gw = max(1, h // scale), max(1, w // scale)
    g = rng.normal(0, amp, (gh + 1, gw + 1)).astype(np.float32)
    n = np.kron(g, np.ones((scale, scale), np.float32))[:h, :w]
    return n[..., None]


def bricks(w, h, bw, bh, cracked=False):
    out = np.zeros((h, w, 3), np.float32)
    rows = h // bh
    for r in range(rows):
        off = (bw // 2) if r % 2 else 0
        y0 = r * bh
        for c in range(-1, w // bw + 1):
            x0 = c * bw + off
            t = rng.random()
            col = R1 + (R2 - R1) * (0.15 + 0.6 * t)
            if rng.random() < 0.12:
                col = R3 * 0.85
            if cracked and rng.random() < 0.18:
                col = SOOT * 1.1
            col = col * (1 + rng.normal(0, 0.04))
            for xx in range(x0, x0 + bw):
                out[y0:y0 + bh, xx % w] = col
    # mortar lines (3 px), horizontal + staggered vertical; wrap in x so tiles join
    for r in range(rows):
        y0 = r * bh
        out[y0:y0 + 3] = MORTAR
        off = (bw // 2) if r % 2 else 0
        for c in range(0, w // bw + 1):
            x = (c * bw + off) % w
            out[y0:y0 + bh, [x % w, (x + 1) % w, (x + 2) % w]] = MORTAR
    out += noise(h, w, 8, 1.6)
    # top-of-brick bevel highlight, bottom shadow: reads as chunky relief under flat light
    for r in range(rows):
        y0 = r * bh
        out[y0 + 3:y0 + 5] = np.where(out[y0 + 3:y0 + 5] > MORTAR + 1, out[y0 + 3:y0 + 5] * 1.18, out[y0 + 3:y0 + 5])
        out[y0 + bh - 3:y0 + bh] *= 0.82
    # soot gradient: darker toward the floor (v bottom of region = floor level)
    grad = np.linspace(1.0, 0.72, h, dtype=np.float32)[:, None, None]
    out *= grad
    if cracked:
        for _ in range(5):  # a few dark crack polylines
            x, y = rng.integers(0, w), rng.integers(0, h)
            for _ in range(rng.integers(40, 110)):
                out[max(0, y - 1):y + 2, max(0, x - 1):x + 2] = SOOT * 0.8
                y += rng.integers(1, 4)
                x = (x + rng.integers(-2, 3)) % w
                if y >= h:
                    break
        # soot streaks running down from the top
        for _ in range(6):
            x = rng.integers(0, w - 24)
            ln = rng.integers(h // 4, h // 2)
            out[:ln, x:x + rng.integers(10, 24)] *= 0.8
    return out


def stone_blocks(w, h, bw, bh):
    out = np.zeros((h, w, 3), np.float32)
    for r in range(h // bh):
        off = (bw // 2) if r % 2 else 0
        for c in range(-1, w // bw + 1):
            x0 = c * bw + off
            col = R2 + (R3 - R2) * rng.random() * 0.6 + rng.normal(0, 1.5)
            for xx in range(x0, x0 + bw):
                out[r * bh:(r + 1) * bh, xx % w] = col
        out[r * bh:r * bh + 4] = R0
        for c in range(0, w // bw + 1):
            x = (c * bw + off) % w
            out[r * bh:(r + 1) * bh, [(x + i) % w for i in range(4)]] = R0
    out += noise(h, w, 16, 2.0)
    return out


def flagstone(sz, cracked=False):
    out = np.zeros((sz, sz, 3), np.float32)
    # one slab per cell (the slab bevel is modelled), slight tone shift across the slab
    half = sz // 2
    for i in range(2):
        for j in range(2):
            col = R0 + (R1 - R0) * (0.78 + 0.12 * rng.random()) + rng.normal(0, 0.8)
            out[i * half:(i + 1) * half, j * half:(j + 1) * half] = col
    out += noise(sz, sz, 16, 1.8)
    j = 5
    out[:j] = out[-j:] = R0
    out[:, :j] = out[:, -j:] = R0
    if cracked:
        x, y = rng.integers(20, sz - 20), 0
        while y < sz:
            out[y:y + 2, max(0, x - 1):x + 2] = R0 * 0.9
            y += 1
            x = int(np.clip(x + rng.integers(-1, 2), 2, sz - 3))
    return out


def plate(sz, base, hi, rivets=True):
    out = np.zeros((sz, sz, 3), np.float32) + base
    out += noise(sz, sz, 8, 1.5)
    out[:6] = hi
    out[-6:] *= 0.75
    if rivets:
        for y in (24, sz - 28):
            for x in range(24, sz, 52):
                out[y - 5:y + 5, x - 5:x + 5] = hi
                out[y + 3:y + 6, x - 5:x + 5] *= 0.6
    return out


def rough(sz, base, var):
    out = np.zeros((sz, sz, 3), np.float32) + base
    out += noise(sz, sz, 32, var) + noise(sz, sz, 8, var * 0.5)
    return out


img[0:1024, 0:256] = bricks(256, 1024, 64, 32)
img[0:1024, 256:512] = bricks(256, 1024, 64, 32, cracked=True)
img[0:512, 512:768] = stone_blocks(256, 512, 128, 64)
img[512:768, 512:768] = flagstone(256)
img[768:1024, 512:768] = flagstone(256, cracked=True)
img[0:256, 768:1024] = plate(256, IRON, IRON_HI)
img[256:512, 768:1024] = rough(256, CLAY, 3.0)
br = rough(256, BRONZE, 2.5)
mask = (noise(256, 256, 32, 1.0)[..., 0] > 0.6)
br[mask] = br[mask] * 0.5 + PATINA * 0.5
img[512:768, 768:1024] = br
for i, (_, col) in enumerate(SWATCHES):
    y, x = 768 + (i // 8) * 32, 768 + (i % 8) * 32
    img[y:y + 32, x:x + 32] = col
# rest of the swatch grid: deep tone
img[832:1024, 768:1024] = R0


def pilaster(w, h):
    """Riveted cast-iron post face: lit edge strips, a seam, two rivet columns with a top-left highlight."""
    out = np.zeros((h, w, 3), np.float32) + IRON
    out += noise(h, w, 8, 1.2)
    out[:, :5] = IRON_HI
    out[:, -5:] *= 0.7
    out[:4] = IRON_HI * 0.9
    out[-4:] *= 0.7
    hi = hexrgb("#3E4D52")   # V32: brightest rivet tip, still inside the scenery range
    for y in range(24, h, 48):
        for x in (18, w - 18):
            out[y - 6:y + 6, x - 6:x + 6] = IRON_HI
            out[y - 6:y - 2, x - 6:x - 2] = hi
            out[y + 3:y + 7, x - 5:x + 7] *= 0.55
    return out


img[832:1024, 768:832] = pilaster(64, 192)

img = np.clip(img, 0, 255).astype(np.uint8)

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "textures/T1_Scenery_Atlas.png"
    Image.fromarray(img).save(out)
    # report value/saturation stats against style.md section 4
    hsv = np.array([colorsys.rgb_to_hsv(*(p / 255.0)) for p in img.reshape(-1, 3)[::97]])
    v, s = hsv[:, 2] * 100, hsv[:, 1] * 100
    print(f"wrote {out}  V p5/p50/p95 = {np.percentile(v, 5):.0f}/{np.percentile(v, 50):.0f}/{np.percentile(v, 95):.0f}%"
          f"  S p50/p95 = {np.percentile(s, 50):.0f}/{np.percentile(s, 95):.0f}%")
