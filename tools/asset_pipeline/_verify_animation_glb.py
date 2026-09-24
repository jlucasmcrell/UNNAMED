"""Verify an animation-only GLB against its registered clip contract.

Section 6 requires loop validation, root behaviour and a structural check on every clip, and
section 27 lists the animation checks the playable manifest depends on. The animation document
fixes none of the numeric tolerances, so the ones used here are declared explicitly rather than
inherited from nowhere.

What it checks, and why each one is here:

  * animation-only     - zero meshes. A clip GLB that carries geometry silently doubles the
                         character's mesh count when it is loaded alongside the body.
  * targets exist      - every animated node name must be a bone in the declared skeleton family,
                         or the clip plays against nothing.
  * no NaN / inf       - a single NaN keyframe makes a whole rig disappear in engine.
  * duration           - the baked span must match the declared `duration_s`, because gameplay
                         timing is authored against the declared number.
  * loop seam          - for `loop: true` clips, first and last keyframe must agree. This is the
                         check that a looping walk does not visibly hitch once per cycle.
  * root behaviour     - `root_motion: false` clips must not translate the root, or the character
                         drifts across the world while standing still.

Usage:
    python _verify_animation_glb.py --clip assets/animation/clips/<id>.json --glb <path.glb>
    python _verify_animation_glb.py --all
"""
import argparse
import io
import json
import math
import os
import struct
import sys

ASSETS = r"W:\UNNAMED\assets"
CLIPS = os.path.join(ASSETS, "animation", "clips")
READY = os.path.join(ASSETS, "animation", "ready")
REGISTRY = r"W:\UNNAMED\tools\asset_pipeline\_animation_registry.py"

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
COMPONENT = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2),
             5125: ("I", 4), 5126: ("f", 4)}
TYPE_N = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}

# Declared tolerances. The design document leaves these open, so they are chosen here and stated
# so a later change is visible rather than buried.
DURATION_TOLERANCE_S = 0.02
LOOP_SEAM_TOLERANCE = 1e-3      # radians / metres
ROOT_TRANSLATION_TOLERANCE = 1e-4


def load_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    magic, _version, length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        raise ValueError("not a GLB")
    offset, gltf, binary = 12, None, None
    while offset < length:
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        payload = data[offset:offset + chunk_length]
        if chunk_type == JSON_CHUNK:
            gltf = json.loads(payload.decode("utf-8"))
        elif chunk_type == BIN_CHUNK:
            binary = payload
        offset += chunk_length
    return gltf, binary


