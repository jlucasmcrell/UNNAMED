import os
from pathlib import Path
import tempfile
import tomllib
from write_public_metadata import write

with tempfile.TemporaryDirectory() as directory:
    root=Path(directory)
    game=root/'game';game.mkdir()
    (game/'Otherreach.exe').write_bytes(b'fixture')
    retained=root/'retained_version'
    retained.write_text('previous build')
    os.link(retained,game/'VERSION.txt')
    write(game,'a'*40)
    assert retained.read_text()=='previous build'
    assert (game/'VERSION.txt').read_text()=='build Playtest 1\ncommit '+'a'*40+'\n'
    manifest=tomllib.loads((game/'.itch.toml').read_text())
    assert manifest['actions']==[dict(name='Play Otherreach',path='Otherreach.exe')]
    assert 'args' not in manifest['actions'][0]
print('PASS public metadata, no-argument launcher and retained hard-link isolation')
