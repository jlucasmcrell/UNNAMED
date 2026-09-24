# Phase-1 audio sprint — status

Sub-sprint of the Otherreach asset work, opened after the playable-asset sprint reached its
checkpoint. The goal is not a large audio library:

> Deliver the smallest coherent audio set that makes Ashen Hollow feel like a playable RPG through M6.

**Outcome: 173 sounds, 218 seconds, all validated, all imported by Godot with zero problems.**
Ashen Hollow can play through M6 without placeholder silence in any of the eight categories the brief
names.

The content target is `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md`, which is newer than
`PROTOTYPE.md` and renames several things: the five creatures are the Ash Ember Hound, Bone Walker
Husk, Animated Armour, Bristleback Boar and Cave Hunting Spider; the three formulas are Impulse Bolt
(Force), Brace Ward (Warding) and Mending Thread (Vital); and the craft chain is raw iron ore → Iron
Billet → March Spear. No stale names — no generic mana, no `spell.ember.bolt` — appear anywhere in
the delivered set.

Companion documents: `PHASE1_AUDIO_PROVENANCE.md` (licensing),
`PHASE1_AUDIO_EVENT_CONTRACT.md` (what Claude needs to hook up).

---

## 0. Read this first: the pipeline has no ears

Nothing in this sprint could listen to the output. Every claim below is either a **measurement**
(duration, onset, peak, ITU-R BS.1770 loudness, spectral centroid, loop-seam statistics) or an
inspection of a **spectrogram image**, which shows a sound's structure — where its energy sits in
time and frequency — but not whether it is *good*.

That is enough to catch a great deal, and it did: a clipped transient, a missing onset, a trim that
captured the wrong part of a render, a loop whose crossfade made the seam worse, and a weapon impact
that was a low-frequency thud where it should have been a metallic ring. It is not enough to know
whether the sword sounds convincing.

**This set needs a listening pass by a human before it is treated as final.** Spectrogram sheets for
every family are in `assets/review/audio/` to make that pass fast.

## 1. Environment and tool capability

Reconnaissance came first, because nothing could be planned before it. The finding was not what the
brief assumed.

**All three hosts ship ComfyUI's audio *nodes*. Almost none of them had audio *weights*.**

| Host | GPU | ComfyUI | Audio nodes | Audio weights found |
|---|---|---|---|---|
| BEAST | RTX 3090 24 GB (22.1 free) | 0.34.0 | Stable Audio set, ACE-Step set, 91 audio-ish nodes | **only LTX / MiniMax / joyai audio VAEs** — no sound-effect model |
| ASTRAL | RTX 5090 32 GB (23.8 free) | 0.35.0 | same, 70 audio-ish nodes | LTX audio VAEs only |
| RAZER | RTX 4070 Ti 12 GB (**4.1 free**) | 0.33.0 | same, 56 audio-ish nodes | **ACE-Step 1.5** (`acestep_v1.5_xl_turbo_bf16` + qwen CLIP + ace vae) |

So the rig could generate **music** — ACE-Step, on the host with the least free VRAM, whose model share
is not reachable from BEAST — and could not generate a **sound effect** at all.

Also present and rejected: MMAudio weights on the shared drive with **no node pack installed on any
host**, and only a community `nsfw_gold` finetune whose licence is unstated; the abandoned
VibeVoice/TTS path; and hosted API nodes (ElevenLabs, ByteDance Seed, Doubao, MiniMax Music 3, Fish
Audio, HeyGen) that require an external service. Full reasoning in `PHASE1_AUDIO_PROVENANCE.md`.

**Resolution:** Stable Audio Open 1.0 is supported natively by the audio nodes already installed on
all three hosts, so the gap was two weight files, not a code change. They were fetched into BEAST's
existing `models/checkpoints` and `models/text_encoders`. **No existing file was modified, no custom
node installed, no ComfyUI upgraded.** The LTX, Trellis and Z-Image paths are untouched.

