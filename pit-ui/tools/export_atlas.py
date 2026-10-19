"""Export every UI sprite as a native-resolution PNG and pack them into one 2048x1024 atlas.

    python3 export_atlas.py ../assets

Writes:
  assets/sprites/<name>.png   one file per sprite, 1 art px = 1 texel
  assets/atlas/ui_atlas.png   2048x1024 RGBA, point-filter, 1px edge extrusion, 2px gutter
  assets/atlas/ui_atlas.json  rects (top-left origin and Unity bottom-left origin), 9-slice borders, glyph advances
  assets/atlas/ui_atlas_preview.png  the used region at x4 for review
"""
import json, os, sys
import numpy as np
from PIL import Image
import sprites as SP

ATLAS_W, ATLAS_H = 2048, 1024
EXTRUDE, GUTTER = 1, 2          # extrude edge texels 1px, then leave 2px empty

def group(name):
    for pre, g in (('font_', 'font'), ('ic_', 'icon'), ('ball_', 'ball'), ('item_', 'item'), ('hero_', 'hero')):
        if name.startswith(pre):
            return g
    return 'frame'

def glyph_advance(name, w):
    font = name.split('_')[1]
    return w + (1 if font == 'hud' else -1)

def main(out):
    S = SP.all_sprites()
    sdir, adir = os.path.join(out, 'sprites'), os.path.join(out, 'atlas')
    os.makedirs(sdir, exist_ok=True); os.makedirs(adir, exist_ok=True)
    for n, a in S.items():
        Image.fromarray(a, 'RGBA').save(os.path.join(sdir, n + '.png'))

    # shelf pack, tallest first, grouped so related sprites sit together
    order = sorted(S, key=lambda n: (group(n) != 'frame', -S[n].shape[0], n))
    pad = EXTRUDE + GUTTER
    atlas = np.zeros((ATLAS_H, ATLAS_W, 4), np.uint8)
    x = y = shelf_h = 0
    meta = {}
    for n in order:
        a = S[n]; h, w = a.shape[:2]
        cw, ch = w + 2 * pad, h + 2 * pad
        if x + cw > ATLAS_W:
            x, y, shelf_h = 0, y + shelf_h, 0
        if y + ch > ATLAS_H:
            raise SystemExit('atlas full')
        ox, oy = x + pad, y + pad
        e = np.pad(a, ((EXTRUDE, EXTRUDE), (EXTRUDE, EXTRUDE), (0, 0)), mode='edge')
        atlas[oy - EXTRUDE:oy + h + EXTRUDE, ox - EXTRUDE:ox + w + EXTRUDE] = e
        m = {'x': ox, 'y': oy, 'w': w, 'h': h, 'unity_y': ATLAS_H - oy - h, 'group': group(n)}
        if n in SP.BORDERS:
            m['border'] = list(SP.BORDERS[n])          # L, B, R, T
        if m['group'] == 'font':
            m['advance'] = glyph_advance(n, w)
        meta[n] = m
        x += cw; shelf_h = max(shelf_h, ch)
    used_h = y + shelf_h
    Image.fromarray(atlas, 'RGBA').save(os.path.join(adir, 'ui_atlas.png'), optimize=True)
    prev = Image.fromarray(atlas[:used_h, :max(v['x'] + v['w'] + pad for v in meta.values())], 'RGBA')
    prev.resize((prev.width * 4, prev.height * 4), Image.NEAREST).save(os.path.join(adir, 'ui_atlas_preview.png'))
    doc = {'image': 'ui_atlas.png', 'size': [ATLAS_W, ATLAS_H], 'pixelsPerUnit': 16, 'filter': 'point',
           'extrude': EXTRUDE, 'gutter': GUTTER, 'border_order': 'L,B,R,T', 'used_height': used_h,
           'sprites': meta,
           # same data as a list for Unity's JsonUtility (no dictionaries there)
           'list': [{'name': k, 'x': v['x'], 'y': v['unity_y'], 'w': v['w'], 'h': v['h'],
                     'border': v.get('border', [0, 0, 0, 0]), 'advance': v.get('advance', 0)} for k, v in meta.items()]}
    json.dump(doc, open(os.path.join(adir, 'ui_atlas.json'), 'w'), indent=1)
    print(len(meta), 'sprites; used', used_h, 'px of', ATLAS_H)

if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else '../assets')
