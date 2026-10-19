"""Screen layouts for the Pit UI, in art pixels (design grid 180x320 = 9:16).

One art pixel = 2 pt in Figma (360x640 frames) = 6 device px on a 1080-wide phone.
The same data drives the wireframe page, the mockup page and UI-SPEC.md coordinates.
"""
from sprites import BALLS

W, H = 180, 320
SAFE_TOP = 22          # status bar / notch (44 pt)
SAFE_BOTTOM = 17       # home indicator (34 pt)
CAPSULE = (124, 22, 52, 22)   # reserved TikTok capsule zone, top-right (x, y, w, h)

# element constructors ------------------------------------------------------------
def N9(name, spr, x, y, w, h):            return dict(k='n9', name=name, spr=spr, x=x, y=y, w=w, h=h)
def IMG(name, spr, x, y, s=1):            return dict(k='img', name=name, spr=spr, x=x, y=y, s=s)
def TILE(name, spr, x, y, w, h):          return dict(k='tile', name=name, spr=spr, x=x, y=y, w=w, h=h)
def NUM(name, txt, font, x, y, s=1, tint=None, anchor='l'):
    return dict(k='num', name=name, txt=txt, font=font, x=x, y=y, s=s, tint=tint, anchor=anchor)
def TXT(name, txt, x, y, w, h, pt=12, wt='Regular', col='F2EEE4', ha='CENTER', va='CENTER', shadow=True):
    return dict(k='txt', name=name, txt=txt, x=x, y=y, w=w, h=h, pt=pt, wt=wt, col=col, ha=ha, va=va, shadow=shadow)
def RECT(name, x, y, w, h, col, op=1.0, r=0):  return dict(k='rect', name=name, x=x, y=y, w=w, h=h, col=col, op=op, r=r)
def ELL(name, x, y, w, h, col, glow=None):     return dict(k='ell', name=name, x=x, y=y, w=w, h=h, col=col, glow=glow)
def ZONE(name, x, y, w, h, label):             return dict(k='zone', name=name, x=x, y=y, w=w, h=h, label=label)

def center_x(w):
    return (W - w) // 2

# shared pieces ----------------------------------------------------------------------
BALL_GLOW = {k: v[1] for k, v in BALLS.items()}

def scene(enemies=True, boss=False):
    """Placeholder for the 3D battlefield (not UI; shown so the HUD reads in context)."""
    e = [RECT('scene/fog', 0, 0, W, H, '262937'),
         RECT('scene/wall_L', 0, 0, 10, H, '202A2E'), RECT('scene/wall_R', 170, 0, 10, H, '202A2E'),
         RECT('scene/wall_L_edge', 10, 0, 2, H, '323E42'), RECT('scene/wall_R_edge', 168, 0, 2, H, '323E42')]
    if enemies and not boss:
        spots = [(20, 84), (52, 84), (100, 84), (132, 84), (36, 108), (84, 108), (148, 108),
                 (20, 132), (68, 132), (116, 132), (52, 156), (132, 156)]
        for i, (x, y) in enumerate(spots):
            e += [RECT(f'scene/enemy{i}', x, y, 16, 14, '38302A', r=2),
                  RECT(f'scene/enemy{i}_eyeL', x + 4, y + 5, 2, 1, 'D1BC7D'),
                  RECT(f'scene/enemy{i}_eyeR', x + 10, y + 5, 2, 1, 'D1BC7D')]
    if boss:
        e += [RECT('scene/boss', 50, 84, 80, 80, '3A2E22', r=10), RECT('scene/boss_crack', 88, 96, 4, 56, 'FF9E4A'),
              RECT('scene/boss_rim', 50, 150, 80, 8, '6B5640', r=2)]
    for i, (k, x, y) in enumerate([('ember', 44, 186), ('rime', 92, 172), ('spark', 120, 204), ('ember', 70, 214)]):
        e.append(ELL(f'scene/ball{i}', x, y, 6, 6, BALLS[k][2], glow=BALL_GLOW[k]))
    e += [RECT('scene/hero', 84, 238, 12, 16, '2B74D9', r=3), RECT('scene/hero_head', 86, 232, 8, 7, '525861', r=3)]
    return e

def safe_zones():
    return [ZONE('safe/status_notch', 0, 0, W, SAFE_TOP, 'status bar / notch'),
            ZONE('safe/tiktok_capsule', *CAPSULE, 'TikTok capsule'),
            ZONE('safe/home_indicator', 0, H - SAFE_BOTTOM, W, SAFE_BOTTOM, 'home indicator')]

