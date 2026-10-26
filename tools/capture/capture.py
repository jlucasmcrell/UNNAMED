"""One owned-window capture. Python stdlib; FFmpeg with gfxcapture; Windows only."""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import time
import uuid


ENCODER_FILTER = "hwdownload,format=bgra,pad=ceil(iw/2)*2:ceil(ih/2)*2,format=yuv420p"
IMMUTABLE = {".glb", ".dds", ".png", ".jpg", ".jpeg", ".webp", ".res", ".wav", ".ogg", ".mp3", ".mp4", ".pck"}


def write_json(path, value):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(value, indent=2), encoding="utf-8")
    temporary.replace(path)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def stage_payload(source, destination):
    """Only binary immutable payload may share inodes. Every manifest/cache index is private."""
    source, destination = source.resolve(), destination.resolve()
    if source == destination or source in destination.parents or destination in source.parents:
        raise ValueError("Payload and output must be disjoint")
    destination.mkdir()
    copied = linked = 0
    manifests = {}
    for root, directories, files in os.walk(source):
        directories[:] = [d for d in directories if d not in {".git", ".godot", "profile", "appdata"}]
        target = destination / Path(root).relative_to(source)
        target.mkdir(exist_ok=True, parents=True)
        for name in files:
            src, dst = Path(root) / name, target / name
            if src.is_symlink():
                raise ValueError(f"Payload symlink refused: {src}")
            if src.suffix.lower() in IMMUTABLE:
                try:
                    os.link(src, dst)
                    linked += 1
                    continue
                except OSError:
                    pass
            shutil.copy2(src, dst)
            copied += 1
            if src.suffix.lower() in {".json", ".yaml", ".yml"}:
                manifests[str(src.relative_to(source))] = digest(dst)
    return {"copied": copied, "linked_immutable": linked, "private_manifests": manifests}


def validate_plan(plan):
    for key in ("exe", "payload", "ffmpeg", "ffprobe", "pwsh", "gpu_lock", "owner_flag"):
        if not Path(plan[key]).is_absolute():
            raise ValueError(f"Absolute path required: {key}")
    for key, low, high in (("x", -100000, 100000), ("z", -100000, 100000),
                           ("yaw", -360, 360), ("pitch", -1.35, 1.2), ("distance", 0.4, 6), ("seconds", 5, 120)):
        value = plan.get(key, 5 if key == "seconds" else None)
        if not isinstance(value, (int, float)) or not math.isfinite(value) or not low <= value <= high:
            raise ValueError(f"Invalid {key}")
    if "_console" in Path(plan["exe"]).stem.lower():
        raise ValueError("Use the GUI executable: console wrappers do not own the game HWND")
    width, height = plan.get("size", [1920, 1080])
    if any(not isinstance(n, int) or not 64 <= n <= 7680 for n in (width, height)):
        raise ValueError("Invalid capture size")


def owned_window(pid, hwnd):
    user = ctypes.WinDLL("user32", use_last_error=True)
    user.IsWindow.argtypes = [wintypes.HWND]
    user.IsWindowVisible.argtypes = [wintypes.HWND]
    user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    actual = wintypes.DWORD()
    rectangle = wintypes.RECT()
    user.GetWindowThreadProcessId(hwnd, ctypes.byref(actual))
    if not user.IsWindow(hwnd) or not user.IsWindowVisible(hwnd) or actual.value != pid:
        raise RuntimeError("Game-ready HWND is not a visible window owned by the launched PID")
    if not user.GetClientRect(hwnd, ctypes.byref(rectangle)) or rectangle.right < 64 or rectangle.bottom < 64:
        raise RuntimeError("Game window has no capturable client area")
    return [rectangle.right, rectangle.bottom]


