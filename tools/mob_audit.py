"""All-sources mob and creature asset audit (BRIEF D5). CPU only.

Walks every owned source of creature-shaped art - the depot packs, the external-asset sources, the UE Fab projects, the game's art root and
the HumGen bodies - and reports, per model: where it is, how many triangles (GLB read directly; FBX/zip listed but not tessellated),
what textures it carries and at what size, what rig and animations the file names, a licence label taken from the source folder, and a
grade. It writes a machine-readable JSON plus a markdown source table; the per-creature ranking and the gaps are written by hand from it.

Nothing is extracted into the game and nothing is downloaded.
"""
import json
import re
import struct
import sys
import zipfile
from pathlib import Path

OUT_JSON = Path(r"C:\Otherreach_Build\mob_audit.json")
OUT_MD = Path(r"C:\Otherreach_Build\mob_audit_sources.md")

# Every root the brief names, with the licence label we hold for it. A label is "settled" (we may ship it and we know its terms) or
# "check" (known source, terms need a line in the licence log before it ships).
SOURCES = [
    (Path(r"D:\Otherreach_Depot\Creatures_20261017"), "Fab Standard (settled)", "A/B"),
    (Path(r"D:\Otherreach_Depot\Fab_Creatures_Free_20261018"), "Fab Free (settled)", "B/C"),
    (Path(r"D:\Otherreach_Depot\models"), "mixed / check manifest", "check"),
    (Path(r"D:\Otherreach_Depot\UE_Exports"), "UE export of owned Fab content (settled)", "A/B"),
    (Path(r"C:\Otherreach_External_Assets\Fab"), "Fab Standard (settled)", "A/B"),
    (Path(r"C:\Otherreach_External_Assets\Fab_Free_20261016"), "Fab Free (settled)", "B/C"),
    (Path(r"C:\Otherreach_External_Assets\Cosmos_Premium"), "Cosmos Premium (settled for 3D; never to generative AI)", "A"),
    (Path(r"C:\Otherreach_External_Assets\Sketchfab_20261016"), "Sketchfab CC-BY (attribution required)", "B"),
    (Path(r"C:\Otherreach_External_Assets\BlenderKit"), "BlenderKit royalty-free (settled)", "B"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_BlenderKit_20261018"), "BlenderKit royalty-free (settled)", "B"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_CC0_20261018"), "CC0 (settled)", "B/C"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_20261018"), "mixed sourced (check per pack)", "check"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_20261018_Additional"), "mixed sourced (check per pack)", "check"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_20261018_Canon_Gaps"), "mixed sourced (check per pack)", "check"),
    (Path(r"C:\Otherreach_External_Assets\Sourced_DS_20261018"), "mixed sourced (check per pack)", "check"),
    (Path(r"C:\Otherreach_External_Assets\UE_Exports"), "UE export of owned content (settled)", "A/B"),
    (Path(r"C:\Otherreach_External_Assets\Godot_Converted_20261018"), "converted from owned sources (settled)", "B"),
    (Path(r"C:\Otherreach_External_Assets\Modelled_20261018"), "modelled in-house (settled)", "A/B"),
    (Path(r"C:\Otherreach_External_Assets\Stylized_Borderline_20261016"), "stylised (C-grade only)", "C"),
    (Path(r"C:\Otherreach_External_Assets\Quaternius"), "CC0 stylised (C-grade fallback only)", "C"),
    (Path(r"C:\Otherreach_External_Assets\KayKit"), "CC0 stylised (C-grade fallback only)", "C"),
    (Path(r"C:\Otherreach_External_Assets\Kenney"), "CC0 stylised (C-grade fallback only)", "C"),
    (Path(r"C:\Otherreach_External_Assets\WildMesh"), "WildMesh (licensed, settled)", "A/B"),
    (Path(r"C:\Users\jluca\Documents\Unreal Projects\FabAssets"), "Fab Standard, pulled into UE (settled)", "A"),
    (Path(r"C:\Users\jluca\Documents\Unreal Projects\FabStaging_56"), "Fab Standard staging (settled)", "A/B"),
    (Path(r"D:\Otherreach_Scratch\ds_art\primary"), "the game's own art root (settled)", "A/B"),
    (Path(r"C:\Otherreach_External_Assets\HUMGEN_GAME_20261021"), "HumGen (owner's full-quality rule)", "A"),
    (Path(r"C:\Otherreach_External_Assets\HumGen_Wardrobe"), "HumGen wardrobe", "A"),
    (Path(r"C:\Otherreach_External_Assets\Race_Bodies"), "HumGen race bodies", "A"),
]

