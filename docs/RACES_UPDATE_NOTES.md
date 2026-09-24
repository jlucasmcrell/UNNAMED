# RACES.md biology update — what changed and what it breaks

The owner supplied a revised `RACES.md` on 2026-09-22 (29846 bytes, replacing 12505).
This records what the revision changed, and which existing assets no longer match it.
The previous version is kept at `RACES.md.bak-before-biology-update`.

---

## New material

Four whole sections that did not exist before:

- **Visual and morphology design principle** — shared body-plan ancestry but distinct
  silhouettes. Introduces the **solid-black silhouette test**: if two races are hard to
  tell apart in neutral poses at gameplay distance, their proportions or posture need
  another pass.
- **Biology, aptitude and culture are separate** — innate biology cannot be learned;
  aptitude is an advantage not a monopoly; cultural knowledge is *taught*, so a human
  raised among Vaskaal can know their engineering better than a Vaskaal raised in
  Otherhome.
- **Body-plan, armor and animation production guidance** — a small number of *fit
  families* rather than bespoke equipment per race, with crafting able to **refit** gear
  between compatible families.
- **Future lineage, hybrid and mutation design** — compatibility grades, trait
  expression rather than averaged stats, and enhancements that cost something.

---

## Per-race biology changes

| Race | What changed |
|---|---|
| **Veth** | Now subtly non-human rather than plain: elongated forearms and lower legs, long fingers and toes, unusually mobile shoulders/hips/wrists/ankles, a narrower ribcage, reflective irises. Uncanny only *in motion*. |
| **Kal** | **Major: large folding dorsal wings.** Collapse into channels beside the spine, partially hideable under cloaks and fitted armor. Not capable of sustained flight while armoured; used for controlled descent, braking, jump assistance and short gaps. Possible low-gravity ancestral origin. |
| **Siann** | Opal-like, faintly translucent skin with **silver-lavender subdermal structures** in thin areas, pale lavender eyes with a **brighter inner ring** that brightens during magic, elongated neck, narrow shoulders. **Explicitly no pointed ears** — avoid elf shorthand. |
| **Orenth** | New: reality does not register them perfectly. Edges double, shadows lag, reflections resolve late, movement leaves a faint secondary silhouette. Older Orenth accumulate **phase scars** — skin with no texture, an eyebrow casting no shadow, a fingertip absent from mirrors. |
| **Mor** | Form is explicitly an approximation of personhood. Edges dissolve, feet need not touch the floor, features shift when attention moves. Different Mor choose different degrees of anthropomorphism. |
| **Constructed** | Baseline stays two-armed. Modular morphology is now named as *future design space* — secondary manipulator arms with real tradeoffs, retractable to keep armour silhouettes compatible. |
| **Vaskaal** | **Major rework.** Now 2.2–2.5 m, exceptionally lean with a shallow torso. Secondary rotational articulation in the forearm, an extra flexible segment in the ankle, precise rotational shoulders. **Three long primary fingers and two opposing thumbs.** No external ears, narrow jaw, altered nasal openings. Long stride, little vertical bob. |
| **Ondrek** | Should read as living geology that organised itself into a person-shape, not a human with a rock texture: exposed strata, fault lines, mineral veins, crystalline inclusions, healed fractures, asymmetrical growth. Faces with recessed sensors or crystalline eyes. |

---

## Existing assets that no longer match

Every race asset was built before this revision, so all of them predate the new biology.
The ones that are now **substantially wrong**, worst first:

| Asset | Why it no longer matches |
|---|---|
| `race_kal_representative`, all `raceclass_kal_*` | **No wings at all.** The wings are the Kal's most immediately surprising feature and are entirely absent. |
| `race_vaskaal_representative`, `raceclass_vaskaal_*` | Old proportions and human-like hands. Needs 2.2–2.5 m, lean, 3+2 digits, no external ears. |
| `race_siann_representative`, `raceclass_siann_*` | Skin, eyes and subdermal structures all absent. Currently reads as "very pale human". |
| `race_veth_representative`, `raceclass_veth_*` | Reads as an ordinary human. Needs the elongation and mobility cues. |
| `race_orenth_representative`, `raceclass_orenth_*` | No phase scars or registration errors. |
| `race_ondrek_representative`, `raceclass_ondrek_*` | Reads more like a stone golem than organised geology. |
| `race_mor_representative`, `raceclass_mor_*` | Closest to the new spec already; low priority. |
| `race_constructed_representative` | **Approved by the owner.** Unchanged by this revision — baseline is still two-armed and seamless. Leave alone. |

`racebody_*` concepts (the undergarment bases) have the same gaps and are not yet
rendered.

---

## What this means for production

1. **The Kal wings affect the rig and the armour system**, not just the concept art. A
   folded wing state and a deployed state both need geometry, and armour needs dorsal
   channels. This is the most expensive single change in the revision.
2. **Vaskaal proportions change their fit family** — 2.2–2.5 m tall puts them in *tall /
   narrow humanoid*, not the standard family, so armour does not transfer.
3. The other six races keep the standard humanoid fit family and only need new concept
   art and, later, new textures.
4. The **silhouette test** is now the stated acceptance criterion, and it should be run
   against the new concepts before any 3D production.

---

## The silhouette test, and how to run it

**Asking the image model to draw silhouettes does not test anything.** A silhouette prompt
carries no anatomy, and it produced seven near-identical human outlines with no wings, no
wide Kal and no irregular Ondrek.

Derive them from the actual concepts instead, which measures what the concepts really look
like once colour and detail are removed:

```
python W:\UNNAMED\tools\asset_pipeline\_silhouette_sheet.py ^
  race2_veth_representative race2_kal_representative race2_siann_representative ^
  race2_orenth_representative race2_mor_representative race2_constructed_representative ^
  race2_vaskaal_representative race2_ondrek_representative ^
  --out W:\UNNAMED\assets\review\silhouette_races.jpg
```

The backdrop is a vignette, so a fixed threshold fails: one image measured 139 at a corner
and 171 in the centre. `_silhouette_sheet.py` estimates the backdrop per image as the 85th
percentile of its own brightness histogram, which is stable across all of them.

### Result on the revised concepts

**Passes.** All eight separate at a glance:

| Race | Silhouette read |
|---|---|
| Veth | ordinary human line, only subtly long-limbed |
| Kal | unmistakable — huge deployed wings over a compact dense body |
| Siann | very tall and thin, long hair mass |
| Orenth | human in a long coat |
| Mor | slight, dissolving outline |
| Constructed | tall, smooth, featureless |
| Vaskaal | exceptionally tall and extremely narrow |
| Ondrek | massive irregular blocky mass |

Kal, Vaskaal and Ondrek read strongest, which is the right outcome — they are the three
whose biology diverges most from the human baseline.

## Known issue: the Kal concept reads demonic

`race2_kal_representative` and `race2_kal_wings_deployed` render with **hook-like spurs at
the wing joints and a dark bat-like membrane**, which reads as a demon. RACES.md is
explicit that the Kal "should not read as conventional ogres or dwarves", and a demon read
is that same problem wearing horns.

Both prompts have been corrected to specify **smooth, unarmoured spars** of pale bone and
grey metal with no hooks, spurs or claws, a **pale warm-grey leather membrane** rather than
a dark bat one, and the craftsman signals (apron, hammer, harness) kept visually dominant
over the wings. They need a re-render to confirm.

