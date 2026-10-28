"""Export native modular grooms/wardrobe on the two creator skeletons. CPU only.

HumGen processes its own highest-quality LOD and materials. Export selection
contains only the requested parts and their exact base rig, never another body.
Usage in Blender: -- <creator-work> <catalogue> [<option-id> ...]
"""
import json
import re
import sys
from pathlib import Path
import bmesh
import bpy
import numpy as np
from mathutils import Vector
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
                 spring=dict(stiffness=.55, drag=.45, gravity=.035, radius=.028)),
    'medium': dict(chains=('back', 'side_l', 'side_r'), segments=3,
                   spring=dict(stiffness=.55, drag=.45, gravity=.035, radius=.028)),
}


def hair_class(option_id):
    if option_id.startswith('hair.long.'):
        return 'long'
    if option_id.startswith('hair.curls.'):
        return 'medium'
    return ''


def add_hair_chains(rig, meshes, length_class, option_id):
    """Hang bone chains along the hair's own fall and weight the cards to them.

    Returns the spring specification the runtime reads from the part's contract, or nothing for a short style.
    """
    if not length_class:
        return {}
    import math
    spec = HAIR_CHAINS[length_class]
    head = next((b for b in rig.data.bones if b.name == 'Head'), None)
    assert head is not None, 'Hair export found no Head bone on the rig'
    origin = rig.matrix_world @ head.head_local
    points = [rig.matrix_world @ v.co for mesh in meshes for v in mesh.data.vertices]
    top = max(p.z for p in points)
    bottom = min(p.z for p in points)
    drop = max(top - bottom, .05)
    segments = spec['segments']

    def sector(point):
        angle = math.degrees(math.atan2(point.x - origin.x, point.y - origin.y))
        return 'back' if abs(angle) > 120 else ('side_r' if angle > 0 else 'side_l')

    bands = {}
    for point in points:
        below = (top - point.z) / drop
        index = min(int(below * segments), segments - 1)
        bands.setdefault((sector(point), index), []).append(point)
    chains = {}
    for chain in spec['chains']:
        nodes = []
        for index in range(segments):
            group = bands.get((chain, index)) or bands.get(('back', index)) or []
            if group:
                nodes.append(sum(group, Vector()) / len(group))
        if len(nodes) < 2:
            continue
        chains[chain] = nodes
    if not chains:
        return {}
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    made = {}
    for chain, nodes in chains.items():
        parent = rig.data.edit_bones['Head']
        names = []
        for index in range(len(nodes) - 1):
            bone = rig.data.edit_bones.new('hair_' + chain + '_' + str(index + 1))
            bone.head = nodes[index]
            bone.tail = nodes[index + 1]
            bone.parent = parent
            bone.use_connect = index > 0
            parent = bone
            names.append(bone.name)
        made[chain] = (names, nodes)
    bpy.ops.object.mode_set(mode='OBJECT')
    for mesh in meshes:
        groups = {chain: [mesh.vertex_groups.new(name=bone) for bone in names] for chain, (names, _) in made.items()}
        head_group = mesh.vertex_groups.get('Head') or mesh.vertex_groups.new(name='Head')
        for vertex in mesh.data.vertices:
            point = mesh.matrix_world @ vertex.co
            below = (top - point.z) / drop
            best = None
            for chain, (names, nodes) in made.items():
                for index in range(len(nodes) - 1):
                    a, b = nodes[index], nodes[index + 1]
                    span = (b - a)
                    length = span.length
                    if length < 1e-6:
                        continue
                    along = max(0.0, min(1.0, (point - a).dot(span) / (length * length)))
                    distance = (a + span * along - point).length
                    if best is None or distance < best[0]:
                        best = (distance, chain, index, along)
            if best is None or below <= .02:
                head_group.add([vertex.index], 1.0, 'REPLACE')
                continue
            _, chain, index, along = best
            names = made[chain][0]
            names_for_group = groups[chain]
            tilt = min(1.0, below * 3.0)
            names_for_group[index].add([vertex.index], (1.0 - along) * tilt, 'REPLACE')
            if index + 1 < len(names):
                names_for_group[index + 1].add([vertex.index], along * tilt, 'REPLACE')
            else:
                names_for_group[index].add([vertex.index], along * tilt, 'REPLACE')
            if tilt < 1.0:
                head_group.add([vertex.index], 1.0 - tilt, 'REPLACE')
    print('CREATOR_HAIR_CHAINS', option_id, length_class, sum(len(names) for names, _ in made.values()), flush=True)
    return {'hair_' + chain: dict(spec['spring']) for chain in made}


