# Phase-1 audio — where the current set is and how to load it

Written because the handoff that existed still described V1 and called the work in progress "audio V2",
and nothing on disk named the V3 set as the one to use. An agent picking this up would have looked for
the wrong directory.

---

## 1. The set to use

**`assets/audio/v3_delivered/`** — 230 files, flat, one per id: `<audio_id>.wav`.

Read **`assets/manifests/playable_prototype_audio_v3.json`** for everything about them. It is the
manifest; the files carry no metadata of their own.

```jsonc
{
  "version": 3,
  "active_selection": "v3",
  "counts": { "total": 230 },
  "locomotion": { /* the footfall convention, see below */ },
  "validation": { /* lengths, channels, Godot result */ },
  "sounds": [
    {
      "audio_id": "sfx.creature.ash_ember_hound.walk.01",
      "group": "creature.move",
      "gameplay_role": "...",
      "prompt": "TrackType: SFX, one paw of a large canine landing on dry packed earth...",
      "seconds": 0.55,            // the intended length
      "delivered_seconds": 0.55,  // what the file actually is
      "duration_error_s": 0.0,
      "channels": 1,              // 1 = mono positional, 2 = stereo non-positional
      "loop": false,
      "sample_rate": 48000,
      "peak_dbfs": -9.4,
      "delivered": "audio/v3_delivered/sfx.creature.ash_ember_hound.walk.01.wav",
      "locomotion_convention": "one_footfall"
    }
  ]
}
```

Path resolution: `delivered` is relative to `assets/`. The file is likewise findable by name —
`assets/audio/v3_delivered/<audio_id>.wav` — so a lookup by id never has to consult the manifest, only
to learn its properties.

## 2. The other two sets, and why they are still here

| Set | Path | What it is |
|---|---|---|
| **V1** | `assets/audio/v1_stable_audio_open/` | The original Stable Audio Open 1.0 set. 362 files, archived and hash-verified. |
| V2 | `assets/audio/v2_delivered/`, `assets/audio/v2_candidates/` | Three candidates per id. **Superseded** — its UI sounds were delivered at 18% of spec and the candidates were indistinguishable. |
| **V3** | `assets/audio/v3_delivered/` | **Use this.** 230 files, one per id. |

Both earlier sets are deliberately intact. V1 is the only record of the original design intent, and V2
is the evidence for why V3 exists. Neither is referenced by the game.

## 3. How to wire it up

**`docs/PHASE1_AUDIO_EVENT_CONTRACT.md`** is the mapping from game event to audio id, and it covers all
230. Read it before writing loader code. It states, per event, which ids to play, which are mono and
positional versus stereo and non-positional, and where the engine has to supply information audio
cannot infer for itself.

Three things in it are easy to get wrong:

- **Footsteps and creature locomotion are one footfall per sound, cycled.** Not a walk cycle. Fire per
  step and rotate variants; never repeat one back to back.
- **The spider carries two steps per sound**, because eight legs move in overlapping groups. Its four
  variants are meant to be cycled across the engine's repeat.
- **`channels: 1` means positional.** 222 of the 230 are mono and belong in the world; the 8 stereo
  ones are the four looping cell beds and the three Strain layers, which are non-positional.

## 4. What is verified, and what is not

Verified on all 230, by measurement:

| | |
|---|---|
| Length | every file is its spec length, max error **0.0000 s** |
| Format | 48 kHz, PCM 24-bit, mono or stereo exactly as the manifest says |
| Clipping | 0 clipped samples |
| Silence | 0 silent files |
| Godot | `ok=true`, **230/230 imported**, 0 rate / channel / length / loop mismatches |
| Ids | all 177 V1 ids present, no duplicates, plus 53 section 30A additions |

**Not established: whether any of it sounds good.** `human_auditioned` is `0` in the manifest, and that
is accurate rather than an oversight. The owner listened to 15 sounds from the V2 set, concluded the
candidates within a class were too similar to be worth discriminating between, and accepted V3 unheard
as a placeholder for the playtest. Nobody has heard V3, including the 53 section 30A additions, which
have had no listening at all.

Treat every sound here as **plausible, loadable and correctly shaped** — not as approved. If something
is wrong with one in motion, that is expected and not a sign the pipeline is broken.

## 5. If a sound needs regenerating

`tools/audio_pipeline/` holds the pipeline. `_generate_sa3_v3.py` is the renderer; it takes one
candidate per id, renders close to the target length, and re-renders anything whose delivered length
misses. It skips ids already marked verified, so re-running it is cheap.

Before changing prompts, read **`docs/PHASE1_AUDIO_PROMPTING_REFERENCE.md`** — it records what the
model's own documentation says and why the prompts are shaped as they are. The short version:
`TrackType: SFX` as a lead tag, prose rather than keyword lists, and a `Length: N seconds` token.

**Do not re-run seeds to get a different sound.** It was measured: a new seed reproduces the same
character at a new realisation, moving the spectral centroid 242 Hz against 531 Hz for a rewritten
prompt. Three candidates per id was the mistake V2 made.

## 6. Known limitations, stated plainly

- **The model is the ceiling.** The owner's assessment is that Stable Audio 3 Small SFX produces a
  narrow range of outcomes per prompt, and that a different model is the answer for a real audio pass.
  V3 is a playtest placeholder built to that expectation.
- **Impacts are not split by severity.** Light and heavy are distinguished for swings, not for impacts.
  If combat needs "blade on plate, hard" versus "glancing", that is a small real gap.
- **The creature vocalisations may not convince.** Nothing in the model's documentation claims it
  produces believable animal sounds, and its own sound-effect examples are all material interactions.
- **Nothing has been heard with animation attached.** The owner's own point, and the reason V3 was
  accepted unheard: an isolated sound can be judged for whether it is pleasant, not for whether it
  works.
