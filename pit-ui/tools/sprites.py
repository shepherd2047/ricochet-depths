"""Procedural pixel-art sprites for the Pit UI (all original, drawn in code).

Every sprite is a numpy uint8 RGBA array at native resolution (1 art pixel = 1 px).
9-slice sprites carry their border in BORDERS[name] = (left, bottom, right, top)
(Unity's Sprite Editor order is L, B, R, T).
"""
import numpy as np

def hx(s, a=255):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)

# ---------------------------------------------------------------- palette
P = {
    'ink': hx('0B0F11'), 'clear': (0, 0, 0, 0),
    # slate (theme 1 scenery hue, lifted for UI)
    's0': hx('141C1F'), 's1': hx('202A2E'), 's2': hx('323E42'), 's3': hx('3F555C'),
    's4': hx('5E7F89'), 's5': hx('8FB0B8'),
    # furnace copper (theme 1 accent)
    'c0': hx('4A2410'), 'c1': hx('7A3E1C'), 'c2': hx('A35E2A'), 'c3': hx('D98841'), 'c4': hx('F2B872'),
    # brass / amber (enemy accent family)
    'b0': hx('5C4A2E'), 'b1': hx('8A7046'), 'b2': hx('B29562'), 'b3': hx('D1BC7D'), 'b4': hx('F2E6B6'),
    'cream': hx('F2EEE4'),
    'r0': hx('3D0E14'), 'r1': hx('7A1E2A'), 'r2': hx('C8384A'), 'r3': hx('FF7A7A'),
    'x0': hx('0E2A33'), 'x1': hx('1E5A6E'), 'x2': hx('47AAB2'), 'x3': hx('9FF2EE'),
    'g0': hx('5C3F0E'), 'g1': hx('A6781E'), 'g2': hx('E6CA5C'), 'g3': hx('FFF2A6'),
    'n0': hx('163310'), 'n1': hx('3D7A22'), 'n2': hx('5DA629'), 'n3': hx('B8F27A'),
    'd1': hx('3A4044'), 'd2': hx('565D61'), 'd3': hx('7C8488'),
    'white': hx('FFFFFF'),
}

def canvas(w, h, col='clear'):
    a = np.zeros((h, w, 4), np.uint8)
    a[:] = P[col] if isinstance(col, str) else col
    return a

def C(c):
    return P[c] if isinstance(c, str) else c

def fill(a, x, y, w, h, c):
    if w > 0 and h > 0:
        a[max(y, 0):y + h, max(x, 0):x + w] = C(c)

def px(a, x, y, c):
    if 0 <= y < a.shape[0] and 0 <= x < a.shape[1]:
        a[y, x] = C(c)

def from_map(rows, key):
    h, w = len(rows), max(len(r) for r in rows)
    a = canvas(w, h)
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch in key:
                a[y, x] = C(key[ch])
    return a

def blit(dst, src, x, y):
    h, w = src.shape[:2]
    m = src[:, :, 3] > 0
    region = dst[y:y + h, x:x + w]
    region[m[:region.shape[0], :region.shape[1]]] = src[:region.shape[0], :region.shape[1]][m[:region.shape[0], :region.shape[1]]]

def mask_outline(mask, fillc, outc='ink', shadow=True, pad=1):
    """1-bit mask -> sprite with 1px outline (8-neighbour) and optional 1px drop shadow."""
    m = np.pad(np.array(mask, bool), pad + (1 if shadow else 0))
    h, w = m.shape
    a = canvas(w, h)
    dil = m.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            dil |= np.roll(np.roll(m, dy, 0), dx, 1)
    if shadow:
        sh = np.roll(dil, 1, 0) & ~dil
        a[sh] = C(outc)
    a[dil] = C(outc)
    a[m] = C(fillc)
    return a

def parse_mask(rows):
    return np.array([[ch != '.' and ch != ' ' for ch in r.ljust(max(map(len, rows)))] for r in rows], bool)

# ---------------------------------------------------------------- 9-slice frames
BORDERS = {}

def bevel_box(w, h, out, hi, body, lo, lip=1, cut=True, inner_hi=None):
    a = canvas(w, h)
    fill(a, 0, 0, w, h, out)
    fill(a, 1, 1, w - 2, h - 2, lo)
    fill(a, 1, 1, w - 2, h - 2 - lip, body)
    fill(a, 1, 1, w - 2, 1, hi)          # top highlight
    fill(a, 1, 1, 1, h - 2 - lip, hi if inner_hi is None else inner_hi)  # left highlight
    if cut:
        for (x, y) in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
            a[y, x] = P['clear']
        for (x, y) in ((1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2)):
            a[y, x] = C(out)
    return a