def covered_body(body, meshes):
    """Which body vertices each worn garment covers, and their triangles in the body's own UV space.

    A vertex is covered when its nearest point on the worn garments is within 10 mm and the vertex lies behind that surface, which
    is what HumGen's per-garment mask means geometrically. The triangles are written in UV so the runtime can discard the skin there
    without touching the body mesh.
    """
    from mathutils.bvhtree import BVHTree
    points, polygons = [], []
    for obj in meshes:
        offset = len(points)
        points += [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
        for polygon in obj.data.polygons:
            polygons.append([offset + index for index in polygon.vertices])
    if not polygons:
        return None
    tree = BVHTree.FromPolygons(points, polygons)
    covered = set()
    for vertex in body.data.vertices:
        probe = body.matrix_world @ vertex.co
        hit = tree.find_nearest(probe)
        if hit[0] is None:
            continue
        point, normal, _, distance = hit
        if distance is None:
            continue
        # Inside the garment, or close enough to its edge that a hole in it would show skin: the second test is what closes the
        # patches where the shirt's own triangles are missing.
        if distance <= .020 and ((probe - point).dot(normal) < .001 or distance <= .008):
            covered.add(vertex.index)
    if not covered:
        return None
    uv_layer = body.data.uv_layers.active
    if uv_layer is None:
        raise SystemExit('CREATOR_BODY_MASK_NO_UV: the body has no active UV layer to stencil against')
    uvs = uv_layer.data
    triangles = []
    for polygon in body.data.polygons:
        inside = [index for index in polygon.vertices if index in covered]
        if len(inside) < 2:
            continue
        corners = [uvs[loop].uv for loop in polygon.loop_indices]
        for step in range(1, len(corners) - 1):
            triangles.append([corners[0].x, corners[0].y, corners[step].x, corners[step].y,
                              corners[step + 1].x, corners[step + 1].y])
    return dict(body_vertex_count=len(body.data.vertices), covered=len(covered), uv_triangles=triangles)


def add_lining(meshes):
    """Copy each worn mesh, flip it inside out and pull it 3 mm toward the body: the cloth seen through an opening."""
    lining = []
    for obj in meshes:
        # Only a skinned garment piece gets a lining: a static piece (a collar) has no bones to follow and would sit apart.
        if not any(modifier.type == 'ARMATURE' for modifier in obj.modifiers):
            continue
        copy = obj.copy()
        copy.data = obj.data.copy()
        copy.name = obj.name + '_lining'
        bpy.context.collection.objects.link(copy)
        mesh = bmesh.new()
        mesh.from_mesh(copy.data)
        bmesh.ops.reverse_faces(mesh, faces=list(mesh.faces))
        mesh.normal_update()
        for vertex in mesh.verts:
            vertex.co -= vertex.normal * .003
        mesh.to_mesh(copy.data)
        mesh.free()
        copy.parent = obj.parent
        copy.parent_type = obj.parent_type
        if obj.parent_type == 'BONE': copy.parent_bone = obj.parent_bone
        copy.matrix_world = obj.matrix_world
        for modifier in obj.modifiers:
            if modifier.type == 'ARMATURE':
                copy.modifiers.new(modifier.name, 'ARMATURE').object = modifier.object
        lining.append(copy)
    return lining


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
            # A worn garment ships with its own lining, so an opening reveals cloth rather than the body (owner's route b).
            if not tag:
                meshes = meshes + add_lining(meshes)
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
            if not tag:
                mask = covered_body(copy.objects.body, meshes)
                if mask:
                    (folder / 'body_coverage.json').write_text(json.dumps(
                        dict(target_body_sha256=identity['target_body_sha256'], **mask)))
                    print('CREATOR_BODY_MASK', identifier, mask['covered'], 'of', mask['body_vertex_count'],
                          len(mask['uv_triangles']), 'triangles', flush=True)
            (folder / 'creator_part.json').write_text(json.dumps(dict(kind=kind, option=option['id'], target_base=base,
                target_body_sha256=identity['target_body_sha256'], source=path, native_meshes=[obj.name for obj in meshes],
                **({'hair_chains': chains} if chains else {})), indent=2))
            print('NATIVE_PART_DONE', identifier, len(meshes), flush=True)
print('NATIVE_PARTS_COMPLETE', flush=True)
