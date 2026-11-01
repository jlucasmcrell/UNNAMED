"""Cut the marketing video from rendered frame folders. Usage: edit.py edl.json out_dir
EDL: {"clips": [{"dir": "...", "from": 0, "frames": 150}, ...], "music": [["file", start_s, dur_s], ...]}
Clips are native 60 fps frame sequences, with short dissolves between clips,
an OTHERREACH title card first and a Playtest 2 end card last. Music is the game's own soundtrack only."""
import argparse, json, subprocess, os, shutil
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('edl', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--ffmpeg', default=shutil.which('ffmpeg'))
args = parser.parse_args()
if not args.ffmpeg:
    parser.error('Pass --ffmpeg <executable> or put ffmpeg on PATH')
FF = args.ffmpeg
fonts = Path(__file__).resolve().parents[2] / 'src/Presentation/Ui/Fonts'
FONT = (fonts / 'BarlowCondensed-SemiBold.ttf').as_posix().replace(':', r'\:')
FONT2 = (fonts / 'Barlow-Medium.ttf').as_posix().replace(':', r'\:')
XF = 0.5  # dissolve seconds

edl = json.loads(args.edl.read_text(encoding='utf-8-sig'))
out = str(args.output)
os.makedirs(out, exist_ok=True)
inputs, filters, labels, durs = [], [], [], []

def card(lines, dur):
    k = sum(1 for a in inputs if a == '-i')
    inputs.extend(['-f', 'lavfi', '-t', str(dur), '-i', 'color=c=black:s=1920x1080:r=60'])
    draw = []
    for text, size, y, font in lines:
        draw.append(f"drawtext=fontfile='{font}':text='{text}':fontsize={size}:fontcolor=0xE8E2D4:x=(w-text_w)/2:y={y}")
    filters.append(f"[{k}:v]{','.join(draw)},fade=t=in:st=0:d=0.8,fade=t=out:st={dur-0.8}:d=0.8,format=yuv420p,setsar=1,settb=AVTB[c{k}]")
    labels.append(f"c{k}"); durs.append(dur)

card([('OTHERREACH', 150, '(h-text_h)/2', FONT)], 3.5)
for i, c in enumerate(edl['clips']):
    idx = sum(1 for a in inputs if a == '-i')
    ext = c.get('ext', 'jpg')
    folder = Path(c['dir'])
    if not folder.is_absolute():
        folder = args.edl.resolve().parent / folder
    receipt = json.loads((folder / 'shot_done.json').read_text(encoding='utf-8-sig'))
    if receipt['fps'] != 60:
        raise ValueError(f'{folder}: reshoot at --fixed-fps 60; held 30 fps frames cannot make a smooth 60 fps film')
    # Existing EDLs count 30 fps edit frames. Keep their edit durations when fresh sources have twice as many samples.
    edit_fps = c.get('edit_fps', edl.get('edit_fps', 30))
    if edit_fps <= 0:
        raise ValueError('edit_fps must be positive')
    start, count = (round(c[key] * 60 / edit_fps) for key in ('from', 'frames'))
    if start < 0 or count <= 0 or start + count > receipt['frames']:
        raise ValueError(f'{folder}: edit exceeds the completed native frame range')
    for frame in range(start, start + count):
        if not (folder / f'f_{frame:05d}.{ext}').is_file():
            raise FileNotFoundError(f'{folder}: missing frame {frame}')
    inputs.extend(['-framerate', '60', '-start_number', str(start), '-i', str(folder / f'f_%05d.{ext}')])
    dur = count / 60.0
    filters.append(f"[{idx}:v]trim=end_frame={count},setpts=PTS-STARTPTS,scale=1920:1080:force_original_aspect_ratio=increase:flags=lanczos,crop=1920:1080,format=yuv420p,setsar=1,settb=AVTB[v{idx}]")
    labels.append(f"v{idx}"); durs.append(dur)
card([('OTHERREACH', 150, '(h-text_h)/2-60', FONT), ('Playtest 2', 64, '(h-text_h)/2+90', FONT2)], 5.0)

# chain dissolves
cur, t = labels[0], durs[0]
for n in range(1, len(labels)):
    off = t - XF
    filters.append(f"[{cur}][{labels[n]}]xfade=transition=fade:duration={XF}:offset={off:.3f}[x{n}]")
    cur, t = f"x{n}", off + durs[n]
total = t
filters.append(f"[{cur}]null[vout]")

# music
mi = []
for j, (f, start, dur) in enumerate(edl['music']):
    idx = sum(1 for a in inputs if a == '-i')
    inputs.extend(['-ss', str(start), '-t', str(dur), '-i', f])
    mi.append(idx)
if mi:
    chain = f"[{mi[0]}:a]aresample=48000[a0]"
    filters.append(chain)
    cur_a = 'a0'
    for j in range(1, len(mi)):
        filters.append(f"[{mi[j]}:a]aresample=48000[m{j}]")
        filters.append(f"[{cur_a}][m{j}]acrossfade=d=3[a{j}]")
        cur_a = f"a{j}"
    filters.append(f"[{cur_a}]atrim=end={total:.3f},afade=t=in:st=0:d=1.5,afade=t=out:st={total-4:.3f}:d=4[aout]")

fc = ';'.join(filters)
open(os.path.join(out, 'filter.txt'), 'w', encoding='utf-8').write(fc)
master = os.path.join(out, edl.get('name', 'otherreach_playtest2') + '_master.mov')
deliver = os.path.join(out, edl.get('name', 'otherreach_playtest2') + '_1080p60.mp4')
amap = ['-map', '[aout]'] if mi else []
subprocess.run([FF, '-v', 'error', '-y', *inputs, '-filter_complex_script', os.path.join(out, 'filter.txt'), '-map', '[vout]', *amap,
                '-c:v', 'prores_ks', '-profile:v', '3', '-pix_fmt', 'yuv422p10le', '-c:a', 'pcm_s16le', master], check=True)
subprocess.run([FF, '-v', 'error', '-y', '-i', master, '-c:v', 'libx264', '-preset', 'slow', '-crf', '16', '-pix_fmt', 'yuv420p',
                '-r', '60', '-movflags', '+faststart', '-c:a', 'aac', '-b:a', '256k', deliver], check=True)
print(f'total {total:.2f}s -> {deliver}')
