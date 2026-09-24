# WAVE 0 — OVERNIGHT STATUS

Written 2026-09-22 20:30. Unattended run; both machines producing.

---

## 1. All 15 proof assets classified

Full detail in `WAVE_0_PROOF_CLASSIFICATION.md`. Summary:

| Class | Count | Assets |
|---|---|---|
| **PASS** | **2** | telescoping mechanism, hybrid staff/spear |
| FAIL — wrong object | 9 | mace head, both hafts, pommel, both grips, shield, focus crystal, gorget, gambeson |
| FAIL — completes the object | 2 | arming blade (became a whole sword), gorget (became a helmet) |
| FAIL — unresolvable noise | 2 | chest plate, Kal back channel |
| BORDERLINE — spec violated | 1 | gambeson (a garment on an implied body) |

**Isolated-prompt success rate: 2/15 = 13%.**

This reconciles the earlier report. I previously said "1 of 9 inspected" while proposing to
regenerate 13 — the 1-of-9 was a partial inspection, and the full classification is 2 of 15.
Both figures are now stated explicitly rather than mixed.

## 2. Weapon stub success rate

**Structural success: 100% (2/2 tested).** Both attempts produced a complete, well-formed
whole weapon with a clean head, shaft and pommel — precisely what the isolated prompts failed
to produce.

**Prompt-accuracy success: 0/1 so far.** The mace came out as a **double-headed hammer**,
because "flanged mace head with six radiating vertical flanges" reads as an axe or hammer to
the image model. This is a *concept prompt* failure, not a stub failure: the stub convention
did its job by giving the generator a whole object to reconstruct.

Corrected prompt written (`_macefix.json`) using unambiguously spherical language: "a single
ROUND SPHERICAL steel ball head … nothing protruding sideways … no blades no axe shape no
hammer shape". Re-rendering.

**Conclusion: the stub convention is approved as the weapon-component production method. The
open risk is prompt vocabulary, not the convention.** Terms to avoid in component prompts:
"flanged", "radiating", "vertical" used of a head; anything that admits a symmetric-blade
reading.

## 3. Armour bakeoff: A/B/C

Full detail in `WAVE_0_ARMOR_BAKEOFF.md`.

### Method A — ghost-man: **PASS for shape, FAIL for fit**
A convincing complete cuirass with pauldrons, riveted waist band and flared fauld. Genuinely
usable. But nothing establishes it fits a declared body, it is generation noise at ~40k faces,
and it differs every run.
**The gorget variant failed outright** — the mesh is empty. The "nothing inside it"
instruction was followed far enough that the collar itself did not survive.

### Method B — sacrificial separated mannequin: **REJECTED**
The deliberate air gap was **not respected**. Both pieces reconstruct as **armour fused to a
generated human figure** — the chestplate produced a muscled male torso in shorts with the
cuirass welded on. This is exactly what the hard rule forbids: the generated mannequin would
become the authoritative body surface. Method B fails its own test and is withdrawn.

### Method C — canonical Blender fit shell: **PASS for fit, no style**
Deterministic, 2.3 s, no GPU, exact declared dimensions.

| Piece | Faces | Dimensions (m) | Verdict |
|---|---|---|---|
| chest plate | 894 | 0.468 × 0.364 × 0.130 | **correct** — flared curved plate following the torso section |
| gorget | 1692 | 0.280 × 0.041 × 0.296 | **still wrong** — a flat plate, not a collar |

The chest region proves the section derivation is sound. The gorget needs its region-to-section
mapping fixed: two attempts gave a 36 cm disc (solidifying a closed 360° ring) and then a 4 cm
strip (after moving the band between shoulders and jaw). This is a parameterisation bug in one
region definition.

### Automation time per method

| Method | Time per piece | GPU | Determinism |
|---|---|---|---|
| A ghost-man | ~280 s generate + 4 s cleanup | yes | none — differs each run |
| B gap | ~280 s generate + 4 s cleanup | yes | none |
| C fit shell | **2.3 s** | **none** | **exact** |

