# M7 research notes - world_lore: who and what is in and around Ashen Hollow

Key: `world_lore`. Canonical source: read-only snapshot of `origin/main` at commit `e10d2c4`. Every `path:line` citation below is repo-relative and valid at that commit unless it is prefixed `G:/UNNAMED/docs/` (UNTRACKED, authority unconfirmed) or `G:/UNNAMED_HISTORY/` (reports, not source).

Conventions: **FACT** = stated in a cited file. **INFERENCE** = my reading, not stated anywhere. Verbatim wording is in quotation marks. Nothing here invents a faction; section 4 lists what exists and what is implied, and labels which is which.

---

## 1. Bottom line for the M7 designer

1. **FACT: no faction, guild, cult, order, government, lord or named organisation exists anywhere in Phase-1 content or runtime code.** `content/` has no `factions/` directory (the directory kind is registered, `src/Content/SchemaResolution.cs:158-163`, but empty). `NpcDefinition` has no faction field (`src/Domain/Social/Social.cs:10`). No file under `src/Persistence`, `src/World`, `src/Application` or `src/EntityRegistry` mentions "faction". A quest `faction_ref` is refused as "not built in Phase 1" (`tests/Content.Tests/QuestContentTests.cs:118-122`). The quest objective types `faction_reputation` and `faction_state` are listed as not built, waiting on "factions (M7)" (`src/Domain/Quests/Quests.cs:154-155`).
2. **FACT: the highest authority in Ashen Hollow is Renn Vale, "waystation steward / practical local authority"** (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:267-269`). Nobody above him (an owner, lord, council, company or crown) is named or implied in any tracked text. He says: "I keep this waystation, such as it is." (`content/dialogue/ashen_hollow/renn_vale.yaml:11`).
3. **FACT: Phase 1 has one settlement, four named people, and no opposed groups.** PROTOTYPE excluded factions because they need "multiple settlements or at least two opposed groups to be meaningful" (`docs/PROTOTYPE.md:33`). **INFERENCE:** M7 cannot draw a faction pair from Ashen Hollow as built. Either implied groups get promoted (section 4.1), or groups come from outside the 200 m square.
4. **FACT: every group in Phase-1 content is implied, not named.** They are: the waystation community; travellers and traders on "the old road" (the damaged merchant cart); Sel's survey, whose sponsor is unknown; **an unknown party who deliberately turned the Quiet Stones out of line**; the unknown builders of the stones; the people who used to work Blackvein Cut; whoever made or raised the Animated Armour and the Bone Walker Husk; and the wildlife. The legacy keeper Halda and her brother survive only as an item (section 3.6).
5. **FACT: the cultural layer is canon and well developed.** The four NPCs are one each of **Veth, Kal, Siann, Orenth** (bible `:267-281`). `MYTHOLOGY.md` gives each people a society type, beliefs and inter-people attitudes (section 4.2). These are peoples, not factions.
6. **FACT: the only named factions in the repository belong to other places.**
   - `docs/VERTICAL_SLICE.md` names "The Vessmere Compact" and "the Ashlings", in a town called **Vessmere** in a region called **Kaldrun Reach** (`:30`, `:44`, `:94`, `:169`).
   - `docs/DATA_MODEL.md` uses illustrative example IDs, among them `faction.settlement.stoneford_covenant` (`:102`, `:564-580`).
   - A test uses `faction.hollow.wardens` as a deliberately nonexistent reference (`tests/Content.Tests/QuestContentTests.cs:120`).
   None of them is tied to Ashen Hollow. The bible calls Ashen Hollow's larger future region **"Otherhome Marches"** (`:6`), not Kaldrun Reach.
7. **FACT: naming is explicitly unsettled.** "Names are working names until cultural naming is finalized." (bible `:265`). The V4 handoff repeats this (`G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:262`, untracked).
8. **Player acts already in content that plausibly matter to more than one group** (section 6):
   - steadying the Foldscar, which undoes someone's deliberate act;
   - emptying the waystation's storage chest (the scripted acceptance run does it at 0:08);
   - looting the damaged merchant cart;
   - mining Blackvein and killing its sentinel armour or its husk;
   - killing or sparing wolves and the spider;
   - trading with Kera;
   - making a fine or poor spear.
   Today every one of these either moves one named NPC's relationship value or nothing.

---

## 2. The Phase-1 map

### 2.1 Frame, cells and keys

- **FACT, coordinates** (bible `:38-44`): "X increases east", "Z increases north", "Y is elevation", south-west corner `(0, 0)`, north-east corner `(200, 200)`. Footprint: "200 m × 200 m, four 100 m × 100 m cells" (`:5`).
- **FACT, content:** `region.ashen_hollow`, `region_key: r_0_0`, `cells: [r_0_0:c_00_00, r_0_0:c_00_01, r_0_0:c_01_00, r_0_0:c_01_01]`, `bounds_m: { min: [0, 0], max: [200, 200] }` (`content/regions/ashen_hollow.yaml:1-10`). Cell index = floor(x/100), floor(z/100). The mapping is confirmed by tests: the waystation cell is `r_0_0:c_00_01` (`tests/Application.Tests/InteractionAndDiscoveryTests.cs:11`), the Foldscar cell is `r_0_0:c_01_00` (`tests/Application.Tests/FoldscarTests.cs:23`), and the four keys are in `tests/Application.Tests/SessionTests.cs:17`.

| Cell | Bible bounds (`:46-51`) | Cell key | Identity | Typical Y |
|---|---|---|---|---|
| A | X 0-100, Z 100-200 | `r_0_0:c_00_01` | Ashen Hollow Waystation | 6-9 m (bible `:132`); built as "a level terrace at 7.2 m" (`docs/M6_STATUS.md:16`) |
| B | X 100-200, Z 100-200 | `r_0_0:c_01_01` | Charwood Verge | 4-7 m (`:170`) |
| C | X 0-100, Z 0-100 | `r_0_0:c_00_00` | Blackvein Cut | rim 7-10 m, floor 1-3 m (`:206-207`); built rim 8-10 m, floor 1.7-2.5 m (`docs/M6_STATUS.md:18`) |
| D | X 100-200, Z 0-100 | `r_0_0:c_01_00` | Foldscar Ruin | 2-7 m (`:228`); basin 2.6 m, stones on 5-7 m raised ground (`docs/M6_STATUS.md:19`) |

- **FACT, spawn:** `spawn: { position_m: [30, 158], facing_deg: 90 }`, "beside the Ashen Waystone, facing east into the waystation" (`content/regions/ashen_hollow.yaml:11-12`).
- **FACT, terrain:** a 41 x 41 heightfield at 5 m spacing (`content/regions/ashen_hollow.yaml:15-62`), "falling from 9.6 m at the north-west road ridge to the quarry floor's 1.7 m" (`docs/M6_STATUS.md:21`).
- **FACT, boundary:** "Movement stops at the edge (PROTOTYPE.md §3); the ravine beyond is scenery." (`content/regions/ashen_hollow.yaml:9`). PROTOTYPE says "Enclosed by authored geometry (a sheer ravine on three sides) plus a movement stop at the edge" (`docs/PROTOTYPE.md:62`). Which three sides is not stated.
- **FACT, bible macro elevation** (`:84-92`): road ridge 8-10 m; waystation 6-8; Charwood 4-7; quarry floor 0-3; Foldscar basin 2-4; standing stones 5-7; quarry rim 8-11.

### 2.2 The primary road and connectivity

- **FACT, road spline** (bible `:102-118`): `(0,180) → (30,165) → (55,145) [Ashen Hollow] → (78,125) → (105,108) → (132,92) → (158,70) → (200,55)`. Widths (`:120-125`): main road 3-4 m; settlement paths 1.5-2.5; forest trails 1-1.5; quarry work paths 2-3.
- **INFERENCE:** the road enters at the west edge `(0,180)` and leaves at the east edge `(200,55)`, so Ashen Hollow is a stop on a through-road between two unnamed off-map places. Renn: "Ashen Hollow: a stop on the old road, between the quarry and the woods." (`renn_vale.yaml:47`).
- **FACT, road as built:** no road geometry. The M6 divergence table lists "Kera's smithy smoke, a road with widths | Greybox boxes and bare terrain" (`docs/M6_STATUS.md:33`).
- **FACT, exits and approaches** (bible `:152-154`, `:178`, `:211`, `:241-242`): Charwood exit `(88,145)`; Blackvein exit `(65,108)`; Foldscar trail branch near `(90,115)`; Charwood trail entry `(102,142)`; quarry entrance `(63,98)`, built as a ramp from the road at `(64,104)` (`docs/M6_STATUS.md:18`); Foldscar primary approach `(120,93)`; secondary Charwood approach `(164,101)`.
- **FACT, seams:** "Avoid critical content directly on seams", soft exclusion `X = 95–105`, `Z = 95–105` (bible `:735-742`). The acceptance movement must exercise A→B, B/A→C, C→A, A→D, D→A (`:746-754`).
- **FACT, stream** (bible `:184-192`): `(190,195) → (170,165) → (150,132) → (132,108) → Foldscar drainage`. As built, "a bed without water in the greybox" (`docs/M6_STATUS.md:21`).
- **FACT, a structure straddles a seam today.** The wolves' den rock `den_rock_west` is `box_m: [93, 180, 109, 198]` (`content/regions/ashen_hollow.yaml:94`), which crosses X = 100, the A/B seam. Relevant to M7's seam-straddling building and navigation work.

### 2.3 Cell A - Ashen Hollow Waystation (the only settlement)

- **FACT, character** (bible `:163`): "The settlement should feel like a frontier stop, not a village or city." Developed footprint X 30-85, Z 115-170 (`:156-161`). Final principle: "Ashen Hollow should feel small because it is a frontier waystation, not because it is a test map." (`:1161`).
- **FACT, opening view** (`:136-142`): Kera's smithy smoke, the communal building, the road, the quarry ridge, and "only a faint suggestion of Foldscar stone through distant trees".

| Place | Bible position (`:146-154`) | Built (`content/regions/ashen_hollow.yaml`) |
|---|---|---|
| Ashen Waystone | (27,158) | `rock_waystone` circle r 0.8 m, h 2.4 m (`:66`) |
| Communal lodge / steward building | (44,128) | "The communal lodge, Renn's": `longhouse_*` walls, box 36-52 x 124-132, h 3.2 m, door on the east wall (`:67-72`); door `door.longhouse`, flag `world.hollow.longhouse_door_open` (`:143`) |
| Kera's smithy | (58,142) | "Kera's smithy ... the M3 forge shed": `forge_*` walls, box 53-63 x 138-146, h 3.0 m, door on the west wall (`:73-78`); `door.forge_shed`, flag `world.hollow.forge_shed_door_open` (`:144`); stations `station.forge_hearth` (forge) at (60.5,143.5) and `station.forge_anvil` (anvil) at (57.5,140.5) (`:161-163`) |
| Sel's survey area | (72,122) | `survey_table` box 73.4-75.2 x 120.4-122.2, h 0.9 m (`:82-83`) |
| Well | (49,151) | `rock_well` (`:81`) |
| Storage chest | (38,137) | `container.waystation_chest`, `loot.waystation_chest`, 12 slots (`:148`) |
| Fence (camera test) | not placed in bible | `fence_smithy` box 44-51.4 x 146-146.4, h 1.2 m (`:79-80`) |

- **FACT, location record:** `location.outpost`, name "Ashen Hollow Waystation", `anchor (55, 145)`, `kind_of_place: settlement`, discovery radius 30 m, XP 0, "The ID predates the bible's name." (`content/locations/outpost.yaml:1-13`).
- **FACT, ownership:** no text anywhere says who owns the waystation, the lodge, the chest or the well.
  - The lodge is "Renn's" (`content/regions/ashen_hollow.yaml:67`).
  - The smithy is "Kera's" (bible `:147`). Renn: "Kera has the smithy up by the waystone" (`renn_vale.yaml:47`).
  - The chest is "The waystation's storage chest" (`content/loot/waystation_chest.yaml:6`).
  - The bible names the lodge a "Communal lodge/steward building" (`:148`).
  - **INFERENCE:** "steward" and "communal" both imply stewardship on behalf of others (a community, or an absent owner or authority), but nothing says whom.

### 2.4 Cell B - Charwood Verge

- **FACT** (bible `:172-194`): "Open woodland with alternating tree clusters, brush, fallen timber, clearings, and shallow drainage", sight 20-40 m.
  - Placements: trail entry (102,142); Ash Ember Hound (128,150); "Damaged merchant cart / hunting bow" (157,162); Ash Haft resource (180,138); optional Woundmoss (151,128).
  - First strong Foldscar view at about (175,120).
- **FACT, built:**
  - `cart_wreck` (`content/regions/ashen_hollow.yaml:85`) and `container.merchant_cart` (8 slots, `loot.merchant_cart`: hunting bow and 12 rough arrows) (`:149`; `content/loot/merchant_cart.yaml:6-10`).
  - `location.ruined_cart`, "The Ruined Cart", (157,162), discovery XP 15 (`content/locations/ruined_cart.yaml`).
  - `location.herb_patch`, "The Woundmoss Patch", (151,128) (`content/locations/herb_patch.yaml`).
  - A crouch-under beam at the Woundmoss (`:87-91`), `node.wood.ash_stand` at (180,138) (`:159`), and 32 trees (`:109-140`).
- **FACT, wolves (not in the bible):** "The wolves' den: a notch in the rock in Charwood's north-west corner (the prototype's den, kept; not in the bible), clear of the bible's acceptance path." (`content/regions/ashen_hollow.yaml:92-96`).
  - `location.den_mouth`, "The Wolves' Den", anchor (112,176) (`content/locations/den_mouth.yaml`).
  - `container.den_cache` at (112,189) (`content/regions/ashen_hollow.yaml:147`).
  - The M6 divergence table: "No wolves | The prototype's den pack, strays and respawning pack kept, in Charwood | ... the owner can drop them" (`docs/M6_STATUS.md:32`).

### 2.5 Cell C - Blackvein Cut

- **FACT** (bible `:204-221`): "A shallow abandoned quarry / iron working."
  - Placements: quarry entrance (63,98); upper overlook (54,74); Bone Walker patrol (48,58); iron vein (28,45); Animated Armour (65,34); Bristleback Boar territory (18,72); blocked shaft (76,22).
  - "The player should be able to get ore without being forced to defeat every enemy."
  - "The blocked shaft communicates that the world extends beyond the prototype; it is not a Phase-1 dungeon."
- **FACT, built:**
  - `rock_iron_seam` (28,46.2); `node.ore.iron_seam` at (28,45.1).
  - `rock_blocked_shaft` box 73-79 x 16-21, h 4 m. The bible says (76,22); M6_STATUS says (76,18).
  - Four rim rocks (`content/regions/ashen_hollow.yaml:97-103`, `:158`).
  - `location.blackvein_cut` anchor (54,74), radius 20 m, XP 25. "Was location.iron_shelf" (`content/locations/blackvein_cut.yaml`), aliased by `content/_aliases.yaml:4`.
  - Bible interaction set for Cell C: "iron node; loot; quarry equipment" (`:721-724`). **INFERENCE:** "quarry equipment" implies former workers.
- **FACT, the fiction offered:**
  - Kera: "There's a seam in Blackvein Cut, the old quarry south of the road" (`kera_voss.yaml:67`), and "Mind what walks down there - or don't, if you're quick. Iron doesn't care how you got to it." (`:75`).
  - Quest summary: "bring iron up out of Blackvein Cut" (`content/quests/ashen_hollow/iron_under_ash.yaml:8`).
  - UNTRACKED lock premise: "Ashen Hollow needs usable iron; Blackvein has become unsafe." (`G:/UNNAMED/docs/PHASE1_QUEST_NPC_AND_CONTENT_LOCK.md:213-214`).

### 2.6 Cell D - Foldscar Ruin

- **FACT** (bible `:230-259`): "The Foldscar is the first place where the prototype visibly stops being ordinary frontier fantasy."
  - Its weirdness is kept restrained: "slight image doubling; delayed shadow; displaced audio; inconsistent reflections".
  - Placements: primary approach (120,93); Charwood approach (164,101); Central Foldscar (153,48); North Quiet Stone (150,78); Southwest (122,38); Southeast (181,31); spider (172,48); Tavar recovery (145,42).
  - Safer route round the spider: (160,65) → (187,65) → (194,45) → (181,31).
  - "The non-kill quest must actually be completable without killing the spider."
- **FACT, built:**
  - `rock_stone_north|southwest|southeast` (r 0.9, h 2.8) and `rock_foldscar_heart` (r 1.5, h 3.5) (`content/regions/ashen_hollow.yaml:104-108`).
  - Four switches (`:166-192`) and barrier `barrier.foldscar_fold`, a 3 m circle at (145,42) with flag `world.foldscar.steadied` and prompt "Tavar Orr, half a breath out of true - your hand stops short of him" (`:194-199`).
  - `location.foldscar`, "The Foldscar", (153,48), radius 30 m: "three Quiet Stones around something that is not quite there" (`content/locations/foldscar.yaml:6`).
- **FACT, switch texts** (region yaml):
  - north: "The stone grinds a hand's width round and stops, square to the ring. The air beside it goes still." (`:172`)
  - south-west: "The stone turns with a sound that arrives a moment late, and settles square to the ring." (`:178`)
  - south-east: "The stone comes round into line. For a moment your shadow points two ways, then one." (`:184`)
  - heart, while any stone is still out of line: "It slides out from under your hand. Somewhere in the ring a Quiet Stone is still out of line." (`:191`)
  - heart, done: "The heart holds. The ring goes quiet all at once, and across it Tavar's shadow catches up with him." (`:192`)
- **FACT, world flags** (all `type: bool`, `default: 0`, `owning_system: S-22`, persisted in the Foldscar cell's delta): `world.foldscar.stone_north_aligned`, `world.foldscar.stone_southwest_aligned`, `world.foldscar.stone_southeast_aligned`, `world.foldscar.steadied` (`content/world_flags/foldscar/*.yaml`). The two Cell-A door flags are `world.hollow.longhouse_door_open` and `world.hollow.forge_shed_door_open` (`content/world_flags/hollow/*.yaml`).
- **FACT, tone:** first half "wood; iron; mud; smoke; cloth; forest; quarry stone"; second half "Foldscar misregistration; Orenth displacement; Resonance; impossible alignment". "If everything is strange, the Other stops feeling Other." (bible `:790-807`).

### 2.7 Creatures and spawns (the non-human population)

| Spawn ID (`content/spawns/hollow/`) | Creature | Count and role | At | Level | Respawn |
|---|---|---|---|---|---|
| `spawn.hollow.charwood_hound` | `creature.beast.ash_ember_hound` | 1 hunter | (128,150) | 3 | none |
| `spawn.hollow.den_pack` | `creature.beast.wolf_grey` | 1 den_guardian, 2 pack_hunter, 1 sleeper | (112,184) r 2.5 | 2 | none ("They do not return.") |
| `spawn.hollow.east_pack` | wolf_grey | 2 roamer, route (192,165)-(194,182)-(180,194) | (188,180) | 2 | timer 24000 ticks (20 min) |
| `spawn.hollow.valley_strays` | wolf_grey | 2 stray | (118,128) r 6 | 2 | none |
| `spawn.hollow.iron_shelf_husk` | `creature.undead.bone_walker_husk` | 1 roamer, route round (40-54, 52-62) | (48,58) | 3 | none |
| `spawn.hollow.iron_shelf_armour` | `creature.construct.animated_armour` | 1 sentinel ("stands guard over the iron seam") | (65,34) | 5 | none |
| `spawn.hollow.boar_wallow` | `creature.beast.bristleback_boar` | 1 territorial | (18,72) | 4 | none |
| `spawn.hollow.spider_lair` | `creature.beast.cave_hunting_spider` | 1 ambusher | (172,48) | 3 | none |

- **FACT, creature notes:**
  - boar: "defends its wallow and ignores those who leave it be" (`content/creatures/beast/bristleback_boar.yaml:7`);
  - spider: "Near-blind but feels every footfall; a walker passes, a runner is taken. It never leaves its lair" (`cave_hunting_spider.yaml:7`);
  - armour: "Slow, heavy, bloodless" (`animated_armour.yaml:7`);
  - husk: "arrows pass through bone, and it has no blood to lose" (`bone_walker_husk.yaml:7`).
  - Families are `beast`, `construct` and `undead`. There is no creature `faction_ref`.
- **FACT, the only group communication in the game:** wolves howl. Roles `den_guardian` (`calls_for_help: true`) and `pack_hunter` / `sleeper` / `roamer` (`answers_calls: true`); `heard_call: 80` awareness; the howl carries `call: 45` m (`content/config/creature_behaviour.yaml`). "a packmate's howl brings it at a run". Perception is sight/sound only, and "a sound gives a place to look, never the target itself".
- **FACT, loot:** wolf (meat 70%, hide 45%, fang 15%); boar (2 meat guaranteed); armour (iron ingot 50%); den cache (3 iron ingots, 12 arrows, 2 salves, **Halda's Token**) (`content/loot/*.yaml`).

### 2.8 Divergences and stale notes in map data (FACT unless marked)

- The bible has no wolves; content keeps three wolf spawners (`docs/M6_STATUS.md:32`, `:144`).
- `content/spawns/hollow/iron_shelf_armour.yaml:6` says the bible's (65,34) is "inside the M3 forge shed; this keeps the working layout (a recorded divergence)". The spawn is at (65,34) now, so the note is stale.
- `content/spawns/hollow/boar_wallow.yaml:6` says "west of the palisade". No palisade exists in the current region structures. It is an M3-outpost remnant (`docs/M3C_STATUS.md:53` mentions "inside the outpost palisade").
- `docs/PROTOTYPE.md:72` says the den was kept "at its north edge". The region, M6_STATUS `:17` and `:90` say the north-west corner (112,184) after the part-4 move.
- `docs/PROTOTYPE.md:64` puts the settlement "entirely inside one 100 m cell, `r_0_0:c_00_00`". This is superseded by its own M6 note (`:72`: `r_0_0:c_00_01`).
- `docs/M4_STATUS.md:26` puts "Sel in the longhouse's old-books corner". Since M6 she stands at her outdoor survey table (`content/regions/ashen_hollow.yaml:153`).
- Bible §37 ID placeholders (`:1103-1138`: `creature.ashen_hollow.*`, `item.weapon.ashen_hollow.arming_sword`, `resource.ashen_hollow.woundmoss`, `formula.*`) differ from the built IDs (`creature.beast.*`, `item.weapon.rusted_sword`, `spell.force.impulse_bolt`...). The bible calls them "design placeholders" (`:1138`). The bible, the V4 handoff and the untracked lock say "arming sword"; the content is "Rusted Sword" (`content/items/weapon/rusted_sword.yaml:6`).

---

## 3. The NPC roster

### 3.1 Table

| ID | Name | People (bible `:267-281`) | `role` (content) | Services | Placed at (facing) | Bible role / purpose |
|---|---|---|---|---|---|---|
| `npc.ashen_hollow.renn_vale` | Renn Vale | Veth | `steward` | - | (46.5,126.2), 90°, "the lodge, facing its door" | "waystation steward / practical local authority"; "orientation, structured dialogue, world-state acknowledgment" |
| `npc.ashen_hollow.kera_voss` | Kera Voss | Kal | `craftsperson` | `[trade]`, `merchant.ashen_hollow.kera_voss` | (61.6,139.6), 300°, "the smithy, by the anvil" | "smith"; "first crafting quest, equipment interaction, resource-to-object loop" |
| `npc.ashen_hollow.sel_arien` | Sel Arien | Siann | `scholar` | - | (72,122), 135°, "her survey area, looking towards the Foldscar" | "archivist / survey scholar"; "Foldscar framing, basic magic access, uncertain knowledge rather than omniscient exposition" |
| `npc.ashen_hollow.tavar_orr` | Tavar Orr | Orenth | `villager` | - (has a `companion` block) | (145,42), 53°, "caught in the Foldscar ... facing its heart" | "guide / first companion"; "non-kill quest outcome, first recruitable companion, Other-related perspective" |

Sources: `content/npcs/ashen_hollow/*.yaml` and placements at `content/regions/ashen_hollow.yaml:151-155`.

- **FACT:** the `role` enum is "villager, merchant, guard, craftsperson, quest_giver, trainer, innkeeper, noble, bandit, scholar, steward" (`src/Content/SocialContent.cs:23-24`). **INFERENCE:** `guard`, `noble` and `bandit` already exist as role words, but no NPC uses them.
- **FACT:** refused NPC fields in Phase 1 are `schedule_ref`, `anchors`, `combat_profile`, `relationships_init` and `name_pool` (`src/Content/SocialContent.cs:85-89`). `species_ref` is also not built (`docs/DATA_MODEL.md:310`). The NPCs' people (Veth, Kal...) is therefore **not data**. It lives in the bible and in asset choices only. UNTRACKED DeepSeek prompt: "Keep Kal at 1.30 m for now" (`G:/UNNAMED/docs/FOR DEEPSEEK/DEEPSEEK_POST_M6_ASSET_MAINTENANCE_PROMPT.md:189`).
- **FACT, Tavar's companion block:** `health: 100`, `weapon_item_ref: item.weapon.march_spear`, `armor: { head: 0, torso: 3, limbs: 1 }` (`content/npcs/ashen_hollow/tavar_orr.yaml:12-15`). His NPC note: "Sel's survey guide, caught in the Foldscar until its heart is steadied. The perspective of someone the Other has touched, kept plain." (`:7`).
- **UNTRACKED personalities** (`G:/UNNAMED/docs/PHASE1_QUEST_NPC_AND_CONTENT_LOCK.md:55-110`, authority unconfirmed; V4 `:1349-1354` says the lock "Needs revision before becoming authoritative"):
  - Renn "practical; skeptical; fair; mildly exhausted". He represents "mundane administration; local knowledge; structured dialogue; persistent acknowledgment of major prototype events".
  - Kera "dry; precise; impatient with waste; respects competent work". She represents "crafting as an existing occupation; resource/material logic; Kal morphology in daily life".
  - Sel: "knowledge with uncertainty; science/magic overlap; Foldscar investigation. Sel has theories, not omniscience."
  - Tavar: "Orenth perspective; exploration expertise; first companion continuity."
- **UNTRACKED owner tone ruling:** the owner "strongly liked" the M6 dialogue ("concise; useful; dry humor; not overly wordy"). "Protect this tone. Do not rewrite dialogue merely because it is prototype-era text." (V4 `:269-276`).

### 3.2 What each NPC says about the world (the complete lore surface of Phase 1)

**Renn Vale** (`content/dialogue/ashen_hollow/renn_vale.yaml`):
- greet: "Off the road in one piece. I'm Renn Vale; I keep this waystation, such as it is." (`:11`)
- place: "Ashen Hollow: a stop on the old road, between the quarry and the woods. Kera has the smithy up by the waystone. Sel keeps her books at the survey table east of here, where she can watch the Foldscar." (`:47`)
- work: "Kera needs iron and a steady pair of hands. Sel needs someone who reads more than they swing. Either would do you more good than wandering." (`:53`)
- shelf_news, offered only while carrying iron ore: "And came back carrying ore. Then you've met what walks down there, and you're still breathing. Kera will want that iron." (`:59`)
- spear: "One of Kera's? No - yours. It has the look of a first try. It'll keep a wolf honest." (`:67`)
- **FACT:** Renn has **no line about the Foldscar being steadied, Tavar being freed, the wolves, the cart or the chest.** He acknowledges exactly two world facts: carried iron ore and a carried spear.

**Kera Voss** (`content/dialogue/ashen_hollow/kera_voss.yaml`):
- greet: "Mind the anvil. I'm Kera. Hands like those have never held a hammer." (`:11`)
- lesson: "Then bring me iron. There's a seam in Blackvein Cut, the old quarry south of the road ... A billet and a straight ash haft - Charwood's full of ash - make a spear at the anvil. Make one and show me. And don't blame the iron." (`:67`)
- where: "South, down the road to the quarry mouth ... The seam's on the west side. Mind what walks down there - or don't, if you're quick. Iron doesn't care how you got to it." (`:75`)
- praised (quality ≥ 1): "Hm. Straight, and the point holds. Better than I'd have guessed from those hands." (`:81`)
- judged: "It'll hold, mostly. The next one will be better. They always are." (`:89`)

**Sel Arien** (`content/dialogue/ashen_hollow/sel_arien.yaml`):
- greet: "Careful with the table - half of what's on it is older than the waystation. I'm Sel Arien. I survey the ruin south-east of here, when the spider lets me." (`:11`)
- primer: "The Resonance Primer - three small workings, one to strike, one to brace, one to mend. It's no use to me in a drawer. Read it, and don't push past what your body can hold." (`:69`)
- foldscar: "Foldscar. Three standing stones around something that isn't there any more - or isn't here. I have guesses about what they were for. I've learned not to trust them." (`:80`)
- tavar: "Tavar Orr. He guides my surveys. Three days ago he walked in among the Quiet Stones for a chain I'd dropped, and he's still standing there. ... The stones were turned out of line when we found them. I think they matter. I don't know how." (`:93`)
- stones: "Turn them square to the ring - the north stone, the south-west, the south-east - and the heart itself last. That's my guess. And the spider on the east side hunts by the ground, not by sight: walk past it, don't run." (`:102`)
- tavar_back: "I know. I saw the ring go quiet from the rise, and then I saw him walk. I'd no idea how to get him out - only that the stones were wrong. Thank you." (`:108`)
- strain: "Past your tolerance a working takes it out of your body instead. It won't refuse you. That's the danger." (`:114`)

**Tavar Orr** (`content/dialogue/ashen_hollow/tavar_orr.yaml`). Every line is spoken after he is freed:
- greet: "\"You're not Sel. Did she send you, or do you just go about turning stones?\"" (`:11`)
- caught: "\"Three days, Sel will say. For me it was one long afternoon where everything arrived a breath late. I could see the waystation smoke. I heard you coming from the wrong side.\"" (`:29`)
- stones: "\"Holding something shut, or holding it still - I couldn't tell you which. Somebody turned them out of line before we ever came out here. Not me.\"" (`:43`). The player's reply: "Then somebody else has been out here." (`:46`)
- joined: "\"Back to the waystation? I'll walk behind you. I've had enough of being in front of things.\"" (`:77`)

**Quest framing:**
- Iron Under Ash summary: "Kera Voss will make a smith of you if you bring iron up out of Blackvein Cut and make something worth carrying." (`content/quests/ashen_hollow/iron_under_ash.yaml:8`)
- Three Quiet Stones summary: "Sel Arien's guide, Tavar Orr, is caught in the Foldscar - there to be seen, and half a breath out of step with everything. The Quiet Stones around it were turned out of line." (`content/quests/ashen_hollow/three_quiet_stones.yaml:8`)
- UNTRACKED lock premise for Quest 2: "Three markers appear to constrain a Foldscar distortion. Tavar became misregistered while investigating." (`G:/UNNAMED/docs/PHASE1_QUEST_NPC_AND_CONTENT_LOCK.md:249-250`)

### 3.3 Existing relationship mechanics (the only "standing" system that exists)

- **FACT, model:** "What one NPC thinks of the player, on named dimensions (SYSTEMS.md S-26): there is no single good-or-evil meter." Values are in [-100, 100], and "a value of 0 is not stored" (`src/Domain/Social/Social.cs:27-38`). The dimensions are exactly `affection, fear, grudge, respect, trust` (`:35`).
- **FACT, runtime:** "The runtime `RelationshipSystem` owns what each NPC thinks of the player (`StateSlice.Relationships`), saved with the player (schema 10)". A change is an internal command naming its reason, published as `RelationshipChanged(npc, dimension, from, to, event)`. "Phase 1 keeps no memory log and no NPC-to-NPC values; the event names are the attribution until the log arrives." (`docs/SYSTEMS.md:300`). The persisted shape is `(npc_id, dimension, value)` plus conversation memory `(dialogue_id, heard)` (`docs/M4_STATUS.md:21`).
- **FACT, S-26 target model** (not built): per-pair values, "the memory log of attributed events (event key, actor, timestamp, weight), and derived disposition tiers". It reads S-12 "who the player attacked" and gift/trade events (`docs/SYSTEMS.md:292-299`).
- **FACT, every relationship change in content** (all positive; `affection`, `fear` and `grudge` are never used):

| NPC | Dimension | Delta | Event key | Trigger | Source |
|---|---|---|---|---|---|
| Kera | trust | +5 | `asked_to_learn` | "Then teach me the forge." (also starts Quest 1, gives both recipes) | `kera_voss.yaml:20`, `:37` |
| Kera | respect | +10 | `showed_fine_work` | showing a March Spear of quality ≥ 1, once | `kera_voss.yaml:52` |
| Kera | respect | +5 | (quest reward) | Iron Under Ash complete (also 120 XP, 25 currency) | `iron_under_ash.yaml:47-50` |
| Renn | respect | +5 | `went_to_the_cut` | "I've been down in Blackvein Cut." while carrying iron ore, once | `renn_vale.yaml:38` |
| Sel | trust | +5 | `given_the_primer` | accepting the Resonance Primer | `sel_arien.yaml:77` |
| Sel | trust | +5 | `brought_tavar_back` | "Tavar's free." after Tavar has been spoken to (also starts Quest 2) | `sel_arien.yaml:26`, `:64` |
| Sel | trust | +10 | (quest reward) | Three Quiet Stones complete (also 150 XP) | `three_quiet_stones.yaml:47-50` |
| Tavar | trust | +10 | `freed_from_the_fold` | first reply to his greeting, either branch | `tavar_orr.yaml:20`, `:26` |

- **FACT:** a `relationship` dialogue condition exists (`src/Domain/Social/Social.cs`, `RelationshipCondition`), but **no content uses it**. No line anywhere is gated on a relationship value.
- **FACT, what moves no relationship:** killing any creature, looting any container (including the waystation chest and the merchant cart), trading (`TradeSystem` "owns nothing", `docs/M4_STATUS.md:19`), casting, the companion's kills ("His kills earn the character nothing and count for no quest", `docs/M6_STATUS.md:69`), and doors.
- **FACT, trade:** Kera's stock is fixed: 60 rough arrows, 1 hide vest, 1 hide cap. She buys `[material, consumable, weapon, armor, misc]` (`content/merchants/ashen_hollow/kera_voss.yaml:7-12`). `sell_ratio: 0.4`: "a merchant buys at 40% of value, so a craft-sell loop loses money" (`content/config/economy.yaml:6-7`).
- **FACT, companion:** Tavar follows/waits. Downed, he waits `revive_window_s: 60` for a hand, then "falls and comes back at the Ashen Waystone" (`content/config/companion.yaml:6`, `:18-19`). Bible: no "affinity ladder; romance; full tactical menu; personal quest; offscreen life simulation" (`:489-495`); the companion HUD has "No affinity meter" (`:599`).

### 3.4 NPC-to-NPC relationships (implied only; nothing is data)

- Sel employs or directs Tavar ("He guides my surveys", `sel_arien.yaml:93`). Tavar defers to her ("You're not Sel. Did she send you", `tavar_orr.yaml:11`). The player can tell Tavar "Sel's been watching for you." (`:40`).
- Renn knows both Kera's and Sel's needs (`renn_vale.yaml:47`, `:53`). **INFERENCE:** as steward he brokers the waystation's small economy of favours.
- Renn recognises Kera's work ("One of Kera's? No - yours.", `:67`).
- **FACT:** NPC-to-NPC values are not built (`docs/SYSTEMS.md:300`). `relationships_init` is refused (`src/Content/SocialContent.cs:85`).

### 3.5 What NPCs can know (information locality - relevant to "information is not magically global")

- **FACT:** "Dialogue reads and writes flags in the speaker's cell." (`src/Domain/Social/Social.cs:70`, also `:106`). **INFERENCE:** Renn, Kera and Sel stand in cell A (`c_00_01`). The Foldscar flags live in cell D (`c_01_00`). A `world_state` condition in their dialogue therefore cannot read `world.foldscar.steadied`. That is an accidental but real form of locality.
- **FACT:** Sel's knowledge that Tavar is free is expressed as `{ kind: visited, dialogue_ref: dialogue.ashen_hollow.tavar_orr, node: greet }` (`sel_arien.yaml:23`). That means "the player has heard Tavar's greeting". Her line claims she saw it: "I saw the ring go quiet from the rise" (`:108`), and her survey table faces the Foldscar (135°).
- **FACT:** a quest `world_state` objective reads "the flag in the cell holding that place" (`docs/DATA_MODEL.md:517`). Quest state is global to the player.
- **FACT, only generic statements exist for information spread:**
  - "Hostility/reputation should spread through information." (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:637`)
  - "no psychic faction omniscience" (V4 `:459`, untracked)
  - "The map represents what the character knows, not what the game database knows." (`docs/MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md:8`)
  - Knowledge sources listed include "faction intelligence", "rumors" and "NPC directions" (`:30-44`).
  - Map rule: "Default should avoid unexplained omniscience." (`:150`)

### 3.6 Legacy roster (pre-bible PROTOTYPE) - still partly in content

- **FACT:** PROTOTYPE's original three were `npc.keeper_halda` (quest giver), `npc.smith_orren` (smith and merchant) and `npc.warden_kesh` (companion-to-be) (`docs/PROTOTYPE.md:87`).
- **FACT:** the original quest `quest.hollow.lost_token` included "Recover Halda's token from the pack" (with ≥ 3 wolf kills at the den) and "Bring Halda a salve for her brother's leg." (`docs/PROTOTYPE.md:139-144`).
- **FACT:** it was replaced by Iron Under Ash. "The table's token, den and salve objectives are not built; `item.quest.halda_token` stays defined and unused." (`docs/PROTOTYPE.md:150`).
- **FACT:** `item.quest.halda_token`, "Halda's Token" (`no_drop`, `no_sell`, `value_base: 0`), lies in the den cache for reachability (C18) (`content/items/quest/halda_token.yaml`; `content/loot/den_cache.yaml:6`, `:12`).
- **INFERENCE:** in-world, a named "Halda's Token" is an orphaned trace of a person who is not in the world, sitting in a wolf den. It is an unused hook, not a faction.
- **FACT:** save fixtures still rename "the warden" (Kesh) on load (`docs/M4_STATUS.md:51`, `docs/M6_STATUS.md:62`, "the warden following Aelin"). These are test-fixture names only.

### 3.7 Can NPCs be attacked? (attack legality today)

- **FACT:** the player's attack resolution returns creatures only: `private CreatureState? Loose(...)`, `Trace(...)` returning `CreatureState? Target`, and `RangedTarget(...)` (`src/World/Runtime/Combat.cs:511`, `:523`, `:548`).
- **INFERENCE:** named NPCs cannot currently be struck, so assault and murder of townsfolk do not exist as player acts in Phase 1. PROTOTYPE also says "NPCs are non-combatants and take no damage in the prototype" (`docs/PROTOTYPE.md:203`). "NPC bodies block movement" (`docs/M4_STATUS.md:19`).

---

## 4. Every named or implied group, culture, authority or interest near Ashen Hollow

### 4.1 Implied by Phase-1 content itself (none is named)

| # | Implied group or interest | Evidence (FACT) | What is unknown | Notes (INFERENCE) |
|---|---|---|---|---|
| G1 | **The waystation and its keeper(s)** - a small communal stop on a road | "waystation steward / practical local authority" (bible `:267-269`); "Communal lodge/steward building" (`:148`); "I keep this waystation, such as it is." (`renn_vale.yaml:11`); a shared storage chest (`content/loot/waystation_chest.yaml:6`); bible non-goal "full crime/court simulation" (`:1083`) | Who appointed Renn; whether the waystation belongs to a road authority, a trade body or a community; any law | The only candidate for a local "authority" faction. "Steward" implies an absent principal. |
| G2 | **Road travellers, traders and carters on "the old road"** | "a stop on the old road" (`renn_vale.yaml:47`); road enters at (0,180) and leaves at (200,55) (bible `:102-118`); "Damaged merchant cart" with a bow and arrows still in it (bible `:180`; `content/loot/merchant_cart.yaml`); "The Ruined Cart" (`content/locations/ruined_cart.yaml:7`); the player is a "Wayfarer" arriving "Off the road" (bible `:401`; `renn_vale.yaml:11`) | Whose cart it was; what happened to its owner; where the road goes | A trade or travel interest exists by implication. The cart's goods are ownerless in the data. |
| G3 | **Sel's survey** - a scholarly or archival interest in the Foldscar | Sel is an "archivist / survey scholar" (bible `:275-277`); "I survey the ruin south-east of here" (`sel_arien.yaml:11`); "half of what's on it is older than the waystation" (`:11`); Tavar "guides my surveys" (`:93`) | Whom she works for or reports to; whether she is local | MYTHOLOGY says Siann "accumulate in archives, courts, universities and religious orders" (`docs/MYTHOLOGY.md:182-184`). She may represent an institution. That is unstated. |
| G4 | **Whoever turned the Quiet Stones out of line** | "The stones were turned out of line when we found them." (`sel_arien.yaml:93`); "Somebody turned them out of line before we ever came out here. Not me." (`tavar_orr.yaml:43`); player: "Then somebody else has been out here." (`:46`); quest summary (`three_quiet_stones.yaml:8`) | Who, why, whether they will return, and whether they want the fold open | **The strongest grounded seed of an opposed interest in Phase 1.** Steadying the heart undoes a deliberate act. |
| G5 | **Builders or keepers of the Quiet Stones** (ancient) | "Three standing stones around something that isn't there any more - or isn't here. I have guesses about what they were for." (`sel_arien.yaml:80`); "Holding something shut, or holding it still" (`tavar_orr.yaml:43`); stones sit "square to the ring" (region `:172`) | Everything | COSMOLOGY lists "standing stones" among possible Othergate forms (`docs/OTHERREACH_COSMOLOGY.md:162-164`) and "stabilize a dangerous fold" as an epic-quest shape (`:666`). Historical, not a present faction. |
| G6 | **Former workers of Blackvein Cut** | "A shallow abandoned quarry / iron working" (bible `:204`); "quarry equipment" (`:724`); "blocked shaft" (`:217`, `:221`); "the old quarry" (`kera_voss.yaml:67`); UNTRACKED "Blackvein has become unsafe" (lock `:213-214`) | Who worked it, when and why it stopped, who blocked the shaft | A mining interest exists by implication. The Kal/Ondrek quarry dispute (4.4) is the only canon conflict about quarries. Nothing links Blackvein to it. |
| G7 | **Makers or masters of the quarry's guardians** | Animated Armour role `sentinel`, "stands guard over the iron seam" (`content/spawns/hollow/iron_shelf_armour.yaml:6`); Bone Walker Husk, family `undead`, patrols the floor (`iron_shelf_husk.yaml`) | Who animated or raised them | MYTHOLOGY: "Necromancy is a Mor discipline" (`:255`). Constructed "maintenance bodies ... still perform duties whose purpose has been lost" (`:287-288`). Both are interpretive hooks only. |
| G8 | **Wildlife**: grey wolves (a signalling pack at a den), Ash Ember Hound, Bristleback Boar, Cave Hunting Spider | Section 2.7; wolves call and answer (`content/config/creature_behaviour.yaml`) | - | `DATA_MODEL.md`'s example has `faction.wildlife` (`:565`). The wolves are the only in-game group with shared signalling. |
| G9 | **Halda and her brother** (legacy, absent) | `item.quest.halda_token` in the den cache; "Bring Halda a salve for her brother's leg." (`docs/PROTOTYPE.md:142`) | Whether they exist in the current fiction | Unused. Not referenced by any current dialogue. |
| G10 | **The Ashen Waystone** (respawn anchor) | "You return at the Ashen Waystone." "Do not prematurely canonize full Soul/resurrection metaphysics." (bible `:524-528`) | What the waystone is | Not a group. Any religious or metaphysical owner of it is explicitly held back. |

**FACT, explicit Phase-1 non-goals for Ashen Hollow** (bible `:1077-1097`): mounts; "player settlement building"; romance; "full faction warfare"; "full crime/court simulation"; full survival; seasons; custom spells; Great Works; Soul reincarnation; community servers; networking; PvP; Othergate travel; simulation-tier switching; "full NPC schedules"; major dungeon; boss fight; "sprawling dialogue trees".

### 4.2 The peoples embodied by the four NPCs (canon culture; not factions)

Race is a player choice in future, but the bible casts each NPC as one people (`:267-281`). The key canon for each:

- **Veth** (Renn; also the player's fixed "Veth acceptance character", bible `:397`).
  - "Their sacred act is **keeping** — records, lineages, oaths, and the continuity of ordinary life." (`docs/MYTHOLOGY.md:97-98`).
  - Society: "Villages, towns, and kingdoms — the ones who build the roads everyone else uses. Governed variously by oath-councils, hereditary houses, or whoever last won. Their institutions are strong" (`:105-108`).
  - Heresy: "the claim of descent" (`:101-103`).
  - Relations: "Majority population ... They are the ones who decided, somewhere along the way, that the Kal quarries were a complicated question rather than a desecration." (`:110-112`).
  - Roles: "Farmers, soldiers, merchants, magistrates, priests of continuity" (`:114`).
  - **INFERENCE:** Renn's "I keep this waystation" echoes Veth "keeping", and Veth build roads, which fits a waystation on "the old road". Nothing states a link.
- **Kal** (Kera).
  - "Clan-holds built into stone they did not quarry ... Their economy is craft, and their politics is genealogy" (`docs/MYTHOLOGY.md:139-141`).
  - "At war with the Ondrek, in fact though not in name. Tolerated by the Veth, who buy their work and avoid their funerals." (`:143-145`).
  - Roles: "Smiths, masons, engineers, archivists" (`:147`).
  - Petrification: "a Kal is **fixed** by one [Pachakuti]" and "The old mined galleries and carved halls that Kal regard as ancestral are not buildings. They are ancestors." (`docs/RACES.md:199-205`).
  - Visual: "large folding dorsal wings" (`:167-168`); asset height 1.30 m (UNTRACKED DeepSeek prompt `:189`, `:196`).
- **Siann** (Sel).
  - "the Record: an obsession with accurate testimony. A Siann's word is their standing" (`docs/MYTHOLOGY.md:165-167`).
  - "No homeland. Siann accumulate in archives, courts, universities and religious orders" (`:182-184`).
  - "Trusted with history and trusted with nothing else. They are the setting's lawyers and historians" (`:186-187`).
  - Secret: "the Record begins too late" (`:169-172`).
- **Orenth** (Tavar).
  - "Kin-bands rather than settlements, organised around a route ... What law they have is contractual and portable, and their most serious institution is a **ledger of costs**" (`docs/MYTHOLOGY.md:215-219`).
  - "Needed and disliked ... the setting's smugglers, guides and couriers" (`:221-224`); "An Orenth who settles is considered to be dying slowly" (`:211-213`).
  - Visual: "a shadow lags behind by an instant" (`docs/RACES.md:292`). **INFERENCE:** this matches the Foldscar's effect on him ("Tavar's shadow catches up with him", region `:192`).

**FACT, the inter-people attitude matrix** (`docs/MYTHOLOGY.md:421-430`), rows for the four present peoples. Read row → column.

| From \ To | Veth | Kal | Siann | Orenth |
|---|---|---|---|---|
| Veth | - | "buy, avoid funerals" | "employ" | "hire" |
| Kal | "depend" | - | "respect" | "unprovable" |
| Siann | "patronise" | "rival scholars" | - | "useful" |
| Orenth | "trade" | "no contact" | "trade" | - |

**INFERENCE:** the canon matrix already describes the social texture of the four NPCs:
- Veth "employ" Siann and "hire" Orenth;
- Siann find Orenth "useful" (Sel employs Tavar);
- Orenth have "no contact" with Kal (Tavar and Kera have no lines about each other).
These are attitudes of peoples, not factions, and ruling 3 wants personal relationships kept separate from group standing.

### 4.3 Peoples in canon lore not present near Ashen Hollow

- **Mor**: "What organisation exists is **custodial**". Schism "between those who want to recover the Lost Pacha and those who want to avenge it, and both factions are patient" (`docs/MYTHOLOGY.md:242-253`). This is the only place the lore itself uses "factions" for an intra-people split.
- **Constructed**: "their oldest institutions are **maintenance bodies**" (`:286-288`).
- **Vaskaal**: "A single expedition ... being litigated by their own command structure ... Their internal politics is entirely about whether to leave." (`:366-369`).
- **Ondrek**: "Few, old, and dispersed. They live inside other peoples' abandoned structures" (`:399-402`).
- **FACT:** none of these is placed in or near Ashen Hollow by any tracked document.

### 4.4 The one canon inter-group conflict about quarries - the Stone Question

- **FACT:** "A major faction conflict and multi-stage questline ... Design intent: **no correct answer.**" (`docs/STONE_QUESTION.md:3-8`). Three parties: "**The Kal** regard the quarries as graves"; "**The Ondrek** accept the Kal's grief as real and dismiss the Kal's certainty as unearned"; "**Everyone else** buys the result ... most settlements would rather not examine where the stone came from" (`:49-63`). Stage I: "An old quarry is being worked. The Kal have known for months and cannot stop it" (`:72-74`). Aftermath: "The Ondrek, if they succeed, become a faction that remembers who helped." (`:131`).
- **INFERENCE (unsupported by any text):** Blackvein Cut is "an old quarry" and Kera is Kal. **No document says Blackvein is Kal stone, an Ondrek working or a grave.** Iron Under Ash mines an iron seam, not quarried stone. Any link is a design choice, not existing lore.

### 4.5 Cosmology-level interests around the Other (the Foldscar is the Other's foothold in Phase 1)

- **FACT:** "There should not be one universally accepted explanation of the Other." (`docs/OTHERREACH_COSMOLOGY.md:524`). It lists these interpretive constituencies (`:526-545`):
  - Scholars ("folds connect normally separated coordinates");
  - Arcane practitioners ("The Veil is a magical boundary");
  - Druids ("Otherways follow those roots");
  - Religions ("divine realm ... prison, or forbidden domain");
  - Necromancers;
  - Folk belief ("Some roads are wrong. Some doors should not be opened.");
  - "Ancient technological interpretation".
- **FACT, settlements near folds** (`:684-696`): "settlements near folds attract scholars, merchants, or cults"; "dangerous folds increase risk"; "governments regulate gates"; "religious factions forbid certain crossings". These are "later-game possibilities and do not alter the early prototype roadmap".
- **FACT, candidate terms:**
  - "**Otherbound** ... an order or faction devoted to Other exploration. This remains a strong candidate for faction, quest-line, or character terminology." (`:450-459`)
  - Otherhome may be "home to factions that disagree about whether Other-travel should continue" (`:200`).
  - "Otherborn" is a social identity that may be "honored; feared ... subject to laws or prejudice" (`:219-232`).
  - "Othermarked" is "altered later by contact" with possible "social or religious stigma" (`:236-253`).
- **INFERENCE:** Tavar, held by the fold, fits "Othermarked" as a social category. No text says so.
- **FACT, the Foldscar is not named an "Otherfold" anywhere.** The bible uses "Foldscar", "misregistration", "Orenth displacement", "Resonance" (`:802-805`). The untracked lock uses "Foldscar distortion" (`:250`).

### 4.6 Named factions anywhere in the repository - status for Ashen Hollow

| Name | Where | Status |
|---|---|---|
| **The Vessmere Compact** - "town authority, order, trade" | `docs/VERTICAL_SLICE.md:94`, `:169` | Phase-2 slice faction for the town **Vessmere** in **Kaldrun Reach** (`:30`, `:44`). Wants "the iron seam reopened and the Ashlings expelled". Not connected to Ashen Hollow by any text. |
| **The Ashlings** - "outcast reclaimers of the old works" | `docs/VERTICAL_SLICE.md:61`, `:94`, `:169` | Same slice. Wants "the seam left shut and the works reclaimed". Contact NPC `npc.outcast_venn`. |
| `faction.settlement.stoneford_covenant`, `faction.bandit.ash_road_crew`, `faction.scholar.conclave_of_ash`, `faction.undead.barrow_host`, `faction.wildlife` | `docs/DATA_MODEL.md:102`, `:275`, `:297`, `:479`, `:489`, `:564-580` | Schema examples illustrating the `FactionDefinition` shape. "Starting faction; its tiers are the tutorial for 'reputation buys access, not power' (D-09)." (`:580`). Not content, not canon. |
| `faction.hollow.wardens` | `tests/Content.Tests/QuestContentTests.cs:120` | A deliberately nonexistent reference in a lint test. |

- **FACT:** the slice's town problem strongly parallels Phase 1: "the iron seam is deliberately sealed and the Ashlings want it opened" (`docs/VERTICAL_SLICE.md:49`; note §5.6 says the reverse, that the Ashlings want it "left shut", `:169`). Phase 1 has Blackvein's "blocked shaft" and an iron seam. **INFERENCE:** VERTICAL_SLICE was written before the bible, and the two share motifs (iron, ash, old works). No document reconciles them.
- **FACT:** VERTICAL_SLICE says "Two opposed factions is the minimum for reputation to be a *choice*. A third would need a third region to be meaningful." (`:94`). Reputation tiers: "Hostile / Wary / Neutral / Trusted / Sworn" (`:95`).

### 4.7 ENDGAME_QUESTLINES - faction names near the start region

- **FACT:** `docs/ENDGAME_QUESTLINES.md` names **no organisation** and no place near a start region. Its eight chains are keyed to peoples:
  - "The Measured Dead — Kal" (`:84`)
  - "The Front of the Book — Siann" (`:101`)
  - "The Found and the Empty — Orenth" (`:119`)
  - "The Author's Name — Constructed" (`:137`)
  - "The Cause — the Silence" (`:157`)
  - "The Weather — Vaskaal" (`:173`)
  - "The Accusation — Mor" (`:190`)
  - "The Ending — Ondrek" (`:209`)
  
  They run with graduated access (Native / Adopted / Borrowed / Rented, `:45-50`). The Kal chain "Runs through the Ondrek war, the Instrument, and the sealed archive" (`:91-92`).
- **FACT:** the Orenth chain's gated moments include "the crossing itself (Orenth, or a party led by one)" (`:132-133`). **INFERENCE:** Tavar is the only Orenth in the game so far. Nothing states he is intended for this chain.

---

## 5. Naming status

- **FACT, explicitly unsettled:**
  - "Names are working names until cultural naming is finalized." (bible `:265`)
  - "Working names, not permanently canon until cultural naming is finalized" (V4 `:262`, untracked)
  - Bible IDs are "design placeholders" (`:1138`)
  - "Otherhome Marches, a working name ... The lore name and biome are not final." (`docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:157`, `:167`)
  - VERTICAL_SLICE: "Names here are provisional and cheap to change while content volume is low." (`:398`)
- **FACT, setting tone is open:** "The setting-temperature part stays open ... Default if unanswered: a **melancholic, ancient, low-magic-feeling high fantasy**" (`docs/DECISIONS.md:262`).
- **FACT, naming canon rules that exist:**
  1. **Race-name root rule:** "One ancestral root family appears in the oldest layer of every language: three syllables, **veth**, **kal**, **mor**. Each culture kept one and lost the others, and each now believes its syllable is the original word for *person*." (`docs/RACES.md:26-35`). Open question: "Do the Veth know?" (`:707-708`).
  2. **Other- prefix discipline:** "The **Other-** prefix should be used deliberately. Not every strange thing should be called Other-something." A believable world also needs "regional names; religious terms; scientific terms; obsolete names; slang; folk terminology" (`docs/OTHERREACH_COSMOLOGY.md:722-737`). The vocabulary is split into a "Primary Working Vocabulary" (`:741-758`) and a "Secondary Candidate Vocabulary ... do not treat as established canon" (`:762-790`). The secondary list explicitly may "later become names for materials, factions, regions" (`:790`).
  3. **Originality:** "Do not copy proprietary names, quests, maps, dialogue, creatures, races, factions, or lore from existing games." (`docs/PROJECT_CHARTER.md:11`)
  4. **"Nothing here is a stock fantasy race. There are no dwarves, elves or gnomes."** (`docs/RACES.md:22`)
  5. **Definition-ID grammar** for any faction ID: `<kind>[.<subkind>]*.<snake_case_name>`, lowercase ASCII (`docs/DATA_MODEL.md:97`). Faction IDs take the `faction.` prefix (`:50`).
- **FACT, existing place-name morphology in Phase 1:** Ashen Hollow, Ashen Waystone, Charwood Verge, Blackvein Cut, Foldscar, Quiet Stones, Ash Haft, Ash Ember Hound, ashbloom, Woundmoss, March Spear. **INFERENCE:** these are plain English compounds with an ash / char / black / scar motif. "March" (the spear, and "Otherhome Marches") suggests borderland. No personal-name language rules exist. Renn Vale, Kera Voss, Sel Arien and Tavar Orr do not visibly follow per-people phonology.
- **FACT:** the bible does not explain "Ashen". Nothing records a past fire or burning.

---

## 6. Player acts in Phase-1 content that could matter to more than one group

"Currently" columns are FACT. "Plausible stakeholders" are INFERENCE, grounded only in the implied groups of 4.1 and the peoples of 4.2. The Phase-1 rules for these acts: "The prototype should tolerate: quarry visit before accepting Kera's quest; Foldscar visit before receiving Sel's quest; bow pickup before quest progression; obtaining ore without combat; ignoring the boar; killing or avoiding the spider; alternate routes." (bible `:979-987`).

| # | Act | Content hook (FACT) | Currently affects | Plausible stakeholders (INFERENCE) |
|---|---|---|---|---|
| A1 | **Aligning the three Quiet Stones and steadying the heart** | switches set `world.foldscar.*` once, in cell `c_01_00` (region `:166-192`). The quest counts stones turned before it starts (`three_quiet_stones.yaml:6`) | Tavar freed (barrier lifts); Tavar trust +10; Sel trust +5 and +10; 150 XP | Sel's survey (G3) gains; **whoever turned the stones (G4) loses**; the stones' purpose (G5) is restored, either "Holding something shut, or holding it still"; Orenth (Tavar's kin) may care. A classic two-group act: one benefits, one is thwarted, and only the player, Sel (who "saw the ring go quiet from the rise") and Tavar know. |
| A2 | **Freeing and recruiting Tavar** | `recruit_companion` (`tavar_orr.yaml:37`) | Companion joins | Sel's survey regains its guide. Orenth "Needed and disliked" (`MYTHOLOGY.md:221`) means a local reaction to an Orenth walking with the player could differ by people. Not built. |
| A3 | **Emptying the waystation's storage chest** | `container.waystation_chest` (salve, 6 arrows, 2 ashbloom); the acceptance run empties it at 0:08: "The waystation's storage chest opened and emptied" (`docs/acceptance/m6/transcript.md:13`) | Nothing: no ownership, no witness, no relationship | The waystation (G1, Renn as steward) is the obvious owner. Whether taking is theft, charity or entitlement is exactly the "legal status vs reputation vs relationship" separation of ruling 3. |
| A4 | **Looting the damaged merchant cart** (bow and 12 arrows) | `container.merchant_cart` (`content/loot/merchant_cart.yaml`); the bible frames it as "Bow found at merchant cart" (`:337`) | Nothing | The cart's unknown owner or trade interest (G2). Salvage vs theft. The Wayfarer is itself a road traveller. |
| A5 | **Mining Blackvein iron** | `node.ore.iron_seam` (finite, 3 charges, `docs/PROTOTYPE.md:173`); Quest 1 | Kera's quest; Renn respect +5 if the player tells him while carrying ore | Kera (a Kal smith who needs iron) gains. Former or future claimants of the quarry (G6) and the armour's maker (G7) could object. A Kal-grave or Ondrek reading of any quarry is lore-available (4.4) but unlinked. |
| A6 | **Killing the Animated Armour (sentinel) or the Bone Walker Husk** | spawns `iron_shelf_armour` / `iron_shelf_husk`; respawn none (they stay dead) | XP and loot (armour: 50% iron ingot) | Whoever set the guard (G7). Undead implies necromantic interest (Mor lore). Nothing states either. |
| A7 | **Killing wolves**: the den pack (4, no respawn), strays (2), east pack (2, 20 min respawn) | spawns (section 2.7); corpses loot meat, hide, fang | XP and loot; nothing social. Renn: "It'll keep a wolf honest." (`renn_vale.yaml:67`) | Waystation safety and road travellers (G1, G2) plausibly benefit. Wildlife (G8) as a "faction" exists only in DATA_MODEL's example. The old Halda quest wanted wolves killed. |
| A8 | **Looting the den cache** (3 iron ingots, 12 arrows, 2 salves, Halda's Token) | `container.den_cache` | Nothing | Halda (G9, legacy) and whoever stocked the cache. **INFERENCE:** a cache of ingots and arrows in a wolf den implies a human owner, unexplained. |
| A9 | **Killing or sparing the Cave Hunting Spider** | spider never leaves its lair; the non-kill route exists (bible `:250-259`) | Nothing social. Quest 2 has "no mandatory combat" (`:471`) | Sel's survey ("when the spider lets me", `sel_arien.yaml:11`) benefits from its death. No other stakeholder exists. |
| A10 | **Ignoring or killing the boar** | "It defends its wallow and ignores those who leave it be." | Nothing | None implied. |
| A11 | **Trading with Kera** (buying or selling at 40%) | `merchant.ashen_hollow.kera_voss`; `sell_ratio 0.4` | Items and coin only | Kera personally. The waystation economy (G1). VERTICAL_SLICE and DATA_MODEL gate prices by reputation tier (`VERTICAL_SLICE.md:95`, `:169`; `DATA_MODEL.md:566-571`). |
| A12 | **Smithing a fine vs poor spear and showing Kera** | quality ≥ 1 → respect +10 (`kera_voss.yaml:46-53`) | Kera's respect | Personal competence standing. **INFERENCE:** a craft-reputation seam that belongs to relationship, not faction, under ruling 3. |
| A13 | **Accepting Sel's primer and using Resonance magic** | `item.tome.resonance_primer` grants three spells (`content/items/tome/resonance_primer.yaml:13-18`) | Sel trust +5 | COSMOLOGY's interpretive constituencies (religions, arcane practitioners) could view magic differently. Nothing built. |
| A14 | **Entering buildings / opening doors** | `world.hollow.longhouse_door_open`, `world.hollow.forge_shed_door_open` | The door state persists | Trespass semantics (G1) do not exist. |
| A15 | **Dying and returning at the Ashen Waystone** | respawn at (30,158); XP debt; weakened (`docs/M6_STATUS.md:82`) | Player only | The bible explicitly holds back metaphysics (`:528`). No group should own this yet. |

**FACT:** no Phase-1 act is witnessed, reported, remembered by more than one NPC, or propagated. The only cross-NPC awareness is Sel's `visited` check on Tavar's greeting (`sel_arien.yaml:23`, `:60`).

---

## 7. Phase-2 space adjacent to Ashen Hollow

- **FACT, bible:** "Larger future region: Otherhome Marches" (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:6`).
- **FACT, V3 handoff (tracked):** "**First world region for asset production (lives only here):** the **Otherhome Marches**, a working name. It is a 2×2 km region adjacent to Otherhome, not the central city." It is meant to exercise (`docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:157-167`):
  - "a road toward Otherhome with a distant skyline";
  - "a village/waystation";
  - "farms, woodland, water and highland";
  - "wilderness building space";
  - "a mine/quarry, ruins and small dungeons";
  - "a limited or inactive Othergate";
  - "travelers from several cultures".
  
  "The lore name and biome are not final. This is Phase-2 vertical-slice territory; Phase 1 is the 200 m prototype."
- **FACT, Otherhome:** "the leading candidate for the main central city, starting city, or first major settlement". Possible origin: "displaced settlers founded a permanent refuge after losing access to their original home ... it became another home — **Otherhome**". Possible roles include "meeting place of multiple races and cultures", "city built around or near an ancient gate", and "home to factions that disagree about whether Other-travel should continue". "**Status:** strong candidate, not immutable canon." (`docs/OTHERREACH_COSMOLOGY.md:179-202`)
- **FACT, other mentions:**
  - The DeepSeek environment kit: "Build a SMALL Otherhome-Marches prototype environment kit." (`docs/DEEPSEEK_PLAYABLE_ASSET_SPRINT_PROMPT.md:444`)
  - An asset status note: "the prototype's place is **Ashen Hollow** (200 m × 200 m, 4 cells), not 'Otherhome Marches', which is a 2 × 2 km Phase-2 region." (`docs/PLAYABLE_ASSET_SPRINT_STATUS.md:842-843`)
  - An illustrative cell key `region.otherhome_marches.cell_14_28` (`docs/M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md:191`)
- **FACT, VERTICAL_SLICE's region is a different name:**
  - "**Kaldrun Reach** — one contiguous 2 000 m × 2 000 m region" (`:30`)
  - town "**Vessmere** (fishing + iron, on the lake outlet)", "~150 m × 150 m" (`:44-45`)
  - 8 named NPCs (`:52-63`)
  - 2 factions (`:94`)
  - "One buildable plot on the town's north edge, 40 m × 40 m ... (`r_0_0:c_03_07`)" (`:50`, `:161`)
  
  No document states whether Ashen Hollow lies in Kaldrun Reach or the Otherhome Marches.
- **FACT, addressing:** a region is "2000 m × 2000 m (2×2 km)" with "20×20 = 400 exterior cells" (`docs/WORLD_ARCHITECTURE.md:26-27`), and cell indices lie in `[0, 19]` (`:45`). Ashen Hollow is `r_0_0` with cells `c_00_00`..`c_01_01`. **INFERENCE:** structurally, Ashen Hollow occupies the four south-west cells of a 2×2 km region `r_0_0`. The other 396 cells are unauthored. VERTICAL_SLICE's plot key `r_0_0:c_03_07` would also sit in `r_0_0`, 300-400 m east and 700-800 m north of the origin. That is a numbering coincidence, not a stated plan.
- **FACT, the prototype's in-world edges:**
  - "a sheer ravine on three sides" (`docs/PROTOTYPE.md:62`)
  - the blocked shaft "communicates that the world extends beyond the prototype" (bible `:221`)
  - the road runs off-map at both ends (bible `:102-118`)
  - the stream runs in from (190,195) (`:186`)
- **FACT, schedule:** the ROADMAP M9 slice is "the integration point where the systems built in M7–M8 meet the slice's content target" (`docs/ROADMAP.md:296`). M7 exit criteria include "the same act moves two factions in opposite directions in a fixture" (`:284`) and "a reputation fixture table" (`:285`). **INFERENCE:** M7 needs only fixture factions to exit. Canonical Ashen Hollow factions are not required by the roadmap.
- **FACT, M7 authorization context (untracked):** "Do **not** begin M7 until Phase-1 consolidation, performance proof, and required playtest work are finished." (V4 `:20`); "**M7 remains unauthorized**" (V4 `:527`). This is research only.

---

## 8. Owner rulings - where each is recorded, and conflicting documents

| # | Ruling (from the task) | Recorded at (FACT) | Conflicts / tensions found |
|---|---|---|---|
| 1 | Navigation not Godot-authoritative; deterministic, headless, rebuilt from structure state, across seams | Not found as such in tracked docs or V4. Related: `docs/RISK_REGISTER.md:280-294` (RK-14, seam stitching "Rebuild navmesh across a stitched multi-cell neighborhood"). | `docs/WORLD_ARCHITECTURE.md:414` ("Navmesh around buildings is rebuilt per cell on change, debounced") and `:435` ("Recast-style, baked per cell, stitched at cell borders"); `docs/ROADMAP.md:284-285` ("the navmesh updates on placement", "navmesh path test"). All are engine-neutral wording, but they need reconciling to the ruling. `docs/M3_STATUS.md:72` records a Godot/Recast navmesh bake in the perf spike (measurement only). Also: a den rock already straddles the A/B seam (`content/regions/ashen_hollow.yaml:94`). |
| 2 | Building v1 is one storey | Not found as such. `docs/VERTICAL_SLICE.md:98`, `:161` (4 families: foundation/floor, wall, roof, opening; "no structural simulation"). The bible bans "player settlement building" in Phase 1 (`:1080`). | `docs/ROADMAP.md:283` lists "foundations, walls, floors, roofs, doors". "Floors" is ambiguous and could be read as multi-storey. `docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:34` lists "stairs; lifts" as streaming transitions, not building pieces. |
| 3 | Factions: smallest set; no morality meter; keep the listed dimensions separate; the same act can hit two factions differently; information not global | Charter: "Do not reduce morality to one universal 'good/evil meter.' Different people should interpret the same decision differently." (`docs/PROJECT_CHARTER.md:725-727`). V3 handoff hostility row (`docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:145`). S-27: "Reputation answers *access* ... **It never decides who attacks**" (`docs/SYSTEMS.md:309`). S-26: "no single good/evil meter" (`:299`). Code: `src/Domain/Social/Social.cs:27`. V4 §40 (`:1402-1414`, untracked). "Hostility/reputation should spread through information." (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:637`). "no psychic faction omniscience" (V4 `:459`). ROADMAP M7 (`:282`, `:284`). Smallest-set reasoning: `docs/PROTOTYPE.md:33`; `docs/VERTICAL_SLICE.md:94`. | **`docs/VERTICAL_SLICE.md:169`**: backing the Compact "makes the Ashlings hostile"; backing the Ashlings "puts town guards on you". A reputation choice directly driving hostility and guard response conflicts with S-27 and ruling 3's separation of reputation from tactical threat and attack legality. **`:95`** names a reputation tier "Hostile", which conflates standing with hostility. `docs/ROADMAP.md:284` tests "opposite directions". That is narrower than "differently" but compatible. `docs/PROTOTYPE.md:110`, `:264`, `:395` (A-2) claim a saved `faction_id` on NPCs that does not exist in code (section 9). |
| 4 | C10 (one den encounter forcing sword, bow and every spell) retired | `docs/PROTOTYPE.md:298`: "**Revised by owner ruling (2026-09-24):** ... No single encounter is required to force every combat tool. The March Spear is not weakened". Also `docs/M6_STATUS.md:105`, `:141`. | **V4 handoff §18** (`G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:611-620`, untracked) still calls the direction "not yet formally owner-ratified". It is stale relative to `PROTOTYPE.md:298`. The wolves themselves remain a bible divergence "the owner can drop" (`docs/M6_STATUS.md:32`). |
| 5 | No core action may require a radial | Bible §20: "no core action, spell, weapon, companion command, building function, emote, or interaction may require a radial menu" (`:645`), "No capability disappears if radial menus are disabled." (`:670`). V4 §3 (`:78-82`). Companion commands "No radial menu is required." (bible `:288`). | `docs/HUD_INPUT_AND_ACTIONS.md:36`: "Controller users should retain the same underlying capability through context/radials". This could be read as radials carrying controller capability. It needs wording aligned. |
| 6 | No networking in M7 | "No networking code is written in Phase 0–2. Only the *seam* exists." (`docs/DECISIONS.md:71`). Bible non-goals (`:1090`). VERTICAL_SLICE non-goal (`:329`). V4 (`:131`, `:1514`). | None found. |
| 7 | Engine-independent C#; presentation observes and submits commands; dotted IDs; ULIDs; sparse deltas | `AGENTS.md`; `docs/DECISIONS.md:64-71`, `:238-240`; V4 §4 (`:110-131`, untracked); ID grammar `docs/DATA_MODEL.md:97`. | None in my topic. |

---

## 9. Contradictions found (each cited)

1. **Doc vs code, faction field.** `docs/PROTOTYPE.md:110` says "`faction_id` field exists on NPCs and is saved" (restated at `:264` and assumption A-2 `:395`). The code has no such field: `NpcDefinition(string Id, string Name, string Role, ImmutableArray<string> Services, string? MerchantId, string? DialogueId)` (`src/Domain/Social/Social.cs:10`), and no faction reference exists in `src/Persistence`, `src/World`, `src/Application` or `src/EntityRegistry`. M7 will need a save-schema step for NPC membership or player reputation, contrary to A-2's "no save migration is needed later".
2. **Doc vs doc, the Phase-2 region's name and shape.** The bible `:6` and V3 handoff `:157` say "Otherhome Marches" (adjacent to Otherhome, with a waystation, quarry, ruins, inactive Othergate). `docs/VERTICAL_SLICE.md:30`, `:44` say "Kaldrun Reach" with town "Vessmere" (lake, fishing and iron). Neither mentions the other or Ashen Hollow.
3. **Doc vs doc, the slice's peoples and magic vs the later canon.**
   - VERTICAL_SLICE races `race.human_vess`, `race.ashborn` (`:88`) vs the eight peoples (`docs/RACES.md`; V3 handoff `:155`).
   - Schools Ember/Ward/Mend/Reave/Iron (`:83`) vs the content domains Force/Warding/Vital (bible `:381-391`).
   - VERTICAL_SLICE reconciles only mana → Focus/Strain (`:137`).
4. **Doc internal, VERTICAL_SLICE Ashlings' goal.** `:49` says "the iron seam is deliberately sealed and the Ashlings want it opened". `:169` says the Ashlings "want the seam left shut and the works reclaimed", and the Compact "wants the iron seam reopened".
5. **Doc vs ruling 3 / S-27.** VERTICAL_SLICE §5.6 lets a reputation choice make a faction hostile and set guards on the player (`:169`), and names a tier "Hostile" (`:95`). S-27 says reputation "never decides who attacks" (`docs/SYSTEMS.md:309`).
6. **Doc vs doc, the C10 ruling's status.** `docs/PROTOTYPE.md:298` records the owner ruling of 2026-09-24. The untracked V4 handoff §18 (`:611-620`) says "not yet formally owner-ratified".
7. **Bible vs content, wolves.** The bible's enemy roster has five archetypes and no wolves (`:294-314`). Content keeps a den pack, strays and a respawning pack (`docs/M6_STATUS.md:32`). The V4 handoff lists "legacy wolf den vs five-archetype lock" as open (`:1352`, untracked).
8. **UNTRACKED lock vs content, Renn.** The lock says Renn represents "persistent acknowledgment of major prototype events" (`G:/UNNAMED/docs/PHASE1_QUEST_NPC_AND_CONTENT_LOCK.md:62`). Renn's dialogue acknowledges only carried ore and a carried spear (`content/dialogue/ashen_hollow/renn_vale.yaml:32-43`), not the Foldscar or Tavar.
9. **Bible vs UNTRACKED lock, Blackvein.** Bible: "A shallow abandoned quarry / iron working" (`:204`). Lock: "Blackvein has become unsafe" (`:213-214`). "Become unsafe" implies recent use. These are compatible but carry different histories.
10. **Doc vs doc, the den location.** `docs/PROTOTYPE.md:72` says the den was kept "at its north edge". `docs/M6_STATUS.md:17`, `:90` and `content/regions/ashen_hollow.yaml:92` say the north-west corner (112,184).
11. **Stale content notes.** `content/spawns/hollow/iron_shelf_armour.yaml:6` refers to "the M3 forge shed" and a divergence that no longer exists. `content/spawns/hollow/boar_wallow.yaml:6` refers to "the palisade", which no longer exists.
12. **Bible vs content, the starting sword.** Bible, lock and V4 say "arming sword" (`:405`; lock `:28`). Content is `item.weapon.rusted_sword`, "Rusted Sword" (`content/items/weapon/rusted_sword.yaml:6`).
13. **Lore internal, magic prohibitions.** `docs/RACES.md:686` says race grants "never a prohibition — except the Vaskaal, who genuinely have no magic". But `docs/RACES.md:209` says Kal have "No affinity for magic at all", and `docs/STONE_QUESTION.md:41` says Ondrek have "No magic". Relevant only if a Kal NPC like Kera would ever use Resonance.
14. **Two parallel cosmological vocabularies, unreconciled.** MYTHOLOGY and RACES frame the setting through "Pacha", "Pachakuti" and "the Silence" (`docs/MYTHOLOGY.md:15-79`). OTHERREACH_COSMOLOGY frames it through "the Other", "the Veil", "Otherfolds" and "the Otherfall" (`:30-89`, `:312-331`). The V3 handoff lists both as setting sources (`:155`). The bible uses "the Other" vocabulary (`:281`, `:802-807`). RACES borrows "Otherhome" (`:107`). No document maps one onto the other, for example whether a Pachakuti is an Otherfall. This is not strictly contradictory, but a faction built around the Foldscar would need to choose which frame its beliefs use.

---

## 10. Open questions (for the owner or designer; none answered by the sources)

1. Who owns or appointed the waystation, and what authority (if any) Renn answers to? Is there law at Ashen Hollow at all?
2. Who turned the Quiet Stones out of line, and is that party a present-day interest (a candidate faction) or history?
3. Whom does Sel survey for? Is she an independent scholar or attached to an archive, court, university or order (the Siann institution types)?
4. Who owned the damaged merchant cart and the den cache, and is taking from them theft?
5. Who worked Blackvein Cut, why was it abandoned, who blocked the shaft, and who set the armour to guard the seam? Is there any intended link to the Kal/Ondrek Stone Question, given Kera is Kal?
6. Is Ashen Hollow inside the "Otherhome Marches", inside VERTICAL_SLICE's "Kaldrun Reach", or neither? Does the M9 slice replace or extend the bible's region? (`r_0_0` is shared by both on paper.)
7. Do the legacy wolves stay (V4 "open"), and if so, are they "wildlife" in any faction sense?
8. Does Halda (and her brother) exist in the current fiction, or should the token be retired?
9. Which cosmological vocabulary (Pacha vs the Other) do Phase-2 group beliefs use about the Foldscar?
10. Should the NPCs' people (Veth, Kal, Siann, Orenth) become data (`species_ref` is not built), and if so is it kept strictly separate from faction membership, as ruling 3's separation suggests?
11. Should Renn get the "persistent acknowledgment of major prototype events" the untracked lock promises (the Foldscar, Tavar, the emptied chest)? Given cell-local dialogue flags, how does he learn of them?
12. Is VERTICAL_SLICE's Vessmere Compact / Ashlings pair still intended canon, given the later bible and region naming?

---

## 11. Files read (for provenance)

Snapshot (`e10d2c4`):
- `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` (all), `MYTHOLOGY.md` (all), `OTHERREACH_COSMOLOGY.md` (all), `STONE_QUESTION.md` (all), `VERTICAL_SLICE.md` (all), `ENDGAME_QUESTLINES.md` (all), `MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md` (all).
- `RACES.md` (`:1-330`, `:330-370`, `:408-420`, `:469-482`, `:599-725`); `WORLD_ARCHITECTURE.md` (`:1-60`, `:105-130`, `:379-424`); `PROTOTYPE.md` (`:25-150` and targeted greps).
- `ROADMAP.md` (M4 note, M7-M9); `SYSTEMS.md` (S-26, S-27); `DATA_MODEL.md` (`:95-105`, `:268-310`, `:555-585`); `M4_STATUS.md`, `M6_STATUS.md` (targeted); `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` (`:1-60`); `OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md` (`:140-200`); `PROJECT_CHARTER.md` (`:11-40`, `:712-735`); `DECISIONS.md` (greps).
- Content: every file under `content/regions`, `npcs`, `dialogue`, `quests`, `merchants`, `locations`, `world_flags`, `spawns`, `loot`, plus `content/_aliases.yaml`, `config/economy.yaml`, `config/companion.yaml`, `config/creature_behaviour.yaml`, `items/quest/halda_token.yaml`, `items/tome/resonance_primer.yaml`, `items/weapon/*.yaml`, and the creature notes.
- Code: `src/Domain/Social/Social.cs`, `src/Content/SocialContent.cs`, `src/Domain/Quests/Quests.cs:140-160`, `src/World/Runtime/Combat.cs` (targets); faction greps across `src`, `tests`.

UNTRACKED (authority unconfirmed):
- `G:/UNNAMED/docs/PHASE1_QUEST_NPC_AND_CONTENT_LOCK.md` (all)
- `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md` (`:1-140`, `:185-300`, `:440-470`, `:600-630`, `:1340-1520`)
- `G:/UNNAMED/docs/FOR DEEPSEEK/DEEPSEEK_POST_M6_ASSET_MAINTENANCE_PROMPT.md` (greps)

Reports: `G:/UNNAMED_HISTORY/OVERNIGHT_REPORT_2026-09-24.md` (greps only; no new lore).
