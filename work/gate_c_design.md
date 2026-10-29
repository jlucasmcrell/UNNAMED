# F1 GATE C — every NPC unique (design note)

Worktree `D:\Otherreach_Scratch\agy_gatec_wt`, branch `npc/gate-c`, rebased onto `fb9a477a`.
Status: **survey + implementation plan only. No code written by this note.**
Every claim below was read out of this worktree or the canon repo in this session; a claim without a
`path:line` is marked *inference*.

---

## 0. The one-line problem

The creator (Gate B) can build a full HumGen character — 83 native face presets, 91 shaping sliders, 142
modular parts, per-people constraints — but **not one NPC goes through it**. NPCs are drawn on MetaHuman
bodies (`mh_npc_*`, `mh_resident_*`) that carry no HumGen face, no preset blend shapes and no modular
parts; and on a normal owner build they are not drawn on a `SkinnedFigure` at all, so even
`PeopleAppearance` never runs on them. Gate C is therefore mostly a *re-binding* job plus one missing
bake, not a new pipeline.

---

## 1. What actually exists today

### 1.1 NPC counts, by kind

| Kind | Where it is defined | Count |
|---|---|---|
| Named cast (own NPC file) | `content/npcs/ashen_hollow/*.yaml` (4), `content/npcs/cairnwold/*.yaml` (4) | **8** |
| Role-slot residents (no NPC file) | `content/routines/ashen_hollow/*.yaml` minus the 4 named; `content/routines/cairnwold/*.yaml` minus the 4 named | **12** (6 + 6) |
| **NPCs the world holds** | 8 + 12 | **20** |

Named cast (id — name, sex, role, town):
`npc.ashen_hollow.renn_vale` — Renn Vale, m, steward, Ashen Hollow ·
`npc.ashen_hollow.sel_arien` — Sel Arien, f, scholar ·
`npc.ashen_hollow.kera_voss` — Kera Voss, f, craftsperson ·
`npc.ashen_hollow.tavar_orr` — Tavar Orr, m, villager/companion ·
`npc.cairnwold.berrin_garth` — Berrin Garth, m, steward ·
`npc.cairnwold.durn_skell` — Durn Skell, m, craftsperson ·
`npc.cairnwold.mara_ling` — Mara Ling, f, villager ·
`npc.cairnwold.tam_crowden` — Tam Crowden, f, villager.

Role slots — Ashen Hollow: `cook`, `fletcher`, `guard`, `hunter`, `miner`, `porter`.
Cairnwold: `carter`, `guard`, `herder`, `hunter`, `trapper`, `woodcutter`.
A slot is a routine with no `npc_ref` (`src/Domain/Social/NpcLife.cs:189-224`, `IsSlot` at `:221`), and
its residents are generated one arrival after another (`src/World/Runtime/NpcLife.cs:78-95`; the instance
is `ResidentIdOf(slot, generation)`, `src/World/Runtime/Npcs.cs:107-112`).

### 1.2 Towns

- **Ashen Hollow** (`content/routines/ashen_hollow/`, workspace at x≈446-545 m, z≈722-739 m —
  `content/regions/starting_frontier.yaml:149758-149776`) and **Cairnwold**
  (`content/routines/cairnwold/`, x≈941-982 m, z≈643-681 m — same file `:149777-149793`) both have
  content. Both are placed in `region.starting_frontier` (`content/regions/starting_frontier.yaml`,
  `npcs:` block at `:149757`).
- **Sedge has NO content at all in this worktree.** `git grep -i -l sedge` over the whole repository
  returns two incidental matches (`docs/WORLD_MATERIALS.md`, `src/Presentation/Spike/SpikeScene.cs`) and
  nothing under `content/`. There is no `content/routines/sedge/`, no `content/npcs/sedge/`, no
  `sedge` region, no settlement id. Sedge was built on other lanes (`playtest/ds-first-update`, Gate 3b/4
  per `CODEX_PROGRESS.md:5559-5706`, `:6080`) and never reached this branch.
  **Gate C can only prove Ashen Hollow and Cairnwold until Sedge content is merged here.**

### 1.3 What the eight NPC files say today

Each file carries `id, kind, sex, schema, display_key, tags, name, notes, role[, services, merchant_ref,
dialogue_ref, unique, faction_ref], people_ref, teaches/values/companion`. **None carries any appearance
data.** `people_ref` is present on five and **missing on three**:

| file | `people_ref` |
|---|---|
| `content/npcs/ashen_hollow/renn_vale.yaml:13` | `people.veth` |
| `content/npcs/ashen_hollow/sel_arien.yaml:12` | `people.siann` |
| `content/npcs/ashen_hollow/kera_voss.yaml:14` | `people.kal` |
| `content/npcs/ashen_hollow/tavar_orr.yaml:12` | `people.orenth` |
| `content/npcs/cairnwold/berrin_garth.yaml:12` | `people.veth` |
| `content/npcs/cairnwold/durn_skell.yaml` | **none** |
| `content/npcs/cairnwold/mara_ling.yaml` | **none** |
| `content/npcs/cairnwold/tam_crowden.yaml` | **none** |

The approved personality bible does assign the Cairnwold four Veth
(`proposals/new/inputs/2026-10-17/NPC_PERSONALITY_BIBLE.md:88-91`: "Durn Skell | Cairnwold (Veth)" …),
and `art_bindings.json` already binds all eight to the right people (table in §5.3) — but the *content*
never says it, so `NpcDefinition.PeopleId` is null for those three (`src/Content/SocialContent.cs:162`)
and the constraint function has nothing to constrain them by. **Step 1 fixes that.**

### 1.4 How an NPC is drawn today (the crux)

`src/Presentation/Greybox/NpcsView.cs:89-107`:

```
native = MHCharacters.MetaHumanFigure.Create(_art, _bindings, npc.Id, sex)      // :93
body   = native is null ? Art.SkinnedFigure.Create(...) : null                 // :95
...
if (body is not null && body.ModelId.StartsWith("humgen_") && !named)
    Art.PeopleAppearance.ApplyResidentLook(body, npc.PeopleId, instance)       // :105-106
```

- `MetaHumanFigure.Available` is `ComparisonCast is null && File.Exists(<exe>/mh_characters/catalog.json)`
  (`src/Presentation/MHCharacters/MetaHumanFigure.cs:22-23`). **On the owner's build the bundle is
  installed, so every NPC is a MetaHumanFigure** and lines 95-106 never execute: no HumGen body, no
  `PeopleAppearance`, no per-instance colour. Residents become literally identical natives.
- When the bundle is absent (review runs without it), NPCs fall to `SkinnedFigure`, bound by
  `_bindings.Person(npc.Id, peopleId, instance, sex)` (`src/Presentation/Art/ArtBindings.cs:194-208`), whose
  models are `mh_npc_<name>` for the cast (entry keys at `src/Presentation/Art/art_bindings.json:1087-1662`,
  their `"model"` lines at `:1124,1206,1288,1371,1453,1535,1617,1699`) and `mh_resident_<people>_NN` for
  residents (`:1781` onward, six per people, `resident_people` at `:5944`, read by
  `src/Presentation/Art/ArtBindings.cs:452-453`). Those are MetaHuman bodies: they
  have no `f_/b_/s_` blend shapes and no `identity_weights.json`, so
  `HumGenCreatorAppearance.Apply` cannot run on them even if it were called — its first line refuses
  anything whose id does not start with `humgen_creator_`
  (`src/Presentation/Art/HumGenCreatorAppearance.cs:41`).
- What an NPC gets today **only when the bundle is absent**, i.e. on the `SkinnedFigure` path:
  `PeopleAppearance.Apply` at figure build
  (`src/Presentation/Art/SkinnedFigure.cs:157`) — Kal stone + wings, Siann opal skin + violet eyes +
  white hair, Orenth phase, Mor/Ondrek/Constructed/Vaskaal surfaces
  (`src/Presentation/Art/PeopleAppearance.cs:16-154`) — plus, for residents, two swatch indices hashed
  from the instance (`PeopleAppearance.ApplyResidentLook` `:443-452` →
  `LookSwatches.ResidentColours` `src/Presentation/Art/LookSwatches.cs:217-228`). That is skin tint and
  hair tint on one shared face per people — and on the owner's build, not even that. That is the whole of
  "uniqueness" today.

