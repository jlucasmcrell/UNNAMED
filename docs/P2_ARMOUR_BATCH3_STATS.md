# P2 armour batch 3 — complete stat review

Existing schema 1, ordinary item class (common rarity), stack 1; first-hour level 1–5 target. Armour has no attribute or level requirement. Native worn approval and normal equipment-path binding are separate gates.

| ID | Item / slot | Armour / material | kg | Coin | Durability | Repair station / skill / material | Vendor |
| --- | --- | --- | ---: | ---: | ---: | --- | --- |
| A12 | River Lame Gloves / hands | 2 / plate | 0.8 | 22 | 400 | anvil / smithing 4 / iron_ingot | Kera |
| A22 | Sealed Leather Wristguards / hands | 2 / leather | 0.35 | 16 | 180 | workbench / survival 2 / small_hide | Durn |
| A04 | Sealed Road Breeches / legs | 3 / leather | 2.2 | 26 | 250 | workbench / survival 2 / small_hide | Durn |
| A08 | Quilted Ward Breeches / legs | 4 / leather | 2.8 | 33 | 250 | workbench / survival 2 / small_hide | Durn |
| A14 | River Lame Tassets / legs | 4 / plate | 3.0 | 35 | 400 | anvil / smithing 4 / iron_ingot | Kera |

Anchors: Hide Cap 3 armour / 1 kg / 12 coin / 150 durability; Hide Vest 6 / 4 kg / 30 / 150; Lamework Jack 10 / 7 kg / 71 / 500. Iron repairs use an anvil, smithing 4 and iron ingot; hide repairs use workbench, survival 2 and small hide. Weight, coin and repair access remain costs alongside protection and durability. Material class is authored in the existing schema; this branch does not implement separate material resistance multipliers.

Closed-helm perception/hearing is not an existing item stat in this branch. Its StealthPenalty increases enemy detection of the wearer; it does not reduce wearer hearing. No unsupported field or incorrectly repurposed penalty is shipped. Claude’s suggested perception tradeoff remains an explicit follow-up unless the owning systems lane provides the real mechanic.
