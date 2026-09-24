# Phase-1 audio V2 regeneration — status

Complete regeneration of the Phase-1 audio set with **Stable Audio 3 Small SFX**, replacing the
Stable Audio Open 1.0 set. V1 is preserved in full and both are on disk; the game-facing ids are
identical between them, so nothing in gameplay changes.

**228 sounds delivered, 684 candidates rendered, 683 technically valid, 0 ids without a valid
candidate, Godot importing 228/228 with no problems. Nothing has been listened to.**

---

## 1. Model

| | |
|---|---|
| Model | **Stable Audio 3 Small SFX** (post-trained SFX checkpoint, not the base model) |
| Checkpoint | `stable_audio_3_small_sfx.safetensors` |
| Checkpoint SHA-256 | `ed9cf1b6172f1a8c2921a9560c21109ff3239524563ced9dce6dcdef41e2f515` |
| Size | 2,270,384,940 bytes; 567,573,761 parameters |
| Architecture | Stable Audio 3, DiT `dit1.0`, FLOW |
| Text encoder | `t5gemma_b_b_ul2.safetensors` (t5gemma-b-b-ul2) |
| Encoder SHA-256 | `1e1eba25be8872edb0d3c6335c6658fd6388e7b14b60da6e454e404cfcd8150e` |
| Source used | `Comfy-Org/stable-audio-3` (ComfyUI single-file layout) |
| Upstream | `stabilityai/stable-audio-3-small-sfx`, licence-gated; owner accepted the terms |
| Licence | Stability AI Community License |
| Redistributed components | Gemma components under the **Gemma Terms of Use** |
| ComfyUI | 0.34.0, native SA3 support (`comfy.supported_models.StableAudio3`, `comfy.text_encoders.sa3`) |
| Host | BEAST, RTX 3090 |

The Comfy-Org repackaging is used because ComfyUI loads a combined checkpoint and a single encoder, and
its filenames are the ones ComfyUI's own official blueprint references. Both files were verified
against Hugging Face's LFS object IDs, and the base (`_base`) checkpoint was deliberately not used.

**A note on how the hashes got here.** The first fetch recorded "expected" hashes whose tails were
invented from a truncated API response, and the download failed its own check. The values above are the
real ones, confirmed by comparing local SHA-256 against the repository's LFS oids. The check that
caught it is the reason hashes are recorded at all.

## 2. Generation

| | |
|---|---|
| Settings | **8 steps, cfg 1.0, `lcm` / `simple`, denoise 1.0** |
| Negative prompt | **none** |
| Candidate renders | **684** (228 ids × 3) |
| Failed renders | **0** |
| Generation wall clock | ~59 min total (~44 min first pass, 14.5 min second) |
| Mean render | ~5–7 s per candidate |
| Seeds | derived from the id and candidate label, so a candidate is reproducible from its name alone |

### The settings are not V1's, and that matters

| | V1 | V2 |
|---|---|---|
| Steps | 32 | **8** |
| CFG | 7.0 | **1.0** |
| Sampler | `euler` | **`lcm`** |
| Conditioning | `ConditioningStableAudio` | **not used** (SA3 embeds length internally) |

Taken from ComfyUI's official SA3 blueprint rather than carried over. Reusing V1's numbers is the
single most likely way to produce a whole library of wrong-sounding audio, so they were established
first.

### The negative prompt is inert, and that was tested rather than assumed

The brief asked for the universal negative to be dropped, and for negative prompting to be omitted
entirely if the model does not meaningfully support it. At cfg 1.0 the classifier-free-guidance
formula collapses to the positive prediction alone, which predicts the negative does nothing.

Two renders with opposite negatives differed in hash — but with *identical* peak and RMS, which is what
nondeterminism looks like. So a determinism control was rendered: identical inputs twice. Result:

```
determinism control  A vs A2 : peak 0.000000  rms 0.000000
negative effect      A vs B  : peak 0.000000  rms 0.000000
```

The sampler is bit-exact and the negative changes nothing. **V2 supplies no negative prompt.**

## 3. V1 preservation

| | |
|---|---|
| Location | `assets/audio/v1_stable_audio_open/` |
| Review images | `assets/review/audio/v1_stable_audio_open/` |
| Files | **362** (173 delivered WAVs, 173 FLAC masters, 4 manifests, 3 docs, 9 spectrograms) |
| Size | 85.8 MB |
| Verification | **362 ok, 0 missing, 0 mismatched** |

