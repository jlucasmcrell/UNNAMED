import json
from pathlib import Path
import tempfile
from stage_public_art import stage

with tempfile.TemporaryDirectory() as directory:
    root=Path(directory);source=root/'source';target=root/'target'
    for scope in ('primary','fallback','humgen_characters','THIRD_PARTY_NOTICES'):
        (source/scope).mkdir(parents=True)
    (source/'primary/model.glb').write_bytes(b'unchanged model')
    cache=source/'primary/texture_cache';cache.mkdir()
    (cache/'live.dds').write_bytes(b'live')
    (cache/'orphan.dds').write_bytes(b'orphan')
    (cache/'manifest.json').write_text(json.dumps(dict(entries={'model.glb#image0':dict(dds='live.dds'),
        'missing.glb#image0':dict(dds='orphan.dds')})))
    (source/'humgen_characters/library.log').write_text('developer log')
    (source/'humgen_characters/native.audit.json').write_text('{"runtime":"keep"}')
    (source/'humgen_characters/mocap_motion').mkdir()
    (source/'humgen_characters/mocap_motion/body_motion.res').write_bytes(b'exact native library')
    (source/'humgen_characters/facial_motion').mkdir()
    (source/'humgen_characters/facial_motion/expression.json').write_text('{"fps":100,"frames":[]}')
    result=stage(source,target)
    assert result['omitted_orphan_dds_bytes']==6
    assert (cache/'orphan.dds').read_bytes()==b'orphan'
    assert not (target/'primary/texture_cache/orphan.dds').exists()
    assert (target/'primary/model.glb').stat().st_ino==(source/'primary/model.glb').stat().st_ino
    assert not (target/'humgen_characters/library.log').exists()
    assert not (target/'humgen_characters/native.audit.json').exists()
    assert (source/'humgen_characters/native.audit.json').is_file()
    assert (target/'humgen_characters/mocap_motion/body_motion.res').read_bytes()==b'exact native library'
    assert (target/'humgen_characters/facial_motion/expression.json').read_bytes()==(source/'humgen_characters/facial_motion/expression.json').read_bytes()
    entries=json.loads((target/'primary/texture_cache/manifest.json').read_text())['entries']
    assert list(entries)==['model.glb#image0']
    try:
        stage(source,source/'nested')
    except AssertionError:
        pass
    else:
        raise AssertionError('Recursive staging destination was accepted')
    assert not (source/'nested').exists()
print('PASS fresh staging preserves originals/native libraries/live textures; receipts stay outside the public copy')
