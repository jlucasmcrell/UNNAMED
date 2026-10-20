"""Body-shell boots and gloves for the MetaHuman slice bodies.
blender -b --python build_shells.py -- <masculine|feminine> <outdir>
The shell is cut out of the body's own mesh (bisected, so the edge is a clean loop), keeps the body's
342 vertex groups, and is inflated/solidified. A derived body with the same faces removed is exported
so the skin cannot show through or z-fight.
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

name = sys.argv[sys.argv.index('--')+1]
ROOT = 'D:/Otherreach_Scratch/mh_playtest_20261019'
SRC = f"{ROOT}/runtime/npc/{name}/body_lod0.glb"
out = f"{ROOT}/footwear_lod0/{name}"
for directory in ['blend','glb','tex']:os.makedirs(os.path.join(out,directory),exist_ok=True)
TEX = os.path.join(out, 'tex')

# ---- parameters (metres) ----
BOOT_THICK = 0.0045      # inflate + solidify of the boot shell
SOLE_DROP = 0.006        # flat plantar plane below the lowest skin vertex (+ BOOT_THICK = sole thickness)
TOE_SPRING = 0.008
CUFF_BUMP = 0.003        # rolled collar at the boot top
GLOVE_THICK = 0.002
GLOVE_UP = 0.05          # glove reaches this far above the wrist along the forearm
BOOT_TOP = 0.5           # fraction of ankle->knee height
TILE = 0.30              # metres per leather tile

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
bpy.ops.import_scene.gltf(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
for o in list(bpy.data.objects):
    if o not in (arm, body):
        bpy.data.objects.remove(o)
bones = arm.data.bones
native_rest = [(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in bones]
native_body = [tuple(v.co) for v in body.data.vertices]
head = lambda n: Vector(bones[n].head_local)

ankle_z = head('foot_l').z
knee_z = head('calf_l').z
ZCUT = ankle_z + BOOT_TOP * (knee_z - ankle_z)
gl = {}
for s, sg in (('l', 1), ('r', -1)):
    wrist = head('hand_' + s)
    elbow = head('lowerarm_' + s)
    d = (wrist - elbow).normalized()
    gl[s] = (sg, wrist - d * GLOVE_UP, d)
print('ZCUT', ZCUT, 'glove planes', {k: (tuple(v[1]), tuple(v[2])) for k, v in gl.items()})


def in_glove(c):
    for s, (sg, p, d) in gl.items():
        if c.x * sg > 0.30 and (c - p).dot(d) > 0:
            return True
    return False


def in_patch(c, s):
    sg, p, d = gl[s]
    return c.x * sg > 0.30 and (c - p).dot(d) > -0.10


def cut(bm):
    """Split the body along the boot plane and the two glove planes (glove planes only locally)."""
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces), dist=1e-6,
                           plane_co=(0, 0, ZCUT), plane_no=(0, 0, 1))
    for s in gl:
        sg, p, d = gl[s]
        faces = [f for f in bm.faces if in_patch(f.calc_center_median(), s)]
        geom = set(faces)
        for f in faces:
            geom.update(f.edges)
            geom.update(f.verts)
        bmesh.ops.bisect_plane(bm, geom=list(geom), dist=1e-6, plane_co=p, plane_no=d)


def is_boot(c):
    return c.z < ZCUT


def new_obj(label, bm):
    me = bpy.data.meshes.new(label)
    bm.to_mesh(me)
    ob = bpy.data.objects.new(label, me)
    bpy.context.scene.collection.objects.link(ob)
    for vg in body.vertex_groups:
        ob.vertex_groups.new(name=vg.name)
    return ob


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


# ---------- cut the body once, then split into pieces ----------
base = bmesh.new()
base.from_mesh(body.data)
cut(base)
base.faces.ensure_lookup_table()
print('faces boot', sum(1 for f in base.faces if is_boot(f.calc_center_median())),
      'glove', sum(1 for f in base.faces if in_glove(f.calc_center_median())))


def piece(sel):
    bm = base.copy()
    bm.faces.ensure_lookup_table()
    dele = [f for f in bm.faces if not sel(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=dele, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=2e-5)
    return bm


def derived_body():
    bm = base.copy()
    bm.faces.ensure_lookup_table()
    dele = [f for f in bm.faces if is_boot(f.calc_center_median()) or in_glove(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=dele, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    return bm


def vgidx(n):
    return body.vertex_groups[n].index


# ---------- boots ----------
bb = piece(is_boot)
dl = bb.verts.layers.deform.verify()
bb.verts.ensure_lookup_table()
for s in ('l', 'r'):
    toes = [vgidx(f'{t}_{k}_{s}') for t in ('bigtoe', 'indextoe', 'middletoe', 'ringtoe', 'littletoe') for k in ('01', '02')]
    ball = vgidx('ball_' + s)
    for v in bb.verts:
        w = v[dl]
        tot = 0.0
        for t in toes:
            if t in w:
                tot += w[t]
                del w[t]
        if tot:
            w[ball] = w.get(ball, 0.0) + tot

# replace the separate toes with one rounded toe box swept from the cut ring at the forefoot
for s, sg in (('l', 1), ('r', -1)):
    ball = vgidx('ball_' + s)
    y0 = head('ball_' + s).y + 0.03
    side = lambda c: c.x * sg > 0
    geom = [e for e in bb.edges if side(e.verts[0].co)] + [v for v in bb.verts if side(v.co)] + [f for f in bb.faces if side(f.calc_center_median())]
    bmesh.ops.bisect_plane(bb, geom=geom, dist=1e-6, plane_co=(0, y0, 0), plane_no=(0, 1, 0))
    bb.faces.ensure_lookup_table()
    toe_pts = [v.co.copy() for v in bb.verts if side(v.co) and v.co.y < y0 - 1e-5]
    fw = [f for f in bb.faces if side(f.calc_center_median()) and f.calc_center_median().y < y0]
    bmesh.ops.delete(bb, geom=fw, context='FACES')
    bmesh.ops.delete(bb, geom=[v for v in bb.verts if not v.link_faces], context='VERTS')
    ring = [v for v in bb.verts if side(v.co) and abs(v.co.y - y0) < 1e-4 and v.is_boundary]
    c0 = sum((v.co for v in ring), Vector()) / len(ring)
    ring.sort(key=lambda v: math.atan2(v.co.z - c0.z, v.co.x - c0.x))
    xr = max(v.co.x for v in ring) - min(v.co.x for v in ring)
    zr = max(v.co.z for v in ring) - min(v.co.z for v in ring)
    tip = min(p.y for p in toe_pts)
    stations = []
    tx = (max(p.x for p in toe_pts) + min(p.x for p in toe_pts)) / 2
    N = 10
    for k in range(1, N + 1):
        y = y0 + (tip - y0) * (k / N)
        sl = [p for p in toe_pts if abs(p.y - y) < 0.01] or toe_pts
        f = k / N
        cx = c0.x + (tx - c0.x) * f
        cz = (max(p.z for p in sl) + min(p.z for p in sl)) / 2
        sx = max((max(p.x for p in sl) - min(p.x for p in sl)) / xr, 0.92 - 0.12 * f)
        sz = max((max(p.z for p in sl) - min(p.z for p in sl)) / zr, 0.7)
        stations.append((y, cx, cz, sx, sz))
    # round the tip: shrink the last stations along a quarter circle
    cap = 5
    ylast, cx, cz, sx, sz = stations[-1]
    for j in range(1, cap + 1):
        a = j / cap * math.pi / 2
        stations.append((ylast - 0.022 * math.sin(a), cx, cz, sx * math.cos(a), sz * math.cos(a)))
    rings = [ring]
    for (y, cx, cz, sx, sz) in stations:
        rows = []
        for v in ring:
            nv = bb.verts.new((cx + (v.co.x - c0.x) * sx, y, cz + (v.co.z - c0.z) * sz))
            nv[dl][ball] = 1.0
            rows.append(nv)
        rings.append(rows)
    new_faces = []
    for r0, r1 in zip(rings[:-1], rings[1:]):
        n = len(r0)
        for i in range(n):
            j = (i + 1) % n
            new_faces.append(bb.faces.new((r0[i], r0[j], r1[j], r1[i])))
    tipc = sum((v.co for v in rings[-1]), Vector()) / len(ring)
    tv = bb.verts.new(tipc)
    tv[dl][ball] = 1.0
    n = len(ring)
    for i in range(n):
        new_faces.append(bb.faces.new((rings[-1][i], rings[-1][(i + 1) % n], tv)))
    bmesh.ops.recalc_face_normals(bb, faces=bb.faces[:])
    bmesh.ops.remove_doubles(bb, verts=list(bb.verts), dist=1e-6)
    bb.verts.ensure_lookup_table(); bb.faces.ensure_lookup_table()
    for _ in range(6):
        bmesh.ops.smooth_vert(bb, verts=[v for v in bb.verts if side(v.co) and v.co.y < y0 - 0.002], factor=0.4, use_axis_x=True, use_axis_y=True, use_axis_z=True)

bb.normal_update()
zmin = min(v.co.z for v in bb.verts)
zflat = zmin - SOLE_DROP
for s, sg in (('l', 1), ('r', -1)):
    ball_y = head('ball_' + s).y
    heel_y = max(v.co.y for v in bb.verts if v.co.x * sg > 0)
    for v in bb.verts:
        if v.co.x * sg <= 0 or v.co.z > ankle_z:
            continue
        w = smooth((-v.normal.z - 0.2) / 0.55)
        if w > 0:
            v.co.z += (zflat - v.co.z) * w
            lift = TOE_SPRING * smooth((ball_y + 0.01 - v.co.y) / 0.07) ** 2
            heel = 0.004 * smooth((v.co.y - (heel_y - 0.02)) / 0.02)
            v.co.z += (lift + heel) * w
bb.normal_update()
# rolled collar: bump the top 2.5 cm outward along the normals
for v in bb.verts:
    t = ZCUT - v.co.z
    if 0.003 < t < 0.025:
        v.co += v.normal * CUFF_BUMP * math.sin(math.pi * smooth(t / 0.025))
bb.normal_update()
boots = new_obj(f'{name}_boots', bb)
bb.to_mesh(boots.data)

# ---------- gloves ----------
gb = piece(in_glove)
gb.normal_update()
for v in gb.verts:
    for s, (sg, p, d) in gl.items():
        if v.co.x * sg > 0.30:
            t = (v.co - p).dot(d)
            if 0.002 < t < 0.02:
                v.co += v.normal * 0.0015 * math.sin(math.pi * smooth(t / 0.02))
gloves = new_obj(f'{name}_gloves', gb)
gb.to_mesh(gloves.data)

dbm = derived_body()
dbody = new_obj(f'{name}_body_derived', dbm)
dbm.to_mesh(dbody.data)
for bm in (bb, gb, dbm, base):
    bm.free()


def bind(ob):
    ob.parent = arm
    m = ob.modifiers.new('Armature', 'ARMATURE')
    m.object = arm


for ob, thick in ((boots, BOOT_THICK), (gloves, GLOVE_THICK)):
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    sm = ob.modifiers.new('Solidify', 'SOLIDIFY')
    sm.thickness = thick
    sm.offset = 1.0
    sm.use_even_offset = False
    sm.use_rim = True
    bv = ob.modifiers.new('Bevel', 'BEVEL')
    bv.width = thick * 0.35
    bv.segments = 2
    bv.limit_method = 'ANGLE'
    bv.angle_limit = math.radians(50)
    for m in ('Solidify', 'Bevel'):
        bpy.ops.object.modifier_apply(modifier=m)
    ob.select_set(False)
    for p in ob.data.polygons:
        p.use_smooth = True

for ob in (boots, gloves):
    me = ob.data
    zero = sum(1 for v in me.vertices if not v.groups or sum(g.weight for g in v.groups) < 0.5)
    print(ob.name, 'verts', len(me.vertices), 'polys', len(me.polygons), 'verts with weight<0.5:', zero)


sole_top = None  # set below once zmin is known
bmb = bmesh.new()
bmb.from_mesh(boots.data)
sole_top = zmin - SOLE_DROP - BOOT_THICK + 0.012
bmesh.ops.bisect_plane(bmb, geom=list(bmb.verts) + list(bmb.edges) + list(bmb.faces), dist=1e-6,
                       plane_co=(0, 0, sole_top), plane_no=(0, 0, 1))
bmb.to_mesh(boots.data)
bmb.free()

# ---------- UVs (metric box projection) and materials ----------
def box_uv(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = {0: (1, 2), 1: (0, 2), 2: (0, 1)}[ax]
        for l in f.loops:
            c = l.vert.co
            l[uv].uv = (c[a] / TILE, c[b] / TILE)
    bm.to_mesh(ob.data)
    bm.free()


for ob in (boots, gloves):
    for l in list(ob.data.uv_layers):
        ob.data.uv_layers.remove(l)
    box_uv(ob)

os.makedirs(TEX, exist_ok=True)


def small(src, dst, size=1024):
    if os.path.exists(dst):
        return dst
    im = bpy.data.images.load(src)
    im.scale(size, size)
    im.file_format = 'JPEG'
    im.filepath_raw = dst
    im.save()
    bpy.data.images.remove(im)
    return dst


PH = 'C:/Otherreach_External_Assets/PolyHaven/textures/brown_leather/'
alb = small(PH + 'brown_leather_albedo_4k.jpg', TEX + '/brown_leather_albedo_1k.jpg')
nor = small(PH + 'brown_leather_nor_gl_4k.jpg', TEX + '/brown_leather_nor_gl_1k.jpg')


def leather(matname, tint, rough=0.65):
    m = bpy.data.materials.new(matname)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL':
            nt.nodes.remove(n)
    out_n = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Metallic'].default_value = 0.0
    bsdf.inputs['Roughness'].default_value = rough
    nt.links.new(bsdf.outputs['BSDF'], out_n.inputs['Surface'])
    ta = nt.nodes.new('ShaderNodeTexImage')
    ta.image = bpy.data.images.load(alb)
    tn = nt.nodes.new('ShaderNodeTexImage')
    tn.image = bpy.data.images.load(nor)
    tn.image.colorspace_settings.name = 'Non-Color'
    nm = nt.nodes.new('ShaderNodeNormalMap')
    nm.inputs['Strength'].default_value = 0.8
    nt.links.new(tn.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
    mix = nt.nodes.new('ShaderNodeMix')
    mix.data_type = 'RGBA'
    mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 1.0
    mix.inputs[7].default_value = tint
    nt.links.new(ta.outputs['Color'], mix.inputs[6])
    nt.links.new(mix.outputs[2], bsdf.inputs['Base Color'])
    return m


mat_boot = leather('Leather_Boot', (0.55, 0.46, 0.40, 1))
mat_sole = leather('Leather_Sole', (0.20, 0.17, 0.15, 1))
mat_glove = leather('Leather_Glove', (0.50, 0.43, 0.38, 1))
boots.data.materials.append(mat_boot)
boots.data.materials.append(mat_sole)
gloves.data.materials.append(mat_glove)
zsole_top = sole_top
for p in boots.data.polygons:
    cz = sum(boots.data.vertices[i].co.z for i in p.vertices) / len(p.vertices)
    if cz < zsole_top - 1e-6:
        p.material_index = 1

for ob in (boots, gloves, dbody):
    bind(ob)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'blend', f'{name}_shells.blend'))


def export(objs, fn):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    for o in objs:
        bpy.context.view_layer.objects.active=o
        tri=o.modifiers.new('Export_Triangles','TRIANGULATE')
        bpy.ops.object.modifier_apply(modifier=tri.name)
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(filepath=os.path.join(out, 'glb', fn), export_format='GLB', use_selection=True,
                              export_skins=True, export_all_influences=True, export_def_bones=False, export_normals=True, export_tangents=True, export_animations=False, export_apply=False,
                              export_image_format='JPEG', export_yup=True, export_morph=False)


export([boots], f'{name}_boots.glb')
export([gloves], f'{name}_gloves.glb')
# The derived body is never exported or staged. Native runtime body remains untouched.
assert native_rest == [(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in bones]
assert native_body == [tuple(v.co) for v in body.data.vertices]
import shutil,json,hashlib
runtime = os.path.join(ROOT,'runtime','wardrobe','shell');os.makedirs(runtime,exist_ok=True)
records=[]
for part in ['boots','gloves']:
 fn=f'{name}_{part}.glb';dest=os.path.join(runtime,fn)
 shutil.copy2(os.path.join(out,'glb',fn),dest)
 records.append(dict(part=part,path=dest,sha256=hashlib.sha256(open(dest,'rb').read()).hexdigest(),native_body_unchanged=True,native_rig_unchanged=True,source_body=SRC,all_influences=True))
open(os.path.join(ROOT,'evidence','shells_lod0_'+name+'.json'),'w').write(json.dumps(records,indent=2))
print('DONE', name)
