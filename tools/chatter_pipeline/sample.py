"""Draw the random review samples that go in front of a human.

Five per cent of every batch, never fewer than 15 lines of a batch and never fewer than 100 lines in all, drawn by one
fixed seed so the sample that is reviewed is the sample that was drawn. Written to content/chatter/_review_sample.md,
batch by batch.

    python tools/chatter_pipeline/sample.py
"""

import random
from pathlib import Path

import yaml

CONTENT = Path("content")
CHATTER = CONTENT / "chatter"
SEED = 20261018
SHARE = 0.05
BATCH_MINIMUM = 15
TOTAL_MINIMUM = 100


def main():
    batches = {}
    for path in sorted(CHATTER.glob("*.yaml")):
        if path.name.startswith("_"):
            continue
        doc = yaml.safe_load(path.read_text(encoding="utf-8"))
        if doc.get("role") != "corpus":
            continue
        batches[doc["batch"]] = doc.get("lines") or []
    total = sum(len(lines) for lines in batches.values())
    drawn = {}
    for batch, lines in batches.items():
        count = max(BATCH_MINIMUM, round(len(lines) * SHARE))
        drawn[batch] = sorted(random.Random(SEED).sample(lines, min(count, len(lines))), key=lambda row: row["key"])
    short = TOTAL_MINIMUM - sum(len(sample) for sample in drawn.values())
    if short > 0 and batches:   # a tiny corpus still gets its hundred lines
        biggest = max(batches, key=lambda batch: len(batches[batch]))
        extra = [row for row in batches[biggest] if row not in drawn[biggest]]
        drawn[biggest] += sorted(random.Random(SEED + 1).sample(extra, min(short, len(extra))),
                                 key=lambda row: row["key"])

    out = [f"# Chatter review sample ({sum(len(s) for s in drawn.values())} of {total} lines, seed {SEED})",
           "",
           "Every line is AI-generated offline (`ai_generated: true` in the batch's file and `_provenance.yaml`). Read",
           "for voice, and for anything the lint cannot see: a line that is dull, smug, exposition, or untrue to the",
           "world. To have a line changed, name its key and that batch is regenerated. 5% of every batch is here.",
           ""]
    for batch in sorted(drawn):
        out += [f"## {batch} ({len(drawn[batch])} of {len(batches[batch])} lines)", ""]
        for row in drawn[batch]:
            tags = []
            for field in ("speakers", "places", "times", "weather", "events"):
                values = row.get(field) or []
                if values:
                    tags.append(f"{field}: {', '.join(values)}")
            if row.get("channel", "local") != "local":
                tags.append(f"channel: {row.get('channel')}")
            out.append(f"- `{row['key']}` _({'; '.join(tags)})_")
            out.append(f"  {row['text']}")
        out.append("")
    (CHATTER / "_review_sample.md").write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"wrote {CHATTER / '_review_sample.md'}: "
          + ", ".join(f"{batch} {len(sample)}/{len(batches[batch])}" for batch, sample in sorted(drawn.items())))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
