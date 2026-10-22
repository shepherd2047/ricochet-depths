# pit-art gem_xp.glb -> Resources/Models/<mesh>.bytes, one file per mesh, in glb_to_bytes.py's v2 layout (Blender axes:
# x right, -y front, z up; vertex colour sRGB). Plain python, no Blender: the gem file holds several meshes, untextured,
# with the facet colour in COLOR_0 (linear, normalized uint16) and the emissive strength in uv.x.
# Run: python3 Tools/gem_glb_to_bytes.py ../pit-art/models/lowpoly/gem_xp.glb Assets/Resources/Models
import json, struct, sys, os
src, dst = sys.argv[1], sys.argv[2]
b = open(src, 'rb').read()
jl = struct.unpack_from('<I', b, 12)[0]
j = json.loads(b[20:20 + jl])
bin0 = 20 + jl + 8
SIZE = {5126: 4, 5123: 2, 5125: 4, 5121: 1}
FMT = {5126: 'f', 5123: 'H', 5125: 'I', 5121: 'B'}
N = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
def acc(i):
    a = j['accessors'][i]; v = j['bufferViews'][a['bufferView']]
    n, ct = N[a['type']], a['componentType']
    o = bin0 + v.get('byteOffset', 0) + a.get('byteOffset', 0)
    st = v.get('byteStride', n * SIZE[ct])
    return [struct.unpack_from('<%d%s' % (n, FMT[ct]), b, o + k * st) for k in range(a['count'])]
def srgb(x):
    x = max(0.0, min(1.0, x))
    return int(round(255 * (12.92 * x if x <= 0.0031308 else 1.055 * x ** (1 / 2.4) - 0.055)))
for m in j['meshes']:
    p = m['primitives'][0]; at = p['attributes']
    P, Nn, U, C = acc(at['POSITION']), acc(at['NORMAL']), acc(at['TEXCOORD_0']), acc(at['COLOR_0'])
    I = [t[0] for t in acc(p['indices'])]
    with open(os.path.join(dst, m['name'] + '.bytes'), 'wb') as w:
        w.write(struct.pack('<iii', len(P), 1, len(I)))
        for k in range(len(P)):
            (x, y, z), (nx, ny, nz), (u, v), c = P[k], Nn[k], U[k], C[k]
            # glTF y-up -> Blender z-up: (x, -z, y)
            w.write(struct.pack('<8f4B', x, -z, y, nx, -nz, ny, u, 1 - v, *[srgb(q / 65535) for q in c[:3]], 255))
        w.write(struct.pack('<%dH' % len(I), *I))
    print(m['name'], len(P), len(I) // 3)
