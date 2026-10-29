"""Writes src/Presentation/Art/humgen_creator.json: what the HumGen pipeline can produce, as the character creator offers it.

Reads the HumGen content folder directly (no Blender): the model presets (faces), head and face hair presets, outfits and footwear,
and the livekey files behind the body and face sliders. Every option keeps the content-relative path HumGen's own
get_options()/set() use, per sex, so an offline bake can turn a saved choice back into a HumGen call. The races, their statures
(RACES.md "Adult stature") and fit families (RACE_SCALE_SPEC.md section 8) and the baked player bodies are stated here.

usage: python tools/creator/humgen_catalogue.py [content_dir] [out.json]
"""
import json
import re
import sys
from pathlib import Path

CONTENT = Path(sys.argv[1] if len(sys.argv) > 1 else r"D:\HumGen Content")
OUT = Path(sys.argv[2] if len(sys.argv) > 2 else Path(__file__).resolve().parents[2] / "src/Presentation/Art/humgen_creator.json")
SEXES = {"f": "female", "m": "male"}

# RACES.md "Adult stature" (owner-approved 2026-10-23) and RACE_SCALE_SPEC.md sections 1 and 8. Only the Veth and Orenth are a
# human body HumGen makes as it ships; every other people needs its own morphology first, so the creator lists them as pending.
RACES = [
    ("people.veth", "Veth", "The touched lineage", 160, 190, "standard", "standard", True),
    ("people.orenth", "Orenth", "The walkers", 160, 190, "standard", "standard", True),
    ("people.kal", "Kal", "The calcified", 150, 175, "broad", "compact_broad", True),
    ("people.siann", "Siann", "The long view", 140, 155, "slight", "compact_broad", True),
    ("people.mor", "Mor", "The stolen", 160, 190, "nonphysical", "non_physical", False),
    ("people.constructed", "Constructed", "The written", 210, 230, "lean_long", "modular", False),
    ("people.vaskaal", "Vaskaal", "The void-farers", 220, 250, "lean_long", "tall_narrow", False),
    ("people.ondrek", "Ondrek", "The read-stone", 200, 260, "heavy", "heavy", False),
]

# The player bodies the production cast bakes (claude_motion_gate humgen_production_native.py CAST and HAIR): only these
# combinations exist as GLBs with packaged motion. Everything else a draft chooses is pending bake.
BAKED = [
    ("humgen_player_ada_f", "f", "models/female/Caucasian presets/Caucasian 1.json", 24, 170, "hair/head/female/Long/Ponytail.json", None,
     "outfits/female/Casual/Smart_Casual.blend", "footwear/female/Outdoor/HG_Garden_Boot_Female.blend"),
    ("humgen_player_mateo_m", "m", "models/male/Caucasian Presets/Caucasian 2.json", 29, 182, "hair/head/male/Long/Ponytail Short.json",
     "hair/face_hair/Other/Stubble_Short.json", "outfits/male/Casual/Casual_Weekday.blend", "footwear/male/Outdoor/HG_Garden_Boot_Male.blend"),
]

# The race bodies and the Veth face variants the cast pipelines have already baked into the art root. They are not choices
# of HumGen's content folder but of our own exports, so they are named by catalogue id: the creator offers them as baked.
EXPORTED = [
    ("humgen_race_kal_f_compact", "f", None, "face.f.caucasian_presets.caucasian_4", 44, 150, None, None),
    ("humgen_race_kal_m_compact", "m", None, "face.m.caucasian_presets.caucasian_4", 48, 155, None, None),
    ("humgen_race_siann_f_long", "f", None, "face.f.caucasian_presets.caucasian_1", 24, 142, "hair.long.medium_center_part", None),
    ("humgen_race_siann_m_long", "m", None, "face.m.caucasian_presets.caucasian_2", 27, 150, "hair.long.medium_center_part", None),
    ("humgen_race_orenth_f_frontier", "f", None, "face.f.caucasian_presets.caucasian_3", 34, 168, "hair.long.bob_short", None),
    ("humgen_race_orenth_m_frontier", "m", None, "face.m.caucasian_presets.caucasian_1", 38, 188, "hair.short.short_combed", None),
    ("humgen_veth_m_face_holt", "m", "humgen_player_mateo_m", "face.veth.m_holt", 30, 178, "hair.veth.m_holt", "facehair.veth.m_holt"),
    ("humgen_veth_m_face_marsh", "m", "humgen_player_mateo_m", "face.veth.m_marsh", 30, 178, "hair.veth.m_marsh", None),
    ("humgen_veth_m_face_tarr", "m", "humgen_player_mateo_m", "face.veth.m_tarr", 30, 178, "hair.veth.m_tarr", "facehair.veth.m_tarr"),
    ("humgen_veth_f_face_holt", "f", "humgen_player_ada_f", "face.veth.f_holt", 30, 168, "hair.veth.f_holt", None),
    ("humgen_veth_f_face_marsh", "f", "humgen_player_ada_f", "face.veth.f_marsh", 30, 168, "hair.veth.f_marsh", None),
    ("humgen_veth_f_face_tarr", "f", "humgen_player_ada_f", "face.veth.f_tarr", 30, 168, "hair.veth.f_tarr", None),
]

