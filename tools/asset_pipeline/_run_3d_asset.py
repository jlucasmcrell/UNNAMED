"""Run the native Pixal3D/Trellis2 template headlessly and report the exported GLB.

Uploads the input image, submits the workflow to the live server, polls until the
prompt settles, then reports every 3D artifact the run produced.

Usage:
    python _run_3d_asset.py [image_path] [--trellis2] [--seed N] [--faces N]
"""
import argparse
import json
import os
import subprocess
import threading
import time
import urllib.error
import urllib.request
import uuid

SERVER = os.environ.get("UNNAMED_COMFY_SERVER", "http://127.0.0.1:8188")
OUTPUT_DIR = os.environ.get("UNNAMED_COMFY_OUTPUT", r"C:\Users\jluca\ComfyUI\output")
# Ceiling for one asset. The slowest measured is ~17 min, so this is generous while
# still guaranteeing a stuck prompt cannot hang the queue overnight.
# A build at the current quality settings (40k faces, 4096 bakes, 30 steps) can
# legitimately run past 45 minutes, so this ceiling is generous on purpose.
MAX_PROMPT_SECONDS = int(os.environ.get("UNNAMED_MAX_PROMPT_SECONDS", 5400))

# Transfer timeouts for /object_info and /prompt. With 2499 nodes registered, a
# busy server can take minutes to answer /object_info, and a 180s ceiling was
# failing healthy assets with a timeout that looked like a generation failure.
HTTP_TIMEOUT = int(os.environ.get("UNNAMED_HTTP_TIMEOUT", 900))
# The workflow template stays in ComfyUI's own workflows folder; that is the only
# part of this pipeline that belongs to ComfyUI rather than to the game project.
TEMPLATE = os.environ.get(
    "UNNAMED_3D_TEMPLATE",
    r"C:\Users\jluca\ComfyUI\user\default\workflows\3d_pixal3d_trellis2_image_to_model.json")


def get(path):
    with urllib.request.urlopen(f"{SERVER}{path}", timeout=HTTP_TIMEOUT) as response:
        return json.loads(response.read())


def post(path, payload):
    request = urllib.request.Request(
        f"{SERVER}{path}",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=HTTP_TIMEOUT) as response:
        return json.loads(response.read())


def input_specs(info, node_type):
    entry = info[node_type]["input"]
    specs = []
    for section in ("required", "optional"):
        for name, spec in entry.get(section, {}).items():
            specs.append((name, spec))
    return specs


def build_prompt(info, workflow, overrides):
    """Return (prompt, skipped).

    Widgets come from widgets_values_named, which is the frontend's own name->value
    map, so widget order never has to be reconstructed. The name list is filtered
    against the server schema to drop frontend-only entries (control_after_generate,
    upload) and the dotted sub-fields of dynamic combos (sign_mode.qef).
    """
    links = {link[0]: link for link in workflow["links"]}
    prompt = {}
    skipped = []

    for node in workflow["nodes"]:
        node_type = node["type"]
        if node_type in ("Note", "MarkdownNote"):
            skipped.append(node_type)
            continue

        specs = dict(input_specs(info, node_type))
        inputs = {}

        for name, spec in specs.items():
            link_id = None
            for node_input in node.get("inputs", []):
                if node_input.get("name") == name:
                    link_id = node_input.get("link")
                    break
            if link_id is not None and link_id in links:
                source = links[link_id]
                inputs[name] = [str(source[1]), source[2]]

        named = node.get("widgets_values_named") or {}
        for name, value in named.items():
            if "." in name:
                # Sub-field of a dynamic combo. The prompt keys these by their full
                # dotted path; the server's build_nested_inputs splits that path and
                # re-nests the value into the combo dict before execute() runs.
                parent = name.split(".", 1)[0]
                if parent not in specs:
                    continue
                inputs[name] = value
                continue
            if name not in specs:
                continue
            # A widget whose input is linked (the template's "Switch to Trellis2" boolean into its switches) keeps its link: the
            # stored widget value is stale, and taking it silently ran every --trellis2 build as Pixal3D.
            if isinstance(inputs.get(name), list):
                continue
            inputs[name] = value

        for name, value in overrides.get(node["id"], {}).items():
            inputs[name] = value

        prompt[str(node["id"])] = {"class_type": node_type, "inputs": inputs}

    return prompt, skipped


