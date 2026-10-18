# NPCs and residents on the approved MetaHuman bodies

Owner ruling, 2026-10-18: preserve the approved anatomy. Use uniform whole-model stature only. Native Creator muscularity is permitted for Kal/Ondrek but unused in this build. Only main-chat published passing garments may be bound. Follow the owner's simplified wardrobe method: combined authored outfit sets and whole standard body-region visibility, with native fitted Creator output used as-is where it fits. NPCs have no separate clothing/fitting pipeline. Until publication, retain the approved player's original base layer.

The former pipeline rebuilt each NPC's body and omitted the player's runtime corrective companion. This implementation copies the actual approved masculine/feminine body resources and corrective tables instead. Native Creator faces/grooms supply identity; their extra facial joints are appended. The body is never re-exported through Blender. Native identity eye joints are separate from the original player's facial eye helpers. `BoneNames` selects the identity eyes for look-at when present.

## Sources and anatomy contract

| Approved source | SHA-256 |
|---|---|
| `mh_veth_m_rigged.glb` | `89a6dc3e6ebf30a330027d556d259d079df19d57ada99f5b8f4bde0af55a7c5d` |
| `mh_veth_f_rigged.glb` | `2bcbb1bbecd5b4f1f6d32469b3485cbc4ea119c01eff43dd99b1d56e2442cb11` |
| Accepted `anim.player.mh.run.glb` | `ac1878ab2c600ca399c69f91cdae5908ec2e19eea3b1cbf8f0f7099e368a9b77` |

The validator checks the source hashes, original binary prefix, all original accessors/buffer views, body skin, four body LOD meshes, body material, every original body joint's rest properties and unchanged corrective JSON. Appending identity/wing children is allowed. No approved body attributes, weights, inverse binds or proportions change. No body measurement/blend solver is run.

Optional `body_skin_colour.png` changes skin colour only, matching the native identity palette or Siann opal finish. Original alpha and non-skin/base-layer pixels are asserted unchanged. Original normal maps remain in use. The runtime loads this sidecar by stable asset ID, never by Godot's mutable node name; per-instance materials prevent cross-character changes.

## Roster

Eight named identities: Renn/Walter, Kera/Sook-ja, Sel/Asha, Tavar/Cameron, Berrin/Jorge, Durn/Bruce, Mara/Zuri and Tam/Tuya. Tam remains a woman; Durn remains bald, with his protected garment design retained in fitting sources until publication.

Each of eight peoples has six resident appearance bindings. Veth, Kal and Siann have six physical identity modules each; Orenth/Ondrek/Constructed/Mor/Vaskaal reuse the Veth identity masters with people surfaces and uniform stature. Total: 48 resident appearances and 26 physical NPC/resident models. Siann use Asha/Zuri/Etta/Cameron/Kelvin/Omari; visibly older Grace/Lani/Walter candidates were replaced to respect `direction/extensions/RACES.md:217-218`.

Stature values in the recipes are presentation proposals: Veth/Orenth/Constructed/Mor1.0, Kal0.90, Siann0.88, Ondrek1.35, Vaskaal1.29. They apply once to the whole model. Clip paces remain the original unscaled player values; `SkinnedFigure` accounts for stature once. Paired anatomy proofs deliberately use scale1 on both figures.

The owner's current anatomy rule supersedes older directional shoulder/neck/limb/finger proportion descriptions. This migration does not implement the Vaskaal's extra articulations/digit arrangement or reshape Ondrek silhouettes. People details here are native identity faces, eyes/grooms, surfaces, uniform stature and Kal wing attachments. Those older anatomical proposals must not silently re-enter a future build recipe.

Named and resident bindings use the player's existing native clips, grips and corrective path. Retired NPC overrides are removed from both visual-option sections and regional/depot overlays. Resident selection is a stable hash of the resident's persisted instance ID, without new saved appearance state. The two existing miner/woodcutter routines supply Ondrek residents; Ondrek remains non-playable.

