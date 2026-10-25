"""Install only exact-hash owner-approved ElevenLabs recordings; never the rejected pilot."""
import hashlib
import json
import os
from pathlib import Path
import sys

MANIFEST = json.loads(Path(__file__).with_name('approved_public_voice.json').read_text())
APPROVED = MANIFEST['files']


def install(source: Path, primary: Path) -> None:
    data = {name: (source / name).read_bytes() for name in APPROVED}
    for name, content in data.items():
        if hashlib.sha256(content).hexdigest() != APPROVED[name] or not content.startswith(b'OggS'):
            raise ValueError('Unapproved public recording: ' + name)
    target = primary / 'vo'
    target.mkdir(parents=True, exist_ok=True)
    if {p.relative_to(target).as_posix() for p in target.rglob('*.ogg')} - APPROVED.keys():
        raise ValueError('Unexpected public voice files; use a fresh public payload')
    for name, content in data.items():
        temporary = target / (name + '.installing')
        temporary.parent.mkdir(parents=True, exist_ok=True)
        temporary.write_bytes(content)
        os.replace(temporary, target / name)  # Preserve retained-package hard links.
    assert {p.relative_to(target).as_posix() for p in target.rglob('*.ogg')} == APPROVED.keys()
    assert all(hashlib.sha256((target / name).read_bytes()).hexdigest() == digest for name, digest in APPROVED.items())
    receipt = target / 'OWNER_APPROVED_PUBLIC_VOICE.json'
    temporary = receipt.with_suffix('.installing')
    temporary.write_text(json.dumps(MANIFEST, indent=2) + '\n')
    os.replace(temporary, receipt)
    print(f'PASS: {len(APPROVED)} exact owner-approved public voices installed; old hard links preserved')


if __name__ == '__main__':
    install(Path(sys.argv[1]), Path(sys.argv[2]))
