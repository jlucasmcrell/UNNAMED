# Phase-1 audio provenance

Every sound in the Ashen Hollow audio set is generated locally from text. No audio was ingested from
any source: there is no sample library, no ripped game or film audio, no purchased pack and no
external audio input of any kind. That makes provenance unusually simple to state, and it is stated
per asset as well as per model.

The generator records `prompt`, `seed`, `steps`, `cfg`, `sampler`, `scheduler`, `prompt_id`, the
generation host and the model hash for every sound, and all of that is written into
`assets/manifests/playable_prototype_audio.json`. The weight files themselves are hashed in
`assets/manifests/audio_models.json`.

---

## 1. Models used

### Stable Audio Open 1.0 — sound effects and ambience

| | |
|---|---|
| Role | Text-to-audio. Every sound effect and ambience bed in this set. |
| Source | `Comfy-Org/stable-audio-open-1.0_repackaged` on Hugging Face |
| File | `stable-audio-open-1.0.safetensors`, 4.52 GiB |
| SHA-256 | `7b20458a071231aa…` (full value in `assets/manifests/audio_models.json`) |
| License | **Stability AI Community License** (`stable-audio-community`) |
| Commercial use | **Permitted**, including commercially, for organisations under **USD $1,000,000** annual revenue |
| Obligations | Provide a copy of the licence; retain the notice *"This Stability AI Model is licensed under the Stability AI Community License, Copyright © Stability AI Ltd. All Rights Reserved"*; prominently display **"Powered by Stability AI"**; register at stability.ai/community-license |
| Above the threshold | The licence **terminates**; an enterprise licence is required |

### T5-base — text encoder

| | |
|---|---|
| Role | Text encoder for the audio latent. Not a generator; it produces no audio of its own. |
| Source | `google-t5/t5-base` on Hugging Face |
| File | `model.safetensors`, 0.83 GiB, installed as `t5_base.safetensors` |
| SHA-256 | `a90903540cc02cbe…` |
| License | **Apache-2.0** |
| Commercial use | Permitted, no attribution obligation beyond the licence text |

### Which repository, and why not the canonical one

The canonical `stabilityai/stable-audio-open-1.0` repository is **licence-gated** on Hugging Face:
downloading it returns HTTP 401 until someone accepts the terms while signed in. Accepting a licence
is the owner's legal act, not the pipeline's, so the Comfy-Org distribution mirror was used instead.
It is the mirror ComfyUI documents for this node set, it is not gated, and it ships the same
`LICENSE.md`.

**This is a decision the owner should confirm.** If the intent is to be bound by the canonical
repository's terms specifically, the gate needs accepting once by a human, and the checkpoint
re-downloaded. Nothing downstream changes: the file is the same weights.

---

## 2. Models deliberately not used

Reconnaissance found the audio *nodes* installed on all three hosts and almost no audio *weights*.
What existed and why it was not used matters as much as what was:

| Option | Where it exists | Why it is not used |
|---|---|---|
| **ACE-Step 1.5** | Installed on RAZER (`acestep_v1.5_xl_turbo_bf16`) | It is a **text-to-music** model. Section 22 forbids starting the music sprint, and it cannot produce a 200 ms sword impact, which is most of what this set is. RAZER also has 4.1 GB free VRAM and its model share is not reachable from BEAST. |
| **MMAudio** | Weights on the shared drive (`mmaudio_large_44k_nsfw_gold_8.5k_final_fp16.safetensors`) | No node pack is installed on any host, and the only weights present are a **community finetune** (a `nsfw_gold` variant) whose licence is not stated anywhere in the repository. Section 3.3 forbids using a model for production whose licence is unclear, and section 3.5 forbids assuming open weights are commercially safe. |
| **VibeVoice / RIFTDialogTTS / TTS path** | Previously installed | Explicitly off-limits and abandoned per the rig notes. Not touched. |
| **ElevenLabs, ByteDance Seed, Doubao, MiniMax Music 3, Fish Audio, HeyGen** | Nodes present on all hosts | All are **hosted API nodes**. They require an external service and an account, they send audio or prompts off the machine, and they are not local generation. Not used. |
| **LTX / MiniMax / joyai audio VAEs** | Present on BEAST and ASTRAL | These are autoencoders belonging to video models. They reconstruct audio for a video latent; they cannot be prompted for a sound effect. |

