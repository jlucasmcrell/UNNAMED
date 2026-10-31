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
                self.vertex(points[y, x].tolist(), normal.tolist(), [x/(width-1), y/(height-1)], binding)
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
        if not hasattr(self, 'surface_tree'):
            points, normals, bindings = [], [], []
            for index, node in enumerate(self.body.doc['nodes']):
                if 'mesh' not in node or 'Body_LOD0' not in self.body.doc['meshes'][node['mesh']].get('name', ''):
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
        fitted = np.asarray(centre) + normal*(.016-np.dot(np.asarray(centre)-self.surface_points[nearest], normal))
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
        doc['skins'][0]['inverseBindMatrices'] = accessor(native_binds, '<f4', 'MAT4', 5126)
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
        assert np.array_equal(check.read(check.doc['skins'][0]['inverseBindMatrices']), native_binds)
        assert np.allclose(np.asarray(self.weights).sum(1), 1)
        assert len(self.positions) > 1000
        meta = {'schema_version': '1', 'id': self.identifier, 'asset_id': self.identifier,
                'body': self.identifier.split('__', 1)[1], 'layer': layer, 'hides': [], 'hides_mask': 0,
                'hide_regions': [], 'chains': {}, 'source': 'Original OTHERREACH hand-authored metalwork; no generative input or output',
                'native_inverse_binds_exact': True, 'vertices': len(self.positions), 'triangles': sum(map(len, self.faces))//3}
        path.with_name(self.identifier+'_garment.json').write_text(json.dumps(meta, indent=2))
        return meta


def gorget(g):
    neck = g.point('Neck')
    chest = g.point('UpperChest')
    scale = abs(g.point('LeftUpperArm')[0]-g.point('RightUpperArm')[0])/.3468
    binding = [('UpperChest', 1)]
    # A flared shoulder mantle, turned-down lip, short articulated neck stand and an open jaw clearance.
    profile = [(.148, .126, -.066), (.144, .122, -.060), (.124, .109, -.038),
               (.099, .093, -.016), (.080, .078, .002), (.079, .077, .023),
               (.081, .079, .027), (.084, .082, .024), (.083, .081, .003),
               (.103, .097, -.018), (.128, .113, -.040), (.148, .126, -.066)]
    rows = []
    for rx, rz, y in profile:
        rows.append([[neck[0]+scale*rx*math.cos(a), neck[1]+scale*(y-.018*max(0, math.sin(a))),
                      neck[2]+scale*rz*math.sin(a)] for a in np.linspace(0, 2*math.pi, 97)])
    g.grid(rows, 0, binding)
    # The rim and the rear rolled collar edge are separate metal surfaces, not painted contours.
    for y, rx, rz in ((.025, .082, .080), (-.064, .147, .125)):
        roll = []
        for v in np.linspace(0, 2*math.pi, 7):
            roll.append([(neck+scale*np.array([(rx+.0022*math.cos(v))*math.cos(u),
                y-.018*max(0, math.sin(u))+.0022*math.sin(v), (rz+.0022*math.cos(v))*math.sin(u)])).tolist()
                for u in np.linspace(0, 2*math.pi, 97)])
        g.grid(roll, 1, binding)
    for a in np.linspace(0, 2*math.pi, 17)[:-1]:
        centre = neck + scale*np.array([.128*math.cos(a), -.038-.018*max(0, math.sin(a)), .113*math.sin(a)])
        g.torus(centre, .0028*scale, .0015*scale, binding, 1, segments=12)


def besagews(g):
    for side in ('Left', 'Right'):
        shoulder = g.point(side+'UpperArm')
        sign = 1 if side == 'Left' else -1
        centre = shoulder + np.array([sign*.006, -.063, .157])
        binding = [(side+'Shoulder', 1)]
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
            g.grid([[(centre+[dx+w, y, z]).tolist() for w in (-.004, .004)] for y, z in ((.045, -.007), (.080, -.025), (.110, -.065), (.105, -.110))], 2, binding)
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--body', type=Path, required=True)
    parser.add_argument('--body-id', required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    body = Glb(args.body)
    source_hash = hashlib.sha256(args.body.read_bytes()).hexdigest()
    report = {'body': str(args.body), 'body_sha256': source_hash, 'garments': []}
    for part, author, layer in [('gorget', gorget, 3), ('besagews', besagews, 3), ('voiders', voiders, 2)]:
        garment = Garment(body, 'gap_'+part+'__'+args.body_id)
        author(garment)
        report['garments'].append(garment.save(args.out, layer))
    assert hashlib.sha256(args.body.read_bytes()).hexdigest() == source_hash
    (args.out/(args.body_id+'_gap_authoring.json')).write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
