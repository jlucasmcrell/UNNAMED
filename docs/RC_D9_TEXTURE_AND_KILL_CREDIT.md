# RC d9: texture and party-credit rulings (2026-10-24)

The owner/Claude queue requires player credit for owned-party last hits and compressed full-resolution packaged art.

`CreatureSystem.Die` resolves a registered pet/summon or companion/hireling to the current owning player once, before XP, deeds,
quests/bounties, faction acts and kill observers. `CreatureKilled.DirectAttacker` retains the physical attacker; hit and pet-skill
events still name that attacker. Ambient NPCs are not party members and do not receive this ownership rewrite. The pet regression
takes Renn's bounty normally, provokes a real wolf, lets its following pet finish it, and checks player XP, the kill deed, bounty
progress and normal corpse-loot access. The companion regression supersedes M6's old no-player-XP expectation by this owner ruling.

Packages explicitly select `textures=cache`. Both primary and fallback libraries read their own source-signed cache. Terrain maps
use the same cache. Scatter keeps compressed coverage textures instead of rebuilding them as RGBA. Imported art at least 2048 pixels
on either axis fails closed in the exported game if it reaches the material upload path uncompressed. Numeric terrain/control maps,
render targets and font atlases are renderer data, not imported art images.

`tools/release/check_texture_cache.py <game> <receipt> --prepare` inventories large GLB material images and loose art maps. Run the
headless editor scene `res://Diagnostics/PackageTextureCacheGate.tscn -- <receipt>` to fill missing entries through the production
`TextureCache`, then rerun the checker without `--prepare`. The clean exporter with `-Primary` requires this check to pass.
It checks source signatures, existing DDS files, BC7 for ordinary art and original dimensions. HDR panoramas use native BC6H to retain
their radiance range. Godot's BC block alignment may extend a non-multiple-of-four edge by at most three pixels (2050 -> 2052);
downscaling is rejected. Source files are unchanged. Fresh packaged starts save current/peak renderer VRAM and texture memory.

This is a storage/upload correction, not an environment quality tier reduction. No source resolution, model LOD or density is lowered.