def upload_image(image_path):
    boundary = uuid.uuid4().hex
    filename = os.path.basename(image_path)
    with open(image_path, "rb") as handle:
        content = handle.read()
    body = b"".join([
        f"--{boundary}\r\n".encode(),
        f'Content-Disposition: form-data; name="image"; filename="{filename}"\r\n'.encode(),
        b"Content-Type: image/png\r\n\r\n",
        content,
        f"\r\n--{boundary}--\r\n".encode(),
    ])
    request = urllib.request.Request(
        f"{SERVER}/upload/image",
        data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
    )
    with urllib.request.urlopen(request, timeout=300) as response:
        return json.loads(response.read())


def sample_gpu(samples, stop_event, interval=5.0):
    """Record GPU utilization and host CPU load for the duration of a run.

    CPU load comes from GetSystemTimes deltas rather than spawning a shell per
    sample, which would perturb the measurement it is trying to take.
    """
    import ctypes
    from ctypes import wintypes

    class FILETIME(ctypes.Structure):
        _fields_ = [("dwLowDateTime", wintypes.DWORD), ("dwHighDateTime", wintypes.DWORD)]

    def cpu_times():
        idle, kernel, user = FILETIME(), FILETIME(), FILETIME()
        ctypes.windll.kernel32.GetSystemTimes(
            ctypes.byref(idle), ctypes.byref(kernel), ctypes.byref(user))
        to_int = lambda ft: (ft.dwHighDateTime << 32) | ft.dwLowDateTime
        return to_int(idle), to_int(kernel) + to_int(user)

    prev_idle, prev_total = cpu_times()
    while not stop_event.wait(interval):
        try:
            out = subprocess.run(
                ["nvidia-smi",
                 "--query-gpu=utilization.gpu,utilization.memory,memory.used",
                 "--format=csv,noheader,nounits"],
                capture_output=True, text=True, timeout=10,
            ).stdout.strip().splitlines()[0]
            gpu_util, mem_util, mem_used = (int(x.strip()) for x in out.split(","))

            idle, total = cpu_times()
            d_idle, d_total = idle - prev_idle, total - prev_total
            prev_idle, prev_total = idle, total
            cpu = round(100 * (1 - d_idle / d_total)) if d_total > 0 else 0

            samples.append((gpu_util, mem_util, mem_used, cpu))
        except Exception:
            pass


