# Phase-1 technical closeout

The integrated Phase-1 state, for the owner's merge into `main` and as M7's base. This is not M7 and not Phase B. It follows
`PHASE1_AUDIT_REMEDIATION_STATUS.md` (the audit fixes) and the Phase-A complete prototype (real player, NPCs, enemies and world art on
the whole route; its report is kept with the project history, `G:\UNNAMED_HISTORY\phaseA_report\`). Evidence:
`docs/acceptance/phase1_closeout/`; full logs and every run folder: `G:\UNNAMED_HISTORY\phase1_integration_20260925\`.

## Branch and history

`integration/phase1-complete`, from `origin/main` at `e10d2c4` (Case A of the integration plan: the Phase-A branch descends from the
remediation `75e6759`, which descends from `e10d2c4`):

| Commit | What |
|---|---|
| `1f0f36b` | Merge of `phase1/complete-prototype` at `29d4f45` (PR #6): the audit remediation and Phase A, unchanged |
| `06dfbac` | Merge of `dbb877a` (the owner's local `main`, 7 commits): `docs/ASSET_LIBRARY_FORENSIC_AUDIT_2026-09-24.md` and the seven `tools/asset_pipeline/_*audit*` scripts its Method section cites. None of the eight files is in `29d4f45` and nothing there supersedes them |
| `b5cfb27` | L-05 (below) |
| `7151816` | The profile lock on Linux, where CI runs (below) |
| this commit | This report and its evidence |

Not merged: `assets/remediation` (`b093808`). It is `e871462`'s change set applied to `dbb877a` instead of `75e6759`: its
`tools/` are identical to `e871462`'s apart from the seven audit scripts (merged through `dbb877a`), no file exists only in it, and
its `src/` lacks the remediation. Merging it would duplicate Phase A against the wrong base.

The tree is `29d4f45` plus the eight audit files, `SaveStore.cs`, `ProfileLock.cs`, two test files and these documents. No history
was rewritten and nothing was force-pushed.

Before integration: every head recorded (`heads.txt`); a bundle of all refs, `G:\UNNAMED_HISTORY\bundles\pre_phase1_integration_20260925.bundle`
(567,411,468 bytes, SHA-256 `9362d8f4396f6fb0326582da63befe43ecd270d78a107ff51c49b45a98bfa835`, `git bundle verify`: complete history).

## Asset snapshot

`F:\Otherreach_Backups\phaseA_complete_prototype_29d4f45_20260925`: 36,205 files, 37,880,330,927 bytes. Re-hashed independently for
this closeout: every file's SHA-256 matches `manifests/assets.sha256.tsv`. The package's 609 asset files are byte-identical to it.

## Fixed in this pass

- **L-05** (`b5cfb27`): `SaveStore.PreMigrationBackups` matched `pre_migration_*_<slot>` by suffix, so deleting `quick` also deleted
  `manual_x_quick`'s pre-migration copies. A copy is now a slot's only when its name is exactly `pre_migration_<schema>_<slot>`. The
  new `SlotTests.DeletingASlot_LeavesAnotherSlotsPreMigrationCopies` failed before the change and passes after. Nothing in the game,
  Application or the save tool calls `Delete` or `PreMigrationBackups`, so no build could reach the defect.
- **CI on Linux** (`7151816`): PR #6's CI (ubuntu-latest) failed three `SaveFailureTests` added by the remediation. On Unix .NET keeps
  `FileShare.None` with `flock` and reports a lock already held as an `IOException` whose HResult is the raw errno (EWOULDBLOCK: 11
  on Linux, 35 on macOS - from the .NET 8 source), so `ProfileLock` let it escape and a second copy of the game crashed instead of
  refusing the profile. It is recognised on every OS now; on Windows the condition is unchanged. A save whose predecessor is held
  open is a Windows case (POSIX moves the directory anyway), so that test is a `WindowsFact`: skipped on Linux, not passed.

## H-02: closed

`ArtLibrary`'s material and clip records are read by `ArtRecords` (typed, every field's kind checked, paths kept inside the
material's folder, no throw), and `ArtLibrary.WorldMaps`/`Info` catch every read failure per record. At unit level
`Presentation.Tests/ArtRecordsTests` covers the malformed-record matrix. At runtime, thirteen records broken at once (six materials,
seven clips - null, wrong kinds, negative lengths, an escaping path, not JSON, empty) each gave one line naming it and why, greybox for
that one thing, 0 exceptions, no scene-wide fallback, and a smoke PASS; clips first asked for during play were reported once, not
every frame; the coverage gate counted them as unexpected. A binding naming a model that does not exist is one line and one greybox
element. Details: `docs/acceptance/phase1_closeout/h02_malformed_records.md`.

Residual, carried to M7 (not a player-reachable defect): a creature binding in `src/Presentation/Art/art_bindings.json` with no `model`
key makes `CreaturesView.Draw` throw every frame (`ArgumentNullException` in `ArtLibrary.Rigged`), which ends that frame's
`Main.Draw` early. The file is part of the repository and is built into the package's `.pck`; the committed file has a model for
every creature and person. The follow-up is a shape check of the bindings in `Presentation.Tests`, or skipping a binding with no model
the way projectiles already are. A wrong kind anywhere in the bindings refuses the whole file with one warning and draws greybox.

## The combined regression gate

On the integration head (`b5cfb27` for the long runs; `7151816` rebuilt and re-run through build, tests, smoke and input - the
ProfileLock change leaves Windows behaviour identical). Godot 4.7.2 mono, console binary. `UNNAMED_ASSET_ROOT` unset throughout.

| Stage | Result |
|---|---|
| Build | `src/UNNAMED.sln` and `src/Presentation`: 0 errors (2 existing nullable warnings in `MagicContent.cs`) |
| Tests, Windows | **825 of 825 passed, 0 skipped**: Domain 147, Application 204, Persistence 173, Content 146, World 61, Presentation 57, EntityRegistry 23, Architecture 14 (824 at `29d4f45`, plus the L-05 test) |
| Tests, Linux (PR CI) | 825 discovered, 824 passed, 1 skipped (the `WindowsFact`), 0 failed |
| Content lint | 102 definitions, 0 errors; the full-pack layout copy validates too |
| Headless smoke | PASS without assets (pure greybox) and with the snapshot: 115 drawn at boot, 0 withheld; art coverage 169 requested, 168 resolved, 1 allowed, 0 unexpected; audio 0 missing; save/load digest identical |
| Input check | PASS headless and windowed: no gameplay key reaches the world through any panel; F5 saves; a load closes a conversation |
| Layout, full pack | PASS at 1366x768, 1280x720 and 1920x1080: 24 stacks, 39 buttons on screen, the last row scrolls into view |
| Acceptance route A and B, with the snapshot | Every beat happened; the relaunch went through Continue to `manual_acceptance`; the load complete (nothing quarantined, rejected, re-derived or lost); **956 fields compared, 0 differences**; the digest equal; the death's XP debt applied exactly once |
| Deterministic replay | `state_replay.json` byte-identical across A, B, C (pure greybox, no asset root) and the package run: `sha256 f7d79bf0b813...`, as at `29d4f45`. `commands.tsv` identical once instance IDs are masked (ULIDs are minted per run by design); `state_saved.json` differs only in the character's random appearance seed and ULID order |
| Delta shots | 21 beats, exit 0; art coverage 181 requested, 180 resolved, 1 allowed, 0 unexpected |
| UI shots | 40 pictures, exit 0 |
| Resume shots | From a copy of run A's profile: Continue would load `manual_acceptance`; continued at tick 7322 |
| Visual audit | 16 shots, 0 art problems, 0 greybox fallbacks |
| Logs | No `leaked at exit`, no `a handler for ... threw`, no `building the scene ... failed`, 0 exceptions, 0 errors in any windowed run. Headless runs log `Not supported by this display server` from `keyboard_get_keycode_from_physical` (the key-label lookup) about once a frame: a headless-only message, absent in every windowed run |

One harness incident, not a product finding: the first visual audit, run beside five other game instances, hit a Vulkan device loss
(`VkResult -4`) with the RTX 5090's 32 GB of VRAM full; it was stopped and re-run alone, cleanly.

### Visual: no greybox on the route

Run A's art coverage by kind (the package run's is the same): barrier 1, building 2, cliffs 1, clip 66, container 2, creature 6,
door 2, effect 3, icon 10, node 4, person 5, scatter 1, station 2, structure 54, switch mark 4, terrain 4, water 1, weapon 2 - every
one resolved. The one fallback is `debug:discovery_rings`, the F3 developer overlay's markers, allowed by
`art_coverage_allowlist.json` (drawn only while a developer has the overlay on). **Unexpected production fallbacks: 0.** Pictures
checked by eye: the player, Kera, Sel, Renn, Tavar, the hound, the wolves, trees and rocks are real models; no mannequin, box creature
or grey cylinder. Withheld entries that nothing on the route asks for are not counted against the gate.

### Audio

230 IDs in `playable_prototype_audio_v3.json`, 217 reachable, 0 malformed. Route: 56 requested, 56 resolved, 0 missing (development);
57, 57, 0 (package). Every exit line: "asked for and missing: none".

### Save and Continue

Schema 14. Fixtures v1-v14, migration, the M6 acceptance save (schema 12 -> 14) loading and playing on, the continuation tests and
the completeness tests are in the 825. The relaunch compare above is the evidence line.

## The Windows package

`G:\UNNAMED_HISTORY\playtest_build\Otherreach_Phase1_Complete_Prototype_2026-09-25_29d4f45.zip`: 1,108,521,454 bytes, SHA-256
`302a4352e5d94fb2a986e67d767aa027b6b810ab39e87ee8786f69b84e5e2055` (re-hashed: matches). `Otherreach.exe` SHA-256
`60e07324a9b9e74f90182eb288216e4104811fc9f882dd12c3aac9f6b321f80f`. Exported by Phase A from `29d4f45` with the "Windows Playtest"
preset, the payload staged by its `stage_payload_phaseA.py` (the export command itself is not recorded - L-28, the build is still
hand-assembled).

Extracted to a fresh folder away from the repository, `UNNAMED_ASSET_ROOT` unset: 921 files; `content/` identical to the integration
head's; the 609 asset files byte-identical to the snapshot; no development path in any text file. `Otherreach.exe --headless -- --smoke`
PASS, exit 0. The acceptance route and its relaunch from the package: every beat, Continue, the complete load, 956 fields with 0
differences, the death's debt once, the replay identical to the development runs, coverage 0 unexpected, audio 0 missing, 0 errors.
The package ships the game's `.pdb` symbol files (they name the build machine's source paths; harmless for a private test).

The ZIP was built at `29d4f45`; the integration head differs from it in code only by `SaveStore.cs` (L-05, which nothing in the game
calls) and `ProfileLock.cs` (identical on Windows). It is therefore the integration build, and was not re-exported.

## RAZER (1920x1080, RTX 4070 Ti 12 GB, 60 FPS)

**Measured 2026-09-25. Verdict by the gate plan's rules: fail pending review - on one criterion, in one short segment.** Every other
gameplay segment holds 60 FPS with room to spare.

How it was run: RAZER answers no remote shell (only SMB and RDP; no credentials were tried, and an RDP session would measure a virtual
display). The owner reconnected the `\\RAZER\d` share; the package ZIP (its SHA-256 checked again on RAZER) and the kit
(`G:\UNNAMED_HISTORY\razer_gate_kit\`, `razer_gate.ps1` SHA-256 `c0f019e98683bd66a4838b58e36c94d4ce345197718e34cae8a5b4d6492a929e`) were
copied to its `D:`, and the owner started `run_razer_gate.cmd` at the console through Parsec. The kit records the machine read-only,
runs the extended performance route once cold and three times warm, with nvidia-smi each second, and writes the verdict against
sections 11-12 of the gate plan. RAZER's monitor is a 3440x1440 ultrawide, so the game ran in a 1920x1080 window (the summary's
resolution is `(1920, 1080)` in every run). Results: `G:\UNNAMED_HISTORY\razer_gate_20260925\`; the report and each run's summary are
in `docs/acceptance/phase1_closeout/razer/`.

Machine: RTX 4070 Ti 12 GB (driver 620.02), Ryzen 7 5800X3D (8 cores, 16 threads), 32 GB, Windows 11 Insider Preview 26300, High
performance plan, no overlay or capture process running, shader and pipeline caches absent before the cold run. All four runs
valid: 1920x1080, VSync disabled, every goal met, no death.

| Segment (3 warm runs) | Avg FPS | 1% low FPS | Median ms | p99 ms | Worst frame ms | Render CPU p99 ms | GPU p99 ms | 60 by the 1% low |
|---|---|---|---|---|---|---|---|---|
| conversation | 116-117 | 97-98 | 8.6-8.7 | 9.5-9.7 | 12.5-16.7 | 1.7 | 9.3-9.4 | 3 of 3 |
| **magic** | 114-115 | **48-58** | 8.4-8.5 | 11.1-12.4 | 29.9-35.9 | 1.2-1.3 | 8.7-8.8 | **0 of 3** |
| combat | 105-107 | 83-85 | 9.6-9.7 | 10.0-10.4 | 29.8-43.5 | 1.4 | 10.3-10.5 | 3 of 3 |
| loot | 104 | 89-93 | 9.5-9.7 | 10.0-10.4 | 16.7-17.2 | 0.8-0.9 | 10.2-10.3 | 3 of 3 |
| third person | 114-115 | 83-88 | 8.5-8.6 | 10.6-10.7 | 30.9-39.1 | 1.2 | 10.3-10.4 | 3 of 3 |
| first person | 120 | 88-93 | 8.3 | 10.6 | 13.2-25.0 | 1.2 | 10.1-10.2 | 3 of 3 |

- **Magic** (4.5 s, about 510 frames, so its 1% low is its worst five frames): one or two main-thread stalls at the same moments in
  every run - 1.5 s (29.9-35.9 ms) and 3.3 s (19.0-27.6 ms), when formulas are first worked - with render CPU under 1 ms and ordinary
  GPU time on those frames. They are the same moments as on the RTX 5090 (1.53 s and 3.33 s). They are not the effects' atlases
  (512x256) or the formulas' sounds (under 1 MB), the two synchronous first-use loads found in the code; the likeliest remaining cause
  is Godot preparing the effects' material shaders on first draw, **not confirmed** - it needs a profiler pass. The cold run's same
  moment was 137 ms (no shader cache yet).
- **Repeatable hitches elsewhere**, at the same place in all three warm runs, all main-thread (render CPU under 1 ms): combat at
  16.4 s (30-44 ms), third person at 43.9 s (30-37 ms) and 58.5 s (31-39 ms). Under the plan's 50 ms fail line: review items. The kit
  names as repeatable only hitches over 33 ms in every warm run, and run 1's were 30-31 ms, so its report does not list them; they are
  listed here.
- **Autosave**: the capture frame and the two after it 8.2-10.2 ms against an 8.3 ms median; the write invisible. No finding against P-01.
- **Warmup** (the first 0.3 s after the scene loads, every run): two frames of 117-150 ms. First use, not scored.
- **Memory**: 6.8-6.9 GB of the 12 GB in use on the whole GPU (nvidia-smi); Godot's own estimate 5.8 GB; working set about 1.9 GB.
- **Bound**: the GPU is busy 98% of the time at p95 and its p99 is about 10 ms against a median frame of 8.3-9.7 ms - GPU-bound at
  about 105-120 FPS. Several segments sit on frames of exactly 8.333 ms (120 FPS), which looks like a frame-rate cap on RAZER outside
  the game (the harness disables VSync, and the same package ran at about 250 FPS on the RTX 5090); it lowers the averages, not the
  verdict.

What it means: sustained frame rate is not the problem - every segment's median frame is under 10 ms and five of six hold a 1% low of
82.7 FPS or better. The miss is a handful of one-off 20-45 ms frames at four fixed moments, which a player feels as a brief hitch the
first time a formula is worked in a session, once in the first fight, and at two places on the walk. M3's exit (a) - walking the area
at a sustained 60 FPS at 1080p on RAZER - is met by the two traversal segments, with the two walk hitches noted. The 2 x 2 km capture
(M3's exit (b)) was not part of this gate and is still to run.

The smallest corrective action is narrow, with no visual compromise: profile the four moments and move that first-use work to the
load screen (a warm-up of the effects' materials and whatever the combat and walk moments first create), then re-run the kit. That is
the owner's call - accept this as the Phase-A baseline with the findings carried to M7, or have it done before the blind test.

The kit was first validated on this machine (RTX 5090, `G:\UNNAMED_HISTORY\razer_gate_kit\astral_validation_20260925\`); that run
found and fixed three faults in the kit itself, and showed the same magic stalls at the same moments.

## Remaining

For the owner's decision:
- **The RAZER result**: accept it as the Phase-A baseline, with the magic segment's miss and the three repeatable hitches carried to
  M7, or have the narrow first-use fix above done and the kit re-run first.

Carried to M7, not blockers: the bindings-shape residual above; L-25's owner ruling; the audit's deferred items (L-01, L-02, L-04,
L-07, L-08, L-28, T-04 to T-11, P-02 to P-06); the design rulings reserved to the owner (`PHASE1_AUDIT_REMEDIATION_STATUS.md`);
Phase A's own list - flat grey boundary walls, stiff 20-bone motion, Renn's and Tavar's low hands, Kera and Sel idle rather than
working, flat sprite effects, the companion able to stand in front of the camera after a load, 106 LOD rebuild failures, texture
memory about 3.8 GB.

## Readiness

**M7 base: yes, once the owner merges this into `main`** - the head is clean, pushed, green on Windows and on CI, schema 14 is stable
(fixtures v1-v14, the M6 save, the relaunch with 0 differences), every harness stage passes, the package passes, H-02 is closed and
there is no open Phase-1 technical blocker. The RAZER result is measured and waits only on the owner's decision above; it
does not change anything M7 builds on. M7 plans schema 14 -> 15 (freeze the V14 shapes, add
`SchemaV14ToV15`, a v15 fixture, per `tests/Persistence.Tests/Fixtures/README.md`).

**Blind test (3-5 testers): the package is suitable**, with two notes: on a 4070 Ti-class card testers will see about 105-120 FPS
with a brief hitch the first time each formula is worked, once in the first fight and at two places on the walk (above) - worth
fixing first only if the test is meant to judge feel; and tell testers to expect Windows SmartScreen's "unknown publisher" prompt on first launch (the
executable is unsigned). From a fresh folder it starts with nothing to install (the .NET runtime ships inside it), the start screen
offers New Game and Continue, the route shows real models and no greybox, sound plays, save and Continue work across a quit, and F1
shows the controls. That was checked on this development machine, and the same package ran unchanged on RAZER from a fresh folder; a
first launch on a machine that never had Godot or .NET installed is the one check not made. It needs a Vulkan GPU with about 6 GB of VRAM or more (texture
memory about 3.8 GB) and a 1.1 GB download.
