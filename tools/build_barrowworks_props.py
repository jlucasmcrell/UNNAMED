"""CPU-only assembly of real metal/masonry assets and first-party paper/type for the ring's identifiable fixtures."""
import json
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Vector
import numpy as np

DEPOT = Path(r'D:\Otherreach_Depot\barrowworks_codex')
bpy.ops.wm.read_factory_settings(use_empty=True)
sources = {}
for kind in ('masonry', 'pipe', 'valve', 'elbow', 'pillar'):
    asset = 'bw_codex_'+kind
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(DEPOT/'ready'/asset/(asset+'.glb')))
    imported = set(bpy.data.objects)-before
    mesh = next(o for o in imported if o.type == 'MESH')
    data = mesh.data.copy()
    data.transform(mesh.matrix_world)
    if kind == 'pipe': data.transform(Matrix.Rotation(-math.pi/2,4,'Y'))
    low = Vector(tuple(min(v.co[i] for v in data.vertices) for i in range(3)))
    high = Vector(tuple(max(v.co[i] for v in data.vertices) for i in range(3)))
    data.transform(Matrix.Translation(-(low+high)/2))
    sources[kind] = data, high-low
    for obj in imported: bpy.data.objects.remove(obj,do_unlink=True)

def flat_material(name, colour):
    image = bpy.data.images.new(name+'_colour',width=4,height=4)
    image.pixels = list(colour)*16
    image.pack()
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = image
    bsdf = nodes.get('Principled BSDF')
    material.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = .87
    return material

paper = flat_material('first_party_aged_paper',(.55,.48,.34,1))
ink = flat_material('first_party_carbon_ink',(.025,.022,.018,1))

def piece(kind, at, dimensions, rotation=(0,0,0)):
    data,size = sources[kind]
    obj = bpy.data.objects.new(kind,data.copy())
    bpy.context.collection.objects.link(obj)
    obj.location = at
    obj.rotation_euler = rotation
    obj.scale = tuple(dimensions[i]/size[i] for i in range(3))
    return obj

def label(text,width,height,front=-1,at=(0,0,0)):
    x,y,z = at
    mesh = bpy.data.meshes.new('paper_mesh')
    mesh.from_pydata([(x,y-width/2,z), (x,y+width/2,z), (x,y+width/2,z+height), (x,y-width/2,z+height)],[],
                     [(3,2,1,0) if front<0 else (0,1,2,3)])
    uv = mesh.uv_layers.new()
    for loop in mesh.loops: uv.data[loop.index].uv = ((0,0),(1,0),(1,1),(0,1))[loop.vertex_index]
    page = bpy.data.objects.new('ruled_paper',mesh)
    bpy.context.collection.objects.link(page)
    page.data.materials.append(paper)
    curve = bpy.data.curves.new('inscription','FONT')
    curve.body = text
    curve.size = min(width/max(8,max(map(len,text.splitlines())))*1.4,height/max(4,len(text.splitlines())+1))
    curve.space_line = 1.35
    curve.extrude = .0005
    curve.resolution_u = 2
    obj = bpy.data.objects.new('printed_heading',curve)
    bpy.context.collection.objects.link(obj)
    obj.location = (x+front*.003,y-front*(width/2-.08),z+height-.13)
    # Local text X runs horizontally on the paper, local Y is vertical, its front faces the room.
    obj.rotation_euler = Matrix(((0,0,front),(front,0,0),(0,1,0))).to_euler()
    obj.data.materials.append(ink)
    return page,obj

manifest=[]
def export(asset_id, pieces, animation=False):
    folder = DEPOT/'ready'/asset_id
    folder.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in pieces: obj.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.export_scene.gltf(filepath=str(folder/(asset_id+'.glb')),export_format='GLB',use_selection=True,
        export_yup=True,export_apply=True,export_animations=animation,export_animation_mode='ACTIVE_ACTIONS',
        export_force_sampling=True,export_lights=False)
    metadata={'asset_id':asset_id,'category':'prop','source':'Licensed source modules; manual assembly and first-party paper/type.',
        'sources':['DanStudios Lost Temple: Fab Standard Personal','SLASH-RENDAR Old Industrial Pipe Pack PBR: CC-BY 4.0'],
        'modifications':'Resized, assembled, labelled and animated where specified. Source materials retained.',
        'licence_evidence':r'C:\Otherreach_Recovery\ASSET_REBUILD\FAB_LICENCES.md','shippable':True}
    (folder/(asset_id+'_meta.json')).write_text(json.dumps(metadata,indent=2)+'\n',encoding='utf-8')
    manifest.append(metadata)
    for obj in pieces: bpy.data.objects.remove(obj,do_unlink=True)

for asset,width,height,front,text in [
    ('notice',2,1.6,-1,'SERVICE RING\nWITHDRAWAL NOTICE\n\nMEASUREMENT\nUNFINISHED'),
    ('fitting_note',3.2,1.4,1,'FITTING NOTE\nASH TAPER\n\nUNFINISHED'),
    ('diagram',3.2,1.8,-1,'FITTING PATTERN\nASH WEDGE\n\nCOMPARE THE CUTS'),
    ('ledger',5.2,1.5,-1,'BLIND STORE\nMEASUREMENTS\n\nLAST READING')]:
    pieces=[piece('masonry',(0,0,height/2),(.4,width,height))]
    # The source masonry frame supports distinct paper/board layouts, rather than another unmarked rock slab.
    pages=3 if asset=='fitting_note' else 2 if asset=='ledger' else 1
    for i in range(pages):
        y=(i-(pages-1)/2)*(width-.2)/pages
        pieces.extend(label(text if i==0 else 'FITTING\n\nMEASURE\n\nUNFINISHED', (width-.25)/pages,height-.3,front,
                            (front*.225,y,.15)))
    if asset=='diagram':
        # A visible taper assembled from the same physical iron rail: a pattern comparison, not a deciphering minigame.
        pieces.append(piece('pipe',(-.26,.2,.65),(.045,.045,1.1),(math.pi/2,0,.12)))
    export('bw_codex_'+asset,pieces)

