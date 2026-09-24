"""Render waveform and spectrogram sheets so audio can be inspected rather than assumed.

This pipeline has no ears. Every other claim it makes is measured, and measurement cannot tell a
sword swing from a hiss: both can have the same peak, the same duration and the same loudness. What
a spectrogram shows is *structure*, and structure is most of what distinguishes the families here:

  * a transient     - a bright vertical edge at a known onset, then a decay. Footsteps, impacts.
  * a whoosh        - broadband, band-limited, rising then falling across the arc. Swings, thrusts.
  * a tonal body    - horizontal lines. A metallic ring, a resonance, an unstable ward.
  * a noise bed     - a broad even wash with no strong lines. Ambience, fire, wind.
  * a click/hiss    - energy concentrated above 8 kHz with no low-frequency body. The failure mode
                      for a weapon swing or a creature growl.

So the sheet is laid out as one row per sound: the waveform on the left, the log-frequency
spectrogram on the right, and the measured numbers underneath. That combination is enough to catch
a wrong-shaped sound, a clipped transient, a missing onset or an accidental music bed.

Log frequency, not linear: most of the informative content of a footstep or a growl sits below
2 kHz, and a linear axis spends three quarters of its height on near-silence.

Usage:
    python _audio_review.py --group weapon.swing --out assets/review/audio/swings.png
    python _audio_review.py --ids sfx.ui.select sfx.player.footstep.dirt.walk.01
"""
import argparse
import io
import json
import math
import os

import numpy as np
import soundfile as sf
from PIL import Image, ImageDraw

ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec.json")
QA = os.path.join(ASSETS, "manifests", "audio_qa.json")
OUT_ROOT = os.path.join(ASSETS, "audio")

WAVE_W = 300
SPEC_W = 700
ROW_H = 170
LABEL_H = 26
FMIN, FMAX = 30.0, 20000.0
N_FFT = 1024

# A magma-like ramp. Dark is quiet, bright is loud, and the middle stays distinguishable in
# greyscale print, which a red-green ramp would not.
RAMP = [(0.0, (8, 6, 18)), (0.20, (48, 22, 82)), (0.40, (126, 32, 96)),
        (0.60, (200, 62, 74)), (0.80, (246, 130, 48)), (1.0, (252, 236, 160))]


def ramp_lut(steps=256):
    lut = np.zeros((steps, 3), dtype=np.uint8)
    for index in range(steps):
        t = index / (steps - 1)
        for position in range(len(RAMP) - 1):
            t0, c0 = RAMP[position]
            t1, c1 = RAMP[position + 1]
            if t0 <= t <= t1:
                k = (t - t0) / (t1 - t0)
                lut[index] = [int(c0[i] + (c1[i] - c0[i]) * k) for i in range(3)]
                break
    return lut


LUT = ramp_lut()


def find_delivered(audio_id):
    for base, _dirs, files in os.walk(OUT_ROOT):
        for filename in files:
            if filename == f"{audio_id}.wav":
                return os.path.join(base, filename)
    return None


