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
| Unarmed | 1 | 10 | 13.30 | 11.35 | 15.25 | 21 | no (a fallback, not a weapon family) |
| Unarmed | 2 | 11 | 12.65 | 11.35 | 14.60 | 20 | no (a fallback, not a weapon family) |
| Unarmed | 3 | 12 | 12.65 | 10.70 | 13.95 | 20 | no (a fallback, not a weapon family) |

## Dying, standing still

A level-1 character wakes the wolves with one swing each, then stands: no guard, no dodge, no reply. Time runs from the first wound to death. Bleeding is part of it. Phase 1's encounters come in pairs (the valley strays, the respawning pack) and in the den's four, so the pair is the standard encounter the band is read against.

| Wolves and armor | Median s | Fastest s | Slowest s | In the 8-15 s band |
|---|---|---|---|---|
| 1 wolf, armor: none | 24.65 | 17.40 | 31.90 | no |
| 2 wolves, armor: none | 13.05 | 11.05 | 16.65 | yes |
| 4 wolves, armor: none | 7.25 | 5.85 | 8.70 | no |
| 1 wolf, armor: hide vest + cap | 25.50 | 17.40 | 33.35 | no |
| 2 wolves, armor: hide vest + cap | 13.90 | 11.60 | 17.40 | yes |
| 4 wolves, armor: hide vest + cap | 7.25 | 6.55 | 8.75 | no |

## No health sponges

The same body authored at level 6 with a doubled bite (8-12). Level is not an input to the damage pipeline, and the health pool is unchanged, so it dies as fast as the level-2 wolf; it is more dangerous only because it hits harder.

| Wolf | Level | Health | Bite | Kill with the sword, median s | Death standing still, median s |
|---|---|---|---|---|---|
| As authored | 2 | 50 | 4-6 | 5.60 | 24.65 |
| Over-band variant | 6 | 50 | 8-12 | 5.60 | 14.50 |
