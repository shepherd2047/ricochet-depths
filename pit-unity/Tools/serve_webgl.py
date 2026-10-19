# Serves Builds/WebGL locally with the headers Unity's Brotli output needs. Usage: python3 Tools/serve_webgl.py [port]
import http.server, os, sys

class H(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        p = self.path.split("?")[0]
        if p.endswith(".br"):
            self.send_header("Content-Encoding", "br")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def guess_type(self, path):
        base = path[:-3] if path.endswith(".br") else path
        if base.endswith(".wasm"): return "application/wasm"
        if base.endswith(".js"): return "application/javascript"
        if base.endswith(".data"): return "application/octet-stream"
        return super().guess_type(path)

os.chdir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Builds", "WebGL"))
port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
http.server.ThreadingHTTPServer(("127.0.0.1", port), H).serve_forever()
