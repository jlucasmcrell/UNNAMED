# Audio Regeneration Required

**Date:** 2026-09-24
**Policy:** marked, **not regenerated**

## Why nothing was regenerated

The maintenance brief preserves the audio pipeline and assets and asks only that the families
needing regeneration be identified. There are three further reasons not to rebuild them now:

1. The M6 build is frozen for the owner's playtest. Swapping 66 sounds underneath it would
   change what is being tested.
2. The existing set is playable. It is the *character* of the creature and impact sounds that
   is wrong, not their loudness, length, format or import.
3. The root cause is the model's handling of short articulatory sounds, so a regeneration
   needs an approach decision from the owner first. Re-running the same prompts with different
   seeds would spend GPU time to produce the same noise wash.

## Owner feedback this is based on

> "Some of these sounds are fine, but there's a lot I'm not sure really even make sense... None
> of the animal sounds sound like an animal, just ticks and light thunk sounds."

## Marked for regeneration

**66 of 177 sounds.**

| family | sounds | why |
|---|---|---|
| `creature` | 55 | The owner's listening pass: "None of the animal sounds sound like an animal, just ticks and light thunk sounds." The measurements agree - the creature family's median spectral centroid is 4142 Hz with |
| `ui` | 6 | Recorded at generation time: the confirm tick reads as a short low-frequency thump rather than a dry wooden tick. A short percussive transient is the same short-articulation failure the creature and h |
| `player` | 5 | Recorded at generation time: reads as a steady broadband noise wash rather than a pained breath, and the model did not produce a breath shape. Same root cause as the creature family. |

### Breakdown of what is marked

- **`creature` (55)** - every creature vocalisation: idle, alert, attack, hit, death across the
  Phase-1 archetypes. Measured median spectral centroid 4142 Hz with broadband energy and no
  formant structure, i.e. a noise wash rather than a vocalisation.
- **`player.hurt` (5)** - recorded at generation time as reading like a broadband noise wash
  rather than a pained breath; the model produced no breath shape.
- **`ui` (6)** - recorded as the confirm tick reading like a low-frequency thump rather than a
  dry wooden tick.

## Checked and deliberately not marked

| family | sounds | note |
|---|---|---|
| `weapon` | 48 | the obvious guess for the same defect, but no listening note and no recorded limitation supports it |
| `player` | 25 | no evidence of the same defect |
| `magic` | 17 | no evidence of the same defect |
| `crafting` | 10 | no evidence of the same defect |
| `interaction` | 7 | no evidence of the same defect |
| `ashen_hollow` | 1 | no evidence of the same defect |
| `charwood_verge` | 1 | no evidence of the same defect |
| `blackvein_cut` | 1 | no evidence of the same defect |
| `foldscar_ruin` | 1 | no evidence of the same defect |

Weapon impacts are worth calling out: they are the family one would most expect to share the
defect, and marking them on that expectation alone would be guessing. They stay unmarked until
the owner says otherwise.

## Effect on the shipped set

`shipping_status` for the marked entries becomes `prototype_ok_needs_regen`. Everything else
stays `prototype_ok`. Nothing is removed, nothing is deleted, and the sounds still load and
play exactly as they did.

*Tagged into the manifest: no (audit only).*
