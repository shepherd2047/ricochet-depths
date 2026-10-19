# Ricochet Depths — Audio List (audio side, clean room)

Inputs read: `ballpit-spec/spec.md` (which events exist), `ballpit-spec/style.md` (mood), `pit-art/art-bible.md` (setting). No reference-game audio was heard, sampled or described. Every sound here is synthesised from scratch by `tools/gen.py` (numpy + scipy), so the output is ours to ship.

## Status

**Batch 1 (2026-10-19): auditioned and approved by the user, delivered (see `DELIVERY.md`).** 10 core SFX + one electro battle loop. The user chose electronic music, a ≈ 6 MB budget, and a small first batch to audition before the rest.

Status column below:
- ✅ **B1**: in batch 1, delivered in `sfx/` / `music/`, shown in `preview.html`.
- 📝 **draft**: recipe already exists in `gen.py` and a render is parked in `_drafts/`. Not auditioned, not part of the payload. Render with `tools/gen.py --all`. May be redone after batch-1 feedback.
- ⬜ **todo**: not started.

## Sonic identity

The setting is a sunken bell foundry (art bible §1). The scenery is dark and muted, and the balls and VFX are bright and punchy (style.md §1).

- **Music**: punchy electro. Four-on-the-floor kick, kick-ducked rolling bass and supersaw pad, ping-pong pluck arp, clap and hats. A quiet bronze bell stab on bars 1 and 9 ties it to the SFX.
- **World layer** (enemies, steps, board): low, dry, dusty. Clay and slag thuds, iron creaks, furnace rumble. Never louder than the ball layer.
- **Ball / VFX layer** (launch, hits, elements, pickups): bright, short, transient-heavy. The launcher is a tuning fork, so ball sounds have a small pitched ping.
- **Signature**: struck bronze, from our own inharmonic partial set (`BELL_PARTIALS` in gen.py). Used for crits and level-up now; later for the Fuser, the boss toll and the stings.
- Key: D minor / D harmonic minor for music and tuned SFX, so they don't clash.

## Conventions

