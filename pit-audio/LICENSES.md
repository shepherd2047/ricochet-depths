# Audio licences: Ricochet Depths

Every file in `sfx/`, `music/` and `masters/` was **synthesised from scratch** by `tools/gen.py` in this folder: oscillators, noise and filters written for this project, using numpy and scipy, then encoded with lame. No samples, loops, presets or recordings from any other source were used, and no reference-game audio was heard or analysed.

| Files | Source | Licence / owner |
|---|---|---|
| `sfx/*.mp3` (24 files), `masters/*_<n>.wav` | `tools/gen.py` SFX recipes: `ball_launch`, `ball_wall`, `ball_hit`, `ball_crit`, `ball_catch`, `en_death`, `pl_hurt`, `pl_levelup`, `pk_xp`, `ui_click` | Original work made for this project; owned by the project owner. No third-party rights |
| `music/mus_battle_loop.mp3`, `masters/mus_battle_loop.wav` | `tools/gen.py` `battle_loop()`: original electro composition (D minor, 128 BPM, 16 bars) | Original work, owned by the project owner |
| `_drafts/**` (not payload) | Earlier renders from `tools/gen.py`: the other SFX recipes, plus ambient battle, boss and menu loops | Original work, owned by the project owner |
| `tools/gen.py`, `preview.html` | Written for this project | Owned by the project owner |

## Tools used (no claim on the output)

| Tool | Licence | Note |
|---|---|---|
| numpy 2.x, scipy 1.18 | BSD-3-Clause | Libraries only; generated audio is not a derivative of them |
| LAME 4.0 (MP3 encoder) | LGPL | Encoder licence doesn't extend to encoded content. MP3 patents expired in 2017 |
| Python 3.14 | PSF | |

Chord progressions (i–VI–iv–V), scales and drum patterns are common musical building blocks and aren't protectable. All melodies were written here.

If any third-party asset is added later (for example a Kenney CC0 pack or a Freesound CC0 file), add a row with the exact file, source URL, author and licence.
