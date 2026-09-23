# DATA_MODEL.md — Content Definitions, Identity, and Runtime State

**Project:** Otherreach (codename UNNAMED) — full-body third-person with seamless first-person zoom, solo-first, open-world fantasy RPG
**Phase:** 0 — Architecture. **Status:** Draft for owner review.
**Reads:** `PROJECT_CHARTER.md`, `PHASE_0.md`, `DECISIONS.md` (D-03, D-04, D-05, D-07, D-09, D-10), `SYSTEMS.md` (S-18 loads this data; S-02 assigns runtime identity).
**Audience:** an implementation session writing the C# schema types, the YAML loader, the validator, and the sample content.

Defines **how content is represented and where the line sits between definition data and runtime state**. One or two examples per schema is the maximum (D-03, PHASE_0 prohibitions).

| Rule | Statement | Source |
|---|---|---|
| M-1 | Content is human-readable **YAML** under `content/`, validated at load against **C# schema types**. | D-03 |
| M-2 | Definitions are referenced by **string definition IDs** — never by file path, GUID, or object reference. | D-03, D-04 |
| M-3 | Definition IDs are dotted, lowercase, namespaced by kind, and **never change once shipped**; renames require a migration map. | D-04 |
| M-4 | Runtime instances get **ULID** instance IDs from the Entity Registry at creation. Never authored by hand. | D-04, D-10 |
| M-5 | Validation failure at load is a **hard error with file and line**, never a null reference mid-quest. | D-03 |
| M-6 | Definitions are **immutable at runtime**; mutate the instance or add a modifier, never the definition. | D-02, D-11 |
| M-7 | Behaviour differences are expressed as data over a **closed vocabulary** — no per-quest, per-item, or per-spell engine code. | D-03, D-07 |

Notation: `id_ref` = cross-reference to a definition ID; `dur` = game minutes; `pct` = 0..1 float unless stated.

---

## 1. File layout, kind table, and loading

```
content/
  items/  resources/  creatures/  npcs/  spells/  abilities/  effects/  recipes/
  quests/  dialogue/  factions/  loot/  merchants/  regions/  world_flags/  facts/
  species/  schedules/  sets/  anchors/
  nodes/  spawns/  locations/  config/
  _tags.yaml      # closed tag vocabulary
  _aliases.yaml   # ID migration map (append-only) — schema owned by §2.1
```

**This list is closed and normative**, and — critically — **every directory in it maps to exactly one `kind` in the table below, and every `kind` in that table maps to exactly one directory.** That bijection is what makes the `PROTOTYPE.md` §4.4 rule implementable: *"no other content directory may exist in Phase 1; the validator fails the build on an unknown top-level content kind."* A directory with no `kind` and a `kind` with no directory are both validator errors.

`_tags.yaml` and `_aliases.yaml` are **underscore-prefixed, and the validator must skip any path whose basename begins with `_`.** They are not definition directories and carry no `kind`; treating them as content is the specific mistake that makes a "closed kind table" self-contradictory. An earlier revision listed `flags/` and `facts/` while providing no `flag`, `fact`, `region`, `species`, `schedule`, or `set` kind, so any file legal by that directory list was rejected by the validator the same section mandated. The table below is the repair.

One definition per file. YAML anchors and multi-document files are disallowed: anchors create hidden coupling, and multi-document files break per-file error reporting.

Load order (S-18): read all files → parse → resolve `kind` to a C# schema via the kind table → deserialize → collect all IDs → **cross-reference validation** (§5) → expose read-only tables. The second pass is mandatory because forward references between files are legal and expected. Hot-reload is developer-builds only; shipped builds use the compiled cache, because baseline determinism (D-05) depends on a frozen content version.

| `kind` | Schema | ID prefix | `kind` | Schema | ID prefix |
|---|---|---|---|---|---|
| `item` | ItemDefinition | `item.` | `recipe` | RecipeDefinition | `recipe.` |
| `item.weapon` | WeaponDefinition (extends Item) | `item.weapon.` | `resource` | ResourceDefinition | `resource.` |
| `item.armor` | ArmorDefinition (extends Item) | `item.armor.` | `quest` | QuestDefinition | `quest.` |
| `creature` | CreatureDefinition | `creature.` | `dialogue` | DialogueDefinition | `dialogue.` |
| `npc` | NPCDefinition | `npc.` | `faction` | FactionDefinition | `faction.` |
| `spell` | SpellDefinition | `spell.` | `loot` | LootTableDefinition | `loot.` |
| `ability` | AbilityDefinition | `ability.` | `merchant` | MerchantProfile | `merchant.` |
| `effect` | StatusEffectDefinition | `effect.` | `affix` | AffixDefinition (§4.20) | `affix.` |
| `node` | NodeDefinition (§4.16) | `node.` | `spawn` | SpawnDefinition (§4.17) | `spawn.` |
| `location` | LocationDefinition (§4.18) | `location.` | `config` | ConfigDefinition (§4.19) | `config.` |
| `skill` | SkillDefinition (§4.21) | `skill.` | | | |

**Referenced kinds — minimum shape.** These seven are the kinds `§5`'s cross-reference table resolves and `Assumptions` 1–2 previously left "implied". They are **in the closed table**, because a reference that resolves to a kind the validator does not know is a reference the validator cannot check. Each is specified to the minimum depth references require; full specification belongs to `WORLD_ARCHITECTURE.md` (regions, anchors, schedules) and `PROGRESSION.md` (attributes).

| `kind` | Schema | ID prefix | Directory | Minimum required content |
|---|---|---|---|---|
| `species` | SpeciesDefinition | `species.` | `species/` | Trait list, attribute biases, racial abilities, homeland region ref, inter-species attitude table |
| `schedule` | ScheduleDefinition | `schedule.` | `schedules/` | Ordered phases with `anchor_ref` + time band per phase; abstract-tier profile (D-06) |
| `set` | ItemSetDefinition | `set.` | `sets/` | Member `item.*` refs, per-member-count bonus table (consumed by `ItemDefinition.set_ref`) |
| `region` | RegionDefinition | `region.` | `regions/` | Extent, biome, level band, spawn-table refs, ambient preset, authored cell-set |
| `anchor` | AnchorDefinition | `anchor.` | `anchors/` | Named point in a region or cell, used by `NPCDefinition.anchors` and `ScheduleDefinition` phases |
| `fact` | FactDefinition | `fact.` | `facts/` | Text key, source, propagation/known-by rules; the `known_fact` command target (§4.12) |
| `world_flag` | WorldFlagDefinition | `world.` | `world_flags/` | Declared type (`bool`\|`int`\|`string`), default, and owning system — a flag's type is save-version-sensitive (§6) |

**`world_flag` uses the `world.` prefix, not `flag.`**, and lives in `world_flags/` so the directory name cannot be confused with the kind name. Its IDs are referenced as `world.*` throughout (§4.11 reward kinds, dialogue consequences). The validator enforces the prefix, so the directory/kind pair is unambiguous while the ID namespace stays as authored.

**`merchant` is a kind without a `merchants/` resolver target** in §5's table; it is referenced by `npc.*` and stored in `merchants/`, and appears in the main table above rather than here.

**Note on `resource` vs `node`.** `resource` is the *material* (a definition of iron ore, its properties, its tiers). `node` is a *harvestable placement* of that material in the world (yield, respawn class, tool tier required). They are distinct kinds because one material has many node variants and node behaviour is world-placement data, not material data.

**Suffix rule:** the ID's segments must match the file stem and the ID prefix must match `kind` (`kind: item.weapon` ⇒ `item.weapon.*` in `content/items/`). The validator enforces this; it is the cheapest way to kill a whole typo class.

### Common envelope (every definition)

```yaml
schema: 1                                  # schema revision; bump on incompatible shape change
id: item.weapon.iron_sword                 # required, grammar in §2
kind: item.weapon                          # required; selects the C# schema type
display_key: item.weapon.iron_sword.name   # required for anything player-visible; never raw English in logic
tags: [weapon, sword, metal, mundane]      # from content/_tags.yaml (validator-enforced)
```

`notes` is encouraged on every definition: it is how the *why* stays next to the content (D-03). Optional envelope fields: `defines` (sub-IDs this definition creates), `deprecated`, `alias_of` (alias/removal records only, §2.1).

---

## 2. Identity conventions

### 2.1 Definition IDs (D-04)

Grammar: `<kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]`, lowercase ASCII, `.`-separated, `[a-z0-9_]` per segment.