- Files: `sfx/<event>_<n>.mp3` (n = variation 1..k), `music/<name>.mp3`. Masters: `masters/*.wav` (SFX 16-bit 32 kHz mono; music 16-bit 44.1 kHz stereo). Masters are Unity import sources, not payload.
- Variations: the game picks one at random and also applies ±3 % random pitch (±1.5 % for tuned UI sounds). XP pickups should step pitch up a semitone per pickup within a short combo window, then reset (the preview's "XP combo" demo does this).
- Durations are of the file, tail included.
- Voice limits (runtime): `ball_wall` 4, `ball_hit` 6, `en_step` 3 (one per enemy row, not per enemy), `pk_xp` 4, others 2. Drop same-event retriggers within 30 ms.

## Event list

P0 = needed for the demo slice (`pit-game/` features). P1 = nice to have.

### Balls

| Event | File key | P | Status | Dur | Var | Notes |
|---|---|---|---|---|---|---|
| Launch | `ball_launch` | P0 | ✅ B1 | 0.16 s | 3 | Tuning-fork ping + air puff. Plays per ball in a salvo, so short and soft |
| Wall bounce | `ball_wall` | P0 | ✅ B1 | 0.08 s | 3 | Dull stone tick, quiet; the most frequent sound in the game |
| Enemy hit | `ball_hit` | P0 | ✅ B1 | 0.13 s | 4 | Punchy click + body thud |
| Crit | `ball_crit` | P0 | ✅ B1 | 0.4 s | 2 | Hit + bright bronze ring |
| Catch ("good catch") | `ball_catch` | P0 | ✅ B1 | 0.18 s | 2 | Rising two-step pling |
| Return / recall | `ball_return` | P0 | 📝 draft | 0.28 s | 1 | Soft rising whoosh |
| Shield block | `ball_block` | P1 | ⬜ todo | 0.1 s | 2 | Iron clank, no damage |
| Ball swallowed / spat | `ball_gulp` | P1 | ⬜ todo | 0.3 s | 1 | |

### Elements (one hit per element; DoT tick loops are P1)

| Event | File key | P | Status | Dur | Var | Notes |
|---|---|---|---|---|---|---|
| Fire / burn hit (Ember) | `el_fire` | P0 | 📝 draft | 0.45 s | 2 | Crackle burst + whoomp |
| Freeze (Rime) | `el_freeze` | P0 | 📝 draft | 0.55 s | 2 | Glassy crystalline shards |
| Lightning chain (Spark) | `el_lightning` | P0 | 📝 draft | 0.4 s | 2 | Jittery zap + crackle; once per chain, not per target |
| Poison (Venom) | `el_poison` | P0 | 📝 draft | 0.45 s | 2 | Rising bubbles |
| Bomb / blast (Powder Keg) | `el_bomb` | P0 | 📝 draft | 1.0 s | 2 | Explosion with sub drop |
| Laser row/column (Ray) | `el_laser` | P0 | 📝 draft | 0.5 s | 2 | Bright beam sweep |
| Earthquake | `el_quake` | P0 | 📝 draft | 0.9 s | 1 | Low rumble + crunch |
| Burn tick | `el_burn_tick` | P1 | ⬜ todo | 0.15 s | 2 | |
| Bleed (Thorn) | `el_bleed` | P1 | ⬜ todo | 0.2 s | 2 | |
| Ghost pass-through (Wisp) | `el_ghost` | P1 | ⬜ todo | 0.3 s | 1 | |
| Life steal (Sap) | `el_lifesteal` | P1 | ⬜ todo | 0.3 s | 1 | |
| Split / brood burst | `el_split` | P1 | ⬜ todo | 0.25 s | 2 | |
| Wind push (Gale), pull (Lodestone) | `el_wind`, `el_pull` | P1 | ⬜ todo | 0.4 s | 1 | |
| Aura loops (blizzard, storm, …) | `el_aura_*` | P1 | ⬜ todo | 2 s loop | 1 | |

### Enemies

| Event | File key | P | Status | Dur | Var | Notes |
|---|---|---|---|---|---|---|
| Death | `en_death` | P0 | ✅ B1 | 0.42 s | 3 | Crumble into shards |
| Advance step | `en_step` | P0 | 📝 draft | 0.12 s | 3 | Low slag thud. One per turn tick, not per enemy |
| Melee wind-up | `en_windup` | P0 | 📝 draft | 0.6 s | 1 | Creak + hiss swell at wind-up start (the wind-up itself lasts half a turn) |
| Melee lunge / impact | `en_lunge` | P0 | 📝 draft | 0.35 s | 2 | Whoosh into heavy thump |
| Shoot (tar spit, cinder fling) | `en_shoot` | P0 | 📝 draft | 0.2 s | 2 | |
| Telegraph warning (cannon/laser aim) | `en_telegraph` | P0 | 📝 draft | 0.6 s | 1 | Two dull alarm strikes; repeat every 1 s during the 3 s warning |
| Boss phase change | `boss_phase` | P0 | 📝 draft | 2.6 s | 1 | The Bellwarden tolls (cracked deep bell) |
| Boss death | `boss_death` | P1 | ⬜ todo | 3 s | 1 | |
| Heal (Mender), spawn (Broodpot) | `en_heal`, `en_spawn` | P1 | ⬜ todo | 0.4 s | 1 | |

### Player

| Event | File key | P | Status | Dur | Var | Notes |
|---|---|---|---|---|---|---|
| Hurt | `pl_hurt` | P0 | ✅ B1 | 0.3 s | 2 | |
| Level-up | `pl_levelup` | P0 | ✅ B1 | 1.0 s | 1 | Rising bell arpeggio |
| Low HP (< 25 %) | `pl_lowhp_loop` | P0 | 📝 draft | 1.0 s loop | 1 | Heartbeat; loops while HP is low, music ducked −4 dB |
| Upgrade pick | `ui_pick` | P0 | 📝 draft | 0.3 s | 1 | Confirm chime |
| Reroll | `ui_reroll` | P0 | 📝 draft | 0.4 s | 1 | Ratchet + whoosh |
| Banish | `ui_banish` | P1 | ⬜ todo | 0.4 s | 1 | |
| Revive | `pl_revive` | P1 | ⬜ todo | 1.5 s | 1 | |

### Fuser

| Event | File key | P | Status | Dur | Var | Notes |
|---|---|---|---|---|---|---|
| Fuser drop / open | `fuser_open` | P0 | 📝 draft | 1.2 s | 1 | Furnace whoomph + low bell |
| Fuse (combine) | `fuser_fuse` | P0 | 📝 draft | 1.1 s | 1 | Two tones converge, clang |
| Evolve | `fuser_evolve` | P0 | 📝 draft | 1.8 s | 1 | Rising sweep + bright chord |
| Walk away | `fuser_skip` | P1 | ⬜ todo | 0.4 s | 1 | |

### Pickups

| Event | File key | P | Status | Dur | Var | Notes |
|---|---|---|---|---|---|---|
| XP gem | `pk_xp` | P0 | ✅ B1 | 0.09 s | 3 | Glassy tick; combo pitch-up at runtime |
| Gold | `pk_gold` | P0 | 📝 draft | 0.22 s | 2 | Small bronze coin ring (inharmonic, not a classic two-note coin) |
| Heal | `pk_heal` | P0 | 📝 draft | 0.5 s | 1 | Warm rising dyad |

### Board, flow, UI

| Event | File key | P | Status | Dur | Var | Notes |
|---|---|---|---|---|---|---|
| UI click | `ui_click` | P0 | ✅ B1 | 0.05 s | 1 | |
| Board expansion (boss turn) | `board_expand` | P0 | 📝 draft | 3.0 s | 1 | Grinding rumble + chain rattle, matching the 3 s camera pull-back |
| Clear bonus | `clear_bonus` | P0 | 📝 draft | 0.8 s | 1 | Sparkle flourish |
| Win sting | `sting_win` | P0 | 📝 draft | 3.0 s | 1 | D major resolve, bells. Redo in the electro style |
| Lose sting | `sting_lose` | P0 | 📝 draft | 2.6 s | 1 | Falling minor, detuned low bell. Redo in the electro style |
| UI back / cancel | `ui_back` | P1 | ⬜ todo | 0.06 s | 1 | |

### Music

| Track | File key | P | Status | Length | Notes |
|---|---|---|---|---|---|
| Battle loop | `mus_battle_loop` | P0 | ✅ B1 | 30.0 s (128 BPM, 16 bars) | Electro, D minor (Dm–B♭–Gm–A, 2 bars each). Bars 1–4: kick, clap, hats, rolling bass, dark pad. Bars 5–8: arp enters with its filter opening. Bars 9–16: full, brighter pad. Bar 16: clap roll + riser back into bar 1. Stereo, seamless |
| Boss loop | `mus_boss_loop` | P1 | ⬜ todo | ≈ 30 s | Electro, darker and faster (≈ 140 BPM), Phrygian. An ambient-style draft exists in `_drafts/`, but the style changed |
| Menu loop | `mus_menu_loop` | P1 | ⬜ todo | ≈ 24 s | Electro-lite: pad + arp, no kick. The ambient draft in `_drafts/` is superseded |
| Win / lose stings | see above | P0 | 📝 draft | | |

`_drafts/music/mus_battle_loop_ambient.mp3` is the earlier ambient battle loop, kept for comparison only.

## Mix targets

Peak levels the generator normalises to (dBFS): wall/xp −13, step −12, launch −10, UI −10 to −8, hits −6, crit/levelup −4 to −3, hurt −3, elements −5 to −1. The battle loop peaks at −1.5 with RMS ≈ −13.4 dBFS, so the music group should sit around −6 dB under SFX in Unity. Duck music −3 dB under the boss toll, board expansion and stings.

## Budget and formats

- Payload budget ≈ 6 MB (raised from 3 MB). Actual totals are at the end.
- **SFX**: MP3 mono, 32 kHz, VBR `lame -V 6` (≈ 2–11 KB each).
- **Music**: MP3 joint stereo, 44.1 kHz, VBR `lame -V 2` (≈ 215 kbps, ≈ 0.8 MB per 30 s loop). Stereo is worth it for the electro pad and arp width. At this rate the 6 MB budget fits about 5 loops plus all SFX.
- MP3 instead of OGG because Vorbis decoding in iOS WebView/Safari is unreliable, while TikTok's `tt.createInnerAudioContext` plays MP3 everywhere.
- Loops: the MP3s carry the LAME gapless header, and Web Audio `decodeAudioData` returns exactly 30.000 s (checked in Chrome). For Unity, import the matching **WAV master**; Unity re-encodes anyway.

### Unity WebGL import settings

Unity WebGL builds re-encode all AudioClips to AAC regardless of the Compression Format setting, so the source format only affects quality, not size. Import the WAV masters for best quality.

| Setting | SFX | Music loop |
|---|---|---|
| Force To Mono | on | **off** (stereo) |
| Load In Background | off | on |
| Load Type | Decompress On Load | Compressed In Memory (Streaming isn't supported on WebGL and falls back) |
| Preload Audio Data | on | on for battle |
| Compression Format | Vorbis/AAC (ignored on WebGL) | same |
| Quality | 50–60 % | 70 % |
| Sample Rate Setting | Override 32000 Hz | Preserve (44100) |
| Loop | n/a | AudioSource.loop = true; check the seam by ear |

Remote loading: put SFX in one Addressables group (load at boot) and each music track in its own group (load on demand). WebGL needs a user gesture before audio can start, so resume the AudioContext on the first tap on the title screen. For TikTok mini games, the converter's own audio path (`tt.createInnerAudioContext` with MP3 URLs on your CDN) is often lighter on memory than Unity's decoder for long music. If you use it, ship the MP3s directly instead of bundling them.

## Tools and licences considered

| Tool / source | Use here | Terms (checked 2026-10-19; re-check before shipping) |
|---|---|---|
| **numpy / scipy synthesis (`tools/gen.py`)** | **all files** | Our own code and output. numpy/scipy are BSD-licensed, which places no claim on generated audio |
| lame (encoder) | MP3 encoding | LGPL encoder; using it places no claim on the encoded files. MP3 patents expired in 2017 |
| jsfxr / sfxr | not needed (gen.py covers the same ground) | Output is free to use; the tool is open source |
| Kenney audio packs | not used | CC0 |
| Freesound | not used | Per-file licence; only CC0 results would be safe without attribution |
| ElevenLabs Sound Effects | **not used** | Conflicting statements: the general help centre says the free plan has no commercial licence and needs "elevenlabs.io" attribution; the sound-effects pages say SFX are royalty-free for commercial use, with free plans requiring attribution. Treat the free tier as unsafe for a commercial release |
| Suno | **not used** | Free (Basic) plan output is non-commercial only, Suno keeps ownership, and later subscribing does not grant rights to earlier songs. Paid-tier ownership terms reportedly changed in 2025–26 |

## Totals (batch 1, generated 2026-10-19)

| Group | Files | Size |
|---|---|---|
| SFX (`sfx/*.mp3`, 10 events × variations) | 24 | 74 KB |
| Electro battle loop (`music/mus_battle_loop.mp3`) | 1 | 814 KB |
| **Payload total** | **25** | **888,675 B = 0.85 MiB** (budget ≈ 6 MB) |

`masters/` (5.4 MB of WAV) holds Unity import sources only; don't ship it. `_drafts/` (8.3 MB) holds the unauditioned drafts; it isn't payload either.

Regenerate (deterministic seed):

```bash
python3 -m venv .venv && .venv/bin/pip install numpy scipy && .venv/bin/python tools/gen.py
```

Add `--all` to also render the 📝 draft SFX into `sfx/`. The script writes `masters/`, `sfx/`, `music/`, `manifest.json` (file list with durations and sizes, which the game can use too) and `manifest.js` (for `preview.html`).
