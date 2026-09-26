# Phase B remediation <-> M7: input and HUD coordination

Date 2026-09-26. Phase B does **not** change M7 or any gameplay-facing contract. This note hands M7 (the input owner) one UX
request and lists the files both lines touch, so a later merge is planned rather than discovered. M7's in-progress source is
treated as non-canonical until it lands on its branch and passes its slice gate; nothing here was read from the M7 worktree -
only the pushed branch `origin/claude/m7-factions-building` (docs) and `origin/main`.

## 1. The request: movement dismisses non-critical panels

Owner observation (the rejected build): with the inventory open, pressing a movement key does nothing until Tab closes it. The
Codex audit (M05) confirms this is inherited Phase-A behaviour, not a Phase-B regression: `Main.Modal` treats the inventory,
dialogue, character and saves panels as modal, and `ReadInput` sends zero movement and returns before movement is read
(`src/Presentation/Main.cs`, `Modal` and `ReadInput`; line numbers move - search for them).

Desired UX (owner): a movement key closes the **non-critical** panels (inventory, character sheet, journal) and the movement goes
through in the same frame; **critical** panels (a conversation, the saves list, a death recap, a confirmation) keep blocking.

Why M7 owns it: it is input semantics in `ReadInput`, and M7 is changing adjacent input (build mode, `Main.Modal`'s list - M7
status E5 notes F1 and build mode against the same modal set). Making the change concurrently in Phase B would collide.

The pushed M7 documents contain no rule numbered R15 (searched `origin/main` and the M7 branch on 2026-09-26); M7's
`HUD_INPUT_AND_ACTIONS.md` section 8 (minimal persistent HUD, configurable display) is compatible with the request. If R15 lives in
an unpushed M7 document, this request composes with it through the classification above (critical versus non-critical).

Suggested acceptance (for M7): with each non-critical panel open, one movement key press closes it and moves the body that frame
(an input-check step); with a conversation open, movement still does nothing.

## 2. Files both lines touch (merge planning)

| File | Phase B remediation change | Likely M7 overlap |
|---|---|---|
| `src/Presentation/Main.cs` | frame-state log (`--state-log`), the player's stride speed at tick wraps (H04), the NPC draw call's tick length, showcase harness (no HUD call changed) | `Modal`, `ReadInput`, build mode, HUD calls |
| `src/Presentation/Ui/Hud.cs` | a candidate visual style under the visual option `hud=production` (classic unchanged): the same elements and information restyled; what is shown and when stays FE-2's | any M7 HUD element (faction/reputation display) |
| `src/Presentation/Greybox/NpcsView.cs` | `GaitSpeed` sampling, the fold echo | M7 NPC routines (navigation) |
| `src/Presentation/VisualOptions.cs` | the `hud` option | none expected |

No save data, simulation state, command or content schema is touched by Phase B.
