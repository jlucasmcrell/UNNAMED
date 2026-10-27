"""Creature animation depth: give the shipped creatures the states they were missing.

For every creature that has clip records in the art root, this reads the record of its existing clips to learn WHICH SOURCE FILE it came from,
pulls that file's own takes, picks the takes for the states the game did not have yet - an extra idle, a stagger, a left turn, a right turn and
a second and third death (plus a second hit where the source has one) - and writes a clip record for each, in exactly the same schema as the
six the game already ships. Nothing existing is touched, so the C1 move sets are untouched by construction.

Sources it understands:
  * a WildMesh FBX (takes appear as `Name.tak` strings inside the file),
  * a converted GLB whose animations are named in its JSON chunk (the Quaternius Universal Animation Library, used by the bone walker).

It only writes a state where the source genuinely offers a matching take; it never invents one, and it reports what it could not fill.
"""
import json
import re
import shutil
import struct
import subprocess
import sys
from pathlib import Path

ART = Path(r"D:\Otherreach_Scratch\ds_art\primary")
CLIPS = ART / "animation" / "clips"
SRC_TREE = Path(r"C:\Otherreach_Recovery\asset_work\creatures\_src")
BACKUP = Path(r"C:\Otherreach_External_Assets\first_update\clips_backup_20261027b")

# Which take name fills which new state. Ordered by preference; the first name in each list that the source actually has wins.
WANTED = [
    ("idle_b", True, [r"idle_?0?2\b", r"idle_0?2$", r"stand_idle_02", r"idle_02", r"breathing", r"idle"]),
    ("idle_c", True, [r"idle_?0?3\b", r"idle_03", r"lying_idle", r"sitting_idle", r"stand_idle", r"breathing", r"graze", r"eat"]),
    ("stagger", False, [r"stagger", r"hit_stand_l", r"hit_stand", r"hit_lying", r"hit_sitting", r"hit"]),
    ("turn_left", False, [r"turn_?l\b", r"walk_?l$", r"trot_?l$", r"run_?l$", r"_l$", r"left"]),
    ("turn_right", False, [r"turn_?r\b", r"walk_?r$", r"trot_?r$", r"run_?r$", r"_r$", r"right"]),
    ("death_b", False, [r"death_?(?:sitting|stand_r|laying|lying|b|02)", r"death"]),
    ("death_c", False, [r"death_?(?:lying|laying|stand_l|sit|03|c)", r"death_pose"]),
    ("hit_b", False, [r"hit_?(?:sitting|lying|stand_r|b)", r"hit"]),
]


def takes_of_fbx(path: Path):
    try:
        blob = path.read_bytes()[:24_000_000].decode("latin-1", "ignore")
    except OSError:
        return []
    names = set()
    for m in re.finditer(r"([A-Za-z][A-Za-z0-9_\.\-]{4,60})\.tak", blob):
        names.add(m.group(1))
    # The plain form: a name with an action word in it and an underscore, which is how most of these packs store their takes.
    for m in re.finditer(r"([A-Za-z][A-Za-z0-9_\-]{3,60})", blob):
        s = m.group(1)
        if "_" in s and re.search(r"(idle|walk|run|trot|turn|hit|stagger|dead|death|die|attack|graze|eat|breath|sit|lying|stand)", s, re.I) \
                and not re.search(r"^(NodeAttribute|Rig|LOD|Material|Shader)", s, re.I):
            names.add(s)
    return sorted(names)


def takes_of_glb(path: Path):
    try:
        with path.open("rb") as f:
            magic, _v, length = struct.unpack("<4sII", f.read(12))
            if magic != b"glTF":
                return []
            js = None
            while f.tell() < length:
                clen, ctype = struct.unpack("<I4s", f.read(8))
                data = f.read(clen)
                if ctype == b"JSON":
                    js = json.loads(data.decode("utf-8"))
        return sorted({a.get("name", "") for a in (js or {}).get("animations", []) if a.get("name")})
    except Exception:  # noqa: BLE001
        return []


