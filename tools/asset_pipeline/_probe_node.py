"""Submit a minimal graph and report exactly what each node returned.

Some ComfyUI nodes fail inside KSampler with an opaque error because an upstream node
produced something structurally unexpected. This runs a graph up to (but not including)
the failing node and dumps the actual Python type and shape of each output.

Usage:
    python _probe_node.py --node EmptyAceStep1.5LatentAudio
"""
import argparse
import json
import sys
import time
import urllib.error
import urllib.request
import uuid

SERVER = "http://RAZER:8188"
HTTP_TIMEOUT = 600


def get(path):
    with urllib.request.urlopen(f"{SERVER}{path}", timeout=HTTP_TIMEOUT) as response:
        return json.loads(response.read())


def post(path, payload):
    request = urllib.request.Request(
        f"{SERVER}{path}", data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=HTTP_TIMEOUT) as response:
        return json.loads(response.read())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--node", required=True)
    parser.add_argument("--seconds", type=float, default=5.0)
    args = parser.parse_args()

    info = get(f"/object_info/{args.node}")[args.node]
    out_types = info.get("output")
    print(f"{args.node}: output types = {out_types}")

    # Feed every required input with a plausible literal so the node can execute alone.
    inputs = {}
    for field, cfg in info["input"].get("required", {}).items():
        kind = cfg[0]
        if isinstance(kind, list):
            inputs[field] = kind[0]
        elif kind == "FLOAT":
            inputs[field] = args.seconds if field == "seconds" else 1.0
        elif kind == "INT":
            inputs[field] = 1
        elif kind == "STRING":
            inputs[field] = ""
        elif kind == "BOOLEAN":
            inputs[field] = False
        else:
            print(f"  skipping {field}: needs a {kind} connection")
            return 2
    print(f"  literal inputs: {inputs}")

    graph = {"1": {"class_type": args.node, "inputs": inputs}}
    submitted = post("/prompt", {"prompt": graph, "client_id": uuid.uuid4().hex})
    if submitted.get("node_errors"):
        print("validation failed:", json.dumps(submitted["node_errors"])[:1500])
        return 1

    prompt_id = submitted["prompt_id"]
    for _ in range(60):
        history = get(f"/history/{prompt_id}")
        if prompt_id in history:
            entry = history[prompt_id]
            status = entry.get("status", {})
            print(f"  status: {status.get('status_str')}")
            outputs = entry.get("outputs", {}).get("1", {})
            print(f"  reported outputs: { {k: (v if not isinstance(v, list) else f'list[{len(v)}]') for k, v in outputs.items()} }")
            preview = entry.get("outputs", {}).get("1", {}).get("result")
            if preview is not None:
                print(f"  result: {preview}")
            if status.get("messages"):
                for m in status["messages"]:
                    if m[0] == "execution_error":
                        print("  error:", json.dumps(m[1], indent=2)[:1500])
            return 0
        time.sleep(2)
    print("timed out")
    return 1


if __name__ == "__main__":
    sys.exit(main())