MODEL_EXT = {".fbx", ".glb", ".gltf", ".blend", ".obj", ".ma", ".mb", ".usd", ".uasset"}
CREATURE_WORDS = re.compile(
    r"creature|monster|mob|beast|wolf|hound|dog|bear|boar|deer|stag|elk|moose|bull|bison|mammoth|horse|cat|fox|rabbit|hare|rat|mouse|"
    r"spider|insect|beetle|scorpion|serpent|snake|eel|fish|shark|bird|raven|crow|eagle|owl|bat|dragon|wyvern|golem|knight|guard|soldier|"
    r"undead|zombie|skeleton|ghost|wraith|cultist|bandit|raider|rogue|goblin|orc|troll|ogre|giant|jotunn|mutant|parasite|crab|worm|"
    r"slime|imp|demon|devil|elemental|spirit|construct|automaton|armour|armor|corpse|drowned|eel|kraken|sauropelta|cockroach|nephila",
    re.I)
REJECT_WORDS = re.compile(
    r"tree|bush|grass|flower|rock|cliff|crate|barrel|wall|door|window|roof|furniture|vehicle|car|weapon|sword|armour_set|"
    r"texture|material|rig_setup|readme|license|preview|thumbnail|icon|fbxkey|\.png|\.jpg", re.I)


def png_dims(data: bytes):
    if data[:8] == b"\x89PNG\r\n\x1a\n" and len(data) >= 24:
        return struct.unpack(">II", data[16:24])
    if data[:2] == b"\xff\xd8" and len(data) > 40:
        i = 2
        while i + 9 < len(data):
            if data[i] != 0xFF:
                i += 1
                continue
            m = data[i + 1]
            if m in (0xC0, 0xC1, 0xC2):
                h, w = struct.unpack(">HH", data[i + 5:i + 9])
                return w, h
            if i + 4 > len(data):
                break
            i += 2 + struct.unpack(">H", data[i + 2:i + 4])[0]
    if data[:4] == b"DDS " and len(data) >= 20:
        return struct.unpack("<II", data[12:20])
    return None


def glb_tris(path: Path):
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
        accessors = js.get("accessors", [])
        tris = 0
        for mesh in js.get("meshes", []):
            for prim in mesh.get("primitives", []):
                if "indices" in prim:
                    tris += accessors[prim["indices"]]["count"] // 3
                elif "POSITION" in prim.get("attributes", {}):
                    tris += accessors[prim["attributes"]["POSITION"]]["count"] // 3
        return {"tris": tris, "anims": [a.get("name", "?") for a in js.get("animations", [])],
                "joints": max((len(s.get("joints", [])) for s in js.get("skins", [])), default=0)}
    except Exception:  # noqa: BLE001
        return None


def fbx_rig(blob: bytes):
    text = blob.decode("latin-1", "ignore")
    rig = ("Mixamo humanoid" if "mixamorig" in text else
           "3ds Max Biped" if "Bip0" in text else
           "Rigify-style deform" if "DEF-spine" in text else
           "humanoid (Hips naming)" if "Hips" in text else
           "custom creature rig" if re.search(r"(Root|Pelvis|Spine)\w*", text) else "no rig names")
    anims = len(set(re.findall(r"AnimStack::([A-Za-z0-9_ ]+)", text)))
    return rig, anims


def zips_of(folder: Path):
    return [p for p in folder.rglob("*.zip") if p.is_file()]


