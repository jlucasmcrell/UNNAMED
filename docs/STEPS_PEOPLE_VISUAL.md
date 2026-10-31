# The Steps' people — visual review lane

Branch: `visual/steps-people`. Claude alone integrates the release candidate.

All 27 existing Otherhome NPC definitions now carry an authored `people_ref` and
native creator `appearance`: 16 market people and 11 other district characters.
Names, sex, roles, dialogue, teaching, services and placement are preserved.
The looks use 27 different HumGen face presets and the existing people's shape,
skin, hair and stone constraints. The selected peoples are visual proposals for
Claude's verdict, not a new assertion about previously unauthored ancestry.

The row child keeps a short 142 cm silhouette. The available native creator's
youngest supported face age is 18; this uses that existing young preset rather
than claiming a new child-face rig. Judge its read in the full-body frame.

## Native delivery

Period wardrobe source: `npc/gate-c-outfits` at
`eed02d285645ddae392c21ac86d4f56b70a58dc7`; sealed native delivery
`D:\gc_delivery_layer04\game\primary`. Eight options, fifteen sex variants,
original 4K materials and complete BC7 mip chains. The original Gate C visual
review remains separate; this lane must prove the Steps' own looks in game.

Keep the accepted B-007 corrected robe folders from the `aaffd2f5` art handoff
when applying this wardrobe. Do not overwrite them with older Gate C robes.
Merge texture-cache entries by source key and copy the referenced DDS files;
do not replace the world's cache manifest wholesale. Original source recipes
are `tools/creator/role_garments.json`; attribution is retained in credits.

No new body, rest skeleton or texture reduction. The current native body-control
and opaque double-sided cloth fixes are dependencies. Apron and cloak outer
layers keep their additional 8 mm clearance. The existing HumGen LOD selector
now recognizes native `_LOD0_lining` names and selects one lining with its shell.
The cloak's already double-sided authored mesh does not draw a generated
intersecting reverse shell. Guard boots are part of its outfit, so no second
pair is fitted over them. Dye retains the garment's native texture.

## Validation and verdict

Release build and `StepsPeopleLooksTests` pass. The test checks all 27 live
content records, distinct native faces, sex-compatible parts, people-specific
shape ranges and period outfit choices. Native eye-height/full-body frames
are required before this lane is FINISHED. Do not treat this source checkpoint
as Claude's visual acceptance or merge it directly into `rc/playtest-2`.
