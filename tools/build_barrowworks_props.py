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
for kind in ('masonry', 'pipe', 'valve', 'elbow', 'pillar', 'relief_rock', 'relief_masonry'):
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

# Deterministic stained paper with small ink strokes; no luminous stencil labels.
rng=np.random.default_rng(805)
paper=flat_material('first_party_aged_paper',(.45,.36,.22,1))
image=next(n.image for n in paper.node_tree.nodes if n.type=='TEX_IMAGE')
image.scale(256,256)
noise=rng.normal(0,.025,(256,256,1));base=np.array([.47,.39,.26])[None,None,:]
colour=np.clip(base+noise,0,1);pixels=np.concatenate([colour,np.ones((256,256,1))],axis=2)
image.pixels.foreach_set(pixels.astype(np.float32).ravel());image.pack()
ink=flat_material('first_party_carbon_ink',(.035,.029,.02,1))
wood=flat_material('first_party_dark_oiled_frame',(.11,.065,.033,1))

def piece(kind, at, dimensions, rotation=(0,0,0)):
    data,size = sources[kind]
    obj = bpy.data.objects.new(kind,data.copy())
    bpy.context.collection.objects.link(obj)
    obj.location = at
    obj.rotation_euler = rotation
    obj.scale = tuple(dimensions[i]/size[i] for i in range(3))
    return obj

def box(name,at,size,material):
    bpy.ops.mesh.primitive_cube_add(size=1,location=at)
    obj=bpy.context.object;obj.name=name;obj.scale=size;obj.data.materials.append(material)
    bevel=obj.modifiers.new('worn_edges','BEVEL');bevel.width=.012;bevel.segments=2
    bpy.context.view_layer.objects.active=obj;bpy.ops.object.modifier_apply(modifier=bevel.name)
    return obj

