"""Report what a stub cut would keep, without cutting.

Iterating on sign conventions by cutting and rendering is slow and destructive. This prints
the measured axis range, the detected boundary, and the extent and vertex count on each side,
so the correct side can be chosen from numbers rather than from a picture.

Run inside Blender:
  blender --background --factory-startup --python _probe_cut.py -- ^
      --input ready\\name\\name.glb --sockets sockets\\name.json --socket SOCK_head
"""
import argparse
import importlib.util
import json
import os
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location(
    "cut", os.path.join(TOOL_DIR, "_blender_cut_stub.py"))
cut = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cut)

import bmesh  # noqa: E402  (must come after the module load above)

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
parser = argparse.ArgumentParser()
parser.add_argument("--input", required=True)
parser.add_argument("--sockets", required=True)
parser.add_argument("--socket", required=True)
args = parser.parse_args(argv)

with open(args.sockets, encoding="utf-8") as handle:
    definition = json.load(handle)
socket_spec = definition["sockets"][args.socket]

cut.reset_scene()
obj = cut.join_meshes(cut.import_glb(args.input))
axis = cut.export_frame_to_blender(socket_spec["primary"]).normalized()
origin = cut.export_frame_to_blender(socket_spec["position"])

profile, low, high = cut.measure_profile(obj, axis, origin)
boundary, drop = cut.find_stub_boundary(profile)

print("PROBE socket=%s" % args.socket)
print("PROBE axis_in_blender=%s origin_in_blender=%s" % (
    [round(v, 4) for v in axis], [round(v, 4) for v in origin]))
print("PROBE envelope=%.4f m  axis range %.4f .. %.4f" % (
    float(socket_spec.get("envelope", 0.0)), low, high))
print("PROBE boundary=%.4f m  radial drop=%.5f m" % (boundary or 0.0, drop))

bm = bmesh.new()
bm.from_mesh(obj.data)
bm.verts.ensure_lookup_table()
below = [(v.co - origin).dot(axis) for v in bm.verts if (v.co - origin).dot(axis) < boundary]
above = [(v.co - origin).dot(axis) for v in bm.verts if (v.co - origin).dot(axis) >= boundary]
bm.free()

def describe(label, values):
    if not values:
        print("PROBE %-6s empty" % label)
        return
    print("PROBE %-6s verts=%-7d span %.4f .. %.4f  (thickness %.4f)" % (
        label, len(values), min(values), max(values), max(values) - min(values)))

describe("below", below)
describe("above", above)
print("PROBE nearer_to_origin=%s" % (
    "below" if (not above or (below and max(abs(v) for v in below) < max(abs(v) for v in above)))
    else "above"))
