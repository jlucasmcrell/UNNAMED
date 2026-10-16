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

---

## OQ-11 — The two entrances are 610 m apart, because that is where the canon features are

**What was asked:** place the two entrances "relative to the Ashen Hollow hill and Sinking Moss drain
mouth, inside content/regions/ as two placements only, and record coordinates so the owner can move them".

**What was placed**, at the canon features those words name:

| Entrance | Exterior anchor | Canon feature | Interior anchor |
|---|---|---|---|
| A `portal.barrowworks.threshold` | (438, 663) | Blackvein's spoil heaps, `blackvein_spoil_1`/`_2` at (440,662) and (436,665) | R01 centre (50, 10) |
| B `portal.barrowworks.drain` | (1050, 770) | The Sinking Moss, `location.cairnwold.the_bog` anchored at (1050, 770) | R04 centre (8, 38) |

**The consequence, stated rather than buried:** those two places are about **610 m apart**, and the interior
is about 137 m from R01 to R04. So walking in at A and out at B is 137 m of dungeon against 610 m of
overland - the interior becomes a *shortcut between two distant places* rather than the two ways into one
dungeon that `04_LEVEL_MAPS.md` describes. Entrance B exists to be an alternate way in, not a route across
the world.

**Smallest unblocking choice:** leave the placements as instructed and let the owner move B. Nothing depends
on the separation being small: both entrances are gated, both round-trip to their own anchor, and
`BarrowworksEntranceTests.TheTwoEntrances_AreNotAtTheSamePlace` records the distance so the change is
visible rather than silent.

**If B should move**, the smallest fix is one pair of numbers in `content/regions/starting_frontier.yaml`
under `portals:`. The interior anchor does not change; only `exterior_m` does.

---

## OQ-12 - A fresh worktree cannot render until two gitignored directories are populated

**Found the hard way.** Every render taken in this worktree was black - the game's own starting region as
much as the Barrowworks - and the cause was not the code at all:

| Directory | gitignore | this worktree | needed |
|---|---|---|---|
| `src/Presentation/.godot/` | yes | **absent** | 2,269 imported assets |
| `src/Presentation/addons/` | yes | 3 of 23 | the rest |

Godot does not draw unimported assets, so a fresh worktree of this project has **no world at all** until:

```
godot --headless --path src/Presentation --import
robocopy C:\Otherreach_Recovery\UNNAMED\src\Presentation\addons src\Presentation\addons /E
```

**Not a code problem, and not recorded anywhere.** The plan's P2e section carries the measurements. Anyone
who checks this branch out fresh and runs the survey will otherwise conclude, as I did, that the dungeon is
broken.

**Caution:** the import pass rewrites tracked `*.import` files under `Art/` and `Environment/`. Those are
Godot's own metadata and the rewrite is spurious; restore them (`git checkout -- src/Presentation/Art
src/Presentation/Environment`) so they never reach a commit.

---

## OQ-13 - A new drive `D:` exists, empty, with depot-shaped folders on it

After the 16 Oct drive install, `D:` is present with **1,862.9 GB free and 0.1 GB used**, carrying three
**empty** folders: `Otherreach_Depot`, `Otherreach_Audio_Library`, `Otherreach_Scratch`.

The depot is still `C:\Otherreach_External_Assets` and every path in this plan still resolves there.

**Why it is a question and not an assumption:** if the owner means to move the depot to `D:`, then the art
root, the material paths and the launch command in §P3a all change - and a drive letter moving is the exact
defect §3.1 already had to work around once. Nothing is blocked today. Asked rather than guessed.

---

## OQ-14 - The quest's craft step is at the waystation's anvil, which is outside the hill

`unfinished_measure.yaml` objective `o_wedge` reads: *"Fit a wedge to the measure **at the waystation's
anvil**."*

Every other step in the fourteen is inside the service ring. The waystation is an Ashen Hollow place, outside
the hill and some distance from either entrance.

Two readings, and the owner's to pick:
- the anvil is meant to be somewhere in the Barrowworks, and the source text is stale; or
- the quest deliberately sends a character back out mid-dungeon, which is a real design beat (it makes the
  reading and the making two separate journeys).

**Not guessed at**, because it changes what a player does and where they go. Ported as written until the owner
says otherwise, so the behaviour matches the design document rather than my reading of it.

---

## OQ-15 - The brief asks the Ring Custodian for three telegraphed attacks; the engine builds two

The brief's P5 says: "Ring Custodian (L5, 150 HP, **3 telegraphed attacks**, rear weak point)."

`CombatContent` builds **at most a blow and a charge** per creature, and says so in two places: a second attack
that is not a charge is refused (*"a second attack is a charge (Phase 1 builds one blow and one charge)"*), and
a third is refused outright (*"Phase 1 builds at most a blow and a charge"*). The animated armour has one
attack; the boar has a blow and a charge; **nothing in the content has three.**

**Picked the smallest unblocking choice:** the Custodian has the two the engine builds, and **both telegraph** -
a 1.1 s windup on the blow and a 1.2 s on the charge, which is what the brief is really asking for (a fight a
player reads rather than reacts to). The third attack is not in the content.

**The owner's call, when they want it:** either the brief's "three" becomes "two, both telegraphed" in the
design, or the combat system gains a third attack slot - which is a change to shared combat, not to the
Barrowworks, and outside this brief's scope. Deleting the ability I had written (`index_grasp`) is the honest
state in the meantime; keeping an ability nothing can use would be dead content.

---

## OQ-16 - The design has the fitting wedge open G3, and neither a door nor a switch can require an item

The design chains **o_wedge -> o_open**: a character fits a wedge to the measure at the waystation's anvil, and
then opens the headworks door (G3) beyond the measuring court. **The quest orders this; the world does not enforce
it.**

**G3 is a door, and a door opens by being used.** `door.longhouse` in the reference regions has no switch
anywhere, and the flag it names is what the door itself records when it swings. A switch cannot stand in for that,
because **WLD013 refuses a switch and a door that share a world flag** - which is right: two interactables writing
one flag is two answers to one question.

The one thing that would express the design is **an item requirement on a door**, which the engine does not have.
A switch's `requires` names **other world flags**, read in that switch's own cell - not items. So G3 opens like
any other door, and nothing stops a character who never crafted the wedge.

**Smallest unblocking choice taken:** the design is ported as written - the quest still asks for the wedge before
it asks for the door - and the missing enforcement is **recorded rather than approximated** with a flag chain the
design never describes. Claude's plan section 2.11 already lists "the wedge opening G3" as remaining W7 work, so
this is the same gap reached from the other end.

**Needed to close it:** either an `item_ref` on `DoorSite` (Content, plus the door interaction), or a switch a
door reads without sharing its flag. Both are shared-world changes rather than Barrowworks content, which is why
they are not made here.
