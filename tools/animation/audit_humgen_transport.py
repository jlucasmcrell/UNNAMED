"""Audit native glTF rest/hierarchy and sample actual retargeted global motion (CPU only)."""
import argparse
import json
import struct
from pathlib import Path
import numpy as np


class Glb:
    def __init__(self, path):
        data = Path(path).read_bytes()
        size = struct.unpack_from('<I', data, 12)[0]
        self.doc = json.loads(data[20:20 + size])
        self.binary = data[28 + size:]
        self.nodes = self.doc['nodes']
        self.joints = self.doc['skins'][0]['joints']
        self.names = {self.nodes[i]['name']: i for i in self.joints}
        self.parents = {child: i for i, node in enumerate(self.nodes) for child in node.get('children', [])}
        self._accessors = {}

    def accessor(self, index):
        if index in self._accessors:
            return self._accessors[index]
        a = self.doc['accessors'][index]
        v = self.doc['bufferViews'][a['bufferView']]
        dtype = {5126: '<f4', 5125: '<u4', 5123: '<u2', 5121: 'u1'}[a['componentType']]
        width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
        stride = v.get('byteStride', np.dtype(dtype).itemsize * width)
        result = np.ndarray((a['count'], width), dtype=dtype, buffer=self.binary,
                          offset=v.get('byteOffset', 0) + a.get('byteOffset', 0),
                          strides=(stride, np.dtype(dtype).itemsize)).astype(float)
        self._accessors[index] = result
        return result

    def globals(self, at=None):
        overrides = {}
        if at is not None:
            animation = self.doc['animations'][0]
            for channel in animation['channels']:
                sampler = animation['samplers'][channel['sampler']]
                times = self.accessor(sampler['input'])[:, 0]
                values = self.accessor(sampler['output'])
                assert sampler.get('interpolation', 'LINEAR') in ('LINEAR', 'STEP')
                right = min(int(np.searchsorted(times, at, side='right')), len(times) - 1)
                left = max(0, right - 1)
                f = np.clip((at - times[left]) / max(times[right] - times[left], 1e-12), 0, 1)
                if sampler.get('interpolation') == 'STEP':
                    f = 0
                a, b = values[left], values[right]
                if channel['target']['path'] == 'rotation':
                    a, b = a / np.linalg.norm(a), b / np.linalg.norm(b)
                    dot = float(a @ b)
                    if dot < 0:
                        b, dot = -b, -dot
                    if dot < .9995:
                        angle = np.arccos(np.clip(dot, -1, 1))
                        value = (np.sin((1-f)*angle)*a + np.sin(f*angle)*b) / np.sin(angle)
                    else:
                        value = (1-f)*a + f*b
                else:
                    value = (1-f)*a + f*b
                overrides.setdefault(channel['target']['node'], {})[channel['target']['path']] = value
        result = {}

        def world(i):
            if i in result:
                return result[i]
            n = dict(self.nodes[i], **overrides.get(i, {}))
            if 'matrix' in n:
                m = np.array(n['matrix']).reshape(4, 4).T
            else:
                q = np.array(n.get('rotation', [0, 0, 0, 1]), dtype=float)
                x, y, z, w = q / np.linalg.norm(q)
                m = np.eye(4)
                m[:3, :3] = [[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                              [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                              [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]
                m[:3, :3] *= np.array(n.get('scale', [1, 1, 1]))[None, :]
                m[:3, 3] = n.get('translation', [0, 0, 0])
            result[i] = world(self.parents[i]) @ m if i in self.parents else m
            return result[i]

        return {name: world(i) for name, i in self.names.items()}


def audit(body_path, clips):
    body = Glb(body_path)
    rest = body.globals()
    report = {'body': str(body_path), 'clips': []}
    for clip_path in clips:
        clip = Glb(clip_path)
        assert body.names.keys() == clip.names.keys(), 'Joint vocabulary changed'
        for name in body.names:
            def parent(g, bone):
                p = g.parents.get(g.names[bone])
                return g.nodes[p]['name'] if p in g.joints else None
            assert parent(body, name) == parent(clip, name), (name, 'parent changed')
        target_rest = clip.globals()
        position_error = max(float(np.linalg.norm(rest[n][:3, 3] - target_rest[n][:3, 3])) for n in rest)
        matrix_error = max(float(np.max(np.abs(rest[n] - target_rest[n]))) for n in rest)
        assert position_error < .0001 and matrix_error < .0001, ('Native rest changed', position_error, matrix_error)
        animation = clip.doc['animations'][0]
        end = max(float(clip.accessor(s['input'])[-1, 0]) for s in animation['samplers'])
        # Inspect working/gesture bindings closely; two endpoints suffice for
        # the unbound catalogue here. The native importer checks every key.
        focused = not clip_path.stem.startswith('owned_')
        samples = [clip.globals(t) for t in np.linspace(0, end, 13 if focused else 3)]
        heights = np.array([s['Hips'][1, 3] for s in samples])
        # A valid rest alone missed an imported Y-up reference / Z-up animation
        # mismatch: Epic amplified hip motion to 55–77 metres. Reject that
        # transport before it can replace a working runtime library.
        assert np.max(np.abs(heights)) < 20, ('Implausible absolute hip height', clip_path, heights.tolist())
        if clip_path.stem in ('work_smith', 'work_cook', 'work_write', 'work_carry', 'talk_1', 'talk_2', 'talk_3'):
            assert np.max(np.abs(heights)) < 3, ('Standing performance outside character scale', clip_path, heights.tolist())
        measurements = {}
        for name in ['Head', 'LeftUpperArm', 'LeftLowerArm', 'LeftHand', 'RightHand', 'LeftFoot', 'RightFoot']:
            points = np.array([s[name][:3, 3] - s['Hips'][:3, 3] for s in samples])
            measurements[name] = {'mean_hip_offset_m': points.mean(axis=0).round(4).tolist(),
                                  'range_m': np.ptp(points, axis=0).round(4).tolist(),
                                  'max_displacement_m': round(max(float(np.linalg.norm(a-b)) for a in points for b in points), 4)}
        report['clips'].append({'clip': str(clip_path), 'bones': len(clip.names), 'seconds': end,
                                'rest_position_error_m': position_error, 'rest_matrix_error': matrix_error,
                                'hip_height_range_m': [float(heights.min()), float(heights.max())],
                                'motion': measurements})
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('body', type=Path)
    parser.add_argument('clips', type=Path, nargs='*')
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--report', required=True, type=Path)
    args = parser.parse_args()
    clips = args.clips + ([Path(clip['path']) for clip in json.loads(args.manifest.read_text())['clips']] if args.manifest else [])
    assert clips, 'No native transports supplied'
    result = audit(args.body, clips)
    args.report.write_text(json.dumps(result, indent=2), encoding='utf-8')
    for c in result['clips']:
        print(Path(c['clip']).stem, 'rest error mm', round(c['rest_position_error_m']*1000, 4),
              'hand movement m', c['motion']['LeftHand']['max_displacement_m'], c['motion']['RightHand']['max_displacement_m'],
              'hand mean', c['motion']['LeftHand']['mean_hip_offset_m'], c['motion']['RightHand']['mean_hip_offset_m'])
