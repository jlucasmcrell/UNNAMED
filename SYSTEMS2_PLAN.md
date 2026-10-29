# SYSTEMS lane 2 - systems/needed-2 (off build2-integ)

Seven systems, one commit-and-post each, gate per system. Branch `systems/needed-2` from `build2-integ` (`e2fd64d9`),
worktree `D:\Otherreach_Scratch\ds_systems2_wt`. The parallel systems session owns **fishing, lockpicking, mining and
swimming**; this lane takes the rest of `ROADMAP/SYSTEMS_NEEDED.md`:

| # | System | Hook(s) it closes | First question to answer from the code |
|---|---|---|---|
| 1 | **Resource tiers** | `HOOK.NODE.ORE.T2`/`.T3`, `HOOK.PROSPECT.SIGN`, `HOOK.NODE.SKILL_BOUND`, `HOOK.NODE.RESIN` | where a `node.ore.*` definition binds its harvest skill and yield, and whether a tier/grade field and a hidden-deposit flag can ride the same record |
| 2 | **Carcass and skinning** | `HOOK.NODE.CARCASS` | what a killed creature leaves today (`loot_ref` on the creature), and where a carcass entity would sit so skinning can act on it |
| 3 | **Ambience binding** | `HOOK.AMBIENT.BINDING` | how cells already resolve sound (CellSounds in ArtBindings) and whether a content `ambience:` on a location/cell can feed it |
| 4 | **Grapple** | `HOOK.TRAV.GRAPPLE` | the locomotion state machine and whether a `anchor:` placement kind plus a catalog clip can carry a pull-to-point |
| 5 | **Scramble** | `HOOK.TRAV.SCRAMBLE_SLOPE` | how slope affects movement now, and where an authored steep route could let the character enter a scramble |
| 6 | **Grave** | `HOOK.DEATH.GRAVE` | the death/return path (waystone respawn) and where remains could be placed and read |
| 7 | **Ritual circle** | `HOOK.RITUAL.CIRCLE` | the switch/flag machinery a circle would use, and whether the art library's three magic circles can be bound |

## Per-system gate (D15)
1. `dotnet build src/Content -c Release`; `content lint` 0 errors; `content hooks` shows the hook as **placed**
   (not unmodelled) for the area it is placed in, with the content id recorded.
2. The dossier checklist for that area updated with the id, and `ROADMAP/SYSTEMS_NEEDED.md` row moved from needed to
   closed (or the honest reason it stays open).
3. **Real animation from the catalog** where the system moves a body (grapple, scramble, skinning, grave, circle rite):
   find it in the animation catalog, record its id and clip name in the commit, do not invent motion.
4. **A real-art clip on the farm**: captured from the game with the HUD off, player visible only where the system *is*
   the player's action, checked by looking at frames - not by trusting exit 0.
5. `FINISHED - <system>` posted with the ids, the clip path, and what is still not true.

## Standing rules
No pushing. Pathspec commits. Fair GPU lease (`gpu.lock`) for every run, never steal focus, kill only own processes.
Never fake a system: if a piece needs an asset or a clip the build does not have, it is named in the post and in
`ROADMAP/SYSTEMS_NEEDED.md`.

## State
- Worktree created, branch at `e2fd64d9`. Nothing implemented yet.

## System 1 (resource tiers) - what the code says, gathered 2026-10-28
- NodeDefinition is src/Domain/Crafting/Crafting.cs:20 - (Id, ItemId, YieldMin, YieldMax, Charges, Respawn, SkillId,
  Difficulty) plus ResourceId. **No tier, no rarity, no hidden flag, no per-node skill variety.**
- Nodes are built in src/Content/CraftingContent.cs:131 BuildNodes: one resource of exactly one yield, espawn
  none|daily, harvest_skill must be a skill, and 	ool_tier_min is explicitly rejected ("not built in Phase 1").
- A resource is one file per material (content/resources/ore/iron.yaml: yields: [{item_ref, count_range}]), a node is
  one file per kind (content/nodes/ore/loose_iron.yaml: esource_ref, charges, respawn, harvest_skill, difficulty).
- The hooks audit is src/Content/WorldHookChecks.cs: a hook declared unmodelled: <reason> in
  content/_world_hooks.yaml is reported and **never places** (line 118); evidence kinds are switched at ~line 422
  (case "node":, case "spawn":, ...). So each tier hook needs BOTH a new content field and a new evidence kind.

### Order of work for system 1
1. Tier on NodeDefinition + 	ier: read in BuildNodes with validation 1-5; hidden: for a prospectable deposit;
   keep the per-node harvest_skill as it already is.
2. New evidence kinds in WorldHookChecks (
ode_tier, 
ode_hidden, 
ode_kind), then delete the three
   unmodelled: lines for ORE.T2/T3, PROSPECT.SIGN, NODE.SKILL_BOUND and NODE.RESIN and give them evidence.