def accessor_values(gltf, binary, index):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    fmt, size = COMPONENT[acc["componentType"]]
    n = TYPE_N[acc["type"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride") or n * size
    return [struct.unpack_from("<" + fmt * n, binary, start + i * stride)
            for i in range(acc["count"])]


def skeleton_family_bones(family):
    """Bone names for a family, read from the built skeleton contract rather than duplicated."""
    candidates = {
        "humanoid_standard": os.path.join(ASSETS, "rigs", "humanoid_standard",
                                          "humanoid_standard_body_skeleton.json"),
    }
    path = candidates.get(family)
    if not path or not os.path.exists(path):
        return None
    contract = json.load(io.open(path, encoding="utf-8"))
    return {b["name"] for b in contract["bones"]}


def check(clip_path, glb_path):
    clip = json.load(io.open(clip_path, encoding="utf-8"))
    problems = []
    facts = {}

    if not os.path.exists(glb_path):
        return ["glb missing"], {}

    gltf, binary = load_glb(glb_path)
    animations = gltf.get("animations", [])
    if not animations:
        return ["no animations in the GLB"], {}
    animation = animations[0]

    mesh_count = len(gltf.get("meshes", []))
    facts["meshes"] = mesh_count
    facts["skins"] = len(gltf.get("skins", []))
    facts["channels"] = len(animation.get("channels", []))
    if mesh_count != 0:
        problems.append(f"not animation-only: carries {mesh_count} mesh(es)")

    names = [n.get("name", "") for n in gltf.get("nodes", [])]
    bones = skeleton_family_bones(clip["skeleton_family"])

    targets = set()
    duration = 0.0
    nan_channels = []
    for channel in animation.get("channels", []):
        target = channel["target"]
        node = target.get("node")
        if node is None or node >= len(names):
            problems.append("channel targets a node that does not exist")
            continue
        node_name = names[node]
        targets.add(node_name)
        if bones is not None and node_name not in bones:
            problems.append(f"channel targets '{node_name}', not a bone of "
                            f"{clip['skeleton_family']}")
        sampler = animation["samplers"][channel["sampler"]]
        times = [t[0] for t in accessor_values(gltf, binary, sampler["input"])]
        values = accessor_values(gltf, binary, sampler["output"])
        if times:
            duration = max(duration, max(times))
        for row in values:
            if any(math.isnan(v) or math.isinf(v) for v in row):
                nan_channels.append(node_name)
                break

    if nan_channels:
        problems.append(f"NaN or infinite keyframes on: {sorted(set(nan_channels))[:5]}")

    declared = float(clip["duration_s"])
    facts["duration_actual_s"] = round(duration, 4)
    facts["duration_declared_s"] = declared
    if abs(duration - declared) > DURATION_TOLERANCE_S:
        problems.append(f"baked {duration:.4f} s against declared {declared:.4f} s")

    facts["targets"] = len(targets)
    facts["bones_keyed"] = sorted(targets)

    # Loop seam: first and last keyframe of every channel must agree.
    if clip.get("loop"):
        worst = 0.0
        worst_channel = None
        for channel in animation.get("channels", []):
            sampler = animation["samplers"][channel["sampler"]]
            values = accessor_values(gltf, binary, sampler["output"])
            if len(values) < 2:
                continue
            first, last = values[0], values[-1]
            if channel["target"]["path"] == "rotation" and len(first) == 4:
                # A quaternion and its negation are the SAME rotation, so comparing components
                # directly reports a seam of ~2.0 on a clip that loops perfectly. Compare the
                # rotation instead: the angle between the two, via |dot|.
                dot = abs(sum(a * b for a, b in zip(first, last)))
                delta = 2.0 * math.acos(min(dot, 1.0))
            else:
                delta = max(abs(a - b) for a, b in zip(first, last))
            if delta > worst:
                worst, worst_channel = delta, names[channel["target"]["node"]]
        facts["loop_seam_max_delta"] = round(worst, 6)
        if worst > LOOP_SEAM_TOLERANCE:
            problems.append(f"loop seam breaks on '{worst_channel}' by {worst:.5f} "
                            f"(tolerance {LOOP_SEAM_TOLERANCE})")

    # Root behaviour: a non-root-motion clip must not translate the root.
    if not clip.get("root_motion"):
        for channel in animation.get("channels", []):
            node_name = names[channel["target"]["node"]]
            if node_name not in ("root", "pelvis"):
                continue
            if channel["target"]["path"] != "translation":
                continue
            sampler = animation["samplers"][channel["sampler"]]
            values = accessor_values(gltf, binary, sampler["output"])
            if not values:
                continue
            base = values[0]
            drift = max(max(abs(v[i] - base[i]) for i in range(3)) for v in values)
            facts[f"{node_name}_translation_range_m"] = round(drift, 5)
            if drift > ROOT_TRANSLATION_TOLERANCE:
                # A vertical bob on the pelvis is expected and harmless for locomotion; forward
                # drift is not, because it moves the character while gameplay thinks it is still.
                forward = max(abs(v[2] - base[2]) for v in values)
                if forward > ROOT_TRANSLATION_TOLERANCE:
                    problems.append(
                        f"root_motion is false but '{node_name}' translates {drift:.4f} m "
                        "(forward drift), so the character would slide")

    return problems, facts


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--clip")
    parser.add_argument("--glb")
    parser.add_argument("--all", action="store_true")
    args = parser.parse_args()

    pairs = []
    if args.all:
        for name in sorted(os.listdir(CLIPS)):
            if not name.endswith(".json"):
                continue
            clip_path = os.path.join(CLIPS, name)
            clip = json.load(io.open(clip_path, encoding="utf-8"))
            # Player clips live under humanoid/, creature clips under creatures/. Search both
            # rather than assuming, so a new clip family does not silently go unverified.
            glb = None
            for family_dir in ("humanoid", "creatures", "mechanical"):
                candidate = os.path.join(READY, family_dir, clip["animation_id"] + ".glb")
                if os.path.exists(candidate):
                    glb = candidate
                    break
            if glb is None:
                glb = os.path.join(READY, "humanoid", clip["animation_id"] + ".glb")
            pairs.append((clip_path, glb))
    elif args.clip and args.glb:
        pairs.append((args.clip, args.glb))
    else:
        print("  give --clip and --glb, or --all")
        return 1

    failed = 0
    for clip_path, glb in pairs:
        clip = json.load(io.open(clip_path, encoding="utf-8"))
        problems, facts = check(clip_path, glb)
        status = "OK  " if not problems else "FAIL"
        print(f"  {status} {clip['animation_id']}")
        if facts:
            print(f"        meshes={facts.get('meshes')} skins={facts.get('skins')} "
                  f"channels={facts.get('channels')} bones={facts.get('targets')} "
                  f"duration={facts.get('duration_actual_s')}s "
                  f"loop_delta={facts.get('loop_seam_max_delta', 'n/a')}")
        for problem in problems:
            print(f"        {problem}")
        if problems:
            failed += 1

    print(f"\n  {len(pairs) - failed}/{len(pairs)} clips verified")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
