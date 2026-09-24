"""Hold the SSH tunnel from BEAST to ASTRAL's ComfyUI open, reconnecting when it drops.

A one-shot `ssh -N -L` dies on any connection reset and takes the ASTRAL builder with it: the
client then fails every prompt in about three seconds because the server is unreachable, and a
queue that looks like it is still running quietly burns through its remaining assets.

`_make_assets.py` treats a *starting* server as one to wait for, so a reconnect gap is survivable
as long as the forward comes back. This keeps bringing it back, and logs each drop so a tunnel
that is flapping is visible rather than silent.

Usage:
    python _astral_tunnel.py --log W:\\UNNAMED\\assets\\astral_tunnel.log
"""
import argparse
import datetime
import subprocess
import sys
import time

LOCAL_PORT = 18190
REMOTE = "127.0.0.1:8190"
HOST = "astral"


def stamp():
    return datetime.datetime.now().strftime("%H:%M:%S")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--log", default=r"W:\UNNAMED\assets\astral_tunnel.log")
    parser.add_argument("--local-port", type=int, default=LOCAL_PORT)
    parser.add_argument("--remote", default=REMOTE)
    parser.add_argument("--host", default=HOST)
    args = parser.parse_args()

    attempt = 0
    while True:
        attempt += 1
        command = [
            "ssh", "-o", "BatchMode=yes", "-o", "ExitOnForwardFailure=yes",
            "-o", "ServerAliveInterval=15", "-o", "ServerAliveCountMax=3",
            "-N", "-L", f"{args.local_port}:{args.remote}", args.host,
        ]
        line = f"{stamp()}  connect attempt {attempt} -> localhost:{args.local_port}"
        print(line, flush=True)
        with open(args.log, "a", encoding="utf-8") as handle:
            handle.write(line + "\n")

        # The tunnel is the foreground job; this blocks until ssh exits for any reason.
        result = subprocess.run(command)

        line = f"{stamp()}  tunnel exited (code {result.returncode}); reconnecting in 5s"
        print(line, flush=True)
        with open(args.log, "a", encoding="utf-8") as handle:
            handle.write(line + "\n")
        time.sleep(5)


if __name__ == "__main__":
    sys.exit(main())
