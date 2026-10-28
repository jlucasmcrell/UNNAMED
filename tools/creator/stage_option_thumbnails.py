"""Stage HumGen's own option previews beside the exported parts.

The creator's pickers show a tile per choice (the owner's ruling, 2026-10-28), so every modular option needs its native
preview in the runtime art root. HumGen ships one .jpg next to every option's .json or .blend; this copies it to
rigged/<part>/thumbnail.jpg, where the same lookup as the face presets finds it. Idempotent: nothing is overwritten when
the file is already there, so it can run after any export batch.
"""
import json
import shutil
import sys
from pathlib import Path

CONTENT = Path('D:/HumGen Content')
work = Path(sys.argv[1] if len(sys.argv) > 1 else r'D:\Otherreach_Scratch\codex_f1_siann_wt\work\creator')
catalogue = json.loads(Path(sys.argv[2] if len(sys.argv) > 2 else
                           r'D:\Otherreach_Scratch\codex_f1_siann_wt\src\Presentation\Art\humgen_creator.json').read_text(encoding='utf-8'))
rigged = work / 'game/primary/rigged'

KINDS = (('hair', 'hair'), ('facial_hair', 'beard'), ('outfits', 'outfit'), ('footwear', 'footwear'))
copied, present, missing = 0, 0, []
for collection, kind in KINDS:
    for option in catalogue.get(collection) or []:
        for sex, source in option['paths'].items():
            part = 'humgen_creator_' + sex + '_' + kind + '_' + ''.join(
                c if c.isalnum() else '_' for c in option['id'].lower()).strip('_')
            target = rigged / part / 'thumbnail.jpg'
            if not (rigged / part / 'creator_part.json').is_file():
                continue
            if target.is_file():
                present += 1
                continue
            preview = (CONTENT / Path(source)).with_suffix('.jpg')
            if not preview.is_file():
                missing.append((option['id'], sex, str(preview)))
                continue
            shutil.copy2(preview, target)
            copied += 1
print(f'option previews: {copied} copied, {present} already staged, {len(missing)} without a native preview')
for entry in missing[:10]:
    print('  no preview:', entry)
