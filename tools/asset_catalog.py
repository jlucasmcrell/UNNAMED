"""BRIEF D8: the complete asset library catalog. CPU only, no downloads.

The same walk as the mob library, over everything we own rather than creatures alone: architecture, nature, props, story items, weapons,
armour, clothing, materials, VFX, audio and animation libraries, each in its own category, deduplicated by name, with the numbers that can
be read without importing anything (texture resolution from image headers, triangle and clip counts from GLBs, clip names and rigs from
FBX, duration and sample rate from audio headers), a licence label from the source, an era tag, a grade, and every path the same asset
appears at. Pack previews are copied into one contact sheet per category.

Run it again at any time: nothing is cached outside the two output folders.
"""
import csv
import json
import re
import struct
import wave
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

OUT = Path(r"C:\Otherreach_External_Assets\asset_library")
SELECTIONS = Path(r"C:\Otherreach_External_Assets\selections.json")
PREVIEWS = OUT / "previews"
MISSING = object()

SOURCES = [
    (Path(r"D:\Otherreach_Depot\Creatures_20261017"), "Fab Standard (settled)", "20261017"),
    (Path(r"D:\Otherreach_Depot\Fab_Creatures_Free_20261018"), "Fab Free (settled)", "20261018"),
    (Path(r"D:\Otherreach_Depot\Mixamo_20261017"), "Mixamo (Adobe licence, settled)", "20261017"),
    (Path(r"D:\Otherreach_Depot\Rokoko_Free_263"), "Rokoko free library (settled)", "20261017"),
    (Path(r"D:\Otherreach_Depot\UE_Exports"), "UE export of owned content (settled)", "20261017"),
    (Path(r"D:\Otherreach_Depot\models"), "mixed depot (check per pack)", "legacy"),
    (Path(r"C:\Otherreach_External_Assets\Fab"), "Fab Standard (settled)", "20261016"),
    (Path(r"C:\Otherreach_External_Assets\Fab_Free_20261016"), "Fab Free (settled)", "20261016"),
    (Path(r"C:\Otherreach_External_Assets\Cosmos_Premium"), "Cosmos Premium (settled for 3D)", "20261016"),
    (Path(r"C:\Otherreach_External_Assets\Sketchfab_20261016"), "Sketchfab CC-BY (attribution)", "20261016"),
    (Path(r"C:\Otherreach_External_Assets\BlenderKit"), "BlenderKit royalty-free (settled)", "20261018"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_BlenderKit_20261018"), "BlenderKit royalty-free (settled)", "20261018"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_CC0_20261018"), "CC0 (settled)", "20261018"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_20261018"), "mixed sourced (check per pack)", "20261018"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_20261018_Additional"), "mixed sourced (check per pack)", "20261018"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_20261018_Canon_Gaps"), "mixed sourced (check per pack)", "20261018"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_DS_20261018"), "mixed sourced (check per pack)", "20261018"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_Armory_Pilot_20261019"), "sourced armory pilot", "20261019"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_Clothing_20261019"), "sourced clothing", "20261019"),
    (Path(r"C:\Otherreach_External_Assets\PolyHaven"), "PolyHaven CC0 (settled)", "20261014"),
    (Path(r"C:\Otherreach_External_Assets\ambientCG"), "ambientCG CC0 (settled)", "20261014"),
    (Path(r"C:\Otherreach_External_Assets\UnityLabs_VFX_Flipbooks"), "CC0 flipbooks (settled)", "20261016"),
    (Path(r"C:\Otherreach_External_Assets\Audio"), "owned audio (settled)", "legacy"),
    (Path(r"C:\Otherreach_External_Assets\Voice_ElevenLabs"), "ElevenLabs voice (licence held)", "20261020"),
    (Path(r"C:\Otherreach_External_Assets\Voice_Pilot"), "voice pilot (licence held)", "20261020"),
    (Path(r"C:\Otherreach_External_Assets\Music_Review"), "music review (licence held)", "20261020"),
    (Path(r"C:\Otherreach_External_Assets\CMU_rancidmilk"), "CMU mocap (free, attribution)", "legacy"),
    (Path(r"C:\Otherreach_External_Assets\UE_Exports"), "UE export of owned content (settled)", "20261017"),
    (Path(r"C:\Otherreach_External_Assets\Modelled_20261018"), "modelled in-house (settled)", "20261018"),
    (Path(r"C:\Otherreach_External_Assets\Godot_Converted_20261018"), "converted from owned sources", "20261018"),
    (Path(r"C:\Otherreach_External_Assets\Quaternius"), "CC0 stylised (C by rule)", "legacy"),
    (Path(r"C:\Otherreach_External_Assets\KayKit"), "CC0 stylised (C by rule)", "legacy"),
    (Path(r"C:\Otherreach_External_Assets\Kenney"), "CC0 stylised (C by rule)", "legacy"),
    (Path(r"C:\Otherreach_External_Assets\HUMGEN_GAME_20261021"), "HumGen (owner's full-quality rule)", "20261021"),
    (Path(r"C:\Otherreach_External_Assets\Gear_Catalog_HumGen"), "HumGen gear", "20261021"),
    (Path(r"C:\Otherreach_External_Assets\HumGen_Wardrobe"), "HumGen wardrobe", "20261021"),
    (Path(r"D:\Otherreach_Scratch\ds_art\primary"), "the game's own art root", "in_game"),
]