```
item.weapon.iron_sword          creature.beast.wolf_grey        effect.bleeding
recipe.smithing.iron_ingot      resource.ore.iron_vein          faction.settlement.stoneford_covenant
quest.artifact.shattered_crown.03     dialogue.settlement.miller_hale.intro
```

- The **ordinal** segment (`.03`) orders series: quest-chain steps, dialogue variants, tier variants. Fixed-width two digits so lexical sort equals narrative order.
- IDs are a **public API**. Renames are expressed only in `content/_aliases.yaml`, which is append-only and never rewritten:

```yaml
aliases:                                              # renamed IDs, kept forever
  item.weapon.ironsword: item.weapon.iron_sword
removed:                                              # merged/removed IDs, mapped forward
  quest.artifact.shattered_crown.09: quest.artifact.shattered_crown.10
  item.junk.cracked_bottle: ~                         # removed with no replacement: references are dropped, as reported loss
```

The loader resolves an alias on read and logs a deprecation warning. A save referencing a removed ID must resolve through `removed` or fail load loudly — never a silent drop (D-05). A removal mapped to `~` is the explicit `destroy` disposition of `PERSISTENCE.md` §6.3. It is declared here and reported by every load and dry run, so it is not a silent drop. Renames may chain; a cycle is an error. The validator also rejects: an alias target or replacement that is not defined (`ALIAS001`, `ALIAS003`); an ID listed as renamed or removed that is still defined (`ALIAS004`); and anything in the file that is not a definition-ID mapping (`ALIAS005`). The map is part of `content_hash`.

**The CI guard.** The committed historical save fixtures (`tests/Persistence.Tests/Fixtures`) load against their content pack in CI. Renaming or removing an ID they reference, without an entry here, fails the build (M2b).

### 2.2 Instance IDs (D-04, D-10)

Format `<prefix>_<ULID>`: a lowercase kind prefix and a canonical 26-character Crockford-base32 ULID, which is uppercase, and sortable: `itm_01J8ZC4K9P4M2Q7X8B3NDTVW6R`. Parsing accepts a lowercase ULID and normalizes it; comparison is ordinal. (An earlier wording said "lowercase" for the whole ID while its own example was uppercase.) Generated **only** by the Entity Registry (S-02) at creation. Content files never contain instance IDs — a ULID-shaped value in YAML is a validation error. Prefixes: `itm` item, `npc` NPC, `crt` creature, `bld` building, `cnt` container, `qst` quest instance, `crp` corpse, `anc` travel anchor, `sum` summon, `plt` farm plot, `evt` world-event instance, `chr` player character (added by M2).

---

## 3. Definitions vs runtime state — the required argument

Three tests decide every field, applied in order:

1. **Sharing test.** If two instances could hold different values, it cannot be a shared definition field for those instances — it is instance state (or an authored definition variant, if the difference is designed rather than earned).
2. **Baseline test.** If a value is recomputable identically from the baseline tuple - the world seed and the generator contract, including its placement data (`PERSISTENCE.md` §1.3) - with no player input, it does not belong in the save; it belongs to the deterministic baseline (D-05). If it is not recomputable, it must be persisted.
3. **Authority test.** If a value is read as truth by more than one system (health, position, ownership, quest progress), it lives in the World State Store (S-03) and nowhere else.

### 3.1 Where the line sits

