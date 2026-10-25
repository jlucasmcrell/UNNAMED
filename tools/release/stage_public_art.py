"""Stage unchanged art in a fresh folder, omitting manifest-orphan DDS and known developer files."""
import argparse
import json
import os
from pathlib import Path
import shutil

IMMUTABLE={'.dds','.glb','.res','.png','.jpg','.jpeg','.webp','.exr','.hdr','.wav','.ogg'}


def developer_file(relative):
    return (any(part=='barrowworks_native' or part=='_rejected' or part.startswith('_before_') for part in relative.parts)
        or relative.suffix.lower() in {'.log','.stderr','.pdb','.db'}
        or relative.name.endswith(('.process.json','.log.stderr'))
        or any(mark in relative.name.lower() for mark in ('.after_sheen','.opaque_test_bak','.premask','.hairbak','.skinbak','.flathair_bak','.preflat_bak','.nohairnormal_bak')))


def stage(source, destination=None):
    if destination is not None:
        assert not destination.exists(), 'Fresh destination only; retained builds are never pruned'
        destination.mkdir(parents=True)
    report=dict(linked=0,copied=0,kept_bytes=0,omitted_orphan_dds_bytes=0,omitted_developer_bytes=0)
    for scope in ('primary','fallback','humgen_characters','THIRD_PARTY_NOTICES'):
        base=source/scope
        assert base.is_dir(),base
        manifest=base/'texture_cache/manifest.json'
        cache=None
        if manifest.is_file():
            cache=json.loads(manifest.read_text(encoding='utf-8-sig'))
            cache['entries']={key:entry for key,entry in cache['entries'].items()
                if (base/key.split('#')[0]).is_file() and not developer_file(Path(key.split('#')[0]))}
            used={entry['dds'] for entry in cache['entries'].values()}
            for name in used:
                assert Path(name).name==name and (manifest.parent/name).is_file(), name
        else:
            used=set()
        for item in base.rglob('*'):
            if not item.is_file():continue
            relative=item.relative_to(base)
            size=item.stat().st_size
            if developer_file(relative):
                report['omitted_developer_bytes']+=size;continue
            if cache is not None and item.parent==manifest.parent and item.suffix.lower()=='.dds' and item.name not in used:
                report['omitted_orphan_dds_bytes']+=size;continue
            report['kept_bytes']+=size
            if destination is None:continue
            target=destination/scope/relative
            target.parent.mkdir(parents=True,exist_ok=True)
            if item==manifest:
                target.write_text(json.dumps(cache,indent=2)+'\n');report['copied']+=1
            elif item.suffix.lower() in IMMUTABLE:
                os.link(item,target);report['linked']+=1
            else:
                shutil.copy2(item,target);report['copied']+=1
    return report


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path)
    parser.add_argument('destination',type=Path,nargs='?')
    args=parser.parse_args()
    print(json.dumps(stage(args.source,args.destination),indent=2))