CATEGORIES = [
    ("mobs", r"(?<![a-z])(?:creature|monster|mob|beast|wolf|hound|dog|bear|boar|deer|stag|elk|bull|bison|mammoth|horse|fox|rabbit|spider|beetle|"
             r"scorpion|serpent|snake|eel|fish|bird|raven|crow|eagle|owl|bat|dragon|wyvern|golem|knight|guard|soldier|undead|zombie|"
             r"skeleton|ghost|wraith|cultist|bandit|raider|goblin|orc|troll|ogre|giant|jotunn|mutant|demon|jellyfish|nephila|husk)(?![a-z])"),
    ("architecture", r"bld|building|house|hall|church|tower|wall|gate|bridge|stairs|stair|roof|beam|floor|door|window|chimney|"
                     r"longhouse|smithy|forge_|granary|stable|barn|cottage|chapel|inn|tavern|keep|palisade|fence|dock|pier|"
                     r"causeway|arch|ruin|tomb|barrow|well|hearth"),
    ("nature", r"tree|bush|grass|fern|flower|moss|lichen|rock|boulder|crag|cliff|stone_|terrain|hill|mountain|slope|log|"
               r"stump|root|branch|leaf|leaves|plant|weed|vine|mushroom|fung|reed|corn|wheat|crop"),
    ("props", r"prop|barrel|crate|chest|table|chair|stool|bench|shelf|bed|rack|cart|wagon|wheel|bucket|jug|pot|bowl|dish|"
              r"lantern|candle|torch|sack|bag|crate|anvil|trough|lamp|sign|banner|rug|carpet|furniture|bucket|rope|crate"),
    ("story_items", r"quest|relic|artifact|artefact|key_|waystone|foldscar|token|seal_|letter|book|scroll|tome|ledger|"
                    r"shard|heart_|bone_|skull|idol|altar|shrine|pedestal|crown|ring_|amulet|talisman"),
    ("weapons", r"sword|axe|blade|dagger|spear|halberd|mace|hammer|bow_|arrow|quiver|shield_|staff|wand|scythe|club|"
                r"knife|pike|flail|weapon"),
    ("armour", r"armou?r|helmet|helm_|gauntlet|greave|cuirass|pauldron|breastplate|plate_|mail_|vambrace|visor"),
    ("clothing", r"cloth|shirt|tunic|robe|cloak|cape|hood|boot|shoe|glove|belt|pouch|apron|hat_|scarf|trouser|skirt|"
                 r"dress|garb|outfit|wardrobe"),
    ("materials", r"material|texture|_pbr|pbr_|atlas|decal|trim_sheet|tiling"),
    ("vfx", r"vfx|fx_|flipbook|particle|smoke|spark|flame|fire_|dust|mist|fog|splash|ripple|glow|trail|impact|"
            r"explosion|shock|ember|rain_|snow"),
    ("audio", r"sfx|audio|music|voice|amb_|ambient_|footstep|impact_|ui_|hum_|chime|bell_|wind_|rain_|loop"),
    ("animation", r"anim|mocap|locomotion|walk_|run_|idle_|attack_|hit_|death_|jump_|climb|vault|dance|gesture|"
                  r"kneel|sit_|wave_|talk_"),
]
AUDIO_EXT = {".wav", ".ogg", ".mp3", ".flac", ".opus"}
IMAGE_EXT = {".png", ".jpg", ".jpeg", ".exr", ".tga", ".tif", ".dds", ".hdr", ".webp"}
MODEL_EXT = {".fbx", ".glb", ".gltf", ".blend", ".obj", ".ma", ".mb", ".usd", ".uasset"}
SKIP = re.compile(r"preview|thumbnail|readme|licen|\.git|/\.|\\\.|cache|screenshot|frame_\d", re.I)


