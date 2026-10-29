"""Encode the short native proof using recorded frame timestamps and the real voice clock."""
import argparse
import json
from pathlib import Path
import statistics
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('frames', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--voice-root', type=Path)
args = parser.parse_args()
data = json.loads((args.frames / 'performance.json').read_text())
frames = data['frames']
assert frames, 'No real frames captured'
lines = ['ffconcat version 1.0']
duration = 0
voiced_duration = 0
for index, frame in enumerate(frames):
    same_segment = index + 1 < len(frames) and frames[index + 1]['segment'] == frame['segment']
    if same_segment:
        step = frames[index + 1]['seconds'] - frame['seconds']
    else:
        intervals = [b['seconds'] - a['seconds'] for a, b in zip(frames, frames[1:])
                     if a['segment'] == b['segment'] == frame['segment']]
        step = statistics.median(intervals) if intervals else 1 / 30
    assert step > 0, 'Non-monotonic capture clock'
    path = (args.frames / frame['file']).resolve()
    assert path.is_file(), path
    escaped = path.as_posix().replace("'", "'\\''")
    lines.extend(["file '" + escaped + "'", f'duration {step:.6f}'])
    duration += step
    if frame.get('voice_position') is not None:
        voiced_duration += step
lines.append(lines[-2])  # Concat needs the last image repeated to retain its duration.
args.output.parent.mkdir(parents=True, exist_ok=True)
manifest = args.output.with_suffix('.ffconcat')
manifest.write_text('\n'.join(lines) + '\n', encoding='utf-8')
ffmpeg = 'C:/Otherreach_Worker/tools/ffmpeg/bin/ffmpeg.exe'
command = [ffmpeg, '-hide_banner', '-y', '-f', 'concat', '-safe', '0', '-i', str(manifest)]
voice = None
if args.voice_root and data.get('line'):
    voice = args.voice_root / (data['line'] + '.ogg')
    assert voice.is_file(), voice
    position = frames[0].get('voice_position')
    assert position is not None, 'Capture predates voice-clock metadata; recapture before muxing'
    command += ['-ss', str(position), '-i', str(voice), '-af', f'atrim=duration={voiced_duration:.6f},apad', '-c:a', 'aac', '-b:a', '192k']
args.output.parent.mkdir(parents=True, exist_ok=True)
command += ['-vf', 'pad=ceil(iw/2)*2:ceil(ih/2)*2,fps=30', '-c:v', 'libx264', '-preset', 'fast', '-crf', '18',
            '-pix_fmt', 'yuv420p', '-t', str(duration), '-movflags', '+faststart', str(args.output)]
subprocess.run(command, check=True)
print('Native proof encoded:', args.output, 'seconds:', round(duration, 2), 'voice:', voice)
