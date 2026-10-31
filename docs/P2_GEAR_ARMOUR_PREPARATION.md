# Playtest 2 armour source preparation

Claude's coordinator ruling in `CODEX_PROGRESS.md` at 07:30 EDT on 2026-10-31 requires native worn fits and movement/layering proof before any new armour ships. Static source renders, inventory icons and valid statistics do not meet that gate. No new armour item or worn binding is included in this weapon batch.

Twenty distinct source pieces passed the source-look review: A01, A02, A04–A18 and A21–A23. They cover head (5), chest (4), legs (4), hands (3) and feet (4). A09/A12 now have readable single-gauntlet icons; their original paired sources remain intact. A03 wrist guards, A19's problematic material conversion and A20's weak helmet were excluded.

Private preparation is retained at `D:\Otherreach_Scratch\p2_gear_20261031`:

- `selected_manifest.json`: exact source paths, proposed item IDs, source authors/licences and first-hour statistics.
- `draft/items/armor`: twenty proposed definitions, deliberately outside live content.
- `game_art/ready`: self-contained static source models with native materials and no legacy character skins.
- `game_art/ui` and `game_art/manifests/ui_icons.json`: native rendered inventory icon candidates.
- `game_art/static_source_verification.json`: structural/material audit; **not** a wearable-fit certificate.
- `licences`: exact primary receipts. DevonLux's separate compilation upstream-credit trail remains unresolved; do not describe that trail as cleared.

The personally reviewed native source sheet is `C:\Otherreach_External_Assets\asset_library\playtest2_gear_20261031\ARMOR_EXPORTED_NATIVE_REVIEW_V2.jpg`. Source approval does not approve a new body attachment.

Continue through `HUMGEN_WORN_ARMOUR_PLAN.md`: fit the actual current native body and rest pose, use HumGen's clothing setup, preserve controls/binds, retain appropriate underlayers, and prove equip/unequip plus movement and LODs in Godot. The comparison-cast path currently clears legacy `EquipmentOutfits`; attaching old MetaHuman garments or adding invisible inventory armour would not finish this work. Begin with the plan's small chest-piece proof before expanding. No source art has been sent to a generative model.
