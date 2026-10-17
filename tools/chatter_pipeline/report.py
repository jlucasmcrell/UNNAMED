"""Line counts by tag cell and by tag, for the corpus in content/chatter.

    python tools/chatter_pipeline/report.py            # every batch
    python tools/chatter_pipeline/report.py b0001      # one batch, cell by cell
"""

import collections
import sys
from pathlib import Path

import yaml

CONTENT = Path("content")
CHATTER = CONTENT / "chatter"


def read(batch=None):
    lines = []
    for path in sorted(CHATTER.glob("*.yaml")):
        if path.name.startswith("_"):
            continue
        doc = yaml.safe_load(path.read_text(encoding="utf-8"))
        if doc.get("role") != "corpus" or (batch and doc.get("batch") != batch):
            continue
        for row in doc.get("lines") or []:
            lines.append((doc["batch"], row))
    return lines


def cell_of(row):
    places = row.get("places") or []
    times = row.get("times") or []
    return (places[0] if len(places) == 1 else "many"), (times[0] if len(times) == 1 else "many")


def main():
    batch = sys.argv[1] if len(sys.argv) > 1 else None
    lines = read(batch)
    print(f"{len(lines)} corpus lines")
    for field in ("speakers", "places", "times", "weather", "events"):
        counts = collections.Counter()
        for _, row in lines:
            for value in row.get(field) or ["none"]:
                counts[value] += 1
        if counts:
            print(f"  {field:<9} " + ", ".join(f"{value} {count}" for value, count in sorted(counts.items())))
    channels = collections.Counter(row.get("channel", "local") for _, row in lines)
    print(f"  channel   " + ", ".join(f"{value} {count}" for value, count in sorted(channels.items())))
    cells = collections.Counter(cell_of(row) for _, row in lines)
    print(f"  {len(cells)} cells")
    for (place, band), count in sorted(cells.items()):
        always = sum(1 for _, row in lines
                     if cell_of(row) == (place, band) and not (row.get("events") or []))
        print(f"    {place:<14} {band:<10} {count:>3} lines, {always:>3} always sayable")
    return 0


if __name__ == "__main__":
    sys.exit(main())
