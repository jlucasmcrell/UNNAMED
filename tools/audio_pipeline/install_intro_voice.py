"""Install only the six owner-approved ElevenLabs intro recordings; never the rejected pilot."""
import hashlib
import json
import os
from pathlib import Path
import sys

APPROVED = {
    'opening.01.ogg': 'ab89e297823c53e2eb90ebe392669fc2528739cd7fde979a6d8c41ca55d39528',
    'opening.02.ogg': 'c4c77d44b0173c2eb7268ee54d2e7c6df28621c3ab55b87c0a1aceaa3ec49beb',
    'opening.03.ogg': 'f60478c0441ea2f7e5a9a6b6abd3f4cf7292c58f84758c538f7f83f8265ebc98',
    'opening.04.ogg': '190baE2471757933d1de2a6b9cc2d4cbf670fedc06efdbE93455da9dcc9072ff'.lower(),
    'opening.05.ogg': '65d9fe96fb4a0be5fdb4716064c8e50b5d80a4d48280360835f4684f6ec23118',
    'opening.06.ogg': '9a91a8c18804ec02fb82d6268864b4a9f8b4d69877ec86d51bdf988d2ed92349',
}


def install(source: Path, primary: Path) -> None:
    data = {name: (source / name).read_bytes() for name in APPROVED}
    for name, content in data.items():
        if hashlib.sha256(content).hexdigest() != APPROVED[name] or not content.startswith(b'OggS'):
            raise ValueError('Unapproved intro recording: ' + name)
    target = primary / 'vo'
    target.mkdir(parents=True, exist_ok=True)
    if {p.name for p in target.glob('opening.*.ogg')} - APPROVED.keys():
        raise ValueError('Unexpected opening voice files; use a fresh public payload')
    for name, content in data.items():
        temporary = target / (name + '.installing')
        temporary.write_bytes(content)
        os.replace(temporary, target / name)  # Preserve retained-package hard links.
    assert {p.name for p in target.glob('opening.*.ogg')} == APPROVED.keys()
    assert all(hashlib.sha256((target / name).read_bytes()).hexdigest() == digest for name, digest in APPROVED.items())
    (target / 'OWNER_APPROVED_INTRO.json').write_text(json.dumps(dict(
        owner_approval='CODEX_PROGRESS.md 2026-10-25: Reva - Ancient Epic Narrator, Eleven v4',
        replaces='Rejected Chatterbox pilot; no other voice delivery installed', files=APPROVED), indent=2) + '\n')
    print('PASS: six exact owner-approved intro voices installed; old hard links preserved')


if __name__ == '__main__':
    install(Path(sys.argv[1]), Path(sys.argv[2]))
