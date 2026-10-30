"""Fail a package if any >=2K source art image lacks its signed, full-resolution GPU cache.

--prepare writes native build requests and succeeds before the editor fills missing entries.
HDR panoramas use BC6H so their range survives; ordinary art uses BC7. No downscale is permitted.

Every source the presentation can require must be listed, or the gate passes a package that
throws `Packaged texture >=2K is uncompressed` at boot (B-002: the HumGen hair-flow LODs live
directly under rigged/<asset>/, not in quality_4k/, and were invisible to this check).
"""
import argparse, hashlib, io, json, re, struct
from pathlib import Path
from PIL import Image

# Maps the presentation resolves beside a rigged asset without a contract file naming them:
# PeopleAppearance's hair-flow LODs and Kal stone maps, the Kal skin/wing maps, and the creator
# part's painted body coverage.
BESIDE_ASSET = ('body_coverage.png', 'body_skin_colour.png', 'siann_skin.png', 'kal_stone_rgbh.png',
                'kal_stone_rm.png', 'hair_flow.png', 'hair_flow1.png', 'hair_flow2.png', 'hair_flow3.png')
# Per-asset contracts whose string values name loose image files: HumGenMaterials' native maps,
# the creator material sets, and the close-up map manifest.
CONTRACTS = ('native_materials.json', 'materials.json', 'textures.json')
LOOSE_FORMATS = ('png', 'jpg', 'jpeg', 'webp', 'hdr')


def signature(file):
    with file.open('rb') as f:
        length = file.stat().st_size
        data = f.read(min(65536, length))
        if length > 65536:
            f.seek(-min(65536, length-65536), 2)
            data += f.read()
    return hashlib.sha256(data).hexdigest()[:24].upper()


def add(records, root, file, key, raw, offset, fmt, role, required=True):
    if fmt == 'hdr':
        h, w = re.search(rb'-Y\s+(\d+)\s+\+X\s+(\d+)', raw[:8192]).groups()
        size = int(w), int(h)
    elif raw.startswith(b'\x89PNG\r\n\x1a\n'):
        # Dimensions do not require inflating large ancillary EXIF profiles in native HumGen PNGs.
        size = struct.unpack_from('>II', raw, 16)
    else:
        try:
            with Image.open(io.BytesIO(raw)) as image:
                size = image.size
        except Exception as error:
            raise ValueError((str(file), key, fmt, offset, len(raw), raw[:16])) from error
    records.append(dict(root=str(root), file=str(file), key=key, offset=offset, length=len(raw),
                        format=fmt, role=role, width=size[0], height=size[1], required=required))


def role_of(name, fmt=None):
    """How the presentation treats a map, which only decides BC6H (HDR) against BC7 for this gate."""
    fmt = fmt or name.rsplit('.', 1)[-1]
    if fmt == 'hdr':
        return 'hdr'
    role = 'normal' if any(n in name for n in ('normal', '_nor_', '_nor-', '_nrm')) else \
        'data' if any(n in name for n in ('rough', 'metal', 'occlusion', '_orm', 'height', '_ao', 'flow')) else 'color'
    return 'color' if 'albedo_height' in name else role


def add_loose(records, root, file, role=None, required=True):
    fmt = file.suffix.lower().lstrip('.').replace('jpeg', 'jpg')
    add(records, root, file, file.relative_to(root).as_posix(), file.read_bytes(), 0, fmt,
        role or role_of(file.stem.lower(), fmt), required)


def named(contract):
    """Every string in a contract, which is how the presentation names a map file relative to it."""
    try:
        data = json.loads(contract.read_text(encoding='utf-8-sig'))
    except Exception as error:
        raise ValueError((str(contract), 'unreadable asset contract', str(error))) from error
    stack = [data]
    while stack:
        node = stack.pop()
        if isinstance(node, str):
            yield node
        elif isinstance(node, dict):
            stack.extend(node.values())
        elif isinstance(node, list):
            stack.extend(node)


