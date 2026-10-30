"""Point the two capture scripts at the art root the other lanes use, where the library actually indexes (creatures were greybox).

The tracking capture's log said "0 assets drawn from the library at boot", and every person and animal came out as a greybox stand-in - the flat
white shapes in the frames. The other lanes' bright runs log 495 assets drawn from D:\\Otherreach_Shared\\art_root.
"""
from pathlib import Path

OLD = "'--asset-root', 'D:\\Otherreach_Scratch\\art_merged'"
NEW = "'--asset-root', 'D:\\Otherreach_Shared\\art_root'"

for name in ('capture_tracking.ps1', 'capture_fishing.ps1'):
    path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\work') / name
    text = path.read_text(encoding='utf-8')
    if OLD not in text:
        print(name, 'unchanged (anchor not found)')
        continue
    path.write_text(text.replace(OLD, NEW), encoding='utf-8')
    print(name, 'now reads the shared art root')
