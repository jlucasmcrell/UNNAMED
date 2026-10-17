"""Lint the corpus in content/chatter the way the WP16 lint does, and report coverage.

    python tools/chatter_pipeline/lint.py            # every corpus file
    python tools/chatter_pipeline/lint.py b0001      # one batch

The game's own lint (ChatterContent.Lint, CHT001-CHT012) is the gate and runs in the C# tests. This one is the fast
copy used while writing; it covers the content rules - key format, tags, slots, length, raw ids, proper nouns, banned
terms, near-duplicates, provenance - for corpus files. Exit code 1 means stop.
"""

import sys
from pathlib import Path

import yaml

from chatter_rules import check_line, coverage, load_meta, near_duplicates

CONTENT = Path("content")
CHATTER = CONTENT / "chatter"


def read_corpus(batch=None):
    batches = {b["id"] for b in
               (yaml.safe_load((CHATTER / "_provenance.yaml").read_text(encoding="utf-8")).get("batches") or [])}
    lines = []
    for path in sorted(CHATTER.glob("*.yaml")):
        if path.name.startswith("_"):
            continue
        doc = yaml.safe_load(path.read_text(encoding="utf-8"))
        if doc.get("role") != "corpus" or (batch and doc.get("batch") != batch):
            continue
        for row in doc.get("lines") or []:
            line = {"key": row["key"], "text": row["text"], "batch": doc["batch"],
                    "ai_generated": row.get("ai_generated") is True, "channel": row.get("channel", "local")}
            for field in ("speakers", "places", "times", "weather", "events"):
                line[field] = list(row.get(field) or [])
            line["_file"] = path.name
            lines.append(line)
    return lines, batches


def main():
    batch = sys.argv[1] if len(sys.argv) > 1 else None
    schema, banned = load_meta(CONTENT)
    lines, batches = read_corpus(batch)
    problems = [problem for line in lines for problem in check_line(line, schema, banned, batches)]
    problems += [("CHT010", f"{a} is a near-duplicate of {b} (word-pair overlap {score})")
                 for a, b, score in near_duplicates(lines, schema.near_duplicate_jaccard)]
    for code, message in problems:
        print(f"{code}: {message}")
    thin = coverage(lines, schema)
    print(f"{len(lines)} corpus lines, {len(problems)} problems, {len(thin)} of "
          f"{len(schema.cells())} cells under {schema.coverage_min_lines} always-sayable lines")
    if thin:
        thin.sort(key=lambda gap: gap[3])
        print("  thinnest: " + ", ".join(f"{s}/{p}/{b} {n}" for s, p, b, n in thin[:8]))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