def collect(root, records):
    for file in sorted(root.rglob('*.glb')):
        rel = file.relative_to(root).as_posix()
        if 'barrowworks_native/' in rel:
            continue
        with file.open('rb') as f:
            magic, version, total = struct.unpack('<III', f.read(12))
            assert magic == 0x46546c67 and version == 2, file
            length, kind = struct.unpack('<II', f.read(8))
            doc = json.loads(f.read(length))
            if not doc.get('images'):
                continue
            bin_length, bin_kind = struct.unpack('<II', f.read(8))
            bin_offset = f.tell()
            roles = {}
            def textures(value, cutout=False):
                if not isinstance(value, dict):
                    return
                for key, child in value.items():
                    if key.endswith('Texture') and isinstance(child, dict) and 'index' in child:
                        texture = doc['textures'][child['index']]
                        source = texture.get('source')
                        if source is None:
                            extensions = texture.get('extensions', {})
                            for extension in ('EXT_texture_webp', 'KHR_texture_basisu'):
                                if extension in extensions:
                                    source = extensions[extension].get('source')
                                    break
                        if source is not None:
                            role = 'normal' if 'normal' in key.lower() else 'color' if any(x in key.lower() for x in ('color', 'emissive')) else 'data'
                            roles[source] = 'cutout' if key == 'baseColorTexture' and cutout else role
                    textures(child, cutout)
            for material in doc.get('materials', []):
                textures(material, material.get('alphaMode') == 'MASK')
            for index, image in enumerate(doc['images']):
                if index not in roles:
                    continue
                if 'bufferView' in image:
                    view = doc['bufferViews'][image['bufferView']]
                    offset = bin_offset + view.get('byteOffset', 0)
                    f.seek(offset)
                    raw = f.read(view['byteLength'])
                    fmt = image.get('mimeType', 'image/png').split('/')[-1].replace('jpeg', 'jpg')
                    add(records, root, file, rel+'#'+str(index), raw, offset, fmt, roles[index])
                else:
                    raise ValueError('External/data-URI image needs an explicit cache route: '+str(file))
    for file in sorted(root.glob('rigged/*/quality_4k/*.png')):
        add_loose(records, root, file)
    for folder in ('materials', 'sky', 'ui', 'vfx'):
        for file in sorted((root/folder).rglob('*')):
            if not file.is_file() or file.suffix.lower().lstrip('.') not in ('png', 'jpg', 'webp', 'hdr'):
                continue
            add_loose(records, root, file)
    # Everything else the rigged assets carry as a loose map: the contract-referenced files and
    # the names resolved beside an asset. A map below 2K is only required when the cache has it -
    # TextureCache.RequireCompressed has a 2K floor, so a small map never fails a boot.
    rigged = [p for p in (root/'rigged').rglob('*') if p.is_file()]
    for contract in sorted(p for p in rigged if p.name in CONTRACTS):
        for name in named(contract):
            file = contract.parent/name
            if name.lower().endswith(LOOSE_FORMATS) and file.is_file():
                add_loose(records, root, file, required=False)
    for asset in sorted(p for p in (root/'rigged').glob('*') if p.is_dir()):
        for name in BESIDE_ASSET:
            file = asset/name
            if file.is_file():
                add_loose(records, root, file, required=False)
    unique, keys = [], set()
    for record in records:
        if record['key'] not in keys:
            keys.add(record['key'])
            unique.append(record)
    return unique


def check(game, receipt, prepare=False):
    roots = [r for r in (game/'primary', game/'fallback') if r.is_dir()]
    if not roots:
        raise SystemExit(f'no asset workspace under {game}; a package without art cannot pass')
    records, failures = [], []
    for root in roots:
        records += collect(root, [])
    manifests, signatures = {}, {}
    for record in records:
        root, file = Path(record['root']), Path(record['file'])
        if record.get('required') is False and max(record['width'], record['height']) < 2048:
            continue
        if root not in manifests:
            path = root/'texture_cache/manifest.json'
            manifests[root] = json.loads(path.read_text(encoding='utf-8-sig'))['entries'] if path.exists() else {}
        if file not in signatures:
            signatures[file] = signature(file)
        entry = manifests[root].get(record['key'])
        reason = None
        if not entry or entry.get('size') != file.stat().st_size or entry.get('sig') != signatures[file]:
            reason = 'missing/stale source signature'
        else:
            dds = root/'texture_cache'/entry['dds']
            if not dds.is_file():
                reason = 'missing DDS'
            else:
                with dds.open('rb') as f:
                    header = f.read(148)
                if header[:4] != b'DDS ' or header[84:88] != b'DX10':
                    reason = 'not DX10 block compression'
                elif struct.unpack_from('<I', header, 128)[0] not in ((95, 96) if record['role'] == 'hdr' else (98, 99)):
                    reason = 'not BC6H HDR / BC7 ordinary art'
                else:
                    height, width = struct.unpack_from('<II', header, 12)
                    # Godot aligns non-multiple-of-four images upward for BC blocks (e.g. 2050 -> 2052).
                    if not (record['width'] <= width <= (record['width']+3)//4*4
                            and record['height'] <= height <= (record['height']+3)//4*4):
                        reason = 'source resolution reduced or unexpectedly resized'
                    record['cached_width'], record['cached_height'] = width, height
        if reason:
            failures.append(dict(key=record['key'], root=record['root'], reason=reason))
    receipt.parent.mkdir(parents=True, exist_ok=True)
    receipt.write_text(json.dumps(dict(status='PASS' if not failures else 'BUILD_REQUIRED', images=records,
                                        failures=failures, source_resolution_preserved=not failures), indent=2), encoding='utf-8')
    print(json.dumps(dict(image_references=len(records), large_images=sum(max(r['width'], r['height']) >= 2048 for r in records),
                          failures=len(failures), receipt=str(receipt))))
    return 0 if not failures or prepare else 1


def main(argv):
    p = argparse.ArgumentParser()
    p.add_argument('game', type=Path)
    p.add_argument('receipt', type=Path)
    p.add_argument('--prepare', action='store_true')
    a = p.parse_args(argv)
    return check(a.game, a.receipt, a.prepare)


if __name__ == '__main__':
    raise SystemExit(main(None))