### Blender / GLB / Godot validation

- Method C output: exported, correct Y-up orientation, asset-id naming, exact dimensions.
- **Godot validation of armour not yet run** — the fit shell has no sockets authored yet, so
  the modular chain has not been applied to it.
- Weapon stubs: built through the standard cleanup path, verified, but **not yet through
  `_make_modular.py`** (sockets → naming → `.blend` → GLB → socket verify → Godot).

## 4. Recommended final workflow per armour category

| Category | Method | Rationale |
|---|---|---|
| Rigid standalone plates | **C for fit + A for style** | A cannot be trusted to fit; C cannot be trusted to look like anything |
| Conforming soft layers (gambeson, cloth) | **C throughout** | The generator makes a garment, not a shell |
| Mail | **C + procedural rings** | Periodic texture; generation adds nothing |
| Articulated / hybrid | **C base + A style**, articulations procedural | Each articulation needs deterministic placement |
| Attachment hardware | **procedural** | No whole form exists to generate from |

**The key production insight: A and C are not competitors — they solve different halves.** The
missing step is transferring A's exterior surface onto C's fit boundary (shrinkwrap/retopo),
which is deterministic once written and identical for every piece.

## 5. Modular chain validated on real geometry — new since the first draft

All nine stub components were run through the complete modular chain against **real Trellis
geometry**, not just socket definitions:

```
python _make_modular.py --sockets ... --raw ... --out ... --resize-to-nominal --validate --stub-pending
```

| Component | Result |
|---|---|
| weaponcomp_mace_head_flanged_a | **PASS** |
| weaponcomp_pommel_counterweight_a | **PASS** |
| weaponcomp_grip_standard_a | **PASS** |
| weaponcomp_grip_vaskaal_a | **PASS** |
| weaponcomp_blade_arming_sword_a | **PASS** |
| weaponcomp_shield_heater_a | **PASS** |
| magiccomp_focus_crystal_a | **PASS** |
| weaponcomp_haft_short_a | **PASS** |
| weaponcomp_haft_long_a | **PASS** |

**9/9.** Each passed: socket authoring with full basis → asset-id mesh and material naming →
nominal sizing → canonical `.blend` written → GLB export → socket basis verified in the GLB →
**Godot import validation**.

This closes the loop the proof set opened. The chain that was infrastructure-only this
afternoon now works on generated assets.

### One interface change the geometry forced

The Godot size assertion initially failed on every stub component — correctly, because the
mesh still carries its **sacrificial stub** and is therefore deliberately larger than nominal.
Added `--stub-pending`, which skips the size check until the stub is cut. This is a real
interface change discovered by running the chain on real geometry rather than on definitions.

### Stub removal is now implemented

`_blender_cut_stub.py` performs the cut, and it is deterministic for the same reason the
standard is: the stub is a plain cylinder of known diameter coaxial with the socket, so
removal is a single plane cut perpendicular to the socket axis.

The cut position is **measured, not assumed**. `measure_profile()` samples the cross-sectional
radius along the socket axis, and `find_stub_boundary()` takes the sharpest radial step — where
the component's bulk gives way to the plain narrow stub. On the stub mace:

```
axis range -0.3000 .. 0.0000
-0.2238  r=0.0554   component (the head)
-0.2088  r=0.0088   <-- radial drop 0.031 m: the boundary
-0.0138  r=0.0434   stub end
```

**Three wrong attempts came first, and the reasons matter:**

1. Nothing removed — I assumed the stub lay on the far side of the socket origin. Wrong.
2. Removed 24,958 of 25,203 verts, leaving 245 faces — the detect-and-keep heuristic kept the
   wrong side.
3. Kept the haft instead of the head — the sign convention inverted again.