def capsule_mock():
    x, y, w, h = CAPSULE
    return [RECT('mock/capsule', x + 6, y + 3, 42, 15, 'FFFFFF', op=0.18, r=8),
            TXT('mock/capsule_label', '•••  ◉', x + 6, y + 3, 42, 15, pt=10, col='FFFFFF', shadow=False)]

def dim(op=0.72):
    return [RECT('dim', 0, 0, W, H, '06080A', op=op)]

def top_hud(level='7', xp=0.62, gold='235', speed='x1', boss=False):
    e = [N9('hud/btn_pause', 'btn_secondary', 4, 24, 24, 24), IMG('hud/btn_pause/icon', 'ic_pause', 8, 27),
         N9('hud/btn_speed', 'btn_secondary', 30, 24, 24, 24), NUM('hud/btn_speed/label', speed, 'hud', 42, 31, anchor='c'),
         N9('hud/gold', 'inset', 58, 28, 52, 16), IMG('hud/gold/icon', 'ic_coin_s', 61, 31), NUM('hud/gold/value', gold, 'hud', 106, 32, anchor='r'),
         IMG('hud/level_badge', 'badge_level', 4, 50), NUM('hud/level_badge/value', level, 'hud', 13, 55, anchor='c'),
         N9('hud/xp/frame', 'bar_frame', 24, 54, 152, 10), N9('hud/xp/fill', 'bar_xp', 26, 56, int(148 * xp), 6),
         TXT('hud/turn', 'Turn 34 · Boss at 60', 24, 65, 152, 8, pt=9, col='8FB0B8', ha='RIGHT')]
    if boss:
        e[-1] = TXT('hud/boss/name', 'THE BELLWARDEN', 24, 66, 152, 9, pt=10, wt='Bold', col='F2B872', ha='LEFT')
        e += [N9('hud/boss/frame', 'bar_frame', 24, 76, 152, 10), N9('hud/boss/fill', 'bar_boss', 26, 78, int(148 * 0.86), 6)]
    return e

SLOTS_BALLS = ['ember', 'rime', 'spark', None]
SLOTS_ITEMS = ['soot_lens', 'bellows', None, None]

def slot_strip(y=278, hp=(84, 120)):
    e = [IMG('hud/hp/heart', 'ic_heart_s', 4, y - 15), N9('hud/hp/frame', 'bar_frame', 15, y - 16, 161, 12),
         N9('hud/hp/lag', 'bar_hp_lag', 17, y - 14, int(157 * (hp[0] + 9) / hp[1]), 8),
         N9('hud/hp/fill', 'bar_hp', 17, y - 14, int(157 * hp[0] / hp[1]), 8),
         NUM('hud/hp/value', f'{hp[0]}/{hp[1]}', 'hud', 95, y - 13, anchor='c')]
    for i, b in enumerate(SLOTS_BALLS):
        x = 4 + i * 21
        e.append(N9(f'slots/ball{i}', 'slot_ball' if b else 'slot_empty', x, y, 20, 20))
        if b:
            e.append(IMG(f'slots/ball{i}/icon', 'ball_' + b, x + 2, y + 2))
    for i, it in enumerate(SLOTS_ITEMS):
        x = 93 + i * 21
        e.append(N9(f'slots/item{i}', 'slot_item' if it else 'slot_empty', x, y, 20, 20))
        if it:
            e.append(IMG(f'slots/item{i}/icon', 'item_' + it, x + 2, y + 2))
    return e

def pips(prefix, x, y, have, nxt=0, total=3):
    e = []
    for i in range(total):
        spr = 'pip_on' if i < have else ('pip_next' if i < have + nxt else 'pip_off')
        e.append(IMG(f'{prefix}/pip{i}', spr, x + i * 7, y))
    return e