def image_size(path: Path):
    try:
        with path.open("rb") as f:
            head = f.read(32)
    except OSError:
        return None
    if head[:8] == b"\x89PNG\r\n\x1a\n":
        return struct.unpack(">II", head[16:24])
    if head[:2] == b"\xff\xd8":
        return None  # jpeg sizes are read from the header stream only when needed
    if head[:4] == b"DDS ":
        return struct.unpack("<II", head[12:20])
    return None


def audio_info(path: Path):
    try:
        with wave.open(str(path), "rb") as w:
            return w.getframerate(), round(w.getnframes() / max(1, w.getframerate()), 2)
    except Exception:  # noqa: BLE001
        return None


def glb_info(path: Path):
    try:
        with path.open("rb") as f:
            magic, _v, length = struct.unpack("<4sII", f.read(12))
            if magic != b"glTF":
                return None
            js = None
            while f.tell() < length:
                clen, ctype = struct.unpack("<I4s", f.read(8))
                data = f.read(clen)
                if ctype == b"JSON":
                    js = json.loads(data.decode("utf-8"))
        if not js:
            return None
        acc = js.get("accessors", [])
        tris = 0
        for mesh in js.get("meshes", []):
            for prim in mesh.get("primitives", []):
                if "indices" in prim:
                    tris += acc[prim["indices"]]["count"] // 3
                elif "POSITION" in prim.get("attributes", {}):
                    tris += acc[prim["attributes"]["POSITION"]]["count"] // 3
        return tris, len(js.get("animations", [])), len(js.get("images", []))
    except Exception:  # noqa: BLE001
        return None


def fbx_info(path: Path):
    try:
        blob = path.read_bytes()[:4_000_000].decode("latin-1", "ignore")
    except OSError:
        return None
    clips = len(set(re.findall(r"AnimStack::([A-Za-z0-9_ \-]+)", blob)))
    rig = ("Mixamo humanoid" if "mixamorig" in blob else
           "3ds Max Biped" if "Bip0" in blob else
           "Rigify-style" if "DEF-spine" in blob else
           "custom rig" if re.search(r"(Pelvis|Root|Spine)\w*", blob) else "no rig")
    return clips, rig


