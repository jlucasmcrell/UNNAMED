"""Build three missing Waystone families from the licensed, delivered Sonniss chime.

Install this private recovery root LAST with recovery_20261016/install.py, into a
new package whose manifest is a private copy. Never install into the source root.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def build(source: Path, destination: Path, ffmpeg: str) -> None:
    if destination.exists():
        raise ValueError('Fresh recovery root required; preserve prior evidence')
    manifest = source / 'manifests/playable_prototype_audio_v3.json'
    original = manifest.read_bytes()
    entry = next(row for row in json.loads(original)['sounds']
                 if row['audio_id'] == 'sfx.magic.mending_thread.resolve.01')
    provenance = entry['source']
    if provenance['kind'] != 'recording / sound library' or 'Sonniss' not in provenance['library']:
        raise ValueError('Expected the licensed recorded replacement, not the old generated sound')
    recording = source / entry['delivered']
    recording_hash = hashlib.sha256(recording.read_bytes()).hexdigest()
    delivered = 'audio/recorded_waystone/waystone_chime.wav'
    output = destination / 'out' / delivered
    output.parent.mkdir(parents=True)
    # The transient-rich recording measures -26.35 LUFS / -6.75 dBTP. A fixed
    # +4.35 dB reaches -22 LUFS while retaining its envelope and >2 dB headroom.
    subprocess.run([ffmpeg, '-hide_banner', '-nostdin', '-n', '-i', str(recording),
                    '-af', 'volume=4.35dB', '-ar', '48000', '-ac', '1',
                    '-c:a', 'pcm_s24le', str(output)], check=True, capture_output=True)
    result = subprocess.run([ffmpeg, '-hide_banner', '-nostdin', '-i', str(output),
                             '-af', 'loudnorm=I=-22:TP=-1:LRA=7:print_format=json',
                             '-f', 'null', '-'], check=True, capture_output=True, text=True)
    measurement, _ = json.JSONDecoder().raw_decode(result.stderr[result.stderr.rfind('{'):])
    if not -23 <= float(measurement['input_i']) <= -21 or float(measurement['input_tp']) > -1:
        raise ValueError(f'Chime loudness/headroom gate failed: {measurement}')
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    roles = {'touch': 'a waystone answers a touch', 'bind': 'a return point is bound',
             'arrive': 'arrival through a waystone'}
    rows = [dict(audio_id=f'sfx.travel.waystone.{name}.01', category='travel', group='magic',
                 gameplay_role=role, human_auditioned=False, channels=1, loop=False,
                 sample_rate=48000, subtype='PCM_24', seconds=entry['seconds'],
                 delivered=delivered, sha256=digest, source=provenance,
                 loudness_lufs=float(measurement['input_i']),
                 true_peak_dbtp=float(measurement['input_tp']),
                 processing={'source_sha256': recording_hash, 'gain_db': 4.35})
            for name, role in roles.items()]
    (destination / 'out/gap_rows.json').write_text(json.dumps({'sounds': rows}, indent=2) + '\n')
    existing = source / 'audio/CREDITS_audio_recovery.txt'
    credits = existing.read_text(encoding='utf-8-sig') if existing.exists() else ''
    (destination / 'out/CREDITS_audio_recovery.txt').write_text(
        credits + '\nWaystone touch/bind/arrival: ' + provenance['library'] + '; '
        + provenance['pack'] + '; ' + provenance['file'] + '. '
        + provenance['licence'] + '. Delivered recording gain +4.35 dB.\n', encoding='utf-8')
    assert manifest.read_bytes() == original
    assert hashlib.sha256(recording.read_bytes()).hexdigest() == recording_hash
    (destination / 'VERIFIED.json').write_text(json.dumps(dict(
        status='DELIVERY_AND_LOUDNESS_VERIFIED_AUDITION_PENDING', source=str(recording),
        source_sha256=recording_hash, output_sha256=digest, measurement=measurement,
        families=[row['audio_id'] for row in rows], source_unchanged=True), indent=2) + '\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_primary', type=Path)
    parser.add_argument('recovery_root', type=Path)
    parser.add_argument('--ffmpeg', required=True)
    args = parser.parse_args()
    build(args.source_primary, args.recovery_root, args.ffmpeg)