def make_frames():
    S = {}
    # Main panel: slate body, brass rivets, 2px rim. 24x24, border 7.
    a = canvas(24, 24)
    fill(a, 0, 0, 24, 24, 'ink')
    fill(a, 1, 1, 22, 22, 's3')
    fill(a, 2, 2, 20, 20, 's1')
    fill(a, 2, 2, 20, 1, 's4')       # inner top light
    fill(a, 2, 21, 20, 1, 's0')      # inner bottom shade
    fill(a, 1, 22, 22, 1, 's2')
    for (x, y) in ((0, 0), (23, 0), (0, 23), (23, 23)):
        a[y, x] = P['clear']
    for (x, y) in ((3, 3), (19, 3), (3, 19), (19, 19)):   # rivets 2x2
        fill(a, x, y, 2, 2, 'b2'); px(a, x, y, 'b4'); px(a, x + 1, y + 1, 'b0')
    S['panel'] = a; BORDERS['panel'] = (7, 7, 7, 7)

    # Inset (sunken) panel for counters / stat rows. 12x12 border 3.
    a = canvas(12, 12)
    fill(a, 0, 0, 12, 12, 'ink')
    fill(a, 1, 1, 10, 10, 's0')
    fill(a, 1, 1, 10, 1, 'ink')
    fill(a, 1, 10, 10, 1, 's2')
    for (x, y) in ((0, 0), (11, 0), (0, 11), (11, 11)):
        a[y, x] = P['clear']
    S['inset'] = a; BORDERS['inset'] = (3, 3, 3, 3)

    # Buttons 24x24: border L6 B8 R6 T6. lip = 3px darker band (3D thickness).
    def button(hi, body, lo, lip=3):
        b = bevel_box(24, 24, 'ink', hi, body, lo, lip=lip)
        return b
    S['btn_primary'] = button('c4', 'c3', 'c1')
    S['btn_secondary'] = button('s5', 's4', 's2')
    S['btn_disabled'] = button('d3', 'd2', 'd1')
    S['btn_danger'] = button('r3', 'r2', 'r1')
    # pressed = same face, shifted down 2 px, lip 1
    for k, (hi, body, lo) in {'btn_primary': ('c4', 'c3', 'c1'), 'btn_secondary': ('s5', 's4', 's2')}.items():
        p = canvas(24, 24)
        face = bevel_box(24, 22, 'ink', hi, body, lo, lip=1)
        blit(p, face, 0, 2)
        S[k + '_pressed'] = p
    for k in ('btn_primary', 'btn_secondary', 'btn_disabled', 'btn_danger', 'btn_primary_pressed', 'btn_secondary_pressed'):
        BORDERS[k] = (6, 8, 6, 6)

    # Card frame: dark body, brass double rim, corner studs. 32x32 border 8.
    def card(r_out, r_hi, r_lo, body):
        a = canvas(32, 32)
        fill(a, 0, 0, 32, 32, 'ink')
        fill(a, 1, 1, 30, 30, r_lo)
        fill(a, 1, 1, 30, 1, r_hi); fill(a, 1, 1, 1, 30, r_hi)
        fill(a, 2, 2, 28, 28, r_out)
        fill(a, 3, 3, 26, 26, 'ink')
        fill(a, 4, 4, 24, 24, body)
        fill(a, 4, 4, 24, 1, 's2')
        for (x, y) in ((0, 0), (31, 0), (0, 31), (31, 31)):
            a[y, x] = P['clear']
        for (x, y) in ((1, 1), (28, 1), (1, 28), (28, 28)):   # 3x3 corner plates
            fill(a, x, y, 3, 3, r_hi); px(a, x + 1, y + 1, 'b4' if r_hi != 'd3' else 'd3'); px(a, x + 2, y + 2, r_lo)
        return a
    S['card'] = card('b2', 'b3', 'b0', 's1')
    S['card_copper'] = card('c3', 'c4', 'c1', 's1')
    S['card_disabled'] = card('d2', 'd3', 'd1', 's0')
    for k in ('card', 'card_copper', 'card_disabled'):
        BORDERS[k] = (8, 8, 8, 8)

    # Selection outline (drawn over a card/slot), 2px cream + ink. 12x12 border 4.
    a = canvas(12, 12)
    fill(a, 0, 0, 12, 12, 'ink'); fill(a, 1, 1, 10, 10, 'b4'); fill(a, 3, 3, 6, 6, 'ink'); fill(a, 4, 4, 4, 4, 'clear')
    for (x, y) in ((0, 0), (11, 0), (0, 11), (11, 11)):
        a[y, x] = P['clear']
    S['select'] = a; BORDERS['select'] = (4, 4, 4, 4)

    # Icon slots (9-slice so they scale with integer icon scale). 12x12 border 4.
    def slot(rim, rim_hi, inner):
        a = canvas(12, 12)
        fill(a, 0, 0, 12, 12, 'ink')
        fill(a, 1, 1, 10, 10, rim)
        fill(a, 1, 1, 10, 1, rim_hi); fill(a, 1, 1, 1, 10, rim_hi)
        fill(a, 2, 2, 8, 8, 'ink')
        fill(a, 3, 3, 6, 6, inner)
        for (x, y) in ((0, 0), (11, 0), (0, 11), (11, 11)):
            a[y, x] = P['clear']
        return a
    S['slot_ball'] = slot('c2', 'c3', 's0')
    S['slot_item'] = slot('s3', 's4', 's0')
    S['slot_empty'] = slot('s2', 's2', 's0')
    S['slot_fused'] = slot('b2', 'b4', 's0')
    for k in ('slot_ball', 'slot_item', 'slot_empty', 'slot_fused'):
        BORDERS[k] = (3, 3, 3, 3)

    # Small tag (NEW / MAX), 8x8 border 3, tinted by colour variants.
    def tag(hi, body, lo):
        a = bevel_box(8, 8, 'ink', hi, body, lo, lip=1)
        return a
    S['tag_green'] = tag('n3', 'n2', 'n1'); S['tag_gold'] = tag('g3', 'g2', 'g1'); S['tag_red'] = tag('r3', 'r2', 'r1')
    for k in ('tag_green', 'tag_gold', 'tag_red'):
        BORDERS[k] = (3, 3, 3, 3)

    # Bars: frame 12x10 border 3 (L,B,R,T = 3,3,3,3); fills 4x4 border 1 top/bottom.
    a = canvas(12, 10)
    fill(a, 0, 0, 12, 10, 'ink'); fill(a, 1, 1, 10, 8, 's2'); fill(a, 2, 2, 8, 6, 's0')
    for (x, y) in ((0, 0), (11, 0), (0, 9), (11, 9)):
        a[y, x] = P['clear']
    S['bar_frame'] = a; BORDERS['bar_frame'] = (3, 3, 3, 3)
    def barfill(hi, body, lo):
        a = canvas(4, 4); fill(a, 0, 0, 4, 4, body); fill(a, 0, 0, 4, 1, hi); fill(a, 0, 3, 4, 1, lo); return a
    S['bar_hp'] = barfill('r3', 'r2', 'r1')
    S['bar_hp_lag'] = barfill('cream', 'b4', 'b3')
    S['bar_xp'] = barfill('x3', 'x2', 'x1')
    S['bar_boss'] = barfill('c4', 'c3', 'c1')
    for k in ('bar_hp', 'bar_hp_lag', 'bar_xp', 'bar_boss'):
        BORDERS[k] = (1, 1, 1, 1)
    # Bar tick (every 25 % of HP), 1x4
    a = canvas(1, 4, 'ink'); S['bar_tick'] = a

    # Level pips 5x5
    a = canvas(5, 5); fill(a, 0, 0, 5, 5, 'ink'); fill(a, 1, 1, 3, 3, 'b3'); px(a, 1, 1, 'b4'); px(a, 3, 3, 'b1')
    for (x, y) in ((0, 0), (4, 0), (0, 4), (4, 4)):
        a[y, x] = P['clear']
    S['pip_on'] = a
    a = canvas(5, 5); fill(a, 0, 0, 5, 5, 'ink'); fill(a, 1, 1, 3, 3, 's2'); px(a, 2, 2, 's0')
    for (x, y) in ((0, 0), (4, 0), (0, 4), (4, 4)):
        a[y, x] = P['clear']
    S['pip_off'] = a
    a = canvas(5, 5); fill(a, 0, 0, 5, 5, 'ink'); fill(a, 1, 1, 3, 3, 'n2'); px(a, 1, 1, 'n3'); px(a, 3, 3, 'n1')
    for (x, y) in ((0, 0), (4, 0), (0, 4), (4, 4)):
        a[y, x] = P['clear']
    S['pip_next'] = a   # the pip this pick would fill

    # Level badge 18x18 (octagon, brass rim, dark centre for the digits)
    rows = [
        '.....########.....',
        '....#bbbbbbbb#....',
        '...#bBBBBBBBBb#...',
        '..#bB########Bb#..',
        '.#bB#dddddddd#Bb#.',
        '#bB#dddddddddd#Bb#',
        '#bB#dddddddddd#Bb#',
        '#bB#dddddddddd#Bb#',
        '#bB#dddddddddd#Bb#',
        '#bB#dddddddddd#Bb#',
        '#bB#dddddddddd#Bb#',
        '#bB#dddddddddd#Bb#',
        '#bB#dddddddddd#Bb#',
        '.#bB#dddddddd#Bo#.',
        '..#bB########Bo#..',
        '...#oooooooooo#...',
        '....#oooooooo#....',
        '.....########.....',
    ]
    S['badge_level'] = from_map(rows, {'#': 'ink', 'b': 'b4', 'B': 'b3', 'd': 's0', 'o': 'b1'})

    # Boss banner: stripe tile 16x8 (tiled horizontally) and body 9-slice
    a = canvas(16, 8)
    for y in range(8):
        for x in range(16):
            a[y, x] = P['r2'] if ((x + y) // 4) % 2 == 0 else P['ink']
    S['banner_stripe'] = a
    a = canvas(12, 12)
    fill(a, 0, 0, 12, 12, 'ink'); fill(a, 0, 1, 12, 1, 'r1'); fill(a, 0, 10, 12, 1, 'r1'); fill(a, 0, 2, 12, 8, hx('120A0C'))
    S['banner_body'] = a; BORDERS['banner_body'] = (0, 3, 0, 3)

    # Speed / round chip background is just btn_secondary. Header ribbon 9-slice 24x16 border 8,4
    a = canvas(24, 16)
    fill(a, 0, 0, 24, 16, 'ink')
    fill(a, 1, 1, 22, 14, 'c1'); fill(a, 1, 1, 22, 11, 'c3'); fill(a, 1, 1, 22, 1, 'c4')
    fill(a, 3, 3, 18, 1, 'c2'); fill(a, 3, 10, 18, 1, 'c2')
    for (x, y) in ((0, 0), (23, 0), (0, 15), (23, 15)):
        a[y, x] = P['clear']
    # notched ends
    for y in range(4, 12):
        pass
    S['ribbon'] = a; BORDERS['ribbon'] = (6, 5, 6, 5)

    # 1x1 white for dims / generic tint, 4x4 to survive atlas bleeding
    S['white'] = canvas(4, 4, 'white')
    return S

# ---------------------------------------------------------------- UI glyph icons (mask + outline)
ICON_MASKS = {
    'pause': ['..........', '.###..###.', '.###..###.', '.###..###.', '.###..###.', '.###..###.', '.###..###.', '.###..###.', '.###..###.', '..........'],
    'play': ['.#........', '.##.......', '.###......', '.####.....', '.#####....', '.#####....', '.####.....', '.###......', '.##.......', '.#........'],
    'restart': ['...####...', '..#....#..', '.#......#.', '.#.......', '.#.....###', '.#......#.', '.#........', '..#....#..', '...####...', '..........'],
    'home': ['....##....', '...####...', '..######..', '.########.', '..######..', '..##..##..', '..##..##..', '..##..##..', '..######..', '..........'],
    'reroll': ['..#####...', '.#.....#.#', '#.......##', '#......###', '..........', '..........', '###......#', '##.......#', '#.#.....#.', '...#####..'],
    'back': ['....#.....', '...##.....', '..########', '.#########', '..########', '...##.....', '....#.....', '..........'],
    'lock': ['...####...', '..#....#..', '..#....#..', '.########.', '.###..###.', '.###..###.', '.####.###.', '.########.', '..........'],
    'check': ['.........#', '........##', '.......##.', '#.....##..', '##...##...', '.##.##....', '..###.....', '...#......'],
    'plus': ['...##...', '...##...', '########', '########', '...##...', '...##...'],
    'arrow': ['....#...', '....##..', '#######.', '########', '#######.', '....##..', '....#...'],
    'skull': ['..######..', '.########.', '##########', '##..##..##', '##..##..##', '##########', '.###..###.', '..######..', '..#.##.#..'],
    'clock': ['..######..', '.#......#.', '#....#...#', '#....#...#', '#....###.#', '#........#', '#........#', '.#......#.', '..######..'],
    'sword': ['........##', '.......###', '......###.', '.....###..', '#...###...', '##.###....', '.####.....', '..##......', '.####.....', '##..#.....'],
    'star': ['....#....', '....#....', '...###...', '#########', '.#######.', '..#####..', '..##.##..', '.##...##.', '##.....##'],
    'speed': ['#...#.....', '##..##....', '###.###...', '####.###..', '###.###...', '##..##....', '#...#.....'],
    'sound': ['....#.....', '...##..#..', '####...#.#', '####.#.#.#', '####.#.#.#', '####...#.#', '...##..#..', '....#.....'],
    'mute': ['....#.....', '...##.....', '####..#..#', '####...##.', '####...##.', '####..#..#', '...##.....', '....#.....'],
    'music': ['...######', '...######', '...#....#', '...#....#', '...#....#', '.###..###', '####.####', '.##...##.'],
    'close': ['##....##', '###..###', '.######.', '..####..', '..####..', '.######.', '###..###', '##....##'],
    'gem': ['..####..', '.#.##.#.', '########', '.######.', '..####..', '...##...'],
}

def icon(name, color='cream', size=16):
    m = parse_mask(ICON_MASKS[name])
    spr = mask_outline(m, color)
    a = canvas(size, size)
    h, w = spr.shape[:2]
    blit(a, spr, (size - w) // 2, (size - h) // 2)
    return a

def coin(size=9):
    if size == 9:
        rows = ['..#####..', '.#GGGGG#.', '#GgWGGGG#', '#GWgggGG#', '#GgggggG#', '#GggggGG#', '#GGgggGg#', '.#gGGGg#.', '..#####..']
    else:
        rows = ['....######......', '..##GGGGGG##....', '.#GGWWGGGGGG#...', '.#GWWGGGGGGGg#..', '#GGWGgggggGGGg#.', '#GGGgGGGGGgGGg#.',
                '#GGGgGGGGGgGGg#.', '#GGGgGGGGGgGGg#.', '#GGGgGGGGGgGGg#.', '#GGGGgggggGGgg#.', '.#GGGGGGGGGGg#..', '.#gGGGGGGGGgg#..',
                '..##ggggggg##...', '....######......', '................', '................']
    key = {'#': 'ink', 'G': 'g2', 'g': 'g1', 'W': 'g3'}
    a = from_map(rows, key)
    return a

def heart(size=9):
    rows = ['.##...##.', '#RR#.#RR#', '#RWRRRRR#', '#RRRRRRr#', '.#RRRRr#.', '..#RRr#..', '...#r#...', '....#....', '.........']
    return from_map(rows, {'#': 'ink', 'R': 'r2', 'W': 'r3', 'r': 'r1'})

# ---------------------------------------------------------------- 16x16 ball sprites
BALLS = {  # key: (dark, mid, bright) from art-bible §6
    'ember': ('380E06', 'BF581D', 'FFE699'), 'rime': ('0A2533', '47AAB2', 'D9FFF9'),
    'spark': ('221638', '708FCC', 'FFFBCC'), 'venom': ('0D2608', '5DA629', 'E9FFA6'),
    'anvil': ('15181F', '77828C', 'F2DEB6'), 'thorn': ('2E070D', 'A62949', 'FFCCB2'),
    'wisp': ('1F3333', '8FBFAF', 'EBFFF1'), 'ray': ('403813', 'E6CA5C', 'FFFDE6'),
    'cluster': ('2E122E', 'B2599C', 'FFD9EC'), 'lodestone': ('0F0F33', '8659B2', 'D9F2FF'),
    'gale': ('172E28', '6BB28F', 'ECFFD9'), 'sap': ('29081E', 'A63232', 'FFDFBF'),
}

def ramp(dark, mid, bright, n=5):
    d, m, b = (np.array(hx(c)[:3], float) for c in (dark, mid, bright))
    out = []
    for i in range(n):
        t = i / (n - 1)
        c = d + (m - d) * (t * 2) if t < .5 else m + (b - m) * ((t - .5) * 2)
        out.append(tuple(int(round(v)) for v in c) + (255,))
    return out

def shade_shape(inside, r, lightdir=(-0.6, -0.8)):
    """inside: bool HxW; returns sprite shaded with ramp r (5 stops), outlined with r[0]."""
    h, w = inside.shape
    a = canvas(w, h)
    ys, xs = np.nonzero(inside)
    cy, cx = ys.mean(), xs.mean()
    rad = max(1.0, np.sqrt(((ys - cy) ** 2 + (xs - cx) ** 2).max()))
    edge = inside & ~(np.roll(inside, 1, 0) & np.roll(inside, -1, 0) & np.roll(inside, 1, 1) & np.roll(inside, -1, 1))
    for y, x in zip(ys, xs):
        dx, dy = (x - cx) / rad, (y - cy) / rad
        l = -(dx * lightdir[0] + dy * lightdir[1])          # -1..1
        idx = int(np.clip(round(1.6 + l * 1.6), 1, 3))
        a[y, x] = r[idx]
    a[edge] = r[0]
    return a, (cx, cy, rad)

def disc(w, h, cx, cy, rx, ry=None):
    ry = rx if ry is None else ry
    yy, xx = np.mgrid[0:h, 0:w]
    return ((xx + .5 - cx) / rx) ** 2 + ((yy + .5 - cy) / ry) ** 2 <= 1.0

def ball(key):
    r = ramp(*BALLS[key])
    W = 16
    if key == 'rime':        # faceted octagon
        yy, xx = np.mgrid[0:W, 0:W]
        ins = (abs(xx - 7.5) + abs(yy - 7.5) <= 9.5) & (abs(xx - 7.5) <= 6.5) & (abs(yy - 7.5) <= 6.5)
    elif key == 'anvil':
        ins = disc(W, W, 8, 8.5, 7, 5.5)
    elif key in ('venom', 'sap'):
        ins = disc(W, W, 8, 9.5, 5.5) | (disc(W, W, 8, 5, 2.5) & (np.mgrid[0:W, 0:W][0] < 9))
        yy, xx = np.mgrid[0:W, 0:W]
        ins |= (abs(xx + .5 - 8) <= (yy - 1) * 0.55) & (yy >= 2) & (yy < 8)
    elif key == 'cluster':
        ins = disc(W, W, 5.5, 10, 3.6) | disc(W, W, 10.5, 10, 3.6) | disc(W, W, 8, 5.5, 3.6)
    elif key == 'spark':
        ins = disc(W, W, 8, 8.5, 4.5)
    elif key == 'thorn':
        ins = disc(W, W, 8, 8, 4.6)
    else:
        ins = disc(W, W, 8, 8, 6.5)
    a, (cx, cy, rad) = shade_shape(ins, r)
    B, M, D = r[4], r[2], r[0]
    if key == 'ember':
        for (x, y) in ((6, 5), (7, 6), (7, 7), (8, 8), (9, 8), (9, 9), (10, 10), (6, 9), (5, 10)):
            px(a, x, y, r[4])
        px(a, 8, 7, r[3])
    elif key == 'rime':
        for i in range(3, 13):
            if ins[i, 8]: px(a, 8, i, r[1])
        for i in range(4, 12):
            if ins[8, i]: px(a, i, 8, r[1])
        px(a, 5, 5, B); px(a, 6, 5, B); px(a, 5, 6, B)
    elif key == 'spark':
        for (x, y) in ((2, 6), (3, 5), (4, 4), (5, 3), (11, 3), (12, 4), (13, 3), (13, 5), (12, 6), (3, 12), (2, 13)):
            px(a, x, y, B)
        px(a, 6, 7, B)
    elif key == 'venom':
        px(a, 6, 8, B); px(a, 6, 9, r[3]); px(a, 8, 15, M); px(a, 8, 14, D) if not ins[14, 8] else None
        for (x, y) in ((9, 10), (10, 11), (7, 12)):
            px(a, x, y, r[1])
    elif key == 'anvil':
        fill(a, 3, 10, 10, 1, r[1]); fill(a, 4, 11, 8, 1, r[0])
        px(a, 5, 5, B); px(a, 6, 5, B); px(a, 4, 6, r[3])
        px(a, 8, 7, r[1]); px(a, 7, 7, r[3])
    elif key == 'thorn':
        for (x, y) in ((8, 1), (8, 2), (8, 13), (8, 14), (1, 8), (2, 8), (13, 8), (14, 8), (3, 3), (12, 3), (3, 12), (12, 12), (4, 4), (11, 4), (4, 11), (11, 11)):
            px(a, x, y, r[1] if (x in (1, 14) or y in (1, 14) or (x, y) in ((3, 3), (12, 3), (3, 12), (12, 12))) else r[2])
        px(a, 6, 6, B)
    elif key == 'wisp':
        inner = disc(16, 16, 8, 8, 3.8)
        a[inner] = r[0]
        core = disc(16, 16, 8, 8, 1.6)
        a[core] = r[4]
        px(a, 4, 4, B); px(a, 5, 3, r[3])
    elif key == 'ray':
        for i in range(2, 14):
            px(a, i, 8, r[3]); px(a, 8, i, r[3])
        px(a, 8, 8, B); px(a, 7, 8, B); px(a, 8, 7, B); px(a, 9, 8, B); px(a, 8, 9, B)
        px(a, 4, 4, B)
    elif key == 'cluster':
        for (x, y) in ((4, 9), (9, 9), (7, 4)):
            px(a, x, y, B)
    elif key == 'lodestone':
        yy, xx = np.mgrid[0:16, 0:16]
        half = ins & (xx >= 8) & ~disc(16, 16, 8, 8, 6.5) | (ins & (xx >= 8))
        a[ins & (xx >= 8) & (yy > 1) & (yy < 14)] = r[1]
        gap = disc(16, 16, 8, 8, 2.6)
        a[gap] = r[0]
        fill(a, 6, 7, 4, 9, 'clear')
        for y in range(7, 16):
            for x in (5, 10):
                if a[y, x, 3] and y < 14: a[y, x] = r[0]
        fill(a, 2, 12, 3, 2, r[4]); fill(a, 11, 12, 3, 2, r[3])
        px(a, 4, 4, B)
    elif key == 'gale':
        for (x, y) in ((8, 8), (9, 8), (9, 7), (8, 6), (7, 6), (6, 7), (6, 8), (6, 9), (7, 10), (8, 10), (9, 10), (10, 10), (11, 9), (11, 8), (11, 7), (11, 6), (10, 5)):
            px(a, x, y, r[0])
        px(a, 4, 4, B); px(a, 5, 4, B)
    elif key == 'sap':
        heart_ = [(7, 9), (9, 9), (6, 10), (7, 10), (8, 10), (9, 10), (10, 10), (7, 11), (8, 11), (9, 11), (8, 12)]
        for (x, y) in heart_:
            px(a, x, y, r[0])
        px(a, 6, 7, B); px(a, 6, 8, r[3])
    if key not in ('wisp', 'ray'):
        # generic specular highlight top-left
        ys, xs = np.nonzero(ins)
        top = ys.min(); left = xs[ys == top + 2].min() if (ys == top + 2).any() else xs.min()
        px(a, left + 1, top + 2, B)
    return a

def ball_powder_keg():
    """Evolved Ember + Anvil: iron-banded keg with an ember fuse (blends both ramps)."""
    e, an = ramp(*BALLS['ember']), ramp(*BALLS['anvil'])
    rows = [
        '................',
        '..........f.....',
        '.........fF.....',
        '........ff......',
        '....######......',
        '...#aAAAAa#.....',
        '..#eEEEEEEe#....',
        '..#EHEEEEEE#....',
        '..#aAAAAAAa#....',
        '..#eEEEEEEe#....',
        '..#EEEEEEEe#....',
        '..#aAAAAAAa#....',
        '...#eeeeee#.....',
        '....######......',
        '................',
        '................',
    ]
    a = from_map(rows, {'#': e[0], 'e': e[1], 'E': e[2], 'H': e[4], 'a': an[1], 'A': an[3], 'f': e[3], 'F': e[4]})
    # shift right by 2 to centre
    out = canvas(16, 16); blit(out, a[:, :14], 2, 0)
    return out

# ---------------------------------------------------------------- 16x16 item icons (placeholder set)
ITEMS = {
    'bellows': (['................', '.....######.....', '....#WWWWWW#....', '...#WwwwwwwW#...', '..#WwwwwwwwwW#..', '..#WwwwwwwwwW#..',
                 '..#bbbbbbbbbb#..', '..#WwwwwwwwwW#..', '...#WwwwwwwW#...', '....#WwwwwW#....', '.....#WwwW#.....', '......#BB#......',
                 '......#BB#......', '......#BB#......', '.......##.......', '................'],
                {'#': 'ink', 'W': 'c3', 'w': 'c2', 'b': 'b3', 'B': 'b2'}),
    'soot_lens': (['................', '.....######.....', '....#bbbbbb#....', '...#b#xxxx#b#...', '..#b#xXxxxx#b#..', '..#b#XxxxxX#b#..',
                   '..#b#xxxxxx#b#..', '..#b#xxxxxx#b#..', '..#b#xxxxXx#b#..', '...#b#xxxx#b#...', '....#bbbbbb#....', '.....##bb##.....',
                   '......#bb#......', '......#bb#......', '.......##.......', '................'],
                  {'#': 'ink', 'b': 'b2', 'x': 'x1', 'X': 'x3'}),
    'thimble': (['................', '................', '......####......', '.....#BbBb#.....', '....#bBbBbB#....', '....#BbBbBb#....',
                 '...#bBbBbBbB#...', '...#BbBbBbBb#...', '...#bBbBbBbB#...', '...#BbBbBbBb#...', '..#oooooooooo#..', '..#OOOOOOOOOO#..',
                 '...##########...', '................', '................', '................'],
                {'#': 'ink', 'B': 'b3', 'b': 'b2', 'o': 'b1', 'O': 'b0'}),
    'slag_magnet': (['................', '..####....####..', '..#RR#....#xx#..', '..#RR#....#xx#..', '..#ss#....#ss#..', '..#ss#....#ss#..',
                     '..#ss#....#ss#..', '..#ss######ss#..', '..#sssssssSss#..', '...#ssssSsss#...', '....#ssssss#....', '.....######.....',
                     '................', '..g..........g..', '.g.g........g...', '................'],
                    {'#': 'ink', 'R': 'r2', 'x': 'x2', 's': 's4', 'S': 's5', 'g': 'g2'}),
    'furnace_coal': (['................', '................', '.......##.......', '......#yy#......', '.....#yOOy#.....', '....#yOrrOy#....',
                      '....#OrrrrO#....', '...##########...', '..#kkKkkkkkk#...', '..#kkkkkKkkk#...', '..#kKkkkkkkKk#..', '..#kkkkKkkkkk#..',
                      '...##########...', '................', '................', '................'],
                     {'#': 'ink', 'y': 'g3', 'O': 'c3', 'r': 'r2', 'k': 's1', 'K': 'c2'}),
    'chain_link': (['................', '..####..........', '.#ssss#.........', '#s#..#s#........', '#s#..#s####.....', '#s#..#ssSSS#....',
                    '.#ssss#S#.#S#...', '..####.#S#.#S#..', '......#S#..#S#..', '.......#SSSS#...', '........####....', '................',
                    '................', '................', '................', '................'],
                   {'#': 'ink', 's': 's4', 'S': 'b3'}),
}

def item(key):
    rows, k = ITEMS[key]
    return from_map(rows, k)

# ---------------------------------------------------------------- 24x24 hero emblems
HEROES = {  # key: (deep, bright) signature from art-bible §4
    'tinker': ('030F1F', '2B74D9'), 'wren': ('11031F', '822BD9'),
    'osk': ('031F18', '2BD9AD'), 'juno': ('1F0315', 'D92B9F'),
}
HERO_GLYPH = {
    'tinker': ['..#....#..', '..#....#..', '..#....#..', '..#....#..', '..#....#..', '...#..#...', '....##....', '....##....', '....##....', '...####...'],
    'wren':   ['....##....', '...#..#...', '..######..', '..#.##.#..', '..#.##.#..', '..#....#..', '..######..', '....##....', '....##....', '....##....'],
    'osk':    ['...####...', '..#....#..', '.#......#.', '.#......#.', '.#......#.', '..#....#..', '...####...', '....##....', '....##....', '...####...'],
    'juno':   ['....#.....', '...###....', '..#.#.#...', '.#..#..#..', '########..', '.#..#..#..', '..#.#.#...', '...###....', '....#.....', '..........'],
}

def hero_emblem(key):
    deep, bright = HEROES[key]
    W = 24
    a = canvas(W, W)
    outer = disc(W, W, 12, 12, 11.5)
    rim = disc(W, W, 12, 12, 10.5)
    inner = disc(W, W, 12, 12, 9)
    a[outer] = P['ink']; a[rim] = hx(bright); a[inner] = hx(deep)
    # rim highlight
    yy, xx = np.mgrid[0:W, 0:W]
    a[rim & ~inner & (yy < 9) & (xx < 12)] = tuple(min(255, int(c * 1.0 + 60)) for c in hx(bright)[:3]) + (255,)
    g = mask_outline(parse_mask(HERO_GLYPH[key]), 'cream', outc=hx(deep), shadow=False, pad=0)
    gh, gw = g.shape[:2]
    blit(a, g, (W - gw) // 2, (W - gh) // 2)
    return a

# ---------------------------------------------------------------- bitmap digit fonts
GLYPHS = {
    '0': ['.###.', '#...#', '#...#', '#...#', '#...#', '#...#', '.###.'],
    '1': ['..#..', '.##..', '..#..', '..#..', '..#..', '..#..', '.###.'],
    '2': ['.###.', '#...#', '....#', '...#.', '..#..', '.#...', '#####'],
    '3': ['####.', '....#', '....#', '.###.', '....#', '....#', '####.'],
    '4': ['...#.', '..##.', '.#.#.', '#..#.', '#####', '...#.', '...#.'],
    '5': ['#####', '#....', '####.', '....#', '....#', '#...#', '.###.'],
    '6': ['..##.', '.#...', '#....', '####.', '#...#', '#...#', '.###.'],
    '7': ['#####', '....#', '...#.', '..#..', '.#...', '.#...', '.#...'],
    '8': ['.###.', '#...#', '#...#', '.###.', '#...#', '#...#', '.###.'],
    '9': ['.###.', '#...#', '#...#', '.####', '....#', '...#.', '.##..'],
    '+': ['.....', '..#..', '..#..', '#####', '..#..', '..#..', '.....'],
    '-': ['.....', '.....', '.....', '#####', '.....', '.....', '.....'],
    '/': ['....#', '...#.', '...#.', '..#..', '.#...', '.#...', '#....'],
    'x': ['.....', '.....', '#...#', '.#.#.', '..#..', '.#.#.', '#...#'],
    '.': ['.', '.', '.', '.', '.', '.', '#'],
    '!': ['#', '#', '#', '#', '#', '.', '#'],
    ':': ['.', '#', '.', '.', '.', '#', '.'],
    '%': ['##..#', '##.#.', '...#.', '..#..', '.#...', '.#.##', '#..##'],
    'k': ['#....', '#....', '#..#.', '#.#..', '##...', '#.#..', '#..#.'],
    ' ': ['...', '...', '...', '...', '...', '...', '...'],
}

def glyph(ch, font):
    m = parse_mask(GLYPHS[ch])
    if font == 'crit':         # bold: OR with itself shifted 1px right
        p = np.pad(m, ((0, 0), (0, 1)))
        m = p | np.roll(p, 1, 1)
    if font == 'hud':          # cream with 1px ink drop shadow (down)
        g = np.pad(m, ((0, 1), (0, 0)))
        a = canvas(g.shape[1], g.shape[0])
        a[np.roll(g, 1, 0) & ~g] = P['ink']
        a[g] = P['cream']
        return a
    if font == 'dmg':          # white with ink outline, tinted at runtime
        return mask_outline(m, 'white', shadow=False)
    if font == 'crit':         # bold, baked gold->copper gradient, outline + 1px shadow
        a = mask_outline(m, 'g2', shadow=True)
        rows = a.shape[0]
        grad = ['g3', 'g3', 'g2', 'g2', 'g2', 'c3', 'c3', 'c3', 'c2', 'c2', 'c2']
        for y in range(rows):
            for x in range(a.shape[1]):
                if tuple(a[y, x]) == P['g2']:
                    a[y, x] = P[grad[min(y, len(grad) - 1)]]
        return a
    raise ValueError(font)

def text_digits(s, font, tint=None):
    gs = [glyph(ch, font) for ch in s]
    gap = 1 if font == 'hud' else -1   # outlined fonts overlap their outlines by 1px
    h = max(g.shape[0] for g in gs)
    w = sum(g.shape[1] for g in gs) + gap * (len(gs) - 1)
    a = canvas(w, h)
    x = 0
    for g in gs:
        blit(a, g, x, h - g.shape[0])
        x += g.shape[1] + gap
    if tint is not None:
        t = np.array(C(tint)[:3], float) / 255
        m = a[:, :, 3] > 0
        a[m, :3] = (a[m, :3] * t).astype(np.uint8)
    return a

def all_sprites():
    S = make_frames()
    for k in ICON_MASKS:
        S['ic_' + k] = icon(k)
    S['ic_coin_s'] = coin(9); S['ic_coin'] = coin(16); S['ic_heart_s'] = heart(9)
    for k in BALLS:
        S['ball_' + k] = ball(k)
    S['ball_powder_keg'] = ball_powder_keg()
    for k in ITEMS:
        S['item_' + k] = item(k)
    for k in HEROES:
        S['hero_' + k] = hero_emblem(k)
    names = {'+': 'plus', '-': 'minus', '/': 'slash', '.': 'dot', '!': 'bang', ':': 'colon', '%': 'pct', ' ': 'space'}
    for font in ('hud', 'dmg', 'crit'):
        for ch in GLYPHS:
            if font == 'crit' and ch in ' ':
                continue
            S[f'font_{font}_{names.get(ch, ch)}'] = glyph(ch, font)
    return S
