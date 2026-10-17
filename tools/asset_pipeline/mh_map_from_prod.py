"""A retarget map onto the MetaHuman game skeleton from one onto the production rig: each MetaHuman bone takes the entry of
its production counterpart (same source joint, align, aim, derive); MetaHuman bones without one (spine_02/04, neck_02,
metacarpals, twist and helper joints, sockets, cloth chains) follow their parent at rest. Target fields come from ue5_manny_mh.json.
usage: python mh_map_from_prod.py retarget_maps <prod map> <out name> <source description>"""
import json, os, sys
D, prod_name, out_name, desc = sys.argv[1:5]
prod = json.load(open(os.path.join(D, prod_name)))
mh = json.load(open(os.path.join(D, 'ue5_manny_mh.json')))
T = {'root': 'root', 'pelvis': 'hips', 'spine_01': 'spine', 'spine_03': 'spine_mid', 'spine_05': 'chest', 'neck_01': 'neck', 'head': 'head'}
for s, S in (('l', 'L'), ('r', 'R')):
    T.update({f'clavicle_{s}': f'shoulder.{S}', f'upperarm_{s}': f'upper_arm.{S}', f'lowerarm_{s}': f'forearm.{S}', f'hand_{s}': f'hand.{S}',
              f'thigh_{s}': f'thigh.{S}', f'calf_{s}': f'shin.{S}', f'foot_{s}': f'foot.{S}', f'ball_{s}': f'toe.{S}'})
    for f in ('thumb', 'index', 'middle', 'ring', 'pinky'):
        for k in (1, 2, 3):
            T[f'{f}_0{k}_{s}'] = f'{f}_0{k}.{S}'
out = {k: mh[k] for k in ('map_version', 'target_rig', 'target_hips', 'target_lateral', 'target_legs', 'target_arms', 'target_chain')}
out['source_skeleton'] = prod['source_skeleton']
out['notes'] = (f'{desc} onto the MetaHuman game skeleton, made from {prod_name} (its entry per production bone given to the MetaHuman '
                'bone it stands for); MetaHuman bones the production rig lacks (spine_02/04, neck_02, metacarpals, twist and helper '
                'joints, sockets, eyes, cloth chains) follow their parent at rest.')
out['source_root'] = prod['source_root']
out['source_lateral'] = prod['source_lateral']
out['bones'] = {b: dict(prod['bones'][T[b]]) if b in T and T[b] in prod['bones'] else {'source': None} for b in mh['bones']}
for s_ in 'lr':
    hand = out['bones'][f'hand_{s_}']
    if hand.get('source'):
        hand['drive'] = hand['source']          # aimed at the middle knuckle (target_chain), driven by the hand itself
inv = {v: k for k, v in T.items()}
out['contacts'] = {inv[k]: v for k, v in prod['contacts'].items()}
json.dump(out, open(os.path.join(D, out_name), 'w'), indent=1)
print(out_name, sum(1 for e in out['bones'].values() if e.get('source') or e.get('derive')), 'driven of', len(out['bones']))
