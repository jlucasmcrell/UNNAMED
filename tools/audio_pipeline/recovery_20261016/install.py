"""Install the recovery build into an asset root (a playtest's primary/): copy the files under audio/, and in its
manifests/playable_prototype_audio_v3.json replace every rebuilt family whole (old variations dropped) and add the new ones. The manifest
is backed up beside itself first; the line endings and indent are kept.
usage: python -P tools/install.py <recovery root> <asset root>"""
import json, os, sys, shutil, hashlib, time

root, asset = sys.argv[1], sys.argv[2]
rows = []
for name in ('combat_rows.json', 'gap_rows.json', 'music_rows.json', 'opening_rows.json'):
    p = os.path.join(root, 'out', name)
    if os.path.exists(p):
        rows += json.load(open(p, encoding='utf-8'))['sounds']
fam = lambda aid: aid.rsplit('.', 1)[0] if aid.rsplit('.', 1)[-1].isdigit() else aid
families = {fam(r['audio_id']) for r in rows}
mp = os.path.join(asset, 'manifests', 'playable_prototype_audio_v3.json')
raw = open(mp, 'rb').read()
crlf = b'\r\n' in raw
text = raw.decode('utf-8').replace('\r\n', '\n')
indent = len(text.split('\n')[1]) - len(text.split('\n')[1].lstrip(' '))
m = json.loads(text)
shutil.copyfile(mp, mp + f'.before_audio_recovery_{time.strftime("%Y%m%d_%H%M%S")}')
kept = [s for s in m['sounds'] if fam(s['audio_id']) not in families]
dropped = len(m['sounds']) - len(kept)
for r in rows:
    src = os.path.join(root, 'out', *r['delivered'].split('/'))
    dst = os.path.join(asset, *r['delivered'].split('/'))
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    # A new package may hard-link unchanged audio from the previous RC. Replace its
    # directory entry rather than overwriting those shared bytes.
    if not os.path.exists(dst) or hashlib.sha256(open(dst, 'rb').read()).hexdigest() != r['sha256']:
        shutil.copyfile(src, dst + '.installing')
        os.replace(dst + '.installing', dst)
    assert hashlib.sha256(open(dst, 'rb').read()).hexdigest() == r['sha256'], r['audio_id']
m['sounds'] = kept + rows
m.setdefault('audio_recovery_20261016', {})
m['audio_recovery_20261016'] = {'families_replaced_or_added': sorted(families), 'rows_dropped': dropped, 'rows_added': len(rows),
                                'build': 'C:/Otherreach_Recovery/asset_work/audio_recovery', 'credits': 'audio/CREDITS_audio_recovery.txt'}
out = json.dumps(m, indent=indent, ensure_ascii=False) + ('\n' if text.endswith('\n') else '')
with open(mp + '.installing', 'wb') as f:
    f.write((out.replace('\n', '\r\n') if crlf else out).encode('utf-8'))
os.replace(mp + '.installing', mp)
cred = os.path.join(root, 'out', 'CREDITS_audio_recovery.txt')
if os.path.exists(cred):
    target = os.path.join(asset, 'audio', 'CREDITS_audio_recovery.txt')
    incoming = open(cred, encoding='utf-8-sig').read()
    existing = open(target, encoding='utf-8-sig').read() if os.path.exists(target) else ''
    combined = existing if incoming in existing else incoming if existing in incoming else existing.rstrip() + '\n\n' + incoming
    with open(target + '.installing', 'w', encoding='utf-8', newline='\n') as f:
        f.write(combined)
    os.replace(target + '.installing', target)
print(f'{len(families)} families: {dropped} rows dropped, {len(rows)} added; manifest now {len(m["sounds"])} sounds')
