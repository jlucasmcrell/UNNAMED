"""Original CMB-04 metalwork on an unchanged native humanoid skin. No model generation service.

Authoring coordinates are glTF metres, Y up. The native skeleton hierarchy and inverse
bind matrices are copied exactly; no round-trip or rest-pose retarget touches the body.
Outputs are garment-pipeline GLBs and layer sidecars, never installed into shared art.
"""
import argparse
import copy
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation
from scipy.spatial import cKDTree

from glb import Glb


class Garment:
    def __init__(self, body, identifier):
        self.body, self.identifier = body, identifier
        self.positions, self.normals, self.uv, self.joints, self.weights = [], [], [], [], []
        self.faces = [[], [], []]
        self.extra_bones = []
        self.chains = {}
        self.skin = body.doc['skins'][0]
        self.bones = {body.doc['nodes'][n]['name']: i for i, n in enumerate(self.skin['joints'])}
        self.parents = {c: i for i, n in enumerate(body.doc['nodes']) for c in n.get('children', [])}

    def world(self, index):
        node = self.body.doc['nodes'][index]
        if 'matrix' in node:
            matrix = np.array(node['matrix']).reshape(4, 4).T
        else:
            matrix = np.eye(4)
            matrix[:3, :3] = Rotation.from_quat(node.get('rotation', [0, 0, 0, 1])).as_matrix() @ np.diag(node.get('scale', [1]*3))
            matrix[:3, 3] = node.get('translation', [0]*3)
        return self.world(self.parents[index]) @ matrix if index in self.parents else matrix

    def point(self, bone):
        return self.world(self.skin['joints'][self.bones[bone]])[:3, 3]

    def secondary_bone(self, name, parent, point):
        parent_node = self.skin['joints'][self.bones[parent]] if parent in [self.body.doc['nodes'][n]['name'] for n in self.skin['joints']] else None
        if parent_node is not None:
            parent_world = self.world(parent_node)
        else:
            parent_world = next(b['world'] for b in self.extra_bones if b['name'] == parent)
        matrix = parent_world.copy()
        matrix[:3, 3] = point
        self.bones[name] = len(self.skin['joints'])+len(self.extra_bones)
        self.extra_bones.append({'name':name,'parent':parent,'world':matrix,'local':np.linalg.inv(parent_world)@matrix})

    def vertex(self, point, normal, uv, binding):
        index = len(self.positions)
        self.positions.append(point)
        self.normals.append(normal)
        self.uv.append(uv)
        self.joints.append([self.bones[n] for n, _ in binding] + [0]*(4-len(binding)))
        self.weights.append([w for _, w in binding] + [0]*(4-len(binding)))
        return index

    def grid(self, rows, material, binding, closed=False):
        """A loft with analytic face normals accumulated per vertex and an unbroken UV seam."""
        start = len(self.positions)
        height, width = len(rows), len(rows[0])
        points = np.array(rows)
        normals = np.zeros_like(points)
        triangles = []
        for y in range(height-1):
            for x in range(width-1):
                for triangle in (((y, x), (y, x+1), (y+1, x+1)), ((y, x), (y+1, x+1), (y+1, x))):
                    a, b, c = (points[p] for p in triangle)
                    normal = np.cross(b-a, c-a)
                    for p in triangle:
                        normals[p] += normal
                    triangles.extend(start+p[0]*width+p[1] for p in triangle)
        for y in range(height):
            for x in range(width):
                normal = normals[y, x]
                normal /= max(np.linalg.norm(normal), 1e-12)
                self.vertex(points[y, x].tolist(), normal.tolist(), [x/(width-1), y/(height-1)], binding(points[y,x]) if callable(binding) else binding)
        self.faces[material].extend(triangles)

    def torus(self, centre, radius, wire, binding, material=0, axis='y', segments=32, basis=None):
        centre = np.array(centre)
        rows = []
        for v in np.linspace(0, 2*math.pi, 7):
            row = []
            for u in np.linspace(0, 2*math.pi, segments+1):
                p = np.array([(radius+wire*math.cos(v))*math.cos(u), wire*math.sin(v), (radius+wire*math.cos(v))*math.sin(u)])
                if axis == 'z': p = p[[0, 2, 1]]
                if axis == 'x': p = p[[1, 0, 2]]
                if basis is not None: p = basis @ p
                row.append((centre+p).tolist())
            rows.append(row)
        self.grid(rows, material, binding)

    def fit_surface(self, centre):
        if hasattr(self, "fit_reference"):
            return self.fit_reference.fit_surface(centre)
        if not hasattr(self, 'surface_tree'):
            points, normals, bindings = [], [], []
            for index, node in enumerate(self.body.doc['nodes']):
                if 'mesh' not in node or getattr(self, 'fit_mesh_name', 'Body_LOD0') not in node.get('name', '') + self.body.doc['meshes'][node['mesh']].get('name', ''):
                    continue
                transform = self.world(index)
                skin = self.body.doc['skins'][node['skin']]
                for primitive in self.body.doc['meshes'][node['mesh']]['primitives']:
                    at = primitive['attributes']
                    position = self.body.read(at['POSITION'])
                    normal = self.body.read(at['NORMAL']) @ np.linalg.inv(transform[:3, :3])
                    points.extend((np.c_[position, np.ones(len(position))] @ transform.T)[:, :3])
                    normals.extend(normal / np.linalg.norm(normal, axis=1)[:, None])
                    for joints, weights in zip(self.body.read(at['JOINTS_0']).astype(int), self.body.read(at['WEIGHTS_0'])):
                        bindings.append([(self.body.doc['nodes'][skin['joints'][joint]]['name'], float(weight))
                                         for joint, weight in zip(joints, weights) if weight > 0])
            self.surface_points, self.surface_normals, self.surface_bindings = np.array(points), np.array(normals), bindings
            self.surface_tree = cKDTree(self.surface_points)
        _, nearest = self.surface_tree.query(centre)
        normal = self.surface_normals[nearest]
        fitted = np.asarray(centre) + normal*(getattr(self, 'fit_clearance', .016)-np.dot(np.asarray(centre)-self.surface_points[nearest], normal))
        return fitted, self.surface_bindings[nearest], normal

    def save(self, destination, layer):
        doc = {'asset': {'version': '2.0', 'generator': 'OTHERREACH original CMB04 metalwork'},
               'scene': self.body.doc.get('scene', 0), 'scenes': copy.deepcopy(self.body.doc['scenes']),
               'nodes': copy.deepcopy(self.body.doc['nodes']), 'skins': [copy.deepcopy(self.skin)],
               'meshes': [], 'accessors': [], 'bufferViews': [], 'buffers': [{'byteLength': 0}]}
        for node in doc['nodes']:
            for key in ('mesh', 'skin', 'weights', 'extensions'): node.pop(key, None)
        data = bytearray()

        def accessor(values, dtype, kind, component, bounds=False):
            array = np.asarray(values, dtype=dtype)
            data.extend(b'\0'*(-len(data) % 4))
            offset = len(data)
            data.extend(array.tobytes())
            view = len(doc['bufferViews'])
            doc['bufferViews'].append({'buffer': 0, 'byteOffset': offset, 'byteLength': array.nbytes})
            result = {'bufferView': view, 'componentType': component, 'count': len(array), 'type': kind}
            if bounds:
                result.update(min=array.min(0).tolist(), max=array.max(0).tolist())
            doc['accessors'].append(result)
            return len(doc['accessors'])-1

        native_binds = self.body.read(self.skin['inverseBindMatrices'])
        bindings = [native_binds]
        for bone in self.extra_bones:
            parent = next(i for i,n in enumerate(doc['nodes']) if n.get('name') == bone['parent'])
            index = len(doc['nodes'])
            doc['nodes'].append({'name':bone['name'],'matrix':bone['local'].T.reshape(-1).tolist()})
            doc['nodes'][parent].setdefault('children',[]).append(index)
            doc['skins'][0]['joints'].append(index)
            bindings.append(np.linalg.inv(bone['world']).T.reshape(1,16))
        doc['skins'][0]['inverseBindMatrices'] = accessor(np.concatenate(bindings), '<f4', 'MAT4', 5126)
        attributes = {
            'POSITION': accessor(self.positions, '<f4', 'VEC3', 5126, True),
            'NORMAL': accessor(self.normals, '<f4', 'VEC3', 5126),
            'TEXCOORD_0': accessor(self.uv, '<f4', 'VEC2', 5126),
            'JOINTS_0': accessor(self.joints, '<u2', 'VEC4', 5123),
            'WEIGHTS_0': accessor(self.weights, '<f4', 'VEC4', 5126),
        }
        doc['materials'] = [
            {'name': 'Forged blue steel', 'pbrMetallicRoughness': {'baseColorFactor': [.24, .28, .30, 1], 'metallicFactor': .94, 'roughnessFactor': .32}, 'doubleSided': True},
            {'name': 'Polished rolled edge', 'pbrMetallicRoughness': {'baseColorFactor': [.45, .43, .37, 1], 'metallicFactor': .92, 'roughnessFactor': .24}, 'doubleSided': True},
            {'name': 'Oiled strap leather', 'pbrMetallicRoughness': {'baseColorFactor': [.075, .042, .025, 1], 'metallicFactor': 0, 'roughnessFactor': .72}, 'doubleSided': True},
        ]
        primitives = []
        for material, indices in enumerate(self.faces):
            if indices:
                primitives.append({'attributes': attributes, 'indices': accessor(np.array(indices).reshape(-1, 1), '<u4', 'SCALAR', 5125), 'material': material})
        doc['meshes'].append({'name': self.identifier, 'primitives': primitives})
        doc['nodes'].append({'name': self.identifier, 'mesh': 0, 'skin': 0})
        doc['scenes'][doc['scene']]['nodes'].append(len(doc['nodes'])-1)
        doc['buffers'][0]['byteLength'] = len(data)
        out = object.__new__(Glb)
        out.doc, out.bin = doc, data
        path = destination / self.identifier / (self.identifier+'_rigged.glb')
        path.parent.mkdir(parents=True, exist_ok=True)
        out.save(path)
        # A real exported-garment check: bind matrices and named hierarchy remain exactly native, with normalized weights.
        check = Glb(path)
        assert np.array_equal(check.read(check.doc['skins'][0]['inverseBindMatrices'])[:len(native_binds)], native_binds)
        assert np.allclose(np.asarray(self.weights).sum(1), 1)
        assert len(self.positions) > 1000
        meta = {'schema_version': '1', 'id': self.identifier, 'asset_id': self.identifier,
                'body': self.identifier.split('__', 1)[1], 'layer': layer, 'hides': [], 'hides_mask': 0,
                'hide_regions': [], 'chains': self.chains, 'secondary_joints': [b['name'] for b in self.extra_bones], 'source': 'Original OTHERREACH hand-authored metalwork; no generative input or output',
                'native_inverse_binds_exact': True, 'vertices': len(self.positions), 'triangles': sum(map(len, self.faces))//3}
        path.with_name(self.identifier+'_garment.json').write_text(json.dumps(meta, indent=2))
        return meta


def gorget(g):
    neck = g.point('Neck')
    chest = g.point('UpperChest')
    scale = abs(g.point('LeftUpperArm')[0]-g.point('RightUpperArm')[0])/.3468
    binding = [('UpperChest', 1)]
    # A flared shoulder mantle, turned-down lip, short articulated neck stand and an open jaw clearance.
    profile = [(.195, .190, -.055), (.191, .186, -.049), (.163, .168, -.027),
               (.126, .143, .006), (.104, .132, .031), (.103, .131, .049),
               (.105, .133, .053), (.108, .136, .050), (.107, .135, .032),
               (.130, .147, .003), (.167, .172, -.030), (.195, .190, -.055)]
    rows = []
    for rx, rz, y in profile:
        rows.append([[neck[0]+scale*rx*math.cos(a), neck[1]+scale*(y-.018*max(0, math.sin(a))),
                      neck[2]+scale*rz*math.sin(a)*(.78 if math.sin(a)<0 else 1)] for a in np.linspace(0, 2*math.pi, 97)])
    g.grid(rows, 0, binding)
    # The rim and the rear rolled collar edge are separate metal surfaces, not painted contours.
    for y, rx, rz in ((.051, .106, .134), (-.053, .194, .189)):
        roll = []
        for v in np.linspace(0, 2*math.pi, 7):
            roll.append([(neck+scale*np.array([(rx+.0022*math.cos(v))*math.cos(u),
                y-.018*max(0, math.sin(u))+.0022*math.sin(v), (rz+.0022*math.cos(v))*math.sin(u)*(.78 if math.sin(u)<0 else 1)])).tolist()
                for u in np.linspace(0, 2*math.pi, 97)])
        g.grid(roll, 1, binding)
    for a in np.linspace(0, 2*math.pi, 17)[:-1]:
        centre = neck + scale*np.array([.166*math.cos(a), -.030-.018*max(0, math.sin(a)), .171*math.sin(a)*(.78 if math.sin(a)<0 else 1)])
        g.torus(centre, .0028*scale, .0015*scale, binding, 1, segments=12)


def besagews(g):
    for side in ('Left', 'Right'):
        shoulder = g.point(side+'UpperArm')
        sign = 1 if side == 'Left' else -1
        centre = shoulder + np.array([sign*.006, -.063, .157])
        root = 'cloth_gap_besagew_'+side+'_0'
        tip = 'cloth_gap_besagew_'+side+'_1'
        pivot = centre+np.array([0,.109,0])
        g.secondary_bone(root, side+'Shoulder', pivot)
        g.secondary_bone(tip, root, centre)
        g.chains['cloth_gap_besagew'] = {'stiffness':.6,'drag':.45,'gravity':.1,'radius':.012}
        binding = [(tip, 1)]
        rows = []
        for radial in np.linspace(.001, .061, 12):
            row = []
            for angle in np.linspace(0, 2*math.pi, 97):
                dome = .015*(1-(radial/.061)**2)
                flute = .0015*math.cos(angle*12)*(radial/.061)**.6
                row.append((centre+[radial*math.cos(angle), radial*math.sin(angle), dome+flute]).tolist())
            rows.append(row)
        g.grid(rows, 0, binding)
        for radius in (.058, .049, .012):
            g.torus(centre+[0, 0, .003 if radius>.02 else .015], radius, .0018, binding, 1, axis='z', segments=96)
        # Straps disappear over the clavicle; paired geometry is authored, never a mirrored skin transform.
        for dx in (-.015, .015):
            def strap_binding(point):
                weight = float(np.clip((pivot[1]-point[1])/.109, 0, 1))
                return [(root, 1-weight), (tip, weight)]
            g.grid([[(centre+[dx+w, y, z]).tolist() for w in (-.004, .004)] for y, z in ((.045, -.007), (.080, -.025), (.110, -.065), (.105, -.110))], 2, strap_binding)
        for angle in np.linspace(0, 2*math.pi, 9)[:-1]:
            g.torus(centre+[.050*math.cos(angle), .050*math.sin(angle), .009], .0025, .0012, binding, 1, axis='z', segments=12)


def voiders(g):
    for side in ('Left', 'Right'):
        shoulder = g.point(side+'UpperArm')
        sign = 1 if side == 'Left' else -1
        # Alternating four-in-one rows: each ring is real geometry and follows its own local patch weights.
        for row in range(16):
            for column in range(18):
                along = (column+(row % 2)*.5)*.0063
                angle = math.pi*(.17+row/20)
                centre = shoulder + [sign*(along-.044), -.049*math.sin(angle), .049*math.cos(angle)]
                centre, binding, normal = g.fit_surface(centre)
                tangent = np.array([1., 0, 0]) - normal*normal[0]
                tangent /= max(np.linalg.norm(tangent), 1e-6)
                across = np.cross(normal, tangent)
                tilt = .4 if row % 2 else -.4
                tilted = normal*math.cos(tilt) + across*math.sin(tilt)
                basis = np.column_stack([tangent, np.cross(tilted, tangent), tilted])
                g.torus(centre, .0041, .00085, binding, 0, axis='z', segments=12, basis=basis)



def bevor(g):
    neck = g.point('Neck')
    # An open-backed jaw cup; the short front throat plate clears the rotating mandible.
    rows = []
    for y, rx, rz, arc in [(-.060,.100,.103,.78),(-.050,.097,.105,.80),(-.023,.089,.105,.83),
                           (.004,.085,.110,.85),(.025,.082,.114,.82),(.030,.084,.116,.82),
                           (.026,.087,.117,.82),(.001,.091,.113,.85),(-.027,.095,.109,.83),(-.060,.100,.103,.78)]:
        rows.append([(neck+[rx*math.sin(a),y+.015*(1-math.cos(a)),rz*math.cos(a)]).tolist()
                     for a in np.linspace(-arc,arc,129)])
    g.grid(rows,0,[('UpperChest',1)])
    for sign in [-1,1]:
        g.torus(neck+[sign*.071,-.027,.083],.003,.0014,[('UpperChest',1)],1,axis='z',segments=20)


def mail_collar(g, curtain=False):
    neck = g.point('Neck')
    # Alternating interlinked rings; widened lower courses drape over the shoulders.
    courses = 24 if curtain else 9
    for row in range(courses):
        y = .018-row*.006
        flare = max(0, -y-.035)
        rx, rz = .077+flare*.65, .076+flare*.5
        circumference = 2*math.pi*math.sqrt((rx*rx+rz*rz)/2)
        count = int(circumference/.0063)
        for column in range(count):
            a=(column+(row%2)*.5)*2*math.pi/count
            centre=neck+np.array([rx*math.cos(a),y,rz*math.sin(a)])
            tangent=np.array([-math.sin(a),0,math.cos(a)])
            radial=np.array([math.cos(a),0,math.sin(a)])
            tilt=.38 if row%2 else -.38
            normal=radial*math.cos(tilt)+np.array([0,1,0])*math.sin(tilt)
            basis=np.column_stack([tangent,np.cross(normal,tangent),normal])
            upper=float(np.clip((y+.06)/.08,0,1)) if curtain else 0
            binding=[('Neck',upper),('UpperChest',1-upper)] if curtain else [('UpperChest',1)]
            g.torus(centre,.0041,.00085,binding,0,axis='z',segments=12,basis=basis)


def couters(g):
    for side in ['Left','Right']:
        elbow=g.point(side+'LowerArm');sign=1 if side=='Left' else -1
        # Articulated elbow cop outside, separate inner-seam plate inside the bend.
        for face in [-1,1]:
            rows=[]
            for r in np.linspace(.001,1,14):
                row=[]
                for a in np.linspace(0,2*math.pi,97):
                    offset=np.array([sign*.064*r*math.cos(a),.063*r*math.sin(a),face*(.053+.016*(1-r*r))])
                    row.append((elbow+offset).tolist())
                rows.append(row)
            def binding(point):
                w=float(np.clip((sign*(point[0]-elbow[0])+.015)/.03,0,1))
                return [(side+'UpperArm',1-w),(side+'LowerArm',w)]
            g.grid(rows,0,binding)
            g.torus(elbow+[0,0,face*.054],.055,.002,binding(elbow),1,axis='z',segments=64)
        # A narrow retention band encircles the elbow without filling the open flexion seam.
        rows=[]
        for x in [-.012,-.008,.008,.012]:
            rows.append([(elbow+[sign*x,.059*math.cos(a),.052*math.sin(a)]).tolist()
                         for a in np.linspace(0,2*math.pi,129)])
        g.grid(rows,2,[(side+'LowerArm',1)])


def poleyns(g):
    for side in ['Left','Right']:
        knee=g.point(side+'LowerLeg');sign=1 if side=='Left' else -1
        rows=[]
        for r in np.linspace(.001,1,16):
            row=[]
            for a in np.linspace(0,2*math.pi,129):
                wing=max(0,sign*math.cos(a))**7*.046
                row.append((knee+[.060*r*math.cos(a)+sign*wing*r,.080*r*math.sin(a),.071+.021*(1-r*r)]).tolist())
            rows.append(row)
        def binding(point):
            lower=float(np.clip((knee[1]-point[1]+.025)/.05,0,1))
            return [(side+'UpperLeg',1-lower),(side+'LowerLeg',lower)]
        g.grid(rows,0,binding)
        # Only lateral pivots: the back of the knee intentionally remains bare.
        for dy in [-.040,.040]:
            g.torus(knee+[sign*.052,dy,.079],.003,.0015,binding(knee+[0,dy,0]),1,axis='z',segments=20)


def groin_guard(g):
    hip=g.point('Hips');rows=[]
    for y,width,depth in [(-.020,.046,.145),(-.024,.050,.148),(-.055,.048,.150),(-.105,.039,.153),
                          (-.145,.021,.154),(-.151,.012,.153),(-.145,.021,.150),(-.105,.039,.149),
                          (-.055,.048,.146),(-.024,.050,.145)]:
        rows.append([(hip+[x,y,depth+.009*(1-(x/width)**2)]).tolist() for x in np.linspace(-width,width,129)])
    g.grid(rows,0,[('Hips',1)])
    for x in [-.025,.025]:g.torus(hip+[x,-.036,.159],.003,.0014,[('Hips',1)],1,axis='z',segments=20)


def faulds(g):
    hip=g.point('Hips')
    scale=abs(g.point('LeftUpperLeg')[0]-g.point('RightUpperLeg')[0])/.204
    # Four overlapping flared lames above the moving thigh, not a solid rigid cone.
    for lame in range(4):
        top=.135-lame*.032
        rows=[]
        for h in [0,.003,.014,.029,.037]:
            flare=(lame*.032+h)/.13
            rx=(.179+.055*flare)*scale;rz=(.145+.040*flare)*scale
            rows.append([(hip+[rx*math.cos(a),scale*(top-h),rz*math.sin(a)]).tolist()
                         for a in np.linspace(0,2*math.pi,129)])
        g.grid(rows,0,[('Hips',1)])
        for a in [.4,math.pi-.4,math.pi+.4,2*math.pi-.4]:
            g.torus(hip+scale*np.array([(.18+.055*lame/4)*math.cos(a),top-.012,(.146+.04*lame/4)*math.sin(a)]),.003*scale,.0012*scale,[('Hips',1)],1,segments=12)
    # Each tasset follows its own native thigh. Five separate overlapping plates
    # retain their shaped edges at full stride instead of bridging the crotch.
    for side in ['Left','Right']:
        sign=1 if side=='Left' else -1
        for lame in range(5):
            top=.035-lame*.043
            rows=[]
            for h in [0,.003,.018,.038,.050]:
                width=(.058+.004*lame)*scale
                rows.append([(hip+[sign*.13*scale+x,scale*(top-h),scale*(.175+.018*(1-(x/width)**2))]).tolist()
                             for x in np.linspace(-width,width,65)])
            g.grid(rows,0,[(side+'UpperLeg',1)])
            for dx in [-.045,.045]:
                g.torus(hip+scale*np.array([sign*.13+dx,top-.012,.190]),.0026*scale,.0012*scale,[(side+'UpperLeg',1)],1,axis='z',segments=12)


def sabatons(g):
    for side in ['Left','Right']:
        foot=g.point(side+'Foot');toe=g.point(side+'Toes')
        scale=abs(toe[2]-foot[2])/.126
        # Interleaved vamp lames; the toe plates use the existing toe joint.
        for lame in range(9):
            back=foot[2]+scale*(-.071+lame*.032)
            binding=[(side+('Toes' if lame>=6 else 'Foot'),1)]
            rows=[]
            for dz in [0,.003,.014,.029,.036]:
                along=(lame*.032+dz)/.29
                width=scale*(.068+.007*math.sin(along*math.pi))
                top=scale*(.145-.065*along)
                rows.append([[foot[0]+width*math.sin(a),scale*.010+top*math.cos(a),back+scale*dz]
                             for a in np.linspace(-math.pi/2,math.pi/2,65)])
            g.grid(rows,0,binding)
            g.grid([rows[-2],rows[-1]],1,binding)
        # Articulated ankle ring remains separate from the flexing vamp.
        rows=[]
        for y,r in [(.102,.055),(.106,.056),(.137,.057),(.171,.063),(.179,.066),(.183,.064)]:
            rows.append([[foot[0]+scale*r*math.cos(a),scale*y,foot[2]+scale*r*math.sin(a)]
                         for a in np.linspace(0,2*math.pi,97)])
        g.grid(rows,0,[(side+'LowerLeg',1)])


def gauntlets(g):
    for side in ['Left','Right']:
        hand=g.point(side+'Hand');sign=1 if side=='Left' else -1
        scale=np.linalg.norm(g.point(side+'IndexIntermediate')-g.point(side+'IndexProximal'))/.0423
        # Flared open-ended wrist cuff, deliberately short of the elbow crease.
        rows=[]
        for x,ry,rz in [(-.105,.046,.046),(-.101,.047,.047),(-.070,.039,.041),(-.026,.033,.037),(.006,.032,.036),(.011,.034,.038)]:
            rows.append([(hand+scale*np.array([sign*x,ry*math.cos(a),rz*math.sin(a)])).tolist()
                         for a in np.linspace(0,2*math.pi,97)])
        g.grid(rows,0,[(side+'Hand',1)])
        # Dorsal metacarpal plate; the palm remains leather rather than a metal mitten.
        rows=[]
        for x,width,height in [(.002,.035,.026),(.012,.040,.030),(.040,.045,.032),(.063,.042,.028),(.073,.039,.025)]:
            rows.append([(hand+scale*np.array([sign*x,height*math.cos(a),width*math.sin(a)])).tolist()
                         for a in np.linspace(-math.pi/2,math.pi/2,97)])
        g.grid(rows,0,[(side+'Hand',1)])
        for digit,radius in [('Thumb',.014),('Index',.012),('Middle',.013),('Ring',.0115),('Little',.010)]:
            names=[side+digit+n for n in (['Metacarpal','Proximal','Distal'] if digit=='Thumb' else ['Proximal','Intermediate','Distal'])]
            for segment,bone in enumerate(names):
                begin=g.point(bone)
                if segment<2:end=g.point(names[segment+1])
                else:end=begin+(begin-g.point(names[segment-1]))*.72
                direction=(end-begin)/np.linalg.norm(end-begin)
                across=np.cross(direction,np.array([0.,1.,0.]));across/=np.linalg.norm(across)
                dorsal=np.cross(across,direction)
                if dorsal[1]<0:dorsal=-dorsal
                radius_now=radius*scale*(1-segment*.13)
                rows=[]
                for through in [0,.08,.40,.72,.91,1.02]:
                    centre=begin+(end-begin)*through
                    rows.append([(centre+radius_now*(across*math.sin(a)+dorsal*math.cos(a))).tolist()
                                 for a in np.linspace(-math.pi*.56,math.pi*.56,49)])
                g.grid(rows,0,[(bone,1)])
                g.grid([rows[-2],rows[-1]],1,[(bone,1)])

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--body', type=Path, required=True)
    parser.add_argument('--body-id', required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--underlayer', type=Path, help='Native frontier garment for the sewn voider seam; source bytes retained')
    parser.add_argument('--parts', nargs='+', default=['gorget','besagews','voiders'], choices=['gorget','besagews','voiders','bevor','aventail','mail_standard','couters','poleyns','groin_guard','faulds','sabatons','gauntlets'])
    args = parser.parse_args()
    body = Glb(args.body)
    source_hash = hashlib.sha256(args.body.read_bytes()).hexdigest()
    report = {'body': str(args.body), 'body_sha256': source_hash, 'garments': []}
    authors = {'gorget':(gorget,3),'besagews':(besagews,3),'voiders':(voiders,2),'bevor':(bevor,3),
               'aventail':(lambda g:mail_collar(g,True),2),'mail_standard':(mail_collar,2),
               'couters':(couters,3),'poleyns':(poleyns,3),'groin_guard':(groin_guard,3),'faulds':(faulds,3),'sabatons':(sabatons,3),'gauntlets':(gauntlets,3)}
    for part in args.parts:
        author,layer=authors[part]
        garment = Garment(body, 'gap_'+part+'__'+args.body_id)
        if part=='voiders' and args.underlayer:
            reference=Garment(Glb(args.underlayer),'seam_reference')
            reference.fit_mesh_name='Frontier_Shirt_LOD0'
            reference.fit_clearance=.002
            garment.fit_reference=reference
        author(garment)
        report['garments'].append(garment.save(args.out, layer))
    assert hashlib.sha256(args.body.read_bytes()).hexdigest() == source_hash
    (args.out/(args.body_id+'_gap_authoring.json')).write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
