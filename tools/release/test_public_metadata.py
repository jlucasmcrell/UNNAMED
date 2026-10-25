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
    old_notice=root/'retained_notice';old_notice.write_text('old notice')
    (game/'THIRD_PARTY_NOTICES').mkdir()
    os.link(old_notice,game/'THIRD_PARTY_NOTICES/NOTICE.txt')
    old_readme=root/'retained_readme';old_readme.write_text('old readme')
    os.link(old_readme,game/'README.md')
    write(game,'a'*40)
    assert old_notice.read_text()=='old notice' and old_readme.read_text()=='old readme'
    notice=(game/'THIRD_PARTY_NOTICES/NOTICE.txt').read_text()
    assert 'ElevenLabs' in notice and 'There is no AI-generated voice acting' not in notice
    assert (game/'README.md').read_text(encoding='utf-8')==Path(__file__).with_name('README_TESTER.md').read_text(encoding='utf-8-sig')
    assert retained.read_text()=='previous build'
    assert (game/'VERSION.txt').read_text()=='build Playtest 1\ncommit '+'a'*40+'\n'
    manifest=tomllib.loads((game/'.itch.toml').read_text())
    assert manifest['actions']==[dict(name='Play Otherreach',path='Otherreach.exe')]
    assert 'args' not in manifest['actions'][0]
print('PASS current notices/README, public metadata, no-argument launcher and retained hard-link isolation')
