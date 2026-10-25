"""The new package's audio install must not change the retained build's hard links."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


with tempfile.TemporaryDirectory() as directory:
    root = Path(directory)
    source, package, recovery = [root / name for name in ('retained', 'new', 'recovery')]
    manifest = 'manifests/playable_prototype_audio_v3.json'
    credits = 'audio/CREDITS_audio_recovery.txt'
    audio = 'audio/chime.wav'
    old = {manifest: json.dumps({'sounds': [{'audio_id': 'old.01'}]}, indent=2).encode(),
           credits: b'retained credits', audio: b'retained audio'}
    for name, data in old.items():
        (source / name).parent.mkdir(parents=True, exist_ok=True)
        (package / name).parent.mkdir(parents=True, exist_ok=True)
        (source / name).write_bytes(data)
        os.link(source / name, package / name)
    (recovery / 'out/audio').mkdir(parents=True)
    (recovery / ('out/' + audio)).write_bytes(b'new audio')
    (recovery / ('out/' + credits)).write_bytes(b'new credits')
    row = dict(audio_id='new.01', delivered=audio, sha256=hashlib.sha256(b'new audio').hexdigest())
    (recovery / 'out/gap_rows.json').write_text(json.dumps({'sounds': [row]}))
    subprocess.run([sys.executable, str(Path(__file__).parent / 'recovery_20261016/install.py'),
                    str(recovery), str(package)], check=True)
    assert all((source / name).read_bytes() == data for name, data in old.items())
    assert (package / audio).read_bytes() == b'new audio'
    assert (package / credits).read_bytes() == b'new credits'
    assert len(json.loads((package / manifest).read_text())['sounds']) == 2
    print('PASS: retained manifest, credits and audio hard links are unchanged')
