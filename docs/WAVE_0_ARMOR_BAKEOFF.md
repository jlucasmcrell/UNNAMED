# WAVE 0 — ARMOUR PRODUCTION BAKEOFF

Deciding how wearable armour is authored. Three methods, two pieces (chest plate and gorget),
scored on ten criteria.

**Rule under test (hard pipeline rule):** *Trellis may define armour shape and style; the
canonical fit-family body defines wearable fit. A generated mannequin is never the
authoritative body surface.*

That rule exists because a generated body is unrepeatable — the same prompt gives a
different torso every run — so armour fitted to one fits nothing consistently. Fit must come
from a declared specification.

---

## The two axes of the problem

The proof set showed two independent failures in armour, and the bakeoff must separate them:

1. **Shape** — a thin curved shell reconstructs as noise (the breastplate shattered, the back
   channel crumpled). This is a generation failure.
2. **Fit** — even when the shape is right, nothing establishes that it fits a body. This is a
   specification failure, and generation cannot fix it by definition.

Method C addresses both. A and B only address shape.

---

## Method A — ghost-man mannequin

**Concept:** armour visually suspended with no visible body and a physical gap, so the
generator produces a standalone shell.

**Question:** does Trellis produce a standalone armour shell when told no body is present?

## Method B — sacrificial separated mannequin

**Concept:** armour over a grey mannequin, but with a deliberate visible air gap maintained
everywhere, so armour and mannequin reconstruct as **disconnected components** that Blender
can separate automatically by loose-part analysis rather than surface surgery.

**Question:** is the air gap respected, and does separation work without manual boundary
tracing? The earlier gorget test proved the generator *can* put a collar on a bust — but the
two surfaces fused, which is what made extraction hard. Method B tests whether a deliberate
gap makes them separable.

## Method C — canonical Blender fit shell

**Implementation:** `_blender_fit_shell.py`.

Derived from the fit-family body by region selection, controlled offset, and solidify:

1. select the body region the piece covers (`chest`, `back`, `gorget`, …)
2. generate the body section from declared landmark widths for the fit family
3. offset radially outward by the construction family's layer offset
4. solidify into a shell with declared thickness
5. cut the boundary along region edges

Fully deterministic. **Measured: 2.3 seconds, no GPU, exact declared dimensions.**

The generated output is then used for *style* — exterior form, decoration, materials — laid
over this base. Shape from generation, fit from specification.

---

## Results