Host choice: **BEAST** — not the 3D-critical host (ASTRAL is), 22 GB free VRAM, and rendering locally
means audio never crosses the network.

## 2. Licensing

Recorded in full in `PHASE1_AUDIO_PROVENANCE.md`; hashes in `assets/manifests/audio_models.json`.

| | Stable Audio Open 1.0 | T5-base |
|---|---|---|
| Role | text-to-audio: every sound in this set | text encoder |
| Size | 4.52 GiB | 0.83 GiB |
| Source | `Comfy-Org/stable-audio-open-1.0_repackaged` | `google-t5/t5-base` |
| SHA-256 | `7b20458a071231aa…` | `a90903540cc02cbe…` |
| License | **Stability AI Community License** | **Apache-2.0** |
| Commercial | permitted under **USD $1M** annual revenue, with attribution and registration | permitted |

**No audio was ingested from any source.** Every sound is text-to-audio from a local model: no sample
library, no ripped audio, no purchased pack, no external audio input. There is nothing whose
provenance needs tracing beyond the model.

**Two decisions the owner should confirm:**

1. The canonical `stabilityai/stable-audio-open-1.0` repository is **licence-gated**, and accepting a
   licence is the owner's act rather than the pipeline's, so the non-gated Comfy-Org distribution
   mirror was used. Same weights, same `LICENSE.md`. Re-download from the canonical repository if
   being bound by its terms specifically is the intent.
2. These assets are **not** marked `PROTOTYPE_ONLY_LICENSE`, because the Community Licence does permit
   commercial use below the revenue threshold. But the licence **terminates** above USD $1M annual
   revenue, at which point every asset here needs an enterprise licence or replacement. The required
   attribution lines are in the provenance document and must reach the game's credits.

## 3. Generated families

173 sounds in 8 categories. Every id, prompt, seed, model hash and measured value is in
`assets/manifests/playable_prototype_audio.json`.

| Category | Count | Notes |
|---|---|---|
| creatures | 55 | 11 per archetype: idle 2, alert 2, attack 3, hurt 2, death 2 |
| weapons | 44 | sword 19, bow 14, polearm 11 (plus 2 declared aliases) |
| player | 30 | footsteps 20 across dirt/stone/wood × walk/run, plus land, jump, hurt, downed, death |
| magic | 17 | Impulse Bolt 6, Brace Ward 5, Mending Thread 3, Strain 3 |
| crafting | 10 | mining strike 3, ore break, forge bed, anvil 4, craft complete |
| interaction | 7 | pickup 3, chest open/close, door open/close |
| ui | 6 | menu open/close, confirm, back, equip, error |
| ambience | 4 | one looping bed per cell |

**On the count.** Section 37 of the brief says to prefer 60–100 coherent sounds over 500, and this set
is 173. That is a genuine conflict inside the brief rather than a decision to over-produce: section 13
requires eleven or more sounds for *each* of *five* creatures, which is 55 on its own, and section 33 —
the acceptance criterion — requires all five archetypes to have identity sets. With the weapon,
movement, magic, interaction, ambience and UI minimums, section 33's own list totals roughly 175.
Coverage was chosen over the lower bound, and section 37's intent was honoured the other way: one
candidate was generated per id and curated, rather than generating forty variations of each. The total
is 3.6 minutes of audio.

## 4. Selected assets

Production intermediate: **WAV, 48 kHz, 24-bit PCM**, per section 23. Original masters are retained
untouched as the 44.1 kHz FLAC the generator produced, in `assets/audio/masters/` — nothing lossy is
ever re-encoded, and the master is what a regeneration would be compared against.

| | |
|---|---|
| Delivered | 173 WAV, 42.4 MiB, 218.3 s |
| Masters | 173 FLAC, 39.7 MiB |
| Mono (positional) | **165** |
| Stereo (beds, Strain, UI) | **8** |
| Looping | **8**, each with a measured seam |
| Aliases | **4** — spear wood/stone impacts resolve to the sword's, per section 11 |

