"""Promote a category agent's staged assets into the playtest package and the repo's bindings.

    python integrate_category.py C:\\Otherreach_Recovery\\asset_work\\rocks_terrain [--dry-run] [--replace]

The staging dir is the overhaul layout (docs/restore/OVERHAUL_AGENT_BRIEF.txt):
  ready\\<id>\\                      converted models (copied without _preview.png); one holding animation\\ is a rigged
                                   creature, installed as rigged\\<id>\\<id>_rigged.glb plus its clips under animation\\
  ready\\materials\\<id>\\ or materials\\<id>\\   world/terrain materials
  bindings_proposed.json           entries for content\\_art_bindings.json (each entry replaced whole, keys of the compiled
                                   entry it drops written as null; "comment" and "_*" keys skipped)
  scatter_rules_proposed.json      {"kinds": {kind: {field: value}}} set on content\\_scatter_rules.json (null deletes the field)
  terrain_layers_proposed.json     the nine Terrain3D slots, replacing <asset-root>\\terrain_layers.json
  dressing_proposed.json           {"places_to_add": [...], "replace_model": {old: new}} on <asset-root>\\dressing.json
  audio_proposed.json              {"sounds": [V3 rows + action, file_in_staging]} into <asset-root>\\manifests\\playable_prototype_audio_v3.json,
                                   each file copied to <asset-root>\\<delivered> (--exclude also matches audio ids)

Where each lands is where PLAY.bat reads it: --bindings-overlay and --scatter-rules are content\\ files (edited in the repo,
then the repo's content\\ is synced into the package's), terrain_layers.json and dressing.json are read from --asset-root only.
The compiled src\\Presentation\\Art\\art_bindings.json is not touched: the overlay merges over it key by key and needs no export.

Every file overwritten is first copied under --backup-dir\\<category>_<time>\\. --dry-run prints the same summary and writes nothing.
An existing ready\\<id> or materials\\<id> with different contents is refused unless --replace (it is backed up first).
"""
import argparse
import filecmp
import fnmatch
import json
import os
import re
import shutil
import sys
import time

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
PACKAGE = r"C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261014"
BACKUPS = r"C:\Otherreach_Recovery\asset_work\integration\backups"
REGION = os.path.join("content", "regions", "starting_frontier.yaml")


class Writer:
    def __init__(self, dry, backup_root):
        self.dry = dry
        self.backup_root = backup_root
        self.backed = set()

    def backup(self, path):
        if self.dry or path in self.backed or not os.path.exists(path):
            return
        self.backed.add(path)
        drive, rest = os.path.splitdrive(os.path.abspath(path))
        dest = os.path.join(self.backup_root, drive.rstrip(":"), rest.lstrip("\\/"))
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        if os.path.isdir(path):
            shutil.copytree(path, dest)
        else:
            shutil.copy2(path, dest)

    def json(self, path, data, indent, newline):
        if self.dry:
            return
        self.backup(path)
        text = json.dumps(data, indent=indent, ensure_ascii=False) + ("\n" if newline else "")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)

    def copy(self, src, dst):
        if self.dry:
            return
        if os.path.exists(dst):
            self.backup(dst)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)


def load(path):
    """The document and how it was written (indent, trailing newline), so a rewrite only shows the real change."""
    text = open(path, encoding="utf-8").read()
    second = text.split("\n", 2)[1] if "\n" in text else ""
    indent = len(second) - len(second.lstrip(" ")) or None
    return json.loads(text), indent, text.endswith("\n")


def same_folder(a, b):
    names = sorted(n for n in os.listdir(a) if not n.endswith("_preview.png"))
    return names == sorted(os.listdir(b)) and all(filecmp.cmp(os.path.join(a, n), os.path.join(b, n), shallow=False) for n in names)