def spectrogram(x, rate):
    """Log-frequency magnitude spectrogram in dB, resampled onto a fixed pixel grid."""
    if x.ndim > 1:
        x = x.mean(axis=1)
    window = np.hanning(N_FFT)
    hop = max(1, N_FFT // 4)
    frames = []
    for start in range(0, max(1, x.size - N_FFT + 1), hop):
        frames.append(np.abs(np.fft.rfft(x[start:start + N_FFT] * window)))
    if not frames:
        frames = [np.abs(np.fft.rfft(np.zeros(N_FFT)))]
    magnitude = np.array(frames).T                     # (freq, time)
    freqs = np.fft.rfftfreq(N_FFT, 1.0 / rate)
    db = 20.0 * np.log10(np.maximum(magnitude, 1e-9))
    db -= db.max()
    db = np.clip((db + 70.0) / 70.0, 0.0, 1.0)

    # Map onto the pixel grid: rows are log-frequency, columns are time.
    rows = np.linspace(math.log10(FMAX), math.log10(FMIN), ROW_H)
    targets = 10.0 ** rows
    band = np.searchsorted(freqs, targets)
    band = np.clip(band, 0, freqs.size - 1)
    column_count = 480
    time_index = np.linspace(0, db.shape[1] - 1, min(column_count, db.shape[1])).astype(int)
    image = db[np.ix_(band, time_index)]
    return image, db


def waveform(x, width, height):
    if x.ndim > 1:
        x = x.mean(axis=1)
    if x.size == 0:
        x = np.zeros(1)
    index = np.linspace(0, x.size - 1, min(width, x.size)).astype(int)
    samples = x[index]
    peak = np.max(np.abs(samples)) or 1e-9
    normalized = samples / peak
    canvas = np.full((height, width), 0.5, dtype=np.float64)
    centre = height // 2
    for column, value in enumerate(normalized):
        span = int(abs(value) * (height / 2 - 1))
        if span == 0:
            canvas[centre, column] = 0.1
            continue
        top = max(0, centre - span)
        bottom = min(height - 1, centre + span)
        canvas[top:bottom + 1, column] = 0.1
    return canvas


def colourise(gray):
    indices = np.clip((gray * 255).astype(np.int32), 0, 255)
    return LUT[indices]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ids", nargs="*", default=None)
    parser.add_argument("--group", nargs="*", default=None)
    parser.add_argument("--category", nargs="*", default=None)
    parser.add_argument("--out", required=True)
    parser.add_argument("--max", type=int, default=6)
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)
    qa = {}
    if os.path.exists(QA):
        with io.open(QA, encoding="utf-8") as handle:
            qa = json.load(handle).get("sounds", {})

    selected = []
    for entry in spec["sounds"]:
        if args.ids and entry["audio_id"] not in args.ids:
            continue
        if args.group and entry["group"] not in args.group:
            continue
        if args.category and entry["category"] not in args.category:
            continue
        if args.ids or args.group or args.category:
            selected.append(entry)
    if not selected:
        print("  nothing selected")
        return 1
    selected = selected[:args.max]

    sheet_h = (ROW_H + LABEL_H) * len(selected)
    sheet = Image.new("RGB", (WAVE_W + SPEC_W, sheet_h), (16, 16, 20))
    draw = ImageDraw.Draw(sheet)

    for row, entry in enumerate(selected):
        audio_id = entry["audio_id"]
        path = find_delivered(audio_id)
        top = row * (ROW_H + LABEL_H)
        stats = qa.get(audio_id, {})
        label = (f"{audio_id}   {stats.get('delivered_seconds', '?')}s  "
                 f"peak {stats.get('peak_dbfs', '?')} dBFS  "
                 f"{stats.get('measured_lufs', '?')} LUFS  "
                 f"centroid {stats.get('spectral_centroid_hz', '?')} Hz")
        draw.text((6, top + 6), label, fill=(236, 236, 230))
        if path is None:
            draw.text((6, top + LABEL_H + 8), "NOT PROCESSED", fill=(230, 120, 120))
            continue

        audio, rate = sf.read(path, always_2d=True, dtype="float64")
        spec_image, _db = spectrogram(audio, rate)
        spec_rgb = colourise(spec_image)
        spec_pil = Image.fromarray(spec_rgb.astype(np.uint8)).resize(
            (SPEC_W, ROW_H), Image.BILINEAR)
        sheet.paste(spec_pil, (WAVE_W, top + LABEL_H))

        wave = waveform(audio, WAVE_W, ROW_H)
        wave_rgb = np.repeat((wave * 255).astype(np.uint8)[:, :, None], 3, axis=2)
        sheet.paste(Image.fromarray(wave_rgb), (0, top + LABEL_H))

        # Frequency guides, so a reader can place the energy without a colour bar.
        for frequency, text in ((100, "100Hz"), (1000, "1k"), (10000, "10k")):
            position = math.log10(FMAX / frequency) / math.log10(FMAX / FMIN)
            y = top + LABEL_H + int(position * ROW_H)
            if top + LABEL_H < y < top + LABEL_H + ROW_H:
                draw.line([(WAVE_W, y), (WAVE_W + SPEC_W, y)], fill=(90, 90, 100), width=1)
                draw.text((WAVE_W + 4, y + 1), text, fill=(210, 210, 210))

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    sheet.save(args.out)
    print(f"{args.out}  {sheet.width}x{sheet.height}  {len(selected)} sound(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
