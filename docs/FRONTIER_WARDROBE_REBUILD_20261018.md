# Frontier wardrobe

The owner's 2026-10-18 simplification supersedes the earlier per-vertex mask and
numeric clipping experiments. Those experiments are retired; their source and
failed results remain in history/private scratch, not the acceptance workflow.

The approved MetaHuman body shapes and accepted run remain unchanged. Clothes
are newly authored to those bodies using native Blender tools and standard skin
binding. No old garment warping or body proportion edits.

## Body regions and clothing sets

The native skin is partitioned into ordinary meshes: head/neck, torso, upper
arms, forearms, hands, hips, thighs, calves, feet, and scalp hair. Garment
sidecars declare `hide_regions`; dressing hides those whole meshes, and
unequipping shows them again. There is no runtime triangle filtering or
per-vertex source-mask shader. Legacy region uniforms remain for older assets.

Layered torso/legs combinations are authored as one skinned outfit mesh per
set, with permanently hidden linen faces cut before its final cloth binding.
Rigid lames retain their rigid bone assignments. Headwear and foot armour also
combine their backing and plates into single assets. Loose cloaks hide no body
region and use native Godot spring bones.

`equipment_outfits` supports `set:<chest item>|<legs item>` entries; empty items
mean unequipped. The six body sets cover linen/vest/jack, each with or without
trousers. Head, feet, gloves and cloak remain independent equipment choices.
Older bindings without combined-set entries keep their existing selection.

## Review

Use the existing `--anim-sheet <output> --anim-profiles --anim-states idle,walk,run`
with the candidate asset root and binding overlay. Personally inspect the
front, side and back turnarounds and running poses. Headless setup is not visual
acceptance. Build/tests, content lint and xref remain the integration checks;
the release candidate additionally needs its packaged playthrough and relaunch.

Current status: combined sets and whole-region runtime are private candidates.
Visual acceptance, live binding promotion, waiting-branch integration and the
release candidate are still pending. No shared asset root has changed.