def find_source(file_name: str):
    """The source file a clip record names, searched across every tree the owned art lives in."""
    if not file_name:
        return None
    roots = (SRC_TREE, Path(r"C:\Otherreach_Recovery\asset_work\creatures"), ART,
             Path(r"C:\Otherreach_External_Assets"), Path(r"D:\Otherreach_Depot"))
    import os
    for root in roots:
        if not root.exists():
            continue
        for base, _dirs, files in os.walk(root):
            if file_name in files:
                return Path(base) / file_name
    return None


IDLE_WORDS = re.compile(r"idle|breath|graze|eat|sit|lying|stand_straight", re.I)
NOT_IDLE = re.compile(r"death|die|hit|attack|run|walk|trot|frighten", re.I)


def pick(takes, patterns, used, want_idle=False):
    """The first take matching a pattern that no other state already took - and an idle state may only take an idle take."""
    for pattern in patterns:
        rx = re.compile(pattern, re.I)
        for take in takes:
            if take in used or not rx.search(take) or len(take) < 7 or take.endswith("_") or take.endswith("."):
                continue
            if want_idle and (NOT_IDLE.search(take) or not IDLE_WORDS.search(take)):
                continue
            return take
    return None


def main():
    BACKUP.mkdir(parents=True, exist_ok=True)
    records = sorted(CLIPS.glob("anim.creature.*.json"))
    by_model = {}
    for record in records:
        model = record.name[len("anim.creature."):-len(".json")].rsplit(".", 1)[0]
        by_model.setdefault(model, []).append(record)
    print(f"{len(by_model)} creature models with clip records")

    written, skipped = [], []
    for model, existing in sorted(by_model.items()):
        base = json.loads(existing[0].read_text(encoding="utf-8"))
        # The record that names a source file is not always the first one alphabetically (a creature whose idle names the UAL library can
        # still have an attack record with no file at all), so take the source from whichever record actually names one.
        for candidate in existing:
            try:
                named = (json.loads(candidate.read_text(encoding="utf-8")).get("source") or {}).get("file") or ""
            except (OSError, ValueError):
                named = ""
            if named:
                base = json.loads(candidate.read_text(encoding="utf-8"))
                break
        source_file = (base.get("source") or {}).get("file") or ""
        have = {r.name[len(f"anim.creature.{model}."):-len(".json")] for r in existing}
        source = find_source(source_file) if source_file else None
        if source is None:
            skipped.append((model, f"source {source_file or 'unknown'} not on disk"))
            continue
        takes = takes_of_fbx(source) if source.suffix.lower() == ".fbx" else takes_of_glb(source)
        if not takes:
            skipped.append((model, f"no readable takes in {source.name}"))
            continue
        used = set()
        for state, loop, patterns in WANTED:
            take = pick(takes, patterns, used, want_idle=loop)
            if state in have or take is None:
                continue
            used.add(take)
            if take is None:
                continue
            record = dict(base)
            record["animation_id"] = f"anim.creature.{model}.{state}"
            record["category"] = "idle" if loop else ("hit" if state.startswith("hit") or state == "stagger" else
                                                      "death" if state.startswith("death") else "turn")
            record["clip_type"] = record["category"]
            record["loop"] = loop
            record["tags"] = [t for t in base.get("tags", []) if t not in ("looping",)] + (["looping"] if loop else [])
            record["source"] = dict(base.get("source") or {})
            record["source"]["action"] = take
            record["source"]["file"] = source.name
            target = CLIPS / f"anim.creature.{model}.{state}.json"
            if target.exists():
                continue
            target.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
            written.append((model, state, take))
    print(f"{len(written)} clip records written, {len(skipped)} models skipped")
    for model, state, take in written:
        print(f"  {model}.{state} <- {take}")
    for model, why in skipped:
        print(f"  skipped {model}: {why}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