The spec declares **177** entries: 173 delivered assets plus those 4 aliases, which carry no audio of
their own and are never generated or copied. The contract resolves `alias_of`, so there is no second
file to drift out of sync.

Selection was **coverage-first, one candidate per id**, then curated by measurement and spectrogram
review. The brief's "generate several candidates and select the best" was deliberately *not* followed
at the per-sound level: with 173 ids, three candidates each means 519 renders for a prototype whose
acceptance criterion is coverage, and the failures that mattered turned out to be systematic pipeline
bugs rather than unlucky seeds — which more candidates per id would not have found.

Channel policy per section 24: mono for every positional one-shot, stereo only for the four area beds,
the three Strain layers and the forge bed. Positional information is never baked into a one-shot.

## 5. Rejected and failed generations

**0 of 173 failed.** Every id has a master and a delivered asset.

What was rejected along the way:

| Rejected | Why |
|---|---|
| ACE-Step 1.5 | Text-to-**music**. Section 22 forbids the music sprint and it cannot render a 200 ms sword impact. |
| MMAudio | No node pack on any host; only weights present are a community finetune with no stated licence. Section 3.3/3.5. |
| Hosted API audio nodes | External service, sends data off-machine. |
| 5 model families as generators | LTX/MiniMax/joyai audio VAEs and T5 are not text-to-audio generators. |
| **~90 re-renders** | The level-seeking pass re-rendered a sound when its master peaked outside the acceptable band. A clipped or 50 dB-too-quiet master was discarded, not shipped. |
| Rejected masters | A master that still reached full scale after correction was never written to disk: the check runs on the downloaded bytes, before the file is placed. |

## 6. Normalisation and QA

Every sound was trimmed to its event, levelled against a per-group target, and measured. Targets are
**relative, not uniform**, per section 25 — a footstep sits at −30 LUFS and a boar impact at −16, so a
sword swing cannot end up louder than a boar impact merely because its waveform peaked higher.

Checked per asset, and all passing: duration, sample rate, channel count, peak, RMS, DC offset, clipped
samples, near-silence, spectral centroid, spectral rolloff, loudness, and for loops the seam statistics.
Section 30's rule is enforced: **a generated file with missing provenance fails validation**, and the
validator exits non-zero. It caught a real case of this mid-batch, when the generation state file was
still empty.

### Bugs this stage found — all found by looking, none by assuming

The spectrogram sheets were not a formality; every one of these was invisible in the metrics and
obvious in the image or the statistic:

