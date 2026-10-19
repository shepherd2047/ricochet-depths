"""Run concept images through the community-hosted TRELLIS (v1) HF Space, which loads the official
MIT weights microsoft/TRELLIS-image-large. Usage:
  uv run --with gradio_client python trellis_space.py <out_dir> img1.jpg [img2.jpg ...]
Writes <out_dir>/<image stem>_trellis.glb. Uses the token cached by `hf auth login` if present
(sign in yourself; never paste tokens into chat). Anonymous ZeroGPU quota is small or zero."""
import os, shutil, sys
from gradio_client import Client, handle_file

out_dir, images = sys.argv[1], sys.argv[2:]
os.makedirs(out_dir, exist_ok=True)
c = Client("trellis-community/TRELLIS", verbose=False)
c.predict(api_name="/start_session")
for path in images:
    stem = os.path.splitext(os.path.basename(path))[0]
    img = c.predict(handle_file(path), api_name="/preprocess_image")
    _video, _glb, dl = c.predict(handle_file(img), [], 0, 7.5, 12, 3.0, 12, "stochastic", 0.95, 1024,
                                 api_name="/generate_and_extract_glb")
    dst = os.path.join(out_dir, f"{stem}_trellis.glb")
    shutil.copy(dl, dst)
    print("saved", dst, os.path.getsize(dst) // 1024, "KB", flush=True)
