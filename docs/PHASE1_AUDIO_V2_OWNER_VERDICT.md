# Phase-1 audio V2 — owner verdict and what it means for the next pass

Written after the owner listened to part of the V2 set and gave a verdict, so the next person to touch
this does not have to rediscover it. The measurements below came from that conversation, not from a
generation run.

---

## 1. The owner's verdict, in their words

> "All of the sounds are so similar to each other in each class, it's kind of a waste of time for me to
> go through them all. Just give me a mix of B and C at random at this point. Either one of those is
> usually what I pick. I think we will just have to find a better model for these after, but for now,
> these will do for the initial playtest. It's really hard to know whether the sounds work at all when
> they're not attached to an animation or visual. A lot of them just seem like they're *not enough*,
> but maybe they don't need to be."

Four distinct findings. Three are about the audio; the fourth is about the process.

## 2. Finding: candidates within a class are not distinguishable

The owner auditioned 15 sounds, then concluded that comparing the rest was not worth their time
because the three candidates in a class sound the same as each other.

This matches what was measured earlier and explained wrongly at first. `_seed_vs_prompt_probe.py`
showed that on a rewrite the same prompt at a new seed moves the spectral centroid 242 Hz, against
531 Hz for a rewritten prompt, and that six seeds produced six spectral centroids inside a 240 Hz band
with an identical duration. **The seed chooses which realisation of a sound you get; it does not
choose which sound.** Three seeds of one prompt are three takes of the same thing, which is precisely
what the owner heard.

So "generate three candidates per id" was the wrong affordance for this model. It multiplies the
listening work by three without multiplying the range of outcomes. A better shape would be three
*different prompts* per id, and one take of each.

**Consequence for the pipeline:** the round-based re-render ladder in `_make_prompt_variants.py` is
the right idea pointed at the wrong stage. Its prompt variation is what should be at generation time,
not only after a rejection.

## 3. Finding: the model is the ceiling

The owner's own conclusion, and the evidence supports it. Stable Audio 3 Small SFX at 8 steps, cfg 1.0
and the `lcm` sampler produces a narrow band of outcomes per prompt. Prompt variation moves it further
than the seed does, but the movement is still modest: the three hand-rewritten prompts moved the
centroid 1326 Hz, 1083 Hz and 68 Hz - and the third of those was a rewrite of a sound whose problem is
that it sits at 128 Hz, which rewording cannot fix.

**A different model is the likely answer for a real audio pass.** Not a different sampler setting and
not more candidates.

## 4. Finding: "not enough" is measurable, and it is mostly the trim

The owner's instinct is correct and quantified. Measuring the 228 delivered files against the spec:

| | |
|---|---|
| Delivered shorter than 75% of spec duration | **101 of 228 (44%)** |
| UI sounds | delivered at **18%** of spec - a 0.22 s tick arrives as 0.04 s |
| Median delivered level | -25.3 dBFS RMS |
| Below -40 dBFS RMS | 0 |

The cause is the single-transient trim in `_process_audio.py`, which clamps a sound to the first
energy lobe and floors it at `min_fraction` of the requested length - 18% for UI, 55% by default. The
trim is doing what it was written to do, and V1 shipped the same behaviour. But it means the delivered
length routinely has little to do with the spec's length, and a UI tick rendered as 40 ms is exactly
the kind of thing that reads as "not enough".

**Two readings, and they are not yet distinguished:**

- the trim is right and the spec's durations are aspirational, so the event contract should be told
  what it will actually get;
- the trim is too aggressive and these sounds should carry their full length.

This is a decision for a listening pass with a visual attached, which is the owner's next point.

## 5. Finding: the process cannot be judged without the game

> "It's really hard to know whether the sounds work at all when they're not attached to an animation
> or visual."

This is the most important sentence in the verdict, and it invalidates part of how the audition page
was framed. An isolated sound cannot be judged for whether it *works* - only for whether it is
pleasant. A footstep that sounds thin alone may be exactly right under a walk animation; a swing that
sounds weak alone may be carried by the visual arc.

**So the audition page answered the wrong question.** It asked "which of these three do you prefer in
isolation", when the useful question is "does this work in motion". A listening pass for the next
round should happen with the game running, not in a browser.

## 6. What was done with the verdict

The owner asked for a random B or C across the remaining sounds rather than 213 more auditions. That
was applied by `_autoselect_bc.py`:

| | |
|---|---|
| Owner-auditioned, left untouched | 15 (including the two they picked as A) |
| Automatically selected | 213 |
| Split | 107 B, 116 C, 2 A |
| Reproducible seed | 20260924 |

**Automatic picks are not recorded as auditions.** They carry `selection_basis: "auto_random_bc"` and
keep `human_auditioned: false`. An owner pick and a random choice are different facts, and a manifest
that recorded both as heard would claim a listening pass that did not happen - which is the one thing
a provenance record exists to prevent.

The three sounds rejected in the first report keep their `needs_rerender` flag, since the rejection
was real even though the set is now shipping for a prototype.

## 7. What to do next, in priority order

1. **Playtest with the game.** The owner's own point: none of this can be judged without animation and
   visuals. That is the only way to find out whether "not enough" is a defect or a non-issue.
2. **Resolve the duration question from that playtest.** If sounds read as thin in motion, relax the
   transient trim and re-process from the existing candidates - it is a re-run of one tool, not a
   regeneration, because every candidate is still on disk.
3. **Then choose a model.** If the set still reads as narrow after 1 and 2, the limitation is the
   model, and it should be replaced rather than re-prompted. Do not spend another pass on seeds.
4. **Only then** author further content. The 51 section 30A additions have had no listening at all.

## 8. What not to do

- Do not run another seed-variation pass. It is established that it reproduces the same sound.
- Do not treat the 213 automatic selections as reviewed. They are placeholders that happen to be
  playable.
- Do not conclude the set is artistically approved. It has never been heard in context by anyone.
