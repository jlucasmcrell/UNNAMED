"""Draw the random review sample that goes in front of a human.

5% of the corpus, and never fewer than 100 lines, drawn from the content files by one fixed seed so the sample that is
reviewed is the sample that was drawn. Written to content/chatter/_review_sample.md.

    python tools/chatter_pipeline/sample.py
"""

import random
from pathlib import Path

import yaml

CONTENT = Path("content")
CHATTER = CONTENT / "chatter"
SEED = 20261018
SHARE = 0.05
MINIMUM = 100


def main():
    lines = []
    for path in sorted(CHATTER.glob("*.yaml")):
        if path.name.startswith("_"):
            continue
        doc = yaml.safe_load(path.read_text(encoding="utf-8"))
        if doc.get("role") != "corpus":
            continue
        for row in doc.get("lines") or []:
            lines.append((doc["batch"], row))
    count = max(MINIMUM, round(len(lines) * SHARE))
    drawn = sorted(random.Random(SEED).sample(lines, min(count, len(lines))), key=lambda pair: pair[1]["key"])
    out = [f"# Chatter review sample ({len(drawn)} of {len(lines)} lines, drawn by seed {SEED})",
           "",
           "Every line is AI-generated offline (`ai_generated: true` in the batch's file and `_provenance.yaml`).",
           "Read for voice, and for anything the lint cannot see: a line that is dull, smug, exposition, or untrue to",
           "the world. Any line you want changed: name its key and the batch is regenerated.",
           ""]
    for batch, row in drawn:
        tags = []
        for field in ("speakers", "places", "times", "weather", "events"):
            values = row.get(field) or []
            if values:
                tags.append(f"{field}: {', '.join(values)}")
        if row.get("channel", "local") != "local":
            tags.append(f"channel: {row.get('channel')}")
        out.append(f"- `{row['key']}` _({'; '.join(tags)})_")
        out.append(f"  {row['text']}")
    (CHATTER / "_review_sample.md").write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"wrote {CHATTER / '_review_sample.md'}: {len(drawn)} lines of {len(lines)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
