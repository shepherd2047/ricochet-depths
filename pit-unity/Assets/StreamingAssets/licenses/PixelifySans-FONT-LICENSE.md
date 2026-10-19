# Font licence: Pixelify Sans

- **Font:** Pixelify Sans, designed by Stefie Justprince
- **Copyright:** Copyright 2021 The Pixelify Sans Project Authors (https://github.com/eifetx/Pixelify-Sans)
- **Licence:** SIL Open Font License, Version 1.1. The full text is in [OFL.txt](OFL.txt) and at https://openfontlicense.org
- **Source:** downloaded 2026-10-19 from https://github.com/google/fonts/tree/main/ofl/pixelifysans (`PixelifySans[wght].ttf`, `OFL.txt`)

## Files

| file | notes |
|---|---|
| `PixelifySans[wght].ttf` | upstream variable font, unmodified |
| `PixelifySans-Regular.ttf` | static instance at wght=400, made with `fontTools.varLib.instancer --update-name-table` |
| `PixelifySans-Bold.ttf` | static instance at wght=700, made the same way |

The static instances are "Modified Versions" under the OFL. The copyright notice declares no Reserved Font Name, so they may keep the name "Pixelify Sans".

## Obligations when shipping

- The OFL allows bundling the font in the game, including commercial use. Do not sell the font files on their own.
- Ship `OFL.txt` with the game, or put the copyright line and licence text in an in-game credits or licence screen. For a TikTok mini game, include the copyright line and a link to the licence in the credits or about text, and keep `OFL.txt` in the package.
- TMP SDF font assets generated from these files are covered by the same licence.

## Other type in the UI

The bitmap digit fonts (`font_hud_*`, `font_dmg_*`, `font_crit_*` in the UI atlas) are original glyphs drawn for this project in `tools/sprites.py`. They are not derived from any font.