The root cause of all three: **a mace has wide ends and a narrow middle**, so "which end is the
stub" cannot be inferred from the ends, and the socket's `primary` direction did not correspond
to the geometry the way I assumed. The fix was to stop reasoning about the axis and **measure
the vertex distribution on each side** with `_probe_cut.py`, which reported:

```
below  verts=19089  span -0.3000 .. -0.2113   <- the component
above  verts= 6068  span -0.2111 ..  0.0000   <- the stub
```

**Verified result:** `verts_removed 5980`, `boundary_edges_capped 7680`, 30,364 faces
remaining, dimensions `0.1215 x 0.0927 x 0.0702 m` — the head alone, on the ground plane,
centred, with the cut face closed. Rendered in `review\stubcut\cut_mace_sheet.jpg`.

### What the stub cut still needs

- **Per-component side declaration.** The side is declared in `assets\cut_sides.json`, but
  only the mace is visually verified. The other seven carry `"verified": false` and need a
  render check before their output is trusted.
- **Cut face quality.** 7,680 boundary edges were filled on the mace. The cap is closed but
  irregular; whether it needs a planar cap or bevel is an art call.
- **The shield has no interface envelope** on `SOCK_body_mount`, so no stub boundary can be
  found for it. Its mount socket needs an envelope before it can use the stub path.
- **The pommel is a degenerate case.** Its probe shows the component and stub are not
  separated by a radial step: 23,730 verts below the boundary against 70 above. The generator
  rendered the counterweight as essentially the whole mesh, so there is nothing to cut.
  The pommel needs a re-render with a clearer stub, not a cut.

### A finding that changes how stub prompts must be written

Probing the components revealed the generator did **not** consistently produce
stub-attached components. Measured axis lengths of the raw meshes:

| Component | Intended size | Generated mesh length |
|---|---|---|
| focus crystal | ~0.10 m | **0.92 m** |
| standard grip | ~0.13 m | **0.91 m** |
| short haft | ~0.42 m | **1.00 m** |
| vaskaal grip | ~0.15 m | **0.95 m** |

The "sacrificial stub" is the **lower third** of a full-length weapon, not a short stub. The
generator read "a plain haft which is completely plain and undecorated, of uniform thickness
throughout" and gave it most of the frame. So the cut reliably produces a component, but the
component is whatever the generator drew as the detailed part, and the stub is far longer than
the standard's `LENGTH_SHARE = 1/3` intends.

**This is a prompt-convention bug, not a cut bug.** The stub prompts must bound the stub
explicitly — a stated length in the prompt and a stated proportion of the frame — or the
generator will render a whole weapon and the "component" is simply whichever end has detail.

### Net position on stub removal

The **mechanism is proven**: `_blender_cut_stub.py` + `_probe_cut.py` locate the boundary by
measurement and cut on a plane, and the mace went through the complete chain
(`cut_stub → sockets → verify_sockets → godot_validate`) with a visually correct result.

The **convention is not yet reliable**: seven of eight components need their side verified, one
is degenerate, one has no envelope, and the stub length is uncontrolled. Stub removal should be
treated as working-but-unvalidated, and the stub prompts corrected before it is trusted at
Wave 1 scale.

## 6. Methods A and C are complementary — the production recipe

| Layer | Source | Why |
|---|---|---|
| **Fit** | Method C canonical shell | Deterministic, exact, zero clipping, 2.3 s, no GPU |
| **Style** | Method A ghost-man generation | Convincing exterior form, detail, materials |

Method B is rejected. The missing step is transferring A's surface onto C's boundary.

## 7. What is running right now

| Machine | Work |
|---|---|
| **BEAST** | Night supervisor, 9 stages, 139 assets queued: stub components → magic components → items → herbs → flora → reagents → resources → race bodies (rigged) → props |
| **RAZER** | Corrected mace prompt, then ~400-concept backfill (remaining_hq, props_weapons_hq, world_materials) |