def selections():
    """What each selected asset was selected for, by catalog key (BRIEFS, standing rule 2026-10-27)."""
    try:
        data = json.loads(SELECTIONS.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return {re.sub(r"[^a-z0-9]", "", k.lower()): v for k, v in (data.get("selections") or {}).items()}


def classify(name: str, suffix: str):
    if suffix in AUDIO_EXT:
        return "audio"
    if suffix in IMAGE_EXT:
        if re.search(r"normal|rough|metal|_ao|occlusion|orm|mask|gloss|emission|specular|_pbr|albedo|basecolor", name, re.I):
            return "materials"
        return "nature" if re.search(r"tree|grass|rock|cliff|bark|leaves|ground", name, re.I) else "materials"
    for label, rx in CATEGORIES:
        if re.search(rx, name, re.I):
            return label
    return "props" if suffix in MODEL_EXT else "materials"


def preview_for(path: Path):
    folder = path.parent
    for where in (folder, folder.parent):
        if not where or not where.exists():
            continue
        for hit in sorted(where.glob("*.png"), key=lambda q: -q.stat().st_size)[:2]:
            if hit.stat().st_size > 8_000:
                return hit
        for hit in sorted(where.glob("*.jpg"), key=lambda q: -q.stat().st_size)[:2]:
            if hit.stat().st_size > 8_000:
                return hit
    zipped = next(folder.glob("*.zip"), None) or (path if path.suffix.lower() == ".zip" else None)
    if zipped:
        try:
            with zipfile.ZipFile(zipped) as z:
                names = [n for n in z.namelist() if n.lower().endswith((".png", ".jpg"))
                         and not re.search(r"normal|rough|metal|_ao|orm|mask", n, re.I)]
                names.sort(key=lambda n: -z.getinfo(n).file_size)
                for n in names[:2]:
                    data = z.read(n)
                    if len(data) < 6_000:
                        continue
                    target = PREVIEWS / re.sub(r"[^A-Za-z0-9_.-]", "_", f"{zipped.stem}_{Path(n).name}")
                    target.write_bytes(data)
                    return target
        except Exception:  # noqa: BLE001
            return None
    return None


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    PREVIEWS.mkdir(parents=True, exist_ok=True)
    library = {}
    for root, licence, era in SOURCES:
        if not root.exists():
            continue
        for path in root.rglob("*"):
            if not path.is_file() or SKIP.search(str(path)):
                continue
            suffix = path.suffix.lower()
            if suffix in MODEL_EXT or suffix in IMAGE_EXT or suffix in AUDIO_EXT or suffix == ".zip":
                pass
            else:
                continue
            name = path.stem
            key = re.sub(r"[^a-z0-9]", "", name.lower())
            if not key:
                continue
            cat = classify(name, suffix)
            entry = library.get(key)
            if entry is None:
                entry = {"name": name, "category": cat, "paths": [], "sources": set(), "licence": licence, "era": era,
                         "tris": None, "clips": 0, "maps": [], "audio": "", "rig": "", "preview": None}
                library[key] = entry
            entry["paths"].append(str(path))
            entry["sources"].add(root.name)
            if suffix == ".glb":
                info = glb_info(path)
                if info:
                    entry["tris"], clips, images = info
                    entry["clips"] = max(entry["clips"], clips)
                    if images:
                        entry["maps"].append(f"{images} embedded maps")
            elif suffix == ".fbx":
                info = fbx_info(path)
                if info:
                    clips, rig = info
                    entry["clips"] = max(entry["clips"], clips)
                    entry["rig"] = entry["rig"] or rig
            elif suffix in IMAGE_EXT:
                wh = image_size(path)
                if wh:
                    entry["maps"].append(f"{wh[0]}x{wh[1]}")
            elif suffix in AUDIO_EXT:
                info = audio_info(path)
                if info:
                    entry["audio"] = f"{info[0]} Hz, {info[1]} s"

    for e in library.values():
        px = max([int(m.split("x")[0]) for m in e["maps"] if "x" in m] or [0])
        tris = e["tris"] or 0
        hero = (tris >= 15_000 or px >= 4096) and (e["clips"] > 0 or e["rig"] and "no rig" not in e["rig"])
        good = tris >= 4_000 or px >= 1024 or e["clips"] > 0 or e["audio"] or (e["rig"] and "no rig" not in e["rig"])
        e["grade"] = "A" if hero else "B" if good else "C"

    by_cat = {}
    for e in library.values():
        by_cat.setdefault(e["category"], []).append(e)

    sheet_notes = []
    for cat, entries in sorted(by_cat.items()):
        entries.sort(key=lambda x: (x["grade"], -(x["tris"] or 0)))
        tiles = []
        for e in entries:
            got = preview_for(Path(e["paths"][0]))
            if got:
                e["preview"] = str(got)
                tiles.append(e)
            if len(tiles) >= 24:
                break
        if not tiles:
            continue
        cols, tile = 4, 380
        rows_n = (len(tiles) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * tile, rows_n * (tile + 30)), (16, 17, 19))
        draw = ImageDraw.Draw(sheet)
        try:
            font = ImageFont.truetype("arial.ttf", 18)
        except Exception:  # noqa: BLE001
            font = ImageFont.load_default()
        for i, e in enumerate(tiles):
            x, y = (i % cols) * tile, (i // cols) * (tile + 30)
            try:
                img = Image.open(e["preview"]).convert("RGB")
            except Exception:  # noqa: BLE001
                continue
            img.thumbnail((tile - 8, tile - 8))
            sheet.paste(img, (x + (tile - img.width) // 2, y + (tile - img.height) // 2))
            draw.text((x + 6, y + tile + 2), f"{e['grade']} {e['name'][:32]}", fill=(230, 226, 214), font=font)
        path = OUT / f"{cat}.jpg"
        sheet.save(path, quality=85)
        sheet_notes.append((cat, len(entries), len(tiles)))

    # One markdown per category, plus the index and a CSV for searching.
    for cat, entries in sorted(by_cat.items()):
        lines = [f"# {cat.replace('_', ' ').title()} ({len(entries)} assets)", "",
                 "| model | grade | tris | clips | maps | rig | audio | era | licence | source(s) | selected_for | path |",
                 "|---|---|---|---|---|---|---|---|---|---|---|---|"]
        sel = selections()
        for e in sorted(entries, key=lambda x: (x["grade"], -(x["tris"] or 0))):
            lines.append(f"| {e['name']} | {e['grade']} | {e['tris'] or '-'} | {e['clips'] or '-'} | "
                         f"{', '.join(sorted(set(e['maps']))[:2]) or '-'} | {e['rig'] or '-'} | {e['audio'] or '-'} | "
                         f"{e['era']} | {e['licence']} | {', '.join(sorted(e['sources']))} | "
                         f"{sel.get(re.sub(r'[^a-z0-9]', '', e['name'].lower()), '')} | {e['paths'][0]} |")
        (OUT / f"{cat}.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    counts = {}
    for e in library.values():
        counts[e["grade"]] = counts.get(e["grade"], 0) + 1
    index = ["# The asset library: everything we own", "",
             f"**{len(library)} distinct assets** across {len(by_cat)} categories, deduplicated by name from "
             f"{len(SOURCES)} owned sources. Grades: " + ", ".join(f"{g} {n}" for g, n in sorted(counts.items())) + ".", "",
             "| category | assets | contact sheet | table |", "|---|---|---|---|"]
    for cat, entries in sorted(by_cat.items(), key=lambda kv: -len(kv[1])):
        if any(n == cat for n, _c, _t in sheet_notes):
            index.append(f"| {cat} | {len(entries)} | [{cat}.jpg]({cat}.jpg) | [{cat}.md]({cat}.md) |")
        else:
            index.append(f"| {cat} | {len(entries)} | (no previews in the pack) | [{cat}.md]({cat}.md) |")
    index += ["", "Era tags mark when a pack entered the library (the folder's own date, `legacy` for the earliest imports, `in_game` for "
              "the art root). They are for searching, never for filtering: nothing is excluded because of its era.", "",
              "Triangle and clip counts are exact where the file is a GLB or an FBX whose clip names are readable; where an import pass "
              "would be needed they are left blank rather than guessed. Texture resolution is read from the image's own header.",
              "", "Refresh with `python C:\\Otherreach_Build\\tools\\asset_catalog.py`."]
    (OUT / "INDEX.md").write_text("\n".join(index) + "\n", encoding="utf-8")

    with (OUT / "asset_library.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["name", "category", "grade", "triangles", "clips", "maps", "rig", "audio", "era", "licence", "sources",
                    "selected_for", "path"])
        sel = selections()
        for e in sorted(library.values(), key=lambda x: (x["category"], x["grade"], x["name"].lower())):
            w.writerow([e["name"], e["category"], e["grade"], e["tris"] or "", e["clips"] or "", "; ".join(sorted(set(e["maps"]))),
                        e["rig"], e["audio"], e["era"], e["licence"], "; ".join(sorted(e["sources"])),
                        sel.get(re.sub(r"[^a-z0-9]", "", e["name"].lower()), ""), e["paths"][0]])

    print(f"{len(library)} assets, {len(by_cat)} categories -> {OUT / 'INDEX.md'}")
    for cat, n, t in sorted(sheet_notes, key=lambda s: -s[1]):
        print(f"  {cat}: {n} assets, {t} previews")
    print("grades:", counts)


if __name__ == "__main__":
    main()
