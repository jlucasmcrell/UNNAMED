# Gate C native role outfits — 2026-10-29

Branch: `npc/gate-c-outfits`, based on `npc/gate-c` at `3dd4b4ee` (includes the Kal lining/stencil fix).

The two native creator bodies remain unchanged. Role garments use the native MakeHuman fitting service where applicable, HumGen custom-content bilaplacian skinning, retained HumGen correctives and four native LODs. Creator body controls follow through HumGen's fitting field. Linings retain the same controls and a single armature modifier. Fitted role garments use 3.5 mm base stand-off and per-outfit UV coverage masks. Aprons and capes receive another 8 mm of layer clearance over the tunic. Reversed linings use the opposite growth sign, preserving their authored 3 mm thickness. Stand-off runs after the dye pass and preserves its colours. Lining meshes join the same existing LOD ladder as their outer shells.

| Role | Option | Source catalogue row |
|---|---|---|
| Guard | `outfit.period.guard` | GATEC-KNIGHT-01 supplement: Modular Fantasy knight character, separated Tunic/Bracers/Pants/Boots FBXs |
| Cook | `outfit.period.apron` | CLOT-ae8bc72c989b, CLOT-706b03c41362, CLOT-c42d309f43d6 |
| Smith | `outfit.period.smith_apron` | Same fitted apron/tunic/trousers; MATE-f887d32164cb Leather014 |
| Hunter/trapper | `outfit.period.leathers` | CLOT-706b03c41362, CLOT-c42d309f43d6, CLOT-f5a825bc6dc9; Leather014 |
| Sel Arien | `outfit.period.robe` | CLOT-f707694923ac |
| Renn Vale / steward | `outfit.period.coat` | CLOT-a904f17d3174, CLOT-c42d309f43d6 |
| Villagers / porters / outdoor workers | `outfit.period.tunic`, female `outfit.period.dress` | CLOT-706b03c41362, CLOT-c42d309f43d6, CLOT-4c46ac8bf262 |

Exact source paths and reproducible inputs: `tools/creator/role_garments.json`. Native fitted hunter components are reused only on the identical creator preset rig, with independent rest equality checked; the from-scratch recipe remains available. Preliminary Frontier workwear hunters are superseded and excluded from the delivery.

Final native delta (visual review pending): `D:\gc_delivery_layer04\game\primary`. The original `D:\gc_delivery` is retained failed-fit evidence, not the final delivery. It contains fifteen garment variants and no duplicate world/body payload. Native export inputs remain under `D:\gc_roles02`, `D:\gc_roles_m`, and `D:\gc_hunter02`. Unchanged GLBs are hard-linked into the delta; metadata/masks are copied. No public package or shortcut is promoted by this lane.

File gates: `D:\gc_delivery_layer04\NATIVE_ROLE_GUARD.json` (15 exact native body/rest/control/LOD checks), `FULL_TEXTURE_GATE.json` (140 full-resolution signed BC7 references). Focused checks: six NpcLooks tests and twenty CreditsContent tests; presentation build succeeds. These file checks do not constitute visual clipping acceptance.

The NPC review uses the actual town actors and their assigned looks. Its opt-in fixture omits unrelated creature imports and speculative prefetch; it retains the town scenery and full NPC/garment quality. Main's normal draw loop pauses during temporary staging, and intervening actors are hidden per shot; original transforms/visibility are restored. Native walk/run/crouch/sword poses are sampled with bone-origin and actual material-growth readbacks. Town sheets and Kal chest/back crops remain the required visual gate. This fixture provides appearance evidence, not a world-memory or performance result.

Licences are credited in `content/credits.yaml`: Donitz, Rehman Polanski and MRT (CC0); punkduck coat (CC BY 3.0), punkduck dress (CC BY 4.0), Elvaerwyn apron (CC BY, version unspecified in held notice); Paradoox cloak (owned Fab); owned Unity knight; ambientCG Leather014 (CC0). The Unity source materials are reconstructed from their authored maps because FBX import does not attach Unity materials.

The layered-apron correction uses Blender's native Shrinkwrap modifier (`NEAREST_SURFACEPOINT`, `OUTSIDE`, 10 mm clearance), restricted to nearby intersecting garment vertices. It runs on every retained key and all four native LODs, after HumGen processing and before native material baking/export. It changes only the apron surface; body buffers, rig/rest, garment topology, weights and UVs remain intact. Reversed linings are generated from the corrected keys. Per-key deformation receipts live beside the four repaired GLBs as `layer_clearance.json`; their source job is `D:\gc_apron_layer01`. The completed delta has fifteen native-contract passes, 140 signed full-resolution references, 79 BC7 DDS files with complete mip chains, and zero orphan DDS files. These are file gates, not visual acceptance.

The final review fixture reads the authoritative terrain height directly, sets a native idle for portraits, and hides foreground scatter to expose feet and hems. Both actual rosters use the same clearly labelled Cairnwold staging point (949,695); it does not claim Ashen Hollow scenery. Three rendered frames settle the camera and native paused pose before pixel readback; the motion checker rejects poses separated by less than 20 mm of maximum bone movement. The local nineteen-actor attempt was stopped before any frames when another lane was still rendering after the lock changed. The final review moves to a clean Release package of 694b1ce7 on the 6900XT farm: one Ashen Hollow roster and one Cairnwold roster, both using all fifteen final layer04 assets. Source/assembly/asset pins and original frames must be retained before the twenty-actor review is sealed.

The farm runner accepts optional per-job `env` values only for `OTHERREACH_*` variables. `{BUILD}` and `{OUT}` expand to that job's local paths. The runner restores its own environment immediately after launching the game, including when startup throws; existing jobs without `env` keep their original launch path. The small applied patch is `tools/creator/farm_capture_env.patch`; the original runner is retained beside it on the farm as `runner.before_gatec_env_20261029.ps1`. Jobs use the packaged game and native viewport PNGs, not desktop recording or development on the farm.
