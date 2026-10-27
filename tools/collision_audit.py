"""Collision audit (owner notes 4 and 8, BRIEF D4.1), CPU only.

For every blocker the region content declares - the circles and boxes that are supposed to stop a body - this finds the model the art
bindings draw there and compares the two footprints: the model's own XZ bounds out of the GLB, times whatever scale the binding gives it,
against the blocker's declared size. It reports:

  * a model drawn with no blocker to match it (nothing stops the body at all),
  * a blocker much smaller than its model (you run through the visible rock),
  * a model much smaller than its blocker (an invisible wall).

Read-only. Writes a markdown table and a JSON sidecar.
"""
import json
import re
import struct
import sys
from pathlib import Path

ART = Path(r"D:\Otherreach_Scratch\ds_art\primary")
CONTENT = Path(r"D:\Otherreach_Scratch\ds_first_update_wt\content")
OUT = Path(r"C:\Otherreach_Build\collision_audit.md")
SIDE = Path(r"C:\Otherreach_Build\collision_audit.json")

# A blocker within this share of its model is fine: authored models are rarely exactly the size of their collision.
SLACK = 0.15


def glb_bounds(path: Path):
    """XZ extent of a model, from the POSITION accessors' own min/max (no mesh is decoded)."""
    with path.open("rb") as f:
        magic, _v, length = struct.unpack("<4sII", f.read(12))
        if magic != b"glTF":
            raise ValueError("not a glb")
        js = None
        while f.tell() < length:
            chunk_len, chunk_type = struct.unpack("<I4s", f.read(8))
            data = f.read(chunk_len)
            if chunk_type == b"JSON":
                js = json.loads(data.decode("utf-8"))
    if js is None:
        raise ValueError("no JSON chunk")
    lo = [1e9, 1e9, 1e9]
    hi = [-1e9, -1e9, -1e9]
    for mesh in js.get("meshes", []):
        for prim in mesh.get("primitives", []):
            acc = js["accessors"][prim["attributes"]["POSITION"]]
            if "min" not in acc:
                continue
            for i in range(3):
                lo[i] = min(lo[i], acc["min"][i])
                hi[i] = max(hi[i], acc["max"][i])
    if lo[0] > hi[0]:
        return None
    return (hi[0] - lo[0]) * 1000.0, (hi[1] - lo[1]) * 1000.0, (hi[2] - lo[2]) * 1000.0  # mm


def model_bounds(name: str):
    for rel in (f"ready/{name}/{name}.glb", f"rigged/{name}/{name}_rigged.glb"):
        p = ART / rel
        if p.exists():
            return glb_bounds(p)
    return None


def bindings():
    return json.loads((ART / "bindings.json").read_text(encoding="utf-8"))


def scale_of(spec):
    """The uniform-ish scale a binding applies: a number, [x,y,z] or [lo,hi] (authored range - take the top)."""
    for key in ("scale", "size_scale", "uniform_scale"):
        if key in spec:
            value = spec[key]
            if isinstance(value, (int, float)):
                return float(value)
            if isinstance(value, list) and value:
                return float(value[-1]) if len(value) <= 3 else 1.0
    return 1.0


def _unit_scale(block_text: str) -> float:
    """A multi-line list blocker is authored in decimetres (`box_m: - 444 - 746`), the one-line form in metres. Read both."""
    return 0.1 if re.search(r"(circle_m|box_m):\s*\n", block_text) else 1.0


