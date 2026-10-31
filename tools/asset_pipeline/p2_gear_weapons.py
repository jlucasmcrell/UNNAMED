"""Prepare owned source weapons with the normal grip, LOD and export helpers.

Blender: -- <manifest.json> <private-art-root> [review-id ...]
Only writes the private root. This does not bind items or alter source files.
"""
import json,runpy,sys,hashlib
from pathlib import Path
import bpy
from mathutils import Matrix,Vector

args=sys.argv[sys.argv.index('--')+1:]
manifest,root=Path(args[0]),Path(args[1]);only=set(args[2:])
saved=sys.argv;sys.argv=['real_weapons.py','--',str(root/'ready')]
pipeline=runpy.run_path(str(Path(__file__).with_name('real_weapons.py')))
sys.argv=saved
rows=[r for r in json.loads(manifest.read_text()) if r['item_id'].startswith('item.weapon.') and (not only or r['review_id'] in only)]
recipes={
 'W01':(1,-1,.99,.13),'W02':(0,1,1.50,.16),'W03':(1,1,1.12,.12),
 'W04':(0,1,1.02,.12),'W05':(1,1,1.08,.12),'W06':(1,1,.81,.22),
 'W07':(1,1,.72,.20),'W08':(1,1,1.90,.48),'W09':(0,1,1.65,.50),
 'W10':(0,1,1.40,.50),'W13':(1,1,.45,.22),'W14':(1,-1,.34,.23),
 'W15':(1,-1,.29,.24),'W16':(2,1,1.88,.37),'W17':(1,1,1.45,.15),
 'W18':(1,-1,.50,.20),'W20':(1,-1,1.12,.15),
 'W21':(1,-1,.28,.22),'W22':(1,-1,.22,.25),'W23':(1,1,.68,.22),'W24':(1,1,.68,.22),
}
receipts=[]
for row in rows:
    id=row['art_id'];folder=root/'ready'/id
    if (folder/(id+'_meta.json')).exists():continue
    bpy.ops.wm.open_mainfile(filepath=row['source'])
    # A downloaded .blend may hold preview lights, cameras and linked instances.
    # Evaluate each actual mesh, preserving its native modifier result and maps.
    meshes=[];deps=bpy.context.evaluated_depsgraph_get()
    for source in list(bpy.context.scene.objects):
        if source.type not in ('MESH','CURVE') or source.hide_render:continue
        if source.name.lower().startswith('ax sheath'):continue
        evaluated=source.evaluated_get(deps)
        mesh=bpy.data.meshes.new_from_object(evaluated,depsgraph=deps)
        mesh.transform(source.matrix_world)
        obj=bpy.data.objects.new('p2_'+source.name,mesh);bpy.context.scene.collection.objects.link(obj)
        meshes.append(obj)
    assert meshes,'No source meshes: '+row['source']
    for obj in list(bpy.data.objects):
        if obj not in meshes:bpy.data.objects.remove(obj,do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:obj.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0]
    if len(meshes)>1:bpy.ops.object.join()
    weapon=meshes[0]
    weapon.data.validate(clean_customdata=False)
    axis,sign,length,grip=recipes[row['review_id']]
    direction=Vector((int(axis==0)*sign,int(axis==1)*sign,int(axis==2)*sign))
    weapon.data.transform(direction.rotation_difference(Vector((0,0,1))).to_matrix().to_4x4())
    low,high=pipeline['bounds'](weapon)
    if high.x-low.x > high.y-low.y:
        # The established socket frame expects the guard/edge along glTF Z,
        # with the broad face normal along glTF X.
        import math
        weapon.data.transform(Matrix.Rotation(math.pi/2,4,'Z'))
    if row['review_id'] in ('W02','W04'):
        pipeline['flip_if_guard_high'](weapon,1)
    if row['review_id']=='W22':
        # The round wooden grip is wider than this fine carving blade. Use the
        # blade's own section to choose edge roll, rather than the grip's box.
        low,high=pipeline['bounds'](weapon)
        blade=[v.co for v in weapon.data.vertices if v.co.z>low.z+(high.z-low.z)*.65]
        if max(v.x for v in blade)-min(v.x for v in blade)>max(v.y for v in blade)-min(v.y for v in blade):
            import math
            weapon.data.transform(Matrix.Rotation(math.pi/2,4,'Z'))
    low,high=pipeline['bounds'](weapon);scale=length/(high.z-low.z)
    weapon.data.transform(Matrix.Scale(scale,4))
    low,high=pipeline['bounds'](weapon)
    weapon.data.transform(Matrix.Translation((0,0,-low.z)))
    grip_z=length*grip
    if any(abs(v.co.z-grip_z)<.035 for v in weapon.data.vertices):
        pipeline['centre_xy_on_axis'](weapon,grip_z-.035,grip_z+.035)
    else:
        # A low-poly shaft can have no vertices near the hand. Its actual edge
        # intersections still locate the shaft, without centring on the head.
        section=[]
        for edge in weapon.data.edges:
            a,b=(weapon.data.vertices[index].co for index in edge.vertices)
            if min(a.z,b.z)<=grip_z<=max(a.z,b.z) and abs(a.z-b.z)>1e-7:
                section.append(a.lerp(b,(grip_z-a.z)/(b.z-a.z)))
        assert section,'Grip plane misses source geometry: '+row['review_id']
        centre=sum(section,Vector())/len(section)
        weapon.data.transform(Matrix.Translation((-centre.x,-centre.y,0)))
    # Real grip centre is specified per source; it is never inferred from the item reach.
    # These source hafts were 41-54 mm across at the hand. Correct transverse
    # metric sizing for a 28 mm grip while retaining length, UVs and native maps.
    grip_scale={'W06':(1,.5),'W07':(.75,.67),'W08':(.55,.55)}.get(row['review_id'],(1,1))
    if grip_scale!=(1,1):weapon.data.transform(Matrix.Diagonal((*grip_scale,1,1)))
    sockets=[pipeline['add_socket']('SOCK_grip_primary',length*grip),pipeline['add_socket']('SOCK_attach',length*.45),pipeline['add_socket']('SOCK_head',length)]
    if row['family']=='polearm':sockets.append(pipeline['add_socket']('SOCK_grip_secondary',length*.66))
    if row['family']=='bow':sockets.append(pipeline['add_socket']('SOCK_ammo',length*.5+.06,0,-.08))
    images={node.image for mat in weapon.data.materials if mat and mat.use_nodes
            for node in mat.node_tree.nodes if node.type=='TEX_IMAGE' and node.image}
    missing=[im.name for im in images if max(im.size)==0]
    assert not missing,'Missing native images: '+str(missing)
    native_px=max([max(im.size) for im in images]+[2048])
    triangles=sum(len(p.vertices)-2 for p in weapon.data.polygons)
    lod_tris=[0,0,0] if triangles<=5000 else [0,max(200,triangles//2),max(100,triangles//4)]
    rec=pipeline['export'](weapon,id,sockets,lod_tris,[native_px,native_px,native_px],dict(source=row['source'],source_sha256=hashlib.sha256(Path(row['source']).read_bytes()).hexdigest(),review_id=row['review_id'],transverse_metric_scale=grip_scale,lod_geometry_policy='Sources at or below 5000 triangles retain native geometry on every level; larger sources reduce by half and quarter.',lod_material_policy='All geometry LODs retain the same native maps; runtime uses the complete native GPU mip chain.',licence='See source licence receipt; no generative processing',status='PRIVATE_CANDIDATE_REQUIRES_SOURCE_AND_ENGINE_VERDICT'))
    receipts.append(rec);(root/'weapon_export_receipts.json').write_text(json.dumps(receipts,indent=2))
    print('P2_WEAPON_PREPARED',row['review_id'],id,flush=True)
