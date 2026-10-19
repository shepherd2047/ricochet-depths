#!/bin/zsh
# Decimate every TripoSR enemy in models/raw/ to its budget (art-bible.md §5).
# Smooth normals keep Unity verts ≈ tris (style.md: ≤ 400); facets come from the shader.
cd "${0:A:h}/.."
typeset -A TRIS HEIGHT
TRIS=(shambler 300 mite 200 brute 350 slab 350 spitter 300 lobber 280 mender 300 broodpot 300)
HEIGHT=(shambler 0.9 mite 0.45 brute 0.95 slab 0.6 spitter 1.0 lobber 0.9 mender 0.9 broodpot 0.85)
for key in ${(k)TRIS}; do
  src=models/raw/enemy_${key}_triposr.glb
  [[ -f $src ]] || { echo "skip $key (no $src)"; continue; }
  /opt/homebrew/bin/blender -b -P tools/decimate.py -- --in $src --out models/lowpoly/enemy_${key}.glb \
    --tris ${TRIS[$key]} --tex 256 --quantize 10 --height ${HEIGHT[$key]} --up=-y --front=+x \
    --vrange 0.04,0.30 --accent-vrange 0.55,0.85 --normals smooth \
    --preview models/lowpoly/enemy_${key}_preview.png 2>&1 | grep -E "final|Error|Traceback" | sed "s/^/$key: /"
done
