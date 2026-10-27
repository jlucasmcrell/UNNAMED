# M3c time-to-kill table

**Generated from the build; do not edit.** `TtkTableTests` fights each row in the real simulation, over the game's own content, in 24 worlds (seeds 1-24), and fails when this file differs from what the build produces. Regenerate after an intended tuning change with `UNNAMED_WRITE_TTK=1 dotnet test tests/Application.Tests --filter TtkTable` and review the diff.

Design bands (`VERTICAL_SLICE.md` §5.1): **4-8 s** to kill a standard enemy at peer level and gear; **8-15 s** to die of sustained mistakes. Every number below is placeholder tuning with a stated shape (`PROTOTYPE.md` A-5).

## Killing one grey wolf (level 2, 50 health, fur 2 on the torso)

A level-L character has put its L-1 level-up points into Might. The sword and bare hands close to reach; the bow opens from 15 m and keeps shooting. Time runs from the first swing or draw to the kill.

| Weapon | Level | Might | Median s | Fastest s | Slowest s | Median attacks | In the 4-8 s band |
|---|---|---|---|---|---|---|---|
| Rusted Sword | 1 | 10 | 5.95 | 4.85 | 7.40 | 8 | yes |
| Rusted Sword | 2 | 11 | 5.55 | 4.10 | 6.30 | 7 | yes |
| Rusted Sword | 3 | 12 | 5.55 | 4.10 | 6.30 | 7 | yes |
| Hunting Bow | 1 | 10 | 8.65 | 6.15 | 9.95 | 7 | no |
| Hunting Bow | 2 | 11 | 7.45 | 6.15 | 9.95 | 6 | yes |
| Hunting Bow | 3 | 12 | 8.65 | 6.15 | 9.95 | 7 | no |
| Unarmed | 1 | 10 | 17.35 | 13.35 | 25.10 | 23 | no (a fallback, not a weapon family) |
| Unarmed | 2 | 11 | 16.70 | 13.35 | 24.35 | 23 | no (a fallback, not a weapon family) |
| Unarmed | 3 | 12 | 15.00 | 12.00 | 24.35 | 21 | no (a fallback, not a weapon family) |

## Dying, standing still

A level-1 character wakes the wolves with one swing each, then stands: no guard, no dodge, no reply. Time runs from the first wound to death. Bleeding is part of it. Phase 1's encounters come in pairs (the valley strays, the respawning pack) and in the den's four, so the pair is the standard encounter the band is read against.

| Wolves and armor | Median s | Fastest s | Slowest s | In the 8-15 s band |
|---|---|---|---|---|
| 1 wolf, armor: none | 20.40 | 16.00 | 28.55 | no |
| 2 wolves, armor: none | 13.90 | 10.85 | 19.85 | yes |
| 4 wolves, armor: none | 13.75 | 11.55 | 17.50 | yes |
| 1 wolf, armor: hide vest + cap | 21.35 | 17.00 | 28.55 | no |
| 2 wolves, armor: hide vest + cap | 14.85 | 11.00 | 21.05 | yes |
| 4 wolves, armor: hide vest + cap | 14.40 | 12.25 | 17.50 | yes |

## No health sponges

The same body authored at level 6 with a doubled bite (8-12). The owner first-update level-gap rule changes damage and accuracy, while the health pool stays unchanged; it is harder to trade blows with because its strikes and accuracy have the level advantage.

| Wolf | Level | Health | Bite | Kill with the sword, median s | Death standing still, median s |
|---|---|---|---|---|---|
| As authored | 2 | 50 | 4-6 | 5.95 | 20.40 |
| Over-band variant | 6 | 50 | 8-12 | >=60 (trade lost/censored) | 5.05 |
