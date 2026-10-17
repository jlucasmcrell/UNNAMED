"""Write a batch's lines into content/chatter, keeping every line's key stable.

The source of truth for a batch is `batches/<batch>.yaml`: the lines as authored, each with the key it will always
have. A line whose text changes keeps its key; a line withdrawn is marked `retired: true` and its key is never reused,
so nothing downstream shifts position. This script validates a source file and writes the content definition the game
loads, plus the batch's entry in `_provenance.yaml`:

    python tools/chatter_pipeline/assemble.py b0001
"""

import sys
from pathlib import Path

import yaml

from chatter_rules import check_line, coverage, load_meta, near_duplicates

HERE = Path(__file__).resolve().parent
CONTENT = Path("content")
CHATTER = CONTENT / "chatter"

FIELDS = ("speakers", "places", "times", "weather", "events")


def read_source(batch):
    path = HERE / "batches" / f"{batch}.yaml"
    return yaml.safe_load(path.read_text(encoding="utf-8"))


def expand(entry, batch):
    """A source entry as the content line the game reads."""
    line = {"key": entry["key"], "text": entry["text"].strip(), "batch": batch,
            "ai_generated": True, "channel": entry.get("channel", "local")}
    place, band = entry["cell"].split("-", 1)
    line["speakers"] = list(entry.get("speakers") or [])
    line["places"] = list(entry.get("places") or [place])
    line["times"] = list(entry.get("times") or [band])
    line["weather"] = list(entry.get("weather") or [])
    line["events"] = list(entry.get("events") or [])
    return line


def tags(values):
    return "[" + ", ".join(values) + "]"


def write_entry(line):
    parts = [f"key: {line['key']}", f'text: "{line["text"]}"']
    for field in FIELDS:
        if line[field]:
            parts.append(f"{field}: {tags(line[field])}")
    if line["channel"] != "local":
        parts.append(f"channel: {line['channel']}")
    return "  - { " + ", ".join(parts) + ", ai_generated: true }"


def main():
    batch = sys.argv[1]
    source = read_source(batch)
    schema, banned = load_meta(CONTENT)
    batches = {batch}
    lines, problems = [], []
    for entry in source["lines"]:
        if entry.get("retired"):
            continue
        line = expand(entry, batch)
        problems.extend(check_line(line, schema, banned, batches))
        lines.append(line)
    problems.extend(("CHT010", f"{a} is a near-duplicate of {b} (word-pair overlap {score})")
                    for a, b, score in near_duplicates(lines, schema.near_duplicate_jaccard))
    if problems:
        for code, message in problems:
            print(f"{code}: {message}")
        print(f"{batch}: {len(problems)} problems, nothing written")
        return 1

    keys = [line["key"] for line in lines]
    if len(set(keys)) != len(keys):
        print(f"{batch}: duplicate keys in the source")
        return 1

    header = [
        f"id: chatter.{batch}",
        "kind: chatter",
        "schema: 1",
        f"display_key: chatter.{batch}.name",
        "tags: [chatter]",
        f"notes: {source['notes']}",
        "role: corpus",
        f"batch: {batch}",
        "lines:",
    ]
    body = [write_entry(line) for line in lines]
    (CHATTER / f"{batch}.yaml").write_text("\n".join(header + body) + "\n", encoding="utf-8")

    provenance = yaml.safe_load((CHATTER / "_provenance.yaml").read_text(encoding="utf-8"))
    entry = {"id": batch, "model": f'"{source["model"]}"', "date": str(source["date"]),
             "prompt_hash": source["prompt_hash"], "lint": f'"{source["lint"]}"', "review": f'"{source["review"]}"'}
    provenance["batches"] = [b for b in provenance.get("batches") or [] if b["id"] != batch] + [entry]
    (CHATTER / "_provenance.yaml").write_text(
        "# Where each AI-generated corpus batch came from (WP16; CHT011). Every corpus file names one batch; every line in it carries\n"
        "# ai_generated: true and a key chat.<batch>.<nnnn>. Record the batch here before its file lands:\n"
        "#   - { id: b0001, model: <generator model and version>, date: YYYY-MM-DD, prompt_hash: sha256:<64 hex digits of the exact prompt> }\n"
        "# The lint reads id, model, date and prompt_hash; the lint and review notes beside them are for the reader.\n"
        "# The LFG, news and system templates are hand-written and need no entry.\n"
        "batches:\n" + "".join(
            f"  - {{ id: {b['id']}, model: {b['model']}, date: {b['date']}, prompt_hash: {b['prompt_hash']}"
            f", lint: {b.get('lint', '\"clean\"')}, review: {b.get('review', '\"pending\"')} }}\n"
            for b in sorted(provenance["batches"], key=lambda b: b["id"])),
        encoding="utf-8")

    thin = coverage(lines, schema)
    print(f"{batch}: wrote {CHATTER / (batch + '.yaml')} - {len(lines)} lines, lint clean")
    print(f"  speakers {len({s for l in lines for s in l['speakers']})}/10  places {len({p for l in lines for p in l['places']})}/4"
          f"  bands {len({t for l in lines for t in l['times']})}/6  weather-tagged {sum(1 for l in lines if l['weather'])}"
          f"  world {sum(1 for l in lines if l['channel'] == 'world')}  events {sum(1 for l in lines if l['events'])}")
    print(f"  coverage gaps: {len(thin)}" + (f" (thinnest {min(thin, key=lambda g: g[3])})" if thin else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
