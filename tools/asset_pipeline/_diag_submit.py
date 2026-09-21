"""Submit one concept graph to a given server and print the raw rejection if any.

Useful when a render is refused with a bare HTTP error: the body carries ComfyUI's
node_errors, which name the exact input it disliked.
"""
import argparse
import importlib.util
import json
import os
import urllib.error
import urllib.request
import uuid

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("mc", os.path.join(TOOL_DIR, "_make_concepts.py"))
mc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mc)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", required=True)
    parser.add_argument("--asset-id", default="diag_test")
    parser.add_argument("--width", type=int, default=832)
    parser.add_argument("--height", type=int, default=1216)
    parser.add_argument("--steps", type=int, default=24)
    args = parser.parse_args()

    mc.SERVER = args.server
    request = {
        "id": args.asset_id,
        "prompt": "a plain iron hand axe on a neutral grey background, three-quarter view",
        "width": args.width,
        "height": args.height,
        "steps": args.steps,
    }
    graph = mc.build_prompt(request, f"diag/{args.asset_id}")
    print(f"submitting {len(graph)} nodes to {args.server}")
    print(json.dumps(graph["5"]["inputs"], indent=2, default=str)[:900])

    payload = json.dumps({"prompt": graph, "client_id": uuid.uuid4().hex}).encode()
    req = urllib.request.Request(f"{args.server}/prompt", data=payload,
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            body = json.loads(resp.read())
        print(f"accepted, prompt_id {body.get('prompt_id')}")
        return 0
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", "replace")
        print(f"HTTP {exc.code}")
        try:
            parsed = json.loads(detail)
            print(json.dumps(parsed, indent=2)[:3000])
        except ValueError:
            print(detail[:3000])
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
