"""BRIEF D7: the complete mob library catalog (owner request, top priority). CPU only.

One row per DISTINCT creature / monster / animal / undead / construct / humanoid-enemy model across every source we own, with both paths
when the same asset lives in two places, its category, source and licence, triangles where the file lets them be read, texture resolution,
rig and clip count, a grade, and whether it is in the game today. Preview images are pulled out of the packs (from a folder next to the
model, or out of the pack's zip) and laid on one contact sheet per category with the name and grade printed on each tile.

Props, armour pieces and environment art are excluded by name. Nothing is downloaded; nothing goes to a generative model.
"""
import json
import re
import shutil
import struct
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

AUDIT = Path(r"C:\Otherreach_Build\mob_audit.json")
ART = Path(r"D:\Otherreach_Scratch\ds_art\primary")
OUT = Path(r"C:\Otherreach_External_Assets\mob_library")
SELECTIONS = Path(r"C:\Otherreach_External_Assets\selections.json")
PREVIEWS = OUT / "previews"

CATEGORIES = [
    ("canine", r"wolf|hound|dog|hyena|jackal|fox|coyote|grimhound|werewolf"),
    ("feline_bear", r"bear|lion|tiger|panther|leopard|lynx|jaguar|saber|sabre|cougar|puma|cat\b"),
    ("ungulate", r"deer|stag|elk|moose|bull|bison|buffalo|mammoth|horse|boar|pig|goat|sheep|antelope|rhino|elephant|donkey|ram\b|camel|zebra|giraffe"),
    ("bird", r"bird|raven|crow|eagle|owl|hawk|chicken|rooster|hen\b|vulture|falcon|parrot|penguin|duck|goose|swan|gull"),
    ("arthropod", r"spider|tarantula|nephila|scorpion|beetle|cockroach|insect|crab|arahnid|arachnid|ant\b|wasp|bee\b|moth|butterfly"),
    ("aquatic", r"eel|fish|shark|kraken|octopus|squid|serpent|snake|croc|frog|toad|turtle|whale|seal\b|jellyfish|stingray|piranha"),
    ("undead", r"undead|zombie|skeleton|ghost|wraith|corpse|drowned|lich|revenant|husk|bone_|skull|mummy|vampire"),
    ("construct", r"golem|automaton|construct|mech|robot|drone|armou?r_?golem|statue_?golem|elemental_|sentinel"),
    ("humanoid_enemy", r"knight|guard|soldier|warrior|raider|bandit|cultist|rogue|assassin|merc|samurai|viking|barbarian|fighter|archer|militia|swordsman|sorcerer|mage|shaman|boss_goblin"),
    ("monster", r"dragon|wyvern|jinn|jotunn|giant|troll|ogre|orc\b|goblin|demon|devil|mutant|parasite|sauropelta|beast_?monster|monster|creature_|kraken|hydra|basilisk|gargoyle|imp\b|slime"),
    ("armour_humanoid", r"animated_armou?r|empty_armou?r|armou?r_?walker|suit_of|full_?plate"),
]
PROP = re.compile(
    r"tree|bush|grass|flower|rock_?set|cliff|crate|barrel|wall_|\bwall\b|door|window|roof|furniture|chair|table|shelf|bed_|bench|"
    r"vehicle|\bcar\b|truck|weapon|sword|axe\d|helmet_?stand|shield_?rack|_texture|_material|_preview|readme|licen|_icon|thumbnail|"
    r"footstep|footprint|\bns_|anim_?pack|_lod\d\b|collision|_rig_?setup|skeleton_?setup|hide_|_hide|pelt|fang|antler|horn_stand|tusk_", re.I)
IN_GAME = set()
CREATURE_WORDS = re.compile(
    r"creature|monster|mob|beast|wolf|hound|dog|bear|boar|deer|stag|elk|moose|bull|bison|mammoth|horse|cat|fox|rabbit|hare|rat|mouse|"
    r"spider|insect|beetle|scorpion|serpent|snake|eel|fish|shark|bird|raven|crow|eagle|owl|bat|dragon|wyvern|golem|knight|guard|soldier|"
    r"undead|zombie|skeleton|ghost|wraith|cultist|bandit|raider|rogue|goblin|orc|troll|ogre|giant|jotunn|mutant|parasite|crab|worm|"
    r"slime|imp|demon|devil|elemental|construct|automaton|armour|armor|corpse|drowned|kraken|sauropelta|cockroach|nephila|jellyfish|"
    r"hyena|grimhound|jinn|sentinel|hydra|basilisk|gargoyle|werewolf|zombie", re.I)


