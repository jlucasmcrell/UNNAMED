# Cloud Claude: wave 3 (systems completeness)

Wave 2 is integrated (snapshot and cloud base at ae95d128, schema v22); sessions 1-4 can start now. Session 5 (WP13) waits for WP12 to merge.

- **Repo:** `jlucasmcrell/otherreach-RESTORE-20261014`
- **Base branch:** `cloud/base-20261017`
- Sessions 1–4 can run in parallel. **Session 5 (WP13) starts only after WP12 is merged.**

## Shared header for every prompt

Paste this at the top of each prompt below:

> Repo jlucasmcrell/otherreach-RESTORE-20261014, base branch cloud/base-20261017.
>
> Read first:
> - AGENTS.md
> - docs/plans/SYSTEMS_COMPLETENESS_PLAN_20261017.md, in full for §5.2 and the §6 header rules
> - docs/plans/README.md
> - the latest docs/plans and the integration notes in the PR history
>
> Owner MMO rules:
> - no pause;
> - no difficulty presets;
> - no quickload;
> - the world keeps living offline;
> - no network requests;
> - never read the wall clock outside Application/GameSession.cs;
> - no new lore.
>
> Persistence edits go in ONE final commit using the next free schema version after the current `SaveModel.SchemaVersion`; the integrator renumbers. Every new world system implements `IAbstractAdvance` with a chunk-invariance test. Build, all tests, and the content lint must pass. Open a PR into cloud/base-20261017 with open questions listed.

---

## 1. WP12: NPC life simulation, simulation part

This package moved from DeepSeek to cloud; DeepSeek is on the Barrowworks dungeon.

> [shared header] Implement WP12's simulation per §2 (NPC life) and the WP12 brief in §6. The Presentation/animation parts are left for local Claude.
>
> - **Port the m7 navigation stack.** Port by hand from `origin/claude/m7-factions-building` on github jlucasmcrell/UNNAMED; fetch it as a second remote. That covers the `Nav*` grid, A* planner, route follower, edit checks, door opening for NPCs and the "never teleport" mover.
> - **Build the tier B/C/D framework**, reusing the existing tier selection, with these parts:
>   - NPC needs;
>   - routines and homes;
>   - work anchors and jobs;
>   - arrivals and replacement (DN4: the 8 named NPCs never die; others are replaced 3–7 game days after leaving);
>   - errands;
>   - NPC–NPC pairs;
>   - life states.
> - **Offline catch-up** through `IAbstractAdvance` at the tier C/D level.
> - **Populations have NO hard cap** anywhere in data, schema or code. Add the cheap structural test: 1000 synthetic off-screen NPCs survive save/load, catch-up and tier changes without anything refusing or truncating them.
> - **Current casts stay modest:** the 8 named NPCs plus 6 role residents per town (DN3). Use the WP2 Suggest name source for residents.
> - **Merchants** keep trading hours 06:00–20:00. Quest givers stay talkable.
> - **Trainers** teach at their work anchor during work hours. Expose this for the WP9 trainers session.
>
> You own `World/Runtime/Npcs.cs` and the new NPC-life files. Don't edit `Dialogue.cs` or `Trade.cs` except through narrow hooks.

## 2. WP3b: durability, repair, laden band

> [shared header] Implement WP3 part 3b per §2.4 and the WP3 brief:
> - **Durability (D5):** weapons, armour and tools only. Wear comes from Hit and Gather **events**; never edit `Combat.cs`. A worn-out item counts as crude quality and is never destroyed.
> - **Repair:** station repair, gated by skill with a capped quality, and a merchant repair service in `World/Runtime/Trade.cs`. Coordinate narrowly: WP14 now owns markets and purses there.
> - **Laden band (D4):** 80–100% load costs extra stamina; keep the hard refusal at 100%.
> - **Quick-bar slots** are owned by local WP9, which is doing its own persistence. Don't add them.

## 3. WP15b: companion approval, banter, progression, gear

> [shared header] Implement WP15 part b per §2 (companions and hirelings) and the WP15 brief, building on the WP15a code now merged:
> - Approval across several dimensions, as canon describes, reacting to deeds. WP5's deed condition is merged.
> - Banter lines (hooks plus a few lines per existing companion, in their established voices).
> - Companion progression and levelling.
> - Companion equipment slots.
> - Loyalty.
> - Personal-quest hooks: the framework only, with no new lore.
> - Offline behaviour: dismissed companions go on with their lives in the NPC life sim. Until WP12 merges, put this behind an interface.
>
> Romance stays out (DN10). The party UI is local Claude's.

## 4. WP9 (cloud part): trainers and techniques

> [shared header] Implement WP9's cloud part per §2.3 and the WP9 brief.
>
> **Trainers:**
> - A `teaches:` block in NPC data: kind (recipe, technique, spell, building piece), id, requirements (skill band, trust, prerequisite, deed or flag), and a coin fee as an economy sink.
> - One generic `teach` effect replaces `give_recipe`. Migrate the existing dialogue with identical outcomes.
> - Teachers teach only at their work anchor in work hours. Use the WP12 hook if it's merged; otherwise add a TODO behind an interface.
> - A lint that every recipe, technique, spell and building piece has a findable source.
> - **Content:** only the current teachers (Kera, Durn, Tavar, Sel, Tam), plus **Berrin teaching the building pieces** (owner decision).
> - Canon: no fee or teacher ever raises a skill.
>
> **Techniques (D11):** one active technique per weapon family, taught by NPCs.
>
> The Learn-column UI is local Claude's.

## 5. WP13: NPC adventurers (start only after WP12 merges)

> [shared header] Implement WP13 per §2 (adventurers) and the WP13 brief, on top of the merged WP12 NPC life sim, WP14 economy and WP15a party code:
> - 8 adventurers in the region (DN5: a target, not a cap).
> - **Their lives:** they take bounties competitively via WP4's `competitive: true` (first claimant wins), hunt, travel, trade at markets, buy gear and skill up.
> - **Death and retirement.** DN2 canon amendment: off-screen outcomes are resolved by fixed rule tables, never simulated combat.
> - **Hiring:** they're hireable through WP15a and keep their level and gear when hired.
> - **Offline catch-up** through `IAbstractAdvance` with a chunk-invariance test.