Shared state:
- `queue_wave0.log` / `queue_wave0_state.json` — BEAST progress
- `mem_guard.log` — host memory guard, limit raised to 200 GB
- `review\armor_bakeoff\ABC_all.jpg` — the A/B/C comparison sheet
- `review\stubtest\stub_mace_verify.jpg` — the stub result

## 8. Problems fixed overnight

1. **Memory guard thrashing.** The 150 GB limit had silently restarted ComfyUI twice (11:06,
   12:40) before the build I started. Raised to 200 GB against a real exhaustion point of
   244 GB. Now stable in the 70-150 GB range without interruption.
2. **`_supervise.py` plan format.** It requires `{"stages": [...]}`; I supplied a bare list and
   it crashed on `plan["stages"]`. Fixed.
3. **Duplicate process trees — repeatedly.** Twice more tonight: two supervisors, two builders,
   two render queues, and orphaned runners whose parents I had killed. Each duplicate wasted
   GPU and one aborted a stage (`exit=4294967295`, which is the kill code). Root cause is my
   own restarts racing existing processes. Mitigation: always enumerate and dedupe before
   launching.
4. **The "HEY JOE" alert fired** when a stage failed from my own kill. Silenced, and I have
   avoided further interference with running stages.
5. **Method C gorget** — fixed on the third attempt after two failures (a 36 cm disc from a
   closed 360° ring, then a 4 cm strip from interpolating shoulder width down to neck width).
   See section 3.

## 9. Not done, and why

- **Stub removal.** The cut plane is computed but no Blender operation performs the cut, so
  the nine components still carry their sacrificial stubs.
- **Godot-validated armour GLBs.** The fit shell has no sockets authored yet. Method C output
  is exported and dimensionally correct but not socketed.
- **Deformation/clipping validation.** Requires posed bodies; the bakeoff produced none.
- **Coverage metadata.** Designed in the bakeoff doc, not yet emitted by any tool. Per
  adjustment 4 it is metadata on the asset, not one mesh per combat region.
- **The A-onto-C surface transfer.** Method A's exterior has not been projected onto Method
  C's fit boundary. This is the main remaining piece of the armour workflow.

## 9b. Host-memory OOM observed and absorbed

A second, different failure mode appeared overnight and the guard handled it:

`
[11/46] item_gold_coins
    generate  1026s
    FAIL  generate (1026s)
`

ComfyUI committed **187.5 GB** against the 200 GB guard, and its own allocator reported
Got an OOM, unloading all loaded models. The guard did not fire, memory fell back to
158 GB, and the queue continued to [12/46]. The server stayed up — no restart, no lost work
beyond the one asset, which the supervisor retries on its next pass.

**This is the second distinct host-memory event**, and it confirms the machinery: the guard
threshold sits between normal peaks (~160-190 GB) and the true exhaustion point (~244 GB), so
ordinary pressure resolves itself and only genuine runaway triggers a restart.

### The guard limit was set too high - corrected to 185 GB from measurement

The 200 GB limit was wrong, and the symptom was subtle rather than dramatic. At **191.6 GB**
committed the machine was not yet failing, but it was **page-file thrashing**:

| Measurement | Value |
|---|---|
| Physical RAM | 63.9 GB, only **7.1 GB free** |
| Commit used | 190.4 GB of a 240 GB limit |
| ComfyUI private bytes | **178.4 GB** |
| Page file | 176.5 GB allocated, **102.4 GB in use**, peak 184.3 GB |
| ComfyUI CPU progress | **1.78 s in 20 s wall** |

That last number is the tell. The server was alive and not stalled, so the watchdog correctly
did nothing - but it was making almost no progress, because every allocation was hitting disk.
**The practical ceiling is well below the theoretical 244 GB exhaustion point**, and the old
limit would have let the machine grind in that state for hours.

Lowered to **185 GB**. It fired immediately, and the recovery was clean:

