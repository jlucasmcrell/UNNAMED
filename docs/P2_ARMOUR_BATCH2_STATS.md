# P2 armour batch 2 — complete stat review

Existing schema 1, ordinary item class (common rarity), stack 1; first-hour level 1–5 target. Armour has no attribute or level requirement. Native worn approval and normal equipment-path binding are separate gates.

| ID | Item / slot | Armour / material | kg | Coin | Durability | Repair station / skill / material | Vendor |
| --- | --- | --- | ---: | ---: | ---: | --- | --- |
| A01 | Sealed Road Boots / feet | 2 / leather | 1.1 | 18 | 250 | workbench / survival 2 / small_hide | Durn |
| A05 | Quilted Ward Boots / feet | 2 / leather | 1.3 | 21 | 350 | workbench / survival 2 / small_hide | Durn |
| A10 | River Lame Boots / feet | 3 / plate | 1.5 | 27 | 400 | anvil / smithing 4 / iron_ingot | Kera |
| A16 | Repaired Proof Sabatons / feet | 3 / plate | 1.9 | 33 | 550 | anvil / smithing 4 / iron_ingot | Kera |
| A09 | Ward Plate Gauntlets / hands | 2 / plate | 0.9 | 23 | 500 | anvil / smithing 4 / iron_ingot | Kera |

Anchors: Hide Cap 3 armour / 1 kg / 12 coin / 150 durability; Hide Vest 6 / 4 kg / 30 / 150; Lamework Jack 10 / 7 kg / 71 / 500. Iron repairs use an anvil, smithing 4 and iron ingot; hide repairs use workbench, survival 2 and small hide. Weight, coin and repair access remain costs alongside protection and durability. Material class is authored in the existing schema; this branch does not implement separate material resistance multipliers.

Closed-helm perception/hearing is not an existing item stat in this branch. Its StealthPenalty increases enemy detection of the wearer; it does not reduce wearer hearing. No unsupported field or incorrectly repurposed penalty is shipped. Claude’s suggested perception tradeoff remains an explicit follow-up unless the owning systems lane provides the real mechanic.
