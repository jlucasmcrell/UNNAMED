"""Check every batch at once, the way the game's lint will: all the content rules, near-duplicates across the whole
corpus (not just within a batch), and coverage of the cells once the batches are read together.

    python tools/chatter_pipeline/check_all.py

A batch checked alone can pass while the corpus fails: two batches written a week apart can say the same thing. This is
the check that sees it, and it runs before assembly.
"""

import sys
from pathlib import Path

import yaml

from assemble import check_batch
from chatter_rules import coverage, load_meta, near_duplicates

HERE = Path(__file__).resolve().parent
CONTENT = Path("content")


def main():
    schema, _ = load_meta(CONTENT)
    sources = sorted((HERE / "batches").glob("b*.yaml"))
    sources = [path for path in sources if not path.name.endswith(".draft.yaml")]
    lines, problems, counts = [], [], []
    for path in sources:
        source = yaml.safe_load(path.read_text(encoding="utf-8"))
        batch = source.get("id") or path.stem
        batch_lines, batch_problems = check_batch(source, batch)
        lines.extend(batch_lines)
        problems.extend(batch_problems)
        canon = sum(1 for entry in source["lines"] if entry.get("canon") and not entry.get("retired"))
        counts.append((batch, len(batch_lines), canon))
    problems.extend(("CHT010", f"{a} is a near-duplicate of {b} (word-pair overlap {score})")
                    for a, b, score in near_duplicates(lines, schema.near_duplicate_jaccard))
    for code, message in problems:
        print(f"{code}: {message}")
    thin = coverage(lines, schema)
    for batch, count, canon in counts:
        print(f"  {batch}: {count} lines, {canon} canon ({canon * 100 // max(1, count)}%)")
    print(f"{len(sources)} batches, {len(lines)} corpus lines, {len(problems)} problems, {len(thin)} thin cells")
    return 1 if problems or thin else 0


if __name__ == "__main__":
    sys.exit(main())
