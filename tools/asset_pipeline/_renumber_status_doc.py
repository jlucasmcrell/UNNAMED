"""Renumber the status doc's headings into a single clean sequence.

Headings were added by several rounds of edits, which left a duplicate section number. Numbering is
rewritten from the document order rather than patched by hand, so the sequence cannot drift again.

Usage:
    python _renumber_status_doc.py --audit
    python _renumber_status_doc.py --apply
"""
import argparse
import io
import os
import re
import shutil

DOC = r"W:\UNNAMED\docs\PHASE1_BIBLE_ASSET_SPRINT_STATUS.md"
BACKUP = r"W:\UNNAMED\assets\_superseded\PHASE1_BIBLE_ASSET_SPRINT_STATUS.md.bak"

# Headings that must not be numbered: the front matter sections and the trailing ownership note.
UNNUMBERED = {"Ownership"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(DOC, encoding="utf-8") as handle:
        lines = handle.read().splitlines()

    counter = 0
    changes = []
    for index, line in enumerate(lines):
        match = re.match(r"^## (?:\d+\.\s+)?(.+)$", line)
        if not match:
            continue
        title = match.group(1).strip()
        if title in UNNUMBERED:
            new = f"## {title}"
        else:
            counter += 1
            new = f"## {counter}. {title}"
        if new != line:
            changes.append((line, new))
        lines[index] = new

    for old, new in changes:
        print(f"  {old}\n   -> {new}")
    print(f"\n  {counter} numbered sections, {len(changes)} heading(s) changed")

    if not args.apply:
        print("  (audit only; pass --apply to write)")
        return 0

    os.makedirs(os.path.dirname(BACKUP), exist_ok=True)
    shutil.copy2(DOC, BACKUP)
    with io.open(DOC, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    print(f"  wrote {DOC}\n  backup {BACKUP}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
