"""A small ComfyUI API client for the character production pipeline: upload an image, run an API-format graph,
wait for it, fetch its saved images. (The 3D runner's client, _run_3d_asset.py, is built around one template;
this one runs the pipeline's own small graphs.)"""
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

SERVER = os.environ.get("UNNAMED_COMFY_SERVER", "http://127.0.0.1:8190")


def _get(path, timeout=120):
    with urllib.request.urlopen(f"{SERVER}{path}", timeout=timeout) as r:
        return r.read()


def upload(path):
    boundary = uuid.uuid4().hex
    body = b"".join([
        f"--{boundary}\r\n".encode(),
        f'Content-Disposition: form-data; name="image"; filename="{os.path.basename(path)}"\r\n'.encode(),
        b"Content-Type: image/png\r\n\r\n", open(path, "rb").read(), f"\r\n--{boundary}--\r\n".encode()])
    req = urllib.request.Request(f"{SERVER}/upload/image", data=body,
                                 headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    with urllib.request.urlopen(req, timeout=300) as r:
        return json.loads(r.read())["name"]


def run(graph, timeout_s=1800):
    """Queue an API-format graph and wait; returns {node_id: [image bytes, ...]} for every saved image."""
    req = urllib.request.Request(f"{SERVER}/prompt", data=json.dumps({"prompt": graph, "client_id": uuid.uuid4().hex}).encode(),
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=300) as r:
            pid = json.loads(r.read())["prompt_id"]
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"ComfyUI refused the graph: {e.read().decode()[:2000]}")
    start = time.time()
    while time.time() - start < timeout_s:
        h = json.loads(_get(f"/history/{pid}"))
        if pid in h:
            entry = h[pid]
            status = entry.get("status", {})
            if status.get("status_str") != "success":
                raise RuntimeError(f"ComfyUI run failed: {json.dumps(status.get('messages'))[:3000]}")
            out = {}
            for node, o in entry.get("outputs", {}).items():
                for im in o.get("images", []):
                    q = urllib.parse.urlencode({"filename": im["filename"], "subfolder": im.get("subfolder", ""), "type": im.get("type", "output")})
                    out.setdefault(node, []).append(_get(f"/view?{q}", timeout=300))
            return out
        time.sleep(2)
    raise TimeoutError(f"ComfyUI prompt {pid} did not finish in {timeout_s}s")
