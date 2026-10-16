"""Install the Fab motion-capture locomotion (locomotion_fab.py's staging) into an asset root (a release's primary/): copy every
clip GLB under animation/ready/ and record under animation/clips/, the files they replace backed up first to
animation/_before_locomotion_<time>/; with --bindings, merge the staging's bindings_patch.json into that bindings file (a
release's content/_art_bindings.json): the walk/run/sprint paces and the NPCs' idle/talk clips under
people_clips_by_visual_option - backed up beside itself, line endings and indent kept.
usage: python -P tools/asset_pipeline/locomotion_install.py <staging> <asset root> [--bindings <_art_bindings.json>]"""
import hashlib, json, os, shutil, sys, time

staging, asset = sys.argv[1], sys.argv[2]
bindings = sys.argv[sys.argv.index('--bindings') + 1] if '--bindings' in sys.argv else None
stamp = time.strftime('%Y%m%d_%H%M%S')
backup = os.path.join(asset, 'animation', f'_before_locomotion_{stamp}')
sha = lambda p: hashlib.sha256(open(p, 'rb').read()).hexdigest()
done = []
for sub in ('ready', 'clips'):
    base = os.path.join(staging, 'animation', sub)
    for dirpath, _dirs, files in os.walk(base):
        for name in files:
            src = os.path.join(dirpath, name)
            rel = os.path.relpath(src, os.path.join(staging, 'animation'))
            dst = os.path.join(asset, 'animation', rel)
            if os.path.exists(dst):
                os.makedirs(os.path.dirname(os.path.join(backup, rel)), exist_ok=True)
                shutil.copy2(dst, os.path.join(backup, rel))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copyfile(src, dst)
            assert sha(dst) == sha(src), rel
            done.append(rel.replace('\\', '/'))
print(f'{len(done)} files installed under {asset}\\animation; replaced files backed up to {backup}' if os.path.isdir(backup)
      else f'{len(done)} files installed under {asset}\\animation (nothing replaced)')

if bindings:
    patch = json.load(open(os.path.join(staging, 'bindings_patch.json'), encoding='utf-8'))
    raw = open(bindings, 'rb').read()
    crlf = b'\r\n' in raw
    text = raw.decode('utf-8').replace('\r\n', '\n')
    indent = len(text.split('\n')[1]) - len(text.split('\n')[1].lstrip(' '))
    b = json.loads(text)
    shutil.copyfile(bindings, bindings + f'.before_locomotion_{stamp}')
    set_ = 0
    for option, people in patch.items():
        for person, values in people.items():
            entry = b.setdefault('people_clips_by_visual_option', {}).setdefault(option, {}).setdefault(person, {})
            for key, value in values.items():
                entry.setdefault(key, {}).update(value)
            set_ += 1
    out = json.dumps(b, indent=indent, ensure_ascii=False) + ('\n' if text.endswith('\n') else '')
    open(bindings, 'wb').write((out.replace('\n', '\r\n') if crlf else out).encode('utf-8'))
    print(f'{set_} bindings entries patched (paces, NPC idle/talk) in {bindings}')
