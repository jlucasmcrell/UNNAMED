"""Assemble the Wave 0 proof set into one obvious, self-contained folder.

The 15 were mixed into `ready/` alongside 302 other assets, which makes "the things we
actually test with" impossible to find without cross-referencing a request file. This builds
`assets\\PROOFSET\\` containing the 15, a README and a machine-readable manifest.

The folder holds **copies**, not moves. `ready/` stays the canonical production tree that the
pack verifier checks and that the game imports; this is a convenience view of a subset of it,
so that nothing which expects an asset in `ready/` breaks when the proof set is rebuilt.

Usage:
    python _make_proofset_folder.py
"""
import json
import os
import shutil
import sys

ASSETS = r"W:\UNNAMED\assets"
SOURCE = os.path.join(ASSETS, "ready")
DEST = os.path.join(ASSETS, "PROOFSET")

# What each asset is, and any caveat a tester must know before trusting it. The caveats are
# the important part: an asset can validate cleanly and still be the wrong object.
NOTES = {
    "weaponcomp_haft_short_a": ("weapon component", "correct - short haft with grip, head and pommel sockets"),
    "weaponcomp_haft_long_a": ("weapon component", "correct - long haft, two-hand grip positions"),
    "weaponcomp_blade_arming_sword_a": ("weapon component", "correct - bare blade with tang"),
    "weaponcomp_grip_vaskaal_a": ("weapon component", "correct cut - but the concept lacks the 3+2 finger channels"),
    "weaponcomp_mace_head_flanged_a": ("weapon component", "cut correct - concept drew a double-headed hammer, not a flanged mace"),
    "weaponcomp_shield_heater_a": ("weapon component", "correct - whole object, planar forearm mount, no stub"),
    "weaponcomp_grip_standard_a": ("weapon component", "correct - a complete hilt: pommel, leather grip and crossguard, blade stub removed"),
    "weaponcomp_pommel_counterweight_a": ("weapon component", "correct - faceted iron counterweight, rebuilt from a refined concept, haft stub removed"),
    "weaponcomp_mechanism_telescope_a": ("weapon component", "correct - nested tubes, mechanism and deploy sockets"),
    "armour_chest_plate_base_a": ("armour", "socketed - generated fit, NOT canonical Method C fit"),
    "armour_chest_underlayer_gambeson_a": ("armour", "socketed - generated fit, a garment not a shell"),
    "armour_gorget_plate_a": ("armour", "socketed - generated fit, NOT canonical Method C fit"),
    "armour_kal_back_channel_a": ("armour", "socketed - has Kal wing channel sockets L and R"),
    "magiccomp_focus_crystal_a": ("magic", "correct - faceted crystal in its brass claw mount, staff rod removed"),
    "weapon_hybrid_focus_staff_spear_a": ("hybrid", "correct - stowed staff and deployed spear, mode-dependent grip"),
}


def main():
    with open(os.path.join(ASSETS, "requests", "wave0_proofset.json"), encoding="utf-8") as h:
        proofset = [x["id"] for x in json.load(h)]

    if os.path.isdir(DEST):
        shutil.rmtree(DEST)
    os.makedirs(DEST)

    manifest = []
    for asset_id in proofset:
        src = os.path.join(SOURCE, asset_id)
        if not os.path.isdir(src):
            print(f"  MISSING in ready/: {asset_id}")
            continue
        shutil.copytree(src, os.path.join(DEST, asset_id))
        category, note = NOTES.get(asset_id, ("unknown", ""))
        manifest.append({
            "asset_id": asset_id,
            "category": category,
            "note": note,
            "broken": note.startswith("BROKEN"),
            "files": sorted(os.listdir(os.path.join(DEST, asset_id))),
        })
        mark = "BROKEN " if note.startswith("BROKEN") else "ok     "
        print(f"  {mark} {asset_id}")

    with open(os.path.join(DEST, "proofset.json"), "w", encoding="utf-8") as h:
        json.dump(manifest, h, indent=2)

    good = sum(1 for m in manifest if not m["broken"])
    readme = f"""# Wave 0 Proof Set

The 15 assets produced and validated in Wave 0. **{len(manifest)} assets, {good} usable,
{len(manifest) - good} with known defects.**

This folder is a **copy** of these assets from `..\\ready\\`, gathered here so they can be found
without cross-referencing a request file. `..\\ready\\` remains the canonical production tree
that the pack verifier checks and that the game imports.

## What is in each asset folder

```
<asset>.glb                  base mesh, asset-id naming, sockets, textures
<asset>_lod1.glb             8000 faces, no textures
<asset>_lod2.glb             2500 faces
<asset>_lod3.glb             ~900-2400 faces
<asset>_collision_hull.glb   convex hull proxy
<asset>_collision_box.glb    box proxy
<asset>_sockets.json         the authored sockets, LOD chain and collision record
<asset>_meta.json            normalisation provenance
```

Sockets are named nodes in the GLB, e.g. `SOCK_head`, `SOCK_grip_primary`. Their positions and
orientations are verifiable with `tools\\asset_pipeline\\_verify_sockets.py`.

## Read this before testing

An asset can import cleanly, carry correct sockets and still be **the wrong object**. Four do:

| Asset | Problem |
|---|---|
| `weaponcomp_grip_standard_a` | The generator drew a complete dagger. This is not a grip. |
| `magiccomp_focus_crystal_a` | The generator drew a whole staff. This is not a crystal. |
| `weaponcomp_pommel_counterweight_a` | No stub boundary exists in the mesh, so it was built uncut. |
| `weaponcomp_mace_head_flanged_a` | Cut correctly, but the concept is a double-headed hammer. Right geometry, wrong visual. |

The four armour pieces are socketed and import cleanly, but their **fit is generated, not
canonical**. Method C produces correct canonical fit for the chest and gorget; the generated
surface has not yet been transferred onto that fit boundary. Expect them to socket correctly
but not necessarily sit correctly on a declared body.

## Known-legal assemblies

Verified by `tools\\asset_pipeline\\_check_proofset_assemblies.py` — 11 of 11 legal:

- mace head onto both haft lengths
- gambeson under chest plate; gorget onto chest
- arming blade onto the **standard** and the **Vaskaal** grip
- pommel onto haft butt
- focus crystal into staff
- telescope inserted into the hybrid
- shield and gorget onto the body mount
- Kal back channel wing sockets

## Regenerating

```
cd tools\\asset_pipeline
python _build_proofset.py          # rebuild all 15 through the unified pipeline
python _promote_proofset.py        # move them into ready/
python _make_proofset_folder.py    # refresh this folder
```

See `docs\\WAVE_0_PROOFSET_READY.md` for the full account.
"""
    with open(os.path.join(DEST, "README.md"), "w", encoding="utf-8") as h:
        h.write(readme)

    print(f"\n  wrote {DEST}")
    print(f"  {len(manifest)} assets, {good} usable, {len(manifest) - good} with defects")
    return 0


if __name__ == "__main__":
    sys.exit(main())
