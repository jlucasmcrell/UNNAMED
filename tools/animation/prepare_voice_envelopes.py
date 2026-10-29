"""Decode owned voice recordings on CPU into per-line 20 ms jaw envelopes."""
import argparse
from array import array
import json
import math
from pathlib import Path
import shutil
import subprocess

def prepare(source, output, ffmpeg):
    for voice in sorted(source.rglob('*.ogg')):
        target = output / voice.relative_to(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(voice, target)
        decoded = subprocess.run([str(ffmpeg), '-v', 'error', '-i', str(voice), '-f', 'f32le',
            '-ac', '1', '-ar', '16000', 'pipe:1'], check=True, capture_output=True, timeout=60)
        samples = array('f', decoded.stdout)
        jaw = []
        for start in range(0, len(samples), 320):
            window = samples[start:start + 320]
            rms = math.sqrt(sum(value * value for value in window) / max(1, len(window)))
            jaw.append(round(max(0, min(.65, (rms - .004) * 6.4)), 4))
        target.with_suffix('.ogg.envelope.json').write_text(json.dumps(dict(step_seconds=.02, jaw=jaw)), encoding='utf-8')
    print('VOICE_ENVELOPES_COMPLETE', len(list(output.rglob('*.ogg.envelope.json'))))

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--ffmpeg', type=Path, default=Path('C:/Otherreach_Worker/tools/ffmpeg/bin/ffmpeg.exe'))
    args = parser.parse_args()
    prepare(args.source, args.output, args.ffmpeg)
