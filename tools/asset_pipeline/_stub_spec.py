"""Standardized sacrificial stub: the fixed interface geometry that makes stub removal
deterministic instead of a cleanup guess.

The proof set established that Trellis reconstructs complete objects well and isolated parts
poorly. The conventional fix is to present a part attached to a stub of its neighbour, then
delete the stub. But "delete the stub" is only reliable if the stub is a known shape in a
known place - otherwise Blender is being asked to guess where the component ends and the
neighbour begins, which is the same unknowable-boundary problem in a new form.

So the stub is standardized:

  * **Geometry**: a plain cylinder, six-sided to 24-sided by length, with NO surface detail,
    material variation or decoration whatsoever. It must be trivially distinguishable from
    the component, which is where all the detail is.
  * **Orientation**: coaxial with the mating socket's `primary` axis, so the cut plane is
    perpendicular to the socket and the removal is a single plane cut.
  * **Length**: one third of the neighbour's own length, minimum 0.06 m, maximum 0.30 m -
    long enough that the generator sees a whole object, short enough that the cut wastes
    little.
  * **Diameter**: exactly the socket's declared envelope, so the stub *is* the interface and
    the surviving component inherits a correctly sized mating face.
  * **Marking**: the concept prompt describes it as a plain undecorated shaft or rod, and
    the concept id carries the `_stub` suffix so the pipeline knows a cut is expected.

Removal is then: cut on the plane perpendicular to `primary`, at the socket's declared
insertion depth from the stub end. Deterministic, repeatable, verifiable.

Usage:
    python _stub_spec.py --sockets sockets\\name.json            # print the stub spec
    python _stub_spec.py --sockets-dir sockets --write-prompts   # emit stub prompts
"""
import argparse
import json
import os
import sys

# Stub length as a share of the neighbour's length, clamped.
LENGTH_SHARE = 1.0 / 3.0
MIN_LENGTH_M = 0.06
MAX_LENGTH_M = 0.30

# Which neighbour a component stubs into, by socket role. The stub extends from the socket
# origin along its primary axis, toward the part this component mates with.
STUB_NEIGHBOUR = {
    "head": "haft",
    "pommel": "haft",
    "grip": "blade",
    "channel_focus": "staff",
    "mechanism": "shaft",
}

# Physical description of the neighbour for the concept prompt. Concrete nouns only: the
# generator responds to objects it has seen, so "a plain ash haft" works and "a mating
# interface" does not. Each is described as PLAIN and UNDECORATED so the stub is trivially
# separable from the detailed component at cut time.
NEIGHBOUR_DESCRIPTION = {
    "haft": "a short plain undecorated straight ash haft of uniform thickness",
    "blade": "a plain straight undecorated steel blade tang",
    "staff": "a plain straight undecorated wooden staff of uniform thickness",
    "shaft": "a plain straight undecorated shaft of uniform thickness",
}

# Sockets with no interface envelope are not mating interfaces - they are layer roots or
# body mounts, which describe where a piece sits relative to a body rather than a physical
# connector. A stub cannot be derived from them, so they are skipped.
STUBBABLE_ROLES = frozenset(STUB_NEIGHBOUR)


def stub_for(definition, socket_name):
    """Compute the standardized stub geometry for a component's mating socket.

    Returns None when the socket is not a physical interface and so has no stub.
    """
    spec = definition["sockets"][socket_name]
    role = spec.get("role", "")
    if role not in STUBBABLE_ROLES:
        return None

    envelope = float(spec.get("envelope", 0.0) or 0.0)
    if envelope <= 0.0:
        return None

    neighbour = STUB_NEIGHBOUR[role]
    nominal = definition.get("nominal_size_m") or [0.1, 0.1, 0.1]
    # A stub is a share of the NEIGHBOUR's length, approximated by this component's largest
    # dimension when the neighbour is longer, which holds for every weapon case here.
    length = min(max(max(nominal) * LENGTH_SHARE, MIN_LENGTH_M), MAX_LENGTH_M)

    return {
        "socket": socket_name,
        "role": role,
        "axis": list(spec["primary"]),
        "envelope_m": round(envelope, 4),
        "stub_length_m": round(length, 4),
        "cut_at_m": round(float(spec.get("depth", 0.0) or 0.0), 4),
        "neighbour": neighbour,
        "neighbour_description": NEIGHBOUR_DESCRIPTION[neighbour],
        "plain": True,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sockets", help="One socket definition JSON")
    parser.add_argument("--sockets-dir", help="Directory of socket definitions")
    parser.add_argument("--write-prompts", action="store_true",
                        help="Write stub prompts for every definition that has one")
    parser.add_argument("--out", default=r"W:\UNNAMED\assets\requests\_stub_prompts.json")
    args = parser.parse_args()

    paths = []
    if args.sockets:
        paths.append(args.sockets)
    elif args.sockets_dir:
        paths = [os.path.join(args.sockets_dir, n)
                 for n in sorted(os.listdir(args.sockets_dir))
                 if n.endswith(".json")]
    else:
        parser.error("give --sockets or --sockets-dir")

    prompts = []
    for path in paths:
        with open(path, encoding="utf-8") as handle:
            definition = json.load(handle)
        asset_id = definition["asset_id"]
        specs = {}
        for socket_name in definition.get("sockets", {}):
            stub = stub_for(definition, socket_name)
            if stub is not None:
                specs[socket_name] = stub
        print(f"  {asset_id}")
        for socket_name, stub in specs.items():
            print(f"    {socket_name:<22} axis {stub['axis']}  "
                  f"env {stub['envelope_m']*1000:.1f} mm  "
                  f"stub {stub['stub_length_m']*1000:.0f} mm  "
                  f"cut at {stub['cut_at_m']*1000:.0f} mm  -> {stub['neighbour']}")
        if args.write_prompts and specs:
            primary = next(iter(specs.values()))
            prompts.append({
                "id": asset_id + "_stub",
                "asset_id": asset_id,
                "stub": primary,
            })

    if args.write_prompts and prompts:
        with open(args.out, "w", encoding="utf-8") as handle:
            json.dump(prompts, handle, indent=2)
        print(f"\n  wrote {len(prompts)} stub specs to {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
