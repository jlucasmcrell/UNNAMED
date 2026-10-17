"""Rewrite one line's text in a batch source file, keeping its key and its format.

A near-duplicate found between two batches is fixed by rewriting one line, never by dropping it: the key is the line's
identity and the cell is its coverage.

    python tools/chatter_pipeline/set_line.py b0006 0074 "Up early enough to hear the village not yet awake."
"""

import sys
from pathlib import Path

import yaml

from assemble import read_source

HERE = Path(__file__).resolve().parent
ORDER = ("speakers", "weather", "events")


def entry(line):
    parts = [f"key: {line['key']}", f"cell: {line['cell']}", f'text: "{line["text"]}"']
    for field in ORDER:
        if line.get(field):
            parts.append(f"{field}: [" + ", ".join(line[field]) + "]")
    if line.get("channel"):
        parts.append(f"channel: {line['channel']}")
    if line.get("canon"):
        parts.append("canon: true")
    return "  - { " + ", ".join(parts) + " }"


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    batch, suffix, text = sys.argv[1], sys.argv[2], sys.argv[3]
    if len(text) > 140:
        raise SystemExit(f"{len(text)} characters; a line is at most 140")
    path = HERE / "batches" / f"{batch}.yaml"
    source = read_source(batch)
    if not any(line["key"].endswith("." + suffix) for line in source["lines"]):
        raise SystemExit(f"{batch} has no line {suffix}")
    for line in source["lines"]:
        if line["key"].endswith("." + suffix):
            line["text"] = text
    out = path.read_text(encoding="utf-8").split("lines:")[0] + "lines:\n"
    current = None
    for line in source["lines"]:
        place = line["cell"].split("-", 1)[0]
        if place != current:
            label = "event variants" if line["key"].endswith(".0289") else place
            out += f"\n  # ---- {label} ----\n"
            current = place
        out += entry(line) + "\n"
    path.write_text(out, encoding="utf-8")
    print(f"{batch}.{suffix}: rewritten")
    return 0


if __name__ == "__main__":
    sys.exit(main())