# Sliders: (id, label, panel, group, HumGen key, min, max). Livekey values are HumGen's soft range, stored as thousandths.
BODY = [("Build", ["muscular", "overweight", "skinny"], 0, 1000),
        ("Torso", ["Shoulder Width", "Chest Width", "Chest Height", "Breast Size", "Belly Size", "Waist Thickness", "Hips Size", "Hips Height"], -1000, 1000),
        ("Arms", ["Upper Arm Length", "Upper Arm Thickness", "Forearm Length", "Forearm Thickness", "Hand Length", "Hand Width"], -1000, 1000),
        ("Legs", ["Thigh Length", "Thigh Thickness", "Shin Length", "Shin Thickness", "Foot Length"], -1000, 1000),
        ("Neck", ["Neck Length", "Neck Thickness"], -1000, 1000),
        ("Muscles", ["Shoulder Muscles", "Chest Muscles", "Back Muscles", "Biceps", "Triceps", "Forearm Muscles", "Quad Muscles", "Calves Muscles"], -1000, 1000)]
FACE_GROUPS = ["u_skull", "eyes", "nose", "cheeks", "mouth", "jaw", "chin", "ears"]
FACE_GROUP_LABELS = {"u_skull": "Brow and forehead", "eyes": "Eyes", "nose": "Nose", "cheeks": "Cheeks", "mouth": "Mouth", "jaw": "Jaw",
                     "chin": "Chin", "ears": "Ears"}


def slug(text):
    return re.sub(r"_+", "_", re.sub(r"[^a-z0-9]+", "_", text.lower())).strip("_")


def label(text):
    text = re.sub(r"^HG_", "", text).replace("_", " ").replace("[", "(").replace("]", ")")
    text = re.sub(r"\b(Female|Male)\b", "", text)
    return re.sub(r"\s+", " ", text).strip()


def rel(path):
    return str(path.relative_to(CONTENT)).replace("/", "\\")


def shared(kind, root, pattern, sex_folder=True):
    """Options that exist per sex under the same group and name merge into one choice with a path for each sex."""
    options = {}
    for sex, folder in SEXES.items():
        base = CONTENT / root / folder if sex_folder else CONTENT / root
        for path in sorted(base.rglob(pattern)) if base.exists() else []:
            if "textures" in path.parts:
                continue
            group = path.parent.name if path.parent != base else ""
            option_id = f"{kind}.{slug(group) + '.' if group else ''}{slug(label(path.stem))}"
            entry = options.setdefault(option_id, {"id": option_id, "label": label(path.stem), "group": group, "paths": {}})
            entry["paths"][sex] = rel(path)
    return list(options.values())


def faces(sliders):
    """Every model preset, with the slider values loading it sets (those away from the slider's default)."""
    out = []
    for sex, folder in SEXES.items():
        for path in sorted((CONTENT / "models" / folder).rglob("*.json")):
            data = json.loads(path.read_text(encoding="utf-8"))
            values = {}
            for slider in sliders:
                key = slider["humgen"]
                if key == "age":
                    value = data["age"]["set"]
                elif key.startswith("eyes."):
                    continue
                elif key.startswith("brows."):
                    # The brows carry their own colour in the preset, apart from the head hair's.
                    value = data["hair"]["eyebrows"][key.split(".", 1)[1]] / slider["scale"]
                elif key.startswith(("skin.", "hair.")):
                    part, name = key.split(".")
                    value = (data["skin"] if part == "skin" else data["hair"]["regular_hair"])[name] / slider["scale"]
                else:
                    value = data["keys"].get(key, 0) / slider["scale"]
                if round(value) != slider["default"]:
                    values[slider["id"]] = round(value)
            group = path.parent.name.replace(" presets", "").replace(" Presets", "")
            out.append({"id": f"face.{sex}.{slug(path.parent.name)}.{slug(path.stem)}", "label": path.stem, "group": group,
                        "paths": {sex: rel(path)}, "values": values})
    return out


def face_hair():
    out = []
    for path in sorted((CONTENT / "hair" / "face_hair").rglob("*.json")):
        option_id = f"facehair.{slug(path.parent.name)}.{slug(path.stem)}"
        # HumGen ships face hair for the male body only (get_options() on a female human lists none).
        out.append({"id": option_id, "label": label(path.stem), "group": path.parent.name, "paths": {"m": rel(path)}})
    return out


