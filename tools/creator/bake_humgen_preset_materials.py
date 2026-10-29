"""Bake native materials for every preset, without exporting additional bodies.

Blender background CPU only. Uses HumGen's own full-resolution bake implementation.
Resumable per preset; a completed materials.json is written only after all maps exist.
"""
import json
import shutil
import sys
from pathlib import Path
import bpy
from HumGen3D.human.human import Human
from HumGen3D.human.process.settings import ExportSettings
from HumGen3D.human.process import textures
from HumGen3D.human.process.naming import Namer
from HumGen3D.common.progress import run

args = sys.argv[sys.argv.index('--') + 1:]
output = Path(args[0])
catalogue = json.loads(Path(args[1]).read_text(encoding='utf-8'))
only = args[2:]
for option in catalogue['faces']:
    identifier = option['id']
    if only and identifier not in only:
        continue
    folder = output / identifier
    receipt = folder / 'materials.json'
    if receipt.exists():
        print('PRESET_MATERIALS_PRESENT', identifier, flush=True)
        continue
    bpy.ops.wm.read_homefile(use_empty=True)
    bpy.context.scene.render.threads_mode = 'FIXED'
    bpy.context.scene.render.threads = 8
    human = Human.from_preset(next(iter(option['paths'].values())))
    human.name = identifier
    human.process.convert_to_haircards('ultra', bpy.context)
    settings = ExportSettings.from_recipe('godot')
    settings.textures.resolution.update(body=4096, eyes=2048, hair=2048, teeth=2048)
    settings.textures.haircap_in_skin = False
    sets = [s for s in textures.plan_texture_sets(human, settings.textures) if s.set_name in ('body', 'eyes', 'hair')]
    namer = Namer(settings.output, identifier)
    print('PRESET_MATERIALS_START', identifier, flush=True)
    images = run(textures.bake_steps(human, settings.textures, namer, str(folder), bpy.context, only_sets=('body', 'eyes', 'hair')))
    parts = {}
    for texture_set in sets:
        material = texture_set.material
        bsdf = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        maps = {}
        for image in images:
            # The native naming API owns filenames; no made-up substitute texture.
            prefix = namer.name + '_' + texture_set.part + '_'
            if image.name.startswith(prefix):
                path = Path(bpy.path.abspath(image.filepath))
                assert path.is_file(), path
                maps[image.name[len(prefix):]] = path.name
        assert maps, 'Native bake produced no maps for ' + texture_set.part
        parts[texture_set.part] = dict(maps=maps, roughness=bsdf.inputs['Roughness'].default_value,
                                      metallic=bsdf.inputs['Metallic'].default_value)
    folder.mkdir(parents=True, exist_ok=True)
    thumbnail = Path('D:/HumGen Content') / next(iter(option['paths'].values())).replace('\\', '/')
    if thumbnail.with_suffix('.jpg').is_file(): shutil.copy2(thumbnail.with_suffix('.jpg'), folder / 'thumbnail.jpg')
    receipt.write_text(json.dumps(dict(preset=identifier, parts=parts, source=option['native']), indent=2), encoding='utf-8')
    print('PRESET_MATERIALS_DONE', identifier, len(images), flush=True)
print('PRESET_MATERIALS_COMPLETE', flush=True)