### 1.5 What already exists and must be REUSED, not duplicated

| Asset / code | Where | What it gives Gate C |
|---|---|---|
| 83 native face presets, with per-preset slider values and native keys | `src/Presentation/Art/humgen_creator.json` (`faces`, 83 entries, 41 f / 42 m) | the face choice, free of any bake |
| 91 shaping sliders (32 body, 57 face, 2 hair) with per-people ranges | same file, `sliders`; per-people allowlists in `runtime.people` (`people.veth` 76 sliders, `people.siann` 68 — it lacks the 8 colour sliders) | the dials, and the people's own range for each |
| Race statures and body types | same file, `races` (Veth/Orenth 160-190 cm, Kal 150-175, Siann 140-155) | stature per record, read by `CreatorCatalogue.Race` (`src/Presentation/Ui/Models/CreatorCatalogue.cs:104`) |
| 37 hairstyles, 9 facial hairs, 32 outfits, 8 footwear | same file, `hair`/`facial_hair`/`outfits`/`footwear` | the parts a record names |
| The two creator base bodies | `rigged/humgen_creator_male`, `rigged/humgen_creator_female` (staged; `…/eye_fit.json`, `identity_weights.json`, `native_materials.json`, 174 MB / 177 MB GLB) | the ONE body a sex is drawn on — all variation is blend shapes + parts |
| 83 preset material sets | `rigged/humgen_creator_materials/<face id>/materials.json` + maps | the face's own 4K skin/eye/hair textures (`HumGenCreatorAppearance.cs:242-285`) |
| 142 modular parts (hair, beard, outfit, footwear, Kal growth) | `rigged/humgen_creator_<sex>_<kind>_<option>/` with `creator_part.json` | groom/garment swapped per record (`HumGenCreatorAppearance.cs:408-508`) |
| The people constraint | `src/Presentation/Ui/Models/HumGenCreatorData.cs:109-117` → `src/Presentation/Art/LookSwatches.cs:203-215` | §2.5 |
| The look applier | `src/Presentation/Art/PeopleAppearance.cs:363-440` → `src/Presentation/Art/HumGenCreatorAppearance.cs:39-98` | one call draws a whole record on a figure |
| The plan-driven review harness | `src/Presentation/Main.RaceLooksReview.cs:16-252` (steps `spawn`/`look`/`camera`/`film`/`shot`) | the proof harness (§6) |
| Contact-sheet builder | `tools/creator/build_gateb_sheets.py` (PIL, already a repo dependency) | §6.3 |

---

## 2. The appearance record

### 2.1 Where it lives

**Decision: an `appearance:` block on the NPC's own YAML file, for the named cast; nothing on disk for
residents, who derive theirs (§3).**

Rejected alternatives, with the reason:

- **A new content kind** (`kind: appearance`). The kind vocabulary is closed
  (`src/Content/SchemaResolution.cs:59-352`; `npc` at `:95-99`), every kind owns exactly one directory
  and one ID prefix, and an appearance is not a thing the world references — it is a field of a person.
  The cost (new directory, new loader, new ID prefix, new tests, a `_ref` field everyone would want)
  buys nothing a map does not.
- **A generated file** (`content/npc_looks.yaml` written by a tool). It would have to be regenerated
  whenever a slot is added, and residents arrive without bound at runtime (DN4:
  `src/World/Runtime/NpcLife.cs:148`, `NpcLifeEngine.Hour`), so the residents' looks *cannot* be a
  generated file. The cast's records are authored facts (canon + owner rulings), and they belong in the
  diff next to the `notes:` they come from.
- **Putting it in `art_bindings.json`** (where `model`/`outfit` already live). This is the tempting one
  and it is wrong here: the bindings are the *art* mapping; the record is *who the person is*. It must be
  readable by content lint and by a test that knows nothing about Godot, and it must be constrainable
  against `humgen_creator.json`. Content is the right side of that line.

### 2.2 Record shape

On the NPC file, under the existing front matter (a map, no `_ref` suffix on any key):

```yaml
people_ref: people.siann            # required once an appearance is present
appearance:
  stature_cm: 147                   # whole cm; catalogue race range 140-155 for a Siann
  face: face.f.caucasian_presets.caucasian_1
  hair: hair.long.bob_long
  facial_hair:                      # omitted (or null) = none; always none for a Siann or a Kal
  outfit: outfit.casual.smart_casual
  footwear: footwear.outdoor.garden_boot
  sliders:                          # only what moves off the preset, in the catalogue's stored units
    face.age: 18
    face.jaw_width: -780
    face.cheek_zygomatic_bone: 750
    face.nose_bridge_width: -740
    hair.lightness: 900             # a ratio against the preset's own, not a colour (see §4)
```

The block maps 1:1 onto `CharacterAppearance` (`src/Domain/Character/Character.cs:144-165`):

| YAML | field | notes |
|---|---|---|
| `stature_cm` | `StatureMm` (×10) | `MinStatureMm`/`MaxStatureMm` = 1000/3000 (`Character.cs:147`) |
| `face` | `Face` | a `face.*` id from `humgen_creator.json.faces` |
| `hair` | `Hair` | a `hair.*` id; null is a bald head **only with `look.native_bald: 1`** (§4, Durn) |
| `facial_hair` | `FacialHair` | a `facehair.*` id |
| `outfit` | `Outfit` | an `outfit.*` id |
| `footwear` | `Footwear` | a `footwear.*` id |
| `sliders` | `Sliders` (`ImmutableSortedDictionary<string,int>`) | keys are the 91 catalogue slider ids, plus the three look-only keys `look.native_bald`, `look.kal_growth`, `look.kal_jaw` |

Three look-only keys are already in use by the creator and survive `Constrain` because it only inspects
catalogue slider ids (`HumGenCreatorData.cs:113-115`):

- `look.native_bald` — `1` suppresses the hair part entirely (`HumGenCreatorAppearance.cs:425`);
- `look.kal_growth`, `look.kal_jaw` — the Kal stone growths (`src/Presentation/Art/KalGrowth.cs:22-27`,
  applied at `HumGenCreatorAppearance.cs:429-434`).

Swatch indices (`look.skin_tone`, `look.hair_colour`, `look.kal_stone`, `look.shirt_colour`,
`look.trouser_colour`, `look.hair_style`) are **not** used by the creator-native path — it takes skin
tone, hair lightness and cloth colour from sliders (`HumGenCreatorAppearance.cs:292-389`,
`Dye` at `:396-406`) — but they are harmless and `LookSwatches.Constrain` already refuses them off their
people (`LookSwatches.cs:206-208`). Do not author them on a record that will be drawn natively.

### 2.3 Loader touch points (exact)

1. `src/Domain/Social/Social.cs:11-35` — `NpcDefinition` gains
   `public CharacterAppearance? Appearance { get; init; }` after `Values` (`:32`), with
   `using UNNAMED.Domain.Character;`. Same assembly, so no project reference changes.
2. `src/Content/SocialContent.cs:129-167` — `BuildNpcs` reads the block and sets it in the object
   initializer (after `Values = …` at `:165`). `using UNNAMED.Domain.Character;`.
   Parsing uses the file's existing helpers (`Read`/`Map`/`Text`/`Int` from
   `src/Content/CombatContent.cs:536-568`; scalars arrive as strings, see `ParseInt` at `:565`).
3. `src/World/Runtime/Npcs.cs:34-55` — `NpcView` gains `CharacterAppearance? Appearance { get; init; }`
   (beside `PeopleId` at `:48`), filled at `:235-249` from `n.Definition.Appearance`. Also
   `int Generation { get; init; }` filled from `life?.Generation ?? 0` — the resident generator's seed
   needs it and it is the one fact presentation cannot recompute (§3.1).
4. `src/Presentation/Greybox/NpcsView.cs:89-107` — the look is applied after the figure is created, in
   the same place `ApplyResidentLook` sits today:
   `PeopleAppearance.ApplyLook(body, npc.PeopleId, look, _art, body.ModelId)` — and, because
   `HumGenCreatorAppearance.Apply` needs it, `HumGenCreatorAppearance.WearHeadwear(body, false)` so the
   frontier tricorn does not cover the crown in the face sheet (the owner's own fix 1,
   `CODEX_PROGRESS.md:6980`; the toggle already exists, `HumGenCreatorAppearance.cs:26-37`).
   No new call site: `ApplyLook` (`PeopleAppearance.cs:363`) already forwards to the creator path.

