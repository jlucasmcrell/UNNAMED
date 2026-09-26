# Otherreach M7 - Implementation Design

**Milestone:** M7 - Factions, Reputation, and Building v1 (Phase 2)
**Date:** 2026-09-24
**Status:** Proposed execution design. Design only: no code, content, asset or repository change has been made. M7 is **not authorized**, and nothing here starts it.
**Code base analysed:** `origin/main` at `e10d2c4` ("Merge pull request #5 ... phase1/closeout-followup"). The local `main` in `G:\UNNAMED` points at `dbb877a`, which is `e10d2c4` plus seven asset-audit commits (one document and seven `tools/asset_pipeline/_*.py` scripts) that do not touch anything M7 depends on. All `path:line` citations refer to `e10d2c4`.
**Author role:** M7 design lead. Research, alternative designs, adversarial critiques and audits were run as independent passes. Their working papers are kept beside this file.

## How to use this document

- This document is **normative for the M7 implementation**. It settles the architecture questions, so the implementation prompt only has to point at it. Where it disagrees with a working paper, this document wins.
- Five questions are genuinely the owner's (section 17). Each has a recommended default and a latest decision point. The design proceeds on the defaults, and no question blocks work before its decision point.
- Some items are owner **process** prerequisites, not design questions: authorizing M7, naming the agent, worktree and branch, the RAZER window, the R-1 tag, and the dialogue tone review. They are listed in sections 17 and 18.
- The implementation order is section 18. The slices are section 10, and the acceptance criteria are section 11.

## Sections

1. Authoritative M7 scope
2. Existing-system inventory
3. Navigation v1
4. Building v1
5. Factions and reputation v1
6. Cross-system contracts
7. Persistence and save migration (schema 14)
8. UI and controls
9. Minimal content and asset requirements
10. Implementation slices
11. Exact acceptance criteria
12. Test matrix
13. Runtime proof plan
14. Performance considerations
15. Risk register
16. Explicit deferrals and non-goals
17. Owner decisions
18. Recommended implementation order

## Supporting papers (in `G:\UNNAMED_HISTORY\M7_DESIGN_2026-09-24\`)

| Folder / file | What it holds |
|---|---|
| `M7_EXECUTIVE_BRIEF.md` | The short version for the owner |
| `research/*.md` | Ten cited research notes over the docs and source at `e10d2c4`: authority, building docs, faction docs, world lore, simulation core, spatial movement, persistence, content registry, social/quests code, presentation/performance |
| `drafts/00_SCOPE_RULINGS.md` | The design lead's scope rulings given to every design pass |
| `drafts/01_LEAD_RULINGS_ON_AUDITS.md` | The design lead's rulings on the four audits |
| `drafts/A_navigation.md`, `B_building.md`, `C_factions.md` | The full part designs, with derivations, arithmetic and appendices (superseded where this document differs) |
| `drafts/D_cross_system.md` | The reconciliation register D1-D38, which settles the disagreements between the parts |
| `drafts/E_slices_tests.md`, `F_persistence.md`, `GH_content_ui.md`, `IJ_risk_perf.md` | Working drafts of the plan, persistence, UI, content, risk and performance |
| `drafts/panel/*.md` | Independent candidate designs (three navigation, two faction), the first building design and its adversarial critique |
| `drafts/audit/*.md` | Eight audits. On the drafts: scope, code facts, determinism and persistence, implementability. On the assembled document: two cross-section consistency checks, owner-brief compliance, code facts. Every finding was applied or ruled on |
| `drafts/final_sections/*.md` | The section files this document was assembled from (same text) |

---
