"""Turn generated audio masters into delivered, measured, game-ready WAVs.

Masters come out of Stable Audio Open at 44.1 kHz, at a length chosen for the model rather than for
the game, and at whatever level the sampler happened to produce. This stage is where they become
assets: resampled to the production intermediate format, trimmed so a one-shot fires immediately,
looped so an ambience bed has no seam, and levelled against the relative targets the spec declares.

Why measurement matters more than usual here: the pipeline has no ears. Nothing in this sprint can
judge whether a sword swing sounds like a sword swing, so every claim made downstream is a measured
one - duration, onset delay, peak, LUFS, spectral centroid, spectral rolloff, loop seam
discontinuity - and the spectrogram sheets from _audio_review.py are the only channel through which
a human or a vision model can check the timbre at all. Anything that cannot be measured is recorded
as not checked rather than assumed.

Loudness is ITU-R BS.1770-4 integrated loudness with the standard K-weighting filters and gating,
implemented here because the pipeline Python has no pyloudnorm. Sounds shorter than a 400 ms gating
block cannot produce a gated integrated value, so they fall back to K-weighted RMS and say so in
`loudness_method` rather than reporting a number the standard does not define.

Usage:
    python _process_audio.py --audit
    python _process_audio.py --apply
    python _process_audio.py --apply --only sfx.ui.select
"""
import argparse
import io
import json
import math
import os
import sys

import numpy as np
import soundfile as sf

ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec.json")
MASTERS = os.path.join(ASSETS, "audio", "masters")
OUT_ROOT = os.path.join(ASSETS, "audio")
QA_OUT = os.path.join(ASSETS, "manifests", "audio_qa.json")

TARGET_RATE = 48000
TARGET_SUBTYPE = "PCM_24"
PEAK_CEILING_DBFS = -1.0        # leave a decibel of headroom; glTF-free assets still clip
ONSET_THRESHOLD = 0.02          # fraction of peak that counts as "the sound has started"
PRE_ROLL_S = 0.005              # keep a little in front of the transient so it is not clipped off
FADE_OUT_S = 0.015              # a hard stop at the end of a trimmed one-shot clicks
LOOP_CROSSFADE_S = 1.2          # long enough that a 20 s bed's blend is not audible as a pulse
# Two different shapes need two different cuts. A UI tick that arrived as four pulses should be cut
# hard to the first one, which means a high floor and only a scrap of tail. A footstep or an impact
# has a real decay that must survive, so the floor is low, the tail is longer, and the trim can never
# take more than about half the requested length - otherwise a 420 ms step gets cut to a 70 ms click,
# which is what the first attempt did.
TRANSIENT_PARAMS = {
    "ui": {"floor": 0.45, "hold_s": 0.008, "tail_s": 0.015, "min_fraction": 0.18},
    "default": {"floor": 0.10, "hold_s": 0.015, "tail_s": 0.030, "min_fraction": 0.55},
}

# audio_id prefix -> delivered directory, so the tree stays organised and no one has to guess where
# a file lives from its name alone.
TREE = {
    "sfx.player.": "player",
    "sfx.weapon.sword.": "weapons/sword",
    "sfx.weapon.bow.": "weapons/bow",
    "sfx.weapon.polearm.": "weapons/spear",
    "sfx.creature.ash_ember_hound.": "creatures/ash_ember_hound",
    "sfx.creature.bone_walker_husk.": "creatures/bone_walker_husk",
    "sfx.creature.animated_armour.": "creatures/animated_armour",
    "sfx.creature.bristleback_boar.": "creatures/bristleback_boar",
    "sfx.creature.cave_hunting_spider.": "creatures/cave_hunting_spider",
    "sfx.magic.impulse_bolt.": "magic/impulse_bolt",
    "sfx.magic.brace_ward.": "magic/brace_ward",
    "sfx.magic.mending_thread.": "magic/mending_thread",
    "sfx.magic.strain.": "magic/strain",
    "sfx.crafting.": "crafting",
    "sfx.interaction.": "interactions",
    "sfx.ui.": "ui",
    "amb.ashen_hollow.": "ambience/ashen_hollow",
    "amb.charwood_verge.": "ambience/charwood",
    "amb.blackvein_cut.": "ambience/blackvein",
    "amb.foldscar_ruin.": "ambience/foldscar",
}


