# Frontier wardrobe and worn armour on HumGen — implementation plan

Status: plan only, 2026-10-24. No armour fitting, runtime attachment code or coverage shader is implemented by this document. The accepted RC d8 package stays frozen.

## Shared frontier wardrobe track — owner ruling 2026-10-24

Armour and period clothing share one native HumGen custom-content pipeline: source/licence audit → fit to the actual body and rest pose → native clothing setup → save HumGen outfit → native Godot export → engine movement/coverage/LOD review. Do not create a second converter for civilian clothes.

The interim catalogue pass is documented in `HUMGEN_RCD9_FRONTIER_ATTIRE.md`: occupational workwear in undyed wool, earth, grey, moss/olive and oxblood, with outdoor boots. Its modern construction remains a disclosed temporary limitation. Mixed-era attire is deliberate for a suitable background/station, never the default villager outfit.

Follow with tunics, aprons, cloaks, jerkins, smocks, wraps and leather work boots from the owned Fab/Cosmos/Sourced packs. Record each exact source/licence/attribution. Never feed owned source assets to generative AI. Select real garments suited to smiths, keepers, scholars and labourers. Fit each target's actual native proportions; the named Kal/Siann/Orenth bodies need their own gates when DeepSeek's production assets arrive.

Begin with one simple frontier torso garment and the first chest armour visual on Ada/Mateo; prove the shared path before expanding. Preserve baseline coverage and equipped-state behaviour. This ruling adds the track to this plan, not completed garments or permission to skip Claude's visual gates.

## First deliverable

Make the already-equipped Hide Vest visible on Ada and Mateo, and remove it correctly when unequipped. Keep inventory, armour statistics and save data authoritative and unchanged. Prove one chest item on both actual bodies before expanding to fourteen NPC bodies or more slots.

The paid HumGen wardrobe has no medieval armour. Its accepted catalogue supplies baseline clothing, not an armour solution. Choose a suitably licensed existing/free vest mesh and record the exact source, licence and attribution; the owned DevonLux leather/scalemail sources are candidates for a proper refit, not ready-to-attach HumGen skins. Converted MakeHuman garments remain deferred after their failed visual review.

## Existing source candidates for the shared track

These are source meshes to evaluate and refit, not accepted HumGen garments. Keep the source skeleton only as a placement reference; it does not establish compatible HumGen binds.

| Existing source | First use to evaluate | Required check before implementation/shipping |
| --- | --- | --- |
| `C:/Otherreach_External_Assets/Sourced_Clothing_20261019/devonlux_peasant/extracted/source/Outfit_Leather_Pesant.fbx` | Plain shirt, sleeveless work vest, trousers, leather boots; smith/keeper/labourer baseline | Local `CREDITS.md` records CC BY 4.0 and requires tracing the compilation's upstream authors; fit each actual HumGen body and prove weights in engine |
| `Sourced_Clothing_20261019/devonlux_4_piece_cloth_armor` | Coarse torso clothing / padded layers | Select occupational pieces, audit source attribution and collision/coverage before export |
| `Sourced_Clothing_20261019/devonlux_4_piece_leather_armor` | First Hide Vest visual and leather work pieces | Proper refit through HumGen; never direct old MetaHuman binding reuse |
| Owned Fab/Cosmos garment packs | Additional tunic, apron, smock, wrap and cloak cuts if present | Inventory exact garment files and applicable purchase/licence first; ownership of environment packs does not imply a suitable clothing mesh exists |

The local source register is `C:/Otherreach_External_Assets/Sourced_Clothing_20261019/CREDITS.md`. Do not turn its compilation licence note into a claim that upstream attribution is already complete. No source mesh was processed, fitted or sent to any generative service for this plan.

## Prepare the garment in HumGen

Freeze each target's preset, age, stature, face, hair and native 59-bone reference. Work in separate candidate directories. Fit the garment to that actual body and its rest pose first; moving a bounding box or transferring weights onto an incorrectly placed mesh is insufficient.

Use HumGen's documented Custom Content workflow to add the fitted object as clothing, then save an outfit preset. That workflow parents to the human rig and supplies skinning and corrective setup. If weights need adjustment, retain the required vertex groups. See the official [custom clothing guide](https://humgen3d.com/docs/custom-content/custom-clothing).

