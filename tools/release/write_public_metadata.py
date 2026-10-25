"""Install current build identification, offline launcher, README and notices atomically."""
import argparse
import os
from pathlib import Path
import re


def write(game, commit, label='Playtest 1'):
    assert game.is_dir() and (game/'Otherreach.exe').is_file()
    assert re.fullmatch(r'[0-9a-fA-F]{40}', commit)
    assert label and '\n' not in label and '\r' not in label
    files = {
        'VERSION.txt': f'build {label}\ncommit {commit}\n',
        '.itch.toml': '[[actions]]\nname = "Play Otherreach"\npath = "Otherreach.exe"\n',
    }
    here=Path(__file__).parent
    files['README.md']=(here/'README_TESTER.md').read_text(encoding='utf-8-sig')
    notices=here/'payload/THIRD_PARTY_NOTICES'
    for source in notices.rglob('*'):
        if source.is_file():
            files['THIRD_PARTY_NOTICES/'+source.relative_to(notices).as_posix()]=source.read_text(encoding='utf-8-sig')
    for name, text in files.items():
        target=game/name
        target.parent.mkdir(parents=True,exist_ok=True)
        temporary=target.with_name(target.name+'.new')
        with temporary.open('x',encoding='utf-8',newline='\n') as output:
            output.write(text)
        os.replace(temporary,target)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('game',type=Path)
    parser.add_argument('commit')
    parser.add_argument('--label',default='Playtest 1')
    args=parser.parse_args()
    write(args.game,args.commit,args.label)
