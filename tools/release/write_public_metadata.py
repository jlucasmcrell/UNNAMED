"""Build identification and an offline itch launcher; replace links atomically."""
import argparse
import os
from pathlib import Path
import re


def write(game, commit, label='Playtest 1'):
    assert game.is_dir() and (game/'Otherreach.exe').is_file()
    assert re.fullmatch(r'[0-9a-fA-F]{40}', commit)
    assert label and '\n' not in label and '\r' not in label
    for name, text in {
        'VERSION.txt': f'build {label}\ncommit {commit}\n',
        '.itch.toml': '[[actions]]\nname = "Play Otherreach"\npath = "Otherreach.exe"\n',
    }.items():
        temporary=game/(name+'.new')
        with temporary.open('x',encoding='utf-8',newline='\n') as output:
            output.write(text)
        os.replace(temporary,game/name)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('game',type=Path)
    parser.add_argument('commit')
    parser.add_argument('--label',default='Playtest 1')
    args=parser.parse_args()
    write(args.game,args.commit,args.label)