def copy_folders(kind, staged, root, fallback, args, w):
    """ready\\<id> or materials\\<id> folders into the asset root; returns the ids copied."""
    copied = []
    for aid in sorted(os.listdir(staged)):
        src = os.path.join(staged, aid)
        if not os.path.isdir(src) or (kind == "ready" and aid == "materials"):
            continue
        if excluded(aid, args.exclude):
            print(f"  {kind}/{aid}: excluded")
            continue
        if kind == "ready" and os.path.isdir(os.path.join(src, "animation")):
            install_rigged(aid, src, root, args, w)
            copied.append(aid)
            continue
        dst = os.path.join(root, kind, aid)
        if os.path.isdir(dst):
            if same_folder(src, dst):
                print(f"  {kind}/{aid}: already in place")
                continue
            if not args.replace:
                sys.exit(f"{dst} exists with different contents; pass --replace to back it up and overwrite it")
            print(f"  {kind}/{aid}: REPLACED (old folder backed up)")
            w.backup(dst)
            if not w.dry:
                shutil.rmtree(dst)
        else:
            print(f"  {kind}/{aid}: new")
        files = [n for n in os.listdir(src) if not n.endswith("_preview.png")]
        if kind == "ready":
            # ArtLibrary resolves each file primary-then-fallback: a depot LOD the new set lacks would mix into its ladder.
            depot = os.path.join(fallback, kind, aid)
            stale = [n for n in os.listdir(depot) if n not in files and n.endswith(".glb")] if os.path.isdir(depot) else []
            if stale:
                print(f"    WARNING partial override: fallback still holds {stale}")
        for n in files:
            w.copy(os.path.join(src, n), os.path.join(dst, n))
        copied.append(aid)
    return copied


def excluded(aid, patterns):
    return any(fnmatch.fnmatchcase(aid, p) for p in patterns)


def models_of(entry):
    if not isinstance(entry, dict):
        return []
    return [entry[k] for k in ("model", "spent") if isinstance(entry.get(k), str)] + [m for m in entry.get("models", []) if isinstance(m, str)]


def install_rigged(aid, src, root, args, w):
    """A skinned model with its clips: <id>.glb -> rigged\\<id>\\<id>_rigged.glb, animation\\** -> <root>\\animation\\** (review PNGs stay)."""
    pairs = [(os.path.join(src, f"{aid}.glb"), os.path.join(root, "rigged", aid, f"{aid}_rigged.glb")),
             (os.path.join(src, f"{aid}_meta.json"), os.path.join(root, "rigged", aid, f"{aid}_meta.json"))]
    for folder, _, files in os.walk(os.path.join(src, "animation")):
        for n in files:
            pairs.append((os.path.join(folder, n), os.path.join(root, os.path.relpath(os.path.join(folder, n), src))))
    clash = [d for s, d in pairs if os.path.exists(d) and not filecmp.cmp(s, d, shallow=False)]
    if clash and not args.replace:
        sys.exit(f"{clash[0]} exists with different contents; pass --replace to back it up and overwrite it")
    print(f"  rigged/{aid}: {'REPLACED' if clash else 'new'} ({len(pairs) - 2} clip files)")
    for s, d in pairs:
        w.copy(s, d)


def merge_bindings(proposal, path, base_path, exclude, w):
    data, indent, newline = load(path)
    base = json.load(open(base_path, encoding="utf-8"))
    changes = 0
    for key, value in proposal.items():
        # "_pending_owner" and the like are held back by the category agent, not bindings.
        if key == "comment" or key.startswith("_") or any(excluded(m, exclude) for m in models_of(value)):
            continue
        if not isinstance(value, dict):
            if data.get(key) != value:
                print(f"  {key}: {data.get(key)!r} -> {value!r}")
                data[key] = value
                changes += 1
            continue
        section = data.setdefault(key, {})
        for sub, entry in value.items():
            if sub in ("note", "comment") and isinstance(entry, str) and sub in section:
                continue
            if any(excluded(m, exclude) for m in models_of(entry)):
                print(f"  {key}.{sub}: excluded")
                continue
            old = section.get(sub)
            compiled = base.get(key, {}).get(sub) if isinstance(base.get(key), dict) else None
            if models_of(entry) and isinstance(compiled, dict):
                # The overlay merges INTO the compiled entry, so a replaced entry must null what it drops (an old "surface"
                # would otherwise repaint the new model).
                entry = {**{k: None for k in compiled if k not in entry and k != "note"}, **entry}
            if old == entry:
                continue
            what = lambda e: e.get("model") or e.get("models") or e.get("class") or e.get("category") if isinstance(e, dict) else e
            shadow = " (overrides the compiled binding)" if isinstance(base.get(key), dict) and sub in base[key] and old is None else ""
            print(f"  {key}.{sub}: {'added' if old is None else what(old)} -> {what(entry)}{shadow}")
            section[sub] = entry
            changes += 1
    if changes:
        w.json(path, data, indent, newline)
    print(f"  {changes} binding entries changed in {path}")


