"""Run one concept image through the public microsoft/TRELLIS.2 HF Space (MIT).
Usage: python trellis2_space.py <image> <out.glb> [--res 512|1024] [--hf-token TOKEN]
Anonymous ZeroGPU quota is often 0 (shared IPs). Sign in yourself first: `hf auth login`
(free account; the token stays in ~/.cache/huggingface) or export HF_TOKEN. Never paste tokens into chat."""
import argparse, os, shutil
from gradio_client import Client, handle_file

ap = argparse.ArgumentParser()
ap.add_argument("image"); ap.add_argument("out")
ap.add_argument("--res", default="512"); ap.add_argument("--seed", type=int, default=0)
ap.add_argument("--faces", type=int, default=100000, help="Space-side decimation target")
ap.add_argument("--tex", type=int, default=1024)
ap.add_argument("--hf-token", default=os.environ.get("HF_TOKEN"),
                help="defaults to $HF_TOKEN; otherwise the token cached by `hf auth login` is used")
a = ap.parse_args()

c = Client("microsoft/TRELLIS.2", **({"token": a.hf_token} if a.hf_token else {}), verbose=False)
c.predict(api_name="/start_session")
img = c.predict(handle_file(a.image), api_name="/preprocess_image")
c.predict(handle_file(img["path"] if isinstance(img, dict) else img), a.seed, a.res, api_name="/image_to_3d")
glb, dl = c.predict(a.faces, a.tex, api_name="/extract_glb")
shutil.copy(dl or glb, a.out)
print("saved", a.out)