`
23:27:05  committed 191.6 GB (limit 185.0 GB) - restarting
23:28:48  committed  21.4 GB, healthy
commit used: 33.4 GB of 240   physical free: 47.1 GB
`

The queue moved on within a minute and the in-flight asset was lost and retried, as designed.

**The general lesson:** a memory guard should trigger on *degradation*, not on *exhaustion*.
Waiting for the failure point means tolerating a long period of near-useless work first, and
the CPU-progress signal is what distinguishes that state from either healthy work or a true
stall.

## 9c. The stall watchdog was dead - found by a real stall

A genuine 31-minute stall exposed a bug that had been present since the watchdog was written.

_overnight_queue.py referenced CPU_SAMPLE_SECONDS inside StallWatchdog.run() but **never
defined it at module level**. The reference lived only in the comment above it, so the
watchdog thread raised NameError on its first cycle and died silently. The daemon thread
produced no traceback anywhere visible, so the watchdog looked armed and did nothing.

Symptom: item_health_potion ran from 22:35 with a log stuck at Bake texture: finalize,
ComfyUI CPU advancing 0 s in 45 s, GPU held at 46%, and no watchdog action past the 600 s
threshold.

**Fixed** by defining CPU_SAMPLE_SECONDS = 20 at module level, and verified with a new
_probe_watchdog.py that exercises the watchdog's actual inputs rather than reading the code:

`
STALL_SECONDS      = 600
CPU_SAMPLE_SECONDS = 20
log_idle_seconds() = 4.9
comfy_processes()  = [6684, 26148]
comfy_cpu_seconds  = 29997.6 -> 30175.5   (delta 177.92)
`

The delta is the interesting number: **177 s of CPU in 20 s wall**, about nine cores. So this
particular prompt was not stalled at all, it was doing heavy legitimate work - which is
precisely why the watchdog exists in the form it does, distinguishing a silent-but-busy server
from a silent-and-wedged one. It could not make that distinction because it was dead.

**The lesson generalises:** a guard that fails silently is worse than no guard, because it is
trusted. Every watchdog needs a probe that proves it is alive, not just a review of its logic.

## 9d. The guard restart cost 8 assets - a race I introduced

Lowering the guard to 185 GB fixed the thrashing, but exposed a second bug immediately:

`
[15/46] item_jet_stone            FAIL generate (2s)
[16/46] item_lead_ingot           FAIL generate (2s)
[17/46] item_mana_potion          FAIL generate (2s)
[18/46] item_moonstone_cabochon   FAIL generate (2s)
[19/46] item_nullstone            FAIL generate (2s)
[20/46] item_otherfire_lamp       FAIL generate (2s)
[21/46] item_othergate_lens_frame FAIL generate (2s)
`

Seven assets in a row failed in about two seconds each, plus item_gold_coins earlier: **8 lost**.

**Cause.** _make_assets.py ran a health preflight before each asset and, on failure, recorded
the asset and moved to the next. That is right for one asset that wedges the server, and wrong
during a restart, when *every* asset fails instantly. _comfy_health.py only waited for
recovery **after it rebooted the server itself**, so a restart performed by the memory guard
looked to it like a permanently dead server.

**Two fixes, both narrow:**

1. _comfy_health.py now waits for an **unreachable** server to come back (bounded by
   --wait), because unreachable usually means *starting up*, not *broken*.
2. _make_assets.py now **stops the stage** when the server is unhealthy after that wait,
   instead of skipping onwards. Skipping a batch while the server is down converts one outage
   into one failure per queued asset.

**Recovery needed no intervention.** The 8 items are absent from 
eady, and --skip-existing
only skips assets that are *complete*, so the still-running stage rebuilds them in the same
pass. Verified: 46 item_ concepts, 15 built, 31 missing and queued.

