"""Install the P2 spell SFX into an asset root: copy the files under audio/, replace the six spell sound families in
manifests/playable_prototype_audio_v3.json, and put the credits section into audio/CREDITS_audio_recovery.txt.

The manifest is backed up beside itself first; its line endings and indent are kept, the same way install.py does it. Only the families
this build owns are replaced, so nothing another lane installed is dropped.

usage: python -P tools/audio_pipeline/recovery_20261016/install_spells.py "<recovery root>" "<asset root>"
"""
import hashlib, json, os, shutil, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_spells import section  # the same credits lines the build wrote

root, asset = sys.argv[1], sys.argv[2]
rows = json.load(open(os.path.join(root, 'out', 'spell_rows.json'), encoding='utf-8'))['sounds']
families = {r['audio_id'].rsplit('.', 1)[0] for r in rows}

manifest = os.path.join(asset, 'manifests', 'playable_prototype_audio_v3.json')
raw = open(manifest, 'rb').read()
crlf = b'\r\n' in raw
text = raw.decode('utf-8').replace('\r\n', '\n')
indent = len(text.split('\n')[1]) - len(text.split('\n')[1].lstrip(' '))
document = json.loads(text)
shutil.copyfile(manifest, manifest + f'.before_spell_sfx_{time.strftime("%Y%m%d_%H%M%S")}')

for r in rows:
    src = os.path.join(root, 'out', *r['delivered'].split('/'))
    dst = os.path.join(asset, *r['delivered'].split('/'))
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copyfile(src, dst + '.installing')
    os.replace(dst + '.installing', dst)
    assert hashlib.sha256(open(dst, 'rb').read()).hexdigest() == r['sha256'], r['audio_id']

kept = [s for s in document['sounds'] if s['audio_id'].rsplit('.', 1)[0] not in families]
dropped = len(document['sounds']) - len(kept)
document['sounds'] = kept + rows
document.setdefault('spell_sfx_p2', {})
document['spell_sfx_p2'] = {'build': 'tools/audio_pipeline/recovery_20261016/build_spells.py', 'rows_added': len(rows),
                            'families_replaced_or_added': sorted(families)}
out = json.dumps(document, indent=indent, ensure_ascii=False) + ('\n' if text.endswith('\n') else '')
with open(manifest + '.installing', 'wb') as f:
    f.write((out.replace('\n', '\r\n') if crlf else out).encode('utf-8'))
os.replace(manifest + '.installing', manifest)

credits = os.path.join(asset, 'audio', 'CREDITS_audio_recovery.txt')
existing = open(credits, encoding='utf-8-sig').read() if os.path.exists(credits) else ''
kept_lines, dropping = [], False
for line in existing.split('\n'):
    if line.startswith('== Spell SFX'):
        dropping = True
        continue
    if dropping and line.startswith('== '):
        dropping = False
    if not dropping:
        kept_lines.append(line)
with open(credits, 'w', encoding='utf-8', newline='\n') as f:
    f.write('\n'.join(kept_lines).rstrip('\n') + '\n\n' + section(rows))
print(f'{len(families)} families installed: {dropped} rows dropped, {len(rows)} added; manifest now {len(document["sounds"])} sounds')