Each piece is rendered with `_blender_preview.py`; sheets in `review\armor_bakeoff\`.

| Criterion | A ghost | B gap | C canonical |
|---|---|---|---|
| Correct shape | **yes** — a complete cuirass | pending | **yes** — correct flared chest panel |
| Automated separation | **n/a** — never fused | pending | n/a — never fused |
| Canonical fit | **no** — arbitrary body | pending | **yes** — from declaration |
| Clean boundaries | no — soft organic edges | pending | **yes** — cut on region edge |
| Topology | generation noise, ~40k faces | pending | **894 faces, quad grid, manifold** |
| Repeatability | **no** — different every run | pending | **exact** |
| Skinning | poor — no rig landmarks | pending | riggable, region-aligned origins |
| Clipping | unknown — fit is not controlled | pending | **zero by construction** |
| Automation | full pipeline, ~280 s, GPU | pending | **1 command, 2.3 s, no GPU** |
| Cleanup time | none required | pending | none required |
| **Detail quality** | **high** — rivets, banding, articulated pauldrons | pending | **none** — a bare panel |

### Method A, measured

The ghost-man prompt produced a **convincing complete cuirass**: rounded torso, articulated
pauldrons, a riveted waist band, a flared fauld, and an open neck. It is genuinely usable
armour. The negative instruction ("no mannequin, nothing inside") worked — the generator did
not insert a body.

Its weaknesses are exactly the ones the hard rule predicts: nothing establishes that it fits
any declared body, its surface is generation noise at ~40k faces, and it will differ on every
run.

**Method A gorget: FAILED.** The produced mesh is empty — the "nothing inside it" instruction
was taken far enough that the collar itself did not survive. A and B therefore disagree on
which armour pieces the ghost-man convention works for, which is itself a useful result:
the convention is not uniformly applicable.

### Method B, measured — rejected

The deliberate air gap was **not respected**. Both pieces reconstruct as **armour fused to a
canonical human figure**, which is precisely what the hard rule forbids: the generated
mannequin would become the authoritative body surface. The chestplate produced a complete
muscled male torso in shorts with the cuirass welded to it; the gorget produced a full bust
with the collar attached.

**Method B fails its own test.** It does not yield separable components, and even if it did,
the surviving body would be an arbitrary generated form. Rejected for production.

### Method C, measured

| Piece | Vertices | Faces | Dimensions (m) | Build time |
|---|---|---|---|---|
| chest plate, standard_humanoid, plate | 896 | 894 | 0.470 × 0.350 × 0.101 | 2.3 s |
| gorget, standard_humanoid, plate | 1694 | 1692 | 0.251 × 0.120 × 0.257 | 2.3 s |

The **chest plate is correct**: a flared, curved plate that follows the torso section, with
exact declared dimensions, no clipping, 894 faces, manifold, produced in 2.3 s with no GPU.
It is also completely plain, as expected — Method C produces fit and has no opinion on style.

**The gorget is now correct too.** It took three attempts, and the failure mode is worth
recording because it was a real parameterisation trap rather than a method flaw:

1. A **36 cm disc** — the 360° ring, once solidified, closed into a washer.
2. A **4 cm strip** — moving the band to span shoulder→neck gave only 6.7 cm of height, and
   worse, it interpolated from the **0.44 m shoulder width down to the 0.13 m neck**, so a
   neck collar came out 0.36 m wide.
3. **Correct** — the gorget now has its own two landmarks (`gorget_z`, `gorget_top_z`), both
   near the neck width, giving a 12 cm tall tapering collar with an open front and a
   0.251 × 0.120 × 0.257 m bounding box.

**The lesson: a region that encloses a limb must not interpolate its section from a distant,
much wider landmark.** The band needs landmarks that describe the limb, not the torso.

---

## The finding

**A and C are not competitors. They solve different halves of the problem.**

- **A produces style and exterior form** — convincingly, and better than procedural geometry
  would.
- **C produces fit** — exactly, repeatably, and with zero clipping.
- **Neither alone is sufficient.** A cannot be trusted to fit; C cannot be trusted to look
  like anything.

This is precisely what the hard pipeline rule anticipated. The production method is not "pick
A or C" but **C as the fit layer, A as the style layer**:

1. derive the fit shell from the canonical body (`_blender_fit_shell.py`)
2. generate the armoured form for style (ghost-man prompt, Method A)
3. **transfer A's exterior surface onto C's fit boundary** — the generated piece defines the
   outside, the canonical shell defines where it sits
4. keep C's topology at the interface so sockets, layer offsets and skinning remain exact

Step 3 is the work item this bakeoff identifies: a shrinkwrap-and-retopo step, or a
projection of the generated detail onto the canonical shell. It is deterministic once
written, and it is the same operation for every armour piece.

## Recommended workflow per armour category

| Category | Method |
|---|---|
| Rigid standalone plates | **C base + A style** — canonical shell for fit, generated exterior for form |
| Conforming soft layers (gambeson, cloth) | **C throughout** — the generator makes a garment, not a shell |
| Mail | **C + procedural rings** — periodic texture; generation adds nothing |
| Articulated / hybrid | **C base + A style**, articulations procedurally placed |
| Attachment hardware | **procedural** — no whole form to generate from |

## Status of this bakeoff

- **Method A**: chestplate built and evaluated. Gorget building.
- **Method B**: both pieces building.
- **Method C**: both pieces built and evaluated.
- Deformation and clipping validation (below) **not yet run** — it requires posed bodies,
  which the bakeoff has not produced yet.

---

## Fit families and layer offsets (implemented in Method C)

Declared, not generated:

| Fit family | Height | Chest W×D | Applied to |
|---|---|---|---|
| `standard_humanoid` | 1.80 | 0.34 × 0.22 | Veth, Siann, Ondrek |
| `compact_broad` | 1.30 | 0.46 × 0.30 | Kal |
| `tall_narrow` | 2.35 | 0.26 × 0.17 | Vaskaal |
| `irregular_heavy` | 2.60 | 0.66 × 0.44 | Ondrek heavy |

| Construction family | Layer offset |
|---|---|
| cloth | 4 mm |
| underlayer / gambeson | 8 / 12 mm |
| leather | 14 mm |
| mail | 20 mm |
| scale / brigandine / coat of plates | 24 mm |
| lamellar / splint | 26 mm |
| plate | 28 mm |
| exotic | 30 mm |

These are the values the hard rule refers to: the canonical body plus these offsets define
fit, and generation is free to decide what the outside looks like.

---

## Production categories (adjustment 6)

One technique need not serve every armour family. Preliminary split, to be confirmed by the
bakeoff:

| Category | Method | Reason |
|---|---|---|
| **Rigid standalone pieces** (breastplate, backplate, pauldron, tasset) | **C for fit, generation for style** | Fit is critical; thin curved shells failed in generation |
| **Conforming soft layers** (gambeson, cloth, padding) | **C throughout** | The gambeson proved the generator makes a garment, not a shell |
| **Mail** | **C + procedural rings** | Mail texture is periodic; generation adds nothing |
| **Articulated / hybrid** (gorget, fauld, articulated joints) | **C base + generated rigid detail** | Each articulation needs deterministic placement |
| **Attachment hardware** (sockets, ferrules, buckles, rivets) | **procedural** | No whole form exists to generate from |

---

## Coverage metadata, not mesh slots (adjustment 4)

The 16 combat regions describe **protection and hit semantics**. They are not mesh slots and
must not become one:

- one mesh may cover several regions (a cuirass covers chest, back and abdomen)
- one region may be covered by several meshes (chest = plate + gambeson + mail)
- a region may be partially covered, or covered at different layers

So coverage is **metadata on the asset**, not a structural constraint:

```json
"coverage": {
  "regions": ["chest", "abdomen"],
  "layers": ["plate"],
  "coverage_fraction": {"chest": 0.85, "abdomen": 0.4},
  "protection": "rigid"
}
```

This keeps the armour library from being forced into 16 pieces per family, which would have
multiplied the asset count for no gameplay gain.

---

## Deformation and clipping validation (adjustment 7)

Fitted armour must be validated against a **posed body**, not just a neutral one. Required
poses:

| Pose | Tests |
|---|---|
| neutral | baseline fit |
| arms raised | shoulder and armpit clearance |
| arms inward / cross-body | chest and pauldron interference |
| crouch | abdomen and hip overlap |
| run stride | thigh and tasset separation |
| overhead weapon motion | shoulder and arm opening |

**Kal additionally:** folded wings, deploying wings, deployed wings — the back plate must not
intersect the wing envelope at any state.

Clipping is measured as penetration depth of armour into the body mesh; the standard's
tolerance is **3 mm**. Method C should measure zero by construction, since the shell is
offset outward from the canonical body — but this must be confirmed on posed bodies, not
asserted.