def report_utilization(samples):
    if not samples:
        print("  (no utilization samples)")
        return
    gpu = [s[0] for s in samples]
    mem = [s[2] for s in samples]
    cpu = [s[3] for s in samples]
    print(f"  GPU util  avg {sum(gpu) / len(gpu):5.1f}%  peak {max(gpu)}%")
    print(f"  VRAM used avg {sum(mem) / len(mem) / 1024:5.2f} GB  peak {max(mem) / 1024:.2f} GB")
    print(f"  CPU util  avg {sum(cpu) / len(cpu):5.1f}%  peak {max(cpu)}%")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("image")
    parser.add_argument("--trellis2", action="store_true")
    parser.add_argument("--seed", type=int, default=None)
    parser.add_argument("--faces", type=int, default=None)
    parser.add_argument("--texture-size", type=int, default=None)
    parser.add_argument("--prefix", default=None)
    parser.add_argument("--dump-only", action="store_true", help="Write API json without running")
    parser.add_argument("--measure", action="store_true", help="Sample GPU/CPU utilization")
    parser.add_argument("--steps", type=int, default=None,
                        help="Override steps on every KSampler. The template uses 12/20/12; "
                             "more steps means more surface definition.")
    parser.add_argument("--bake-resolution", type=int, default=None,
                        help="Resolution for the normal and AO bakes (template default is "
                             "2048 normal, 1024 AO).")
    parser.add_argument("--upsample-resolution", default=None,
                        help="Trellis2 shape upsampling target. This drives the size of "
                             "the intermediate mesh before decimation: at 1536 it can "
                             "produce a 35M-face mesh that exhausts system memory during "
                             "unwrap and hangs the server.")
    parser.add_argument("--ao-samples", type=int, default=None,
                        help="Ambient occlusion samples (template default 64). More means "
                             "smoother AO with less dithering noise.")
    args = parser.parse_args()

    info = get("/object_info")
    with open(TEMPLATE, encoding="utf-8") as handle:
        workflow = json.load(handle)

    overrides = {}

    def add(node_id, **values):
        overrides.setdefault(node_id, {}).update(values)

    for node in workflow["nodes"]:
        node_type = node["type"]
        if node_type == "PrimitiveBoolean":
            add(node["id"], value=bool(args.trellis2))
        elif node_type == "DecimateMesh" and args.faces is not None:
            add(node["id"], target_face_count=args.faces)
        elif node_type == "PrimitiveInt" and args.texture_size is not None:
            # The template's "Texture Resolution" primitive. It is linked into both
            # UnwrapMesh.resolution and BakeTextureFromVoxel.texture_size, so this one
            # value sets the UV atlas and the baked texture together.
            add(node["id"], value=args.texture_size)
        elif node_type in ("BakeNormalMapFromMesh", "BakeAmbientOcclusion"):
            if args.bake_resolution is not None:
                add(node["id"], resolution=args.bake_resolution)
            # Separate branch, not an elif: a single node carries both settings and an
            # elif chain would silently drop the second one.
            if node_type == "BakeAmbientOcclusion" and args.ao_samples is not None:
                add(node["id"], samples=args.ao_samples)
        elif node_type == "Trellis2UpsampleStage" and args.upsample_resolution is not None:
            add(node["id"], target_resolution=str(args.upsample_resolution))
        elif node_type == "Save3DAdvanced" and args.prefix is not None:
            add(node["id"], filename_prefix=args.prefix)
        elif node_type == "KSampler":
            if args.seed is not None:
                add(node["id"], seed=args.seed)
            if args.steps is not None:
                add(node["id"], steps=args.steps)

    prompt, skipped = build_prompt(info, workflow, overrides)

    # Point LoadImage at the uploaded file.
    uploaded = upload_image(args.image)
    print(f"uploaded -> {uploaded['name']}")
    for node in workflow["nodes"]:
        if node["type"] == "LoadImage":
            prompt[str(node["id"])]["inputs"]["image"] = uploaded["name"]

    # Keep the debug dump beside this tool, not in ComfyUI's workflows folder which
    # holds only workflow JSON.
    dump_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_api_prompt.json")
    with open(dump_path, "w", encoding="utf-8") as handle:
        json.dump(prompt, handle, indent=2)
    print(f"api prompt nodes: {len(prompt)}  -> {dump_path}")

    if args.dump_only:
        return

    mode = "Trellis.2" if args.trellis2 else "Pixal3D"
    print(f"mode: {mode}")

    submitted = post("/prompt", {"prompt": prompt, "client_id": uuid.uuid4().hex})
    if submitted.get("node_errors"):
        # Rejected nodes are dropped from the run and the remaining subgraph can
        # still report success, so treat validation errors as fatal here.
        print("node_errors (aborting):")
        print(json.dumps(submitted["node_errors"], indent=2)[:4000])
        return
    prompt_id = submitted["prompt_id"]
    print(f"prompt_id: {prompt_id}")

    samples = []
    stop_event = threading.Event()
    sampler = None
    if args.measure:
        sampler = threading.Thread(target=sample_gpu, args=(samples, stop_event), daemon=True)
        sampler.start()

    started = time.time()
    produced = None

    def _pid():
        """ComfyUI's process id, or None if it cannot be read.

        A restart is otherwise undetectable: the replacement server answers normally, it
        simply has no record of a prompt submitted before it started. Without this the
        caller waits out the full ceiling for an answer that can never come.
        """
        try:
            out = subprocess.run(
                ["powershell", "-NoProfile", "-Command",
                 "Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | "
                 "Where-Object { $_.CommandLine -like '*main.py*' } | "
                 "Sort-Object CreationDate | "
                 "Select-Object -First 1 -ExpandProperty ProcessId"],
                capture_output=True, text=True, timeout=45).stdout.strip()
            return int(out) if out.isdigit() else None
        except (OSError, subprocess.SubprocessError, ValueError):
            return None

    pid_at_start = _pid()
    missing_for = 0

    while produced is None:
        # A restart wipes ComfyUI's prompt history, so a prompt submitted before the
        # restart never appears and this loop would wait for ever. Give up after a
        # generous ceiling and fail cleanly so the batch can carry on.
        if time.time() - started > MAX_PROMPT_SECONDS:
            print(f"giving up after {MAX_PROMPT_SECONDS}s: prompt history for "
                  f"{prompt_id} never appeared (server restarted?)")
            stop_event.set()
            return

        # Failing fast on a detected restart matters because the watchdog deliberately
        # restarts a stalled server mid-prompt. Waiting the full ceiling would burn 90
        # minutes per stall for a result that was lost the moment the server went down.
        if pid_at_start is not None:
            current = _pid()
            if current is None:
                # The server process is gone, not restarted. Some inputs (an empty shape
                # latent) kill ComfyUI outright rather than failing one prompt, and the
                # process id never comes back as a different value for the check below to
                # notice. Without this the client polls for the full ceiling while the
                # guard quietly relaunches the server, and the queue looks idle.
                missing_for += 15
                if missing_for >= 180:
                    print("  ComfyUI is gone; the prompt died with it, abandoning it")
                    stop_event.set()
                    return
            else:
                missing_for = 0
                if current != pid_at_start:
                    print(f"  ComfyUI restarted (pid {pid_at_start} -> {current}); this "
                          f"prompt was lost, abandoning it")
                    stop_event.set()
                    return

        try:
            history = get(f"/history/{prompt_id}")
        except urllib.error.URLError as exc:
            print(f"  server unreachable ({exc}); retrying", flush=True)
            time.sleep(15)
            continue

        if prompt_id in history:
            entry = history[prompt_id]
            status = entry.get("status", {})
            elapsed = time.time() - started
            print(f"final status: {status.get('status_str')}  ({elapsed:.0f}s)")

            # A 'success' status only means no node raised. Report how much of the
            # graph actually produced output so a silently dropped branch is visible.
            produced = entry.get("outputs") or {}
            print(f"nodes reporting output: {len(produced)} of {len(prompt)}")

            for message in status.get("messages", []):
                if message[0] in ("execution_error", "execution_interrupted"):
                    print(json.dumps(message[1], indent=2)[:4000])
            break

        queue = get("/queue")
        elapsed = time.time() - started
        print(f"  ...{elapsed:5.0f}s running={len(queue.get('queue_running', []))} "
              f"pending={len(queue.get('queue_pending', []))}", flush=True)
        time.sleep(15)

    if produced is None:
        print("no history recorded for this prompt; treating as failed")
        return

    stop_event.set()
    if sampler is not None:
        sampler.join(timeout=15)
        report_utilization(samples)

    print("\n=== artifacts ===")
    for node_id, output in sorted(produced.items(), key=lambda kv: int(kv[0])):
        for item in output.get("result") or []:
            if not isinstance(item, str) or not item:
                continue
            # Save3DAdvanced reports its file as a path relative to the output dir.
            # Preview3DAdvanced reports a transient preview temp file; skip those.
            candidate = os.path.normpath(os.path.join(OUTPUT_DIR, item))
            if os.path.exists(candidate):
                print(f"ARTIFACT {candidate} {os.path.getsize(candidate)}")


if __name__ == "__main__":
    main()
