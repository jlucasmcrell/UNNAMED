"""Download model files from HuggingFace with visible progress and resume.

`hf_hub_download` can sit for minutes with no output and no bytes written, which is
indistinguishable from a hang. This streams the file directly so progress is obvious and
an interrupted transfer resumes from where it stopped.

Usage:
    python _fetch_model.py --repo ORG/NAME --file path/inside/repo --dest D:\\models\\kind
"""
import argparse
import os
import sys
import time
import urllib.error
import urllib.request


def fetch(repo, remote, dest, retries=5):
    url = f"https://huggingface.co/{repo}/resolve/main/{remote}"
    name = os.path.basename(remote)
    os.makedirs(dest, exist_ok=True)
    target = os.path.join(dest, name)

    existing = os.path.getsize(target) if os.path.exists(target) else 0
    for attempt in range(1, retries + 1):
        headers = {"User-Agent": "unnamed-asset-pipeline"}
        if existing:
            headers["Range"] = f"bytes={existing}-"
        request = urllib.request.Request(url, headers=headers)
        started = time.time()
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                total = response.headers.get("Content-Length")
                if response.status == 206:
                    total = (int(total) + existing) if total else None
                    mode = "ab"
                else:
                    existing = 0
                    mode = "wb"
                total = int(total) if total else None
                print(f"  {name}: resume from {existing/1e9:.2f} GB"
                      f"{f' of {total/1e9:.2f} GB' if total else ''}", flush=True)

                done = existing
                last = time.time()
                with open(target, mode) as handle:
                    while True:
                        chunk = response.read(1024 * 1024)
                        if not chunk:
                            break
                        handle.write(chunk)
                        done += len(chunk)
                        if time.time() - last >= 10:
                            rate = (done - existing) / max(time.time() - started, 1e-6)
                            pct = f"{100*done/total:.1f}%" if total else "?"
                            print(f"    {done/1e9:.2f} GB  {pct}  {rate/1e6:.1f} MB/s", flush=True)
                            last = time.time()
            if total and os.path.getsize(target) < total:
                raise IOError(f"truncated: {os.path.getsize(target)} of {total}")
            print(f"  done: {name} ({os.path.getsize(target)/1e9:.2f} GB)", flush=True)
            return True
        except (urllib.error.URLError, IOError, TimeoutError) as exc:
            existing = os.path.getsize(target) if os.path.exists(target) else 0
            print(f"  attempt {attempt}/{retries} failed ({type(exc).__name__}: {exc}); "
                  f"have {existing/1e9:.2f} GB", flush=True)
            time.sleep(min(15 * attempt, 60))
    return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True)
    parser.add_argument("--file", action="append", required=True,
                        help="Path inside the repo; repeat for several files")
    parser.add_argument("--dest", required=True)
    args = parser.parse_args()

    ok = True
    for remote in args.file:
        if not fetch(args.repo, remote, args.dest):
            ok = False
    print("DONE" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
