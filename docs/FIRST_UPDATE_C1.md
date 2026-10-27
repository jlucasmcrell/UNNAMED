# First-update C1 combat

Base: `claude/pp11-fixes`. Owner brief: `BRIEFS_FIRST_UPDATE_20261027.md` C1.

All 20 creature definitions carry at least three attack profiles. Committed heavy tells take at least 0.8 s; dodge, facing, reach and block resolve through existing combat rules. Boss-tagged enemies below half health recover 25% faster and hit 20% harder, retaining their tells. Armed companions use quick/heavy/lunge profiles, guard or sidestep incoming heavy tells, and display the actual held weapon and combat phase. Kept animals reuse those profiles without forced stagger.

Level-gap rule supersedes the historical no-level-input damage rule: peer and one-level fights retain authored numbers. Beyond that, each additional level adds 0.75 to the damage factor; a lower-level attacker divides damage by that factor and loses 10 percentage points of accuracy per step, capped at 50%. A miss cannot stagger or apply on-hit effects. This applies to player/creature strikes, including higher-level resisted ambushes. Health is not inflated. Kal retains its existing 110% health and 85% physical damage taken; no extra toughness multiplier was added.

Grey wolves and alphas share call propagation. Healthy wolves circle at 2.4 m, alternate approach slots, and avoid simultaneous windups/active attacks; injured wolves withdraw to 8 m. The alpha is 1.25 times adult scale, with a pale mottled coat, Alpha tag, howl and committed pounce/knockdown. Blocking/dodging prevents forced knockdown.

Animation supplement: six existing native WildMesh wolf takes, exported with the existing FBX-to-glTF tool. Animation-source centimetres normalized to metres; target bodies/rests/skins unchanged. Two bite, sweep and pounce variants are bound where available. Other creature rigs retain their existing native attack libraries, with distinct gameplay timing/reach. Humanoid variants remain the accepted UAL set; no KayKit. External delivery and review receipts: `D:/Otherreach_Scratch/c1_combat_20261027`.

Proof: opt-in `OTHERREACH_C1_TABLE` writes 4,840 real-simulation trading-blows rows (8 peoples, player L1–5, all 20 stat blocks at L1–6, plus packs). One fixed seed per cell is coverage, not a measured win probability. No dodge, block, heal, companion or abilities. Status-effect health loss is reported separately from direct hits. Sixty-second unresolved fights are censored. Peaceful small wildlife is identified separately from hostile difficulty totals.

The C1 review harness requires both an explicit environment path and a private content marker, uses a created character named Rook and normal commands/gameplay pacing, and stops on the owner flag. It does not change public saves or start automatically. Captures and final balance receipt are reviewed externally before adoption.