def button(name, kind, x, y, w, h, label, pt=14, icon=None, num=None):
    e = [N9(name, kind, x, y, w, h)]
    cy = y + (h - 2) // 2      # face centre sits 1px above the lip
    if icon and label:
        e.append(TXT(name + '/label', label, x + 6, y, w - 12 - (20 if num else 0), h - 2, pt=pt, wt='Bold', ha='LEFT' if num else 'CENTER'))
    elif label:
        e.append(TXT(name + '/label', label, x, y, w, h - 2, pt=pt, wt='Bold'))
    if icon and not label:
        e.append(IMG(name + '/icon', icon, x + (w - 16) // 2, cy - 8))
    if num:
        txt, coin = num
        e.append(IMG(name + '/coin', 'ic_coin_s', x + w - 32, cy - 4))
        e.append(NUM(name + '/cost', txt, 'hud', x + w - 7, cy - 3, anchor='r'))
    return e

def ribbon(name, text, y, w=120):
    x = center_x(w)
    return [N9(name, 'ribbon', x, y, w, 20), TXT(name + '/label', text, x, y, w, 17, pt=16, wt='Bold')]

def card(prefix, x, y, w, h, icon_spr, title, desc, have, nxt, tag=None, frame='card'):
    e = [N9(prefix, frame, x, y, w, h),
         N9(prefix + '/icon_frame', 'slot_ball' if icon_spr.startswith('ball') else 'slot_item', x + 7, y + 10, 36, 36),
         IMG(prefix + '/icon', icon_spr, x + 9, y + 12, s=2),
         TXT(prefix + '/name', title, x + 50, y + 6, 70, 12, pt=14, wt='Bold', ha='LEFT')]
    e += pips(prefix + '/pips', x + 50, y + 21, have, nxt)
    if tag:
        txt, col = tag
        e += [N9(prefix + '/tag', col, x + w - 38, y + 7, 30, 11), TXT(prefix + '/tag/label', txt, x + w - 38, y + 7, 30, 10, pt=9, wt='Bold', col='0B0F11', shadow=False)]
    e.append(TXT(prefix + '/desc', desc, x + 50, y + 29, w - 58, 22, pt=11, col='D9D4C7', ha='LEFT', va='TOP'))
    return e

# screens ----------------------------------------------------------------------------
def s_title():
    e = scene(enemies=False)
    e += [ELL('title/glow', 30, 50, 120, 110, 'D98841', glow='D98841')]
    e[-1]['op'] = 0.10
    e += [TXT('title/logo_top', 'RICOCHET', 10, 66, 160, 26, pt=34, wt='Bold', col='F2EEE4'),
          TXT('title/logo_bottom', 'DEPTHS', 10, 92, 160, 32, pt=46, wt='Bold', col='D98841'),
          TXT('title/tagline', 'Bounce. Break. Go deeper.', 10, 128, 160, 12, pt=12, col='8FB0B8')]
    for i, (k, x, y) in enumerate([('ember', 26, 150), ('rime', 52, 160), ('spark', 82, 152), ('venom', 112, 162), ('ray', 138, 150)]):
        e.append(IMG(f'title/ball{i}', 'ball_' + k, x, y))
    e += button('title/btn_play', 'btn_primary', 40, 196, 100, 30, 'PLAY', pt=18)
    e += button('title/btn_sound', 'btn_secondary', 4, 278, 24, 24, None, icon='ic_sound')
    e += button('title/btn_music', 'btn_secondary', 30, 278, 24, 24, None, icon='ic_music')
    e += [TXT('title/version', 'demo v0.1', 120, 284, 56, 12, pt=9, col='5E7F89', ha='RIGHT')]
    return e

HERO_INFO = [('tinker', 'TINKER', 'Bell-mender', 'Standard shots. A steady, simple start.'),
             ('wren', 'WREN', 'Lamplighter scout', 'Shots pierce foes until they hit a wall.'),
             ('osk', 'BROTHER OSK', 'Ferryman-monk', 'No minor balls. Extra copies of one ball.'),
             ('juno', 'JUNO', 'Surveyor', 'Lobs every shot in a high arc.')]

def s_hero_pick():
    e = scene(enemies=False) + dim(0.55)
    e += button('pick/btn_back', 'btn_secondary', 4, 24, 24, 24, None, icon='ic_back')
    e += [TXT('pick/header', 'CHOOSE A HERO', 32, 26, 90, 20, pt=14, wt='Bold', ha='LEFT')]
    e += [N9('pick/preview', 'inset', 20, 56, 140, 94), ELL('pick/preview/pedestal', 54, 126, 72, 14, '2B74D9', glow='2B74D9'),
          IMG('pick/preview/emblem', 'hero_tinker', 54, 60, s=3),
          TXT('pick/preview/note', '3D model (RenderTexture)', 20, 139, 140, 10, pt=8, col='5E7F89', shadow=False)]
    e += [TXT('pick/name', 'TINKER', 10, 154, 160, 14, pt=18, wt='Bold'),
          TXT('pick/role', 'Bell-mender', 10, 168, 160, 10, pt=11, col='2B74D9'),
          TXT('pick/rule', 'Standard shots. A steady, simple start.', 14, 180, 152, 20, pt=11, col='D9D4C7', va='TOP')]
    for i, (k, nm, _, _) in enumerate(HERO_INFO):
        x = 8 + i * 42
        e += [N9(f'pick/tile{i}', 'card', x, 204, 38, 38), IMG(f'pick/tile{i}/emblem', 'hero_' + k, x + 7, 211),
              TXT(f'pick/tile{i}/name', nm.split()[-1].title(), x - 2, 243, 42, 9, pt=9, col='F2EEE4' if i == 0 else '8FB0B8')]
        if i == 0:
            e.append(N9('pick/tile0/selected', 'select', x - 2, 202, 42, 42))
    e += button('pick/btn_start', 'btn_primary', 30, 262, 120, 28, 'START RUN', pt=16)
    return e

def s_hud():
    e = scene()
    e += [NUM('fx/dmg1', '37', 'dmg', 60, 96), NUM('fx/dmg2', '12', 'dmg', 146, 100, tint='D98841'),
          NUM('fx/crit', '128!', 'crit', 80, 118, s=2)]
    e += top_hud() + slot_strip()
    return e

LEVELUP_CARDS = [('ball_ember', 'Ember', 'Hits ignite. Burning foes take 4 dmg a second.', 1, 1, ('LV 2', 'tag_gold')),
                 ('item_soot_lens', 'Soot Lens', 'Crit chance +6%.', 0, 1, ('NEW', 'tag_green')),
                 ('ball_rime', 'Rime', 'Hits chill. Five chills freeze a foe.', 0, 1, ('NEW', 'tag_green'))]

def s_levelup():
    e = scene() + top_hud() + dim()
    e += ribbon('levelup/header', 'LEVEL UP!', 44)
    for i, (spr, t, d, have, nxt, tag) in enumerate(LEVELUP_CARDS):
        e += card(f'levelup/card{i}', 10, 66 + i * 54, 160, 52, spr, t, d, have, nxt, tag)
    e.append(N9('levelup/card0/selected', 'select', 8, 64, 164, 56))
    e += button('levelup/btn_reroll', 'btn_secondary', 46, 232, 88, 24, 'REROLL', pt=13, icon=True, num=('15', True))
    e += slot_strip()
    return e

def option(prefix, y, title, desc, icons, frame='card_copper'):
    x, w, h = 10, 160, 52
    e = [N9(prefix, frame, x, y, w, h), N9(prefix + '/icons', 'inset', x + 6, y + 10, 62, 36)]
    cx = x + 6 + 31 - sum(16 if s.startswith(('ball', 'item')) else 9 for s in icons) // 2
    for j, s in enumerate(icons):
        if s.startswith(('ball', 'item')):
            e.append(IMG(f'{prefix}/icon{j}', s, cx, y + 20)); cx += 16
        else:
            e.append(IMG(f'{prefix}/icon{j}', s, cx - 2, y + 20)); cx += 9
    e += [TXT(prefix + '/title', title, x + 74, y + 7, w - 80, 12, pt=13, wt='Bold', ha='LEFT', col='F2EEE4' if frame != 'card_disabled' else '7C8488'),
          TXT(prefix + '/desc', desc, x + 74, y + 21, w - 80, 30, pt=10, col='D9D4C7' if frame != 'card_disabled' else '7C8488', ha='LEFT', va='TOP')]
    return e

def s_fuser():
    e = scene() + top_hud() + dim()
    e += ribbon('fuser/header', 'THE CRUCIBLE', 44, w=130)
    e += option('fuser/opt_upgrade', 66, 'FREE UPGRADES', 'Level up 3 of your balls and items.', ['ball_ember', 'item_soot_lens', 'ball_spark'])
    e += [NUM('fuser/opt_upgrade/plus', '+3', 'hud', 66, 102, anchor='r')]
    e += option('fuser/opt_combine', 120, 'COMBINE', 'Merge two Lv 3 balls. The result stops levelling.', ['ball_rime', 'ic_plus', 'ball_spark'])
    e += option('fuser/opt_evolve', 174, 'EVOLVE', 'Ember + Anvil become Powder Keg.', ['ball_ember', 'ic_plus', 'ball_anvil', 'ic_arrow', 'ball_powder_keg'])
    e += button('fuser/btn_skip', 'btn_secondary', 46, 232, 88, 24, 'SKIP', pt=13, icon=True, num=('+50', True))
    e += slot_strip()
    return e

def s_fuser_combine():
    e = scene() + top_hud() + dim()
    e += ribbon('combine/header', 'COMBINE', 44)
    e += [TXT('combine/hint', 'Pick a host, then a partner. Both must be Lv 3.', 14, 68, 152, 20, pt=10, col='D9D4C7')]
    balls = [('ember', 2), ('rime', 3), ('spark', 3), ('anvil', 3)]
    for i, (k, lv) in enumerate(balls):
        x = 12 + i * 40
        ok = lv == 3
        e += [N9(f'combine/ball{i}', 'slot_ball' if ok else 'slot_empty', x, 94, 36, 36), IMG(f'combine/ball{i}/icon', 'ball_' + k, x + 2, 96, s=2)]
        e += pips(f'combine/ball{i}/pips', x + 8, 133, lv)
        if not ok:
            e.append(RECT(f'combine/ball{i}/dim', x, 94, 36, 36, '06080A', op=0.55))
    e += [N9('combine/ball1/selected', 'select', 50, 92, 40, 40), N9('combine/ball2/selected', 'select', 90, 92, 40, 40),
          N9('combine/ball1/tag', 'tag_gold', 52, 141, 36, 11), TXT('combine/ball1/tag/label', 'HOST', 52, 141, 36, 10, pt=9, wt='Bold', col='0B0F11', shadow=False),
          N9('combine/ball2/tag', 'tag_green', 92, 141, 36, 11), TXT('combine/ball2/tag/label', 'PARTNER', 92, 141, 36, 10, pt=8, wt='Bold', col='0B0F11', shadow=False)]
    e += [N9('combine/preview', 'panel', 14, 158, 152, 70),
          IMG('combine/preview/a', 'ball_rime', 46, 172), IMG('combine/preview/plus', 'ic_plus', 66, 175), IMG('combine/preview/b', 'ball_spark', 80, 172),
          IMG('combine/preview/arrow', 'ic_arrow', 100, 176), IMG('combine/preview/res', 'ball_rime', 116, 172), IMG('combine/preview/res2', 'ball_spark', 124, 176),
          TXT('combine/preview/desc', 'Rime stays Lv 3 and gains chain lightning. It can no longer level up.', 22, 190, 136, 32, pt=10, col='D9D4C7', va='TOP')]
    e += button('combine/btn_back', 'btn_secondary', 14, 232, 60, 24, 'BACK', pt=13)
    e += button('combine/btn_confirm', 'btn_primary', 80, 232, 86, 24, 'COMBINE', pt=13)
    e += slot_strip()
    return e

def s_pause():
    e = scene() + top_hud() + dim(0.78)
    e += [N9('pause/panel', 'panel', 14, 54, 152, 238)]
    e += ribbon('pause/header', 'PAUSED', 44, w=100)
    e += [TXT('pause/loadout', 'LOADOUT', 22, 70, 136, 10, pt=10, wt='Bold', col='8FB0B8', ha='LEFT')]
    lv_b = [3, 3, 2, 0]; lv_i = [1, 2, 0, 0]
    for row, (kind, names, lvs, y) in enumerate((('ball', SLOTS_BALLS, lv_b, 82), ('item', SLOTS_ITEMS, lv_i, 124))):
        for i, (n, lv) in enumerate(zip(names, lvs)):
            x = 23 + i * 35
            e.append(N9(f'pause/{kind}{i}', (f'slot_{kind}' if n else 'slot_empty'), x, y, 30, 30))
            if n:
                e.append(IMG(f'pause/{kind}{i}/icon', f'{kind}_{n}', x + 7, y + 7))
                e += pips(f'pause/{kind}{i}/pips', x + 5, y + 31, lv)
    e += button('pause/btn_resume', 'btn_primary', 30, 166, 120, 26, 'RESUME', pt=15)
    e += button('pause/btn_restart', 'btn_secondary', 30, 198, 120, 24, 'RESTART', pt=13)
    e += button('pause/btn_quit', 'btn_secondary', 30, 228, 120, 24, 'QUIT TO TITLE', pt=13)
    e += button('pause/btn_sound', 'btn_secondary', 62, 260, 24, 24, None, icon='ic_sound')
    e += button('pause/btn_music', 'btn_secondary', 94, 260, 24, 24, None, icon='ic_music')
    return e

def results(win):
    e = scene(enemies=not win) + dim(0.8)
    if win:
        e += [TXT('end/title', 'PIT CLEARED!', 10, 42, 160, 26, pt=28, wt='Bold', col='E6CA5C'),
              TXT('end/subtitle', 'The Bellwarden falls silent.', 10, 70, 160, 12, pt=12, col='D9D4C7')]
    else:
        e += [TXT('end/title', 'CREW LOST', 10, 42, 160, 26, pt=28, wt='Bold', col='FF7A7A'),
              TXT('end/subtitle', 'Fell on turn 47 of 60.', 10, 70, 160, 12, pt=12, col='D9D4C7')]
    e += [N9('end/stats', 'panel', 14, 90, 152, 148)]
    rows = [('ic_clock', 'Turns', '60' if win else '47'), ('ic_skull', 'Foes broken', '412' if win else '305'),
            ('ic_sword', 'Damage dealt', '48.2k' if win else '29.7k'), ('ic_star', 'Best hit', '1284' if win else '806'),
            ('ic_coin', 'Gold earned', '386' if win else '244'), ('ball_ember', 'Top ball: Ember', '21.3k' if win else '12.9k')]
    for i, (ic, label, val) in enumerate(rows):
        y = 100 + i * 22
        e += [N9(f'end/row{i}', 'inset', 22, y, 136, 20), IMG(f'end/row{i}/icon', ic, 25, y + 2),
              TXT(f'end/row{i}/label', label, 44, y, 70, 20, pt=11, ha='LEFT', col='D9D4C7'),
              NUM(f'end/row{i}/value', val, 'hud', 153, y + 6, anchor='r')]
    e += button('end/btn_title', 'btn_secondary', 14, 250, 60, 26, None, icon='ic_home')
    e += button('end/btn_again', 'btn_primary', 80, 250, 86, 26, 'PLAY AGAIN' if win else 'RETRY', pt=14)
    return e

def s_win():  return results(True)
def s_lose(): return results(False)

def s_fx():
    e = scene(boss=True) + top_hud(boss=True)
    e += [TILE('boss_warning/stripe_top', 'banner_stripe', 0, 104, W, 8), N9('boss_warning/body', 'banner_body', 0, 112, W, 40),
          TILE('boss_warning/stripe_bottom', 'banner_stripe', 0, 152, W, 8),
          TXT('boss_warning/title', 'WARNING', 0, 114, W, 20, pt=24, wt='Bold', col='FF7A7A'),
          TXT('boss_warning/subtitle', 'THE BELLWARDEN RISES', 0, 134, W, 14, pt=12, wt='Bold', col='F2EEE4')]
    # damage-number legend
    e += [N9('legend/panel', 'inset', 8, 172, 164, 88),
          TXT('legend/title', 'FLOATING NUMBERS', 14, 175, 150, 10, pt=9, wt='Bold', col='8FB0B8', ha='LEFT')]
    rows = [('37', 'dmg', None, 1, 'Normal hit'), ('12', 'dmg', 'D98841', 1, 'Burn tick'), ('8', 'dmg', '5DA629', 1, 'Poison tick'),
            ('21', 'dmg', '9FF2EE', 1, 'Frozen bonus'), ('-12', 'dmg', 'FF7A7A', 1, 'Hero hurt'), ('+5', 'dmg', 'B8F27A', 1, 'Heal')]
    for i, (t, f, tint, s, lab) in enumerate(rows):
        col, row = i % 2, i // 2
        x, y = 14 + col * 78, 188 + row * 14
        e += [NUM(f'legend/n{i}', t, f, x, y, s=s, tint=tint), TXT(f'legend/l{i}', lab, x + 22, y - 1, 54, 10, pt=9, ha='LEFT', col='D9D4C7')]
    e += [NUM('legend/crit', '128!', 'crit', 14, 232, s=2), TXT('legend/crit_l', 'Crit: bold, gold, ×2, punch-in', 86, 234, 82, 20, pt=9, ha='LEFT', col='D9D4C7', va='TOP')]
    e += slot_strip()
    return e

SCREENS = [
    ('01 Title', s_title), ('02 Hero pick', s_hero_pick), ('03 Battle HUD', s_hud), ('04 Level up', s_levelup),
    ('05 Crucible (fuser)', s_fuser), ('06 Crucible – combine', s_fuser_combine), ('07 Pause', s_pause),
    ('08 Win', s_win), ('09 Lose', s_lose), ('10 Boss warning + numbers', s_fx),
]
