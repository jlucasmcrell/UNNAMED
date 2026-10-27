"""Add native preset data and people-safe controls to the existing creator catalogue.

No geometry or image generation: reads the licensed installed HumGen content.
Face/body presets are data over two native retained-key bases, not separate bodies.
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTENT = Path('D:/HumGen Content')
CATALOGUE = ROOT / 'src/Presentation/Art/humgen_creator.json'


def slug(value):
    return re.sub(r'[^a-z0-9]+', '_', value.lower()).strip('_')


def main():
    data = json.loads(CATALOGUE.read_text(encoding='utf-8'))
    controls = {}
    for path in sorted((CONTENT / 'livekeys').rglob('*.npz')):
        if not path.stat().st_size or 'trial' in path.stem.lower():
            continue
        category, *parents, name = path.relative_to(CONTENT / 'livekeys').with_suffix('').parts
        if category == 'body_proportions':
            name = re.sub(r'^(female|male)_', '', name)
        if category not in ('body_proportions', 'face_proportions', 'face_presets', 'special'):
            continue
        group = parents[0] if parents else None
        native = f'{category[0]}_{{{group}}}_{name}' if group else f'{category[0]}_{name}'
        controls[name] = native
    for option in data['faces']:
        source = CONTENT / next(iter(option['paths'].values())).replace('\\', '/')
        option['native'] = json.loads(source.read_text(encoding='utf-8'))
    # All genuine body keys are retained in the assets, but bone-length/eye-placement
    # controls stay hidden: they require refitting the exact-target skeleton and eyes.
    safe_body = {'muscular', 'overweight', 'skinny', 'Waist Thickness', 'Upper Arm Thickness',
                 'Forearm Thickness', 'Thigh Thickness', 'Shin Thickness', 'Neck Thickness',
                 'Shoulder Muscles', 'Chest Muscles', 'Back Muscles', 'Biceps', 'Triceps',
                 'Forearm Muscles', 'Quad Muscles', 'Calves Muscles'}
    rules = {}
    siann = {'jaw_width': (-850, -500, -700), 'chin_width': (-850, -500, -700),
             'chin_size': (-550, -200, -350), 'nose_bridge_width': (-850, -550, -700),
             'nose_tip_width': (-700, -350, -500), 'nose_tip_size': (-650, -300, -450),
             'nose_nostril_flare': (-650, -250, -450), 'cheek_zygomatic_bone': (450, 850, 650),
             'cheek_fullness': (-450, -100, -250), 'eye_height': (-350, -100, -220),
             'eye_width': (0, 200, 100), 'eye_tilt': (150, 400, 250),
             'lip_width': (-500, -200, -350), 'lip_cupid_bow': (100, 400, 250)}
    for people in ('veth', 'orenth', 'kal', 'siann'):
        allowed = {}
        for slider in data['sliders']:
            key, panel = slider['humgen'], slider['panel']
            lo, hi, default = slider['min'], slider['max'], slider['default']
            if panel == 'body':
                if key not in safe_body:
                    continue
                lo, hi = slider['min'], slider['max']
                if people == 'siann':
                    lo, hi, default = (450, 850, 650) if key == 'skinny' else (0, 200, 80) if key in ('muscular', 'overweight') else (-400, -100, -250)
                elif people == 'kal':
                    lo, hi, default = (200, 800, 450) if key in ('muscular', 'overweight') else (0, 250, 50) if key == 'skinny' else (50, 450, 200)
            elif key in ('Eye Depth', 'Eye Distance', 'Eye Height', 'Eye Scale'):
                continue
            elif panel == 'face' and key != 'age' and not key.startswith('skin.'):
                lo, hi = slider['min'], slider['max']
                if people == 'siann':
                    lo, hi, default = siann.get(key, (-180, 180, 0))
                elif people == 'kal' and key in ('jaw_width', 'chin_width', 'cheek_zygomatic_bone'):
                    lo, hi, default = 100, 600, 300
            elif people == 'siann' and key.startswith(('hair.', 'skin.')):
                continue
            allowed[slider['id']] = dict(min=lo, max=hi, default=max(lo, min(hi, default)))
        rules['people.' + people] = allowed
    data['runtime'] = dict(schema=1, bases={'f': 'humgen_creator_female', 'm': 'humgen_creator_male'},
                           keys=controls, people=rules,
                           body_presets=[dict(id='bodyshape.' + option['id'][5:], label=option['label'],
                                              sex=next(iter(option['paths'])), height_cm=option['native']['height']['set'], keys=option['native']['keys']) for option in data['faces']])
    data['comment'] = 'Native HumGen presets and controls. Runtime availability requires the private creator bases, native material sets and exact-target motion; legacy bodies retain their existing path.'
    assert len(data['faces']) == 83, 'Installed face catalogue changed; reconcile the owner count'
    assert all(option.get('native') for option in data['faces'])
    CATALOGUE.write_text(json.dumps(data, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    print('83 native faces/body presets; people controls:', {k: len(v) for k, v in rules.items()})


if __name__ == '__main__':
    main()
