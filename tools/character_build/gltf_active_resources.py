"""Retain scene-used glTF render resources without touching geometry buffers/binds."""
def prune(doc):
 reachable=set()
 def walk(i):
  if i in reachable:return
  reachable.add(i)
  for child in doc['nodes'][i].get('children',[]):walk(child)
 for scene in doc['scenes']:
  for node in scene.get('nodes',[]):walk(node)
 def keep(key,used):
  ids=sorted(used);mapping={old:new for new,old in enumerate(ids)};doc[key]=[doc[key][i] for i in ids];return mapping
 meshes=keep('meshes',{doc['nodes'][i]['mesh'] for i in reachable if 'mesh'in doc['nodes'][i]})
 for node in doc['nodes']:
  if 'mesh'in node:
   if node['mesh']in meshes:node['mesh']=meshes[node['mesh']]
   else:node.pop('mesh');node.pop('skin',None)
 materials=keep('materials',{p['material']for m in doc['meshes']for p in m['primitives']if 'material'in p})
 for m in doc['meshes']:
  for p in m['primitives']:
   if 'material'in p:p['material']=materials[p['material']]
 refs=[]
 def texture_refs(value):
  if isinstance(value,dict):
   for key,item in value.items():
    if key.endswith('Texture')and isinstance(item,dict)and 'index'in item:refs.append(item)
    else:texture_refs(item)
  elif isinstance(value,list):
   for item in value:texture_refs(item)
 texture_refs(doc['materials']);textures=keep('textures',{t['index']for t in refs})
 for ref in refs:ref['index']=textures[ref['index']]
 images=keep('images',{t['source']for t in doc['textures']if 'source'in t})
 for t in doc['textures']:
  if 'source'in t:t['source']=images[t['source']]