def destination_dir(audio_id):
    """Delivered directory for a sound.

    Raises on no match rather than falling back to a scratch directory. The fallback silently put
    three ambience beds in `temp/` because their ids read `amb.charwood_verge.01` while the prefix
    map said `amb.charwood.`, and the manifest then pointed at a path outside the delivered tree.
    A mapping miss is a bug and should stop the run, not be absorbed.
    """
    for prefix, folder in sorted(TREE.items(), key=lambda kv: -len(kv[0])):
        if audio_id.startswith(prefix):
            return folder
    raise KeyError(f"no delivered directory is mapped for '{audio_id}'; add a prefix to TREE")


# ---------------------------------------------------------------------------------------------
# Loudness, per ITU-R BS.1770-4. Coefficients are the published 48 kHz biquads, which is exact here
# because everything is resampled to 48 kHz before measurement.
#
# The filters are applied in the frequency domain rather than with a time-domain recursion.
# scipy.signal cannot be imported on this machine at all: scipy.spatial's compiled extension is
# blocked by an Application Control policy ("An Application Control policy has blocked this file"),
# and scipy.signal imports scipy.spatial. Filtering by multiplying the FFT by the biquad's own
# frequency response is mathematically the same filter for a signal of this length, needs no
# compiled dependency, and is exact enough that the difference is at the noise floor.
# ---------------------------------------------------------------------------------------------
SHELF_B = (1.53512485958697, -2.69169618940638, 1.19839281085285)
SHELF_A = (1.0, -1.69065929318241, 0.73248077421585)
HPF_B = (1.0, -2.0, 1.0)
HPF_A = (1.0, -1.99004745483398, 0.99007225036621)


def biquad_response(b, a, freqs, rate):
    z = np.exp(-2j * np.pi * freqs / rate)
    return np.abs((b[0] + b[1] * z + b[2] * z ** 2) / (a[0] + a[1] * z + a[2] * z ** 2))


def k_weight(x, rate):
    """Apply the BS.1770 K-weighting filters to mono or multichannel audio, along axis 0."""
    if x.ndim == 1:
        x = x[:, None]
    n = x.shape[0]
    freqs = np.fft.rfftfreq(n, 1.0 / rate)
    response = (biquad_response(SHELF_B, SHELF_A, freqs, rate)
                * biquad_response(HPF_B, HPF_A, freqs, rate))
    spectrum = np.fft.rfft(x, axis=0) * response[:, None]
    return np.fft.irfft(spectrum, n=n, axis=0)


def resample_fft(x, rate_in, rate_out):
    """Band-limited resample by FFT. 44.1 kHz to 48 kHz is a rational ratio, so zero-padding the
    spectrum is exact for a band-limited signal and avoids a compiled resampler."""
    if rate_in == rate_out:
        return x
    n_in = x.shape[0]
    n_out = int(round(n_in * rate_out / rate_in))
    spectrum = np.fft.rfft(x, axis=0)
    bins_in = spectrum.shape[0]
    bins_out = n_out // 2 + 1
    if bins_out >= bins_in:
        padded = np.zeros((bins_out, spectrum.shape[1]), dtype=complex)
        padded[:bins_in] = spectrum
    else:
        padded = spectrum[:bins_out].copy()
    return np.fft.irfft(padded, n=n_out, axis=0) * (n_out / n_in)


def integrated_lufs(x, rate):
    """Gated integrated loudness, or None when the signal is too short for one gating block.

    Blocks are 400 ms with 75% overlap (100 ms hop), per the standard. A block is kept if it passes
    the -70 LUFS absolute gate, then the relative gate removes blocks more than 10 LU below the
    mean of what survived.
    """
    block = int(0.400 * rate)
    hop = int(0.100 * rate)
    if x.shape[0] < block:
        return None
    weighted = k_weight(x, rate)
    if weighted.ndim == 1:
        weighted = weighted[:, None]
    powers = []
    for start in range(0, weighted.shape[0] - block + 1, hop):
        chunk = weighted[start:start + block]
        powers.append(float(np.mean(np.sum(chunk ** 2, axis=1))))
    powers = np.array(powers)
    if powers.size == 0:
        return None
    loudness = -0.691 + 10.0 * np.log10(np.maximum(powers, 1e-12))
    keep = loudness > -70.0
    if not keep.any():
        return None
    relative = -0.691 + 10.0 * np.log10(np.mean(powers[keep])) - 10.0
    keep &= loudness > relative
    if not keep.any():
        return None
    return float(-0.691 + 10.0 * np.log10(np.mean(powers[keep])))


