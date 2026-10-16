# Open questions

Points the design leaves open, or where the sources disagree with what is actually on this machine.
Each records the smallest choice that keeps work moving, per the project brief. **None of these is a
request for a decision before work starts**; they are recorded so the choice is visible and reversible.

---

## OQ-01 — The asset depot drive letter has moved (documentation defect)

**Found:** `11_ASSET_REQUIREMENTS.md` gives every depot path under `F:\Otherreach_External_Assets`.
`F:` does not exist on this machine. The depot is at `C:\Otherreach_External_Assets`.

**Smallest choice:** resolve every depot asset by **filename against the current root**, not by drive
letter. Licence text is re-read at the file when imported regardless.

**Why it matters beyond a path fix:** `04_LEVEL_MAPS.md` and `12_SYSTEM_DEPENDENCIES.md` say the world
location and depot drive are deliberately unassigned pending owner rulings, so a stale drive letter in
`11` is expected drift rather than an error in the design. No design change follows from it.

---

## OQ-02 — `assets/` ownership tension

**Found:** the repository `AGENTS.md` states DeepSeek owns `assets/` and `tools/asset_pipeline/**` and
instructs that they are **never staged, edited or committed from a gameplay branch**. It also says
`assets/` is generated output written by another machine and should not be read for coding tasks.

The Barrowworks brief puts me on a feature branch (`feature/barrowworks`) doing both gameplay work (P1)
and art work (P3).

**Smallest choice:** keep all P3 art **out of git** — the depot under `C:\Otherreach_External_Assets`
and the `content/` bindings only. Do not stage `assets/`. If a committed art artefact turns out to be
required, raise it rather than resolving it unilaterally.

---

## OQ-03 — World placement of the two entrances (OD-05)

**From the design:** "Proposed local design; world location remains unassigned" (`03`). `12` D20 lists the
regional geography and new destination exterior as a **required owner placement decision, not a new
region-generator subsystem**, and OD-05 prevents unconditional geography. The external death-return
distance is unknown pending placement and must be measured before content lock.

**Smallest choice:** place the two gated entrance anchors relative to the existing Ashen Hollow hill and
the Sinking Moss drain mouth as the brief describes ("entered from the hill near Blackvein and from the
Sinking Moss drain mouth"), **inside `content/regions/` only as two placements**, and record the actual
coordinates here so the owner can move them without touching code. Do not generate new geography.

---

## OQ-04 — Interior anchors and the stale `anchor.world` shape

**Found:** `content/locations/*.yaml` carries `anchor: { world: [x, y, z] }` — plain exterior world
coordinates. The five Barrowworks locations are **interior**, so their anchors are meaningless without a
space identity.

**Smallest choice:** extend the location anchor to carry an optional space qualifier, defaulting to the
exterior space when absent, so **every existing location file keeps working unchanged**. This is the same
shape of decision P1 makes for runtime coordinates, applied to content.

---

## OQ-05 — Whether an interior location is discoverable like an exterior one

**From the design:** "No unexplored rooms or exact boss weakness appear as map pins" (`03`), and D15
requires space-aware journal and map prompts. But `content/locations/` also drives discovery
(`discovery.methods`), and R02's K1 is explicitly satisfiable "by walking in".

**Smallest choice:** interior locations carry discovery like exterior ones, **but the map pin is
suppressed while inside** and the journal names the room. This satisfies both statements without
inventing a new discovery mode. Recorded because it is a real design ambiguity, not a technical one.

---

## OQ-06 — Boss attempt-reset breadth

**From the design:** `12` D16 warns that the second E8.5 STOP concerns unsaved creature attack windup, and
instructs: "Preserve the resulting owner-approved ordinary-combat contract; **do not silently extend
S-35 boss reset to every fight**."

**Smallest choice:** the load-resets-to-R10 exception applies **only** to the Ring Custodian's active
attempt, identified by S-35's own state. Ordinary creatures keep their actual save contracts. No global
combat-reset flag is introduced.

---

## OQ-07 — Audio for the dungeon is requested, not implemented

**From the brief:** `src/Presentation/Audio/` and the audio manifests belong to a Claude audio agent on
`audio/recovery`. Dungeon audio must be **listed as sound ids in the handoff report** instead.

**Smallest choice:** no audio code or manifest edits. P6 carries a table of needed sound ids, each with
the room and trigger that needs it, and whether an existing accepted event could serve.

---

## OQ-08 — Greybox versus production for the boss and shell

**From the design:** AR01 is "GREYBOX ACCEPTABLE FOR PROOF → NEW PRODUCTION ASSET for final kit"; AR13's
boss body is "GREYBOX ACCEPTABLE FOR PROOF"; and `11` says that if production review remains pending,
**mark visual acceptance BLOCKED rather than calling the gameplay prototype a finished milestone**.

**Smallest choice:** greybox both, and state BLOCKED for final visual acceptance in the handoff report.
Do not describe greybox geometry as finished art.

---

## OQ-10 — The space is runtime state only; persisting it needs its own pass

**What was attempted:** the space and the portal the character came in by were wired all the way through
persistence — `PlayerRecord` fields, the player DTO, a schema 14 -> 15 bump, and a `SchemaV14ToV15` step
that gives older saves the exterior and no traversal.

**Why it was reverted:** `tests/Persistence.Tests/Fixtures/README.md` requires a schema bump to land as one
complete unit — a new `v15/` fixture written by the new writer through M2.Probe, the previous current
section shapes frozen as `Sections/V14` with the step that produces 14 repointed at them, and every
`expected.json` whose current state gained a field updated — and it warns explicitly never to change an
`expected.json` to make a failing migration pass. That is a focused piece of work, not a tail-end addition
to another task. Bumping the format would also change the save for the existing playtest, which the
brief's default-off rule protects.

**Why it is safe to defer:** the state is unreachable. No region declares a portal, so
`EnterSpaceCommand` can never succeed in a shipped run, and there is nothing to persist.
`SpaceTransitionTests.TheSpace_IsNotPersistedYet_SoItMustStayUnreachable` asserts exactly that, so the
deferral cannot rot silently.

**The hazard this creates, stated plainly:** a save made inside a space would load with the body at
interior-local coordinates while the simulation believes it is in the exterior — the same collision the
space identity exists to prevent, in reverse.

**Smallest choice:** do not declare a portal in content until the schema bump lands in the same change.
The test above fails the moment one is added without it.

**What stands:** the whole space model and the `SpaceSystem` are intact and green —
`SpaceId`, `CellKey.Space`, `OfInterior`, space-carrying sites, portals with distinct return anchors, and
enter/exit through the running simulation. Only the persistence of it is deferred.

**For the eventual pass:** `RuntimeState.Space` and `.Traversal` are the transient source; the live
`PlayerRecord` is where the two persisted fields belong; `SectionCodec.EncodePlayer`/its loader are the
two wiring points, both already written once and easy to restore.