Generated adventurers use the people already saved on their agent record and the same identity pools. A generic NPC with no authored people uses the Veth pool as the native-body fallback. This covers the eight generated adventurers in addition to the eight named characters and the routine residents.

## Rebuild and validate

Licensed Creator/native identity inputs remain outside the public repository. A workspace contains `assembled/mh_<identity>/mh_<identity>_rigged.glb`, exported from the held UE5.8 in-editor Creator sources with native fitted grooms and recorded native facial expressions. Neutral facial-bake guards reject a different identity's skeleton preview pose. Distant facial LODs retain native neutral geometry; LOD0 carries the native expression morphs. Garment/groom weights use Blender's built-in Data Transfer modifier, nearest-face interpolated vertex groups; no body fitting or drape solver is involved.

Run from the repository root, supplying a **new** private workspace and the approved player's read-only asset root:

```powershell
python tools/character_build/npc_approved_body.py --workspace <workspace> --player-root <approved-primary> --id npc_renn_vale --sex m
python tools/character_build/npc_body_colour.py --workspace <workspace> --player-root <approved-primary> --id npc_mara_ling --native-body <native-Body_Basecolor.png>
python tools/character_build/npc_verify_approved_body.py --workspace <workspace> --player-root <approved-primary> --stage-root <new-npc-primary> --report <integrity.json>
python tools/character_build/npc_bindings.py --asset-root <new-npc-primary> --integrity-report <integrity.json> --baseline-bindings <approved-primary>/bindings.json
```

The wing-only module consumes the held symmetric, author-folded `exports/kera_exact_attachments.glb`, selecting `wing_module_LOD*` only. `npc_approved_wings.py --workspace ... --player-root ... --id ... --sex ...` writes `approved_wing_models/`. Male wing anatomy receives a uniform module scale and back-anchor translation; the body stays untouched. Its new skin binds are calculated against the unchanged target rest. Harness proposal meshes are excluded from active scenes.

The copy/prune/verification tools use Python's standard library. Colour conversion uses Pillow/NumPy; wing attachment additionally uses SciPy. None uses image/mesh generation services. Author sources, raw models and textures are not committed publicly.

## Visual and integration gates

Use `--anim-profiles --anim-person <binding>` for twelve phases, four views. `--anim-compare <reference>` gives paired front/side/three-quarter idle/run views at identical scale with corrective tables required on both figures. `--anim-faces-only` captures neutral/blink/speech/smile views; `--anim-phase 0..11` selects one body phase for a short people-surface review. Default full-motion behavior remains available.

Images are reviewed visually, including head/body colour, shoulders, torso/hip/limb contours, native eyes/expressions and wing placement. Geometry assertions do not substitute for looking. In-world captures use the real NPC view, including posture, foot planting, face driver and head/eye look-at. Paired images are posted to the shared `CODEX_PROGRESS.md` before merging. Kera's body shape and symmetric wings are owner-approved; this does not constitute blanket face, wardrobe or roster approval.

Run the solution build, test projects, content lint/xref, connected route, playthrough and save/relaunch for integration evidence. Final character acceptance is a clean turnaround and run viewed by eye. Do not add per-triangle clothing coverage or numerical clipping gates. Results and outstanding visual failures belong in the dated review report; successful internal checks do not establish visual acceptance.

## Credits

MetaHuman bodies, native faces and grooms: Epic Games, UE5.8 in-editor MetaHuman Creator; retain the project's existing UE notices. Accepted run: VanillaLoop, *Free Sample Animation Set*, Fab Standard, as recorded in the unchanged clip metadata. Other player clips retain their individual source records (including Fab/Quaternius/Rokoko).

Kal folded wings: held WildMesh3D *100 MODELS*, BaldEagle author-folded pose; the project's licence ledger records the owner-cleared WildMesh Standard v1.1 conditions. Ondrek stone surface uses the held Poly Haven `lichen_rock` PBR maps, CC0. No copyleft asset is mixed into MetaHuman/Fab/WildMesh modules. Keep raw licensed assets out of this public repository and out of generative AI services.