def livekey_sliders():
    sliders = []
    for group, keys, lo, hi in BODY:
        for key in keys:
            sliders.append({"id": f"body.{slug(key)}", "label": key.capitalize() if key.islower() else key, "panel": "body", "group": group,
                            "humgen": key, "min": lo, "max": hi, "default": 0, "scale": 0.001})
    for group in FACE_GROUPS:
        for path in sorted((CONTENT / "livekeys" / "face_proportions" / group).glob("*.npz")):
            if not re.fullmatch(r"[a-z_]+", path.stem):
                continue
            words = path.stem.replace("_", " ")
            sliders.append({"id": f"face.{path.stem}", "label": words[0].upper() + words[1:], "panel": "face",
                            "group": FACE_GROUP_LABELS[group], "humgen": path.stem, "min": -1000, "max": 1000, "default": 0, "scale": 0.001})
    return sliders


def preset_ranges():
    """Skin, hair, brow and iris colour ranges, as the shipped model presets span them."""
    values = {}
    for path in (CONTENT / "models").rglob("*.json"):
        if path.name == "face_rig.json":
            continue
        data = json.loads(path.read_text(encoding="utf-8"))
        for key in ("tone", "redness", "saturation"):
            values.setdefault(("skin", key), []).append(data["skin"][key])
        for key in ("lightness", "redness"):
            values.setdefault(("hair", key), []).append(data["hair"]["regular_hair"][key])
            values.setdefault(("brows", key), []).append(data["hair"]["eyebrows"][key])
    sliders = [{"id": "face.age", "label": "Age", "panel": "face", "group": "Age", "humgen": "age", "min": 18, "max": 90, "default": 30, "scale": 1}]
    for (part, key), seen in sorted(values.items()):
        sliders.append({"id": f"{part}.{key}", "label": key.capitalize(), "panel": "face" if part != "hair" else "hair",
                        "group": "Skin" if part == "skin" else "Brows" if part == "brows" else "Hair colour", "humgen": f"{part}.{key}",
                        "min": round(min(seen) * 1000), "max": round(max(seen) * 1000), "default": round(sorted(seen)[len(seen) // 2] * 1000),
                        "scale": 0.001})
    # The iris itself is baked per face preset; this lights or dims that authored colour.
    sliders.append({"id": "eyes.lightness", "label": "Iris lightness", "panel": "face", "group": "Eyes", "humgen": "eyes.lightness",
                    "min": 300, "max": 2000, "default": 1000, "scale": 0.001})
    return sliders


def main():
    hair = shared("hair", "hair/head", "*.json")
    outfits = shared("outfit", "outfits", "*.blend")
    footwear = shared("footwear", "footwear", "*.blend")
    # Our own frontier bake fixtures sit beside HumGen's outfits in the same folder: the creator
    # offers HumGen's content, and those have no native export behind them.
    outfits = [option for option in outfits if option["group"].lower() != "frontier"]
    sliders = livekey_sliders() + preset_ranges()
    face_options = faces(sliders)
    # A preset may set a key past the slider's soft range (HumGen allows -10..10): the slider reaches every preset's value.
    for slider in sliders:
        seen = [f["values"][slider["id"]] for f in face_options if slider["id"] in f["values"]]
        slider["min"], slider["max"] = min([slider["min"], *seen]), max([slider["max"], *seen])
    beards = face_hair()

    def by_path(options, path):
        if path is None:
            return None
        wanted = path.replace("/", "\\")
        return next(o["id"] for o in options if wanted in o["paths"].values())

    baked = [{"model": model, "sex": sex, "face": by_path(face_options, face), "age": age, "stature_cm": height, "hair": by_path(hair, head),
              "facial_hair": by_path(beards, beard), "outfit": by_path(outfits, outfit), "footwear": by_path(footwear, shoes)}
             for model, sex, face, age, height, head, beard, outfit, shoes in BAKED]
    # Every baked body wears the frontier outfit and its boots.
    frontier = next(o["id"] for o in outfits if o["label"] == "Pirate")
    boots = next(o["id"] for o in footwear if "Pirate" in o["label"])
    for model, sex, face_of, face, age, height, head, beard in EXPORTED:
        baked.append({"model": model, "sex": sex, "face": face, "age": age, "stature_cm": height, "hair": head, "facial_hair": beard,
                      "outfit": frontier, "footwear": boots, **({"face_of": face_of} if face_of else {})})
    catalogue = {
        "comment": "Written by tools/creator/humgen_catalogue.py from the HumGen 4.0.38 content folder. Paths are content-relative, as HumGen's "
                   "get_options()/set() take them. Only the baked bodies exist as GLBs with motion; any other choice is pending bake.",
        "humgen": "4.0.38",
        "races": [{"people": people, "label": name, "epithet": epithet, "stature_cm": [lo, hi], "build": build, "fit_family": fit,
                   "humgen_body": body} for people, name, epithet, lo, hi, build, fit, body in RACES],
        "baked": baked,
        "sliders": sliders,
        "faces": face_options,
        "hair": hair,
        "facial_hair": beards,
        "outfits": outfits,
        "footwear": footwear,
    }
    OUT.write_text(json.dumps(catalogue, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{OUT}: {len(face_options)} faces, {len(hair)} hair, {len(beards)} facial hair, {len(outfits)} outfits, {len(footwear)} footwear, "
          f"{len(catalogue['sliders'])} sliders")


main()
