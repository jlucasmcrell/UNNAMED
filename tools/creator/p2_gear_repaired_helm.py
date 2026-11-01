"""Give A15 a visibly mismatched replacement-steel visor, using real source maps.

CPU Blender: -- <gear work> <creator work> <female|male>.
The previously passed native fit geometry/weights are retained; normal HumGen
export and rest/control gates run again. A fresh native review remains required.
"""
import bpy,json,sys,hashlib
from pathlib import Path
from mathutils import Vector
from HumGen3D.human.human import Human
sys.path.insert(0,str(Path(__file__).resolve().parent))
from p2_gear_native_export import export_candidate
args=sys.argv[sys.argv.index('--')+1:];work,creator=map(Path,args[:2]);sex=args[2]
row=next(r for r in json.loads((work/'selected_manifest_armour_native_v1.json').read_text()) if r['review_id']=='A15')
row={**row,'native_candidate_root':'armour_native_helm_art_v6'}
identifier=row['art_id']+'__humgen_creator_'+sex
original=work/'armour_native_headcontrols_v2/sources'/identifier/'native_outfit.blend'
bpy.ops.wm.open_mainfile(filepath=str(original))
human=Human.from_existing(bpy.data.objects['humgen_creator_'+sex]);obj=human.clothing.outfit.objects[0]
original_mat=obj.data.materials[0];repair=original_mat.copy();repair.name='P2_A15_Replacement_Visor_Steel'
shader=repair.node_tree.nodes.get('Principled BSDF')
grade=work/'armour_repair_materials/A15_replacement_visor_bare_steel_v4.png'
texture=repair.node_tree.nodes.new('ShaderNodeTexImage')
texture.image=bpy.data.images.load(str(grade),check_existing=True)
repair.node_tree.links.new(texture.outputs['Color'],shader.inputs['Base Color'])
for link in list(shader.inputs['Metallic'].links):repair.node_tree.links.remove(link)
shader.inputs['Metallic'].default_value=.25
# Oxidised replacement steel retains a diffuse response under the normal game
# environment. A polished fully metallic plate reflected the dark floor and
# remained indistinguishable from the original black-painted visor.
for link in list(shader.inputs['Roughness'].links):repair.node_tree.links.remove(link)
shader.inputs['Roughness'].default_value=.65
obj.data.materials.append(repair);slot=len(obj.data.materials)-1
bpy.context.view_layer.update();evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
# The source has three actual connected plates. Select its complete forward
# visor island, joining duplicated UV-seam positions for connectivity. A spatial
# slab selected fragments of the crown and produced an unacceptable sawtooth
# material edge; the real plate's authored boundary is the repair boundary.
parent={}
def find(key):
    parent.setdefault(key,key)
    if parent[key]!=key:parent[key]=find(parent[key])
    return parent[key]
keys={v.index:tuple(round(float(a),5) for a in v.co) for v in obj.data.vertices}
for face in obj.data.polygons:
    root=find(keys[face.vertices[0]])
    for i in face.vertices[1:]:parent[find(keys[i])]=root
islands={}
for face in obj.data.polygons:islands.setdefault(find(keys[face.vertices[0]]),[]).append(face.index)
def front(faces):
    return min((obj.matrix_world@obj.data.vertices[i].co).y for f in faces for i in obj.data.polygons[f].vertices)
selected=min((faces for faces in islands.values() if len(faces)>30),key=front)
assert len(selected)<len(obj.data.polygons)/3,('Expected a separate source visor plate',len(selected))
for index in selected:obj.data.polygons[index].material_index=slot
assert len(selected)>30,('Replacement visor selection empty',len(selected))
obj.data.update()
audit=dict(method='Original native passed fit retained; existing front visor faces receive mismatched replacement steel material',
    replacement_visor_source_faces=selected,source_plate_islands=[len(faces) for faces in islands.values()],material_recipe=str(work/'armour_repair_materials/A15_MATERIAL_RECIPE_V4.json'),
    material_sha256=hashlib.sha256(grade.read_bytes()).hexdigest(),metallic=.25,roughness=.65,geometry_changed=False,weights_changed=False)
export_candidate(human,row,work,creator,sex,Path(row['source']),'kept_native_passed_head_fit',0,audit)
