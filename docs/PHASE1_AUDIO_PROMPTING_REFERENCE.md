# Prompting Stable Audio 3 Small SFX — what the documentation actually says

Recorded because the V3 prompts were written from these sources and nobody should have to re-derive
them. Until this document existed, the reasoning lived only in a script docstring, which means a
reviewer could not check the reading and a later pass could not tell whether the prompt style was a
decision or an accident.

Sources, both read in full during the V3 pass:

- [Stability AI, Prompt Guide for stable-audio-3](https://github.com/Stability-AI/stable-audio-3/blob/main/docs/guides/prompting.md)
- [ComfyUI, Stable Audio 3.0 Day-0 Support](https://blog.comfy.org/p/stable-audio-3-day-0-support)

**What this is not:** a substitute for listening. It is what the model's authors say about getting
better output, and it was followed. Whether it worked is an audition, and none has happened.

---

## 1. The three elements of a sound-effect prompt

The guide names what a sound-effect prompt needs:

> - **The core source** — exactly what object, instrument, or synthesizer is making the sound?
> - **The action** — how is the sound being triggered, and how long does it last? *(e.g., slamming
>   shut with fast decay, or a massive suck-back followed by a supersonic crack)*
> - **The production/characteristics** — where is the mic placed, and what's the room character? How
>   is the sound processed?

The V2 prompts had a source and an action and then stopped. Nothing said where the microphone was or
what the space did, which is the third element.

## 2. TrackType: SFX

The guide lists AudioSparx metadata tags that help, and the one for this work is unambiguous:

> `TrackType: SFX` — tends to produce more semantically reasonable sound effects and samples.

No V1, V2 or earlier prompt carried it. Every V3 prompt opens with it.

## 3. The model is trained on metadata, so prompt in that register

> **Think about the training datasets.** Prompt adherence and audio quality are closely linked with the
> dataset the model was trained on. Stable Audio 3 uses audio from Freesound and AudioSparx along with
> their metadata. Prompts that align with that kind of text and audio tend to produce the best results.

This is why the guide's own examples read as prose sentences rather than comma-separated tags:

> A blunt, powerful "thud" made by slamming a wooden desk drawer shut. It has a pronounced low-mid
> body, making it feel heavy, and is given a touch of analog distortion for aggressive character.

> A classic, natural recording of 14-inch hi-hats played tightly closed. Zero ring and a very fast
> decay.

V1 and V2 prompts were keyword lists. V3 prompts are sentences.

## 4. Length is a prompt token, and it should fit the sound

Two separate statements:

> **Set a realistic duration.** ... results tend to be better when you choose a duration that fits what
> you're describing.

> *Tip: Set a short duration. Most sound effects are brief, so set the length accordingly before
> generating.*

And every sound-effect example in the ComfyUI post ends with a length token rather than stating it in
prose:

> Footsteps on gravel, steady walking pace, close perspective. **Length: 8 seconds**
> Car speeding past at high velocity, doppler effect, realistic whoosh. **Length: 3 seconds**

V2 said "0.42 seconds long" as the second item of a comma list, and rendered at 2.5x the target before
trimming down. V3 puts `Length: N seconds` last, as the examples do, and renders close to the target.

## 5. More detail is generally better

> Use the **Gradio Prompt Assistant** ... it will generate or refine a prompt for you. Short prompts
> especially benefit from this, since more detail generally means better results

V3 prompts average 233 characters against V2's ~150, and the added length is description rather than
tags.

## 6. Model compatibility, which catches a wrong choice

| Model | Music | Stems/Solo | Samples/SFX |
|---|---|---|---|
| `medium` | ✓ | ✓ | ✓ |
| `small-music` | ✓ | ✓ | — |
| **`small-sfx`** | — | — | ✓ |

`small-sfx` is the only member of the family that does SFX at all, and it cannot do music. The brief
specified it, and this table confirms the brief was right for the opposite reason to the obvious one:
it is not simply the cheapest SFX option, it is the only SFX option.

Also worth recording for anyone tempted to reach for a bigger checkpoint: `medium` would do SFX too,
but the owner's verdict was that the problem is model quality in this class of sound, not capacity,
and swapping to a model that also does music would not address that.

## 7. What the documentation does not claim

Nothing in either source claims the model produces *believable animal vocalisations*, which is what
the original V1 set failed at. The guide's SFX examples are all **material interactions** - a drawer
shutting, hi-hats, footsteps on gravel. The V3 creature prompts were still written to the same three
elements, but nobody should read this document as evidence that the creature sounds are now good. The
guide offers no basis for that claim and neither does any measurement taken here.

## 8. What was applied

| | V1 / V2 | V3 |
|---|---|---|
| Lead tag | none | `TrackType: SFX` |
| Register | comma-separated keywords | prose sentences |
| Elements | source, action | source, action, production |
| Length | prose mid-list | `Length: N seconds` trailing token |
| Render length | 2.5x target, trimmed down | target + max(0.35 s, 15%) |
| Delivered length | clamped to 18-55% of spec by the lobe trim | exactly the spec length |