def k_weighted_rms_dbfs(x, rate):
    weighted = k_weight(x, rate)
    rms = float(np.sqrt(np.mean(weighted ** 2)))
    return 20.0 * math.log10(max(rms, 1e-12))


def dbfs(value):
    return 20.0 * math.log10(max(abs(float(value)), 1e-12))


def spectral_stats(x, rate):
    """Centroid and rolloff, from the mean magnitude spectrum. Cheap shape descriptors: a low
    centroid is a rumble or a thud, a high one is a click, hiss or scrape."""
    if x.ndim > 1:
        x = x.mean(axis=1)
    if x.size < 256:
        return None, None
    window = np.hanning(min(4096, x.size))
    step = max(1, window.size // 2)
    frames = []
    for start in range(0, x.size - window.size + 1, step):
        frames.append(np.abs(np.fft.rfft(x[start:start + window.size] * window)))
    if not frames:
        return None, None
    spectrum = np.mean(np.array(frames), axis=0)
    freqs = np.fft.rfftfreq(window.size, 1.0 / rate)
    total = float(spectrum.sum()) or 1e-12
    centroid = float((freqs * spectrum).sum() / total)
    cumulative = np.cumsum(spectrum) / total
    rolloff = float(freqs[min(int(np.searchsorted(cumulative, 0.85)), freqs.size - 1)])
    return centroid, rolloff


def find_onset(x, rate):
    """Where the main event starts, in seconds, plus the peak of the signal.

    Anchored on the loudest region rather than on the first sample over the threshold. Stable Audio
    often puts a small lead-in before the real event: one 7.2 s render of a 2.4 s creature idle had
    its energy peak at 4.93 s and a first threshold crossing at 0.58 s, and trimming from the
    crossing captured two seconds of near-silence while the actual event sat outside the window.
    So the envelope is searched for its maximum and then walked back to where that event began.
    """
    if x.ndim > 1:
        mono = np.max(np.abs(x), axis=1)
    else:
        mono = np.abs(x)
    peak = float(mono.max()) if mono.size else 0.0
    if peak <= 0:
        return 0.0, 0.0
    threshold = max(1e-6, ONSET_THRESHOLD * peak)
    anchor = int(np.argmax(envelope(mono, rate, 0.020)))
    index = anchor
    while index > 0 and mono[index] > threshold:
        index -= 1
    start = max(0, index - int(PRE_ROLL_S * rate))
    return start / rate, peak


def envelope(x, rate, window_s=0.005):
    """Short-time moving-average envelope, applied to a 1-D signal.

    Callers pass either the per-sample max across channels, which is what "has the sound started"
    is measured on, or an already-mono signal. The 1-D reduction is kept here so the function cannot
    silently return a 2-D array for a multichannel input.
    """
    if x.ndim > 1:
        x = np.max(np.abs(x), axis=1)
    span = max(1, int(window_s * rate))
    kernel = np.ones(span) / span
    return np.convolve(np.abs(x), kernel, mode="same")


def downmix_to_mono(raw):
    """Average the channels, unless that cancels.

    Stable Audio's two channels are not a stereo pair of one source; they are decorrelated renders,
    and on low-frequency material they can be close to anti-phase. Averaging one such master cost
    13 dB: the channels peaked at -13.7 dBFS and the mono sum at -27.1 dBFS. That is not a quieter
    version of the sound, it is a comb-filtered one, and it also fooled the level match into
    applying 25 dB of gain on top of it.

    So the average is used when it behaves, and the louder single channel when it does not. Which
    happened is recorded, because falling back changes the delivered asset.
    """
    if raw.shape[1] == 1:
        return raw, "mono_source"
    summed = raw.mean(axis=1, keepdims=True)
    loudest_sum = float(np.max(np.abs(summed)))
    loudest_channel = float(np.max(np.abs(raw)))
    if loudest_sum < 0.7 * loudest_channel:
        index = int(np.argmax(np.max(np.abs(raw), axis=0)))
        return raw[:, index:index + 1], f"loudest_channel_{index}_after_antiphase_cancellation"
    return summed, "channel_average"


def single_transient_length(x, rate, start, requested_samples, group):
    """Length of the first energy lobe, for sounds that should be one hit and not a pulse train."""
    params = TRANSIENT_PARAMS.get(group, TRANSIENT_PARAMS["default"])
    segment = x[start:start + requested_samples]
    if segment.shape[0] < 8:
        return requested_samples, None
    env = envelope(segment, rate)
    if env.size == 0:
        return requested_samples, None
    peak_index = int(np.argmax(env))
    peak = float(env[peak_index]) or 1e-12
    floor = params["floor"] * peak
    hold = max(2, int(params["hold_s"] * rate))
    minimum = int(params["min_fraction"] * requested_samples)
    tail = int(params["tail_s"] * rate)

    # The first lobe ends where the envelope stops falling and starts rising again. A decaying
    # footstep has no such point, which is why the decay-floor rule below also exists; a UI tick
    # that arrived as four pulses does, and this is what cuts it to one.
    lobe_end = None
    for index in range(peak_index + 1, env.size):
        if env[index] < 0.6 * peak and env[index] > env[index - 1]:
            lobe_end = index
            break

    run = 0
    by_floor = requested_samples
    for index in range(peak_index, env.size):
        if env[index] < floor:
            run += 1
            if run >= hold:
                by_floor = index - run + tail
                break
        else:
            run = 0

    if lobe_end is not None and lobe_end + tail < by_floor:
        length = min(requested_samples, lobe_end + tail)
        if length >= minimum:
            return length, {"first_lobe_s": round(length / rate, 4),
                            "requested_s": round(requested_samples / rate, 4),
                            "cut_by": "first_lobe_boundary"}

    length = min(requested_samples, by_floor)
    if length < minimum:
        return minimum, {"first_lobe_s": round(minimum / rate, 4),
                         "requested_s": round(requested_samples / rate, 4),
                         "clamped_to_minimum": True}
    return length, {"first_lobe_s": round(length / rate, 4),
                    "requested_s": round(requested_samples / rate, 4),
                    "cut_by": "decay_floor"}


def loop_crossfade(x, fade_samples, loop_length):
    """Blend across the loop point so the wrap is continuous.

    The bed is treated as `loop_length` samples followed by `fade_samples` of spare material. The
    first `fade_samples` of the result blend from that spare material into the head, so:

        out[0]   == x[loop_length]        (exactly what would follow the loop point)
        out[-1]  == x[loop_length - 1]    (unchanged)

    which makes the wrap from last sample to first continuous by construction rather than by fading.

    Blending the *end of the bed* into the head instead - the obvious-looking mistake, and the one
    the first version made - jumps the loop point backwards by the fade length, and measured worse
    than doing nothing at all (seam step ratio 10.8 before, 26.9 after).

    The seam is judged against the bed's own sample-to-sample movement rather than in absolute
    terms. A raw step of 0.02 means nothing on its own: in a quiet, smooth bed it is a click, in a
    loud granular one it is ordinary. `seam_step_ratio` is the step across the join divided by the
    mean absolute first difference inside the bed, so a ratio near 1 means the join is
    indistinguishable from the material around it.
    """
    if x.ndim == 1:
        x = x[:, None]
    fade = int(min(fade_samples, loop_length // 3, x.shape[0] - loop_length))
    if fade < 8:
        return x[:loop_length], None
    head = x[:fade].astype(np.float64)
    after = x[loop_length:loop_length + fade].astype(np.float64)
    ramp = np.linspace(0.0, 1.0, fade)[:, None]
    blended = head * ramp + after * (1.0 - ramp)

    internal = float(np.mean(np.abs(np.diff(x[:loop_length], axis=0)))) or 1e-12
    before = float(np.max(np.abs(x[0] - x[loop_length - 1])))
    out = np.concatenate([blended, x[fade:loop_length].astype(np.float64)], axis=0)
    after_step = float(np.max(np.abs(out[0] - out[-1])))

    # After a correct crossfade the wrap is `x[loop_length - 1]` followed by `x[loop_length]`, which
    # is a *natural* continuation of the source rather than a join between two unrelated points. So
    # the residual step is not a discontinuity that needs to be small - it is whatever the signal
    # happens to do at that instant, and comparing it to the mean step is the wrong yardstick. A
    # granular bed like Charwood Verge has a mean step 25x larger than a smooth one and single
    # samples routinely exceed it.
    #
    # The question that actually matters is whether the wrap is distinguishable from ordinary
    # signal. `seam_percentile_after` answers that directly: where the wrap's step falls in the
    # distribution of the bed's own sample-to-sample steps. Below 99.5 means it is not an outlier.
    diffs = np.abs(np.diff(x[:loop_length], axis=0))
    diffs = diffs.max(axis=1) if diffs.ndim > 1 else diffs
    percentile = float((diffs < after_step).mean() * 100.0) if diffs.size else 100.0

    return out, {
        "loop_length_s": round(loop_length / 48000, 3),
        "seam_before": round(before, 6),
        "seam_after": round(after_step, 6),
        "seam_step_ratio_before": round(before / internal, 4),
        "seam_step_ratio_after": round(after_step / internal, 4),
        "seam_percentile_after": round(percentile, 3),
        "internal_mean_step": round(internal, 6),
        "fade_samples": int(fade),
    }


def channel_mean_square(x):
    return float(np.sqrt(np.mean(x.astype(np.float64) ** 2)))


def process(entry, master_path):
    """Return (audio, rate, qa) for one sound, or raise ValueError with the reason."""
    raw, rate = sf.read(master_path, always_2d=True, dtype="float64")
    if raw.size == 0:
        raise ValueError("empty")

    # Resample first, so every later measurement and every loudness filter is at 48 kHz.
    if rate != TARGET_RATE:
        raw = resample_fft(raw, int(rate), TARGET_RATE)
        rate = TARGET_RATE

    channels = entry["channels"]
    if channels == 1:
        raw, downmix = downmix_to_mono(raw)
    elif raw.shape[1] == 1:
        raw = np.repeat(raw, 2, axis=1)
        downmix = "duplicated_to_stereo"
    else:
        downmix = "kept_stereo"

    qa = {"sample_rate": rate, "channels": int(raw.shape[1]), "downmix": downmix,
          "master_seconds": round(raw.shape[0] / rate, 4)}

    if entry["loop"]:
        wanted = int(round(entry["seconds"] * rate))
        if raw.shape[0] > wanted:
            fade = int(LOOP_CROSSFADE_S * rate)
            raw, seam = loop_crossfade(raw[:wanted + fade], fade, wanted)
            raw = raw[:wanted]
            qa["loop"] = seam
        else:
            # Not enough material for a full-length bed plus a fade, so the loop point moves back to
            # whatever the render actually delivered rather than shipping a padded bed.
            fade = int(LOOP_CROSSFADE_S * rate)
            loop_length = max(fade * 3, raw.shape[0] - fade)
            raw, seam = loop_crossfade(raw, fade, loop_length)
            raw = raw[:loop_length]
            qa["loop"] = seam
            qa["loop_shortened"] = True
        qa["trim"] = "loop_crossfade"
    else:
        onset_s, peak = find_onset(raw, rate)
        start = int(round(onset_s * rate))
        wanted = int(round(entry["seconds"] * rate))
        if entry.get("single_transient"):
            wanted, lobe = single_transient_length(raw, rate, start, wanted, entry["group"])
            if lobe:
                qa["transient_trim"] = lobe
        end = start + wanted
        if end > raw.shape[0]:
            pad = end - raw.shape[0]
            raw = np.concatenate([raw, np.zeros((pad, raw.shape[1]))], axis=0)
        raw = raw[start:end]
        fade = min(int(FADE_OUT_S * rate), raw.shape[0] // 4)
        if fade > 4:
            raw = raw.copy()
            raw[-fade:] *= np.linspace(1.0, 0.0, fade)[:, None]
        qa["trim"] = "onset"
        qa["onset_s"] = round(onset_s, 4)
        qa["leading_silence_s"] = round(onset_s, 4)
        qa["source_peak"] = round(dbfs(peak), 2)

    # Level. Loudness is measured before gain so the record shows what the model produced.
    mono_for_loudness = raw.mean(axis=1)
    lufs = integrated_lufs(mono_for_loudness, rate)
    method = "bs1770_integrated"
    if lufs is None:
        lufs = k_weighted_rms_dbfs(mono_for_loudness, rate)
        method = "k_weighted_rms_dbfs (too short for a 400 ms gating block)"
    gain_db = entry["loudness_lufs"] - lufs
    peak_before = float(np.max(np.abs(raw))) or 1e-12
    predicted_peak = dbfs(peak_before) + gain_db
    limited = False
    if predicted_peak > PEAK_CEILING_DBFS:
        gain_db -= (predicted_peak - PEAK_CEILING_DBFS)
        limited = True
    raw = raw * (10.0 ** (gain_db / 20.0))

    qa.update({
        "loudness_method": method,
        "measured_lufs": round(float(lufs), 2),
        "target_lufs": entry["loudness_lufs"],
        "gain_db": round(float(gain_db), 2),
        "gain_limited_by_peak": limited,
        "delivered_seconds": round(raw.shape[0] / rate, 4),
        "duration_error_s": round(abs(raw.shape[0] / rate - entry["seconds"]), 4),
        "peak_dbfs": round(dbfs(np.max(np.abs(raw))), 2),
        "rms_dbfs": round(20.0 * math.log10(max(channel_mean_square(raw), 1e-12)), 2),
        "dc_offset": round(float(np.mean(raw)), 6),
        "clipped_samples": int(np.sum(np.abs(raw) >= 0.999)),
        "near_silent": bool(np.max(np.abs(raw)) < 10 ** (-40 / 20)),
    })
    centroid, rolloff = spectral_stats(raw, rate)
    qa["spectral_centroid_hz"] = None if centroid is None else round(centroid, 1)
    qa["spectral_rolloff85_hz"] = None if rolloff is None else round(rolloff, 1)
    return raw, rate, qa


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--only", nargs="*", default=None)
    parser.add_argument("--category", nargs="*", default=None)
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)
    entries = {e["audio_id"]: e for e in spec["sounds"]}

    qa_all = {}
    if os.path.exists(QA_OUT):
        with io.open(QA_OUT, encoding="utf-8") as handle:
            qa_all = json.load(handle).get("sounds", {})

    selected = sorted(entries)
    if args.only:
        selected = [i for i in selected if i in args.only]
    if args.category:
        selected = [i for i in selected if entries[i]["category"] in args.category]

    written = failed = skipped = aliased = 0
    print(f"  {'audio id':<48} {'len':>6} {'peak':>7} {'LUFS':>7} {'gain':>6}  {'centroid':>9}")
    print("  " + "-" * 92)
    for audio_id in selected:
        entry = entries[audio_id]
        if entry.get("alias_of"):
            aliased += 1
            continue
        master = os.path.join(MASTERS, f"{audio_id}.flac")
        if not os.path.exists(master):
            skipped += 1
            continue
        try:
            audio, rate, qa = process(entry, master)
        except (ValueError, OSError) as exc:
            print(f"  FAIL {audio_id:<44} {type(exc).__name__}: {exc}")
            failed += 1
            continue
        folder = os.path.join(OUT_ROOT, destination_dir(audio_id))
        os.makedirs(folder, exist_ok=True)
        target = os.path.join(folder, f"{audio_id}.wav")
        if args.apply:
            sf.write(target, audio, rate, subtype=TARGET_SUBTYPE)
            qa["delivered"] = f"audio/{destination_dir(audio_id)}/{audio_id}.wav"
            qa["bytes"] = os.path.getsize(target)
            qa_all[audio_id] = qa
        written += 1
        print(f"  {audio_id:<48} {qa['delivered_seconds']:>6.2f} {qa['peak_dbfs']:>7.2f} "
              f"{qa['measured_lufs']:>7.2f} {qa['gain_db']:>6.2f}  "
              f"{qa['spectral_centroid_hz'] or 0:>9.0f}")

    if args.apply:
        os.makedirs(os.path.dirname(QA_OUT), exist_ok=True)
        with io.open(QA_OUT, "w", encoding="utf-8") as handle:
            json.dump({"version": 1, "sounds": qa_all}, handle, indent=2)
            handle.write("\n")
    print()
    print(f"  {written} processed{'  (written)' if args.apply else '  (audit only)'}, "
          f"{skipped} no master, {aliased} alias, {failed} failed")
    if args.apply:
        print(f"  qa -> {QA_OUT}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
