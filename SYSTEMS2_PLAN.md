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