def strip_rig(stem: str):
    """The model name as the bindings write it: no _rigged, no _rig, no low-detail suffix."""
    return re.sub(r"(_rigged|_rig|_lod\d+|-rigged)$", "", stem, flags=re.I)


def selections():
    """What each selected asset was selected for, by catalog key (BRIEFS, standing rule 2026-10-27)."""
    try:
        data = json.loads(SELECTIONS.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return {re.sub(r"[^a-z0-9]", "", k.lower()): v for k, v in (data.get("selections") or {}).items()}


def category(name: str):
    for label, rx in CATEGORIES:
        if re.search(rx, name, re.I):
            return label
    return "monster"


def clean_name(path: str, raw: str):
    name = Path(raw).stem
    name = re.sub(r"(_rigged|_rig|_LOD\d|_lod\d|[-_](?:rig|free|4k|2k|animated)|_f$|_m$)", "", name, flags=re.I)
    name = name.replace("__", " ").replace("_", " ").strip()
    return name or Path(raw).stem


def biggest_map(maps):
    best = 0
    for m in maps or []:
        found = re.findall(r"(\d{3,4})x(\d{3,4})", m)
        for w, h in found:
            best = max(best, int(w) * int(h))
    return best


def preview_for(path: Path):
    """An image that shows the model: one beside it in its folder, or the biggest one in its pack zip."""
    folder = path.parent if path.is_file() else path
    candidates = [folder, folder.parent, folder.parent.parent]
    art = ART / "ready" / path.stem
    if art.exists():
        candidates.insert(0, art)
    for where in candidates:
        if not where or not where.exists():
            continue
        for pattern in ("*preview*.png", "*preview*.jpg", "*.png", "*.jpg", "*.jpeg"):
            hits = sorted(where.glob(pattern), key=lambda q: -q.stat().st_size)
            for hit in hits[:4]:
                if hit.stat().st_size > 8_000 and "normal" not in hit.name.lower() and "rough" not in hit.name.lower():
                    return hit
    zip_sibling = path if path.suffix.lower() == ".zip" else next(folder.glob("*.zip"), None)
    if zip_sibling:
        try:
            with zipfile.ZipFile(zip_sibling) as z:
                images = [n for n in z.namelist() if n.lower().endswith((".png", ".jpg"))]
                images.sort(key=lambda n: -z.getinfo(n).file_size)
                for n in images[:6]:
                    if re.search(r"normal|rough|metal|_ao|occlusion|orm|mask|gloss|emission|specular", n, re.I):
                        continue
                    data = z.read(n)
                    if len(data) < 6_000:
                        continue
                    target = PREVIEWS / (re.sub(r"[^A-Za-z0-9_.-]", "_", zip_sibling.stem + "_" + Path(n).name))
                    target.write_bytes(data)
                    return target
        except Exception:  # noqa: BLE001
            return None
    return None


def main():
    rows = json.loads(AUDIT.read_text(encoding="utf-8"))
    bindings = json.loads((ART / "bindings.json").read_text(encoding="utf-8")).get("creatures", {})
    for spec in bindings.values():
        if isinstance(spec, dict) and spec.get("model"):
            IN_GAME.add(spec["model"].lower())

    library = {}
    for r in rows:
        name = r["name"]
        if PROP.search(name) or not CREATURE_WORDS.search(name):
            continue
        display = clean_name(r["file"], name)
        key = re.sub(r"[^a-z0-9]", "", display.lower())
        if not key:
            continue
        entry = library.get(key)
        if entry is None:
            library[key] = {
                "name": display,
                "category": category(name),
                "paths": [r["file"]],
                "sources": {r["source"]},
                "licence": r["licence"],
                "tris": r.get("tris"),
                "maps": r.get("maps") or [],
                "rig": r.get("rig") or "",
                "anims": r.get("anims") or 0,
                "in_game": strip_rig(Path(r["file"]).stem).lower() in IN_GAME,
            }
        else:
            entry["paths"].append(r["file"])
            entry["sources"].add(r["source"])
            entry["tris"] = entry["tris"] or r.get("tris")
            entry["maps"] = entry["maps"] or (r.get("maps") or [])
            entry["rig"] = entry["rig"] or (r.get("rig") or "")
            entry["anims"] = max(entry["anims"], r.get("anims") or 0)
            entry["in_game"] = entry["in_game"] or strip_rig(Path(r["file"]).stem).lower() in IN_GAME

    # Grade from what is actually known about each model: a measured triangle count, a map resolution, a rig and its clip count. A model
    # whose triangles could not be read without an import pass is not called a placeholder for it - it is graded on its rig, its clips and
    # its maps, and the table says "not measured" in the triangle column.
    for e in library.values():
        px = biggest_map(e["maps"])
        tris = e["tris"] or 0
        rigged = bool(e["rig"]) and "no rig" not in e["rig"].lower() and "not found" not in e["rig"].lower()
        hero = (tris >= 15_000 or px >= 4096 * 4096) and rigged and e["anims"] >= 1
        good = rigged or e["anims"] > 0 or px >= 1024 * 1024 or tris >= 4_000
        e["grade"] = "A" if hero else "B" if good else "C"

    PREVIEWS.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)

    # Preview per entry, and one contact sheet per category.
    by_cat = {}
    for key, e in library.items():
        e["preview"] = None
        for p in e["paths"]:
            got = preview_for(Path(p))
            if got:
                e["preview"] = str(got)
                break
        by_cat.setdefault(e["category"], []).append(e)

    sheet_notes = []
    for cat, entries in sorted(by_cat.items()):
        entries.sort(key=lambda x: (x["grade"], -(x["tris"] or 0)))
        tiles = [e for e in entries if e["preview"]][:24]
        if not tiles:
            continue
        cols, tile = 4, 420
        rows_n = (len(tiles) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * tile, rows_n * (tile + 34)), (18, 18, 20))
        draw = ImageDraw.Draw(sheet)
        try:
            font = ImageFont.truetype("arial.ttf", 20)
        except Exception:  # noqa: BLE001
            font = ImageFont.load_default()
        for i, e in enumerate(tiles):
            x, y = (i % cols) * tile, (i // cols) * (tile + 34)
            try:
                img = Image.open(e["preview"]).convert("RGB")
            except Exception:  # noqa: BLE001
                continue
            img.thumbnail((tile - 8, tile - 8))
            sheet.paste(img, (x + (tile - img.width) // 2, y + (tile - img.height) // 2))
            label = f"{e['grade']} {e['name'][:34]}"
            draw.text((x + 6, y + tile + 4), label, fill=(232, 228, 214), font=font)
        target = OUT / f"sheet_{cat}.jpg"
        sheet.save(target, quality=86)
        sheet_notes.append((cat, len(entries), len(tiles), target.name))

    sel = selections()
    lines = ["# The mob library: every creature model we own", "",
             f"{len(library)} distinct creature, monster, animal, undead, construct and humanoid-enemy models, from every owned source, "
             "deduplicated by name. Props, armour pieces and environment art are excluded by name. Contact sheets: "
             + ", ".join(f"`{n}` ({c} models, {t} previews)" for c, c, t, n in [(a, b, c, d) for a, b, c, d in sheet_notes]) + ".", "",
             "| # | model | category | grade | tris | maps | rig | clips | source(s) | licence | in game | selected_for | path(s) |",
             "|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for i, e in enumerate(sorted(library.values(), key=lambda x: (x["category"], x["grade"], -(x["tris"] or 0))), 1):
        maps = ", ".join(sorted({m.split(" ")[-1] for m in e["maps"] if " " in m})[:2]) or "-"
        tris = f"{e['tris']:,}" if e["tris"] else "-"
        paths = " ; ".join(e["paths"][:2])
        lines.append(f"| {i} | {e['name']} | {e['category']} | {e['grade']} | {tris} | {maps} | {e['rig'] or '-'} | "
                     f"{e['anims'] or '-'} | {', '.join(sorted(e['sources']))} | {e['licence']} | "
                     f"{'yes' if e['in_game'] else 'no'} | {sel.get(re.sub(r'[^a-z0-9]', '', e['name'].lower()), '')} | {paths} |")
    counts = {}
    for e in library.values():
        counts[e["grade"]] = counts.get(e["grade"], 0) + 1
    lines += ["", f"**Total: {len(library)} models** - " + ", ".join(f"grade {g}: {n}" for g, n in sorted(counts.items())) +
              f". In game today: {sum(1 for e in library.values() if e['in_game'])}."]
    (OUT / "MOB_LIBRARY.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{len(library)} models -> {OUT / 'MOB_LIBRARY.md'}")
    for cat, n, t, name in sheet_notes:
        print(f"  sheet {name}: {n} models, {t} previews")
    print("grades:", counts)


if __name__ == "__main__":
    main()
