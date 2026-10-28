"""Export native modular grooms/wardrobe on the two creator skeletons. CPU only.

HumGen processes its own highest-quality LOD and materials. Export selection
contains only the requested parts and their exact base rig, never another body.
Usage in Blender: -- <creator-work> <catalogue> [<option-id> ...]
"""
import json
import re
import sys
from pathlib import Path
import bpy
import numpy as np
from HumGen3D.human.human import Human
from HumGen3D.human.process.settings import ExportSettings, QualitySettings, ScriptSettings
from HumGen3D.human.process.shape_keys import livekey_group
from HumGen3D.human.process import textures
from HumGen3D.human.process.naming import Namer
from HumGen3D.common.progress import run
from HumGen3D.backend.preferences.preference_func import get_prefs

args = sys.argv[sys.argv.index('--') + 1:]
work = Path(args[0])
catalogue = json.loads(Path(args[1]).read_text(encoding='utf-8'))
only = set(args[2:])
tools = Path(__file__).resolve().parents[1] / 'animation'
sys.path.insert(0, str(tools))
from audit_humgen_transport import Glb
parts = [('hair', 'hair', 'hg_main_hair'), ('facial_hair', 'beard', 'hg_face_hair'),
         ('outfits', 'outfit', None), ('footwear', 'footwear', None)]
def slug(value): return re.sub(r'[^a-z0-9]+', '_', value.lower()).strip('_')

for collection, kind, tag in parts:
    for option in catalogue[collection]:
        if only and option['id'] not in only: continue
        for sex, path in option['paths'].items():
            identifier = 'humgen_creator_' + sex + '_' + kind + '_' + slug(option['id'])
            folder = work / 'game/primary/rigged' / identifier
            source_scene = work / 'parts_sources' / identifier / 'native.blend'
            if (folder / 'creator_part.json').is_file() and source_scene.is_file():
                print('NATIVE_PART_PRESENT', identifier, flush=True)
                continue
            bpy.ops.wm.read_homefile(use_empty=True)
            bpy.context.scene.render.threads_mode = 'FIXED'
            bpy.context.scene.render.threads = 8
            native_sex = 'female' if sex == 'f' else 'male'
            base = 'humgen_creator_' + native_sex
            preset = 'models/' + native_sex + ('/Caucasian presets/' if sex == 'f' else '/Caucasian Presets/') + 'Caucasian 2.json'
            human = Human.from_preset(str(Path(preset)))
            human.name = identifier
            setting = human.hair.regular_hair if kind == 'hair' else human.hair.face_hair if kind == 'beard' else human.clothing.outfit if kind == 'outfit' else human.clothing.footwear
            setting.set(path)
            settings = ExportSettings.from_recipe('godot')
            settings.output.format = 'in_file'
            settings.output.name = identifier
            # Bake only the selected native parts after processing. The temporary
            # body is neither textured nor exported.
            settings.textures.enabled = False
            settings.textures.resolution.update(body=4096, eyes=2048, hair=2048, clothing=4096, teeth=2048)
            settings.textures.haircap_in_skin = False
            settings.shape_keys.body = settings.shape_keys.face = settings.shape_keys.age = 'keep'
            settings.shape_keys.lod0_only = False
            valid = [key for key in human.keys.all_livekeys if livekey_group(key) == 'body'
                     and Path(get_prefs().filepath, key.path).stat().st_size]
            settings.shape_keys.keep['body'] = [name for key in valid for name in (key.name,
                f'{key.category[0]}_{{{key.subcategory}}}_{key.name}' if key.subcategory else f'{key.category[0]}_{key.name}')]
            settings.lods = [QualitySettings(body='original', eyes='original', teeth='original', haircards='ultra'),
                             QualitySettings(body='lower_face', eyes='medium', teeth='medium', haircards='medium'),
                             QualitySettings(body='quarter', eyes='low', teeth='low', haircards='low'),
                             QualitySettings(body='quarter', eyes='low', teeth='low', haircards='haircap_only')]
            settings.scripts.enabled = True
            settings.scripts.items = [ScriptSettings(path=str(tools / 'keep_humgen_creator_groom_keys.py'), stage='before_meshes')]
            print('NATIVE_PART_START', identifier, flush=True)
            result = human.process.run(settings)
            copies = result.humans
            assert len(copies) == 4
            # Keep the original native material graph and generated atlas for
            # colour controls. This is private build input, outside the runtime
            # payload, and avoids recolouring a baked black strand map.
            source_scene.parent.mkdir(parents=True, exist_ok=True)
            for image in bpy.data.images:
                if image.source == 'GENERATED' and image.has_data: image.pack()
            bpy.ops.wm.save_as_mainfile(filepath=str(source_scene), compress=True)
            folder.mkdir(parents=True, exist_ok=True)
            flow = {}
            for level, copy in enumerate(copies):
                copy.location = (0, 0, 0)
                namer = Namer(settings.output, identifier, level, 4)
                # Native material baking, including source alpha/normal/roughness
                # and directional flow. No generic hair/cloth replacement shader.
                selected = ('hair',) if tag else ('clothing',)
                if not tag and level > 0:
                    copy.process.share_textures(copies[0])
                    continue
                # Wardrobe textures end up embedded in the glb; their doubled-up names would pass Windows' path limit in the part's own folder.
                baked = run(textures.bake_steps(copy, settings.textures, namer, str(folder if tag else Path(work.anchor) / 'hg_tex'), bpy.context, only_sets=selected, per_level=level > 0))
                for image in baked:
                    if '_Flow' in image.name:
                        flow['hair_flow' + ('_lod' + str(level) if level else '')] = Path(bpy.path.abspath(image.filepath)).name
            copies[0].process.merge_levels(copies[1:])
            copy = copies[0]
            meshes = list(copy.objects.haircards) if tag else list(copy.clothing.outfit.objects if kind == 'outfit' else copy.clothing.footwear.objects)
            if tag: meshes = [obj for obj in meshes if tag in obj]
            assert meshes, 'Native part selection is empty: ' + identifier
            bpy.ops.object.select_all(action='DESELECT')
            copy.objects.rig.select_set(True)
            for obj in meshes: obj.select_set(True)
            bpy.context.view_layer.objects.active = copy.objects.rig
            destination = folder / (identifier + '_rigged.glb')
            bpy.ops.export_scene.gltf(filepath=str(destination), export_format='GLB', use_selection=True,
                                      export_animations=False, export_morph=True, export_skins=True, export_yup=True)
            target = Glb(work / 'game/primary/rigged' / base / (base + '_rigged.glb'))
            exported = Glb(destination)
            assert not any('_Body_LOD' in node.get('name', '') and 'mesh' in node for node in exported.nodes), 'Part export contains another body'
            assert target.names.keys() == exported.names.keys(), 'Part rig vocabulary differs from the base'
            a, b = target.globals(), exported.globals()
            assert max(float(np.max(np.abs(a[name] - b[name]))) for name in a) < .0001, 'Part rest differs from the exact creator base'
            (folder / 'native_materials.json').write_text(json.dumps(flow, indent=2))
            identity = json.loads((work / 'game/primary/rigged' / base / 'identity_weights.json').read_text())
            (folder / 'creator_part.json').write_text(json.dumps(dict(kind=kind, option=option['id'], target_base=base,
                target_body_sha256=identity['target_body_sha256'], source=path, native_meshes=[obj.name for obj in meshes]), indent=2))
            print('NATIVE_PART_DONE', identifier, len(meshes), flush=True)
print('NATIVE_PARTS_COMPLETE', flush=True)
