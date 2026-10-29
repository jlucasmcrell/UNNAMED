"""Stage owned native humanoid sources from archives into the isolated motion project."""
from pathlib import Path
from zipfile import ZipFile
import argparse
import json

parser = argparse.ArgumentParser()
parser.add_argument('job', type=Path)
args = parser.parse_args()
archive = Path('D:/Otherreach_Depot/Otherreach_External_Assets/Cosmos_Premium/Purchased/Tools/Hyper Farming System v4 (GBH_FarmingSystem).zip')
root = (args.job / 'project').resolve()
with ZipFile(archive) as source:
    entries = [entry for entry in source.infolist() if entry.filename.startswith('Content/Hyper/Locomotion/Chars/')]
    assert entries
    for entry in entries:
        target = (root / entry.filename).resolve()
        assert target.is_relative_to(root), entry.filename
        if entry.is_dir():
            target.mkdir(parents=True, exist_ok=True)
        elif not target.exists():
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(source.read(entry))
(args.job / 'catalog_archive_source.json').write_text(json.dumps(dict(archive=str(archive),
    subtree='Content/Hyper/Locomotion/Chars/', files=len(entries), bytes=sum(entry.file_size for entry in entries)), indent=2))
print('Owned humanoid archive sources staged:', len(entries))
