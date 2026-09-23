# M3c time-to-kill table

**Generated from the build; do not edit.** `TtkTableTests` fights each row in the real simulation, over the game's own content, in 24 worlds (seeds 1-24), and fails when this file differs from what the build produces. Regenerate after an intended tuning change with `UNNAMED_WRITE_TTK=1 dotnet test tests/Application.Tests --filter TtkTable` and review the diff.

Design bands (`VERTICAL_SLICE.md` §5.1): **4-8 s** to kill a standard enemy at peer level and gear; **8-15 s** to die of sustained mistakes. Every number below is placeholder tuning with a stated shape (`PROTOTYPE.md` A-5).

## Killing one grey wolf (level 2, 50 health, fur 2 on the torso)

A level-L character has put its L-1 level-up points into Might. The sword and bare hands close to reach; the bow opens from 15 m and keeps shooting. Time runs from the first swing or draw to the kill.

| Weapon | Level | Might | Median s | Fastest s | Slowest s | Median attacks | In the 4-8 s band |
|---|---|---|---|---|---|---|---|
| Rusted Sword | 1 | 10 | 5.60 | 4.10 | 6.35 | 8 | yes |
| Rusted Sword | 2 | 11 | 5.60 | 4.10 | 5.60 | 8 | yes |
| Rusted Sword | 3 | 12 | 4.85 | 4.10 | 5.60 | 7 | yes |
| Hunting Bow | 1 | 10 | 7.45 | 6.15 | 8.75 | 6 | yes |
| Hunting Bow | 2 | 11 | 7.45 | 6.15 | 8.75 | 6 | yes |
| Hunting Bow | 3 | 12 | 7.45 | 6.15 | 8.75 | 6 | yes |
| Unarmed | 1 | 10 | 13.30 | 10.70 | 14.60 | 21 | no (a fallback, not a weapon family) |
| Unarmed | 2 | 11 | 12.65 | 10.05 | 13.95 | 20 | no (a fallback, not a weapon family) |
| Unarmed | 3 | 12 | 12.00 | 9.40 | 13.95 | 19 | no (a fallback, not a weapon family) |

## Dying, standing still

A level-1 character wakes the wolves with one swing each, then stands: no guard, no dodge, no reply. Time runs from the first wound to death. Bleeding is part of it. Phase 1's encounters come in pairs (the valley strays, the respawning pack) and in the den's four, so the pair is the standard encounter the band is read against.

| Wolves and armor | Median s | Fastest s | Slowest s | In the 8-15 s band |
|---|---|---|---|---|
| 1 wolf, armor: none | 24.65 | 18.85 | 31.90 | no |
| 2 wolves, armor: none | 13.20 | 11.60 | 15.90 | yes |
| 4 wolves, armor: none | 8.05 | 7.30 | 9.50 | yes |
| 1 wolf, armor: hide vest + cap | 26.10 | 20.00 | 33.00 | no |
| 2 wolves, armor: hide vest + cap | 14.45 | 11.75 | 15.95 | yes |
| 4 wolves, armor: hide vest + cap | 8.65 | 7.40 | 10.10 | yes |

## No health sponges

The same body authored at level 6 with a doubled bite (8-12). Level is not an input to the damage pipeline, and the health pool is unchanged, so it dies as fast as the level-2 wolf; it is more dangerous only because it hits harder.

| Wolf | Level | Health | Bite | Kill with the sword, median s | Death standing still, median s |
|---|---|---|---|---|---|
| As authored | 2 | 50 | 4-6 | 5.60 | 24.65 |
| Over-band variant | 6 | 50 | 8-12 | 5.60 | 14.50 |