### 2.4 Validator touch points, and `ContentChecks` field allowlists

`src/Content/ContentChecks.cs` is three passes (`:77-89`). What matters for a new `appearance` key:

- **Reference walk** (`:110-158`). A key ending in `_ref`/`_refs` whose stem is not in
  `ReferenceSuffixes` (`:18-48`) is an error (XREF004, `:138-142`). **Every key in the block above is
  exempt because none ends in `_ref`.** If a later record ever wants `outfit_ref`/`face_ref`, it must be
  added to that table — the table is the allowlist. `people_ref` is already there (`:46`).
- **Phase-1 schemas** (`:229-310`) only constrain `item*` and `creature`; `npc` has no schema entry, so a
  new block is neither required nor refused there. Nothing to change.
- **`BuildNpcs`'s own deny-list** (`src/Content/SocialContent.cs:149-153`) rejects
  `schedule_ref, anchors, combat_profile, relationships_init, name_pool`; `appearance` is not among them,
  so it loads today as an unknown map and is simply ignored. That is why the change is additive.

New checks, all inside `BuildNpcs` (they throw `FormatException`, which the SOC001 wrapper turns into a
lint error, `SocialContent.cs:37-39`, `:638-648`):

1. `appearance` present without `people_ref` → error ("its record is constrained by its people").
2. The map's own keys are exactly `stature_cm, face, hair, facial_hair, outfit, footwear, sliders`
   (an unknown key is a typo that would otherwise be silently ignored).
3. `sliders` keys must be valid definition IDs; values must parse as integers (reuse `Int`).
4. The built record must pass `CharacterAppearance.Problem()` (`Character.cs:153-158`), which checks the
   stature band and the `face.`/`hair.`/`facehair.`/`outfit.`/`footwear.` prefixes.
5. A candidate with `unique: true` and no `appearance` is allowed (a file that has not been dressed yet),
   but a test (§8) asserts all eight cast files have one, so the gate cannot silently lose a face.

**What content can NOT check, and why:** the people's own ranges (`runtime.people` in
`humgen_creator.json`), the swatch lists, and whether the named id is actually *offered for that sex*.
Those live in `src/Presentation/Art/humgen_creator.json`, and `Content` may not reference `Presentation`
(`tests/Architecture.Tests/ArchitectureTests.cs:20-30`). So the loop is closed in two places instead:
at draw time by `Constrain` (§2.5), and in a test that owns the catalogue (§8).

### 2.5 The one constraint function every record passes

`HumGenCreatorData.Constrain(look, catalogue, people)`
(`src/Presentation/Ui/Models/HumGenCreatorData.cs:109-117`) →
`LookSwatches.Constrain(people, look)` (`src/Presentation/Art/LookSwatches.cs:203-215`).
It is already applied at the single place a creator-body figure is built
(`HumGenCreatorAppearance.cs:66`), i.e. an off-people record cannot be drawn even from a hand-edited file.
Gate C adds no second constraining path; it adds records that must survive this one unchanged.

What it enforces, per people (all evidence in the two functions above):

| people | enforced by | effect on a record |
|---|---|---|
| **Siann** | `Constrain` drops every slider absent from `runtime.people["people.siann"]` (68 of 91 — it lacks `skin.tone/redness/saturation`, `hair.lightness/redness`, `brows.lightness/redness`, `eyes.lightness`); `FacialHair(people)==false` → beard cleared (`LookSwatches.cs:193,213`); `HairColour`/`BrowColour` false | chalk-white with opal sheen **forced by shader**, not by a colour value (`HumGenCreatorAppearance.cs:300-317`, `opal_sheen .8`, `opal_pale .9`, `native_tone 1.18`); white hair forced by the groom shader (`:359-379`); **never a beard**; `face.age` clamped to 18-30 |
| **Kal** | `look = look with { Hair = null, FacialHair = null }` (`:211`); `Stone(people)` true only here; `look.kal_growth`/`look.kal_jaw` survive only here (`:212`) | **no groom, no beard, ever**; the hair slot wears a stone growth and the beard slot a jaw/chin growth (`HumGenCreatorAppearance.cs:429-434`, `KalGrowth.cs`); stone skin on, tinted by the chosen swatch (`:328-336`); wings attached (`:112-117`) |
| **Veth / Orenth** | `Stone` false → `look.kal_stone` dropped; growth keys dropped; facial hair allowed; all 76 sliders in range | natural human ranges only: no stone, no chalk/pearl, no Kal or Siann feature; eyebrows/hair colour/tone all live |

Two gaps this note proposes closing inside the same shared rule (small, and in the file that already owns
the rule):

- **A Siann with `hair: null`** is currently legal (nothing forces a groom) and would read as a bald
  Siann, which breaks "white hair only". Add to `LookSwatches.Constrain`, with the other Siann rules:
  if `people == Siann && Hair is null` → the creator's own default style (`hair.long.bob_bangs`,
  `HumGenCreatorAppearance.cs:422-423`).
- **A Kal with a HumGen grooming slider** is already refused, but a Kal record can still name
  `hair.lightness` (it is in the Kal allowlist): harmless because no groom is resolved, and left alone
  so a Kal→other edit stays reversible.

One behaviour to know when authoring values: `Constrain` only *drops* sliders a people does not list; a
value inside a listed slider but outside the people's band is **clamped, not rejected**, in
`HumGenCreatorData.Weights` (`:133-136`, `Math.Clamp(chosen, range.Min, range.Max)`). So a badly-picked
number fails quietly by being clamped — which is why the test in §8 asserts each record's values against
`Allowed(people, slider, sex)` (`HumGenCreatorData.cs:31-37`) and not only against `Constrain`.

---

## 3. Residents: deterministic unique looks without a file

### 3.1 What seeds what

A resident has no NPC file. Its identity is already derived: `EntityId` from
`("unnamed.resident/v1", slotId, generation)` (`src/World/Runtime/Npcs.cs:107-112`), its name from
`RngChannel.Open(worldSeed, cell, "npc_life", $"name:{routine.Id}#{generation}")`
(`src/World/Runtime/NpcLife.cs:105-118`), its people from the slot's `people_ref`
(`RoutineDefinition.PeopleId`, `src/Domain/Social/NpcLife.cs:198-199`; surfaced at
`Npcs.cs:245`).

**The look is a pure function of `(settlement, slot id, generation, people, sex)` — deliberately NOT of
the world seed.** A resident's face is part of who they are, and their identity is world-seed-independent,
so a save, a reload and a different world give the same person the same face. The world seed's stream is
for events; the instance is for identity. This also makes the guarantee in §3.3 testable without building
a world.

### 3.2 Where the generator lives

New **Godot-free** `src/Presentation/Art/NpcLooks.cs`, alongside `LookSwatches.cs` (whose header says
exactly this: "Godot-free: plain values, so the tests read the same tables the game draws with",
`LookSwatches.cs:2`). It is compiled into the game and linked into `tests/Presentation.Tests`
(`tests/Presentation.Tests/Presentation.Tests.csproj:45` already links `LookSwatches.cs`, and
`:57` links all of `src/Presentation/Ui/Models/*.cs`, which is where `HumGenCreatorData` and
`CreatorCatalogue` live). It needs `CreatorCatalogue`/`HumGenCreatorData`, which is why it must live in
Presentation and not in Domain or Content.

API (sketch, to keep the shape honest):

```csharp
public static class NpcLooks
{
    // The twelve (or six) records a town's roster wears, one per slot, no two alike.
    public static ImmutableDictionary<string, CharacterAppearance> Residents(
        IReadOnlyList<string> slotIds, string settlement, string people, string sex,
        CreatorCatalogue catalogue, HumGenCreatorData data);

    // One resident, for a slot whose generation changed (an arrival).
    public static CharacterAppearance Resident(
        string slotId, string settlement, int generation, string people, string sex,
        CreatorCatalogue catalogue, HumGenCreatorData data, int rosterPosition, int rosterSize);

    public static CharacterAppearance? Of(NpcDefinition definition, string slotId, string settlement,
        int generation, string? people, string? sex, CreatorCatalogue catalogue, HumGenCreatorData data);
    // -> the authored record for the cast (already constrained), the generated one for a resident.
}
```

