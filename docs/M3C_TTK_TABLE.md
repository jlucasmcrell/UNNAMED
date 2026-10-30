# M3c time-to-kill table

**Generated from the build; do not edit.** `TtkTableTests` fights each row in the real simulation, over the game's own content, in 24 worlds (seeds 1-24), and fails when this file differs from what the build produces. Regenerate after an intended tuning change with `UNNAMED_WRITE_TTK=1 dotnet test tests/Application.Tests --filter TtkTable` and review the diff.

Design bands (`VERTICAL_SLICE.md` §5.1): **4-8 s** to kill a standard enemy at peer level and gear; **8-15 s** to die of sustained mistakes. Every number below is placeholder tuning with a stated shape (`PROTOTYPE.md` A-5).

## Killing one grey wolf (level 2, 50 health, fur 2 on the torso)

A level-L character has put its L-1 level-up points into Might. The sword and bare hands close to reach; the bow opens from 15 m and keeps shooting. Time runs from the first swing or draw to the kill. Bare-hand trials that reach 60 s are censored; an asterisk marks rows containing one.

| Weapon | Level | Might | Median s | Fastest s | Slowest s | Median attacks | In the 4-8 s band |
|---|---|---|---|---|---|---|---|
| Rusted Sword | 1 | 10 | 6.70 | 5.15 | 9.60 | 9 | yes |
| Rusted Sword | 2 | 11 | 5.55 | 4.10 | 7.40 | 7 | yes |
| Rusted Sword | 3 | 12 | 4.85 | 3.35 | 6.65 | 7 | yes |
| Hunting Bow | 1 | 10 | 7.45 | 4.85 | 11.75 | 6 | yes |
| Hunting Bow | 2 | 11 | 6.15 | 3.55 | 8.65 | 5 | yes |
| Hunting Bow | 3 | 12 | 4.85 | 3.55 | 7.35 | 4 | yes |
| Unarmed | 1 | 10 | 24.20* | 14.80 | 59.90 | 27 | no (a fallback, not a weapon family) |
| Unarmed | 2 | 11 | 16.50 | 12.45 | 24.35 | 22 | no (a fallback, not a weapon family) |
| Unarmed | 3 | 12 | 13.50 | 10.60 | 17.75 | 20 | no (a fallback, not a weapon family) |

## Dying, standing still

A level-1 character wakes the wolves with one swing each, then stands: no guard, no dodge, no reply. Time runs from the first wound to death. Bleeding is part of it. Phase 1's encounters come in pairs (the valley strays, the respawning pack) and in the den's four, so the pair is the standard encounter the band is read against.

| Wolves and armor | Median s | Fastest s | Slowest s | In the 8-15 s band |
|---|---|---|---|---|
| 1 wolf, armor: none | 19.90 | 15.00 | 24.40 | no |
| 2 wolves, armor: none | 13.50 | 10.25 | 17.90 | yes |
| 4 wolves, armor: none | 13.00 | 9.00 | 15.25 | yes |
| 1 wolf, armor: hide vest + cap | 20.80 | 15.40 | 26.40 | no |
| 2 wolves, armor: hide vest + cap | 14.00 | 11.60 | 18.15 | yes |
| 4 wolves, armor: hide vest + cap | 13.35 | 9.05 | 15.35 | yes |

## No health sponges

The same body authored at level 6 with a doubled bite (8-12). The owner first-update level-gap rule changes damage and accuracy, while the health pool stays unchanged; it is harder to trade blows with because its strikes and accuracy have the level advantage.

| Wolf | Level | Health | Bite | Kill with the sword, median s | Death standing still, median s |
|---|---|---|---|---|---|
| As authored | 2 | 50 | 4-6 | 6.70 | 19.90 |
| Over-band variant | 6 | 50 | 8-12 | >=60 (trade lost/censored) | 5.05 |