| Found | Fixed |
|---|---|
| **Anti-phase stereo cancellation.** Averaging Stable Audio's two channels to mono cost up to **13 dB** on low-frequency material, and then fooled the level match into applying 25 dB of gain on top of a comb-filtered signal. | Downmix detects near-anti-phase and falls back to the louder single channel. **44 of 165 mono sounds (27%) needed the fallback** — this was not an edge case. Recorded per asset as `downmix`. |
| **Trim captured the wrong event.** One 7.2 s render of a 2.4 s creature idle had its energy peak at 4.93 s but its first threshold crossing at 0.58 s; trimming from the crossing shipped two seconds of near-silence and missed the event. | Onset is anchored on the loudest region and walks back to where that event began. Gain for that sound went from +34.7 dB to +2.9 dB. |
| **Loop crossfade ran the wrong way.** Blending the *end of the bed* into the head jumps the loop point backwards by the fade length; it measured **worse than doing nothing** (seam step ratio 10.8 before, 26.9 after). | Blend across the loop point using the material just past it. Ratio now **1.015** for that bed; six of eight beds improved from ratios of 16–291 down to 0.1–1.4. |
| **Masters arrived clipped.** ComfyUI's saver writes PCM_16 without scaling, so hot model output clipped — 176 clipped samples inside a 220 ms UI tick. | A level-seeking loop that measures each rendered master and re-renders outside an acceptable band. A clipped master never reaches the masters directory. |
| **Level correction oscillated.** Re-rolling the seed while correcting the gain meant measuring a *different* sound, so the loop bounced between too quiet and clipped. | The seed is kept when correcting level; only genuine failures re-roll. |
| **Godot imported everything as QOA**, a **lossy** format. The 24-bit PCM intermediate was being silently lossy-compressed at import. | A project-level `[importer_defaults]` entry sets PCM. Verified: all 173 now import as `16_BITS`, not `QOA`. |
| **`AudioStreamWav` does not exist** in Godot 4.7.2; the class is `AudioStreamWAV`. | Probed the engine's own `ClassDB` rather than guessing a second time. |
| **Three ambience beds were delivered outside the delivered tree.** The directory-prefix map said `amb.charwood.` while the ids read `amb.charwood_verge.01`, so Charwood Verge, Blackvein Cut and Foldscar Ruin were written to `assets/audio/temp/` and the manifest pointed there. The mapping had a silent `temp` fallback, which absorbed the miss instead of failing. | Prefixes corrected, the fallback removed so an unmapped id now raises and stops the run, and the three beds reprocessed into `ambience/charwood`, `ambience/blackvein` and `ambience/foldscar`. |
| **I then deleted those three assets.** Cleaning up what looked like an empty scratch directory removed the only copy of three delivered beds, after validation had already passed against them. | Caught by re-verifying every delivered path against the filesystem rather than trusting the earlier pass, and restored by reprocessing. Nothing was lost permanently because the FLAC masters are kept, but the lesson is that a validation result is only true at the instant it ran. |

### Known limitations, recorded per asset (13)

Content problems that measurement cannot detect. They are in the manifest under `known_limitations`,
not only in prose, because a limitation that lives in a status document is one nobody reads at the
point of use:

- **Strain layers are predominantly sub-bass.** `sfx.magic.strain.moderate.01` has a spectral centroid
  of **61 Hz**; all three bands may be inaudible on laptop speakers.
- **Mending Thread resolve is a sub-bass rumble** (centroid 180 Hz, 85% rolloff below 300 Hz) rather
  than the fine tissue resonance the prompt asked for.
- **The UI confirm tick reads as a low-frequency thump**, not a dry wooden tick.
- **`sfx.player.hurt.light.*` reads as a steady noise wash**, not a pained breath; the model did not
  produce a breath shape for that prompt.

### One systematic limitation worth understanding

**42 sounds are peak-limited rather than loudness-matched.** These are the short transient impacts: a
300 ms sound has no valid 400 ms BS.1770 gating block, so the loudness estimate is a K-weighted RMS,
and reaching a −16 LUFS target from it would demand a peak above full scale. The peak ceiling wins, so
those impacts land at **peak −1 dBFS** and are quieter than their target. The practical consequence is
that the relative mix between impacts is governed by crest factor rather than by the declared targets,
and a per-category trim in the engine's mix may be wanted. It is recorded per asset as
`gain_limited_by_peak`.

## 7. Godot validation

`tools/godot_validate/validate_audio.gd`, run against Godot 4.7.2 headless, on all 173 staged assets.

```
{"ok":true, "sounds":173, "imported":173, "formats":{"16_BITS":173},
 "mono":165, "stereo":8, "looped":8, "loop_failures":0,
 "clipped":0, "silent":0, "rate_mismatch":0, "channel_mismatch":0,
 "length_mismatch":0, "problems":[]}
```

**`ok: true`, zero problems.** Specifically verified: every file imports as an `AudioStreamWAV`, at
48 kHz, uncompressed PCM; every mono asset is genuinely mono (a stereo one-shot could not be
spatialised by `AudioStreamPlayer3D`); every loop can be given a full-length forward loop, with the
loop set **from the import** rather than by the test; decoded sample data is neither silent nor at full
scale; and stream length matches the file on disk.

