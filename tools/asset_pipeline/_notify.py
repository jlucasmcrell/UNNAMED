"""Audible notifications so an unattended run can be heard from another room.

Design note: a single alert for everything makes the alert useless. If every asset rings
the same way, the sound stops meaning anything within an hour. So the volume of events
drives the choice:

  * every asset landing  -> a short chime, deliberately quiet and brief
  * a stage finished     -> a distinct three-note rise
  * something needs Joe  -> spoken "HEY JOE", loud, and repeated

The last one is reserved for genuine interruption: a hard failure, a batch that ended,
or a server that could not be revived. Asset-by-asset events must never speak, or the
room fills with a voice reading filenames and the signal is lost.
"""
import argparse
import os
import sys
import time

# A rising figure is easier to notice across a room than a single tone, and the three
# events are distinguishable by shape rather than by volume alone.
CHIME_ASSET = [(784, 70), (1047, 110)]
CHIME_STAGE = [(659, 90), (880, 90), (1175, 160)]
ALARM = [(440, 130), (0, 60), (440, 130), (0, 60), (440, 200)]


def tones(sequence):
    """Play a list of (frequency, milliseconds); frequency 0 is a rest."""
    try:
        import winsound
    except ImportError:
        return False
    for frequency, duration in sequence:
        if frequency <= 0:
            time.sleep(duration / 1000.0)
            continue
        winsound.Beep(frequency, duration)
    return True


def speak(text, repeat=3, gap=0.7):
    """Say something out loud. Powershell's SAPI voice, no extra dependency."""
    import subprocess
    script = (
        "Add-Type -AssemblyName System.Speech; "
        "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
        "$s.Rate = 0; $s.Volume = 100; "
        + "".join(f'$s.Speak("{text}"); Start-Sleep -Milliseconds {int(gap*1000)}; '
                 for _ in range(repeat))
    )
    try:
        subprocess.run(["powershell", "-NoProfile", "-Command", script],
                       capture_output=True, timeout=30)
        return True
    except Exception:
        return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("event", choices=["asset", "stage", "attention", "test"])
    parser.add_argument("--message", default="HEY JOE")
    parser.add_argument("--quiet", action="store_true",
                        help="Chime for asset/stage events only; never speak")
    args = parser.parse_args()

    if args.event == "test":
        print("asset:", "ok" if tones(CHIME_ASSET) else "unavailable")
        print("stage:", "ok" if tones(CHIME_STAGE) else "unavailable")
        print("attention:", "ok" if speak(args.message, repeat=1) else "unavailable")
        return 0

    if args.event == "asset":
        tones(CHIME_ASSET)
        return 0

    if args.event == "stage":
        tones(CHIME_STAGE)
        return 0

    # Attention events are the only ones that speak, and they say the same words every
    # time so the phrase itself becomes the signal.
    tones(ALARM)
    if not args.quiet:
        speak(args.message, repeat=3)
    return 0


if __name__ == "__main__":
    sys.exit(main())