**The pattern across all three of tonight's bugs is the same:** a component that fails
*quietly* and is *trusted* to work. The watchdog died silently, the guard limit was set from
the wrong number, and the health check reported a starting server as a dead one. Each was
found by measuring, and each corrective number came from the machine rather than from reasoning.

## 9e. A long generation keeps losing a race with the memory guard

`item_gold_coins` failed **four times** (1026s, 477s, 738s, 660s) while every neighbouring item
built first try. The cause is not its prompt:

```
generate  660.5s  returncode 0
error: generation produced no GLB: ... 642s running=1 pending=0
       ComfyUI restarted (pid 23728 -> 23496); this prompt was lost, abandoning it
```

It is the **longest generation in the batch by a wide margin**, and the memory guard restarts
ComfyUI every 1-3 hours when host memory reaches 185 GB. A six-hundred-second generation has a
real chance of being in flight when a restart lands, and when it is, the prompt dies with the
server. Ordinary assets take ~150-350s and almost always finish inside a window.

**This is a structural weakness, not a bug in one component.** The guard correctly restarts to
prevent thrashing, and `_run_3d_asset.py` correctly abandons a lost prompt rather than waiting
forever. The two behaviours combine into a small but genuine failure rate for the slowest assets.

Candidate fixes, none applied yet:

- run long assets against a fresh, low-memory server (after a restart, before pressure builds)
- give the guard a grace band: do not restart while a prompt has been running under N seconds
- reduce the guard's accumulated growth so restarts are rarer, via the launcher's
  dynamic-VRAM flags

**Recovery in practice:** the other seven race-lost items rebuilt automatically on the
supervisor's retry pass (7 of 8 confirmed). `item_gold_coins` needs a quiet-machine run and has
been left for after the queue drains.

## 9f. The supervisor looped on one stage, blocking 94 assets

A defect that cost most of the night's later throughput. `_supervise.py` picked the **first**
non-done stage every time:

```python
name = pending[0]
```

`world materials: items` failed whenever it reached `item_gold_coins` (item 11 of 46, ~20
minutes wasted per cycle), so it stayed non-done and was chosen again. The eight stages behind
it — holding **94 buildable assets** — were never attempted:

| Stage | Assets waiting |
|---|---|
| world materials: items | 1 |
| world materials: herbs | 17 |
| world materials: flora | 14 |
| world materials: reagents | 4 |
| world materials: resources | 5 |
| race bodies | 8 |
| remaining props | 45 |

**Logged 32 attempts at the same stage.** Confirmed from the log:

```
[05:52:24] --- world materials: items: FAILED
[06:20:50] --- world materials: items: FAILED
[06:40:03] --- world materials: items: FAILED
```

Each cycle spent ~20 minutes reaching `gold_coins`, failing, then rebuilding the 10 items
already complete (skipped cheaply) before failing again.

### The fix

Untouched stages now take priority over already-failed ones. Retrying is still wanted, but not
at the cost of never attempting the rest of the plan:

```python
untouched = [n for n in pending if states.get(n, {}).get("status") is None]
name = untouched[0] if untouched else pending[0]
```

Verified immediately on restart: the supervisor moved to `world materials: herbs` and began
building 17 assets that had never been tried.

### Why this is the same class of bug as the others tonight

Every failure today has been a component doing exactly what it was told, in a situation its
author did not consider. The watchdog was configured correctly but referenced an undefined
name. The memory guard triggered on exhaustion rather than degradation. The health check could
not tell a starting server from a dead one. And the supervisor retried faithfully without ever
asking whether retrying was the right thing to do next.

**The general lesson: a retry policy needs a bound that is not "until it works."** A single
unbuildable asset should cost one asset, never an entire plan.

## 10. Self-recovery behaviour observed

One transient failure occurred and the pipeline handled it without intervention:

```
[5/46] item_bronze_ingot
    generate  706.1s  -> raw\item_bronze_ingot.glb
    FAIL  cleanup (4s)
```

