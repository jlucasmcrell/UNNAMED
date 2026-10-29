"""Fail a package if any >=2K source art image lacks its signed, full-resolution GPU cache.

--prepare writes native build requests and succeeds before the editor fills missing entries.
HDR panoramas use BC6H so their range survives; ordinary art uses BC7. No downscale is permitted.
"""
import argparse, hashlib, io, json, re, struct
from pathlib import Path
from PIL import Image

p = argparse.ArgumentParser()
p.add_argument('game', type=Path)
p.add_argument('receipt', type=Path)
p.add_argument('--prepare', action='store_true')
a = p.parse_args()
records, failures = [], []

def signature(file):
    with file.open('rb') as f:
        length = file.stat().st_size
        data = f.read(min(65536, length))
        if length > 65536:
            f.seek(-min(65536, length-65536), 2)
            data += f.read()
    return hashlib.sha256(data).hexdigest()[:24].upper()

def add(root, file, key, raw, offset, fmt, role):
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
                        format=fmt, role=role, width=size[0], height=size[1]))

for root in (a.game/'primary', a.game/'fallback'):
    root = root.resolve(strict=True)
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
                    add(root, file, rel+'#'+str(index), raw, offset, fmt, roles[index])
                else:
                    raise ValueError('External/data-URI image needs an explicit cache route: '+str(file))
    for file in sorted(root.glob('rigged/*/quality_4k/*.png')):
        role = 'normal' if file.stem.endswith('_normal') else 'data' if file.stem.endswith('_orm') else 'color'
        add(root, file, file.relative_to(root).as_posix(), file.read_bytes(), 0, 'png', role)
    # HumGen's shader sidecars carry maps which glTF cannot name, including
    # anisotropy flow for every hair LOD. They use the same signed native cache.
    for metadata in sorted(root.glob('rigged/humgen_*/native_materials.json')):
        for name, value in json.loads(metadata.read_text()).items():
            if name not in ('skin_pbr', 'cornea_normal', 'eye_scatter') and not name.startswith('hair_flow'):
                continue
            file = metadata.parent / value
            role = 'normal' if name == 'cornea_normal' else 'data'
            add(root, file, file.relative_to(root).as_posix(), file.read_bytes(), 0, file.suffix.lstrip('.'), role)
    for folder in ('materials', 'sky', 'ui', 'vfx'):
        for file in sorted((root/folder).rglob('*')):
            fmt = file.suffix.lower().lstrip('.').replace('jpeg', 'jpg')
            if not file.is_file() or fmt not in ('png', 'jpg', 'webp', 'hdr'):
                continue
            name = file.stem.lower()
            role = 'hdr' if fmt == 'hdr' else 'normal' if any(n in name for n in ('normal', '_nor_', '_nor-', '_nrm')) else 'data' if any(n in name for n in ('rough', 'metal', 'occlusion', '_orm', 'height', '_ao')) else 'color'
            if 'albedo_height' in name:
                role = 'color'
            add(root, file, file.relative_to(root).as_posix(), file.read_bytes(), 0, fmt, role)

manifests, signatures = {}, {}
for record in records:
    root, file = Path(record['root']), Path(record['file'])
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
a.receipt.parent.mkdir(parents=True, exist_ok=True)
a.receipt.write_text(json.dumps(dict(status='PASS' if not failures else 'BUILD_REQUIRED', images=records,
                                    failures=failures, source_resolution_preserved=not failures), indent=2), encoding='utf-8')
print(json.dumps(dict(image_references=len(records), large_images=sum(max(r['width'],r['height']) >= 2048 for r in records), failures=len(failures), receipt=str(a.receipt))))
if failures and not a.prepare:
    raise SystemExit(1)