def run(plan, output):
    validate_plan(plan)
    output = output.resolve()
    output.mkdir(exist_ok=False, parents=True)
    token = str(uuid.uuid4())
    owner = f"capture-{token[:8]} shared-smoke"
    game = encoder = acquire = None
    logs = []
    acquired_at = None
    creationflags = subprocess.CREATE_NO_WINDOW

    def launch(args, name, **kwargs):
        log = (output / f"{name}.stderr").open("wb")
        logs.append(log)
        return subprocess.Popen([str(a) for a in args], stderr=log, creationflags=creationflags, **kwargs)

    def guard():
        if Path(plan["owner_flag"]).exists():
            raise RuntimeError("OWNER_PLAYING flag present; preserving evidence and stopping owned processes")
        if acquired_at is not None and time.monotonic() - acquired_at > 540:
            raise TimeoutError("Owned GPU lease reached 540 seconds")
        if game is not None and game.poll() is not None:
            raise RuntimeError(f"Owned game exited {game.returncode} before capture completed")
        if (output / "failed.json").exists():
            raise RuntimeError((output / "failed.json").read_text())

    def wait_for(predicate, timeout, description):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            guard()
            value = predicate()
            if value:
                return value
            time.sleep(0.1)
        raise TimeoutError(description)

    def signal(name):
        write_json(output / f"{name}.json", {"token": token})

    try:
        guard()
        staging = stage_payload(Path(plan["payload"]), output / "payload")
        write_json(output / "isolation.json", staging)
        payload = output / "payload"
        request = {key: plan[key] for key in ("x", "z", "yaw", "pitch", "distance")}
        request.update(protocol=1, token=token)
        write_json(output / "request.json", request)
        lock_args = [plan["pwsh"], "-NoProfile", "-File", plan["gpu_lock"]]
        acquire = launch(lock_args + ["acquire", owner], "lease", stdout=(output / "lease.log").open("wb"))
        wait_for(lambda: acquire.poll() is not None, plan.get("queue_timeout", 3600), "GPU queue deadline")
        if acquire.returncode:
            raise RuntimeError(f"GPU lease failed: {acquire.returncode}")
        acquired_at = time.monotonic()
        width, height = plan.get("size", [1920, 1080])
        args = [plan["exe"]]
        if plan.get("project"):
            args += ["--path", str(Path(plan["project"]).resolve())]
        args += ["--windowed", "--resolution", f"{width}x{height}", "--",
                 "--capture-session", str(output / "request.json"), "--profile", str(output / "profile"),
                 "--coverage-out", str(output / "coverage"), "--content-root", str(payload / "content"),
                 "--asset-root", str(payload / "primary"), "--asset-fallback", str(payload / "fallback"),
                 "--bindings-overlay", str(payload / "content/_art_bindings.json"),
                 "--scatter-rules", str(payload / "content/_scatter_rules.json"), "--visual", plan["visual"]]
        env = {k: v for k, v in os.environ.items() if not k.startswith(("OTHERREACH_", "UNNAMED_"))}
        env.update(APPDATA=str(output / "appdata"), LOCALAPPDATA=str(output / "localappdata"),
                   OTHERREACH_CHARACTER_ROOT=str(payload / "humgen_characters"))
        for directory in ("appdata", "localappdata", "profile"):
            (output / directory).mkdir()
        game_log = (output / "game.log").open("wb")
        logs.append(game_log)
        game = launch(args, "game", stdout=game_log, env=env, cwd=str(output))
        write_json(output / "launch.json", {"pid": game.pid, "exe": plan["exe"], "exe_sha256": digest(Path(plan["exe"])),
                    "argv": args, "token": token, "utc": time.time(), "plan": plan})
        wait_for(lambda: (output / "gameplay_ready.json").exists(), plan.get("load_timeout", 540), "Game never emitted gameplay_ready")
        ready = json.loads((output / "gameplay_ready.json").read_text())
        if ready["token"] != token or ready["protocol"] != 1 or ready["pid"] != game.pid or ready["loading"]:
            raise RuntimeError("Stale or invalid gameplay-ready signal")
        hwnd = ready["hwnd"]
        client = owned_window(game.pid, hwnd)
        write_json(output / "window.json", {"pid": game.pid, "hwnd": hwnd, "client_size": client, "utc": time.time()})
        progress = output / "encoder.progress"
        encoder = launch([plan["ffmpeg"], "-hide_banner", "-y", "-f", "lavfi", "-i",
                          f"gfxcapture=hwnd={hwnd}:max_framerate=60:capture_cursor=false", "-vf", ENCODER_FILTER,
                          "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-g", "60",
                          "-movflags", "+frag_keyframe+empty_moov+default_base_moof", "-progress", str(progress),
                          str(output / "original_wgc.mp4")], "encoder", stdin=subprocess.PIPE, stdout=subprocess.DEVNULL)

        def first_frame():
            if encoder.poll() is not None:
                raise RuntimeError(f"Encoder exited {encoder.returncode}; see encoder.stderr")
            if not progress.exists() or "frame=" not in progress.read_text():
                return False
            # A complete fragment must decode; a progress counter alone is not frame proof.
            result = subprocess.run([plan["ffmpeg"], "-v", "error", "-y", "-i", str(output / "original_wgc.mp4"),
                                     "-frames:v", "1", str(output / "first_frame.png")], capture_output=True, creationflags=creationflags)
            return result.returncode == 0 and (output / "first_frame.png").exists()

        wait_for(first_frame, 35, "No decodable first WGC frame")
        sample = subprocess.run([plan["ffmpeg"], "-v", "error", "-i", str(output / "first_frame.png"),
                                 "-vf", "scale=32:18,format=gray", "-f", "rawvideo", "-"], capture_output=True, check=True, creationflags=creationflags).stdout
        if len(sample) != 576 or max(sample) - min(sample) < 8 or max(sample) < 12:
            raise RuntimeError("First frame is blank/uniform; inspect first_frame.png")
        owned_window(game.pid, hwnd)
        signal("begin")
        wait_for(lambda: (output / "running.json").exists(), 15, "Game did not acknowledge capture begin")
        started = time.monotonic()
        wait_for(lambda: time.monotonic() - started >= plan.get("seconds", 5), 130, "Clip duration")
        encoder.stdin.write(b"q\n")
        encoder.stdin.flush()
        encoder.wait(timeout=20)
        if encoder.returncode:
            raise RuntimeError(f"Encoder failed {encoder.returncode}")
        signal("stop")
        game.wait(timeout=30)
        if game.returncode:
            raise RuntimeError(f"Game shutdown failed {game.returncode}")
        # Preserve raw pre-roll; deliver the final five seconds without changing recorded frame cadence.
        seconds = plan.get("seconds", 5)
        subprocess.run([plan["ffmpeg"], "-v", "error", "-y", "-sseof", str(-seconds), "-i", str(output / "original_wgc.mp4"),
                        "-an", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-fps_mode", "passthrough",
                        str(output / "clip.mp4")], check=True, creationflags=creationflags)
        probe = json.loads(subprocess.check_output([plan["ffprobe"], "-v", "error", "-count_frames", "-show_streams", "-of", "json", str(output / "clip.mp4")], creationflags=creationflags))
        stream = probe["streams"][0]
        if stream["width"] % 2 or stream["height"] % 2 or float(stream["duration"]) < seconds - 0.25 or int(stream["nb_read_frames"]) < 2:
            raise RuntimeError("Encoded clip dimensions/duration/frame proof failed")
        subprocess.run([plan["ffmpeg"], "-v", "error", "-i", str(output / "clip.mp4"), "-f", "null", "-"], check=True, creationflags=creationflags)
        write_json(output / "COMPLETE.json", {"status": "PASS", "probe": probe, "first_frame_luma_range": [min(sample), max(sample)],
                    "game_exit": game.returncode, "first_frame_sha256": digest(output / "first_frame.png"), "clip_sha256": digest(output / "clip.mp4"),
                    "scope": "Owned-window still + real-time silent video; no audio synchronization claim"})
    except BaseException as error:
        write_json(output / "FAILED.json", {"error": str(error), "type": type(error).__name__})
        raise
    finally:
        if game is not None and game.poll() is None:
            signal("stop")
            try:
                game.wait(timeout=10)
            except subprocess.TimeoutExpired:
                game.kill()  # Only the Popen instance this run launched; no names/global kills.
                game.wait()
        if encoder is not None and encoder.poll() is None:
            encoder.kill()
            encoder.wait()
        if acquire is not None and acquire.poll() is None:
            acquire.kill()
            acquire.wait()  # Its dead queue entry expires under the shared lock protocol.
        lock = Path(plan["gpu_lock"]).parent / "gpu.lock"
        if lock.exists() and lock.read_text().startswith(owner + " "):
            subprocess.run(lock_args + ["release", owner], check=True, creationflags=creationflags)
        for log in logs:
            log.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("plan", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    run(json.loads(args.plan.read_text(encoding="utf-8-sig")), args.output)
