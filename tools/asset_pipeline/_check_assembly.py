"""Check that two modular assets' sockets mate legally.

This is the assembly legality test the proof set must satisfy. It works from socket
definitions alone, so the interface contract is validated before any mesh exists, which is
the point of declaring interfaces rather than inferring them.

**Socket position semantics.** A socket's `position` is its location in the asset's own local
frame. When two parts assemble, B is placed so that B's socket origin lands exactly on A's
socket origin. Coincidence is therefore *definitional* - it happens by placement - and is NOT
a legality constraint. An earlier version of this checker compared the two local positions
and demanded they be within insertion depth, which is meaningless: a haft's head socket sits
at the tip of a 1.55 m haft, so every mount on it failed.

What actually decides legality:

  * **axis** - primary vectors oppose for a joint, agree for a continuation
  * **roll**  - secondary vectors agree after removing the 180 degree flip, so the mated
                part cannot arrive rotated about the joint axis
  * **envelope** - declared interface envelopes must be compatible
  * **depth vs inset** - a joint needs a positive insertion depth on at least one side, and
                that depth must not exceed how far the *other* part extends past its own
                socket, or the parts would intersect

Usage:
    python _check_assembly.py --a haft_short --a-socket SOCK_head ^
                              --b mace_head --b-socket SOCK_head
"""
import argparse
import json
import math
import os
import sys

SOCKET_DIR = r"W:\UNNAMED\assets\sockets"


def load(asset_id):
    path = os.path.join(SOCKET_DIR, asset_id + ".json")
    if not os.path.exists(path):
        raise SystemExit(f"no socket definition for {asset_id}")
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def normalise(vector):
    length = math.sqrt(sum(v * v for v in vector))
    return [v / length for v in vector] if length > 1e-12 else list(vector)


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def extent_past_socket(definition, spec):
    """How far the asset's geometry extends beyond its socket, along the mating axis.

    Used to bound insertion depth. Derived from the declared nominal size and the socket's
    own position rather than from geometry, so the check works before a mesh exists.
    """
    nominal = definition.get("nominal_size_m")
    if not nominal:
        return None
    # nominal is [x, y, z] in export frame; the primary axis decides which matters, but a
    # safe bound is the largest declared extent, since a part may extend any way.
    return max(float(v) for v in nominal)


def check(definition_a, socket_a, definition_b, socket_b, angle_tol_deg=1.0):
    spec_a = definition_a["sockets"][socket_a]
    spec_b = definition_b["sockets"][socket_b]
    problems = []
    notes = []

    family_a = spec_a.get("family")
    family_b = spec_b.get("family")
    if family_a != family_b:
        # Not fatal on its own - a Vaskaal grip on a shared blade is exactly this case -
        # but it must be visible, because the envelope check is what makes it safe.
        notes.append(f"cross-family mating: {family_a} with {family_b}")

    primary_a = normalise(spec_a["primary"])
    primary_b = normalise(spec_b["primary"])
    primary_dot = dot(primary_a, primary_b)

    # joint_type is the relationship between the pair. A per-socket 'mate' flag describes
    # only how THAT socket is oriented, so comparing two of them is meaningless - which is
    # how a continuation mechanism ended up "conflicting" with the socket it continues.
    # Preference order: explicit joint_type on either side, else the first socket's mate.
    mate = spec_a.get("joint_type") or spec_b.get("joint_type") \
        or spec_a.get("mate", "antiparallel")
    if spec_a.get("joint_type") and spec_b.get("joint_type") \
            and spec_a["joint_type"] != spec_b["joint_type"]:
        problems.append(f"joint_type differs: {spec_a['joint_type']} vs {spec_b['joint_type']}")

    if mate == "antiparallel":
        deviation = abs(primary_dot + 1.0)
    else:
        deviation = abs(primary_dot - 1.0)
    # acos of the dot gives the angle directly, which is more readable than the deviation.
    angle = math.degrees(math.acos(max(-1.0, min(1.0, primary_dot))))
    if mate == "antiparallel":
        angle = abs(180.0 - angle)
    if angle > angle_tol_deg:
        problems.append(
            f"primary axes off by {angle:.2f} deg (dot {primary_dot:+.4f}, mate={mate})")

    secondary_dot = dot(normalise(spec_a["secondary"]), normalise(spec_b["secondary"]))
    if abs(abs(secondary_dot) - 1.0) > math.sin(math.radians(angle_tol_deg)):
        problems.append(
            f"secondary roll off (dot {secondary_dot:+.4f}); the part would seat rotated")

    env_a = float(spec_a.get("envelope", 0.0) or 0.0)
    env_b = float(spec_b.get("envelope", 0.0) or 0.0)
    if env_a > 0 and env_b > 0 and abs(env_a - env_b) > 0.001:
        problems.append(
            f"envelope mismatch: {env_a*1000:.1f} mm vs {env_b*1000:.1f} mm")

    depth_a = float(spec_a.get("depth", 0.0) or 0.0)
    depth_b = float(spec_b.get("depth", 0.0) or 0.0)
    if mate == "antiparallel" and depth_a <= 0.0 and depth_b <= 0.0:
        problems.append("joint has no insertion depth declared on either side")

    # An insertion deeper than the other part is long would bury the joint inside it.
    for label, definition, spec, depth in (
            ("a", definition_a, spec_a, depth_a), ("b", definition_b, spec_b, depth_b)):
        if depth <= 0:
            continue
        bound = extent_past_socket(definition, spec)
        if bound is not None and depth > bound:
            problems.append(
                f"{label} insertion depth {depth*1000:.1f} mm exceeds that part's "
                f"extent {bound*1000:.1f} mm")

    return {
        "a": f"{definition_a['asset_id']}.{socket_a}",
        "b": f"{definition_b['asset_id']}.{socket_b}",
        "primary_dot": round(primary_dot, 5),
        "angle_deg": round(angle, 3),
        "secondary_dot": round(secondary_dot, 5),
        "problems": problems,
        "notes": notes,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--a", required=True)
    parser.add_argument("--a-socket", required=True)
    parser.add_argument("--b", required=True)
    parser.add_argument("--b-socket", required=True)
    parser.add_argument("--angle-tolerance-deg", type=float, default=1.0)
    args = parser.parse_args()

    result = check(load(args.a), args.a_socket, load(args.b), args.b_socket,
                   args.angle_tolerance_deg)
    print(f"  {result['a']}  <->  {result['b']}")
    print(f"    primary dot   {result['primary_dot']:+.5f}  (off {result['angle_deg']} deg)")
    print(f"    secondary dot {result['secondary_dot']:+.5f}")
    for note in result['notes']:
        print(f"    note: {note}")
    if result["problems"]:
        print("    ILLEGAL:")
        for problem in result["problems"]:
            print(f"      - {problem}")
        return 1
    print("    LEGAL")
    return 0


if __name__ == "__main__":
    sys.exit(main())