Not a tool that writes content: the arrivals are unbounded (`config.npc_life.yaml:9` — `arrival_days:
[3, 7]`), so there is no finite set to write down.

### 3.3 "No two identical in one town", guaranteed by assignment rather than luck

`Randomize` alone is a hash draw (83 faces) and a collision is unlikely but *not* excluded — and a
guarantee that rests on luck is not a guarantee. So the face is **assigned**:

1. Take the town's slot ids in `StringComparer.Ordinal` order (stable, and `NpcLifeSetup.Routines` is
   itself an ordinal map, `src/Content/NpcLifeContent.cs:241`). Position `i` in that list.
2. `faceIndex = (TownOffset(settlement) + i + generation * GenerationStep) % faces(sex).Count`, where
   `TownOffset` is an FNV-1a of the settlement id (the same hash the repo already uses for stable
   selection, `src/World/Runtime/NpcLife.cs:130-137`, `src/Presentation/Art/ArtBindings.cs:204-207`) and
   `GenerationStep` is a constant coprime-ish stride.
3. Distinctness: for one town and one generation, adding a constant to `i` is a bijection, so the six
   slots get six distinct faces as long as `6 ≤ faces(sex).Count` — 41 (f) / 42 (m) in
   `humgen_creator.json`, so there is a 6.8x margin. An arrival shifts every face by
   `GenerationStep`, which keeps them distinct from each other and (with a stride that is not a multiple
   of the count) from the previous holder of the same slot.