Use the installed add-on's documented `human.clothing.outfit` operations for repeatable fitting/loading. Its API exposes `add_obj`, `deform_cloth_to_human`, `save_to_library` and `set`; check the installed signatures before scripting a refit. The deformation method does not establish that an arbitrary source garment starts in the correct pose. See [OutfitSettings](https://humgen3d.com/docs/api/outfitsettings). HumGen saves outfits as sets of pieces, so define which baseline layers the vest retains or replaces.

Start with a short, sleeveless vest to limit shoulder and elbow problems. Keep the native body proportions, skeleton, bind transforms, fingers and accepted animation tracks. Adjust the garment instead. Treat Blender-only corrective drivers as unproven until their effect survives export and is visible in Godot; a successful Blender preview is not an engine gate.

## Audit body coverage before choosing the runtime asset

First establish whether the current body GLBs contain skin beneath their baked outfits. HumGen uses body masks for clothing, and deleting clothing incorrectly can leave holes; the official [clothing guide](https://humgen3d.com/docs/guide/clothing) documents this dependency.

If those faces are absent, a runtime material cannot restore them. Produce a canonical full-skin export through HumGen's own removal/export workflow, preserving identity, expressions and the native skeleton. Compare common visible body surfaces, all 59 rest transforms, hierarchy and stature against the frozen reference. Update exact-body motion metadata only after those checks pass; retain the existing Epic animation tracks when their target rest is identical.

For modular equipment, keep native body vertex/normal/tangent buffers intact. Bake conservative per-body UV coverage masks offline and combine only the masks for supported outfit combinations. Hide covered skin in its material; restore it when equipment changes. Never mask the native hands, face or feet merely to conceal a bad garment fit. Preserve a valid baseline outfit and its matching coverage when the vest is removed.

If a whole-outfit export is needed to establish the first visual proof, keep it as a two-player prototype. Do not multiply complete character exports across every equipment permutation.

## Export and attach

Use the proven HumGen Godot export recipe, original-resolution materials, native BC7 cache and the sparse-safe LOD material repair. Export a garment prepared for each actual body. Check bone names, ordering, hierarchy, bind transforms, weights and corrective data against that target; a high follow ratio alone is insufficient.

The intended runtime is a garment on the character's existing native skeleton, using its unchanged compatible skin/binds. Reuse the existing outfit attachment path only where it passes those checks. Do not apply MetaHuman bind matrices, merge rigs, remove joints, retarget garment motion, or compensate with edited arm keys. Share the character's existing animation rather than create another animated body for each equipment slot.

Add a small item/body lookup for the first supported chest item: item definition, exact target body, garment asset, covered baseline layers and coverage mask. Let the existing equipment view drive it for the world figure and paper doll. Missing fits retain the baseline outfit and appear in the development coverage report; they must not silently attach a garment fitted to another body.

## Acceptance and expansion

The first gate requires Ada and Mateo wearing the vest in the running game, with close-ups and real-time movement: idle, forward/back/strafe, crouch, sprint, all three accepted sword attacks, sit and lie. Inspect shoulder/arm clearance, waist, neck, hands, coverage, materials, shadows and every shipped garment/body LOD. Equip and unequip repeatedly; the paper doll and world must agree, with no skin holes, leftover layers or pose reset. Verify save/load with existing equipment data.

Claude reviews the frames and movement before expansion. Then fit and gate each of the fourteen NPC bodies, followed by additional items/slots. Keep native hands and the accepted real footwear; body-shell gloves and boots are not the fallback. Cloth simulation, a general wardrobe editor and race-body armour fits are separate later work.

Proposed first output: two fitted chest assets, their source/licence records, body/skeleton audits, coverage evidence, an equip/unequip video, and daylight front/side/attack frames. Implementation begins only as a separately requested task.

## Runtime garment lesson from the interim pass

Source-game `rcd9_review07` showed deep centre/hem folding on the catalogue long Dress in the accepted neutral idle and walk, despite native wear-API catalogue previews. The interim final pass therefore uses work tops and trousers instead of those three dresses. Do not alter the accepted stance, arms or body to conceal a garment problem. Native Blender fit/correctives are not proof of runtime skirt deformation; any future period smock, apron or cloak must pass its actual Godot movement gate before cast-wide expansion.

The same review exposed cyan collared-shirt buttons. `HG_Control` exposes a separate `Buttons` colour field, outside the `_C0` fabric randomisation group; set and record visible detail fields as well as main fabric colours before native export.