def parse_blockers():
    """Every circle/box blocker the region content declares, by the id it is drawn from.

    These files write an entry as one `- { ... }` line, but the big boxes run onto a second line, and a naive comma split breaks
    `circle_m: [27, 158, 0.8]` into three. So the entry is gathered until its braces balance and then read with one regex per field.
    """
    import re
    out = []
    for region in CONTENT.glob("regions/*.yaml"):
        text = region.read_text(encoding="utf-8")
        for block in re.findall(r"- \{([^}]*)\}", text, re.S):
            flat = " ".join(block.split())
            scale = _unit_scale(block)
            ident = re.search(r"id:\s*([A-Za-z0-9_.]+)", flat)
            if not ident:
                continue
            height = re.search(r"height_m:\s*([0-9.]+)", flat)
            height_m = float(height.group(1)) if height else 0.0
            circle = re.search(r"circle_m:\s*\[([^\]]*)\]", flat)
            box = re.search(r"box_m:\s*\[([^\]]*)\]", flat)
            if circle:
                nums = [float(x) * scale for x in circle.group(1).split(",") if x.strip()]
                radius = nums[2] if len(nums) > 2 else 0.5
                out.append({"id": ident.group(1), "kind": "circle", "radius_m": radius, "width_m": 2 * radius,
                            "height_m": height_m, "region": region.stem})
            elif box:
                nums = [float(x) * scale for x in box.group(1).split(",") if x.strip()]
                if len(nums) == 4:
                    w, d = abs(nums[2] - nums[0]), abs(nums[3] - nums[1])
                else:
                    w = d = 0.0
                out.append({"id": ident.group(1), "kind": "box", "width_m": max(w, d), "height_m": height_m,
                            "region": region.stem})
    return out


def main():
    b = bindings()
    draws = {}
    for section in ("structures", "nodes", "cliffs", "models"):
        table = b.get(section)
        if not isinstance(table, dict):
            continue   # some sections are loose text (cliffs' own note)
        for key, spec in table.items():
            if isinstance(spec, dict) and isinstance(spec.get("model"), str) and spec["model"]:
                draws[key] = {"model": spec["model"], "scale": scale_of(spec), "section": section}
    rows = []
    for blocker in parse_blockers():
        drawn = draws.get(blocker["id"])
        row = dict(blocker)
        if not drawn:
            row["verdict"] = "not art-bound here"
            rows.append(row)
            continue
        bounds = model_bounds(drawn["model"])
        row.update({"model": drawn["model"], "scale": drawn["scale"]})
        if bounds is None:
            row["verdict"] = "model file missing"
            rows.append(row)
            continue
        model_xz = max(bounds[0], bounds[2]) * drawn["scale"] / 1000.0
        row["model_width_m"] = round(model_xz, 2)
        row["model_height_m"] = round(bounds[1] * drawn["scale"] / 1000.0, 2)
        blocker_w = blocker["width_m"]
        if model_xz > blocker_w * (1 + SLACK):
            verdict = "BLOCKER TOO SMALL"
        elif blocker_w > model_xz * (1 + SLACK) * 1.6:
            verdict = "blocker much larger (invisible wall)"
        else:
            verdict = "ok"
        if row["model_height_m"] > blocker["height_m"] * (1 + SLACK) and blocker["height_m"] > 0:
            verdict = "BLOCKER TOO SHORT" if verdict == "ok" else verdict
        row["verdict"] = verdict
        rows.append(row)

    SIDE.write_text(json.dumps(rows, indent=1), encoding="utf-8")
    lines = ["# Collision audit (CPU)", "",
             f"{len(rows)} blockers with a drawn model. Slack allowed: {int(SLACK * 100)}%.", "",
             "| blocker | region | kind | blocker w x h (m) | model | model w x h (m) | verdict |",
             "|---|---|---|---|---|---|---|"]
    for r in sorted(rows, key=lambda r: (r["verdict"] == "ok", r["verdict"])):
        if "model" not in r:
            lines.append(f"| {r['id']} | {r['region']} | {r['kind']} | {r['width_m']:.2f} x {r['height_m']:.1f} | - | - | {r['verdict']} |")
            continue
        lines.append(f"| {r['id']} | {r['region']} | {r['kind']} | {r['width_m']:.2f} x {r['height_m']:.1f} | {r['model']} | "
                     f"{r.get('model_width_m', 0):.2f} x {r.get('model_height_m', 0):.2f} | {r['verdict']} |")
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    flagged = [r for r in rows if r["verdict"] not in ("ok", "not art-bound here")]
    print(f"{len(rows)} blockers, {len(flagged)} flagged -> {OUT}")
    for r in flagged:
        print(f"  {r['verdict']:32s} {r['id']:28s} blocker {r['width_m']:.2f}x{r['height_m']:.1f} "
              f"model {r.get('model_width_m', 0):.2f}x{r.get('model_height_m', 0):.2f} {r['model'] if 'model' in r else '-'}")


if __name__ == "__main__":
    sys.exit(main())
