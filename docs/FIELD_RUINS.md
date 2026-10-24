# The Spill and the Stopped Gate

The ruin on the plateau between Old Late and Cairnwold. Owner request 2026-10-23. Proposal: `C:\Otherreach_External_Assets\FIELD_RUINS_PROPOSAL.md`. The site, the level band and the direction of the lore answers (FR-1 to FR-5) were approved by the owner on 2026-10-24. The canon below is that direction written out, and the owner reviews it in `FIELD_RUINS_CANON_FOR_OWNER.md`.

## Canon

**What it is.** A shut Othergate whose makers are unknown (`OTHERREACH_COSMOLOGY.md` §8: ancient infrastructure, builders unknown). More exactly, it is a **stopped gate**: it was halted partway, neither open nor shut, and it has stood under strain longer than anyone has kept records (§8, *Stopped gates*). It is made of Firstfold iron and Otherstone (`WORLD_MATERIALS.md` §6). Nothing in canon names a maker or a date, and this page does not either.

**Names** (FR-1):

| Name | What it names | Whose word |
|---|---|---|
| **the Spill** | The whole ruin field on the plateau: plates, ribs and pylons of Firstfold iron, some fifty to eighty metres long, lying across the top as if something the size of a hill had burst there | Cairnwold's |
| **the Stopped Gate** | The sunken ring court at the Spill's heart, and the gate itself | The Survey's. Sel Arien's lexicon keeps a space for gates that open "only under a condition" (`content/lore/othergate.yaml` p2). She files this one as *stopped* |
| **the Needle** | The broken gate-spire at the court's east rim, nearly 100 m of dark iron, cracked through. It is half again the height of Old Late and is seen from Ashen Hollow, from the road and over Cairnwold's trees | Cairnwold's |
| **the anchors** | The three fixtures round the court that hold the gate in its halt. They hum louder than the gate | The Survey's; Cairnwold says "the teeth", for where the hum is felt |

**What people know.**
- Cairnwold has always called the top "the Spill" and kept off it after dark. The lodge book has one line in Old Corra's hand: *the top hums; leave it to hum.* Berrin reads that as permission to ignore it, and has.
- The road goes round the top by the Rim. Travellers who sleep at the Rim camp hear the hum all night.
- The Survey has the Stopped Gate on record and no reading of it. Sel says the stone at Old Late and the Quiet Stones are one stone cut by the same tools (`content/lore/quiet_folk.yaml`). The Spill is iron, not that stone, and she will not say whether the same hands made both. That question stays open (`LORE_OWNER_QUESTIONS.md`, "Old Late's makers").

**The Rim Stone and the Spill** (FR-2). The Rim Stone is the one thing on the top that was set there **for travellers**. The iron spilled across the rest of the top was not. The Rim Stone's page now says this, in place of "the only fixed thing on the top".

**Firstfold iron in hand** (FR-3). The Spill is where the slice's Firstfold iron lies, and almost none of it can be moved. Where a plate broke when the gate fell, a shard can come free. One shard is the reward for stilling the gate, a statless keepsake. It never rusts and is faintly warm, and held near the Stopped Gate it answers in the gate's note (`WORLD_MATERIALS.md`, Firstfold iron). No forge takes it.

**No grazers** (FR-4). The fold-bleed grazers (barrowback, fellhorn) are not at the Spill. A gate is not a fold, and a stopped gate bleeds nothing (`OTHERREACH_COSMOLOGY.md` §8). They stay the Foldscar's.

