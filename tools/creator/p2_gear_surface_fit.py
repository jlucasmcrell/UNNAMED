"""Preserve real source surfaces while resolving curved-body triangle chords."""
import bmesh,json
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def clear_body_triangles(obj,skin,clearance=.0035,diagnostic=None,refit=None,open_bores=None,project_to_surface=True,triangulate_refined=False,max_steps=6,minimum_gap=0,local_only=False,edge_limit=None):
    history=[]
    for step in range(max_steps):
        if refit:refit()
        obj.data.update()
        tree=BVHTree.FromPolygons([v.co.copy() for v in obj.data.vertices],[list(p.vertices) for p in obj.data.polygons])
        overlaps=tree.overlap(skin)
        near_faces=set()
        if minimum_gap:
            positions=[v.co.copy() for v in obj.data.vertices]
            for face in obj.data.polygons:
                points=[positions[i] for i in face.vertices]
                probes=points+[sum(points,Vector())/len(points)]+[(a+b)*.5 for a,b in zip(points,points[1:]+points[:1])]
                if any(skin.find_nearest(point)[3]<minimum_gap-1e-6 for point in probes):near_faces.add(face.index)
        history.append(dict(step=step,vertices=len(obj.data.vertices),intersections=len(overlaps),near_clearance_faces=len(near_faces)))
        print('P2_BODY_SURFACE_FIT',obj.name,history[-1],flush=True)
        if not overlaps and not near_faces:return history
        if step==max_steps-1:break
        faces={a for a,b in overlaps}|near_faces
        if open_bores and open_bores(faces):continue
        edit=bmesh.new();edit.from_mesh(obj.data);edit.faces.ensure_lookup_table()
        active_vertices={vertex for index in faces for vertex in edit.faces[index].verts}
        edges={edge for index in faces for edge in edit.faces[index].edges if edge.calc_length()>(edge_limit if edge_limit is not None else (.008 if minimum_gap else .012))}
        if edges:
            subdivision=bmesh.ops.subdivide_edges(edit,edges=list(edges),cuts=1,use_grid_fill=True)
            active_vertices.update(element for name in ('geom_inner','geom_split') for element in subdivision.get(name,[]) if isinstance(element,bmesh.types.BMVert))
        if triangulate_refined:bmesh.ops.triangulate(edit,faces=[f for f in edit.faces if len(f.verts)>3])
        # Interpolation keeps the original source texture/UV surface. These
        # extra vertices let a cloth face follow a curved hip rather than cut
        # through it between otherwise correctly cleared original corners.
        for vertex in edit.verts:
            if not project_to_surface or (local_only and vertex not in active_vertices):continue
            point,normal,_,distance=skin.find_nearest(vertex.co)
            if distance<.15 and (vertex.co-point).dot(normal)<clearance+.0003:
                vertex.co=point+normal*(clearance+.0003)
            if step>0 and abs(vertex.co.x)<.06 and vertex.co.y>.05:
                # A trouser's centre-back seam bridges the buttock cleft. The
                # nearest skin triangle there alternates between the cheeks
                # and draws the cloth into the cleft. Fit that small posterior
                # band over the measured outer skin profile instead.
                back=[]
                for dx in (-.04,-.02,0,.02,.04):
                    hit,_,_,_=skin.ray_cast(Vector((vertex.co.x+dx,vertex.co.y+.5,vertex.co.z)),Vector((0,-1,0)))
                    if hit is not None:back.append(hit.y)
                if back:vertex.co.y=max(vertex.co.y,max(back)+clearance+.0003)
        edit.to_mesh(obj.data);edit.free()
    if diagnostic:
        details=[]
        for index in sorted({a for a,b in overlaps}|near_faces)[:100]:
            face=obj.data.polygons[index];points=[obj.data.vertices[i].co.copy() for i in face.vertices]
            center=sum(points,Vector())/len(points)
            point,normal,_,distance=skin.find_nearest(center)
            details.append(dict(face=index,source_face=obj.data.attributes['p2_source_face'].data[index].value if obj.data.attributes.get('p2_source_face') else None,points=[list(p) for p in points],center=list(center),nearest_body=list(point),body_normal=list(normal),distance=distance,signed_gap=(center-point).dot(normal)))
        diagnostic.write_text(json.dumps(dict(history=history,faces=details),indent=2))
    raise AssertionError(('Body triangle clearance did not converge',obj.name,history))
