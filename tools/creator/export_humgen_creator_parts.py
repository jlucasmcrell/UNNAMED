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
def share_correctives(body, meshes):
    """Give every garment mesh the body's corrective shape keys, which HumGen's export leaves off most of them.

    A worn garment that lacks a corrective does not follow the shoulder or the elbow when they move, and the body comes
    through the cloth. The correctives are a small set, so a topology-independent transfer is cheap here.
    """
    keys = body.data.shape_keys
    if keys is None:
        return 0
    wanted = [block for block in keys.key_blocks if block.name.startswith('cor_')]
    shared = 0
    for mesh in meshes:
        if mesh.data.shape_keys is None:
            mesh.shape_key_add(name='Basis', from_mix=False)
        missing = [block for block in wanted if mesh.data.shape_keys.key_blocks.get(block.name) is None]
        for block in missing:
            body.active_shape_key_index = keys.key_blocks.find(block.name)
            bpy.ops.object.select_all(action='DESELECT')
            body.select_set(True)
            mesh.select_set(True)
            bpy.context.view_layer.objects.active = body
            bpy.ops.object.shape_key_transfer()
            shared += 1
    return shared


def complete_correctives(human, tag):
    """Give the worn garment HumGen's full corrective set; its own completion bails out on a partial set.

    `base_clothing._add_missing_correctives` returns as soon as a garment has any `cor_` key, and our native parts were saved
    with four of the body's ten, so the elbow, shoulder and knee correctives never arrive and the body comes through the cloth.
    Nothing here is allowed to kill the export: a failure is written down and the part still builds.
    """
    if tag:
        return
    try:
        from HumGen3D.human.clothing.garment_fit import missing_correctives
        body = human.objects.body
        print('CREATOR_CLOTH_BODY', type(body).__name__, body.name, len(body.data.shape_keys.key_blocks)
              if body.data.shape_keys else 0, flush=True)
        wanted = [key.name for key in body.data.shape_keys.key_blocks if key.name.startswith('cor_')]
        for obj in human.clothing.outfit.objects:
            print('CREATOR_CLOTH_OBJ', type(obj).__name__, getattr(obj, 'name', '?'),
                  len(obj.data.shape_keys.key_blocks) if obj.data.shape_keys else 0, flush=True)
            if obj.data.shape_keys is None:
                continue
            have = {key.name for key in obj.data.shape_keys.key_blocks}
            short = [name for name in wanted if name not in have]
            if not short:
                continue
            # The fit leaves out a corrective that moves nothing on this garment (trousers and an elbow, say), which is
            # HumGen's own rule, so only the keys it does return are added.
            fitted = missing_correctives(human, obj)
            added = 0
            for name in short:
                coords = fitted.get(name)
                if coords is None:
                    continue
                key = obj.shape_key_add(name=name, from_mix=False)
                key.interpolation = 'KEY_LINEAR'
                key.data.foreach_set('co', coords.ravel())
                key.value = 0
                added += 1
            print('CREATOR_CLOTH_CORRECTIVES', obj.name, 'added', added, 'of', len(short), 'missing', flush=True)
    except Exception as failure:
        import traceback
        print('CREATOR_CLOTH_FAILED', repr(failure), flush=True)
        traceback.print_exc()
        (work / 'correctives_failure.txt').write_text(traceback.format_exc(), encoding='utf-8')


# How much hair hangs below the skull decides how much of it moves (brief D12).
HAIR_CHAINS = {
    'long': dict(chains=('back', 'side_l', 'side_r'), segments=4,
                 spring=dict(stiffness=.28, drag=.22, gravity=.06, radius=.022)),
    'medium': dict(chains=('back', 'side_l', 'side_r'), segments=3,
                   spring=dict(stiffness=.4, drag=.3, gravity=.05, radius=.02)),
}


def hair_class(option_id):
    if option_id.startswith('hair.long.'):
        return 'long'
    if option_id.startswith('hair.curls.'):
        return 'medium'
    return ''