4. The dials come from `HumGenCreatorData.Randomize(look, catalogue, people, sex, seed)`
   (`HumGenCreatorData.cs:86-102`) with
   `seed = RngChannel.Open(0, default, "npc_looks", $"{settlement}/{slotId}#{generation}")` — the same
   `RngChannel` shape the name draw uses (`NpcLife.cs:114`) — then `Face` is set to the assigned id
   (`CreatorCatalogue.WithFace` clears the preset's own face sliders, `CreatorCatalogue.cs:130-134`) and
   the whole thing goes through `Constrain`.
5. Body shape comes from the same `Randomize` (it rolls the `body` panel, `:92-97`), so two residents
   sharing a face index in *different* towns still differ in build — and never share a face within one.

The three things the test can prove without a renderer: distinctness per town per sex, stability across
calls and across world seeds, and `Constrain(record) == record` for all 12 + 8 records (§8).

### 3.4 Sex, stature, and the other missing inputs

- **Sex.** A resident has none: the definition built at `src/World/Runtime/NpcLife.cs:93` has no `Sex`,
  so `NpcsView.cs:91` passes `null` down, and `PlayerBodies.ResidentModel(people, sex)`
  (`src/Presentation/Art/PlayerBodies.cs:37-40`) cannot choose a body. Fix at the source: an optional
  `sex: f|m` on the twelve role-slot routines, validated in `NpcLifeContent.Build` beside the existing
  slot checks (`src/Content/NpcLifeContent.cs:186-188`: "a role slot names the people its residents come
  from"), and carried on `RoutineDefinition`. Twelve lines of content, reviewable, and it lets the roster
  read as a village. Fallback if the owner prefers no content churn: derive from FNV-1a of the slot id.
- **Stature.** `catalogue.Race(people)` mid ± `Randomize`'s draw, clamped to the race band
  (`CreatorCatalogue.cs:14`, `:104`); the record's `StatureMm` then drives
  `SkinnedFigure.SetCreatorStature` (`src/Presentation/Art/SkinnedFigure.cs:28-31`), which is a uniform
  scale of the single creator base — the only stance on stature the pipeline has.
- **Outfit.** `outfit.casual.smart_casual` (both sexes) is the one catalogue outfit that reads as
  "work shirt and trousers" (`LookSwatches.ShownNames`, `LookSwatches.cs:55`), and its shirt and
  trousers are dyed per resident from `LookSwatches.Shirts`/`Trousers`
  (`HumGenCreatorAppearance.cs:396-406`), so five shirts x two trousers give residents visibly different
  clothes inside the frontier palette the owner ruled (no blue denim, `LookSwatches.cs:32-35`).
  `footwear.outdoor.garden_boot` is drawn as worn leather (`LookSwatches.cs:58`, `GarmentKind` "boots").

---

## 4. The named cast

### 4.1 What canon actually says (and does not)

Read for this note: `direction/extensions/RACES.md` (the peoples), `direction/extensions/MYTHOLOGY.md`,
`proposals/new/inputs/2026-10-17/NPC_PERSONALITY_BIBLE.md` (owner-approved, "Binding facts used" at
`:16-22`), `canon/areas/ashen-hollow*.md`, `canon/areas/cairnwold.md`, and each NPC's own `notes:` /
dialogue in this worktree.

**The load-bearing negative result: canon specifies no per-NPC physical look at all.** The only per-NPC
facts are each NPC's people and one age:

- `NPC_PERSONALITY_BIBLE.md:17-20` — "Kera is Kal: dense, folding dorsal wings"; "Sel is Siann: opal
  skin, ageless at about 18"; "Tavar is Orenth: … his body registers slightly late"; "Renn is Veth,
  about 60". **Renn is the only binding age.**
- Everything concrete an artist would use is either a people-level default or an art-team decision in
  `proposals/new/inputs/2026-10-18/` that is *not canon*: Renn's "grey hair and blue eyes" and
  "clean-shaven" (`…/External_Assets/CODEX_PROGRESS.md:62,76,134`), Durn's "bald Creator face"
  (`:147`), Kera's "folded wings under a harness" (`…/ASSET_REBUILD/CHARACTERS_AND_GEAR_PLAN_20261017.md:138`).
- Where canon and one binding ruling disagree on the Kal: **"Kal have no hair" is not canon text** — it is
  the owner's ruling of 2026-10-28, recorded at `CODEX_PROGRESS.md:6999` ("KAL HAVE NO HAIR. No head
  hair, brows or facial hair from the HumGen hair library for Kal. The Kal HAIR step instead offers STONE
  GROWTHS…") and implemented per `CODEX_PROGRESS.md:7050`. That ruling is binding for Gate C per the
  brief; it is just not a `RACES.md` line, and the plan says so in the commit that depends on it.
- VO casting ages in the same approved bible are per-NPC but are *casting notes*, not canon: Renn "About
  60" (`:144`), Kera "reads 35-50 (Kal age is hard to read)" (`:185`), Sel "sounds 18-20" (`:225`),
  Tavar "Male, 30s" (`:257`), Berrin "Male, 70s" (`:296`), Durn "Male, 50s" (`:327`), Mara "Female, late
  30s" (`:356`), Tam "Female, late 20s" (`:389`). Berrin is also "Veth, elder" (`:88`).
- People rules used below, all `RACES.md`: Veth `:128-147` (longer line, narrower ribcage, reflective
  irises); Kal `:161-192` (broad, short, dense; large folding dorsal wings in channels beside the spine);
  Siann `:228-254` (opal, faintly translucent, silver-lavender subdermal traces; pale lavender eyes with a
  brighter inner ring; elongated neck, long fine hands, narrow shoulders; unnervingly precise symmetry;
  **no pointed ears**; platinum-white hair, whitest = oldest) and the Mira reference `:543-558`;
  Orenth `:281-308` (broadly humanoid; reality registers it imperfectly; phase scars in the old).

### 4.2 The records (starting values, to be tuned on the face sheet, not in code)

Dials are proposal. The *structure* is the deliverable: preset + a handful of moved sliders + hair +
outfit. "Age" is the `face.age` slider; the Siann's band is 18-30 so her record sits on its floor.

| NPC | people / sex | canon basis | `face` preset | key sliders | hair / facial hair | outfit / footwear |
|---|---|---|---|---|---|---|
| **Renn Vale** | Veth m | ~60 (`bible:20`); Veth longer line, narrower ribcage (`RACES:135-139`); grey + clean-shaven + blue eyes **art only** | `face.m.caucasian_presets.caucasian_2` (the player Mateo's own family, already reviewed in Gate B) | `face.age 60`, `body.skinny 420`, `body.shoulder_width -180`, `body.forearm_length 260`, `hair.lightness 850` (grey as a ratio off the preset, per `HumGenCreatorAppearance.cs:383-388`), `eyes.lightness 1150` | `hair.short.short_combed`, facial hair **none** | `outfit.casual.smart_casual` dyed "Undyed linen"; `footwear.outdoor.garden_boot` |
| **Sel Arien** | Siann f | ageless ~18 (`bible:18,86,225`; `RACES:217-219`); opal + platinum-white, waist-long, loose (`RACES:545-547`, `MYTHOLOGY:174-180`); no pointed ears | `face.f.caucasian_presets.caucasian_1` (Ada's family) + the whole `SiannFaceBySex["f"]` table (`LookSwatches.cs:97-102`: jaw -780, chin -790/-430, brow -70, cheekbone 750, eye tilt 350, nose bridge -740, tip -580) | `face.age 18` (the Siann floor) | `hair.long.bob_long` — long and full, and *not* `medium_center_part`, which Gate B found draws as thin stringy strands (`HumGenCreatorAppearance.cs:420-423`); no beard (forced) | `outfit.casual.smart_casual` dyed "Slate" (an archivist's grey); `footwear.outdoor.garden_boot` |
| **Kera Voss** | Kal f | dense, wings; "reads 35-50"; works a forge all day (her `notes:`), harness over folded wings (`bible:178`) | `face.f.caucasian_presets.caucasian_4` (the face the Kal race body is baked with, `humgen_creator.json baked[2]`) | `face.age 44`, `body.overweight 620`, `body.muscular 520`, `body.waist_thickness 250`, `face.jaw_width 420`, `face.chin_width 300` | **no groom, no beard** (forced); `look.kal_growth 1` (crown crest), `look.kal_jaw 1` (jaw ridges); stone swatch = Basalt (`LookSwatches.KalSkin[2]`, `LookSwatches.cs:156`) | `outfit.casual.smart_casual` dyed "Ochre" (forge dust); `footwear.outdoor.garden_boot` (leather, `LookSwatches.cs:58`) |
| **Tavar Orr** | Orenth m | "30s" casting; "his body registers slightly late" (`bible:19`); Orenth phase scars only in the old (`RACES:298-305`); "a leather jerkin" (his `notes:`) | `face.m.caucasian_presets.caucasian_1` (the Orenth m race body's face, `baked[7]`) | `face.age 34`, `body.skinny 300`, `face.cheek_zygomatic_proc 220` (the gaunt walker), `face.eye_depth -180` | `hair.short.short_combed`; stubble `facehair.other.stubble_long` | `outfit.casual.smart_casual` dyed "Brown" (leather); `footwear.outdoor.garden_boot` |
| **Berrin Garth** | Veth m | "Veth, elder" + "70s" (`bible:88,296`); keeps a long lodge, pokes fires, leans on a broom (`bible:290-293`) | `face.m.caucasian_presets.caucasian_5` | `face.age 72`, `body.overweight 480`, `body.skinny -300`, `face.jaw_width 260` | `hair.short.short_combed` at `hair.lightness 2600` (near-white elder), `brows.lightness 2200`; full beard `facehair.beard.groomed_beard_1` — **inference**, no canon statement either way | `outfit.casual.smart_casual` dyed "Moss"; `footwear.outdoor.garden_boot` |
| **Durn Skell** | Veth m | "50s" (`bible:327`); "bald Creator face" **art only** (`CODEX_PROGRESS.md:147`); never stops working | `face.m.caucasian_presets.caucasian_3` | `face.age 52`, `body.muscular 620`, `body.shoulder_width 340`, `face.browridge_center_size 320` | `hair: null` **plus `look.native_bald: 1`** — without that slider the preset's own groom returns (`HumGenCreatorAppearance.cs:424-425`); short stubble `facehair.other.stubble_short` | `outfit.casual.smart_casual` dyed "Charcoal" + "Ochre"; `footwear.outdoor.garden_boot` |
| **Mara Ling** | Veth f | "late 30s" (`bible:356`); wrings her hands on an apron, taps a foot (`bible:352`) | `face.f.caucasian_presets.caucasian_3` | `face.age 39`, `body.skinny 320`, `face.lip_width -410`, `face.browridge_loc_vertical 180` (a frown that never leaves) | `hair.long.ponytail` (tied back, working); no beard | `outfit.casual.smart_casual` "Moss"; `footwear.outdoor.garden_boot` |
| **Tam Crowden** | Veth f | "late 20s" (`bible:389`); counts sacks on her fingers, arms wrapped round herself (`bible:386`) | `face.f.caucasian_presets.caucasian_2` | `face.age 28`, `body.skinny 500`, `face.eye_width 180`, `face.forehead_size 120` (younger, rounder) | `hair.short.short_side_part`; no beard | `outfit.casual.smart_casual` "Undyed linen"; `footwear.outdoor.garden_boot` |

Notes that matter more than the individual numbers:

- **The preset is the identity, the dials are the person.** Each is from the same family as the body the
  people is already drawn with (`humgen_creator.json.baked`: kal → caucasian_4, siann → caucasian_1 /
  caucasian_2, orenth → caucasian_1 / caucasian_3), so an NPC does not jump people when it is re-bound.
- **Bone over colour.** The owner ruled that a people's palette is enforced (Siann always chalk-white,
  Kal always stone), which leaves *bone structure and hair shape* as the only real identity levers —
  which is exactly what these dials move. That is also why the four Siann-looking presets differ mainly
  in structure (Gate B's own finding, `CODEX_PROGRESS.md:6941`).
- **The two art-only decisions are named as such** in the content comments (Renn's grey and clean
  shave, Durn's baldness), so a later canon pass can overrule them without archaeology.

---

## 5. The export / bake path — what is actually missing

### 5.1 The one real gap: motion for the two creator base bodies

Every figure the game animates needs clips. For a HumGen body those clips are **exact-target libraries**
built offline against that exact body and keyed to its SHA-256
(`src/Presentation/Art/SkinnedModel.cs:181-203`: the payload is
`<OTHERREACH_CHARACTER_ROOT>/mocap_motion/<assetId>_motion.res`, checked with
`motion.GetMeta("target_body_sha256") != sha256(bodyPath)` at `:197-198`). 39 such libraries exist
(in `D:\Otherreach_Scratch\pp11_release\game\humgen_characters\mocap_motion`: the 8 `humgen_named_*`, the
6 `humgen_pool_*`, the 8 race bodies, the 2 players, the 6 Veth face bodies, …). **There is no
`humgen_creator_male_motion.res` and no `humgen_creator_female_motion.res`.**

That is the whole of the bake gap, and it is small because the creator body is *one base per sex*: every
NPC, of every stature and build, is the same two meshes with different blend-shape weights and different
modular parts (`humgen_creator.json.runtime.bases`, and `HumGenCreatorAppearance.Parts` at `:408-508`).
So Gate C needs **two** libraries, not twenty.

Three consequences:

1. The bare-MetaHuman alternative (keep NPCs on `humgen_named_*`/`humgen_pool_*`, which already have
   motion) is closed: those bodies carry no `f_/b_/s_` blend shapes and no `identity_weights.json`, and a
   `creator_part.json` pins its groom/garment to the creator base's
   `target_body_sha256` (checked at `HumGenCreatorAppearance.cs:452-453`). A preset face cannot be drawn
   on them.
2. The per-NPC-body alternative (bake a `humgen_npc_<id>` body per person, as the pool bodies were) costs
   **one Unreal retarget job per NPC** (20 of them today, unbounded once residents arrive) and freezes each
   face into a GLB — losing exactly the data-driven record Gate C is asking for. Rejected.
3. What the two libraries cost: one staging receipt, two Unreal CPU passes, two Godot headless library
   builds, no GPU. The runner already exists —
   `tools/animation/run_humgen_cast_motion.ps1` (params `-JobRoot`, `-StagingReceipt`, `-Only`;
   it imports the target with `probe_humgen_target.py`, retargets with `retarget_humgen_probe.py`, audits
   with `audit_humgen_transport.py`, and builds the library with `tools/animation/build_humgen_motion.gd`
   through `tools/animation/run_cpu_tool.ps1` on `UnrealEditor-Cmd.exe`, `-NullRHI` — CPU only, no GPU
   lease, `:17-19`). The output path is written at `:47`:
   `<parent of JobRoot>/runtime/humgen_characters/mocap_motion/<id>_motion.res`.

### 5.2 Wiring the creator bodies to that payload

`SkinnedModel.Create` installs clips by retargeting the binding's `clips` onto the model's skeleton by
bone name (`:104-118`) — correct for a MetaHuman body, wrong for a HumGen rig; `ComparisonClips` is the
exact-target path and is chosen by `IsComparisonBody(assetId)`, i.e. only when
`--visual people=humgen|mpfb` (`:173-175`, `src/Presentation/VisualOptions.cs:211`).

Change (one line of policy, in one method): a body with an exact-target library available is drawn from
it, whatever the visual option —

```csharp
// SkinnedModel.cs: IsComparisonBody becomes "has its own exact-target motion":
private static bool ExactMotion(string assetId) =>
    IsComparisonBody(assetId) || assetId is "humgen_creator_male" or "humgen_creator_female";
```

used at `:106`, `:119` and inside `ComparisonClips` (`:185` picks the payload root — for a creator body
in a normal run it must be `OTHERREACH_CHARACTER_ROOT`, so the `humgen` branch of `:186-192` has to be
taken for creator bodies too, and the `HumGenMotionPolicy.Validate` call at `:199-203` applies unchanged).
Parts (`humgen_creator_*_hair_*` etc.) are never `SkinnedModel`s — they are loaded raw through
`art.Rigged(asset)` inside `Parts()` — so the prefix test is unambiguous.

### 5.3 Bindings changes, per person

`src/Presentation/Art/art_bindings.json`, `people` entries (entry keys `:1087`…`:1662`, their `model`
lines `:1124`…`:1699`):

| field | today | after | why |
|---|---|---|---|
| `model` | `mh_npc_<name>` | `humgen_creator_male` / `humgen_creator_female` | the only body that carries the presets and parts (`HumGenCreatorAppearance.cs:41`) |
| `hand` / `off_hand` | `SOCK_hand_r` / `SOCK_hand_l` | omitted (the loader defaults to `hand_r`/`hand_l`, `ArtBindings.cs:439,658-659`) | the creator body is the HumGen rig, whose hand bones are `hand_r`/`hand_l` (the same names `MetaHumanFigure.cs:132-137` and `Outfit.cs:148` use for HumGen-shaped rigs) |
| `outfit` | `frontier_*_set__mh_veth_*` | `[]` | those garments are baked for MetaHuman bodies; `Outfit.Dress` returns immediately on an empty list (`src/Presentation/Art/Outfit.cs:41-44`), and the creator's own outfit part dresses the body instead (`HumGenCreatorAppearance.cs:436-441`) |
| `uniform_scale` | 0.88 / 0.9 / 1.0 | omitted (1.0) | stature now comes from the record via `SetCreatorStature` (`SkinnedFigure.cs:28-31`), and a second scale would compound |
| `clips` | `player.mh.*` | unchanged | the exact-target library supplies the clips; the map is unused for a creator body |

The same four changes on the resident bindings if the resident path keeps using them — it should not:
residents are `resident.<people>.NN` *bindings* whose only job today is picking one of six MetaHuman
variants (`ArtBindings.cs:202-207`). With a generated record the variant is pointless; the resident path
should bind the same two creator bodies and take everything else from `NpcLooks` (§3), leaving
`resident_people` in place only for the peoples this pipeline does not draw.

And one behavioural line in `src/Presentation/Greybox/NpcsView.cs:93`: **an NPC with a look record is
drawn as a `SkinnedFigure` on its people's creator body even where the MetaHuman bundle is installed** —
otherwise `MetaHumanFigure.Available` (`MetaHumanFigure.cs:22-23`) wins on the owner's build and none of
this is visible. The player keeps the MetaHuman figure; only NPCs move. (This is the same
"one figure class per person" choice the comparison cast already makes, `ArtBindings.cs:210-217`.)

### 5.4 What is NOT needed (say it out loud, so nobody does it)

- No new export of faces, grooms, outfits or footwear: 83 preset material sets and 142 modular parts are
  already held and marked (`tools/creator/mark_creator_ready.py`, `CODEX_PROGRESS.md:6926`).
- No re-bake of the creator base bodies: `stage_humgen_creator.py` already writes
  `identity_weights.json`, `eye_fit.json` and `native_materials.json` beside them
  (`tools/creator/stage_humgen_creator.py:23-54`).
- No per-NPC GLB, no downscaling, no new VRAM policy — the brief forbids it and the shared base makes it
  unnecessary.
- No image-generation model anywhere in the path (the brief's hard rule).

### 5.5 Art root and the 32 GB that does not need copying

Gate C's runs need two roots, exactly as Gate B's runner passes them
(`tools/creator/run_creator_gate_b.ps1:26-27`):

- `--asset-root` — the world art root. Use the master `D:\Otherreach_Scratch\art_merged` (85 rigged
  folders, `bindings.json`, `ready/`), which already holds `humgen_named_*`, `humgen_pool_*` and the race
  bodies. Gate B used a private 22.0 GB copy (`work\fidelity\game\primary` in `codex_f1_siann_wt`); the
  master root is the same art and costs nothing.
- `--asset-fallback` — the creator root (`10.3 GB`: the two bases, 83 preset material sets, 142 parts),
  which exists **only** in `D:\Otherreach_Scratch\codex_f1_siann_wt\work\creator\game\primary`. Do not
  copy it and do not write into that worktree: junction it —
  `mklink /J work\creator\game\primary D:\Otherreach_Scratch\codex_f1_siann_wt\work\creator\game\primary`
  then `tools/creator/link_creator_parts.py work/creator` (`:13-30`, it junctions every
  `humgen_creator_*` folder into `work/creator/game/world_fallback/rigged`). The junction is a directory
  link into a read-only source; if that worktree is ever cleaned, re-running the export
  (`tools/creator/export_humgen_creator_parts.py`, CPU Blender, work anchor `D:\oc`) rebuilds it.
- `OTHERREACH_CHARACTER_ROOT` — set it to the payload holding `mocap_motion/`
  (`D:\Otherreach_Scratch\pp11_release\game\humgen_characters`), which is also where the two new
  creator-body libraries land.

---

## 6. The proof harness

Requirement: one town at a time, every NPC's face at 2 m in daylight with no HUD, plus three wide shots,
and a contact sheet to look at.

### 6.1 The smallest addition: one new plan-driven review mode

The existing machinery already does 95% of it. `OTHERREACH_MH_REVIEW_OUT` + `OTHERREACH_RCD3_REVIEW=1`
reaches `RunRcd3Review` (`src/Presentation/Main.MetaHumanReview.cs:42-46, 87-88, 194-197` →
`src/Presentation/Main.Rcd3Review.cs:18-54`), which builds a review camera and makes it current
(`:41-46`) and hands a plan file to a step runner when an env var names one (`:49-54`); the runner hides
the HUD and the NPCs (`src/Presentation/Main.RaceLooksReview.cs:20-21`). `RunRaceLooksReview`
(`src/Presentation/Main.RaceLooksReview.cs:16-252`) is that runner, with the steps Gate C needs:
`spawn` (`:64-83`), `look` (a full `CharacterAppearance` from JSON → `PeopleAppearance.ApplyLook`,
`:154-174`), `camera` (`:144-147`; the framing is whatever the plan's `dist`/`height`/`fov` say — 2 m
face framing is `dist 2.0, height 1.62, fov 32`), `shot` (`:151-153` → `Rcd3Shot`,
`Main.Rcd3Review.cs:302-308`), `hour` for daylight (`:140-143`), and `complete.json` at the end (`:251`).

New file `src/Presentation/Main.NpcLooksReview.cs`, same class pattern, entered from
`Main.Rcd3Review.cs` beside the race-look branch:

```
if (OS.GetEnvironment("OTHERREACH_NPC_LOOKS_PLAN") is { Length: > 0 }) { await RunNpcLooksReview(directory, camera); CaptureQuit.At(this); return; }
```

and its plan is minimal, because the roster comes from content, not from the file:

```json
{ "settlement": "ashen_hollow", "hour": 11,
  "faces": [{ "id": "npc.ashen_hollow.renn_vale" }],                       // or "all"
  "wides": [ { "name": "wide_waystation", "at": [449, 12, 733], "look": [460, 6, 745], "fov": 55 } ] }
```

What the runner does, in this order:

1. Resolve the town's residents from content, not a list in the plan:
   `_session.Setup.NpcLife.Routines` where `Settlement == settlement`, split into named
   (`NpcId != null`) and slots (`IsSlot`) — `src/Domain/Social/NpcLife.cs:189-224`.
2. For each person, get the look exactly as the game does: `NpcLooks.Of(...)` (§3.2) for the cast's
   authored record and the residents' generated one. Same call the game makes → the sheet proves the
   shipped look, not a rehearsal.
3. Spawn it on the creator body of its sex with
   `SkinnedFigure.Create(_art, _bindings, personId, body: "humgen_creator_<sex>", people: peopleId)` and
   apply the look with `PeopleAppearance.ApplyLook(figure, people, look, _art, model)` + 
   `HumGenCreatorAppearance.WearHeadwear(figure, false)` — the same two calls `NpcsView` will make (§2.3).
   Stand it at its own site: `_session.Setup.Layout.Npcs` by `NpcId` for the cast, the routine's `work`
   anchor for a resident (`NpcLifeContent.Anchor`, `src/Content/NpcLifeContent.cs:335-344`), and the
   ground height from `_npcs.Ground` as `Main.RaceLooksReview.cs:24,32` does.
4. `hour 11` (daylight, the owner's noon rule), then per person: camera 2 m from the eyes
   (`height 1.62`, `dist 2.0`, `fov 32`), `Rcd3Shot(directory, $"face_{npcId}")`.
5. Three `wide` shots from the plan's authored camera/aim, one of them looking at the town's centre.
6. Write `npc_looks.json` — one row per NPC with people, sex, stature, face id, hair, outfit, the slider
   map, and the mesh/surface/material probe the race review already writes
   (`Main.RaceLooksReview.cs:109-129`), so the sheet and the log can be cross-checked; then
   `complete.json`.

That is ~120 lines in one new file plus 8 lines in `Main.Rcd3Review.cs`. Nothing else in the review
machinery changes.

### 6.2 The runner script

`tools/creator/run_npc_looks.ps1`, a copy of `run_creator_gate_b.ps1` with: the plan env var, the
`--asset-root`/`--asset-fallback` of §5.5, `OTHERREACH_CHARACTER_ROOT`, `--visual` with `hour=11,
tier=high,people=production` (the creator bodies come from the bindings now, not from a comparison cast),
the `gpu_lock.ps1 acquire "<label>"` / release bracket, and an assertion on the log that art was real
(`assets drawn from the library at boot; 0 withheld`), aborting otherwise — exactly the rule the brief
sets and Gate B followed (`CODEX_PROGRESS.md:6933`).

### 6.3 The contact sheet

`tools/creator/build_npc_looks_sheet.py`, modelled on `build_gateb_sheets.py` (PIL, `ImageFont` at 26/34 px,
a caption under every tile, a page per town): one tile per NPC face, cropped to the head box from the
3840x2160 frame, captioned `Ashen Hollow · Renn Vale · Veth male · 61 · Face 12` — a caption built from
the same record the run wrote, so a caption cannot drift from the face.

### 6.4 What I look at, and what I reject

Per the brief and `vision-qa`: open every face tile and the three wides. **Reject my own output** if:
skin is glossy or plastic; eyes stare (no lid, no iris ring); hair is crayon-like or a field of dots
(the Gate B failure, `CODEX_PROGRESS.md:6929`); two faces in one town are the same; a Kal has hair, brows
or a beard; a Siann is not chalk-white with white hair or has a beard; a Veth/Orenth has stone or chalk;
a face is hidden by headwear; a body's hands or neck are the wrong scale for its stature; a wide shot
shows greybox or a missing NPC.

---

## 7. Step-ordered checklist

One coherent behavioural change per commit, pathspec-only, `git -c gc.auto=0`, no push, and the trailer
`Co-Authored-By: Gemini 3.8 via Antigravity <noreply@google.com>`. Sizes are the survey's estimate.

### Step 0 — art and payload (no commit; local staging only)

- `mklink /J work\creator\game\primary <codex_f1_siann_wt>\work\creator\game\primary`; then
  `python tools\creator\link_creator_parts.py work\creator` (expect "creator part folders in the preview
  root: 145").
- `OTHERREACH_CHARACTER_ROOT=D:\Otherreach_Scratch\pp11_release\game\humgen_characters`.
- Check: `work\creator\game\world_fallback\rigged\humgen_creator_male\creator_ready.json` exists and its
  4K/2K sizes pass `tools\creator\mark_creator_ready.py` (`CREATOR_READY 83 faces, 142 modular parts
  held, 0 pending`).

### Step 1 — the three NPCs that never had a people get one

- `content/npcs/cairnwold/durn_skell.yaml`, `mara_ling.yaml`, `tam_crowden.yaml` — `people_ref: people.veth`
  (with a comment naming `NPC_PERSONALITY_BIBLE.md:88-91` as the source, as `berrin_garth.yaml:12` already does).
- Tests: `tests/Content.Tests/SocialContentTests.cs` (existing suite) + `dotnet run --project src/Content -- lint`.
- Why first: every later check needs a people to constrain by, and `NpcDefinition.PeopleId`
  (`src/Content/SocialContent.cs:162`) is what `Systems.PeopleOf` reads (`src/World/Runtime/Systems.cs:86-88`).

### Step 2 — the record: domain field, loader, and the content-side checks

- `src/Domain/Social/Social.cs` (+`Appearance`), `src/Content/SocialContent.cs` (parse + 5 checks of §2.4).
- No content values yet; the block is legal and unused.
- Tests: `tests/Content.Tests/` — a new `NpcLooksContentTests` case that a file *without* an appearance
  still loads, and one that a record without `people_ref`, with an unknown key, with a bad id prefix, or
  with a stature out of 1000-3000 mm fails with SOC001.
- Prove: `dotnet run --project src/Content -- lint` still reports 0 errors.

### Step 3 — the Siann "always has hair" line in the shared rule

- `src/Presentation/Art/LookSwatches.cs` (`Constrain`, one line, §2.5).
- Test: `tests/Presentation.Tests/LookSwatchesTests.cs` — a Siann with `Hair = null` comes back with the
  creator's default style; a Veth with `Hair = null` still comes back bald.
- Separate commit because it changes the creator too (Reset/Randomise/saves pass through the same call).

### Step 4 — the residents' generator

- New `src/Presentation/Art/NpcLooks.cs` (Godot-free) + link it in
  `tests/Presentation.Tests/Presentation.Tests.csproj` (one `<Compile Include>` beside `LookSwatches.cs`).
- Content: `sex: f|m` on the twelve slot routines (`content/routines/ashen_hollow/{cook,fletcher,guard,
  hunter,miner,porter}.yaml`, `content/routines/cairnwold/{carter,guard,herder,hunter,trapper,
  woodcutter}.yaml`); `src/Content/NpcLifeContent.cs` reads and validates it (`:186-188` beside the
  slot/people check), `src/Domain/Social/NpcLife.cs` carries it on `RoutineDefinition` (`:198-199`).
- Tests: new `tests/Presentation.Tests/NpcLooksTests.cs` —
  (a) twelve slots in one town, one sex → twelve **distinct** faces;
  (b) same call twice → identical records; different world seeds → identical records;
  (c) every generated record equals `Constrain(record)` for its people;
  (d) a Kal resident has `Hair == null && FacialHair == null`, a Siann resident's face is in the Siann
  face list and `face.age` lands in 18-30.
- Also extend `tests/Content.Tests/` for `sex` on a slot (f/m only, absent is allowed and falls back to
  the hash).

### Step 5 — the eight authored records

- The eight `content/npcs/**/*.yaml` files gain the `appearance:` block of §4.2 (values are proposals).
- Test: extend `NpcLooksTests` to read the content tree (needs `ProjectReference` from
  `tests/Presentation.Tests` to `src/Content/Content.csproj` — engine-free, so no architecture rule is
  touched; see §8) and assert: every `unique: true` NPC has a record; every record survives `Constrain`
  unchanged; every moved slider's value sits inside `Allowed(people, slider, sex)`
  (`HumGenCreatorData.cs:31-37`, because `Weights` would otherwise clamp it silently — §2.5); every
  `face`/`hair`/`facial_hair`/`outfit`/`footwear` id is offered for that sex by
  `CreatorCatalogue.Options` (`CreatorCatalogue.cs:98-99`); a Kal names no hair/beard; a Veth/Orenth
  names no `look.kal_*`; a Siann names no `look.skin_*` off her list.
- **This is the commit that makes the records real**, and the first one a reviewer should read.

### Step 6 — two motion libraries for the two creator bases

- A staging receipt with two rows
  (`{id: humgen_creator_male, staged: …/humgen_creator_male_rigged.glb, sha256: …}`, and female), then
  `tools/animation/run_humgen_cast_motion.ps1 -JobRoot <job> -StagingReceipt <receipt>
  -Only humgen_creator_male,humgen_creator_female`.
- CPU only (UE `-NullRHI`, `godot --headless`), no GPU lease. Outputs
  `<CHARACTER_ROOT>\mocap_motion\humgen_creator_{male,female}_motion.res`, each with a `.audit.json`.
- Check, before anything is drawn: the receipt's `clips.Count` equals the source selection count, and the
  library's `target_body_sha256` equals the GLB's SHA-256 (`SkinnedModel.cs:197-198` does exactly this at
  load and throws otherwise — the cheapest possible verification).

### Step 7 — wire the creator bodies to that payload

- `src/Presentation/Art/SkinnedModel.cs` — `ExactMotion(assetId)` (§5.2), used at `:106`, `:119`, `:186`.
- Test: `tests/Presentation.Tests/` — a pure-function test of the predicate (`humgen_creator_male` and
  `humgen_creator_female` true, `humgen_creator_male_hair_…` false, `mh_veth_m` false), plus
  `HumGenMotionPolicyTests` unchanged.
- Prove: one figure walking on `humgen_creator_female` through the **existing** runner
  (`OTHERREACH_RACE_LOOKS_PLAN` with `spawn` + `walk` + `film`), with no
  `FileNotFoundException`/`InvalidDataException` from `ComparisonClips` and a `complete.json` at the end.
  That check costs one run and no new code.

### Step 8 — bind the NPCs to the creator bodies

- `src/Presentation/Art/art_bindings.json` — the eight `people` entries per §5.3 (and the resident
  entries, if kept).
- `src/Presentation/Greybox/NpcsView.cs:89-107` — the figure choice (creators for NPCs with a look, even
  with the MetaHuman bundle installed) and the one `ApplyLook` + `WearHeadwear(false)` call.
- Prove: the existing `--rcd3-review` NPC shots still frame every NPC and the log still reports the same
  NPC count; `dotnet build` of `UNNAMED.Presentation` 0 errors.

### Step 9 — the harness and the sheets

- New `src/Presentation/Main.NpcLooksReview.cs`; 8 lines in `src/Presentation/Main.Rcd3Review.cs:49-54`.
- New `tools/creator/run_npc_looks.ps1`, `tools/creator/build_npc_looks_sheet.py`, and the plan JSONs
  (`tools/creator/plans/ashen_hollow.json`, `cairnwold.json`) — two towns, not three (§1.2).
- Produce: `<out>\ashen_hollow\`, `<out>\cairnwold\`, one sheet per town, plus `npc_looks.json`.
- Look at every face tile and the six wides myself; reject and fix per §6.4; only then claim it.

### Step 10 (after the above lands) — Sedge

Only once Sedge content exists on this branch. Nothing in steps 1-9 assumes a town count, so Sedge is the
same three lines of plan JSON plus its records once its NPCs and routines are here. Say plainly in the
report that Sedge is unprovable today.

---

## 8. Tests, and where they live

| Suite | Path | What Gate C adds |
|---|---|---|
| `dotnet test tests\Content.Tests` | `tests/Content.Tests/SocialContentTests.cs`, new `NpcLooksContentTests.cs` | the `appearance` block parses; its five rejections; slot `sex` |
| `dotnet test tests\Presentation.Tests` | new `NpcLooksTests.cs`; existing `LookSwatchesTests.cs`, `HumGenCreatorDataTests.cs`, `ResidentLooksTests.cs` | residents' uniqueness/stability/constraint; the cast's records survive `Constrain`; every named id is offered for its sex; the `ExactMotion` predicate |
| `dotnet test tests\World.Tests` | `tests/World.Tests/` | `NpcView.Appearance` and `NpcView.Generation` reach presentation, and a resident's generation changes its instance |
| `dotnet test tests\Architecture.Tests` | `tests/Architecture.Tests/ArchitectureTests.cs:20-30` | must stay green: no Godot type in Domain/Content/World — the reason the people check is a test and not a loader |
| `dotnet test tests\Domain.Tests` | `tests/Domain.Tests/` | `CharacterAppearance.Problem()` still refuses a bad record |
| content lint | `dotnet run --project src\Content -- lint -c content` | 672 definitions, 0 errors (the baseline the last lanes report, `CODEX_PROGRESS.md:7066`) |

The one project-reference decision to flag for review: the cast-record test needs *both* the content tree
and `humgen_creator.json`, i.e. `Content` and `Presentation.Ui.Models` in one test project. Adding
`<ProjectReference Include="..\..\src\Content\Content.csproj" />` to
`tests/Presentation.Tests/Presentation.Tests.csproj` is the cheapest way (Content is engine-free and has
no dependency on Presentation), and it keeps the people-rule assertion honest instead of hard-coding a
second copy of the ranges. If the reviewer prefers the reference graph untouched, the same assertions move
to `tests/Content.Tests` and read the catalogue with a small `System.Text.Json` reader that duplicates
`CreatorCatalogue.Read` — worse, because it duplicates.

---

## 9. Risks, and the decisions that are the owner's

1. **Sedge cannot be proved today** (§1.2). Deliver Ashen Hollow + Cairnwold, and report Sedge as blocked
   on content, not on looks.
2. **The MetaHuman bundle changes what is on screen.** With `mh_characters/catalog.json` installed every
   NPC is a `MetaHumanFigure` (`MetaHumanFigure.cs:22-23`, `NpcsView.cs:93`), so "NPCs are HumGen now"
   is a *figure-choice* decision, not only a data one (§5.3, Step 8). Nobody should be surprised by that
   line.
3. **The creator base body is one mesh per sex.** Twenty NPCs on two meshes means LOD cost is paid once
   per body, but it also means the body *silhouette* variety is blend-shape variety: broad Kal shoulders
   and a Siann's narrow frame come from `b_sliders` plus a uniform stature scale, not from different
   meshes. If a silhouette test at gameplay distance fails between two Veth, that is this design's
   ceiling, and the honest fix is another body bake, not more dials.
4. **Weapon-holding NPCs.** Tavar carries the March Spear as a companion, and guards hire out with one
   (`content/config/npc_life.yaml:34`). The hand socket on the creator body is `hand_r`
   (`ArtBindings.cs:439,658`), and `HeldWeapon` mounts through `person.Hand`; this needs one run's worth of
   checking in Step 8, and the grip frames in the bindings were authored for the MetaHuman hand, so a
   wobble is possible. Fallback: an empty `hand` on those people (`figure._hand` null → no weapon drawn)
   rather than a weapon floating off the palm.
5. **The creator's outfit range is modern.** After dyeing, `smart_casual` reads as frontier cloth, but
   there is no smith's apron, no hood, no cloak in the 142 parts. Kera's and Durn's forge gear will read
   as "work clothes", not as smithing kit. That is a modelling gap to name, not to fake.
6. **Owner decisions I am not making in this note:** (a) whether residents get authored `sex:` in the
   routines or a derived one; (b) whether the frontier tricorn is worn at all by NPCs (this plan says no,
   because it covers the crown and the face sheet is the proof); (c) whether Renn stays grey/clean-shaven
   and Durn stays bald, given both are art docs rather than canon; (d) whether the player, like the NPCs,
   should be drawn on the creator body in the world (today the player's presets and dials only render in
   the creator preview — `Main.Creator.cs:158` → `ApplyLook` on an `mh_veth_*` model, which the creator
   path refuses at `HumGenCreatorAppearance.cs:41` — so the player's own face is the same class of gap,
   and Gate C deliberately does not open it).
