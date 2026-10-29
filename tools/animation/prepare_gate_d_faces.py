"""Preserve acquired ARKit facial performances as original-rate channel samples."""
import bpy
import json
import sys
from pathlib import Path

root = Path(sys.argv[sys.argv.index('--') + 1])
output = Path(sys.argv[sys.argv.index('--') + 2])
output.mkdir(parents=True, exist_ok=True)
rows = []
for row in json.loads((root / 'inventory.json').read_text()):
    if row['status'] != 'FAILED' or '_face_' not in row['id']:
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=row['source'])
    keys = next(obj.data.shape_keys for obj in bpy.data.objects
                if obj.type == 'MESH' and obj.data.shape_keys)
    actions = list(bpy.data.actions)
    assert actions and keys.animation_data and keys.animation_data.action
    start, end = keys.animation_data.action.frame_range
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    frames = []
    for frame in range(round(start), round(end) + 1):
        bpy.context.scene.frame_set(frame)
        frames.append({key.name: float(key.value) for key in keys.key_blocks if key.name != 'Basis'})
    assert any(max(frame.values()) > .05 for frame in frames), 'Static facial source'
    take = dict(id=row['id'], source=row['source'], fps=fps, seconds=(end-start)/fps, frames=frames)
    path = output / (row['id'] + '.json')
    path.write_text(json.dumps(take, separators=(',', ':')), encoding='utf-8')
    rows.append(dict(id=row['id'], source=row['source'], path=str(path), status='FACIAL_EXPORTED',
                     fps=fps, seconds=take['seconds'], frames=len(frames)))
(output / 'inventory.json').write_text(json.dumps(rows, indent=2), encoding='utf-8')
print('GATE_D_FACIAL_SOURCES_COMPLETE', len(rows))