def add_hair_chains(rig, meshes, length_class, option_id):
    """Hang bone chains under Head along the falling hair and weight the cards to them.

    Returns the spring specification the runtime reads from the part's contract, or nothing for a short style.
    """
    if not length_class:
        return {}
    spec = HAIR_CHAINS[length_class]
    import math
    head = next((b for b in rig.data.bones if b.name == 'Head'), None)
    assert head is not None, 'Hair export found no Head bone on the rig'
    corners = [mesh.matrix_world @ v.co for mesh in meshes for v in mesh.data.vertices]
    top = max(c.z for c in corners)
    bottom = min(c.z for c in corners)
    axis_x = sum(c.x for c in corners) / len(corners)
    axis_y = sum(c.y for c in corners) / len(corners)
    fingers = {
        'back': (axis_x, axis_y - (top - bottom) * .12),
        'side_l': (axis_x - (top - bottom) * .10, axis_y),
        'side_r': (axis_x + (top - bottom) * .10, axis_y),
    }
    drop = max(top - bottom, .05)
    segments = spec['segments']
    length = drop / segments
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    made = {}
    for chain in spec['chains']:
        x, y = fingers[chain]
        parent = rig.data.edit_bones['Head']
        names = []
        for n in range(segments):
            bone = rig.data.edit_bones.new('hair_' + chain + '_' + str(n + 1))
            bone.head = (x, y, top - length * n)
            bone.tail = (x, y, top - length * (n + 1))
            bone.parent = parent
            bone.use_connect = n > 0
            parent = bone
            names.append(bone.name)
        made[chain] = names
    bpy.ops.object.mode_set(mode='OBJECT')
    for mesh in meshes:
        groups = {chain: [mesh.vertex_groups.new(name=bone) for bone in names] for chain, names in made.items()}
        head_group = mesh.vertex_groups.get('Head') or mesh.vertex_groups.new(name='Head')
        for vertex in mesh.data.vertices:
            point = mesh.matrix_world @ vertex.co
            below = (top - point.z) / max(drop, .001)
            if below <= 0:
                head_group.add([vertex.index], 1.0, 'REPLACE')
                continue
            angle = math.atan2(point.x - axis_x, point.y - axis_y) * 180.0 / math.pi
            chain = 'back' if abs(angle) > 120 else ('side_r' if angle > 0 else 'side_l')
            held = min(max(below, 0.0), 1.0) * segments - .5
            index = min(max(int(held), 0), segments - 1)
            blend = max(0.0, min(1.0, held - index))
            tilt = min(1.0, below * 3.0)
            groups[chain][index].add([vertex.index], (1.0 - blend) * tilt, 'REPLACE')
            if index + 1 < segments:
                groups[chain][index + 1].add([vertex.index], blend * tilt, 'REPLACE')
            if tilt < 1.0:
                head_group.add([vertex.index], 1.0 - tilt, 'REPLACE')
    print('CREATOR_HAIR_CHAINS', option_id, length_class, sum(len(v) for v in made.values()), flush=True)
    return {'hair_' + chain: dict(spec['spring']) for chain in made}


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
            complete_correctives(human, tag)
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
            # A clasp, a shirt or a boot must follow the shoulder, the elbow and the knee exactly as the body does, or the skin
            # comes through the cloth the moment a clip moves a joint (owner's fix 8).
            if not tag:
                print('NATIVE_PART_CORRECTIVES', identifier, share_correctives(copy.objects.body, meshes), flush=True)
            # A medium or long style gets the chains that let the hair move; a short one stays welded to the skull.
            chains = add_hair_chains(copy.objects.rig, meshes, hair_class(option['id']), option['id']) if tag else {}
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
            extra = set(exported.names.keys()) - set(target.names.keys())
            assert all(name.startswith('hair_') for name in extra), 'Part adds bones that are not hair chains: ' + str(sorted(extra))
            assert target.names.keys() <= exported.names.keys(), 'Part rig vocabulary differs from the base'
            a, b = target.globals(), exported.globals()
            assert max(float(np.max(np.abs(a[name] - b[name]))) for name in a) < .0001, 'Part rest differs from the exact creator base'
            (folder / 'native_materials.json').write_text(json.dumps(flow, indent=2))
            identity = json.loads((work / 'game/primary/rigged' / base / 'identity_weights.json').read_text())
            (folder / 'creator_part.json').write_text(json.dumps(dict(kind=kind, option=option['id'], target_base=base,
                target_body_sha256=identity['target_body_sha256'], source=path, native_meshes=[obj.name for obj in meshes],
                **({'hair_chains': chains} if chains else {})), indent=2))
            print('NATIVE_PART_DONE', identifier, len(meshes), flush=True)
print('NATIVE_PARTS_COMPLETE', flush=True)
