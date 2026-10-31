"""Run in the acquired FAL project with NullRHI; migrate native dependencies to an isolated visual job."""
import hashlib
import json
import os
from pathlib import Path
import traceback
import unreal

root = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
source = Path('C:/Otherreach_External_Assets/Fab/FabAssets/Content')
destination = root / 'project/Content'
receipt = root / 'fal_migration.json'
state = json.loads(receipt.read_text(encoding='utf-8-sig'))
packages = ['/Game/FreeAnimationLibrary/Animations/UnarmedProne/' + name
            for name in ('anim_Stand_To_Prone', 'anim_Prone_Idle', 'anim_Prone_To_Stand')]
try:
    assert destination.resolve() != source.resolve()
    hashes = {p: hashlib.sha256((source / (p.removeprefix('/Game/') + '.uasset')).read_bytes()).hexdigest()
              for p in packages}
    options = unreal.MigrationOptions()
    options.prompt = False
    options.ignore_dependencies = False
    options.asset_conflict = unreal.AssetMigrationConflict.SKIP
    if not all((destination / (p.removeprefix('/Game/') + '.uasset')).exists() for p in packages):
        unreal.AssetToolsHelpers.get_asset_tools().migrate_packages(packages, str(destination), options)
    for package, digest in hashes.items():
        relative = package.removeprefix('/Game/') + '.uasset'
        assert hashlib.sha256((source / relative).read_bytes()).hexdigest() == digest
        assert (destination / relative).is_file()
        # Native migration may serialize the destination for this engine version. The acquired source stays byte-identical.
        state.setdefault('migrated_sha256', {})[package] = hashlib.sha256((destination / relative).read_bytes()).hexdigest()
    state['packages'] = list(dict.fromkeys(state['packages'] + packages))
    state.setdefault('original_sha256', {}).update(hashes)
    state.pop('error', None)
    state['status'] = 'DONE'
except Exception:
    state.update(status='FAILED', error=traceback.format_exc())
finally:
    receipt.write_text(json.dumps(state, indent=2), encoding='utf-8')
    unreal.SystemLibrary.quit_editor()