def merge_scatter(proposal, path, w):
    data, indent, newline = load(path)
    changes = 0
    for kind, fields in proposal["kinds"].items():
        target = data["kinds"].setdefault(kind, {})
        for field, value in fields.items():
            if value is None:
                if field in target:
                    print(f"  kinds.{kind}.{field}: {target.pop(field)!r} removed")
                    changes += 1
            elif target.get(field) != value:
                if field != "note":
                    print(f"  kinds.{kind}.{field}: {target.get(field)!r} -> {value!r}")
                target[field] = value
                changes += 1
    if changes:
        w.json(path, data, indent, newline)
    print(f"  {changes} scatter fields changed in {path}")


def replace_terrain_layers(layers, root, w):
    path = os.path.join(root, "terrain_layers.json")
    old, indent, newline = load(path)
    if len(layers) != len(old):
        sys.exit(f"terrain layers: the proposal names {len(layers)} slots; {path} has {len(old)} and the slots are addressed by position")
    for slot, layer in enumerate(layers):
        # Terrain3DView reads these two maps from the asset root alone (no fallback); a missing one leaves the slot blank.
        for suffix in ("_albedo_height.png", "_normal_rough.png"):
            if not w.dry and not os.path.exists(os.path.join(root, "materials", layer["id"], layer["id"] + suffix)):
                sys.exit(f"terrain layer slot {slot} {layer['id']}: no materials/{layer['id']}/{layer['id']}{suffix} in {root}")
        before = old[slot]
        if (before["id"], before["uv_scale"], before["tint"]) != (layer["id"], layer["uv_scale"], layer["tint"]):
            print(f"  slot {slot}: {before['id']} x{before['uv_scale']} -> {layer['id']} x{layer['uv_scale']} {layer['tint']}")
    if layers != old:
        w.json(path, layers, indent, newline)


def region_bounds():
    """The region terrain's x/z extent, read from the head of the region YAML (origin_m, spacing_m, columns, rows)."""
    head = open(os.path.join(REPO, REGION), encoding="utf-8").read(20000)
    m = re.search(r"\nterrain:\s*\n\s*origin_m:\s*\n\s*- ([\d.]+)\s*\n\s*- ([\d.]+)\s*\n\s*spacing_m: ([\d.]+)\s*\n\s*columns: (\d+)\s*\n\s*rows: (\d+)", head)
    x, z, s, c, r = (float(g) for g in m.groups())
    return x, z, x + s * (c - 1), z + s * (r - 1)


def merge_dressing(proposal, root, w):
    path = os.path.join(root, "dressing.json")
    data, indent, newline = load(path)
    x0, z0, x1, z1 = region_bounds()
    inside = lambda at: x0 <= at[0] <= x1 and z0 <= at[1] <= z1
    # Dressing.cs places "at" in world metres with no region offset: an entry outside the terrain is drawn off the map.
    outside = [p for p in data["places"] if not inside(p["at"])]
    if outside:
        print(f"  WARNING {len(outside)} of {len(data['places'])} existing places lie outside the region terrain "
              f"x {x0:g}-{x1:g}, z {z0:g}-{z1:g} (e.g. {outside[0]['model']} at {outside[0]['at']}); left as they are")
    changes = 0
    for old, new in proposal.get("replace_model", {}).items():
        for p in data["places"]:
            if p["model"] == old:
                p["model"] = new
                changes += 1
        print(f"  {old} -> {new}: {sum(p['model'] == new for p in data['places'])} places")
    for place in proposal.get("places_to_add", []):
        if not inside(place["at"]):
            sys.exit(f"dressing: {place['model']} at {place['at']} lies outside the region terrain; fix its frame first")
        if any(p["model"] == place["model"] and p["at"] == place["at"] for p in data["places"]):
            continue
        data["places"].append(place)
        print(f"  added {place['model']} at {place['at']}")
        changes += 1
    if changes:
        w.json(path, data, indent, newline)


