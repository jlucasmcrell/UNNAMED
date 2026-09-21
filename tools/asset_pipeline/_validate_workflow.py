"""Validate a UI workflow JSON against the live server schemas.

Catches the failure modes that matter before opening a workflow: unknown node
types, links pointing at wrong slot indices, and widget values that do not line up
with the node's declared widget inputs.

Usage:
    python _validate_workflow.py path\\to\\workflow.json
"""
import json
import sys
import urllib.request

SERVER = "http://127.0.0.1:8188"

LINK_TYPES = {
    "MODEL", "CLIP", "VAE", "CONDITIONING", "LATENT", "IMAGE", "MASK",
    "ZIMAGE_CONFIG", "ZIMAGE_OPTIONS", "MESH", "VOXEL",
}
WIDGET_TYPES = {"INT", "FLOAT", "STRING", "BOOLEAN", "COMBO", "COLOR", "LOAD_3D"}

# The frontend adds a control_after_generate widget directly after a seed widget,
# and it takes a slot in widgets_values without appearing in the node schema.
CONTROL_VALUES = {"fixed", "increment", "decrement", "randomize"}


def aligned_values(specs, values):
    """Pair schema widgets with values, accounting for control_after_generate."""
    pairs = []
    index = 0
    for name, kind in specs:
        if index >= len(values):
            break
        pairs.append(((name, kind), values[index]))
        index += 1
        if kind == "INT" and name == "seed" and index < len(values):
            if isinstance(values[index], str) and values[index] in CONTROL_VALUES:
                index += 1
    return pairs


# Note nodes exist only in the frontend and never reach the server.
FRONTEND_ONLY = {"Note", "MarkdownNote"}


def load_info():
    with urllib.request.urlopen(f"{SERVER}/object_info", timeout=180) as response:
        return json.loads(response.read())


def schema_inputs(info, node_type):
    entry = info[node_type]["input"]
    ordered = []
    for section in ("required", "optional"):
        for name, spec in entry.get(section, {}).items():
            kind = spec[0] if isinstance(spec, list) and spec else spec
            ordered.append((name, kind, section))
    return ordered


def main():
    path = sys.argv[1]
    with open(path, encoding="utf-8") as handle:
        workflow = json.load(handle)
    info = load_info()

    problems = []
    node_by_id = {node["id"]: node for node in workflow["nodes"]}

    for node in workflow["nodes"]:
        node_type = node["type"]
        if node_type in FRONTEND_ONLY:
            continue
        if node_type not in info:
            problems.append(f"node {node['id']}: unknown type {node_type}")
            continue

        specs = schema_inputs(info, node_type)
        required_widgets = [(name, kind) for name, kind, section in specs
                            if section == "required"
                            and (isinstance(kind, list) or kind in WIDGET_TYPES)]
        widget_specs = [(name, kind) for name, kind, _ in specs
                        if isinstance(kind, list) or kind in WIDGET_TYPES]
        required_count = len(required_widgets)
        values = node.get("widgets_values") or []

        # Optional widgets may be omitted, so only a shortfall against the required
        # widgets is a defect.
        if len(values) < required_count:
            problems.append(
                f"node {node['id']} ({node_type}): {len(values)} widget values "
                f"but {required_count} are required "
                f"{[name for name, _ in required_widgets]}")

        # Every declared input must either have a link or be a widget.
        declared = {name for name, _, _ in specs}
        for node_input in node.get("inputs", []):
            if node_input["name"] not in declared:
                problems.append(f"node {node['id']} ({node_type}): input "
                                f"{node_input['name']!r} not in schema")

        # Enum-valued combos must hold a value the node actually offers.
        for (name, kind), value in aligned_values(widget_specs, values):
            if isinstance(kind, list) and isinstance(value, str) and value not in kind:
                problems.append(f"node {node['id']} ({node_type}): {name}={value!r} "
                                f"not one of {kind[:6]}{'...' if len(kind) > 6 else ''}")

    # Links must reference real nodes and real slot indices.
    for link in workflow.get("links", []):
        link_id, from_id, from_slot, to_id, to_slot = link[:5]
        if from_id not in node_by_id:
            problems.append(f"link {link_id}: source node {from_id} missing")
            continue
        if to_id not in node_by_id:
            problems.append(f"link {link_id}: target node {to_id} missing")
            continue
        source_outputs = node_by_id[from_id].get("outputs", [])
        if from_slot >= len(source_outputs):
            problems.append(f"link {link_id}: {node_by_id[from_id]['type']} has no "
                            f"output slot {from_slot}")
        target_inputs = node_by_id[to_id].get("inputs", [])
        if to_slot >= len(target_inputs):
            problems.append(f"link {link_id}: {node_by_id[to_id]['type']} has no "
                            f"input slot {to_slot}")
        else:
            wanted = target_inputs[to_slot]["name"]
            if wanted not in {name for name, _, _ in schema_inputs(info, node_by_id[to_id]['type'])}:
                problems.append(f"link {link_id}: target input {wanted!r} not in schema")

    print(f"{path}")
    print(f"  nodes {len(workflow['nodes'])}  links {len(workflow.get('links', []))}")
    if problems:
        print(f"  PROBLEMS ({len(problems)}):")
        for problem in problems:
            print(f"    - {problem}")
        return 1
    print("  VALID")
    return 0


if __name__ == "__main__":
    sys.exit(main())
