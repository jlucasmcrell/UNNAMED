"""Run a local build/conversion/capture while yielding to OWNER_PLAYING.flag.

Example: python tools/run_owner_safe.py --gpu "codex barrowworks capture" -- Godot.exe ...
No game or asset file is modified by this launcher. Exit 75 means owner play interrupted it.
"""
import argparse
from datetime import datetime, timezone
from pathlib import Path
import os
import subprocess
import time

OWNER_FLAG = Path(r'C:\Otherreach_Recovery\OWNER_PLAYING.flag')
GPU_SCRIPT = Path(r'C:\Otherreach_Recovery\gpu_lock.ps1')
GPU_LOCK = GPU_SCRIPT.with_name('gpu.lock')


def stop(process):
    if process.poll() is not None:
        return
    if os.name == 'nt':
        # Only the process we started and its descendants; never the owner's game.
        subprocess.run(['taskkill.exe', '/PID', str(process.pid), '/T', '/F'],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    else:
        process.terminate()
    process.wait()


def run(command, *, owner_flag=OWNER_FLAG, gpu=None, cwd=None):
    if owner_flag.exists():
        print('OWNER_PLAYING: job not started', flush=True)
        return 75
    if gpu is not None and not gpu.startswith('codex '):
        raise ValueError('GPU lease owner must start with "codex "')
    process = None
    try:
        if gpu is not None:
            last = GPU_SCRIPT.with_name('gpu.last_release')
            if last.exists():
                identity, timestamp = last.read_text().strip().split(maxsplit=1)
                if identity == 'codex':
                    age = (datetime.now(timezone.utc) - datetime.fromisoformat(timestamp)).total_seconds()
                    until = time.monotonic() + max(0, min(60, 60 - age))
                    while time.monotonic() < until:
                        if owner_flag.exists():
                            return 75
                        time.sleep(.25)
            subprocess.run(['powershell.exe', '-NoProfile', '-File', str(GPU_SCRIPT), 'acquire', gpu], check=True)
        # The owner can start while the lease request waits in the official queue.
        if owner_flag.exists():
            print('OWNER_PLAYING: job not started after lease wait', flush=True)
            return 75
        flags = (subprocess.CREATE_NO_WINDOW | subprocess.BELOW_NORMAL_PRIORITY_CLASS) if os.name == 'nt' else 0
        process = subprocess.Popen(command, cwd=cwd, creationflags=flags)
        started = time.monotonic()
        while process.poll() is None:
            if owner_flag.exists():
                print('OWNER_PLAYING: stopping this job and releasing its lease', flush=True)
                stop(process)
                return 75
            if gpu is not None:
                queue = GPU_SCRIPT.with_name('gpu.queue')
                limit = 600 if queue.exists() and queue.read_text().strip() else 1200
                if time.monotonic() - started >= limit:
                    print('GPU batch time limit: stopping this job and releasing its lease', flush=True)
                    stop(process)
                    return 124
            time.sleep(.25)
        return process.returncode
    finally:
        if process is not None:
            stop(process)
        if gpu is not None and GPU_LOCK.exists() and GPU_LOCK.read_text(encoding='utf-8-sig').startswith(gpu + ' '):
            subprocess.run(['powershell.exe', '-NoProfile', '-File', str(GPU_SCRIPT), 'release', gpu], check=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--gpu', help='Use the official GPU lease; for example "codex barrowworks capture"')
    parser.add_argument('--cwd', type=Path)
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command:
        parser.error('a command is required after --')
    raise SystemExit(run(command, gpu=args.gpu, cwd=args.cwd))
