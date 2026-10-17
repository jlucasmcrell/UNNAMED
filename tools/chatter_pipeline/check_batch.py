"""Check one batch's source file without writing anything: the content rules, near-duplicates, keys and coverage.

This is the loop a batch is written in. It is stricter than the game's lint in one way only: it checks coverage
inside the batch, so a batch that leaves a cell thin is refused before it is assembled rather than after.

    python tools/chatter_pipeline/check_batch.py tools/chatter_pipeline/batches/b0002.yaml
    python tools/chatter_pipeline/check_batch.py b0002
"""

import sys
from pathlib import Path

import yaml

from assemble import check_batch
from chatter_rules import coverage, load_meta

HERE = Path(__file__).resolve().parent
CONTENT = Path("content")


def resolve(argument):
    path = Path(argument)
    if path.suffix in (".yaml", ".yml"):
        if not path.is_absolute():
            path = Path.cwd() / path
        return path
    for candidate in (HERE / "batches" / f"{argument}.yaml", HERE / "batches" / f"{argument}.draft.yaml"):
        if candidate.exists():
            return candidate
    raise SystemExit(f"no source file for {argument}")


def main():
    path = resolve(sys.argv[1])
    source = yaml.safe_load(path.read_text(encoding="utf-8"))
    batch = source["id"]
    lines, problems = check_batch(source, batch)
    schema, _ = load_meta(CONTENT)
    thin = coverage(lines, schema)
    for code, message in problems:
        print(f"{code}: {message}")
    canon = sum(1 for entry in source["lines"] if entry.get("canon"))
    print(f"{path.name}: {len(lines)} lines, {len(problems)} problems, {len(thin)} thin cells, "
          f"{canon} marked canon ({canon * 100 // max(1, len(lines))}%)")
    if thin:
        thin.sort(key=lambda gap: gap[3])
        print("  thinnest: " + ", ".join(f"{s}/{p}/{b} {n}" for s, p, b, n in thin[:10]))
    return 1 if problems or thin else 0


if __name__ == "__main__":
    sys.exit(main())