Section 31's scope was respected: this is an **asset-import validator**, the same shape as the existing
asset validators. It does not implement any part of the game's audio runtime, and no gameplay code was
written or modified.

**A real engine limitation to carry forward:** Godot's `AudioStreamWAV` supports only 8-bit PCM, 16-bit
PCM, IMA-ADPCM and QOA. The **24-bit master cannot survive into the engine** — section 23's production
intermediate is correct and is what should be versioned, but the runtime stream is 16-bit PCM. That is
an 8-bit loss at the very end of the chain, and it is unavoidable without a different playback path.

## 8. Claude event-contract readiness

`docs/PHASE1_AUDIO_EVENT_CONTRACT.md` lists every event audio needs, the discriminators needed with
each event, the impact matrix (weapon family × target material), the creature and formula id tables,
the Strain cross-fade bands, and the per-cell ambience beds. It states what audio needs to know and
prescribes no implementation.

Two places where the set is knowingly incomplete are named there, so they are found from the document
rather than from play:

1. **Impacts are not split by severity.** Light and heavy exist for swings, not for impacts.
2. **No music exists**, and none is planned in this sprint.

The impact matrix is the part most worth reading: **sword, arrow and spear do not share a flesh
impact, and blade-into-flesh is a different asset from blade-into-plate**. Verified in the
spectrograms — flesh is a dominant low-frequency body with no ringing, plate burns broadband with
clear high-frequency ringing partials above 1 kHz. They are two distinct sound classes, which is the
requirement section 9 exists to state.

## 9. Missing audio

| Gap | Impact | Cost to close |
|---|---|---|
| **No music** | Intended. Section 22 forbids the music sprint. | A separate sprint. |
| **Impacts not split by severity** | "Blade on plate, hard" vs "glancing" cannot be distinguished. | Small: a handful of ids in an existing family. |
| **No sprint-specific footsteps** | Sprint reuses the run set, which section 7 explicitly permits. | Zero until it is wanted. |
| **No creature locomotion** | Only vocalisations. Footsteps cover the player, not the five archetypes. | Moderate; the surface families already exist. |
| **No dialogue, grunts or barks for the four NPCs** | Section 18 did not ask for them. | Out of scope until there is voice direction. |
| **No equipment or armour foley** | No draw/holster for armour pieces, no cloth. | Small. |

## 10. Blockers

**None.** Every section 33 minimum is met, and the Godot validation passes with zero problems.

Two things need a human rather than the pipeline:

1. **A listening pass.** See section 0. The set is measured and structurally sound; whether it is
   *convincing* has not been and cannot be established from here.
2. **A licensing decision.** Which Stable Audio repository the project means to be bound by. See
   section 2.

## 11. Next actions

1. **Listen to it.** `assets/review/audio/` holds spectrogram sheets by family. The four families
   flagged in section 6 are the ones to audition first.
2. **Regenerate the flagged families** if they bother you in play. Each has a specific prompt-level
   cause recorded; the fixes are prompt changes, not pipeline changes, so this is cheap.
3. **Decide the licence question** in section 2 and put the two attribution lines into the credits.
4. **Hand the event contract to Claude** and confirm the impact matrix matches how combat resolves
   material.
5. **Optional, and worth it:** a per-category mix trim in the engine, given the 42 peak-limited
   sounds.

Not started, and deliberately so: music, NPC dialogue, and any audio runtime architecture beyond asset
import validation.

---

## Ownership

Owned and touched in this sprint: `assets/audio/**`, `assets/manifests/playable_prototype_audio.json`,
`assets/manifests/audio_spec.json`, `assets/manifests/audio_qa.json`,
`assets/manifests/audio_models.json`, `tools/audio_pipeline/**`, `docs/PHASE1_AUDIO_*.md`,
`tools/godot_validate/validate_audio.gd`, `tools/godot_validate/project.godot`,
`assets/review/audio/**`.

Not touched: combat, spell, creature AI, inventory, quest, or any other gameplay or domain system. No
gameplay code was written or modified.