**The keepers** (FR-5).
- **Gate tenders** (level 4) are the gate's own mechanisms, built into the Spill's fabric. They pick up fallen plates and set them back in places they no longer fit, never finishing. They walk the Spill and drive off anything that comes onto it. Cairnwold calls them tenders. They are bloodless, and they are not the Constructed.
- **The Gate Warden** (level 5, the court's keeper) is a giant, near four times a man's height, that stands in the court between the anchors. It is the gate's lock, as a lock is part of a door. It is slow to turn and open at the back where its mechanism runs. Once it is down, it does not return.
- **The animated armour at the two breaches** is not the gate's. It is the same empty armour met elsewhere in the region (`content/lore/animated_armour.yaml`). The hum draws it, and it stands it up at the breaches the way a lodestone stands filings. Whether anything sent it there, Cairnwold argues and nobody knows.

**Stilling the gate.** Stilling all three anchors lets the gate settle shut. It does not open it, and nobody on record knows a rite that would. Once it is still:
- The hum stops, and the Otherfire in the anchors and along the Needle goes dark. At the Spill it burns violet, a colour no ordinary fire makes (`WORLD_MATERIALS.md` §6, Otherfire); it is not the Waystones' cold blue.
- The tenders go on tending, because the world goes on.
- The court stays quiet.

## Build

**Band.** The Spill is level 4 (six gate tenders in three spawns), and the Stopped Gate is level 5 (two animated armour at the breaches and the Gate Warden, tier B, no respawn). `FieldRuinsBandTests` holds this in the real simulation:

| Check | Band |
|---|---|
| A tender's kill time against a level-4 sword | 4–8 s |
| A pair of tenders killing a level-3 character who stands still | 8–15 s |
| The warden fight for a level-5 character fighting from behind | 30–60 s, and the character lives |
| The nameplate at level 3 | tender *Even*, warden *Hard* |
| The nameplate at level 4 | both *Even* |
| The nameplate at level 5 | both *Even* |

The XP budget is in `FIELD_RUINS_PROPOSAL.md` §3.3.

**Content.**
- `content/locations/field/`, `content/spawns/field/`, `content/quests/field/what_stands_on_the_top.yaml`, `content/quests/bounty/rim_road.yaml`.
- `content/creatures/construct/gate_tender.yaml` and `gate_warden.yaml`, plus their abilities and loot.
- `content/world_flags/field/`, `content/items/quest/firstfold_shard.yaml`, and five lore entries.
- New nodes in Sel's and Berrin's dialogue, and a Long Survey reaction.

**Region.**
- `tools/field_ruins/sculpt_spill.py` sinks the court (centre 850,758; floor 9 m out to 17 m; rim at 24 m). Run it once; it refuses a second run.
- `tools/field_ruins/place_spill.py` writes:
  - the Spill's structures, between the `FIELD_RUINS` markers in `starting_frontier.yaml`;
  - the four switches and the court panel;
  - every `fs_*` and `creature.construct.gate_*` binding in `content/_art_bindings.json`.
- Edit the layout there and re-run; never edit the generated lines by hand.
- Each footprint is the converted model's own convex hull, so collision and art are the same shape.
- Both Barrowworks entrances (438,663 and 1050,770) are well clear.

**Art.**
- `tools/field_ruins/convert_spill_kit.ps1` converts 17 Red Echo and Monolith Megastructure pieces (Fab, owned) from their UE exports to `D:\Otherreach_Depot\field_ruins\ready\ruin_fs_*`.
  - They are at megastructure scale: the Needle is 98 m and the slab 77 m.
  - There are **no LODs**, so every piece is drawn at full detail on Ultra.
  - Textures are **4096**, and they **must reach the GPU through the BC7 texture cache** (`--visual textures=cache`, with the cache built by a `--texture-cache` run).
    - The game uploads a runtime GLB's maps uncompressed (RGBA8), and every piece carries its own copy of the kit's maps.
    - Uncompressed, the 4K Spill took 4,365 MB of texture memory (973 MB without it) and crashed the NVIDIA driver on the walk in from Old Late (2026-10-24).
    - Through the cache, the 120 ruin and keeper maps de-duplicate to 18 BC7 images: **96 MB**.
    - `D:\Otherreach_Depot\field_ruins_4k` is the final set. `D:\Otherreach_Depot\field_ruins` (2048, keepers 1024) is an interim set that runs uncompressed until the packaged default reads the cache.
- `tools/field_ruins/build_keepers.py` (Blender 5.2) builds `rigged\creature_gate_tender` (3 m) and `rigged\creature_gate_warden` (8 m) on Stan the Golem's rig (ShyrAssets, CC BY 4.0).
  - Each block is one Spill piece; the base colour is pulled to dark iron; a violet core sits in the open back. Their maps are capped at 2048 in the final set (`build_keepers.py --depot ...field_ruins_4k\ready --max-texture 2048`).
  - They play the golem's own six clips (`creature.creature_golem_stan.*`).
- Otherfire is violet: the anchors' and the Needle's own lights, which go out when their switch is set (`set_goes_dark`). The hum is the fold-hum loop, wide over the Spill and gone once the Needle is set.
- Nothing here went near a generative tool.

**Installing into an RC asset root** (the root the game is launched with as `--asset-root`):
1. Copy `D:\Otherreach_Depot\field_ruins_4k\ready\ruin_fs_*` into `<root>\ready\`.
2. Copy `D:\Otherreach_Depot\field_ruins_4k\rigged\creature_gate_*` into `<root>\rigged\`.
3. Copy the golem's clips from `C:\Otherreach_Recovery\asset_work\creatures\ready_new\creature_golem_stan\animation\`: `ready\creatures\anim.creature.creature_golem_stan.*.glb` into `<root>\animation\ready\creatures\`, and `clips\anim.creature.creature_golem_stan.*.json` into `<root>\animation\clips\`. Clip records are read from the primary root only.
4. The shard's icon: `python tools/ui_icons/make_missing_icons.py --out <dir> --install <root>`.
5. Build the texture cache for the new maps: launch once with the editor binary and `--texture-cache --visual textures=cache` (a headless `--smoke` run does it in about five minutes without the GPU).
6. Ship with `textures=cache` in the launch's `--visual`. Without it the 4K set is uploaded uncompressed. Until then, use the 2048 interim set from `D:\Otherreach_Depot\field_ruins`.

