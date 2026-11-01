"""Shared native leg fit and explicit source masks, also used as chest underlayers."""
import bpy,bmesh,json
from mathutils.bvhtree import BVHTree
from p2_gear_pose_source import posed_source
from p2_gear_native_layers import default_office
from p2_gear_surface_fit import clear_body_triangles

def fit_leg_geometry(human,row,work,sex):
    obj,audit=posed_source(human,row,work/'game_art_v2/ready'/row['art_id']/(row['art_id']+'.glb'))
    rig=human.objects.rig;body=human.objects.body;bpy.context.view_layer.update()
    deps=bpy.context.evaluated_depsgraph_get();evaluated=body.evaluated_get(deps)
    skin=BVHTree.FromPolygons([evaluated.matrix_world@v.co for v in evaluated.data.vertices],[list(p.vertices) for p in evaluated.data.polygons])
    boot_root='armour_native_boot_v4' if (work/'armour_native_boot_v4/primary/rigged'/('gear_repaired_proof_sabatons__humgen_creator_'+sex)/'creator_part.json').exists() else 'armour_native_boot_v2'
    short_boot=work/boot_root/'primary/rigged'/('gear_repaired_proof_sabatons__humgen_creator_'+sex)/'creator_part.json'
    cuffs=json.loads(short_boot.read_text())['fit_audit']['boot_cuffs_m']
    knees={side:(rig.matrix_world@rig.pose.bones['shin.'+side].head).z for side in ('L','R')}
    adjusted=0
    for vertex in obj.data.vertices:
        side='L' if vertex.co.x>0 else 'R';point,normal,_,distance=skin.find_nearest(vertex.co)
        # All lower-leg pieces share an explicit inside-boot envelope; the outer
        # boot was fitted outside skin +3.5mm trousers +3.5mm boot clearance.
        if vertex.co.z<knees[side]+.06:
            target=point+normal*.0035;blend=max(0,min(1,(knees[side]+.06-vertex.co.z)/.06))
            vertex.co=vertex.co.lerp(target,blend);adjusted+=1
        elif (vertex.co-point).dot(normal)<.0035:
            vertex.co=point+normal*.0035;adjusted+=1
    pants,upper,hem,office_source=default_office(human,work,sex)
    for reference in [pants,*upper]:bpy.data.objects.remove(reference,do_unlink=True)
    masked=[v.index for v in obj.data.vertices if v.co.z<cuffs['L' if v.co.x>0 else 'R']-.04 or v.co.z>hem+.08]
    masked_set=set(masked);source_mask=sorted({audit['source_vertex_correspondence'][i] for i in masked})
    edit=bmesh.new();edit.from_mesh(obj.data);edit.verts.ensure_lookup_table()
    bmesh.ops.delete(edit,geom=[f for f in edit.faces if all(v.index in masked_set for v in f.verts)],context='FACES_ONLY')
    bmesh.ops.delete(edit,geom=[v for v in edit.verts if not v.link_faces],context='VERTS')
    edit.to_mesh(obj.data);edit.free();obj.data.update()
    body_clearance=clear_body_triangles(obj,skin,diagnostic=work/'armour_native'/('BODY_FIT_FAILURE_'+row['review_id']+'_'+sex+'.json'))
    audit.update(clearance_between_layers_m=.0035,inside_boot_overlap_m=.04,
        reference_shortest_boot=str(short_boot),masked_prepared_source_vertices=masked,masked_original_source_vertices=source_mask,
        undershirt_overlap_m=.08,undershirt_hem_m=hem,undershirt_source=str(office_source),
        body_triangle_clearance=body_clearance,
        weights='HumGen recalculated from current native body; source skin not exported')
    return obj,audit,adjusted
