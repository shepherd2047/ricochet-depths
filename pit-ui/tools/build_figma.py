"""Render layout sprites (x4) and emit Figma plugin scripts for wireframe + mockup pages.

usage: python3 build_figma.py <outdir>
writes <outdir>/img/<hash>.png, <outdir>/js/{wire,mock}_<i>.js, <outdir>/images.json
"""
import sys, os, json, hashlib
import numpy as np
from PIL import Image
from sprites import all_sprites, BORDERS, text_digits, canvas
import layout as L

S = all_sprites()
PT = 2          # Figma units per art px
UP = 4          # image px per art px

def render9(spr, w, h, border):
    l, b, r, t = border
    sh, sw = spr.shape[:2]
    def idx(n, size, lo, hi):
        mid = size - lo - hi
        out = []
        for i in range(n):
            if i < lo: out.append(i)
            elif i >= n - hi: out.append(size - (n - i))
            else: out.append(lo + (i - lo) % max(1, mid))
        return np.array(out)
    xs, ys = idx(w, sw, l, r), idx(h, sh, t, b)
    return spr[ys][:, xs]

def raster(e):
    k = e['k']
    if k == 'n9':
        return render9(S[e['spr']], e['w'], e['h'], BORDERS.get(e['spr'], (0, 0, 0, 0)))
    if k == 'tile':
        spr = S[e['spr']]
        reps = (e['h'] // spr.shape[0] + 1, e['w'] // spr.shape[1] + 1, 1)
        return np.tile(spr, reps)[:e['h'], :e['w']]
    if k == 'img':
        a = S[e['spr']]
        s = e['s']
        return np.repeat(np.repeat(a, s, 0), s, 1)
    if k == 'num':
        a = text_digits(e['txt'], e['font'], e['tint'] if e['tint'] is None else '#' + e['tint'].lstrip('#') if False else None)
        if e['tint']:
            from sprites import hx
            t = np.array(hx(e['tint'])[:3], float) / 255
            m = a[:, :, 3] > 0
            a = a.copy(); a[m, :3] = (a[m, :3] * t).astype(np.uint8)
        s = e['s']
        return np.repeat(np.repeat(a, s, 0), s, 1)
    return None

def place(e, a):
    """returns x, y (art px) honouring num anchors."""
    x, y = e['x'], e['y']
    if e['k'] == 'num':
        w = a.shape[1]
        if e['anchor'] == 'c': x -= w // 2
        elif e['anchor'] == 'r': x -= w
    return x, y

def hexrgb(h):
    h = h.lstrip('#')
    return '{r:%.4f,g:%.4f,b:%.4f}' % tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


INTERP = r"""
const P = %(page)s;
let page = figma.root.children.find(p => p.name === P);
if (!page) { page = figma.createPage(); page.name = P; }
await figma.setCurrentPageAsync(page);
await Promise.all([['Pixelify Sans','Regular'],['Pixelify Sans','Bold'],['Inter','Regular'],['Inter','Bold']].map(([family, style]) => figma.loadFontAsync({family, style})));
const INK = {r:0.043,g:0.059,b:0.067};
const hx = h => ({r:parseInt(h.slice(0,2),16)/255, g:parseInt(h.slice(2,4),16)/255, b:parseInt(h.slice(4,6),16)/255});
const out = [];
for (const S of SCREENS) {
  const [NAME, fx, fy, bg, D] = S;
  const old = page.children.find(n => n.name === NAME); if (old) old.remove();
  const fr = figma.createFrame(); fr.name = NAME; fr.resize(360, 640); fr.x = fx; fr.y = fy; fr.clipsContent = true;
  fr.fills = [{type:'SOLID', color: hx(bg)}];
  const images = [];
  const rect = (n, x, y, w, h, col, op, r) => { const o = figma.createRectangle(); o.name = n; o.x = x; o.y = y; o.resize(Math.max(w,1), Math.max(h,1));
    o.fills = col ? [{type:'SOLID', color: hx(col), opacity: op ?? 1}] : []; if (r) o.cornerRadius = r; fr.appendChild(o); return o; };
  const txt = (n, s, x, y, w, h, size, fam, st, col, ha, va, sh, lh) => { const t = figma.createText(); t.name = n; fr.appendChild(t);
    t.fontName = {family: fam, style: st}; t.characters = s; t.fontSize = size; t.fills = [{type:'SOLID', color: hx(col)}];
    t.textAutoResize = 'NONE'; t.resize(w, h); t.x = x; t.y = y; t.textAlignHorizontal = ha; t.textAlignVertical = va;
    if (lh) t.lineHeight = {unit:'PERCENT', value: lh};
    if (sh) t.effects = [{type:'DROP_SHADOW', color:{...INK, a:1}, offset:{x:0,y:2}, radius:0, spread:0, visible:true, blendMode:'NORMAL'}]; return t; };
  const DD = [];
  for (const d of D) { if (d[0] === 'M') DD.push(...MACROS[d[1]]); else DD.push(d); }
  for (const d of DD) {
    const k = d[0];
    if (k === 'r') rect(d[1], d[2], d[3], d[4], d[5], d[6], d[7], d[8]);
    else if (k === 'i') { const o = rect(d[1], d[2], d[3], d[4], d[5], 'FF00FF', 0); images.push([d[1].split('|')[1], o.id]); }
    else if (k === 'w' || k === 'wi') { const o = rect(d[1], d[2], d[3], d[4], d[5], k === 'w' ? 'D1D1D1' : '8C8C8C');
      if (k === 'w') { o.strokes = [{type:'SOLID', color: hx('737373')}]; o.strokeWeight = 1; }
      if (d[6]) txt(d[1] + '/wl', d[6], d[2] + 3, d[3] + 2, Math.max(d[4] - 6, 10), 10, 7, 'Inter', 'Regular', '666666', 'LEFT', 'TOP', false, null); }
    else if (k === 'e') { const o = figma.createEllipse(); o.name = d[1]; o.x = d[2]; o.y = d[3]; o.resize(d[4], d[5]);
      o.fills = [{type:'SOLID', color: hx(d[6]), opacity: d[8] ?? 1}];
      if (d[7] && (d[8] ?? 1) < 1) { o.fills = [{type:'SOLID', color: hx(d[6]), opacity: 0.22}]; o.effects = [{type:'LAYER_BLUR', radius:48, visible:true}]; }
      else if (d[7]) o.effects = [{type:'DROP_SHADOW', color:{...hx(d[7]), a:0.9}, offset:{x:0,y:0}, radius:10, spread:2, visible:true, blendMode:'NORMAL'}];
      fr.appendChild(o); }
    else if (k === 't') txt(d[1], d[2], d[3], d[4], d[5], d[6], d[7], d[8], d[9], d[10], d[11], d[12], d[13], d[14]);
    else if (k === 'z') { const o = rect(d[1], d[2], d[3], d[4], d[5], 'FF334D', 0.08); o.strokes = [{type:'SOLID', color: hx('FF4059')}]; o.dashPattern = [4,3];
      const t = figma.createText(); t.name = d[1] + '/label'; fr.appendChild(t); t.fontName = {family:'Inter', style:'Regular'}; t.characters = d[6]; t.fontSize = 7;
      t.fills = [{type:'SOLID', color: hx('FF5973')}]; t.x = d[2] + 3; t.y = d[3] + 2; }
  }
  out.push({ frame: fr.id, name: NAME, images });
}
return out;
"""

def H(h):
    return h.lstrip('#').upper()

def data_for(screen_i, title, elems, mode, images):
    fx = (screen_i % 5) * 420
    fy = (screen_i // 5) * 760
    D = []
    if mode == 'wire':
        D.append(['r', 'scene (3D, not UI)', 0, 0, 360, 640, 'EDEDED', 1, 0])
    for e in elems:
        k, n = e['k'], e['name']
        if k in ('n9', 'img', 'tile', 'num'):
            a = raster(e)
            x, y = place(e, a)
            h, w = a.shape[:2]
            if mode == 'mock':
                big = np.repeat(np.repeat(a, UP, 0), UP, 1)
                hsh = hashlib.sha1(big.tobytes() + bytes(str(big.shape), 'ascii')).hexdigest()[:10]
                if hsh not in images:
                    Image.fromarray(big, 'RGBA').save(os.path.join(OUT, 'img', hsh + '.png'))
                    images[hsh] = 1
                D.append(['i', f"img|{hsh}|{n}", x * PT, y * PT, w * PT, h * PT])
            else:
                if k in ('img', 'num') and n.count('/') >= 2 and ('pip' in n or 'icon' in n):
                    D.append(['wi', n, x * PT, y * PT, w * PT, h * PT, None]); continue
                label = None
                if k == 'num':
                    D.append(['w', n, x * PT, y * PT, w * PT, h * PT, None])
                    D.append(['t', n + '/t', e['txt'], x * PT, y * PT, w * PT, h * PT, 9, 'Inter', 'Bold', '333333', 'CENTER', 'CENTER', False, None]); continue
                if w * PT >= 60 and h * PT >= 20 and not n.endswith(('/frame', '/fill', '/lag')):
                    label = n.split('/')[-1]
                D.append(['w', n, x * PT, y * PT, w * PT, h * PT, label])
        elif k == 'txt':
            fam, style = ('Pixelify Sans', 'Bold' if e['wt'] == 'Bold' else 'Regular') if mode == 'mock' else ('Inter', 'Bold' if e['wt'] == 'Bold' else 'Regular')
            col = H(e['col']) if mode == 'mock' else '262626'
            size = e['pt'] if mode == 'mock' else max(7, e['pt'] - 2)
            lh = 115 if e['va'] == 'TOP' else None
            D.append(['t', n, e['txt'], e['x'] * PT, e['y'] * PT, e['w'] * PT, e['h'] * PT, size, fam, style, col, e['ha'], e['va'], bool(mode == 'mock' and e['shadow']), lh])
        elif k == 'rect':
            if mode == 'wire':
                if n == 'dim': D.append(['r', n, 0, 0, 360, 640, '999999', 0.35, 0])
                continue
            D.append(['r', n, e['x'] * PT, e['y'] * PT, e['w'] * PT, e['h'] * PT, H(e['col']), e['op'], e['r'] * PT])
        elif k == 'ell':
            if mode == 'wire': continue
            D.append(['e', n, e['x'] * PT, e['y'] * PT, e['w'] * PT, e['h'] * PT, H(e['col']), H(e['glow']) if e.get('glow') else None, e.get('op', 1)])
        elif k == 'zone':
            D.append(['z', n, e['x'] * PT, e['y'] * PT, e['w'] * PT, e['h'] * PT, e['label']])
    return [title, fx, fy, '262937' if mode == 'mock' else 'FFFFFF', D]

MACROS = {}
def factor(D):
    """collapse runs of scene/* and safe/* + mock/* entries into named macros"""
    out, i = [], 0
    while i < len(D):
        pre = None
        for p in ('scene/', 'mock/capsule', 'safe/'):
            if D[i][1].startswith(p): pre = p
        if pre is None:
            out.append(D[i]); i += 1; continue
        j = i
        grp = ('scene/',) if pre == 'scene/' else ('mock/capsule', 'safe/')
        while j < len(D) and D[j][1].startswith(grp): j += 1
        chunk = D[i:j]
        key = hashlib.sha1(json.dumps(chunk).encode()).hexdigest()[:6]
        MACROS[key] = chunk
        out.append(['M', key]); i = j
    return out

if __name__ == '__main__':
    OUT = sys.argv[1]
    os.makedirs(os.path.join(OUT, 'img'), exist_ok=True)
    os.makedirs(os.path.join(OUT, 'js'), exist_ok=True)
    images = {}
    for mode in ('wire', 'mock'):
        batch, n = [], 0
        def flush():
            global n
            if not batch: return
            used = sorted({x[1] for b in batch for x in b[4] if x[0] == 'M'})
            js = 'const MACROS = ' + json.dumps({u: MACROS[u] for u in used}, separators=(',', ':')) + ';\n'
            js += 'const SCREENS = ' + json.dumps(batch, separators=(',', ':')) + ';\n' + INTERP % dict(page=json.dumps('Mockup' if mode == 'mock' else 'Wireframes'))
            open(os.path.join(OUT, 'js', f'{mode}_{n}.js'), 'w').write(js)
            print(mode, n, len(js), [b[0] for b in batch])
            n += 1; batch.clear()
        size = 0
        for i, (title, fn) in enumerate(L.SCREENS):
            base = fn()
            if mode == 'mock':
                el = base + L.capsule_mock() + L.safe_zones()
            else:
                el = [e for e in base if not e['name'].startswith(('scene/', 'mock/', 'title/glow', 'pick/preview/pedestal'))] + L.safe_zones()
            d = data_for(i, title, el, mode, images)
            d[4] = factor(d[4])
            s = len(json.dumps(d, separators=(',', ':')))
            if size + s > 21000:
                flush(); size = 0
            batch.append(d); size += s
        flush()
    print('unique images', len(images))