---

## 3. Generation environment

| | |
|---|---|
| Host | **BEAST** (local) |
| GPU | RTX 3090, 24 GB |
| ComfyUI | 0.34.0, `C:\Users\jluca\ComfyUI` |
| Install method | Two weight files added to existing `models/checkpoints` and `models/text_encoders`. **No existing file was modified, no custom node installed, no ComfyUI upgrade.** The LTX, Trellis and Z-Image paths are untouched. |
| Nodes | `CheckpointLoaderSimple`, `CLIPLoader` (`type: stable_audio`), `CLIPTextEncode`, `ConditioningStableAudio`, `EmptyLatentAudio`, `KSampler`, `VAEDecodeAudio`, `AudioAdjustVolume`, `SaveAudioAdvanced` (FLAC) |
| Sampler | `euler`, `simple` scheduler, 32 steps, cfg 7.0 |
| Negative prompt | `music, melody, speech, singing, voice, talking, cinematic, reverb, echo, distorted` — applied to every render |

BEAST was chosen because it is not the 3D-critical host (ASTRAL runs the Trellis work), its GPU had
22 GB free, and rendering locally means audio never crosses the network. RAZER was rejected on VRAM.
ASTRAL was rejected because it is on the critical 3D path.

## 4. Licensing rule compliance

Section 3 of the brief, point by point:

| Rule | Status |
|---|---|
| No ripped game/movie/TV audio | **No audio inputs of any kind.** Every sound is text-to-audio from a locally hosted model. There is nothing to rip. |
| No copyrighted sound libraries ingested | No library was ingested. There is no sample source in this pipeline. |
| No model with unclear/incompatible licensing for production | Stable Audio Open 1.0: Community Licence, commercial use permitted under the revenue threshold, obligations recorded above. T5: Apache-2.0. The two rejected candidates (MMAudio's community finetune, and the hosted APIs) were rejected on exactly this ground. |
| Mark `PROTOTYPE_ONLY_LICENSE` where a model is prototype-only | **Not applied, and the reason matters.** The Community Licence does permit commercial use below the revenue threshold, so these assets are not prototype-only. They *are* conditional: if Otherreach's annual revenue exceeds USD $1,000,000 the licence terminates and every asset in this set would need either an enterprise licence or replacement. |
| Do not assume open weights are commercially safe | Checked explicitly. MMaudio's weights were rejected for this reason despite being on disk and free to use. |
| Preserve prompt/seed/settings provenance | Recorded per asset, and written into the playable manifest. |

## 5. Attribution to carry into the game

Before any public or commercial build, the credits must carry both lines, and the licence text must
be included with the distribution:

```
Audio generated with Stable Audio Open 1.0.
This Stability AI Model is licensed under the Stability AI Community License,
Copyright © Stability AI Ltd. All Rights Reserved.

Powered by Stability AI
```

Text encoding uses T5 (Apache-2.0, Copyright © Google LLC).

## 6. What is not proven

- The full Community Licence text was read in part, not end to end. The obligations above are what
  the document states in the sections retrieved; **the complete terms should be reviewed before a
  commercial release**, in particular the registration requirement and the definition of annual
  revenue.
- No legal review has been performed. This document records what the pipeline did and what the
  licences say; it is not legal advice.
- Per-asset provenance is machine-recorded, but per-asset *listening* has not happened. See
  `PHASE1_AUDIO_SPRINT_STATUS.md` for what a spectrogram can and cannot establish.
