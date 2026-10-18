import json,shutil,sys,os
from pathlib import Path
root=Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
candidate=Path(os.environ.get('OTHERREACH_REGION_CANDIDATE',str(root/'region_candidate')));primary=candidate/'primary'
source=root/'game_candidate/primary'
for name in ('animation','materials','manifests'):
    if not (primary/name).exists():shutil.copytree(source/name,primary/name)
fits=sys.argv[1:] or ['mh_veth_f']
for fit in fits:
    def garment(name):return 'frontier_'+name+'__'+fit
    names=['linen_set','linen_trousers_set','vest_set','vest_trousers_set','jack_set','jack_trousers_set',
           'cap_coif_set','boots_sabatons_set','jack_item','standing_cap','sealed_boots','smith_gloves','wool_cloak']
    for name in names:
        aid=garment(name);shutil.copytree(source/'rigged'/aid,primary/'rigged'/aid,dirs_exist_ok=True)
    maps={}
    for chest,label in [('', 'linen'),('item.armor.hide_vest','vest'),('item.armor.lamework_jack','jack')]:
        for legs in ('','item.armor.wool_trousers'):
            maps[f'set:{chest}|{legs}']=[garment(label+('_trousers' if legs else '')+'_set')]
    for item,mesh in [('hide_cap','standing_cap'),('lame_coif','cap_coif_set'),('hide_boots','sealed_boots'),
                      ('lame_sabatons','boots_sabatons_set'),('smith_gloves','smith_gloves'),('cloak','wool_cloak')]:
        maps['item.armor.'+item]=[garment(mesh)]
    for variant,bodyset,head,feet in [('frontier','vest_trousers_set','standing_cap','sealed_boots'),
                                     ('armour','jack_trousers_set','cap_coif_set','boots_sabatons_set'),
                                     ('base','linen_set',None,None)]:
        outfit=[garment(bodyset)]+([garment(head),garment(feet),garment('smith_gloves'),garment('wool_cloak')] if head else [])
        look={'model':fit,'outfit':outfit,'equipment_outfits':maps}
        options={key:{'player':look} for key in ('player=metahuman_m','player=metahuman_f')}
        (candidate/f'bindings_{fit}_{variant}.json').write_text(json.dumps({'people_by_visual_option':options},indent=2))
    (candidate/f'equipment_{fit}.json').write_text(json.dumps(maps,indent=2))
    print('REGION_OUTFIT_CANDIDATE',fit)