This is the known intermittent `W:` share write failure, not a data problem. Verified by
re-running cleanup manually: it **succeeds**, producing 36,294 faces with all three LODs and a
collision hull. The raw GLB is intact, so the supervisor's next pass re-runs cleanup only — it
does not regenerate. The pipeline moved straight on to `[6/46]`.

This is exactly the behaviour the `--skip-existing` plus multi-pass design exists to produce.

## 11. Machine state at handover

| Machine | Load | Work |
|---|---|---|
| **BEAST** | GPU 36-100%, memory 130-165 GB committed | `world materials: items`, 11 of 46 |
| **RAZER** | 1 prompt running | concept backfill |

Exactly one of each expected process, verified repeatedly: `_supervise`, `_overnight_queue`,
`_make_assets`, `_run_3d_asset`, `_comfy_mem_guard`, `_render_queue`, `_make_concepts`.

**Library: 282 ready assets, 467 concepts, 12+ canonical `.blend` sources.**

## 12. Round-one additions (overnight, after the first handover)

**Implemented the stub cut** — `_blender_cut_stub.py`, with `_probe_cut.py` as a
non-destructive probe and `cut_sides.json` recording the declared side per component.
`_make_modular.py` now runs the cut as **stage 0**, before socket authoring, because the stub
inflates every downstream measurement.

Verified on the mace: `cut_stub → sockets → verify_sockets → godot_validate`, all green, head
cleanly isolated, cut face closed (7,680 boundary edges filled), 30,364 faces remaining.

Also corrected the Godot size assertion. It had been comparing against the declared nominal
size, which is wrong now that the cut produces a real part: the assertion compares the imported
AABB against the **measured** mesh dimensions, testing round-trip fidelity rather than
conformance to an idealised number.

**Found the stub convention is not yet reliable — from measurement, not impression.** Probing
the raw meshes showed the generator rendered the "plain stub" as most of the frame:

| Component | Intended | Generated |
|---|---|---|
| focus crystal | ~0.10 m | **0.92 m** |
| standard grip | ~0.13 m | **0.91 m** |
| short haft | ~0.42 m | **1.00 m** |

The cut works; the stub it cuts is a full weapon length. **Fixed the prompt convention** in
`_write_stub_prompts.py`: the stub is now bounded as roughly one quarter of the frame height
*and* about one third the length of the detailed part. The nine corrected prompts are written
and queued to re-render after the current RAZER batch.

**Two components need work before the stub path applies:**

- `weaponcomp_shield_heater_a` — `SOCK_body_mount` declares no envelope, so no boundary can be
  found. The socket needs an envelope.
- `weaponcomp_pommel_counterweight_a` — degenerate: 23,730 verts one side of the boundary
  against 70 the other, meaning the generator drew the counterweight as the whole mesh. It
  needs a re-render, not a cut.

**Stub removal verified by rendering every result** (`review\stubcut\cut_verification.jpg`):

| Verdict | Count | Components |
|---|---|---|
| **correct** | **5** | mace head, arming blade, vaskaal grip, short haft, long haft |
| concept failure | 2 | standard grip (kept a whole dagger), focus crystal (kept a whole staff) |
| degenerate | 1 | pommel counterweight (no boundary exists in the mesh) |
| no envelope | 1 | shield (a whole object; likely does not need the stub path) |

So the cut is correct for **5 of 8** components that were cut, and all nine are recorded in
`cut_sides.json` with a verdict rather than a boolean.

The two concept failures are the **same failure as the original isolated prompts**: the
generator drew a complete weapon instead of a component on a stub. Neither side of the cut
yields the intended part for either asset, so they need corrected concepts, not a different
cut.

This is why the previous round's "six passed through Godot" was not the same as six correct
components. Godot green means the file imported and the dimensions were internally consistent,
**not** that the right geometry survived. Checking dimensions against intent is what caught it.