Hashes were taken **from the source bytes before copying**, and the archive was then re-read and
re-hashed. This came first because `assets/` is not tracked by Git: an in-place regeneration would have
destroyed the only copy and with it the V1-versus-V2 comparison. V1's provenance records
(`audio_models.json`, `playable_prototype_audio.json`) were **not modified** — V2 adds
`audio_models_v2.json` and `playable_prototype_audio_v2.json` alongside.

## 4. What changed in the prompts

Every V1 prompt ended with a negation tail — `no room, no reverb tail, no music, no speech, no voice,
no cinematic boom` — and for creature vocalisations that tail said **"no voice"** in a prompt whose
entire purpose was to produce one. With the separate negative conditioning proven inert, those inline
negations were the only suppression that could act.

**All 228 V2 prompts contain zero negations**, asserted by construction rather than by reading: the
builder splits on commas and drops any segment carrying a negation word. That check caught three cases
written into the V2 material itself — `no performed acting`, `not a completion jingle`, and
`with no room tone` inside my own acoustic-space table.

Prompts are built to the brief's physical grammar — source, action, material or anatomy, intensity,
duration, acoustic space, desired character — and creature identity follows section 7 per archetype:
the hound stays a canine with ember only as a secondary layer, the husk gets dry bone articulation
rather than a suppressed growl, the armour gets plate and hollow enclosure, the boar stays pig anatomy,
the spider gets chitin rather than a forced mammalian voice.

## 5. Technical QA

Objective checks only: silence, clipping, channel policy, sample rate, duration, loop seam validity.
Candidates are rejected for breakage, never for taste.

| | |
|---|---|
| Candidates rendered | 684 |
| Technically valid | **683** |
| Rejected | **1** — a loop candidate whose crossfade made the seam worse (1.03 → 1.85) |
| Ids with no valid candidate | **0** |
| Peak-limited (not loudness-matched) | 74 |
| Mono (positional) / stereo (beds, UI, Strain) | 220 / 8 |
| Loops with a measured seam | 8 |

### Provisional selection is not aesthetic

The brief forbids choosing a winner on spectral centroid. Survivors are ordered by **duration error**,
with candidate label as a deterministic tie-break. The centroid is recorded for the owner to read and is
**not** a ranking input. Every entry carries `provisional_selection: true` and
`human_auditioned: false`.

### Two V1 defects that do not reproduce

Neither was inherited as a workaround; both were detected and recorded per candidate.

- **Clipping.** SA3 wrote **0 clipped samples** across all 684 renders, against V1's PCM_16 saver which
  produced clipped masters.
- **Anti-phase cancellation.** SA3's output is dual-mono — channel correlation **1.000**, averaging
  loses **0.00 dB**. The defect that cost V1 up to 13 dB across 44 sounds cannot occur here.

### Several rejections were my own bugs, not the audio

Recorded because the distinction matters for trusting these numbers:

1. The loop check looked for a key called `ratio`; the real keys are `seam_step_ratio_before/_after`.
   It rejected **every loop** while the crossfades were working (20.2 → 0.0005).
2. A fixed 0.35 s duration tolerance rejected single-transient sounds whose length is *deliberately*
   the first energy lobe. **V1 shipped the same behaviour** — its own QA records `anvil.strike.01` at
   0.44 s against a 0.8 s spec marked `clamped_to_minimum`. The check was stricter than the validator
   for the set it was replacing.
3. A blanket 50% floor rejected every UI sound; V1's own UI trimmer uses an **18%** floor. The rule now
   reads the trimmer's own group-specific threshold.
4. `GEN_MAX = 12.0` clipped all four ambience beds below their 20 s spec — a real defect, fixed by
   giving loops `loop_length + headroom` instead of a padding multiple.

## 6. Objective V1-versus-V2 comparison

Measured on the provisional set against V1's own QA. **These are measurements, not judgements.** No
claim is made that any sound is convincing.

### Where the measurable defects were

| Target | V1 | V2 | Direction |
|---|---|---|---|
| **Magic Strain** (sub-bass, inaudible on laptops) | `moderate` centroid **61 Hz** | **775 / 1455 / 4075 Hz** | sub-bass dominance removed; all three now in the audible band |
| **UI** (low thumps, brief section 9) | median centroid **1143 Hz** | **2417 Hz** | moved from low-frequency thump to mid-frequency tick |
| **Creature** (broadband noise, no formant structure) | median centroid **4142 Hz** | **3250 Hz** | see caveat below |
| **Weapon** | 7163 Hz | 4746 Hz | more body |
| **Magic** overall | 4065 Hz | 1690 Hz | more body, less top |

