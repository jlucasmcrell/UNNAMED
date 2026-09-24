"""Run PowerShell on a remote host over SSH without quoting hell.

Nested quote escaping through `ssh -> cmd -> powershell -Command` breaks on anything
non-trivial: the first `$_` inside double quotes collapses, `[Math]::Min(...)` gets parsed by
the wrong layer, and the error appears as a syntax complaint from a line you did not write.

PowerShell's `-EncodedCommand` takes base64-encoded UTF-16LE, which passes through every
layer untouched. This wraps that, so remote commands can be written as ordinary PowerShell.

Usage:
    python _remote.py --host astral "Get-Date"
    python _remote.py --host astral --file probe.ps1
    python _remote.py --host astral "nvidia-smi --query-gpu=name --format=csv,noheader" --raw
"""
import argparse
import base64
import subprocess
import sys

DEFAULT_HOST = "astral"


def encode_powershell(script):
    """UTF-16LE base64, which is what -EncodedCommand expects."""
    return base64.b64encode(script.encode("utf-16-le")).decode("ascii")


def run(host, script, timeout=180):
    encoded = encode_powershell(script)
    command = ["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=12", host,
               "powershell", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded]
    result = subprocess.run(command, capture_output=True, text=True, timeout=timeout)
    return result.returncode, (result.stdout or ""), (result.stderr or "")


def run_raw(host, command, timeout=180):
    """For a plain executable, no PowerShell wrapper at all."""
    result = subprocess.run(
        ["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=12", host, command],
        capture_output=True, text=True, timeout=timeout)
    return result.returncode, (result.stdout or ""), (result.stderr or "")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("script", nargs="?", help="PowerShell to run remotely")
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--file", help="Run a local .ps1 on the remote host")
    parser.add_argument("--raw", action="store_true",
                        help="Pass the argument to the remote shell verbatim")
    parser.add_argument("--timeout", type=int, default=180)
    args = parser.parse_args()

    if args.file:
        with open(args.file, encoding="utf-8") as handle:
            script = handle.read()
    elif args.script:
        script = args.script
    else:
        parser.error("give a script or --file")

    if args.raw:
        code, out, err = run_raw(args.host, script, args.timeout)
    else:
        code, out, err = run(args.host, script, args.timeout)

    if out:
        sys.stdout.write(out if out.endswith("\n") else out + "\n")
    if err.strip():
        sys.stderr.write(err if err.endswith("\n") else err + "\n")
    return code


if __name__ == "__main__":
    sys.exit(main())