def scan(folder: Path):
    """Every creature-shaped model in a source folder, with what can be learned from the files without extracting anything."""
    found = []
    if not folder.exists():
        return found
    for path in folder.rglob("*"):
        if not path.is_file():
            continue
        lower = path.name.lower()
        if path.suffix.lower() in MODEL_EXT and CREATURE_WORDS.search(path.stem) and not REJECT_WORDS.search(path.stem):
            row = {"file": str(path), "name": path.stem, "kind": path.suffix.lower().lstrip("."), "tris": None,
                   "maps": [], "rig": "", "anims": 0}
            if path.suffix.lower() == ".glb":
                info = glb_tris(path)
                if info:
                    row.update({"tris": info["tris"], "anims": len(info["anims"]), "rig": f"{info['joints']} joints"})
            elif path.suffix.lower() == ".fbx":
                try:
                    rig, anims = fbx_rig(path.read_bytes()[:6_000_000])
                    row.update({"rig": rig, "anims": anims})
                except Exception:  # noqa: BLE001
                    pass
            found.append(row)
    for z in zips_of(folder):
        if not CREATURE_WORDS.search(z.stem):
            continue
        try:
            with zipfile.ZipFile(z) as zf:
                names = zf.namelist()
                models = [n for n in names if Path(n).suffix.lower() in (".fbx", ".glb", ".blend", ".obj")]
                maps = []
                for n in names:
                    if Path(n).suffix.lower() in (".png", ".jpg", ".tga"):
                        with zf.open(n) as f:
                            head = f.read(32)
                        wh = png_dims(head)
                        if wh:
                            maps.append(f"{Path(n).name} {wh[0]}x{wh[1]}")
                        if len(maps) >= 4:
                            break
                rig = ""
                anims = 0
                fbx = [n for n in models if n.lower().endswith(".fbx")]
                if fbx:
                    blob = zf.read(fbx[0])[:4_000_000]
                    rig, anims = fbx_rig(blob)
                found.append({"file": str(z), "name": z.stem, "kind": "zip", "tris": None, "maps": maps,
                              "rig": rig, "anims": anims, "models": [Path(m).name for m in models][:6]})
        except Exception:  # noqa: BLE001
            continue
    return found


def grade(row, source_grade):
    if row.get("tris"):
        t = row["tris"]
        anims = row.get("anims", 0)
        biggest = max([int(m.split(" ")[-1].split("x")[0]) for m in row.get("maps", []) if " " in m] or [0])
        if t >= 15000 and anims >= 3 and biggest >= 2048:
            return "A"
        if t >= 4000 and biggest >= 1024:
            return "B"
        return "C"
    if source_grade == "C":
        return "C"
    return "B" if row.get("anims") or row.get("rig") else "C"


def main():
    rows = []
    for folder, licence, source_grade in SOURCES:
        hits = scan(folder)
        for row in hits:
            row["source"] = folder.name
            row["licence"] = licence
            row["grade"] = grade(row, source_grade)
            rows.append(row)
    OUT_JSON.write_text(json.dumps(rows, indent=1), encoding="utf-8")

    lines = ["# Mob and creature assets, all sources (BRIEF D5, CPU)", "",
             f"{len(rows)} creature-shaped models found across {len(SOURCES)} owned sources.", "",
             "| source | model | kind | tris | maps (largest) | rig | anims | licence | grade |",
             "|---|---|---|---|---|---|---|---|---|"]
    rows.sort(key=lambda r: (r["grade"], -(r.get("tris") or 0)))
    for r in rows:
        maps = "; ".join(r["maps"][:2]) or "-"
        tris = f"{r['tris']:,}" if r.get("tris") else "-"
        lines.append(f"| {r['source']} | {r['name']} | {r['kind']} | {tris} | {maps} | {r['rig'] or '-'} | "
                     f"{r['anims'] or '-'} | {r['licence']} | {r['grade']} |")
    OUT_MD.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{len(rows)} models -> {OUT_MD}")
    by_source = {}
    for r in rows:
        by_source[r["source"]] = by_source.get(r["source"], 0) + 1
    for k, v in sorted(by_source.items(), key=lambda kv: -kv[1]):
        print(f"  {k}: {v}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
