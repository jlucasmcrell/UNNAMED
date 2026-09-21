"""Check whether the Z-Image flow sampler node loads against a given ComfyUI tree.

The node registers its samplers by appending to comfy.samplers.KSampler.SAMPLERS at
import time, and a failed import is silent, so "the files are there" proves nothing.
This imports the module against the target tree and reports whether the samplers
actually appeared.

Usage:
    python _check_flow_sampler.py <comfyui_root>
"""
import importlib.util
import os
import sys

root = sys.argv[1] if len(sys.argv) > 1 else r"R:\\"
node_dir = os.path.join(root, "custom_nodes", "ComfyUI-ZImageTurbo-FlowSampler")
node_file = os.path.join(node_dir, "zimage_turbo_core.py")

sys.path.insert(0, root)
print(f"root      : {root}")
print(f"node file : {node_file}  exists={os.path.isfile(node_file)}")
if not os.path.isfile(node_file):
    raise SystemExit(2)

import comfy.samplers as cs

before = list(cs.KSampler.SAMPLERS)
print(f"comfy imported OK; samplers before: {len(before)}")

spec = importlib.util.spec_from_file_location("zitflow", node_file)
module = importlib.util.module_from_spec(spec)
try:
    spec.loader.exec_module(module)
except Exception as exc:
    print(f"NODE MODULE FAILED: {type(exc).__name__}: {exc}")
    raise SystemExit(3)

added = [s for s in cs.KSampler.SAMPLERS if s not in before]
print(f"node module imported OK; samplers after: {len(cs.KSampler.SAMPLERS)}")
print(f"added: {added}")
print(f"euler_flow present: {'euler_flow' in cs.KSampler.SAMPLERS}")
