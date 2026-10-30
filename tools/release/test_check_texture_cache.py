import json, struct, tempfile
from pathlib import Path
from PIL import Image
from check_texture_cache import check, signature


def picture(path, side):
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.new('RGBA', (side, side), (20, 30, 40, 255)).save(path)
    return path


def entry_for(root, key, source):
    dds = root / 'texture_cache' / 'aaaa.data.dds'
    dds.parent.mkdir(parents=True, exist_ok=True)
    header = bytearray(148)
    header[0:4] = b'DDS '
    struct.pack_into('<I', header, 12, 2048)
    struct.pack_into('<I', header, 16, 2048)
    header[84:88] = b'DX10'
    struct.pack_into('<I', header, 128, 98)  # BC7_UNORM
    dds.write_bytes(bytes(header))
    manifest = root / 'texture_cache' / 'manifest.json'
    entries = json.loads(manifest.read_text())['entries'] if manifest.exists() else {}
    entries[key] = dict(dds=dds.name, size=source.stat().st_size, sig=signature(source), role='data')
    manifest.write_text(json.dumps(dict(entries=entries)))


with tempfile.TemporaryDirectory() as directory:
    game = Path(directory) / 'game'
    primary = game / 'primary'
    hair = primary / 'rigged' / 'humgen_creator_m_hair_hair_long_medium_center_part'
    flow = picture(hair / 'humgen_creator_m_hair_hair_long_medium_center_part_HairCards_Flow_LOD1.png', 2048)
    (hair / 'native_materials.json').write_text(json.dumps(dict(hair_flow_lod1=flow.name, skin_pbr='skin.png')))

    receipt = Path(directory) / 'audit.json'
    assert check(game, receipt) == 1, 'a 2K map the native materials name must be checked (B-002)'
    failures = json.loads(receipt.read_text())['failures']
    assert [f['key'] for f in failures] == ['rigged/humgen_creator_m_hair_hair_long_medium_center_part/' + flow.name], failures

    entry_for(primary, f'rigged/{hair.name}/{flow.name}', flow)
    assert check(game, receipt) == 0, 'a signed BC7 entry clears the map'
    assert json.loads(receipt.read_text())['status'] == 'PASS'

    # A map under 2K never fails a boot (RequireCompressed's floor), so it never fails the gate.
    small = picture(hair / 'thumbnail.jpg'.replace('.jpg', '.png'), 512)
    assert check(game, receipt) == 0, 'a small loose map is not required to be cached'

    # The presentation also resolves fixed names beside an asset, with no contract naming them.
    beside = picture(hair / 'hair_flow1.png', 2048)
    assert check(game, receipt) == 1, 'a fixed-name map beside an asset is checked too'
    entry_for(primary, f'rigged/{hair.name}/{beside.name}', beside)
    assert check(game, receipt) == 0, 'both loose maps are signed now'

    try:
        check(Path(directory) / 'no-art-here', receipt)
        raise AssertionError('a package without an art workspace must not pass')
    except SystemExit:
        pass
print('TEXTURE_CACHE_GATE_CHECK_PASS')
