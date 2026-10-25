"""Hard gate on real package logs/coverage; run after startup and each route visit.

Usage: python check_packaged_art.py <log-or-art_coverage.json> [...]
No file is an error: a missing capture must never pass as zero missing art.
"""
import json
import re
import sys
from pathlib import Path


def problems(path):
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    failures = []
    for line in text.splitlines():
        drawn = re.search(r"(\d+) assets drawn", line)
        count = re.search(r"(\d+) withheld or unavailable \(greybox stands in\)", line)
        if (drawn and int(drawn[1]) == 0) or (count and int(count[1]) > 0) or re.search(
                r"drawing greybox|rig placeholder|binding.*(?:greybox|placeholder)", line, re.I):
            failures.append(line.strip())
    if path.suffix == ".json":
        data = json.loads(text)
        entries = data if isinstance(data, list) else data.get("entries", [])
        for entry in entries:
            if entry.get("Outcome", entry.get("outcome")) == "fallback" or entry.get("Fallbacks", entry.get("fallbacks", 0)) > 0:
                failures.append(json.dumps(entry))
    return failures


def loaded_art(path):
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    if path.suffix == ".json":
        data = json.loads(text)
        entries = data if isinstance(data, list) else data.get("entries", [])
        return any(entry.get("Outcome", entry.get("outcome")) == "resolved" for entry in entries)
    return any(int(count) > 0 for count in re.findall(r"(\d+) assets drawn", text))


def main(paths):
    if not paths:
        raise SystemExit("Pass startup/visit logs or coverage receipts; no evidence supplied")
    failed = {str(path): errors for path in map(Path, paths) if (errors := problems(path))}
    if not any(loaded_art(path) for path in map(Path, paths)):
        failed["loaded_art"] = ["No positive loaded-art count or resolved coverage entry in supplied evidence"]
    print(json.dumps({"status": "FAIL" if failed else "PASS", "evidence_files": len(paths), "failures": failed}, indent=2))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
