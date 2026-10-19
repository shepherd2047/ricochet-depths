import json, sys, subprocess, concurrent.futures as cf
bdir, batch, urls, page = sys.argv[1:5]
m = json.load(open(f'{bdir}/upload_map.json')); keys = json.load(open(batch))
ids = [l.strip() for l in open(urls) if l.strip()]
assert len(ids) == len(keys), (len(ids), len(keys))
def post(a):
    k, u = a
    url = f'https://mcp.figma.com/mcp/upload/{u}/submit?scaleMode=FILL&currentPageId={page.replace(":", "%3A")}'
    r = subprocess.run(['curl', '-s', '-X', 'POST', '-H', 'Content-Type: image/png', '--data-binary', f'@{bdir}/img/{m[k][1]}', url], capture_output=True, text=True)
    return k, r.stdout[:200]
out = {}
with cf.ThreadPoolExecutor(8) as ex:
    for k, s in ex.map(post, zip(keys, ids)):
        try: out[k] = json.loads(s).get('imageHash')
        except Exception: out[k] = 'ERR ' + s
bad = {k: v for k, v in out.items() if not v or str(v).startswith('ERR')}
print(len(out) - len(bad), 'ok;', 'bad:', bad)
json.dump(out, open(batch.replace('batch_', 'hashes_'), 'w'))
