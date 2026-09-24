"""Check the ComfyUI server is actually alive, and revive it if it has hung.

A hung ComfyUI is the failure mode that wasted hours: the process stays up and keeps
answering `got prompt`, but execution never progresses, so every asset submitted to it
fails with a client-side timeout that looks like a generation failure. Two signals
distinguish hung from merely busy:

  1. `/system_stats` does not answer inside a short timeout.
  2. ComfyUI's own log has not been written to for a long time, so nothing is running.

A busy server fails the first test but passes the second, which is why both are checked
before acting. Recovery uses `/manager/reboot`, letting the existing guard relaunch with
its own correct flags rather than this script inventing a launch line.

Exit codes: 0 healthy or recovered, 1 still unusable.
"""
import argparse
import os
import sys
import time
import urllib.error
import urllib.request

SERVER = os.environ.get("UNNAMED_COMFY_SERVER", "http://127.0.0.1:8188")
LOG = os.environ.get("UNNAMED_COMFY_LOG", r"C:\Users\jluca\ComfyUI\user\comfyui.log")


def stats(timeout):
    with urllib.request.urlopen(f"{SERVER}/system_stats", timeout=timeout) as response:
        return response.read()


def log_age():
    """Seconds since ComfyUI last wrote to its log, or None if it cannot be read."""
    try:
        return time.time() - os.path.getmtime(LOG)
    except OSError:
        return None


def queue_state(timeout):
    """Return (running, pending) job counts, or None if the queue cannot be read."""
    try:
        with urllib.request.urlopen(f"{SERVER}/queue", timeout=timeout) as response:
            data = json.loads(response.read())
        return len(data.get("queue_running", [])), len(data.get("queue_pending", []))
    except Exception:
        return None


def reboot(timeout):
    request = urllib.request.Request(
        f"{SERVER}/manager/reboot", data=b"{}",
        headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=timeout):
            return True
    except (urllib.error.URLError, OSError, TimeoutError):
        # A hung server usually cannot answer the reboot request either, and that is
        # fine: the request itself is often what makes it drop the port.
        return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--probe-timeout", type=float, default=20.0)
    parser.add_argument("--stale-seconds", type=float, default=900.0,
                        help="Log silence that counts as 'nothing is running'")
    parser.add_argument("--wait", type=float, default=240.0)
    args = parser.parse_args()

    reachable = True
    try:
        stats(args.probe_timeout)
    except Exception as exc:
        reachable = False
        print(f"preflight: no response to /system_stats ({type(exc).__name__})")

    age = log_age()
    if age is not None:
        print(f"preflight: log last written {age:.0f}s ago")

    # An unreachable server is usually one that is STARTING UP, not one that is broken.
    # The memory guard restarts ComfyUI whenever host memory runs away, and a restart takes
    # roughly a minute. Treating that as permanent failure made a caller skip every
    # remaining asset in the batch in about two seconds each: one guard restart at 23:27
    # cost seven assets in a row. So wait for the server to come back before judging it.
    if not reachable:
        deadline = time.time() + args.wait
        while time.time() < deadline:
            time.sleep(5)
            try:
                stats(args.probe_timeout)
                print(f"preflight: server came back after "
                      f"{args.wait - (deadline - time.time()):.0f}s")
                return 0
            except Exception:
                continue
        print(f"preflight: still unreachable after {args.wait:.0f}s of waiting")
        return 1

    # A hung server can still answer /system_stats and /queue instantly while doing no
    # work at all, so responsiveness alone does not prove health. The stronger signal is
    # a queue that claims a running job while the log has gone quiet: work is claimed but
    # nothing is happening. That is the state a wedged unwrap produced, and checking only
    # reachability missed it.
    if reachable:
        state = queue_state(args.probe_timeout)
        if state is None:
            print("preflight: healthy")
            return 0
        running, pending = state
        if running and age is not None and age > args.stale_seconds:
            print(f"preflight: {running} job(s) claimed but the log is silent for "
                  f"{age:.0f}s - treating as hung")
            reachable = False
        else:
            print(f"preflight: healthy (running={running} pending={pending})")
            return 0

    if not reachable and age is not None and age < args.stale_seconds:
        # Unreachable or quiet, but the log moved recently: a heavy operation is probably
        # mid-flight and the HTTP handler is starved. Killing it would lose real work.
        print("preflight: log is recent - leaving it alone")
        return 0

    print("preflight: server appears hung; requesting reboot")
    reboot(args.probe_timeout)
    deadline = time.time() + args.wait
    while time.time() < deadline:
        time.sleep(10)
        try:
            stats(args.probe_timeout)
            state = queue_state(args.probe_timeout)
            if state and state[0] == 0:
                print("preflight: server is back and the queue is clear")
                return 0
        except Exception:
            continue
    print(f"preflight: still unusable after {args.wait:.0f}s")
    return 1


if __name__ == "__main__":
    sys.exit(main())