pieces=[]
for y in (-2.4,2.4):
    pieces.append(piece('pipe',(0,y,1.1),(.25,.25,2.2)))
    pieces.append(piece('valve',(0,y,.2),(.4,.35,.4)))
pieces.append(piece('pipe',(0,0,1.25),(.2,.2,5.2),(math.pi/2,0,0)))
pieces.append(piece('pipe',(0,0,1.65),(.12,.12,4.8),(math.pi/2,0,0)))
pieces.append(piece('valve',(.1,0,1.45),(.22,1.0,1.0),(0,math.pi/2,0)))
parts=list(pieces)
export('bw_codex_measure',parts)
# Rebuild the moving outcome as a visibly whole beam; the offset index rotates on the game tick.
pieces=[]
for y in (-2.4,2.4): pieces.append(piece('pipe',(0,y,1.1),(.25,.25,2.2)))
pieces.append(piece('pipe',(0,0,1.25),(.2,.2,5.2),(math.pi/2,0,0)))
index=piece('valve',(.1,0,1.45),(.22,1.0,1.0),(0,math.pi/2,0))
pieces.append(index)
for frame,turn in ((1,0),(121,2*math.pi)):
    index.rotation_euler.x=turn
    index.keyframe_insert('rotation_euler',frame=frame)
bpy.context.scene.frame_start,bpy.context.scene.frame_end=1,121
bpy.context.scene.render.fps=30
export('bw_codex_measure_running',pieces,True)
export('bw_codex_measure_dismantled',[
    piece('pipe',(0,-1.8,.35),(.2,.2,.7)), piece('pipe',(0,1.8,.35),(.2,.2,.7)),
    piece('pipe',(0,0,.2),(.15,.15,4.0),(math.pi/2,0,0)), piece('valve',(0,-.5,.25),(.35,.6,.4))])
for name in ('preserve_control','dismantle_control'):
    pieces=[piece('pillar',(0,0,.43),(.5,.5,.86)),piece('valve',(0,0,1.0),(.6,.5,.2))]
    export('bw_codex_'+name,pieces)
pieces=list(label('CODEX\n\nRULED FRAGMENT',.5,.35,-1,(-.05,0,.08)))
export('bw_codex_codex_fragment',pieces)
foot=piece('valve',(0,0,.2),(.65,.55,.4))
for i, material in enumerate(foot.data.materials):
    material=material.copy()
    foot.data.materials[i]=material
    material.name='bronze_base_source_'+material.name
    # A bronze finish on the retained valve scan, without replacing the source mesh with a generated gear.
    bsdf=material.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Metallic'].default_value=.9
    base=bsdf.inputs['Base Color']
    if base.links:
        original=base.links[0].from_socket
        mix=material.node_tree.nodes.new('ShaderNodeMixRGB')
        mix.blend_type='MULTIPLY'
        mix.inputs[0].default_value=1
        mix.inputs[2].default_value=(.7,.43,.16,1)
        material.node_tree.links.new(original,mix.inputs[1])
        material.node_tree.links.new(mix.outputs[0],base)
export('bw_codex_lantern_foot',[foot])
for gate in range(1,4):
    pieces=[piece('pipe',(0,y,1.2),(.25,.25,2.4)) for y in (-1.35,-.68,0,.68,1.35)]
    for z in (.25,1.2,2.2): pieces.append(piece('pipe',(0,0,z),(.2,.2,3.0),(math.pi/2,0,0)))
    pieces.append(piece('valve',(-.2,0,1.3),(.2,.5,.5),(0,math.pi/2,0)))
    export(f'bw_codex_gate_{gate}',pieces)
(DEPOT/'props_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print('BARROWWORKS_PROPS',len(manifest),flush=True)
for name,length,width,height in [('wash_pipe',3,.8,2),('drain_valve',3,.8,1.8),('inspection_gauge',3,1,1.8)]:
    pieces=[piece('pipe',(0,0,height*.65),(.3,.3,length),(math.pi/2,0,0))]
    for y in (-length*.4,length*.4): pieces.append(piece('pipe',(0,y,height*.3),(.25,.25,height*.6)))
    pieces.append(piece('valve',(-width*.3,0,height*.65),(.25,width*.75,width*.75),(0,math.pi/2,0)))
    export('bw_codex_'+name,pieces)
pieces=[piece('masonry',(0,0,.15),(1,3,.3))]
for y in (-1.3,0,1.3): pieces.append(piece('pipe',(0,y,.85),(.15,.15,1.1)))
for z in (.45,1.25): pieces.append(piece('pipe',(0,0,z),(.13,.13,3),(math.pi/2,0,0)))
export('bw_codex_sorting_rack',pieces)
export('bw_codex_work_seat',[piece('masonry',(0,0,.47),(1,2.5,.26)),
    piece('pillar',(0,-.9,.22),(.6,.5,.44)),piece('pillar',(0,.9,.22),(.6,.5,.44))])
export('bw_codex_counterweight',[piece('masonry',(0,0,.5),(1,2,1)),
    piece('pipe',(0,0,1.55),(.2,.2,1.7)),piece('valve',(0,0,2.1),(.6,.8,.25))])
(DEPOT/'props_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