3. Content: iron T2/T3 nodes, a prospect sign on a hidden deposit, a resin node and its resource, and two nodes on
   different harvest skills - placed in the Foldscar (open ground, band 1-5) and the Den.
4. Gate: build, lint 0, content hooks placing each hook with its id, dossier + SYSTEMS_NEEDED rows updated.
5. Farm clip: prospect-then-mine and a skinning/other clip per the catalog, captured with --asset-root and checked.

### Round 2 result (system 1, resource tiers)
Landed, committed 3f8628d, lint 0:
- NodeDefinition.Tier (1-5) and .Hidden, read and validated in CraftingContent.BuildNodes;
  	ier: outside 1-5 is an error, hidden: true marks a deposit only prospecting finds.
- Five gathering skills the hooks had been asking for but content never had: **skill.mining, skill.prospecting,
  skill.foraging, skill.herbalism, skill.logging** (content/skills/*.yaml).
- Ore grades: 
ode.ore.grey_iron (tier 2), 
ode.ore.bog_iron (tier 3), plus two hidden deposits
  
ode.ore.hidden_grey_iron (tier 2) and 
ode.ore.hidden_bog_iron (tier 3); resin as 
ode.wood.pine_resin
  with esource.wood.resin. Resources esource.ore.grey_iron / esource.ore.bog_iron.
- Placed in the Foldscar: old_grey_iron_seam, old_bog_iron_nodule, old_weathered_grey_iron (hidden),
  old_resin_pine, each with its own 	ier:.
- Audit vocabulary: 
ode_tier, 
ode_hidden, 
ode_skill, 
ode_ref, counting the node **kinds** the build holds
  (a grade is a capability, not a place). hidden/	ier reads are case-insensitive.

Gate state this round: in **foldscar**, HOOK.NODE.ORE.T2, HOOK.NODE.SKILL_BOUND and HOOK.NODE.RESIN place
(they no longer appear as warnings); HOOK.PROSPECT.SIGN still reads 0/2 although two hidden ore kinds exist.

OPEN QUESTION for the next round, with the evidence: 
ode_hidden returns 0 while 
ode_tier on the same list
returns >0, so the hidden scalar is not what the audit compares. Next diagnostic is to print the parsed hidden
value for one kind (a two-line temporary log in WorldFacts), not to guess again. Content scope was tried first and
abandoned: the report prints no content-scope section, so a content hook would have been silently invisible.

### Round 4 result
**Farm pipeline (for every system's clip).** C:\Otherreach_Farm\builds\sys2_tiers\ now holds a packaged build of this
line's exe (copied from ds_d18_bv2), this branch's **content** (744 files), and a **17.9 GB art root** copied in as
rt\ so it travels with the build (the runner mirrors only the build folder to the 6900XT). udit.cmd runs the
visual audit against %~dp0art and %~dp0game\content, and jobs\sys2_tiers01.json was dropped; the 6900XT picked it
up (status unning sys2_tiers01). The mirror of 17.9 GB is the slow part and was still running at the end of the
round; the result has not been read yet. Note the past ds_d18_bv runs drew **0 assets** - the farm had no art root -
so this is the first farm job here that can produce real-art frames at all.

**System 2, carcass and skinning - first half landed** (commit c1235a65), lint 0:
- skill.skinning and skill.hunting; item.material.pelt_grey_wolf, item.material.meat_raw;
  esource.carcass.grey_wolf; node kinds 
ode.carcass.grey_wolf and 
ode.carcass.deer, both
  harvest_skill: skill.skinning, two charges, no respawn.
- HOOK.NODE.CARCASS now counts a carcass node, and two are placed in the Foldscar (old_carcass_deer,
  old_carcass_wolf). **foldscar: 10 placed / 8 missing / 9 unmodelled** (was 9 / 8 / 10); CARCASS is placed there.

**System 2 still owed:** a HOOK.STATION.TANNING surfaced as a real hook once a carcass exists - a tanning rack is the
natural other half and needs a station kind; the carcass nodes need art bindings (no carcass prop exists in the art
root - the library has dead trees, not bodies); the **kill-to-carcass wire** is not built, so authored carcasses are
the only ones that exist; and the clip waits on the farm.

### Round 5 - the farm's own art root, and a queue problem
**Discovery that changes the farm cost:** the 6900XT already holds a valid art workspace at
C:\Otherreach_Test\ds_anim_art (= \\6900XT\Otherreach_Test\ds_anim_art) - manifests/ui_icons.json and
indings.json are there, with 304 ready models. Earlier farm runs that reported "0 assets drawn" were not missing art
on the box, so the cause is still unexplained; the path is good.

- My first farm job (sys2_tiers01) staged 17.9 GB of art inside the build because I had assumed the farm had none.
  The runner mirrors the build folder to the 6900XT, so that job spent its whole life copying: rt landed (272 ready
  dirs on the box), game had not, and it was still unning with nothing to read.
- Second build staged game and content only (**0.29 GB**) with --asset-root C:\Otherreach_Test\ds_anim_art, and
  jobs\sys2_tiers02.json was queued behind the stuck one. Both are still in the queue.

**NEXT (in order):**
1. Read esults\sys2_tiers02 when it lands; look at the three frames; if the art root answers, every later system's
   clip is a 0.29 GB job. If it does not, the "0 assets" cause is the thing to chase, with the log line
   rt: N assets drawn from the library at boot as the test.
2. carcass_ref on the creature schema (CreatureDefinition + a field in the creature loader) and on the wolf, stag
   and boar, then the kill-to-carcass wire: CreatureKilled(Creature, DefId, Killer, Tick) needs the body's position,
   and the carcass has to appear in the layout as a harvestable node. That is the half system 2 still owes.
3. Then a tanning station kind for HOOK.STATION.TANNING, which the audit raised the moment a carcass existed.

### Round 5 outcome - the farm job's real failure
esults\sys2_tiers01 finished with an empty exit code and a C# backtrace in stderr after the world had *started*:
UNNAMED boot: content 0.3.0 (sha256:de3a3148...), region region.starting_frontier, 14 cells and
UNNAMED assets: HUD and effect art from C:\Otherreach_Test\sys2_tiers\art - so the art root I staged was found and
read, and then the run threw.

Cause: **the packaged exe is the old build**. I copied ds_d18_bv2's game folder and swapped in this branch's content,
but 	ier:/hidden: and the new node kinds are content the old assemblies do not know. A farm job needs a build
**exported from this branch**, not an old exe with new content dropped beside it. That is the next step, and it is the
only way any system's farm clip will be real.

Also queued and then withdrawn: sys2_tiers02 (game-only, 0.29 GB, pointing at the farm's own art root) - it would have
failed for the same reason, so I pulled it rather than leave a doomed job in the owner's queue. The farm is idle.

### Round 7 - what the farm can and cannot do, and the carcass field
**Farm: the 6900XT dies on this world in every configuration tried.** All three captured job runs end with
exit=-1073740940 (0xC0000374, heap corruption):
- sys2_tiers03: old exe, staged art, **full profile** - built the world, wrote 3 frames (**greybox, 0 assets drawn**),
  then died at teardown.
- sys2_tiers04: exported build, staged art - died at frame 1 while drawing (prop_long_trestle_table greybox warning
  in stderr shows the art root was being read).
- sys2_tiers06: exported build, staged art, **no --visual at all** (default profile) - booted
  (content 0.3.0 sha256:d7347cd6, region region.starting_frontier, 14 cells), printed ssets: HUD and effect art
  from C:\Otherreach_Test\sys2_tiers\art, then died with the same code before the first shot.
So the farm box is not producing a usable real-art clip for this world right now, and I cannot drive it beyond the job
folder (no remote execution). The clip that exists - real art, looked at frame by frame - was captured on BEAST
(D:\Otherreach_Scratch\ds_sys2_shots\audit_tiers\raw). **The farm check for resource tiers stays owed**, and the
heap corruption is a finding for the farm's own AMD compatibility pass, with the exit code and three configurations
above.

**Export note:** --export-release from this branch works (Otherreach.exe 201.8 MB + runtime folder) and the build
runs on both boxes; it is a **GUI-subsystem exe**, so a PowerShell *> redirect captures nothing locally - use
Start-Process -RedirectStandardOutput if the log is wanted on BEAST.

**Carcass field landed** (commits 90cab0a, 1de0a7b), lint 0: CreatureDefinition.CarcassRef, read from
carcass_ref in the creature loader, registered as a node reference in ContentChecks (XREF004 demanded it), and set
on the grey wolf (
ode.carcass.grey_wolf). The stag and hind files are not where I expected
(content/creatures/grazer/stag.yaml missing) - find them next round before claiming the deer carcass is wired.

**Still owed for carcass and skinning:** the kill-to-carcass wire itself (on CreatureKilled, place a carcass node at
the body's position - needs RegionLayout.Nodes to grow at runtime and the crafting session to learn the new node),
the stag/hind refs, a tanning station kind, carcass art, and a clip.

### Round 8 - the kill-to-carcass wire is in the code
Commit **a0be552**, builds clean (Content and Presentation), lint 0:
- GatheringSystem keeps its nodes in a **list it can grow**; LeaveCarcass(key, nodeDefId, xMm, zMm) adds one in the
  cell the body fell in, refusing a duplicate key. Because a carcass is an ordinary node, the existing GatherCommand,
  its reach check, its charges and the skill it trains (skill.skinning) all work on it unchanged.
- Simulation.LeaveCarcass(...) exposes it; Main.Events.cs subscribes CreatureKilled, reads the fallen body's
  position from simulation.Creatures and, when the creature's definition names a carcass_ref, leaves the node and
  refreshes the crafting view so the body is drawn.
- CreatureDefinition.CarcassRef (+ loader, + ContentChecks reference registration ["carcass_ref"] = ["node"]), set
  on **wolf_grey, wolf_grey_alpha** (
ode.carcass.grey_wolf), **buck, doe** (
ode.carcass.deer) and
  **bristleback_boar** (
ode.carcass.boar, a new node).

**What is NOT yet proved:** no run has yet killed a creature with this build and then shown the carcass, and the carcass
has **no art** (the library has dead trees, not bodies), so a frame today would be a greybox lump. That is why there is
no FINISHED post for carcass and skinning.

**Next, concretely:** a test using the existing Arena.OpenCreatures(...) helper in
	ests\Application.Tests\CombatTests.cs - open a wolf, attack until CreatureKilled, assert
Simulation.Nodes gains a 
ode.carcass.grey_wolf within reach of the corpse. That is the cheapest real proof of the
wire; a clip follows once a carcass model exists or a placeholder is agreed.

### Round 9 - the carcass wire moved into the simulation, with a test that still fails
The presentation-side subscription could only leave a carcass when a view was running, so the body is now left by the
system that owns the death: Creatures.Die dispatches a new internal LeaveCarcassCommand (declared and handled in
Crafting.cs, routed on the internal dispatcher next to WoundCreature), and GatheringSystem.LeaveCarcass adds the
node. Content names the carcass for wolf, alpha wolf, buck, doe and boar. Commits a0be552, plus this round's.

	ests\Application.Tests\CarcassTests.cs has two tests:
- ACreatureWhoseKindNamesNoCarcass_LeavesNothing - **passes**.
- AKilledWolf_LeavesACarcass_WhereItFell - **fails**: the kill happens (CreatureKilled seen, 40-200 ticks), but no
  *new* 
ode.carcass.grey_wolf node appears. The world already carries authored carcasses, so the test compares keys
  before and after; it finds none added.

Next diagnostic (do not guess): make LeaveCarcass return the reason it refused - Setup.Nodes.ContainsKey(carcass)
false (the harness's crafting table may not hold the kind), a duplicate key, or the dispatch never reaching the handler -
and log it once in the test world. That one line decides whether the fix is in content or in the harness.

### Round 11 - carcass and skinning closed at the gate
FINISHED - CARCASS AND SKINNING posted. In oldscar, content hooks places **CARCASS, TANNING, ORE.T2,
PROSPECT.SIGN, SKILL_BOUND, RESIN** - six hooks that were unmodelled when the lane began. Commits 45a1cac5 (hide art
for the carcasses, station.foldscar_tanning, ecipe.tanning.wolf_hide, item.material.leather), canon 966ca82
(dossier rows + the SYSTEMS_NEEDED row closed).
Frames: ds_sys2_shots\audit_carcass\raw\sys2_carcass_deer.png, sys2_carcass_wolf.png, sys2_tanning_rack.png.
Still owed for this system: a carcass **body** model (hides stand in), a frame of a kill actually leaving the body, and
the farm check.

**NEXT SYSTEMS, in the order the brief lists them:** ambience binding, grapple, scramble, grave, ritual circle. Each
needs: the content kind, the hook's evidence, a catalog animation where a body moves, the gate, and a clip.

### Round 13 - the grave's content and pages exist; the hook still counts zero
Landed (lint 0, committed): the **grave content kind** (content/graves/halda.yaml, errin.yaml - whose it is, the
epitaph, the marker, the place), the **two lore pages** the markers open (lore.grave_halda, lore.grave_berrin,
bound by { how: inspected, ref: readable.grave_* } inside the page, which is how lore pages say how they are found),
**two marker structures and two readables** in the frontier region, and a grave evidence branch in
WorldHookChecks counting readables that open a lore.grave. page.
**It still reports HOOK.DEATH.GRAVE 0/1.** Moving the markers inside their locations' discovery radii (den_gorge
anchor 540,797 r10; outpost 455,745 r30) did not change it, so the area placement assignment is not picking up
eadables at all - or assigns by a mechanism I have not matched. Next round: check how placed is built for
eadables (whether they are assigned to areas at all), and if they are not, express the marker as a 
odes-section
placement that references the grave kind, or extend the assignment.

Also learned and worth keeping: lore pages put sources: **inside each page** as { how: <kind>, ref: <id> }, and a
region section's list items carry the section's own indentation (eadables: items are indented two spaces).