def page(at,width=.4,height=.55,front=-1,diagram=False):
    x,y,z=at
    # Curl the outer edges and preserve a physical page thickness/shadow.
    verts=[]
    for row in range(6):
        for col in range(6):
            u,v=col/5,row/5
            curl=.015*abs(u-.5)**2+.004*math.sin(v*5+u*4)
            verts.append((x+front*curl,y+(u-.5)*width,z+v*height))
    faces=[(r*6+c,r*6+c+1,(r+1)*6+c+1,(r+1)*6+c) for r in range(5) for c in range(5)]
    mesh=bpy.data.meshes.new('creased_paper');mesh.from_pydata(verts,[],faces);mesh.materials.append(paper)
    obj=bpy.data.objects.new('curled_document',mesh);bpy.context.collection.objects.link(obj)
    uv=mesh.uv_layers.new()
    for poly in mesh.polygons:
        for loop in poly.loop_indices:
            index=mesh.loops[loop].vertex_index;uv.data[loop].uv=(index%6/5,index//6/5)
    solid=obj.modifiers.new('paper_edge','SOLIDIFY');solid.thickness=.001
    pieces=[obj]
    # Small uneven ruled strokes read as writing at close range, not a billboard heading.
    for row in range(9):
        w=width*(.55+.25*float(rng.random()));yy=y-width*.38+w/2
        pieces.append(box('ink_hand', (x+front*.009,yy,z+height*.15+row*height*.075),(.001,w,.002),ink))
    if diagram:
        for dy,angle in ((-.07,.12),(.08,-.12)):
            line=box('drawn_taper',(x+front*.012,y+dy,z+height*.52),(.002,.003,height*.60),ink)
            line.rotation_euler.x=angle;pieces.append(line)
    return pieces

def label(text,width,height,front=-1,at=(0,0,0)):
    # Pickup fragment: retain the folded paper shape, with its reading supplied by the live interaction text.
    return page(at,min(width,.45),min(height,.55),front)

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

# The four readings have different physical forms: pinned notice, filing shelf, drawing easel and bound ledger.
for asset,width,height,front in [('notice',2,1.6,-1),('fitting_note',3.2,1.4,1),('diagram',3.2,1.8,-1),('ledger',5.2,1.5,-1)]:
    pieces=[]
    if asset=='notice':
        for y in (-.65,0,.65):pieces.append(box('notice_plank',(0,y,.8),(.18,.6,1.6),wood))
        for y,z in ((-.55,.25),(.08,.55),(.53,.22)):
            pieces+=page((-.105,y,z),.43,.63,-1)
            pieces.append(piece('pipe',(-.13,y,z+.59),(.025,.025,.05)))
    elif asset=='fitting_note':
        for y in (-1.4,1.4):pieces.append(box('shelf_upright',(0,y,.7),(.38,.12,1.4),wood))
        for z in (.1,.7,1.35):pieces.append(box('filing_shelf',(0,0,z),(.4,3.2,.10),wood))
        for y in (-1,-.5,0,.5,1):
            pieces+=page((.215,y,.74),.32,.46,1)
            pieces.append(box('folder_spine',(.08,y,.4),(.35,.11,.50),paper))
    elif asset=='diagram':
        for y in (-1.35,1.35):pieces.append(box('easel_leg',(0,y,.9),(.25,.18,1.8),wood))
        pieces.append(box('drawing_table',(0,0,1.12),(.4,3.2,.13),wood))
        # Roll ends and a hanging drawing, plus a physical comparison taper beside it.
        pieces+=page((-.22,-.4,.60),.8,.85,-1,True)
        for y in (-1.1,.7):pieces.append(piece('pipe',(-.19,y,.74),(.07,.07,.95)))
        pieces.append(piece('pipe',(-.22,.7,1.38),(.05,.05,.8),(math.pi/2,0,.08)))
    else:
        for y in (-2.45,0,2.45):pieces.append(box('archive_leg',(0,y,.7),(.35,.17,1.4),wood))
        pieces.append(box('ledger_counter',(0,0,1.43),(.4,5.2,.14),wood))
        # Open folio at the counter's front: a leather binding and fanned paper leaves, no giant text board.
        for y in (-.25,.25):
            cover=box('leather_folio',(-.22,y,1.20),(.08,.46,.50),wood);cover.rotation_euler.z=.12 if y<0 else -.12
            pieces+=page((-.27,y,.96),.40,.43,-1)
        for y in (-1.7,-1.1,1.2,1.8):pieces.append(box('bound_volume',(0,y,.76),(.33,.21,.60),wood))
    export('bw_codex_'+asset,pieces)

def headworks(moving=False):
    pieces=[]
    for y in (-2.35,2.35):
        pieces.append(piece('masonry',(0,y,.18),(.95,.65,.36)))
        for x in (-.31,.31):pieces.append(piece('pipe',(x,y,1.27),(.20,.20,2.2)))
        pieces.append(piece('elbow',(0,y,2.06),(.85,.42,.38)))
        pieces.append(piece('valve',(-.23,y,1.1),(.32,.65,.65),(0,math.pi/2,0)))
    for x,z in ((-.30,1.75),(.30,1.75),(0,.52)):
        pieces.append(piece('pipe',(x,0,z),(.16,.16,5.1),(math.pi/2,0,0)))
    # Sliding gauge carriage and opposed measuring jaws, physically connected to twin rails.
    pieces.append(piece('elbow',(0,0,1.78),(.75,.6,.5)))
    for y in (-.40,.40):pieces.append(piece('pipe',(-.28,y,1.2),(.12,.12,1.25)))
    for y in (-.38,.38):pieces.append(piece('elbow',(-.26,y,.64),(.3,.4,.3)))
    wheel=piece('valve',(-.37,0,1.25),(.28,.95,.95),(0,math.pi/2,0));pieces.append(wheel)
    # A physical scale of alternating riveted graduations below the guide rail.
    for i in range(31):
        pieces.append(piece('pipe',(-.34,-2+i*.133,1.58),(.018,.018,.09 if i%5==0 else .045)))
    if moving:
        for frame,turn in ((1,0),(121,2*math.pi)):
            wheel.rotation_euler.x=turn;wheel.keyframe_insert('rotation_euler',frame=frame)
        bpy.context.scene.frame_start,bpy.context.scene.frame_end=1,121;bpy.context.scene.render.fps=30
    return pieces
export('bw_codex_measure',headworks())
export('bw_codex_measure_running',headworks(True),True)
export('bw_codex_measure_dismantled',[piece('masonry',(0,y,.18),(.95,.65,.36)) for y in (-2.35,2.35)]+[
    piece('pipe',(0,0,.2),(.16,.16,4.0),(math.pi/2,0,0)),piece('valve',(0,-.5,.25),(.35,.6,.4))])
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

# Exterior entrances: masonry threshold and a lower rock-cut drain; both retain an actual dark mouth.
for name,height,width in [('threshold',3.4,3.4),('drain',2.6,3.0)]:
    pieces=[]
    for side in (-1,1):
        pieces.append(piece('relief_rock',(side*(width*.5+.25),0,height*.5),(.9,1.6,height+.35)))
        pieces.append(piece('relief_masonry',(side*width*.34,0,height*.45),(.5,1.0,height*.9)))
    pieces.append(piece('relief_masonry',(0,0,height),(width,1.1,.6)))
    pieces.append(box('shadowed_mouth',(0,.39,height*.46),(width*.57,.045,height*.92),ink))
    pieces.append(piece('masonry',(0,-.22,.05),(width,1.3,.10)))
    if name=='drain':
        for side in (-1,1):pieces.append(piece('pipe',(side*.95,-.48,.75),(.09,.09,1.5)))
    export('bw_codex_entrance_'+name,pieces)
(DEPOT/'props_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
