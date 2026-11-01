# P2 armour batch 1 — complete stat review

Existing schema 1, ordinary item class (common rarity), stack 1; first-hour level 1–5 target. Armour has no attribute or level requirement. Native worn approval and normal equipment-path binding are separate gates.

| ID | Item / slot | Armour / material | kg | Coin | Durability | Repair station / skill / material | Vendor |
| --- | --- | --- | ---: | ---: | ---: | --- | --- |
| A07 | Ward Closed Helm / head | 4 / plate | 1.9 | 31 | 500 | anvil / smithing 4 / iron_ingot | Kera |
| A13 | River Open Helm / head | 4 / plate | 1.7 | 29 | 400 | anvil / smithing 4 / iron_ingot | Kera |
| A15 | Repaired Proof Helm / head | 5 / plate | 2.2 | 38 | 400 | anvil / smithing 4 / iron_ingot | Kera |
| A21 | Open Road Barbute / head | 3 / plate | 1.5 | 25 | 400 | anvil / smithing 4 / iron_ingot | Kera |
| A23 | Old Ward Kettle Helm / head | 5 / plate | 2.6 | 35 | 400 | anvil / smithing 4 / iron_ingot | Kera |

Anchors: Hide Cap 3 armour / 1 kg / 12 coin / 150 durability; Hide Vest 6 / 4 kg / 30 / 150; Lamework Jack 10 / 7 kg / 71 / 500. Iron repairs use an anvil, smithing 4 and iron ingot; hide repairs use workbench, survival 2 and small hide. Weight, coin and repair access remain costs alongside protection and durability. Material class is authored in the existing schema; this branch does not implement separate material resistance multipliers.

Closed-helm perception/hearing is not an existing item stat in this branch. Its StealthPenalty increases enemy detection of the wearer; it does not reduce wearer hearing. No unsupported field or incorrectly repurposed penalty is shipped. Claude’s suggested perception tradeoff remains an explicit follow-up unless the owning systems lane provides the real mechanic.