### Creature, player and UI specifically

| Question the brief asks | What measurement shows |
|---|---|
| Stronger harmonic/formant structure where vocalisation was requested | **Not established.** A spectrogram shows V2 creature sounds with clearer event shape and energy redistributed into the vocal band, but no formant-tracking was run and no claim is made. |
| Less broadband-noise-only behaviour | **Partly.** V2 attack sounds are discrete transients with decay where V1's were sustained washes filling their whole duration, visible in `assets/review/audio_v2/compare_smoke.png`. |
| Better onset/event shape | **Yes.** V1's boar attack is continuous broadband energy across 1.2 s; V2's is a short transient that decays. |
| Reduced sub-bass dominance where inappropriate | **Yes, strongly.** Strain went from a 61 Hz centroid to 775–4075 Hz; UI from 1143 to 2417 Hz. |

**The honest limit:** whether a boar now *sounds like a boar* is not measurable here and has not been
assessed. The pipeline has no ears.

## 7. Godot

```
AUDIO_RESULT {
  "ok": true, "sounds": 228, "imported": 228,
  "mono": 220, "stereo": 8, "looped": 8,
  "rate_mismatch": 0, "channel_mismatch": 0, "length_mismatch": 0,
  "loop_failures": 0, "clipped": 0, "silent": 0,
  "problems": []
}
```

Two faults on the way there, both worth recording because both produced a *clean-looking* result:

1. **The first Godot run passed 173 sounds and validated nothing.** `validate_audio.gd` reads
   `res://assets/audio_manifest.json`; my manifest was written elsewhere, so Godot read V1's stale
   manifest and reported a clean pass over V1's files while the V2 set sat unread beside them. A
   passing validation of the wrong assets is worse than a failure.
2. **A stale import cache** made `sfx.player.crouch.exit.01` import to a 4,100-byte sample instead of
   57,983. The source WAV was structurally identical to its neighbour's, chunk for chunk. Clearing the
   `.import` and re-importing fixed it.

## 8. Audition — for the owner

**Open `assets/review/audio_v2/index.html`.** One page, grouped by family, playing the V1 original and
all three V2 candidates inline for every id, with the provisional pick marked.

| | |
|---|---|
| Index | `assets/review/audio_v2/index.html` |
| Metadata | `assets/review/audio_v2/audition_index.json` |
| Files packaged | 857 (V1 + 3 candidates per id) |
| **human_auditioned** | **0 of 228** |

Provisional picks may be replaced after listening. Nothing is approved until you listen.

## 9. Section 30A additions

51 new Phase-1 ids were added alongside the 177 replacements: creature locomotion for all five
archetypes, player and equipment foley, crouch garment movement, a loot take-all cue, and 11 positional
environmental sweeteners across the four cells.

- **Section 30A-F (impact severity) is deferred, not implemented.** It applies only if gameplay already
  exposes a light/heavy distinction through the event contract, and it explicitly forbids inventing
  gameplay semantics to justify audio. Nothing here inspected Claude's branch.
- No dialogue and no music were generated.

## 10. Not done

- **No listening.** 0 of 228.
- **No per-candidate audition UI beyond the index** — the owner picks; the pipeline does not.
- **Impact severity deferred** pending a gameplay answer.
- Creature movement prompts were authored by analogy to V1's distinctions rather than from a reference
  recording of an armoured skeleton walking.
- The 74 peak-limited sounds carry the same limitation as V1's 42: a short transient has no valid
  400 ms loudness gating block, so the peak ceiling wins over the loudness target.

## 11. Where things live

| | |
|---|---|
| V1 (preserved) | `assets/audio/v1_stable_audio_open/` |
| V2 candidates (684) | `assets/audio/v2_candidates/<audio_id>/candidate_{a,b,c}.flac` |
| V2 provisional set (228) | `assets/audio/v2_delivered/<audio_id>/candidate_<x>.wav` |
| V2 manifest | `assets/manifests/playable_prototype_audio_v2.json` |
| V2 QA | `assets/manifests/audio_qa_v2.json` |
| V2 spec and prompts | `assets/manifests/audio_spec_v2.json` |
| V2 model provenance | `assets/manifests/audio_models_v2.json` |
| Audition package | `assets/review/audio_v2/` |
