# UNNAMED - Audio Pipeline

How audio is generated, what models are needed, and what each one is licensed under.

Companion to `AUDIO_DESIGN.md`, which defines what the sound should be.

---

## Why we generate rather than license

Free and royalty-free libraries are a licensing liability for a shipped game. Packs
routinely carry attribution requirements, limits on redistributing the audio as audio,
and non-commercial clauses that are not obvious from the download page. Tracking
provenance across hundreds of cues is real work with real downside.

Generated audio has a cleaner chain, provided the model licence permits commercial use
and the training data is compliant. Everything listed below is checked on both counts.

---

## Models

### Music — ACE-Step 1.5 XL

| | |
|---|---|
| **Licence** | **MIT**, commercially usable |
| **Training data** | Licensed music, royalty-free/public domain, and synthetic MIDI-to-audio |
| **Size** | XL diffusion model 9.97 GB; plus text encoder and VAE |
| **Speed** | A full song in seconds on a 3090 |
| **Variants** | `xl-base` (most diverse), `xl-sft` (best quality), `xl-turbo` (8 steps, ~6x faster) |

Quality is contested and should be judged on our own material rather than the
announcement's benchmarks. Expect it to be strong for **ambient beds, stings and
texture**, and to need verification before it carries a main theme.

**ComfyUI files**, from `Comfy-Org/ace_step_1.5_ComfyUI_files`:

| File | Destination | Size |
|---|---|---|
| `split_files/diffusion_models/acestep_v1.5_xl_turbo_bf16.safetensors` | `models/diffusion_models/` | 9.97 GB |
| `split_files/text_encoders/qwen_0.6b_ace15.safetensors` | `models/text_encoders/` | 1.19 GB |
| `split_files/vae/ace_1.5_vae.safetensors` | `models/vae/` | 0.34 GB |

The text encoder choice trades size for prompt comprehension: 0.6B (1.19 GB), 1.7B
(3.71 GB) or 4B (8.38 GB). The 0.6B is enough to evaluate quality; move up if prompts
are not being honoured. There is also a single-file `checkpoints/ace_step_1.5_turbo_aio.safetensors` (10.03 GB)
that bundles everything.

### SFX and Foley — MMAudio

| | |
|---|---|
| **Already present** | `X:\audio_encoders\mmaudio_large_44k_nsfw_gold_8.5k_final_fp16.safetensors` (1.96 GB) |
| **Also present** | `joyai_echo_vocoder.safetensors` (246 MB) |
| **Input** | Video or image plus a text prompt; generates synchronised audio |

MMAudio is a **video-to-audio** model, which makes it the right tool for Foley that must
line up with motion: sword swings, impacts, footsteps, creature vocalisations. Pairing it
with the existing asset library is the highest-value starting point because it needs no
download at all.

### Speech — VibeVoice and the existing voice bank

| | |
|---|---|
| **Model** | `X:\TTS` — a 7B model in 10 shards (~17 GB), plus a smaller 3-shard set (~5.2 GB) |
| **Also** | `X:\vibevoice`, `X:\stt` for transcription |
| **Voice clips** | 11 existing character clips in `W:\ComfyUI_LTX25\ComfyUI\input\h3_voices\` |

The existing clips are the important asset. They already cover GLYPH, MIRA, ZARA,
MARCUS, COLT, ALANA, CLARA, MADISON (three variants) and JOE, which means the cast has
reference voices to clone from and each race can be given a consistent voice family.

Nodes available: `RIFTDialogTTS`, `H3AutoVoices`, `JoyLTX_VoicesByName`, plus
ElevenLabs, FishAudio and HeyGen integrations that are **not** used, because they are
paid third-party services and would put a commercial dependency in the pipeline.

---

## What is already installed versus what needs fetching

| Capability | Status |
|---|---|
| SFX / Foley (MMAudio) | **Ready**, no download |
| Speech (VibeVoice, TTS, voice bank) | **Ready**, no download |
| Music (ACE-Step) | Nodes present, **weights need fetching** (~11.5 GB) |
| Audio VAE for video-with-sound | Present (`LTX23_audio_vae_bf16`, `joyai_echo_audio_vae`, `minimax_h3_audio_vae_fp32`) |

---

## VRAM planning

Same constraint as 3D: **BEAST and RAZER cannot both run heavy generation at once**, and
concept rendering already owns RAZER.

| Task | Approximate need | Where |
|---|---|---|
| ACE-Step XL (10 GB model) | ~12 GB | RAZER when idle, or BEAST between stages |
| ACE-Step 1.5 base (4.79 GB) | ~7 GB | either machine |
| MMAudio Foley | moderate | either machine |
| VibeVoice 7B | ~17 GB | BEAST only |

Total available: BEAST 24 GB, RAZER 12.9 GB. Speech therefore has to run on BEAST when
the 3D queue is paused. Music can use the `base` variant on RAZER if XL does not fit.

---

## Running a generation

ComfyUI provides the nodes, so audio is driven the same way as images and 3D: build a
graph and POST it to `/prompt`. Relevant nodes include `EmptyAceStep1.5LatentAudio`,
`EmptyAceStepLatentAudio`, `ConditioningStableAudio`, `SaveAudio`, `SaveAudioMP3`,
`SaveAudioOpus`, `LoadAudio` and `AudioEncoderEncode`.

Output convention, matching the asset pipeline: `audio/<category>/<category>_<subject>_<variant>.ogg`.

### Fetching models

`_fetch_model.py` streams a file directly and resumes an interrupted transfer:

```
python W:\UNNAMED\tools\asset_pipeline\_fetch_model.py ^
    --repo Comfy-Org/ace_step_1.5_ComfyUI_files ^
    --file split_files/vae/ace_1.5_vae.safetensors --dest R:\models\vae
```

**Do not use `hf_hub_download` for these.** On this machine it sat for minutes with no
output and zero bytes written, twice, while a direct request to the same URL returned
200 with a correct `Content-Length`. The snapshot helper was not making progress and gave
no indication of it. `_fetch_model.py` reports GB, percentage and MB/s every ten
seconds, so a stall is visible immediately rather than inferred.

---

## Licensing rules for this pipeline

1. **No paid third-party APIs.** ElevenLabs, FishAudio and HeyGen nodes exist but are
   excluded; a commercial game must not depend on a metered external service.
2. **Check the licence before downloading any model**, and record it in this file.
   Academic-only and non-commercial weights are excluded, the same rule already applied
   to the 3D node packs.
3. **Prefer permissive weights** (MIT, Apache-2.0) and compliant training data.
4. **Keep a provenance record** for every generated cue: model, version, prompt, seed.
   Cheap to do now, impossible to reconstruct later.
