# M7 Cloud Audit Inputs

AUDIT INPUT ONLY.

These files were copied into this temporary review branch solely so a cloud
review agent can inspect local-only M7 design/evidence sources.

They are:

- NOT canonical game documentation
- NOT new M7 implementation
- NOT authorization to modify M7
- NOT intended to be merged wholesale into main

Authoritative implementation under review:

- pre-M7 main: a69693108b89d471cdb409a229ba0bdf78b08fa4
- M7 final runtime-tested commit: 1278b9c
- M7 final branch/documentation head: ac8d9d8
- implementation branch: claude/m7-factions-building

Important repository-native sources already on this branch:

- docs/M7_STATUS.md
- docs/M7_VISUAL_INTEGRATION_HANDOFF.md

The M7_DESIGN_2026-09-24 directory is a preserved copy of the local design
package used during implementation.

M7_EVIDENCE_TEXT contains only lightweight textual evidence copied from the
local evidence archive. Large video captures were intentionally NOT committed.

A cloud reviewer must not infer that unavailable local video/performance
hardware evidence was independently reproduced in the cloud.

Expected audit output:

docs/reviews/M7_FINAL_CLOUD_AUDIT_2026-09-26.md

The reviewer should modify no implementation files.