def merge_audio(proposal, staging, root, exclude, w):
    """audio_proposed.json rows into <root>\\manifests\\playable_prototype_audio_v3.json, each file copied to <root>\\<delivered>."""
    path = os.path.join(root, "manifests", "playable_prototype_audio_v3.json")
    data, indent, newline = load(path)
    rows = {r["audio_id"]: r for r in data["sounds"]}
    counts = {"replace": 0, "add": 0, "excluded": 0}
    for sound in proposal["sounds"]:
        aid = sound["audio_id"]
        if excluded(aid, exclude):
            counts["excluded"] += 1
            continue
        w.copy(os.path.join(staging, sound["file_in_staging"]), os.path.join(root, sound["delivered"]))
        row = {k: v for k, v in sound.items() if k not in ("action", "file_in_staging")}
        if aid in rows:
            # The generation record (prompts, version) described the replaced file, not this one.
            old = {k: v for k, v in rows[aid].items() if not k.startswith("prompt") and k not in ("generation_version", "selection_basis")}
            rows[aid].clear()
            rows[aid].update({**old, **row})
            counts["replace"] += 1
        else:
            data["sounds"].append(row)
            rows[aid] = row
            counts["add"] += 1
    print(f"  {counts['replace']} replaced, {counts['add']} added, {counts['excluded']} excluded in {path}")
    w.json(path, data, indent, newline)


def sync_content(src, dst, w):
    """The repo's content\\ into the package's: files that differ or are missing (nothing is deleted)."""
    count = 0
    for folder, _, files in os.walk(src):
        for n in files:
            a = os.path.join(folder, n)
            b = os.path.join(dst, os.path.relpath(a, src))
            if os.path.exists(b) and filecmp.cmp(a, b, shallow=False):
                continue
            print(f"  content/{os.path.relpath(a, src)}")
            w.copy(a, b)
            count += 1
    print(f"  {count} content files synced to {dst}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("staging")
    ap.add_argument("--package", default=PACKAGE)
    ap.add_argument("--asset-root", help="default <package>\\primary")
    ap.add_argument("--backup-dir", default=BACKUPS)
    ap.add_argument("--replace", action="store_true")
    ap.add_argument("--exclude", action="append", default=[], metavar="PATTERN",
                    help="asset ids to leave out (fnmatch, repeatable); binding entries naming one are left out too")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--no-sync", action="store_true", help="leave the package's content\\ as it is")
    a = ap.parse_args()
    root = a.asset_root or os.path.join(a.package, "primary")
    fallback = os.path.join(a.package, "fallback")
    category = os.path.basename(os.path.normpath(a.staging))
    w = Writer(a.dry_run, os.path.join(a.backup_dir, f"{category}_{time.strftime('%Y%m%d_%H%M%S')}"))
    staged = lambda name: os.path.join(a.staging, name)
    print(("DRY RUN - nothing is written\n" if a.dry_run else "") + f"{category} -> {root}")

    if os.path.isdir(staged("ready")):
        print("models:")
        copy_folders("ready", staged("ready"), root, fallback, a, w)
    for materials in (staged("materials"), os.path.join(staged("ready"), "materials")):
        if os.path.isdir(materials):
            print(f"materials ({materials}):")
            copy_folders("materials", materials, root, fallback, a, w)

    content = os.path.join(REPO, "content")
    if os.path.exists(staged("bindings_proposed.json")):
        print("bindings:")
        merge_bindings(json.load(open(staged("bindings_proposed.json"), encoding="utf-8")),
                       os.path.join(content, "_art_bindings.json"), os.path.join(REPO, "src", "Presentation", "Art", "art_bindings.json"), a.exclude, w)
    if os.path.exists(staged("scatter_rules_proposed.json")):
        print("scatter rules:")
        merge_scatter(json.load(open(staged("scatter_rules_proposed.json"), encoding="utf-8")), os.path.join(content, "_scatter_rules.json"), w)
    if os.path.exists(staged("terrain_layers_proposed.json")):
        print("terrain layers:")
        replace_terrain_layers(json.load(open(staged("terrain_layers_proposed.json"), encoding="utf-8")), root, w)
    if os.path.exists(staged("dressing_proposed.json")):
        print("dressing:")
        merge_dressing(json.load(open(staged("dressing_proposed.json"), encoding="utf-8")), root, w)
    if os.path.exists(staged("audio_proposed.json")):
        print("audio manifest:")
        merge_audio(json.load(open(staged("audio_proposed.json"), encoding="utf-8")), a.staging, root, a.exclude, w)
    if not a.no_sync:
        print("content sync:")
        sync_content(content, os.path.join(a.package, "content"), w)
    if w.backed:
        print(f"backups: {w.backup_root} ({len(w.backed)} files/folders)")


if __name__ == "__main__":
    main()