| Thing | Definition (content) | Instance state (persisted when non-baseline) |
|---|---|---|
| Weapon | base damage, damage type, reach, attack speed, stamina cost, scaling ratios, weapon skill (`skill_ref`), requirements, model, base value | current durability, rolled quality, enchantments/sockets applied, rolled affix magnitudes, crafter signature, display-name override |
| Armor | armor value, slot, weight, movement penalty, material class, requirements, set membership | current durability, enchantments, upgrades, rolled quality |
| Creature | species stats, attack set, habitat, fixed level band, loot table ref, faction, perception, abstract-schedule profile | current health, active effects, aggro, position, alive/dead, looted flag, named flag, morale |
| NPC | role, services, schedule template, dialogue ref, faction, home/work anchor refs, greeting policy | generated identity (name, voice), current phase and coarse position if diverged, disposition, relationship memory, alive/dead/moved state, merchant stock |
| Spell (formula) | magic-domain skill, complexity, Focus/Strain cost plus contextual costs, cast time, range, payload, cooldown, visual | remaining cooldown (only if `persist_cooldown`); the known flag lives in the character's knowledge record (`PROGRESSION.md` §4.4), and domain competence is a skill |
| Status effect | duration, stack policy, tick interval, contributed modifiers, dispel category, immunity tags | stacks, remaining duration, caster ref, tick cursor |
| Recipe | inputs, outputs, station, technique/skill requirements, difficulty, quality curve | known flag (the character's knowledge record), discovered experimental variants, in-progress job timers |
| Resource node | yield range, tool requirement, respawn window, biome placement rule | remaining yield, depleted flag, regrowth deadline, planted-by-player flag |
| Quest | objectives, branches, failure conditions, time limits, level band, rewards | state, per-objective progress, chosen branches, absolute deadlines, repeat counters |
| Building piece | sockets, material cost, health, nav footprint, station capability | transform, owner, current health, repair state, attached storage, occupant assignment |
| Location | region, biome, level band, spawn tables, POI type, entrances, ambient preset | discovered flag, discovery method, map reveal level, visit count |
| Faction | membership graph, attitude defaults, service gates, territory | player reputation value/tier, at-war flags, crime records, bounties |
| Loot table | entries, weights, conditions, guaranteed slots | the source's `looted` flag and which draw was taken |

### 3.2 Rule of thumb, stated once

> **If you must ask "which instance is this?" to answer, it is instance state.**
> **If you must ask "which content version authored this?" to answer, it is definition data.**

Corollaries enforced in code: base-vs-current pairs are always named so (`durability_max` in the definition, `durability_current` on the instance — a bare `durability` is a schema-review failure); anything that must be replayable (loot rolls S-16, spawn picks S-31, weather S-37) consumes a seeded RNG stream derived from `(world_seed, definition_id, instance_or_cell_id, purpose)` rather than persisting the roll.

### 3.3 Worked micro-cases (often decided wrong)

A one-off named sword with +3 fire damage is an **instance override** (if many players can earn it, author a definition instead); a named alpha wolf is a **definition variant** (the instance carries only `named: true` and a generated name); an NPC's opinion after a quest, a merchant's post-trade stock, and a quest's chosen branch are **instance state**, while initial relationships and restock templates are definition data; a deterministic ore vein's *cell* is neither (baseline-generated from seed + `placement`), but its *depletion* is delta state (S-19/S-20); journal text is definition data per node and is never mutated; a quest-set world flag is **world state declared in `content/world_flags/`** as `kind: world_flag` with ID prefix `world.` (§1 — an undeclared flag is an error); a spell's post-fight cooldown is transient unless `persist_cooldown: true`.

---

## 4. Schema reference and examples

Each subsection gives the **schema** (as a commented YAML skeleton: every legal field is shown, `?` = optional, and `# id:` marks where the definition's own ID goes) and **one concrete example**. Enum values listed in a comment form a closed set; new values require a `schema` bump. All schemas inherit the common envelope, so `schema`, `id`, `kind`, `display_key`, and `tags` are not annotated again.

These examples are illustration, not content: when Phase 1 begins, each graduates into a real file under `content/` (S-18) and is deleted from this document, leaving the schemas behind.

### 4.1 ItemDefinition — `kind: item`

```yaml
# id: item.consumable.field_bandage    (envelope: schema/kind/display_key/tags, §1)
category: consumable          # weapon|armor|consumable|material|tool|book|quest_item|container|currency|misc
stack_max: 10                 # 1 = never stacks
weight: 0.2
value_base: 8
rarity: common                # common|uncommon|rare|epic|legendary|artifact
use: { effect_ref: effect.regeneration.minor, charges: 1, cooldown_min: 2, consume: true }
# note: Baseline survival item; must stay useful after better potions exist as the cheap option.
# also: durability_max? (absent = indestructible) | requirements: {level, attribute.*, skill.*}
#       affix_pool: [affix.*] | enchant_sockets: int | set_ref?
```

**`use.grants` — how content changes the character (the `ember_primer` shape).** An earlier draft of this schema could express *damage*, *heal* and *status* on use, but not **grant a capability**, and the prototype's single most instructive content item depends on exactly that: `PROTOTYPE.md`'s `item.tome.ember_primer` is "read once → grants `spell.ember.bolt` + `spell.ward.oakskin`", and its stated purpose is to prove "content can grant capabilities" — the interesting half of criterion C7, which requires that adding an item touch **zero** C# files. With no such field, an implementing session would either invent one ad hoc (fragmenting an unfrozen schema, uncovered by the validator) or write a C# special case and fail C7.

`use.grants` is a **list of the same closed reward kinds defined in §4.11** — this is deliberately *not* a new vocabulary, so the validator's existing reward-kind legality check covers it with no new rules:

```yaml
id: item.tome.ember_primer
category: book
stack_max: 1
weight: 1.0
value_base: 120
rarity: uncommon
use:
  consume: true               # reading the tome destroys it
  grants:
    - { kind: spell,    ref: spell.ember.bolt }      # deliberate: one offensive, one defensive
    - { kind: spell,    ref: spell.ward.oakskin }    # so the proof covers two magic domains at once
  teach_requires: { skill: { skill.research: 10 } }  # optional gate; absent = usable immediately
# note: Reading a tome is the only path in Phase 1 by which content grants a permanent capability,
# which is why it is the worked example rather than a contrived one.
```

The same mechanism carries `ability`, `recipe`, `title`, `access`, `world_flag`, `permanent_ability` and `transformation` grants without further schema work — a tome that teaches a recipe and a shrine that grants a title are the same field with a different `kind`. **`custom_scripted` remains deliberately absent** (see §4.11): if a desired grant cannot be expressed as one of the closed kinds, the correct response is to add a kind, not to add scripting.

**M3b reconciliation (the prototype's items).** A plain item may be worn in a jewelry slot with `equip_slot: ring|amulet`, as the wolf fang is: the prototype's "non-armor, non-weapon equipment slot". `requirements` holds only `attribute.<name>` and `skill.<id>` minima, never a level (`PROGRESSION.md` §11.1). Ammunition is a `misc` item that its weapon names with `ammo_item_ref`. A ranged weapon may give `draw_time` instead of `reach`. Coin is not an item in Phase 1: it is a purse on the character.

### 4.2 WeaponDefinition — `kind: item.weapon` (extends ItemDefinition)

```yaml
# id: item.weapon.iron_sword
category: weapon
stack_max: 1
weight: 3.2
value_base: 90
rarity: common
durability_max: 120
requirements: { level: 1, attribute.might: 8 }
damage: [7, 11]               # [min,max] per swing, before scaling
damage_type: physical_slash   # physical_slash|physical_pierce|physical_blunt|fire|frost|shock|arcane|holy|necrotic|poison
attack_speed: 1.1             # swings per second
reach: 1.8                    # meters; drives S-12 hit resolution
stamina_cost: 14
scaling: { might: 0.8, agility: 0.2 }
skill_ref: skill.one_hand_blade  # S-09 weapon-family skill (PROGRESSION.md §6)
hands: one                    # one|two|offhand
moveset: [ability.moveset.sword_light, ability.moveset.sword_heavy]
affix_pool: [affix.weapon.keen, affix.weapon.balanced]
enchant_sockets: 1
# note: Archetypal starting weapon: mediocre but reliable, and the tuning baseline for every other weapon.
# also: block_profile?: {mitigation, stability} (shields/parry only)
```

### 4.3 ArmorDefinition — `kind: item.armor` (extends ItemDefinition)

```yaml
# id: item.armor.leather_cap
category: armor
stack_max: 1
weight: 0.8
value_base: 25
rarity: common
durability_max: 60
slot: head                    # head|chest|hands|legs|feet|cloak|ring|amulet
armor_value: 6
resistances: { physical_blunt: 1 }     # damage_type -> flat or percent
movement_penalty: 0.0
stealth_penalty: 0.0
material_class: leather       # cloth|leather|mail|plate|chitin|exotic
# no armour mastery axis: armour use is gated by attribute minima (PROGRESSION.md §11.1); M3b reconciles armour
# note: Cheap first upgrade: the first craft teaches the crafting loop without a resource wall.
# also: set_ref? | layering_rules? (which slot groups may combine)
```

### 4.4 CreatureDefinition — `kind: creature`

```yaml
# id: creature.undead.barrow_wight
family: undead
archetype: undead              # predator|prey|scavenger|humanoid|undead|construct|spirit|apex
level_band: [22, 26]           # AUTHORED band; never scaled to the player (charter §1)
pools: { health: 320, focus: 80 }   # Health/Stamina/Focus; there is no mana (PROGRESSION.md §4.1)
attributes: { might: 16, endurance: 18, agility: 7, precision: 6, will: 20, insight: 6, presence: 12 }  # the canonical seven (PROGRESSION.md §4.1)
attack_set: [ability.creature.grave_touch, ability.creature.wail_of_rot]
behavior_profile: ai.profile.relentless_undead          # S-23
perception: { sight_m: 16, hearing_m: 22, darkvision: true }
habitat: { biomes: [barrow, crypt], time_of_day: [any], spawn_weight: 0.35 }
loot_table: loot.undead.barrow_wight.rare
faction_ref: faction.undead.barrow_host
resistances: { physical_slash: 0.25, necrotic: 0.5, holy: -0.5 }
tier_hint: C                   # preferred simulation tier when unobserved (D-06)
tameable: false                # deferred feature gate; false everywhere in Phase 1-2
# note: Resists mundane steel on purpose; the intended answers are the silver line or holy magic, not more levels.
# also: abstract_profile? (tier C/D behaviour template)
```

A generic pack predator (`creature.beast.wolf_grey`, `level_band: [3, 6]`, with `creature.beast.wolf_grey_alpha` as the named variant) is the second authored case; loot table plus level band make it the early "you are not ready" lesson rather than an ambush.

**As implemented (M3c).** The prototype's wolf is level 2 (`PROTOTYPE.md` §4.1), authored as `level_band: [2, 2]`: a creature has one level until the spawner rolls within a band (M3d). Combat reads four more fields: `armor: {head, torso, limbs}` (coverage by region), `move_speed_m_s`, `body_radius_m`, and `xp_value`, the kill XP before AG-1..AG-3. Its attack is the first `attack_set` ability, which is `class: creature` (§4.7). Health stays a same-level body's: a creature is made harder by what it does, never by a larger pool (`VERTICAL_SLICE.md` §5.1).

**As implemented (M3d).** `perception: { sight_m, hearing_m, fov_deg }` builds the creature's senses (`fov_deg` defaults to 120). `turn_deg_s` is how fast its body turns (default 720); `weak_point: { region, from_behind }` forces a blow from behind onto that region (the armour's open helm); `tags` are what effects test their `immunity_tags` against (`undead`, `construct`). `attack_set` gives its blow and, optionally, a charge (§4.7). `loot_table` is what its corpse holds. `behavior_profile` is not used: behaviour is a spawner-assigned role (`config.creature_behaviour`, §4.19). The five Phase-1 archetypes are the content bible's; `M3D_BEHAVIOUR_MATRIX.md` is generated from their definitions.

### 4.5 NPCDefinition — `kind: npc`

```yaml
# id: npc.settlement.miller_hale
species_ref: species.human
role: craftsperson            # villager|merchant|guard|craftsperson|quest_giver|trainer|innkeeper|noble|bandit|scholar|steward
services: [trade, craft_station]        # trade|repair|train|craft_station|rest|stable|bank
dialogue_ref: dialogue.settlement.miller_hale.intro
faction_ref: faction.settlement.stoneford_covenant
schedule_ref: schedule.craftsperson.mill
anchors: { home: anchor.stoneford.mill_house, work: anchor.stoneford.mill, social: anchor.stoneford.tavern, sleep: anchor.stoneford.mill_house }
combat_profile: ai.profile.civilian_flee   # absent = non-combatant
unique: true                  # persistent identity, permanent death
relationships_init:
  npc.settlement.warden_sera: { trust: 0.4, respect: 0.2 }
# note: First craft_station the player meets and the smithing tutorial gate; his dialogue plants the silver-vein rumour.
# also: merchant_profile? (required when `trade` is present) | loot_table? | name_pool? (generic archetypes only)
```

Generic hostiles (`npc.bandit.road_cutter`) set `unique: false` with a `name_pool`; they carry no memory and persist only a death flag.

### 4.6 SpellDefinition — `kind: spell`

```yaml
# id: spell.force.ember_bolt
domain: skill.force           # the magic-domain skill (PROGRESSION.md §7)
complexity: 12                # against domain skill: Strain, stability, cast speed, and skill XP via the difficulty gate
cost: { focus: 6, strain: 8 } # no mana; contextual costs (reagents, charges) are listed below
cast_time_s: 0.9
cooldown_s: 1.5
range_m: 24
targeting: projectile         # self|touch|projectile|aoe_ground|aoe_cone|beam|summon|ritual
payload:
  - { type: damage, damage_type: fire, amount: [14, 20], scaling: { insight: 0.9, skill.force: 0.4 } }
  - { type: apply_effect, effect_ref: effect.burning.minor, chance: 0.25, duration_min: 0.5 }
resist_type: fire
interrupt_priority: 2
# note: The teaching formula: fast, cheap, and weak enough that it never replaces weapon play at low skill.
# also: channel: bool | duration_min | required_reagents?: [item.*] (consumed via S-14) | charges? | persist_cooldown (default false)
# A formula is known through a learning event (PROGRESSION.md §4.4). Casting above one's skill is allowed, at higher Strain and failure risk.
```

**Closed payload vocabulary** — the only legal `payload[].type` values (shared by spells, abilities, and status-effect triggers): `damage`, `heal`, `restore_pool`, `apply_effect`, `remove_effect`, `dispel`, `summon`, `teleport`, `reveal`, `create_item`, `modify_stat`, `taunt`, `absorb`, `reflect`, `resurrect_temporary`, `harvest_corpse`. Schools differ mechanically through this vocabulary plus definition data — never through new engine code per school.

A second authored case, `spell.necromancy.bind_lesser_servant` (`cost: { focus: 10, strain: 18 }` plus an essence charge harvested from corpses, `targeting: summon`, payloads `harvest_corpse` + `summon … permanent: true, cap: 2`, `required_reagents: [item.material.corrupted_marrow]`), shows that a domain's identity lives in its contextual costs, targeting, and payload shape.

### 4.7 AbilityDefinition — `kind: ability` (a technique)

`ability` is the **technique** kind (`PROGRESSION.md` §4.4). There is no tree, no tier and no point cost: a technique is known only through a learning event (starting package, teacher, book, quest, study, experiment, discovery, artifact, culture). Its prerequisites are skills and other known techniques, never level.

```yaml
# id: ability.martial.power_strike
class: active                 # active|passive|reaction|moveset|ritual|creature
discipline: skill.one_hand_blade   # the skill that performs it and earns its use XP
prerequisites: [ability.martial.weapon_focus]   # known techniques
requirements: { skill.one_hand_blade: 20, attribute.might: 10 }   # to learn it; never level
learn_from: [teacher, book, discovery]   # typed learning sources
cost: { stamina: 22 }
cooldown_s: 6
range_m: 2.0
targeting: aoe_cone
payload:
  - { type: damage, damage_type: physical_slash, multiplier_of_weapon: 2.1, scaling: { might: 1.1 } }
  - { type: apply_effect, effect_ref: effect.staggered, duration_min: 0.05, on: hit }
animation_key: anim.attack.sword_heavy          # presentation binding only
# note: The stagger is the point, not the damage: it is the answer to being rushed by a pack.
# passives instead carry modifiers: [{target, op: add|add_pct|multiply|set, value, condition?}]
```

`ability.martial.weapon_focus` (`class: passive`, `modifiers: [{target: stat.stamina_regen, op: multiply, value: 1.15}]`) is in the default starting package: every martial character knows it from creation.

**Creature attacks (M3c).** A `class: creature` ability gives `range_m`, `windup_s`, `active_s` and `recovery_s` - the timing combat runs on; an animation clip is scaled so its `hit_window_start` lands on the windup's end, never the reverse - and a `payload` with one `damage` entry (`amount: [min, max]`, `damage_type`) and optionally `apply_effect` with a `chance`. The wolf's bite is `ability.creature.wolf_bite`.

**Creature attacks (M3d).** `lunge_m` carries the attacker forward through the active window. `advance: true` keeps it running at its target through the windup (the hound). `cooldown_s` spaces its uses. `forces_stagger: true` knocks the target down unless guarded or dodged. `charge: { speed_m_s, min_range_m, max_distance_m, stun_s }` makes the ability a charge: after the windup the attacker runs straight at that speed, committed, until it meets the target, has run its distance, or hits something solid, which stuns it for `stun_s` (the boar).

### 4.8 StatusEffectDefinition — `kind: effect`

```yaml
# id: effect.bleeding
category: dot                  # buff|debuff|dot|hot|control|injury|curse|environmental
stack_policy: stack_intensity  # refresh|stack_intensity|stack_duration|independent
max_stacks: 5
duration_min: 1.0              # 0 = permanent until removed
tick_interval_min: 0.1
on_tick:
  - { type: damage, damage_type: physical_pierce, amount: [2, 4], per_stack: true, ignore_armor_pct: 0.5 }
modifiers:
  - { target: stat.stamina_regen, op: multiply, value: 0.8, per_stack: true }
dispel_category: injury        # none|magic|poison|disease|curse|injury
immunity_tags: [construct, incorporeal]
# note: Value is pressure, not lethality: it punishes standing still and rewards bandaging mid-fight.
# also: on_apply/on_expire/on_remove?: [closed payload vocabulary] | break_on?: damage|movement|action
#       persist (default true; false for sub-second states such as effect.staggered)
```

**As implemented (M3c).** `stack_policy` builds `refresh` and `stack_intensity`; `duration_min` and `tick_interval_min` are game minutes (`config.time`), so the wolf's bleeding is `3.0` (6 s) ticking every `0.5` (1 s). `on_tick` builds `damage` (a fixed `amount` per stack, past armor) and `heal`; `modifiers` build `stat.damage_dealt` and `stat.stamina_regen` (`multiply`) and `stat.armor` (`add`). A player's effects are saved with absolute world-tick deadlines (`PERSISTENCE.md` §5.1, schema 7). Anything else is refused by the lint (`CMB001`) until the content that needs it arrives. From M3d `immunity_tags` is live: an effect is refused on a creature carrying any of the tags (bleeding on `undead` and `construct`).

### 4.9 RecipeDefinition — `kind: recipe`

```yaml
# id: recipe.smithing.silver_sword
profession: profession.smithing
required_skill: 25
station_ref: station.forge    # absent = field-craftable
inputs:
  - { item_ref: item.material.silver_ingot, count: 4, consumed: true, quality_min: 0.5 }
  - { item_ref: item.material.oak_shaft, count: 1, consumed: true }
  - { item_ref: item.material.moonwater_flux, count: 1, consumed: true }
tools: [item.tool.smiths_hammer]        # required, not consumed
outputs: [{ item_ref: item.weapon.silver_sword, count: 1, quality_roll: true }]
craft_time_min: 90
xp_award: { xp: 120, profession_xp: 200 }
quality_curve: { min_skill: 25, max_skill: 60, bonus_chance_by_skill: 0.015 }
discovery: { experiment: true, reveal_tags: [silver, anti_undead] }
learned_from: [item.book.treatise_on_silver_working]
repeatable: true
# note: The intended answer to barrow wights. Gated on a rare input and a book, so it is an achievement rather than a purchase.
```

A refining recipe (`recipe.smithing.iron_ingot`: `station_ref: station.smelter`, two ore + one charcoal ⇒ one ingot, `craft_time_min: 6`) is the cheap shared first step of the smithing line.

### 4.10 ResourceDefinition — `kind: resource`

```yaml
# id: resource.ore.iron_vein
yields:
  - { item_ref: item.material.iron_ore, count_range: [1, 3], quality_roll: true, chance: 1.0 }
  - { item_ref: item.material.rough_gemstone, count_range: [1, 1], quality_roll: false, chance: 0.04 }
gathering_skill: skill.mining
skill_requirement: 1
tool_ref: item.tool.mining_pick
node_kind: ore_vein           # ore_vein|tree|herb|hide_source|fishing_spot|essence_well|salvage
respawn_min: 480
placement: { biomes: [hills, mountains, cave], density_per_cell: 1.5, cluster: true, min_distance_from_settlement_m: 40 }
harvest_time_s: 2.5
depletes: true                # false = unlimited (e.g. a river)
# note: Placed near the starting settlement on purpose: the first crafting loop must be reachable in ten minutes.
# also: availability?: {time_of_day, moon_phase, weather}
```

### 4.11 QuestDefinition — `kind: quest`

A quest is a **graph of objective nodes**. Each node is a declarative predicate over world state with a `type` from a **closed set**; the engine evaluates predicates and contains no per-quest logic (D-07). This is not a visual scripting language.

**Quest-level fields:** `title_key`; `summary_key`; `journal_entries` (node ID → text key); `level_band`; `giver_ref`; `faction_ref`; `parent_quest_ref?`; `chain_index`; `entry_objective`; `objectives`; `fail_if` (predicate list, evaluated continuously); `abandon_policy` (`allowed|allowed_with_penalty|locked`); `repeat_policy` (`{repeatable, cooldown_min}`); `rewards`; `tags`. **Node fields:** `id`; `type`; `description_key`; `params`; `hidden`; `optional`; `all_of`/`any_of`/`not` (composition); `next` (branching); `on_fail` (node ID or `fail_quest`); `time_limit_min?`; `timer_anchor` (`node_activated|quest_accepted|world_hour`); `world_time_required?` (`{time_of_day, weather, moon_phase}`); `requirements?` (gates node activation); `visibility` (`always|when_active|when_satisfied|hidden`).

**Closed objective type set** (no other `type` is legal; params in parentheses):

| Type (`params`) | Satisfied when |
|---|---|
| `explore_location` (`location_ref`, `within_m`) / `visit_location` (`location_ref`) | player inside the radius / trigger entered |
| `discover_secret` (`secret_ref`) | secret or hidden-objective flag discovered |
| `talk_to` (`npc_ref`, `dialogue_ref?`, `node_ref?`) / `dialogue_choice` (`npc_ref`, `choice_id`) | dialogue node completed / a specific choice was taken |
| `acquire_item` (`item_ref`, `count`, `consume?`) / `deliver_item` (`npc_ref`, `item_ref`, `count`) | inventory holds or hands over / transferred via S-14 |
| `craft_item` (`item_ref`, `count`, `quality_min?`) / `use_recipe` (`recipe_ref`) | craft completed (S-17) / recipe executed |
| `harvest_resource` (`resource_ref`, `count`, `mode?`) | harvest events accumulated, or inventory held |
| `construct_building` (`piece_ref` or `tag`, `count`, `at?`) | matching building instances placed |
| `upgrade_settlement` (`settlement_ref`, `metric`, `threshold`) | settlement aggregate reaches the threshold |
| `kill_creature` (`creature_ref` or `tag`, `count`, `where?`) | kill events accumulated |
| `kill_named` (`creature_ref` unique, `count`) / `defeat_boss` (`boss_ref`, `count`) | unique spawn / boss defeat record exists |
| `survive_encounter` (`encounter_ref`, `duration_min`) | survived without dying |
| `escort_npc` (`npc_ref`, `destination_ref`, `require_alive`) | NPC reached the destination |
| `defend_location` (`location_ref`, `waves`, `window_min`) | all waves repelled inside the window |
| `solve_puzzle` (`puzzle_ref`, `state?`) | puzzle instance reaches the state |
| `faction_reputation` (`faction_ref`, `min`/`max`, `tier?`) / `faction_state` (`faction_ref`, `state`, `value`) | reputation predicate (S-27) / faction-state flag matches |
| `relationship_value` (`npc_ref`, `dimension`, `min`/`max`) / `companion_present` (`npc_ref`, `state?`) | relationship predicate (S-26) / roster matches |
| `know_fact` (`fact_ref`) / `world_state` (`flag_ref`, `value`) | fact known (S-30) / world flag matches |
| `time_window` (`start_hour`, `end_hour`, `days?`) / `wait_until` (`at_time`/`after_min`) | clock predicates, composable as guards |

Failure, branching, and timers are expressed with `on_fail`, `next`/`all_of` branch sets, and `time_limit_min` + `timer_anchor` — no new mechanism is required. **`closed type set` is the whole vocabulary: `custom_scripted` does not exist**, and is named here only to document its deliberate absence.

**Closed reward kinds:** `xp`, `currency`, `item`, `spell`, `ability`, `recipe`, `reputation`, `relationship`, `title`, `access` (unlock location/service), `companion`, `property`, `world_flag`, `permanent_ability`, `transformation`. Rewards are granted by S-29 via commands (`Transfer`, `AddReputation`, `AwardXp`), never written directly.

```yaml
# Node fields: id | type | description_key | params | hidden | optional | all_of/any_of/not | next
#   (branching) | on_fail (node ID or fail_quest) | time_limit_min? | timer_anchor | requirements? | visibility
id: quest.artifact.shattered_crown.03
title_key: quest.artifact.shattered_crown.03.title
parent_quest_ref: quest.artifact.shattered_crown.01
chain_index: 3
level_band: [18, 30]
giver_ref: npc.scholar.archivist_veyla
faction_ref: faction.scholar.conclave_of_ash
entry_objective: o_research
abandon_policy: allowed
repeat_policy: { repeatable: false }
fail_if:
  - { type: relationship_value, npc_ref: npc.scholar.archivist_veyla, dimension: trust, max: -0.5 }
rewards:
  - { kind: xp, amount: 2400 }
  - { kind: item, item_ref: item.quest.crown_fragment_upper, count: 1 }
  - { kind: recipe, recipe_ref: recipe.smithing.silver_sword }
  - { kind: reputation, faction_ref: faction.scholar.conclave_of_ash, amount: 15 }
  - { kind: world_flag, flag_ref: world.shattered_crown.fragment_upper_recovered, value: true }
objectives:
  - { id: o_research, type: dialogue_choice, description_key: ...o1, next: [o_find_barrow],
      params: { npc_ref: npc.scholar.archivist_veyla, choice_id: ask_about_third_fragment } }   # dialogue gate
  - { id: o_find_barrow, type: explore_location, description_key: ...o2, next: [o_clear_wight],
      params: { location_ref: loc.barrow.mourning_hollow, within_m: 40 },
      world_time_required: { time_of_day: [night] } }                                            # night-gated
  - { id: o_clear_wight, type: kill_named, description_key: ...o3, next: [o_recover_fragment],
      params: { creature_ref: creature.undead.barrow_wight, count: 1 },
      time_limit_min: 30, timer_anchor: node_activated, on_fail: o_retreat }                     # timed combat
  - { id: o_retreat, type: wait_until, description_key: ...o3b, next: [o_clear_wight],
      params: { after_min: 1 } }                                                                 # failure is a setback
  - { id: o_recover_fragment, type: acquire_item, description_key: ...o4, next: [o_forge_vessel],
      params: { item_ref: item.quest.crown_fragment_upper, count: 1 } }
  - { id: o_forge_vessel, type: craft_item, description_key: ...o5, next: [o_choose_path],
      params: { item_ref: item.material.silver_vessel, count: 1, quality_min: 0.6 } }             # quality floor
  - { id: o_choose_path, type: dialogue_choice, description_key: ...o6, all_of: [o_forge_vessel],
      params: { npc_ref: npc.scholar.archivist_veyla, choice_id: bind_fragment_to_crown },
      next: [o_report_bind, o_report_destroy] }                                                  # branch point
  - { id: o_report_bind, type: talk_to, description_key: ...o7a, next: [o_complete],
      params: { npc_ref: npc.scholar.archivist_veyla } }
  - { id: o_complete, type: world_state, description_key: ...o8,                                  # terminal
      params: { flag_ref: world.shattered_crown.chapter3_resolved, value: true } }
# Hidden/secret objectives use the same schema with `visibility: hidden`; a hidden objective is
# revealed only when its predicate becomes true. A small quest is the same schema with four nodes.
```

### 4.12 DialogueDefinition — `kind: dialogue`

```yaml
# participants: [npc_ref] | root_node | nodes: {node_id: node}. Node: text_key (tokens from a closed
#   set, e.g. {player_name}) | conditions | choices: [{id, text_key, conditions, consequences, next}]
#   | next? (auto-advance) | once (visited flag persists, S-28) | next_if_exhausted?
# Closed condition kinds: quest_state | reputation | relationship | skill | has_item | knows_fact |
#   world_state | time_of_day | companion_present | faction_state | level | visited
# Closed consequence commands: start_quest | advance_quest | complete_objective | fail_objective |
#   add_reputation | record_relationship_event | transfer_item | award_xp | give_recipe | set_world_flag |
#   know_fact | open_service | set_price_modifier | start_combat | relocate_npc | unlock_travel_node | play_scene
id: dialogue.settlement.miller_hale.intro
participants: [npc.settlement.miller_hale]
root_node: greet
nodes:
  greet:
    text_key: dialogue.settlement.miller_hale.greet
    choices:
      - id: ask_work                    # open hook: anyone may start the quest
        text_key: dialogue.settlement.miller_hale.choice.work
        conditions: [{ kind: quest_state, quest_ref: quest.settlement.stoneford_missing_flour, state: not_started }]
        consequences: [{ command: start_quest, quest_ref: quest.settlement.stoneford_missing_flour }]
        next: flour_trouble
      - id: ask_rumour                  # trust-gated: relationship investment pays off
        text_key: dialogue.settlement.miller_hale.choice.rumour
        conditions: [{ kind: relationship, npc_ref: npc.settlement.miller_hale, dimension: trust, min: 0.1 }]
        consequences: [{ command: know_fact, fact_ref: fact.rumour.silver_vein_north_road }]
        next: null
  flour_trouble:
    text_key: dialogue.settlement.miller_hale.flour_trouble
    choices:
      - { id: accept, text_key: dialogue.common.choice.accept, next: null,
          consequences: [{ command: advance_quest, quest_ref: quest.settlement.stoneford_missing_flour, objective: o_ask }] }
# `once: true` + `next_if_exhausted` mark one-time nodes; dialogue emits commands, never mutates state.
```

### 4.13 FactionDefinition — `kind: faction`

```yaml
# attitude_default: {faction_ref: value in [-1,1]} | members: [npc.*] | player_start_reputation
# reputation_tiers: [{tier, min, services: [enum], dialogue_flags: [key]}] | territory: [region|cell]
# laws: [{offense, response, bounty_base}] | services_gated: [enum] | joinable
# join_requirements?: (predicate object) | enemy_of: [faction.*] (hard hostility edges)
id: faction.settlement.stoneford_covenant
attitude_default: { faction.wildlife: 0.0, faction.bandit.ash_road_crew: -1.0, faction.scholar.conclave_of_ash: 0.3, faction.undead.barrow_host: -1.0 }
members: [npc.settlement.miller_hale, npc.settlement.warden_sera]
player_start_reputation: 0
reputation_tiers:
  - { tier: outsider, min: -100, services: [trade] }
  - { tier: tolerated, min: 0, services: [trade, rest] }
  - { tier: trusted, min: 150, services: [trade, rest, craft_station, stable], dialogue_flags: [access_mill_ledger] }
  - { tier: sworn, min: 400, services: [trade, rest, craft_station, stable, bank], dialogue_flags: [access_council] }
territory: [region.stoneford_vale, cell.stoneford_town]
laws:
  - { offense: assault, response: guards_hostile, bounty_base: 25 }
  - { offense: murder, response: guards_lethal_bounty, bounty_base: 200 }
services_gated: [craft_station, stable, bank]
joinable: true
join_requirements: { reputation: { faction_ref: faction.settlement.stoneford_covenant, min: 150 }, quest_ref: quest.settlement.stoneford_missing_flour }
# note: Starting faction; its tiers are the tutorial for 'reputation buys access, not power' (D-09).
```

### 4.14 LootTableDefinition — `kind: loot`

```yaml
# rolls (independent draws, default 1) | entries: [{item_ref or loot_ref, weight, count_range, chance,
#   quality_roll, conditions}] | guaranteed: [entry, always granted when the table resolves]
# currency?: {range, chance} | once_per_source (source records a `looted` flag, S-16)
# conditions (table-level gates): level_band | faction_state | world_flag | time_of_day | luck_stat
# Nested loot_ref is legal; self-recursion (direct or transitive) is a validation error.
id: loot.beast.wolf_grey.common
rolls: 2
currency: { range: [0, 4], chance: 0.5 }
entries:
  - { item_ref: item.material.wolf_hide, weight: 60, count_range: [1, 1], quality_roll: true }
  - { item_ref: item.material.argent_fang, weight: 2, count_range: [1, 1], conditions: [{ level_band: [5, 6] }, { world_flag: world.moon.full, value: true }] }
  - { loot_ref: loot.generic.humanoid_junk, weight: 5, conditions: [{ world_flag: world.region.scavengers_bold, value: true }] }
once_per_source: true
# note: A single loot condition (full moon) creates the behaviour: players hunt wolves at night for the argent fang.
```

An elite table (`loot.undead.barrow_wight.rare`) adds `guaranteed:` with a **quest-state-gated** quest item, so the artifact fragment cannot be farmed before the chain reaches it.

**As implemented (M3b).** An entry with `weight` joins the weighted draw made `rolls` times; an entry with `chance` (in (0, 1]) rolls on its own, which is how a wolf drops meat 70%, hide 45% and fang 15% independently; `guaranteed` entries always resolve. A table is rolled from a source keyed by `(seed, cell, "loot", source key)`, so the same source always gives the same result. Conditions and currency ranges arrive with the content that needs them.

### 4.15 MerchantProfile — `kind: merchant` (referenced by `npc.*`)

```yaml
# stock_mode: fixed|tag_filtered|faction_supply | stock: [{item_ref, count, restock_min, price_bias}]
# buys_tags | buys_rarity_max | gold_reserve: [min,max] at restock | restock_min
# price_bias: {tag: multiplier} (the local specialisation) | trade_skill_effect | repair_service
id: merchant.stoneford.mill_supply
stock_mode: tag_filtered
stock:
  - { item_ref: item.consumable.field_bandage, count: 8, restock_min: 1440, price_bias: 0.9 }
  - { item_ref: item.material.iron_ingot, count: 4, restock_min: 2880, price_bias: 1.2 }
buys_tags: [material, consumable, tool]
buys_rarity_max: uncommon
gold_reserve: [80, 140]
restock_min: 1440
price_bias: { material: 1.1, weapon: 0.85 }
trade_skill_effect: 0.02
repair_service: false
# note: Rural pricing: sells metal dear, buys weapons cheap — deliberately worse than the city so travel has economic value.
```

Phase 1 (M3b) matches `buys_tags` against the item's `category`; the tag vocabulary arrives later. A merchant buys at `config.economy`'s `sell_ratio` of `value_base` and sells at `value_base × price_bias`.

### 4.16 NodeDefinition — `kind: node`

The harvestable *placement* of a material. Distinct from `ResourceDefinition` (§4.10), which defines the material itself. See the note in §1.

```yaml
# resource_ref: resource.*        | respawn: none|timer|daily  (finite nodes never respawn)
# yield: [min,max]                | tool_tier_min?              | charges (total harvests before depletion)
# placement: how the node is scattered by the deterministic baseline generator
id: node.herb.ashbloom
resource_ref: resource.herb.ashbloom
charges: 1                        # one harvest, then depleted
respawn: daily                    # refills at the next world-day boundary (PROTOTYPE C12)
tool_tier_min: 0                  # bare hands suffice
placement: { biomes: [meadow, forest_edge], density: 0.4, cluster: [1, 3] }
harvest_skill: skill.survival     # S-19 read; yield bonus from AX-SKL
# note: respawn is computed as a pure function of (world_time, node key) and NOT stored per node
# unless the node has diverged from baseline — see PERSISTENCE.md §5.5.
```

```yaml
# A finite node: the second respawn class the prototype must prove.
id: node.ore.iron_seam
resource_ref: resource.ore.iron
charges: 3
respawn: none                     # never refills; depletion is a permanent world delta
tool_tier_min: 1
placement: { biomes: [rocky_slope], density: 0.15, cluster: [1, 1] }
```

### 4.17 SpawnDefinition — `kind: spawn`

A spawner: *what* appears, *where*, *how many*, and *how often*. Owned by S-31.

```yaml
# creature_ref(s) + counts | cell or region scope | level_band (AUTHORED, never player-scaled)
# respawn window | population caps | rare/named flags | tier hint
id: spawn.valley.wolf_pack
creatures: [{ creature_ref: creature.beast.wolf_grey, count: [3, 4] }]
scope: { biome: valley_floor, near: { location_ref: location.den_mouth, radius_m: 80 } }
level_band: [2, 5]                # authored; the player's level is NEVER an input (charter §1)
respawn: { kind: timer, window_ticks: 24000, jitter: 0.25 }
population: { per_cell_max: 4, region_max: 12 }
tier_hint: B                      # preferred simulation tier when unobserved (D-06)
# note: respawn windows are content, and content that feeds a persisted derived value must be
# treated as baseline-locked — see PERSISTENCE.md §6.1 and RK-11.
```

**As implemented (M3c).** A spawner names its `region_ref`, a place (`at: { position_m: [x, z], radius_m }`) and `creatures: [{ creature_ref, count: [n, n] }]`; a fixed count is placed when a world starts, each creature where it fits, from rolls keyed by its spawner and index. `respawn`, population state and its persistence are M3d's.

**As implemented (M3d).** Each `creatures` entry may give a `role` (a key of `config.creature_behaviour`'s `roles`); `route_m: [[x, z], ...]` is the route a patrolling role walks. `respawn: { kind: none }` or `{ kind: timer, window_ticks }`: a timer brings a dead member back once the window has passed and the player is past the leash from home, doubled while the cluster is AG-3-saturated. A member that differs from its baseline is saved as a creature record (`PERSISTENCE.md` §5.3, schema 8).

### 4.18 LocationDefinition — `kind: location`

A named discoverable place. Feeds S-30 discovery, fast travel, and quest predicates.

```yaml
# display name | region/cell anchor | discovery method(s) | map behaviour | fast-travel node?
id: location.den_mouth
name: The Den Mouth
region_ref: region.r_0_0
anchor: { world: [118.0, 0.0, 74.0] }        # exact placement; not procedural
kind_of_place: cave_entrance                  # settlement|landmark|cave_entrance|ruin|shrine|...
discovery: { methods: [sighted, visited], radius_m: 45, xp_source: discovery }
map: { reveals_radius_m: 60, marker: none }   # marker: none — no quest diamonds (charter §3)
fast_travel: { node: false }                  # earned later; see S-38
```

### 4.19 ConfigDefinition — `kind: config`

Game-wide tuning constants. One file per group. **This is where numbers live that other documents deliberately left as data constants** — including the game-time units the whole design depends on (see §6 and `WORLD_ARCHITECTURE.md` §6).

```yaml
id: config.time
# Game-time units. These are DEFINED HERE and nowhere else; every duration in every other
# document is expressed in them. RK-12's catch-up test and PROTOTYPE C12 both depend on them.
ticks_per_second: 20              # the fixed domain tick (SYSTEMS.md §1)
seconds_per_game_minute: 2.0      # 1 game minute = 2 real seconds
game_minutes_per_hour: 60
game_hours_per_day: 24
# => one game day = 1440 game minutes = 2880 real seconds = 48 real minutes at 1x (57600 ticks).
# A player who plays a 40-minute prototype session therefore experiences ~20 game hours,
# which is why PROTOTYPE C12's "refill after one in-game day" needs the rest/wait
# fast-forward in config.rest below rather than 48 real minutes of waiting.
```

```yaml
id: config.rest
wait_allowed: true                # rest/wait fast-forwards world_time (S-04)
wait_increments_game_hours: [1, 6, 24]
wait_requires_safe_location: true # cannot fast-forward while in combat or in a hostile cell
wait_max_hours_per_rest: 24
```

```yaml
id: config.xp_curve
# The curve is data, and the published total is DERIVED from it. A unit test sums this
# and asserts the total recorded in PROGRESSION.md §3.2 — that test exists because an
# earlier draft shipped a stated total 28.8% below what the formula actually summed to.
base: 100
exponent: 1.85
round_to: 25
band_multiplier: { levels: [11, 30], factor: 1.15 }
total_to_level_50_expected: 2448025
```

```yaml
id: config.level_cap
soft_cap: 50                      # PROGRESSION.md §3.2; designated masteries continue past it (§8)
level_cap_phase1: 5               # PROTOTYPE.md; a prototype artifact, not the game's cap
```



`config.creature_behaviour` (M3d) holds how creatures perceive and behave: `awareness` (the suspicious level, sight gain at range and close, decay, what a heard noise and a call set, the search time), `noise_m` (how far a walk, run, sprint, swing, blow and call carry), `corpse` (`decay_s`, `stack_slots`), and the `roles` - each an `unaware` behaviour (`hold`, `wander`, `patrol`, `sleep`) with optional `territory_m`, `wander_m`, `calls_for_help`, `answers_calls`, `keep_distance_m`, `strike_within_m`, `flee_below_percent`, `sleep_hearing_percent`, `flank_m` and `pounce_on_noise`.

`config.damage_constants` (M3c) holds the combat tuning: armor `k`, the share of armor a pierce ignores, criticals, region weights and multipliers, stagger threshold and immunity, the guard, the dodge, stamina costs and regeneration, health regeneration out of combat, the split of a swing into windup, active window and recovery, ranged range, bare hands, a creature's leash, and the effect a death applies.

### 4.20 AffixDefinition — `kind: affix` (supports §4.2's `affix_pool`)

```yaml
# applies_to: {kind, tags} filter | tier | weight (drop weight within the pool) | requires_quality_min?
# modifiers (same shape as effects). An item instance stores the ROLLED affix IDs AND their ROLLED
# magnitudes, because magnitudes are not regenerable; roll seed = (world_seed, item_ulid, "affix").
id: affix.weapon.keen
applies_to: { kind: item.weapon, tags: [blade] }
tier: 1
weight: 100
modifiers:
  - { target: stat.crit_chance, op: add, value: 0.04 }
# note: Cheap, common, legible: the first affix a player ever sees should be understandable in one line.
```

### 4.21 SkillDefinition — `kind: skill`

A discipline of `AX-SKL` (`PROGRESSION.md` §4.2): weapon families, magic domains, crafting, gathering, world and social skills are all this one kind. Skill IDs are flat (`skill.<name>`, file `content/skills/<name>.yaml`) so a discipline never changes ID when families are reorganised.

```yaml
id: skill.one_hand_blade
family: combat                # combat|magic|crafting|gathering|world|social
display_key: skill.one_hand_blade.name
# also: use_xp_multiplier? (default 1.0; per-discipline pacing) | passives?: [{at, modifiers}] (from M3c: the blade's
#       stagger at 3; M3f adds athletics and survival)
# note: Competence only. What the character can attempt at all is AX-TEC (abilities, spells, recipes).
```

The progression constants live in one config group, alongside `config.time`, `config.xp_curve` and `config.level_cap` (§4.19):

```yaml
id: config.progression
attribute_base: 10                # every attribute starts here; allocation adds to it
attribute_points_per_level: 1     # the only thing a level-up grants (PROGRESSION.md §3.1)
skill_difficulty_margin: 15       # use grants skill XP only when difficulty > skill - 15
skill_common_ceiling: 60          # designated masteries (M12) continue to 100
xp_debt_fraction: 0.10            # AG-8, of the current level's XP span
# also: derived-pool coefficients (Health/Stamina/Focus maxima, Resonance, Strain tolerance) | the AG-1..AG-3
#       tables | skill XP curve | novelty bonus | the default starting package (attributes, skills, techniques)
```

---

## 5. Cross-references and validation

**Expressing references.** References are plain definition-ID strings in fields named `*_ref` (single) or `*_refs` / a list of `{item_ref: …}` (many). Never a file path, relative name, array index, or GUID. A reference must resolve to the declared target **kind**: a `spell.*` ID in a `creature_ref` field is an error even though the ID exists. Forward references across files are legal (hence the two-pass load). Definition IDs may **not** be built by runtime string concatenation — if a variant is needed, it is authored and referenced.

| Field pattern | Target kind | Field pattern | Target kind |
|---|---|---|---|
| `item_ref` | `item`, `item.weapon`, `item.armor` | `quest_ref` | `quest` |
| `creature_ref` | `creature` | `dialogue_ref` | `dialogue` |
| `npc_ref` | `npc` | `faction_ref` | `faction` |
| `spell_ref` / `ability_ref` | `spell` / `ability` | `loot_ref` | `loot` |
| `effect_ref` / `affix_ref` | `effect` / `affix` | `location_ref` | `location` |
| `recipe_ref` | `recipe` | `fact_ref` | `fact` (declared in `content/facts/`) |
| `resource_ref` | `resource` | `flag_ref` | `world_flag` (ID prefix `world.`, declared in `content/world_flags/`) |
| `species_ref` | `species` (declared in `content/species/`) | `schedule_ref` | `schedule` (declared in `content/schedules/`) |
| `region_ref` | `region` (declared in `content/regions/`) | `anchor_ref` | `anchor` (declared in `content/anchors/`) |
| `set_ref` | `set` (declared in `content/sets/`) | `merchant_ref` | `merchant` (declared in `content/merchants/`) |
| `skill_ref` | `skill` (§4.21; added when `skill` joined the kind table, M2c) | | |

**How the validator finds references (pre-M3b hardening).** It walks every definition to any depth, through maps and lists. A field whose name ends in one of the patterns above is a reference, so a role prefix works: `parent_quest_ref` is a quest. A `*_refs` field is a list of them. A `*_ref` field matching no pattern is an error, because its target kind is unknown: name it by the pattern or add a row here. Four §4 fields predate the convention and are treated as references by name: `loot_table` (loot), `attack_set` and `moveset` (ability), and `affix_pool` (affix). A reward or grant entry, `{ kind: <reward kind>, ref: <id> }` (§4.1 `use.grants`, §4.11), resolves `ref` against the reward kind's target, and a kind outside §4.11's closed list is an error. Content names current IDs: an ID that exists only as an alias is dangling, and the error names the current ID. Codes: `XREF002` wrong kind, `XREF003` dangling, `XREF004` unknown or malformed reference field, `XREF005` unknown reward kind. Each error carries file and line.

**Reserved mod namespace.** `<kind>.mod.<package_namespace>.*`, for every kind in §1 (`item.mod.*`, `item.weapon.mod.*`, `creature.mod.*`), belongs to mods (`MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md`). Core content may not use a `mod` segment directly after its kind prefix (`MOD001`). There is no mod loader.

**Phase-1 semantics.** The item, weapon, armor and creature schemas (§4.1-§4.4) are checked for their required fields, closed enums and ranges (`SEM001` missing, `SEM002` not in the closed set, `SEM003` out of range). Each later schema gains its checks in the milestone that first authors it.

**Validation pass (mandatory at startup and in CI).** Envelope completeness; ID grammar and file/stem/prefix agreement; duplicate IDs (hard error naming both files); kind-target match on every reference; dangling references (hard error with file and line, D-03); alias chains resolve in one hop with no cycles and no target in `removed`; tags exist in `_tags.yaml`; enum membership in closed sets; **closed-vocabulary compliance** for objective `type`, dialogue condition/consequence and spell/ability payload types (D-07); range sanity (`min <= max`, weights > 0); **quest reachability** (every objective reachable from `entry_objective`, every `next`/`on_fail` target exists, at least one terminal objective); reward-kind legality; NPC anchor and schedule integrity; loot-table recursion; and a warning when a category exceeds its phase content budget.

**Tooling (D-03):** `content-lint` (validate + report), `content-schema-dump` (JSON Schema per kind, for editors and AI sessions), `content-id-graph` (reference graph, so "what breaks if I rename this?" is answerable), `content-aliases` (append an alias/removal record).

---

## 6. Save-version sensitivity

Four independent axes — conflating them is the classic failure. `PERSISTENCE.md` §6.1 is the authority for the load-time behaviour column; this table states which axis each surface belongs to.

| Axis | Where | Meaning | Mismatch behaviour |
|---|---|---|---|
| Definition `schema:` | `schema:` in every definition | Shape of the one YAML/C# record | Loader rejects the file: a content bug, not a save bug |
| `content_version` / `content_hash` | save manifest + compiled cache | Which exact content pack wrote the save. **Not a generation input** (M2b) | Definition-ID pass; **tuning** changes additionally require migration (`PERSISTENCE.md` §5.5) |
| `worldgen_version` / `worldgen_fingerprint` / `rng_contract_version`, and per-cell `baseline_hash` | save manifest; `baseline_hash` with every changed cell | Which **generation contract and placement data** the baseline came from, and exactly which baseline each changed cell was made against | Per changed cell: equal hash applies the delta; a different hash needs a registered transition or **refuses**. Never apply a delta to a different baseline (`PERSISTENCE.md` §6.4) |
| `schema_version` | save manifest | Shape of **persisted** state | Migration chain, one version per step. A `save_format` mismatch refuses; a corrupt section is quarantined and loaded without (`PERSISTENCE.md` §7.2) |

**There is no field named `save_version`.** The persisted-state axis is `schema_version`; `save_format` is the *container* layout and is a different field with a different failure mode. An earlier revision of this document used `save_version` and `content_version` for the same concepts (`PERSISTENCE.md` §6.1 records the reconciliation).

**Save-version-sensitive surfaces** (bump `schema_version` on any change — see `PERSISTENCE.md` §6.1 for why it is `schema_version` and not `save_format` or `content_version`): quest instance state (objective progress, branch selections, absolute deadlines, accepted ledger); relationship values and the memory log (eviction policy changes which memories survive); faction reputation, tiers, crime records and bounties (threshold changes retroactively reclassify a player); world flags and their declared types; building instance records (piece refs, socket IDs, transforms, owner, health, assignments — regenerable from nothing, so corruption is unrecoverable); item instance records (definition ref, rolled affix IDs **and magnitudes**, durability, sockets, stack — magnitudes are not regenerable, so loss is a silent power change); resource node deltas (depletion, regrowth deadlines); NPC life-state, schedule phase, coarse position and disposition (deaths and relocations must never be silently dropped); discovered-location records and map reveal levels; dungeon/interior progress flags; merchant stock, reserve and transaction-decay counters (loss re-enables money exploits); weather state and its RNG stream position; fast-travel unlocks, links and player-created anchors; the alias/removal map; the registry's ULID anchor clock.

**Rules.**

1. **Additive changes with a default are migration-free** — add the field, document the default, keep the same `schema_version`.
2. **Structural changes require an ordered, pure, testable migration step** (rename, split, merge, type change, meaning change). Every shipped version needs a fixture save exercised by `Migrate(from, to)`.
3. **Deleting content is never a silent drop** (D-05): either map forward in `_aliases.yaml` or record the loss explicitly in the migration with a player-facing recovery action.
4. **Instances of a removed definition must be handled explicitly** — remap to the successor, or convert to a "relic" record preserving the player's item, its rolled properties, and a `legacy_definition` field. Never delete player property silently. (As implemented, M2b supports remap (`removed: old: new`) and an explicit, reported destroy (`removed: old: ~`). Relic conversion arrives with item instance records that carry rolled properties.)
5. **Baseline-locked surfaces** (generated cells, spawn placement, loot reproducibility) depend on the baseline tuple: the world seed and the generator contract, including its placement data. Changing generation code or `placement` data changes the baseline hash of every cell it affects. A save with changed cells there needs a registered transition (`PERSISTENCE.md` §6.4), or it refuses to load - never a silent regeneration (`RK-01`, D-05). A runtime-only content change (a creature's stats, an item's price) is not a generation input and moves no baseline.
6. **Integrity:** `PERSISTENCE.md` §3.2/§6.1 is the authority for the integrity root — it is `sections.sha256`, which covers every other file **including `manifest.json`**. The manifest deliberately carries **no** checksum of itself (a self-referential checksum is a trap), so the earlier phrasing in this document that implied one was wrong. Writes are atomic (staging + rename + verify) with two backup generations (`PERSISTENCE.md` §7.3), and a corrupt section is quarantined with an explicit statement of what was lost rather than a partial load being applied silently.

---

## Assumptions

1. **Species definitions** are `kind: species` in the closed kind table (§1) with a `SpeciesDefinition` (traits, attribute biases, racial abilities, homeland region ref, inter-species attitudes), stored in `content/species/`. Specified to the minimum depth `species_ref` requires; not populated, per the no-content-volume constraint. **Superseded:** an earlier revision recorded this as an assumption *outside* the fourteen required schemas, which made `species_ref` unresolvable by the validator that the same section mandates. It is now a table row.
2. **`set`, `schedule`, `region`, `anchor`, `fact`, and `world_flag` definitions** are likewise **kind-table rows**, not assumptions (§1 "Referenced kinds"). Each carries the minimum shape needed for references to validate; full specification belongs to `WORLD_ARCHITECTURE.md` (regions, anchors, schedules) and `PROGRESSION.md` (attribute biases). **Superseded:** an earlier revision listed these as "implied by cross-references" while the directory list admitted `regions/`, `schedules/`, `sets/`, `flags/`, and `facts/` — so the validator would have rejected every file it was told was legal.
3. **`kind: affix` is separate from `kind: effect`** even though both carry modifier sets, because affixes have `applies_to` and drop-weight semantics that effects must not inherit. Reusing `effect` was considered and rejected as an over-broad schema.
4. **Merchant data is its own `kind` referenced by `npc.*`** rather than an inline NPC field, so merchant files stay one-per-file and diffable. It is a kind-table row with a `merchants/` directory; it is not part of the `*_ref` kind namespace for top-level resolution.
5. **`custom_scripted` appears in §4.11 only to document its deliberate absence.** If a real quest cannot be expressed with the closed type set, that is the D-07 revisit trigger — not a reason to add an escape hatch.
6. **One definition per file, no YAML anchors, no multi-document files.** Chosen for per-file error reporting and to prevent hidden coupling between definitions.
7. **Content-version numbering is a monotonic integer** carried as a display string in the manifest (`PERSISTENCE.md` §6.1). This document requires only that it is a single value recorded in both the save manifest and the compiled content cache; the monotonic regime is fixed so load-time version *relationships* are decidable rather than a bounded-set rule over a free-form string.
8. **Recorded per instruction, not a disagreement:** D-04's ULID instance IDs cost ~26 characters each. If save size becomes a measured problem, D-04's own sanctioned fallback applies (save-local integer table with ULIDs retained as the cross-save key) — a contained change because identity is centralized in the Entity Registry. No divergence is proposed.
