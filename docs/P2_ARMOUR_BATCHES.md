# P2 armour continuation — owner overnight window, 31 October 2026

Source branch: `content/p2-gear`, continuing the accepted weapon delivery at
`75cd1904`. Claude accepted the FINAL20 both-body worn packet at00:25 Nov1.
All20 pieces are now bound and pass normal item equip/movement/unequip on both bodies.
P2_ARMOUR_DELIVERY.json and P2_ARMOUR_INSTALL.md are the final delivery index.
Proof cutoff: 01:30 EDT on 1 November; integration window ends at 02:00.

| Batch | Source-review IDs | Native worn proof required |
| --- | --- | --- |
| 1 | A07, A13, A15, A21, A23 | Five helms, both current Creator bodies |
| 2 | A01, A05, A10, A16, A09 | Four boot pairs and plate gauntlets |
| 3 | A12, A22, A04, A08, A14 | Two glove pairs and three leg pieces |
| 4 | A18, A02, A06, A11, A17 | Remaining leg piece and four chest pieces |

Every batch needs real native materials and source geometry, exact-target rests
and controls, native clothing setup, 3.5 mm garment clearance, appropriate
underlayers and trousers inside boots. Each piece needs both-body game-camera
walk, run, attack and crouch footage, a contact sheet and Claude's verdict.
Close inspection and all shipped garment LODs remain part of the fit review.
Neither a static source render nor a numerical rest check substitutes for it.

The final first-hour stat blocks are in `P2_GEAR_STATS.json` and the one-page
`P2_ARMOUR_FINAL_STATS_REVIEW.md`. Every bound piece includes its full existing-schema block,
repair, native icons and a real merchant source, followed by content lint,
hooks, balance-table regeneration and the relevant tests. Unproven pieces wait.

`keep_humgen_clothing_keys.py` extends the accepted creator clothing fix
`3cc25af9`; its headwear wrapper also retains native face-proportion controls.
Each export saves its exact hook bytes and hashes in its authoring receipt.
The candidate helm exporter runs HumGen's custom clothing conversion and Godot
recipe, comparing its complete joint vocabulary and global rests with the
exact current Creator export. It preserves native materials rather than
rebaking them at a lower resolution. Small helm geometry is retained at all
four native LOD levels; no reduction is claimed.

The selected twenty pieces now have original-author attribution plus the
DevonLux adaptation trail in `P2_ARMOUR_CREDITS.md` and
`P2_ARMOUR_PROVENANCE.json`. Primary metadata and identification receipts remain
private beside the owned source receipts. This closure applies only to these
selected pieces, not the compiler's entire collection.
No owned source art is sent to a generative model.
