# OTHERREACH — lore: what canon leaves open

Spec 41 pass 2 §5. The lore is written from canon only. Where canon names a thing and then says nothing about it, the lore does not fill the
gap: the gap is listed here for the owner to rule on. Each line names the concept, the question, and where canon stops.

**How to read this:** a gap is not a defect in the writing; it is the boundary of what the writing is allowed to say. Answer one and it becomes
content: the answer goes into the doc it belongs to first, and the lore page follows the doc, never the other way round.

## Open

| Concept | What canon leaves open | Where canon stops |
|---|---|---|
| The fold | What the Foldscar actually is: canon calls it "reality distortion", a barrier and a ring of stones, and does not say what the heart is or why steadying it releases a man. | `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §8, §16; `docs/OTHERREACH_COSMOLOGY.md` |
| The gorge's water | Where the basin's water goes past the Outfall. Canon places the cut and stops at the lip. | `content/locations/cairnwold/the_gorge.yaml` notes |
| The Vaskaal | Whether they are present in the slice at all. Canon names them as a people and a sensor; no place or person in the slice is one. | `docs/RACES.md` |
| Ondrek | Their part in the frontier. Canon names them and gives them no place in the slice. | `docs/RACES.md`, `docs/CHARACTER_CREATION_AND_LINEAGE.md` |
| Dates | Almost all of it: how long ago the stones were raised, when the quarry was worked, when the Order sealed it. Canon gives "two winters back" for one death and nothing else. | every source above |

## Waiting on another specification

| Tie | What it needs | Where it stands |
|---|---|---|
| Lore unlocking **runes** (spec 47) | The enhancement system itself: enhancement slots, an Enchanting skill, an enchanting bench, and rune definitions for a page to name. Spec 47 is written (`C:\Otherreach_External_Assets\specs\47_gear_enhancement.md`, owner rulings 2026-10-21) but **not implemented in this build**: there is no enhancement, enchantment or rune content kind and no code for it. | The lore side is ready and waiting: the cosmology already carries the materials spec 47 infuses (ember hound ash, wolf fang, boar bristle, Blackvein ore) as entries with their own pages, so a rune, once it exists as content, has something to be taught from. A page cannot name a rune that does not exist, and inventing the rune system inside the lore pass would invent a mechanic and world facts at once. |

## Answered

*(an answer moves here with the doc it was written into)*

| Concept | Question | How it was settled | Written into |
|---|---|---|---|
| Old Late's makers | Who cut the giant and the three Quiet Stones, and when. | **Not settled — narrowed.** Canon says "turned out of line when we found them" and gives no hand and no date. Two entries now carry both halves of what canon *does* say: `lore.old_late` (what stands there) and `lore.three_quiet_stones_after` (what turning one back does). The maker is still unnamed, deliberately. | `content/lore/old_late.yaml`, `content/lore/three_quiet_stones_after.yaml` |
| Halda Crowden | What happened to her on the north slope. | **Answered as far as canon goes:** the tally, the bow, the sister's counting and no body are now one entry, and the entry says plainly that no body was found. | `content/lore/halda_crowden.yaml` |
| The Quiet-folk | Whether they were a people at all. | **Answered as a disagreement**, which is what canon supports: the keeper's telling (they were a people) and the Survey's record (they were a name on a stone) are two pages that do not agree. | `content/lore/quiet_folk.yaml` |
| The Order of the Lantern | What the order was and why the seam was sealed. | **Narrowed:** the ledger fragment and the sealed face are written as what a reader can find; the order's purpose is left as canon leaves it. | `content/lore/blackvein.yaml`, `content/lore/long_survey.yaml` |
| The blocked shaft | What is behind the Blackvein walling. | **Deliberately not answered**, and the entry says so in the world's voice rather than inventing a room. | `content/lore/blackvein.yaml` |
| Ember | Why a cinder sac quickens a salve. | **Answered in the world's own terms:** Sel Arien does not know and will not write it down, which is canon's answer. | `content/lore/ember_hounds.yaml`, `content/lore/cinder_sac.yaml` |

| The Spill's names (FR-1) | What the ruin between Old Late and the Rim is called. | **Owner-approved direction, 2026-10-24.** Cairnwold calls the field **the Spill**, the Survey calls the court and gate **the Stopped Gate**, the spire is **the Needle**, and its three fixtures are **the anchors**. | `docs/FIELD_RUINS.md`, `content/lore/the_spill.yaml`, `content/lore/stopped_gate.yaml` |
| The Rim Stone "only fixed thing" (FR-2) | Whether the top may hold a ruin when the Rim Stone was "the only fixed thing on the top". | **Owner-approved direction, 2026-10-24.** The stone is the one thing on the top set there *for travellers*. Chatter 0154 now names the Spill. | `content/lore/rim_stone.yaml`, `content/chatter/b0007.yaml` |
| Firstfold iron in hand (FR-3) | Whether a shard of a Singular material can be carried. | **Owner-approved direction, 2026-10-24.** Yes, one shard, broken free where its plate broke. It is a statless keepsake that answers its own gate. Each gate's iron is one thing. | `docs/WORLD_MATERIALS.md` §6, `content/lore/firstfold_iron.yaml` |
| Grazers at a gate (FR-4) | Whether a gate draws the fold-bleed grazers. | **Owner-approved direction, 2026-10-24.** No. A gate is not a fold and bleeds nothing. | `docs/OTHERREACH_COSMOLOGY.md` §8 *Stopped gates* |
| Constructs at a gate (FR-5) | Who keeps an Othergate. | **Owner-approved direction, 2026-10-24.** Gate tenders and the Gate Warden are the gate's own keepers, neither a people nor the Constructed. The animated armour at the breaches is drawn by the hum. | `docs/OTHERREACH_COSMOLOGY.md` §8, `docs/FIELD_RUINS.md`, `content/lore/gate_tender.yaml`, `content/lore/gate_warden.yaml` |
