"""CPU decode and frame-diff proof for an ordinary packaged-game dialogue clip."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
from fractions import Fraction


def check_dialogue(index, goldens, observations, ffmpeg=None, ffprobe=None):
    video = goldens.get('dialogue_video', {})
    path = index.parent / video.get('file', '__MISSING__')
    errors, proof = [], {}
    if not path.is_file():
        return ['Missing real-time packaged-game dialogue video'], proof
    with path.open('rb') as stream:
        if hashlib.file_digest(stream, 'sha256').hexdigest() != video.get('sha256'):
            return ['Dialogue video hash differs from the golden index'], proof
    frames = [s for s in observations if video.get('start_frame', -1) <= s['frame'] <= video.get('end_frame', -1)]
    if not frames or any(s.get('conversation') != 'npc.cairnwold.mara_ling' or not s.get('normalCamera') for s in frames):
        return ['Dialogue clip has no continuous normal-game Mara conversation observations'], proof
    times = sorted({s['wallSeconds'] for s in frames})
    wall = times[-1] - times[0]
    if wall < 10 or max((b-a for a, b in zip(times, times[1:])), default=99) > .75:
        errors.append('Dialogue clip lacks 10 continuous seconds of real-time observations')
    ffmpeg = ffmpeg or shutil.which('ffmpeg')
    ffprobe = ffprobe or shutil.which('ffprobe')
    if not ffmpeg or not ffprobe:
        return errors + ['FFmpeg and ffprobe are required for decoded dialogue frame-diff verification'], proof
    try:
        probe = subprocess.run([str(ffprobe), '-v', 'error', '-show_streams', '-show_format', '-of', 'json', str(path)],
                               check=True, capture_output=True, text=True, timeout=30)
        data = json.loads(probe.stdout)
        stream = next(s for s in data['streams'] if s['codec_type'] == 'video')
        fps = float(Fraction(stream['avg_frame_rate']))
        duration = float(data['format']['duration'])
        if stream['width'] != frames[0].get('viewportWidth') or stream['height'] != frames[0].get('viewportHeight'):
            errors.append('Dialogue video is not the full native game viewport')
        if duration < 10 or abs(duration - wall) > max(.75, wall * .1):
            errors.append('Dialogue video duration differs from real wall-clock gameplay')
        if not any(s['codec_type'] == 'audio' for s in data['streams']):
            errors.append('Dialogue clip has no captured game audio')
        decoded = subprocess.run([str(ffmpeg), '-nostdin', '-v', 'error', '-hwaccel', 'none', '-threads', '2', '-i', str(path),
                                  '-map', '0:v:0', '-an', '-f', 'framemd5', '-'],
                                 check=True, capture_output=True, text=True, timeout=120)
        hashes = [line.rsplit(',', 1)[1].strip() for line in decoded.stdout.splitlines() if line and not line.startswith('#')]
        longest, run, repeats = 1, 1, 0
        for previous, current in zip(hashes, hashes[1:]):
            repeats += previous == current
            run = run + 1 if previous == current else 1
            longest = max(longest, run)
        if fps <= 0 or len(hashes) < duration * fps * .95:
            errors.append('Dialogue video did not decode completely')
        if fps > 0 and longest / fps > .5:
            errors.append('Dialogue video contains a repeated-frame freeze longer than half a second')
        proof = {'file': str(path), 'sha256': video['sha256'], 'duration': duration, 'wall_seconds': wall,
                 'fps': fps, 'decoded_frames': len(hashes), 'adjacent_repeats': repeats,
                 'longest_identical_run': longest, 'width': stream['width'], 'height': stream['height'],
                 'decoder': 'FFmpeg software decode; full-frame MD5 comparisons'}
    except (OSError, ValueError, KeyError, StopIteration, ZeroDivisionError, subprocess.SubprocessError) as error:
        errors.append('Dialogue video decode/frame-diff failed: ' + str(error))
    return errors, proof
