"""Build the owner's V1-versus-V2 listening package.

The brief's requirement is that the owner must be able to listen, quickly, and not open 519 files one
at a time. So this produces per-family folders holding V1 and all three V2 candidates for every id,
plus a single HTML index that plays them in place, and it names the provisional pick so the owner can
start from a working set and override rather than assemble one.

Spectrograms are deliberately not the interface. They were the right tool for finding defects without
ears and they are the wrong tool for judging whether a boar sounds like a boar.

Usage:
    python _make_audition_package.py --apply
"""
import argparse
import io
import json
import os
import shutil

ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v2.json")
QA = os.path.join(ASSETS, "manifests", "audio_qa_v2.json")
V1_DIR = os.path.join(ASSETS, "audio", "v1_stable_audio_open", "delivered")
CANDIDATES = os.path.join(ASSETS, "audio", "v2_candidates")
OUT = os.path.join(ASSETS, "review", "audio_v2")

LABELS = ("a", "b", "c")

# Family folders, as the brief lists them.
FAMILY_OF_CATEGORY = {
    "player": "player",
    "weapon": "weapons",
    "creature": "creatures",
    "magic": "magic",
    "crafting": "crafting",
    "interaction": "interaction",
    "ui": "ui",
    "ambience": "ambience",
}


def find_v1(audio_id):
    for suffix in (".wav", ".flac"):
        path = os.path.join(V1_DIR, audio_id + suffix)
        if os.path.exists(path):
            return path
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)
    qa = {}
    if os.path.exists(QA):
        with io.open(QA, encoding="utf-8") as handle:
            qa = json.load(handle).get("sounds", {})

    if not args.apply:
        print(f"  ids: {len(spec['sounds'])}  qa entries: {len(qa)}")
        print("  (audit only; pass --apply to build the package)")
        return 0

    for family in sorted(set(FAMILY_OF_CATEGORY.values())):
        os.makedirs(os.path.join(OUT, family), exist_ok=True)

    rows = []
    copied = 0
    for entry in spec["sounds"]:
        audio_id = entry["audio_id"]
        family = FAMILY_OF_CATEGORY.get(entry["category"], "other")
        folder = os.path.join(OUT, family, audio_id)
        os.makedirs(folder, exist_ok=True)

        files = {}
        v1 = find_v1(audio_id)
        if v1:
            target = os.path.join(folder, "V1" + os.path.splitext(v1)[1])
            shutil.copy2(v1, target)
            files["V1"] = os.path.basename(target)
            copied += 1

        for label in LABELS:
            source = os.path.join(CANDIDATES, audio_id, f"candidate_{label}.flac")
            if os.path.exists(source):
                target = os.path.join(folder, f"V2-{label.upper()}.flac")
                shutil.copy2(source, target)
                files[f"V2-{label.upper()}"] = os.path.basename(target)
                copied += 1

        record = qa.get(audio_id, {})
        rows.append({
            "audio_id": audio_id,
            "family": family,
            "group": entry["group"],
            "seconds": entry["seconds"],
            "prompt": entry["prompt"],
            "prompt_v1": entry.get("prompt_v1"),
            "source": entry.get("source", "replacement"),
            "files": files,
            "provisional": record.get("provisional"),
            "survivors": record.get("survivors", []),
            "relative": os.path.join(family, audio_id).replace(os.sep, "/"),
        })

    write_index(rows)
    write_metadata(rows)
    print(f"  ids packaged : {len(rows)}")
    print(f"  audio copied : {copied}")
    print(f"  index        : {os.path.join(OUT, 'index.html')}")
    return 0


def write_metadata(rows):
    with io.open(os.path.join(OUT, "audition_index.json"), "w", encoding="utf-8") as handle:
        json.dump({
            "comment": [
                "Owner audition index for the Stable Audio 3 pass.",
                "",
                "For each id: the V1 original, three V2 candidates, and the candidate an automated",
                "non-aesthetic rule picked as provisional. The owner may replace any provisional",
                "pick; nothing here is approved.",
            ],
            "human_auditioned": False,
            "ids": rows,
        }, handle, indent=2)
        handle.write("\n")


def write_index(rows):
    """One page, grouped by family, playing V1 and each candidate inline."""
    by_family = {}
    for row in rows:
        by_family.setdefault(row["family"], []).append(row)

    parts = ["""<!doctype html><html><head><meta charset="utf-8">
<title>Otherreach Phase-1 audio - V1 vs V2 audition</title>
<style>
 body{background:#12131a;color:#e6e6ee;font:13px/1.5 system-ui,Segoe UI,sans-serif;margin:0;padding:24px}
 h1{font-size:19px;margin:0 0 4px} h2{font-size:15px;margin:28px 0 8px;color:#9fd0ff}
 .note{color:#9a9aa8;max-width:900px;margin-bottom:18px}
 .id{border-top:1px solid #262838;padding:12px 0}
 .name{font-weight:600;color:#ffd88a;font-size:13px}
 .meta{color:#8b8b9c;font-size:11px;margin:2px 0 6px}
 .prompt{color:#b9b9c8;font-size:11px;margin:4px 0;max-width:1000px}
 .v1{color:#7f8fa6}.prov{color:#8ee08a;font-weight:600}
 .row{display:flex;flex-wrap:wrap;gap:14px;align-items:center;margin-top:6px}
 .cell{background:#1b1d27;border:1px solid #262838;border-radius:6px;padding:6px 8px}
 .cell b{display:block;font-size:11px;color:#c9c9d8;margin-bottom:3px}
 audio{height:30px;width:250px}
</style></head><body>
<h1>Otherreach Phase-1 audio &mdash; V1 versus V2</h1>
<div class="note">
V1 is Stable Audio Open 1.0. V2 is Stable Audio 3 Small SFX. For each id you get the V1 original
and three V2 candidates; the one marked <span class="prov">provisional</span> is what an automated
rule picked, and it can be replaced. <b>No sound here has been listened to by a machine</b> &mdash;
technical QA checked duration, level, clipping, silence, channels and loop seams, and nothing else.
</div>"""]

    for family in sorted(by_family):
        parts.append(f"<h2>{family} ({len(by_family[family])})</h2>")
        for row in by_family[family]:
            parts.append('<div class="id">')
            parts.append(f'<div class="name">{row["audio_id"]}</div>')
            parts.append(f'<div class="meta">{row["group"]} &middot; {row["seconds"]}s &middot; '
                         f'{row["source"]}</div>')
            parts.append(f'<div class="prompt"><b>V2 prompt:</b> {row["prompt"]}</div>')
            parts.append('<div class="row">')
            for key in ("V1", "V2-A", "V2-B", "V2-C"):
                name = row["files"].get(key)
                if not name:
                    continue
                mark = ""
                if key == "V1":
                    mark = '<span class="v1">(original)</span>'
                elif row["provisional"] and key == f'V2-{row["provisional"].upper()}':
                    mark = '<span class="prov">provisional</span>'
                label = f"{key} {mark}"
                parts.append(f'<div class="cell"><b>{label}</b>'
                             f'<audio controls preload="none" '
                             f'src="{row["relative"]}/{name}"></audio></div>')
            parts.append("</div></div>")

    parts.append("</body></html>")
    with io.open(os.path.join(OUT, "index.html"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(parts))


if __name__ == "__main__":
    raise SystemExit(main())
