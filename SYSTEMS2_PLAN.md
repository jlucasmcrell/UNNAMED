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
