#!/bin/sh
# Turns VfxLabCapture frames into the preview clips and the overview image.
#   pit-vfx/tools/make_previews.sh <capture dir>     (holds Ember/ Rime/ Spark/ Ray/ with f_000.png ...)
# Writes pit-vfx/Previews/<Ball>.mp4 (540x960, 30 fps), <Ball>.gif (270x480, 15 fps) and overview.png.
set -e
CAP="$1"
HERE="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$HERE/Previews"
ATLAS="$HERE/VfxLab/Assets/RicochetVfx/Textures/RicochetVfxAtlas.png"
mkdir -p "$OUT"
for e in Ember Rime Spark Ray; do
  ffmpeg -loglevel error -y -framerate 30 -i "$CAP/$e/f_%03d.png" -c:v libx264 -pix_fmt yuv420p -crf 18 "$OUT/$e.mp4"
  ffmpeg -loglevel error -y -framerate 30 -i "$CAP/$e/f_%03d.png" \
    -vf "fps=15,scale=270:480:flags=lanczos,split[a][b];[a]palettegen=max_colors=96[p];[b][p]paletteuse=dither=none" "$OUT/$e.gif"
done
# overview: one key frame per ball (top half cropped to the action), then the atlas at 2x
K="${KEYFRAMES:-Ember:66 Rime:102 Spark:60 Ray:24}"
TMP="$(mktemp -d)"
col() { case "$1" in Ember) echo ff7b39;; Rime) echo 7fd8ff;; Spark) echo ffe14d;; Ray) echo ff9df0;; esac; }
args=""; i=0; filt=""
for kv in $K; do
  e="${kv%%:*}"; f=$(printf "%03d" "${kv##*:}")
  python3 "$HERE/tools/pixel_label.py" "$e" "$TMP/l$i.ppm" 4 "$(col $e)"
  args="$args -i $CAP/$e/f_$f.png -i $TMP/l$i.ppm"
  filt="$filt[$((i*2))]crop=540:540:0:120[c$i];[c$i][$((i*2+1))]overlay=12:12[k$i];"
  i=$((i+1))
done
ffmpeg -loglevel error -y $args -i "$ATLAS" -filter_complex \
  "${filt}[k0][k1]hstack[t];[k2][k3]hstack[m];color=c=0x262937:s=512x512[ab];[8]scale=512:512:flags=neighbor[at];[ab][at]overlay=format=auto,pad=1080:540:284:14:0x262937[a];[t][m][a]vstack=inputs=3" \
  -frames:v 1 "$OUT/overview.png"
rm -rf "$TMP"
ls -la "$OUT"
