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

Delivery delta: `D:\gc_delivery\game\primary`. It contains fifteen garment variants and no duplicate world/body payload. Native export inputs remain under `D:\gc_roles02`, `D:\gc_roles_m`, and `D:\gc_hunter02`. Unchanged GLBs are hard-linked into the delta; metadata/masks are copied. No public package or shortcut is promoted by this lane.

File gates: `D:\gc_delivery\NATIVE_ROLE_GUARD.json` (15 exact native body/rest/control/LOD checks), `FULL_TEXTURE_GATE.json` (140 full-resolution signed BC7 references). Focused checks: six NpcLooks tests and twenty CreditsContent tests; presentation build succeeds. These file checks do not constitute visual clipping acceptance.

The NPC review uses the actual town actors and their assigned looks. Its opt-in fixture omits unrelated creature imports and speculative prefetch; it retains the town scenery and full NPC/garment quality. Main's normal draw loop pauses during temporary staging, and intervening actors are hidden per shot; original transforms/visibility are restored. Native walk/run/crouch/sword poses are sampled with bone-origin and actual material-growth readbacks. Town sheets and Kal chest/back crops remain the required visual gate. This fixture provides appearance evidence, not a world-memory or performance result.

Licences are credited in `content/credits.yaml`: Donitz, Rehman Polanski and MRT (CC0); punkduck coat (CC BY 3.0), punkduck dress (CC BY 4.0), Elvaerwyn apron (CC BY, version unspecified in held notice); Paradoox cloak (owned Fab); owned Unity knight; ambientCG Leather014 (CC0). The Unity source materials are reconstructed from their authored maps because FBX import does not attach Unity materials.
