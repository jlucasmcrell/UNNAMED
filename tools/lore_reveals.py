"""A page that tells you of a place puts it on the map (spec 41; knowledge, not travel).

The entry already names the place in its visited source; any other page found by being told or by reading something written adds the place to
the map as told. Idempotent: a page that already says so is left alone.
"""

import pathlib

import yaml

LORE = pathlib.Path(r"C:\Otherreach_Build\ds_lore_wt\content\lore")
written = 0
for path in sorted(LORE.glob("*.yaml")):
    if path.name.startswith("_"):
        continue
    doc = yaml.safe_load(path.read_text(encoding="utf-8"))
    places = [s["ref"] for page in doc.get("pages") or [] for s in page.get("sources") or []
              if s.get("how") == "visited" and s["ref"].startswith("location.")]
    if not places:
        continue
    place = places[0]
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    changed = False
    for index, line in enumerate(lines):
        if line.strip() != f"- {{ how: told," and "how: told" not in line and "how: inspected" not in line:
            continue
        # The page this source belongs to is the nearest "- page:" line above it.
        page_line = max(i for i in range(index) if lines[i].strip().startswith("- page:"))
        if any(lines[i].strip().startswith("reveals:") for i in range(page_line, index)):
            continue
        lines.insert(index - 1, f'    reveals: {place}\n')
        changed = True
        written += 1
        break
    if changed:
        path.write_text("".join(lines), encoding="utf-8")
        print("reveals", doc["id"], "->", place)
print("wrote", written)
