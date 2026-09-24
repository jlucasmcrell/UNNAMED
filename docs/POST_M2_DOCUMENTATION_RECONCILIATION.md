# OTHERREACH — Post-M2 Documentation Reconciliation Plan

> **SUPERSEDED — HISTORICAL RECORD. DO NOT USE AS GUIDANCE.**
> Superseded on 2026-09-23 by `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`, whose §11 carries this plan's open items. It stays in the repository only because it is part of the repository's history.

**Purpose:** Capture exactly what should be updated after M2 is complete and audited, without destabilizing M2 while it is in progress.

## 1. Do not change during M2 merely for brainstorming

Avoid changing current identity/persistence contracts just to accommodate future systems.

Especially protect:

- `PERSISTENCE.md`;
- M2 `ROADMAP.md` exit criteria;
- Definition/Instance identity decisions;
- current save manifest/determinism contracts.

Future state can be added later through normal versioning/migration.

## 2. README

After M2:

- public-facing title → **Otherreach**;
- repository can remain `UNNAMED` codename until deliberate rename;
- include tagline candidate;
- update milestone status;
- clarify code MIT vs non-code asset rights.

## 3. INDEX

Add links to all design-extension docs.

Distinguish:

- normative architecture;
- working design;
- future exploration.

## 4. PROJECT_CHARTER

Small amendments only:

- game title;
- Other cosmology as long-term identity;
- NPCs as world participants;
- knowledge-driven exploration;
- flexible systemic crafting;
- optional future community-server path.

Do not rewrite the charter into a lore encyclopedia.

## 5. WORLD_ARCHITECTURE

Add/reconcile:

- authored macro geography + deterministic micro dressing;
- seamless exterior target;
- property/construction persistence;
- Otherways as possible streaming transitions;
- settlement emergence;
- knowledge vs reality where relevant.

## 6. SYSTEMS

Future system boundaries likely needed for:

- perception/information;
- economy/contracts;
- autonomous NPC goals;
- maps/knowledge;
- pets/animals;
- advanced item grammar;
- optional AI narrative.

Keep authority ownership explicit.

## 7. ROADMAP

Add:

- engine validation spike before deep 3D commitment;
- later milestones/spikes for complex itemization, NPC economy and AI narrative;
- do not pull all future systems into Phase 1.

## 8. RISK_REGISTER

Add risks:

### Godot large-world 3D scalability
Mitigation: stress-test spike and hard budgets.

### Third-party terrain dependency
Mitigation: engine-neutral assets and isolated terrain boundary.

### Combinatorial item explosion
Mitigation: sockets/capability tags/compatibility/automated tests.

### Simulated economy instability
Mitigation: stabilizers and bounded elasticity.

### NPC simulation CPU cost
Mitigation: A/B/C/D tiers and analytical resolution.

### AI hallucination/authority
Mitigation: optional provider-independent service and deterministic validation.

### AI inference latency/VRAM
Mitigation: background generation, caching, local/LAN/cloud abstraction.

### Knowledge-system complexity
Mitigation: one canonical knowledge representation with explicit confidence/source.

### Perception complexity
Mitigation: bounded sensory model and abstract distant simulation.

## 9. PROGRESSION

This is a major reconciliation point.

New ideas must be mapped to existing:

- AX-SKL;
- AX-WM;
- AX-MM;
- AX-ABL;
- AX-PRF.

Do not add duplicate progression currencies.

Questions:

- Are “techniques” abilities, mastery nodes, or learned knowledge?
- How do cultural traditions fit?
- How does component-based crafting replace/extend recipe-tier assumptions?
- What should race aptitude change: start, learning rate, cap, or access?
- How do innate biological traits remain outside learned skill?

## 10. DATA_MODEL

Likely future expansions:

- component-based item definitions/instances;
- material properties;
- item modes;
- armor coverage;
- body-plan/fit families;
- knowledge/map records;
- contracts;
- NPC goal summaries;
- pet records;
- injuries/wounds;
- perception memories;
- AI narrative cached records.

Do not design all schemas before corresponding gameplay is prototyped.

## 11. Existing crafting/inventory/equipment systems

Reconcile simple current schemas with new direction incrementally.

Prefer backwards-compatible conceptual evolution rather than throwing away the current foundations.

## 12. RACES

Replace existing `RACES.md` with the updated version from this design pack after review.

## 13. New canonical design documents

Promote reviewed versions of this pack into `docs/`.

Suggested filenames are already repository-ready.

## 14. Git discipline

Commit documentation reconciliation separately from foundational M2 code.

Suggested commit shape:

`Document post-M2 Otherreach gameplay design`

Do not mix hundreds of generated asset files into the same commit.

## 15. Rule for coding agents

Until a design document is reconciled into normative architecture:

> If a new design direction conflicts with an existing normative contract, stop and surface the conflict. Do not silently rewrite the architecture.
