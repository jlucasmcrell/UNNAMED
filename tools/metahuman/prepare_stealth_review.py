from pathlib import Path
import shutil,json,yaml,subprocess
root=Path(r'D:\Otherreach_Scratch\rcd5_20261021')
source=Path(r'C:\Otherreach_Build\playtest_mh_rcd1_owner_wt')
dest=root/'review_content'
dest.mkdir(exist_ok=False)
for name in subprocess.check_output(['git','-C',str(source),'ls-files','content'],text=True).splitlines():
    p=Path(name);out=dest/p.relative_to('content');out.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source/p,out)
p=dest/'config/inventory.yaml'
p.write_text(p.read_text(encoding='utf-8')+'\n  - { item_ref: item.weapon.iron_dagger, count: 1 }\n  - { item_ref: item.consumable.poison_flask, count: 1 }\n',encoding='utf-8')
p=dest/'effects/coating_venom.yaml';p.write_text(p.read_text(encoding='utf-8').replace('applies_on_hit: effect.venom','applies_on_hit: effect.root'),encoding='utf-8')
index=0
for p in (dest/'spawns').rglob('*.yaml'):
    d=yaml.safe_load(p.read_text(encoding='utf-8'))
    if d.get('region_ref')!='region.starting_frontier':continue
    key=d['id'];index+=1
    if key=='spawn.hollow.valley_strays':
        d['creatures']=[dict(creature_ref='creature.beast.wolf_grey',count=[1,1],role='sleeper')];xy=[610,755];d['level_band']=[2,2]
    elif key=='spawn.hollow.boar_wallow':
        d['creatures']=[dict(creature_ref='creature.beast.bristleback_boar',count=[1,1],role='sentinel')];xy=[624,755]
    else:xy=[1100+index*3,1100]
    d['at']={'position_m':xy,'radius_m':0}
    p.write_text(yaml.safe_dump(d,sort_keys=False),encoding='utf-8')
(dest/'RCD5_REVIEW_FIXTURE.txt').write_text('ISOLATED REVIEW ONLY. Ordinary commands; dagger/flask boot kit; sleeper wolf at610,755; sentinel boar624,755; existing coating delivers root. NOT owner content.\n',encoding='utf-8')
print(dest)
