"""Generalized source garment builder, restricted to trousers.
Pass an exact native body GLB, output directory and character ID.
The wrapper exports only the garment; never stage the utility derived body.
Native source files are read-only. Historical boots/gloves paths are disabled.
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
PIECES = {'trousers'}
if '--pieces' in argv:
    i = argv.index('--pieces')
    PIECES = set(argv[i + 1].split(','))
    del argv[i:i + 2]
if PIECES != {'trousers'}:
    raise ValueError('Body-shell boots/gloves are rejected. Use native hands and direction-rebound rigged boots.')
# usage: -- <masculine|feminine|path/to/body.glb> <outdir> [name]
src, out = argv[:2]
known = {'masculine': 'mateo', 'feminine': 'ada'}
if src in known:
    raise ValueError('Pass the exact native body GLB from the job catalog, not a historical sex alias')
else:
    SRC = src
    name = argv[2] if len(argv) > 2 else os.path.basename(os.path.dirname(src))
WANT_BG = bool(PIECES & {'boots', 'gloves'})
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
TROUSER_THICK = 0.003    # cloth thickness (solidify)
TROUSER_CALF = 0.005     # outer inflation at the calf
TROUSER_LOOSE = 0.008    # outer inflation at thigh and knee
TROUSER_TUCK = 0.002     # outer inflation where the leg goes into the boot (boot wall is 4.5 mm)
TROUSER_HEM_BELOW = 0.03 # hem sits this far below the boot top, inside the boot
TROUSER_TILE = 0.35
TROUSER_RELAX = 12       # laplacian passes over the whole trouser surface
TROUSER_RELAX_CROTCH = 40

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
bpy.ops.import_scene.gltf(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
for o in list(bpy.data.objects):
    if o not in (arm, body):
        bpy.data.objects.remove(o)
bones = arm.data.bones
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


os.makedirs(TEX, exist_ok=True)


def bind(ob):
    ob.parent = arm
    m = ob.modifiers.new('Armature', 'ARMATURE')
    m.object = arm


def box_uv(ob, tile=TILE):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = {0: (1, 2), 1: (0, 2), 2: (0, 1)}[ax]
        for l in f.loops:
            c = l.vert.co
            l[uv].uv = (c[a] / tile, c[b] / tile)
    bm.to_mesh(ob.data)
    bm.free()


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


def export(objs, fn):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(filepath=os.path.join(out, 'glb', fn), export_format='GLB', use_selection=True,
                              export_skins=True, export_animations=False, export_apply=False,
                              export_image_format='JPEG', export_yup=True, export_morph=False)



if WANT_BG:
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
    for ob in (boots, gloves):
        for l in list(ob.data.uv_layers):
            ob.data.uv_layers.remove(l)
        box_uv(ob)

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


    export([boots], f'{name}_boots.glb')
    export([gloves], f'{name}_gloves.glb')
    export([dbody], f'{name}_body_derived.glb')


def normalize_weights(bm):
    # some MetaHuman exports carry vertices whose skin weights sum to well under 1; Godot then drags them
    # toward the origin when bones move. Renormalise so every shell vertex sums to 1.
    dlay = bm.verts.layers.deform.verify()
    for v in bm.verts:
        w = v[dlay]
        tot = sum(w.values())
        if tot > 0 and abs(tot - 1.0) > 1e-4:
            for k in list(w.keys()):
                w[k] = w[k] / tot

# ---------- trousers: waist to mid-calf, one continuous surface including crotch and inner thighs ----------
if 'trousers' in PIECES:
    pelvis_z_t = head('pelvis').z
    WAIST = head('spine_01').z          # just above the iliac crest, under the shirt and vest hem
    HEM = ZCUT - TROUSER_HEM_BELOW      # hem ends inside the boot shaft
    thigh_ids = [g.index for g in body.vertex_groups if g.name.startswith(('thigh_', 'calf_', 'foot_', 'ball_'))]  # thigh_l itself carries no weight; the twist/corrective bones do
    tb0 = bmesh.new()
    tb0.from_mesh(body.data)
    dl0 = tb0.verts.layers.deform.verify()

    def vkey(v):
        return (round(v.co.x * 1e4), round(v.co.y * 1e4), round(v.co.z * 1e4))

    def leg_faces(bm, zmax):
        # faces below zmax in a welded island that carries thigh weights, so hands and arms are excluded
        bm.faces.ensure_lookup_table()
        fs = [f for f in bm.faces if f.calc_center_median().z < zmax]
        parent = {f.index: f.index for f in fs}

        def find(a):
            while parent[a] != a:
                parent[a] = parent[parent[a]]
                a = parent[a]
            return a
        first = {}
        for f in fs:
            for v in f.verts:
                k = vkey(v)
                if k in first:
                    parent[find(f.index)] = find(first[k])
                else:
                    first[k] = f.index
        ok = set()
        for f in fs:
            for v in f.verts:
                w = v[dl0]
                if sum(w.get(g, 0.0) for g in thigh_ids) > 0.3:
                    ok.add(find(f.index))
        return {f.index for f in fs if find(f.index) in ok}

    pre = leg_faces(tb0, WAIST + 0.05)
    geom = set()
    for i in pre:
        f = tb0.faces[i]
        geom.add(f)
        geom.update(f.edges)
        geom.update(f.verts)
    bmesh.ops.bisect_plane(tb0, geom=list(geom), dist=1e-6, plane_co=(0, 0, WAIST), plane_no=(0, 0, 1))
    cut(tb0)
    bmesh.ops.bisect_plane(tb0, geom=list(tb0.verts) + list(tb0.edges) + list(tb0.faces), dist=1e-6,
                           plane_co=(0, 0, HEM), plane_no=(0, 0, 1))
    legset = leg_faces(tb0, WAIST + 1e-4)
    keep = {i for i in legset if tb0.faces[i].calc_center_median().z > HEM}
    print('trousers faces', len(keep), 'leg faces removed from body', len(legset))

    tb = tb0.copy()
    tb.faces.ensure_lookup_table()
    bmesh.ops.delete(tb, geom=[f for f in tb.faces if f.index not in keep], context='FACES')
    bmesh.ops.delete(tb, geom=[v for v in tb.verts if not v.link_faces], context='VERTS')
    bmesh.ops.remove_doubles(tb, verts=list(tb.verts), dist=2e-5)
    normalize_weights(tb)

    def rim_edges(bm):
        # open edges that are not the waist or hem rims: these are holes
        zt = max(v.co.z for v in bm.verts)
        zb = min(v.co.z for v in bm.verts)
        return [e for e in bm.edges if e.is_boundary and zb + 1e-3 < (e.verts[0].co.z + e.verts[1].co.z) / 2 < zt - 1e-3]
    print('trousers open non-rim edges before repair:', len(rim_edges(tb)),
          'non-manifold edges:', sum(1 for e in tb.edges if len(e.link_faces) > 2))
    # the body surface is already closed through the crotch (checked above); fill only if a hole ever shows up
    for _ in range(4):
        holes = rim_edges(tb)
        if not holes:
            break
        bmesh.ops.holes_fill(tb, edges=holes, sides=12)
    print('trousers open non-rim edges after repair:', len(rim_edges(tb)))
    bmesh.ops.recalc_face_normals(tb, faces=tb.faces[:])
    tb.normal_update()
    # outward check: face normals should point away from the leg axis
    def away(f):
        c = f.calc_center_median()
        return f.normal.dot(c - Vector((math.copysign(0.1, c.x), head('pelvis').y, c.z)))
    outward = sum(1 for f in tb.faces if away(f) > 0)
    if outward < len(tb.faces) / 2:
        bmesh.ops.reverse_faces(tb, faces=tb.faces[:])
        tb.normal_update()
    print('trousers faces pointing outward:', max(outward, len(tb.faces) - outward), 'of', len(tb.faces))
    # loose-fitting trousers should not carry the body's anatomy: relax the surface (rims stay put),
    # and relax the crotch area much harder. The body under the shell is removed, so nothing can poke through.
    interior = [v for v in tb.verts if not v.is_boundary]
    for _ in range(TROUSER_RELAX):
        bmesh.ops.smooth_vert(tb, verts=interior, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    pz = head('pelvis').z
    crotch = [v for v in interior if abs(v.co.x) < 0.12 and pz - 0.20 < v.co.z < pz + 0.04]
    for _ in range(TROUSER_RELAX_CROTCH):
        bmesh.ops.smooth_vert(tb, verts=crotch, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    tb.normal_update()
    cy = head('pelvis').y
    top = max(v.co.z for v in tb.verts)
    for v in tb.verts:
        z = v.co.z
        o = TROUSER_CALF + (TROUSER_LOOSE - TROUSER_CALF) * smooth((z - (knee_z - 0.12)) / 0.10)
        o = TROUSER_TUCK + (o - TROUSER_TUCK) * smooth((z - ZCUT) / 0.06)
        medial = -math.copysign(1.0, v.co.x) * v.normal.x if abs(v.co.x) > 0.004 else 1.0
        o *= 1.0 - 0.7 * smooth((medial - 0.35) / 0.5)
        t = top - z
        if t < 0.04:
            env = smooth(t / 0.006) * (1.0 - smooth((t - 0.030) / 0.010))
            theta = math.atan2(v.co.y - cy, v.co.x)
            o += env * (0.004 + 0.0015 * math.sin(30 * theta))
        v.co += v.normal * (o - TROUSER_THICK)
    tb.normal_update()
    trousers = new_obj(f'{name}_trousers', tb)
    tb.to_mesh(trousers.data)
    tb.free()

    dtb = tb0.copy()
    dtb.faces.ensure_lookup_table()
    dele = [f for f in dtb.faces if f.index in legset or in_glove(f.calc_center_median())]
    bmesh.ops.delete(dtb, geom=dele, context='FACES')
    bmesh.ops.delete(dtb, geom=[v for v in dtb.verts if not v.link_faces], context='VERTS')
    normalize_weights(dtb)
    dbody_t = new_obj(f'{name}_body_derived_trousers', dtb)
    dtb.to_mesh(dbody_t.data)
    dtb.free()
    tb0.free()

    bpy.context.view_layer.objects.active = trousers
    trousers.select_set(True)
    sm = trousers.modifiers.new('Solidify', 'SOLIDIFY')
    sm.thickness = TROUSER_THICK
    sm.offset = 1.0
    sm.use_even_offset = True
    sm.use_quality_normals = True
    sm.use_rim = True
    bv = trousers.modifiers.new('Bevel', 'BEVEL')
    bv.width = 0.0012
    bv.segments = 1
    bv.limit_method = 'ANGLE'
    bv.angle_limit = math.radians(50)
    for m in ('Solidify', 'Bevel'):
        bpy.ops.object.modifier_apply(modifier=m)
    trousers.select_set(False)
    for p in trousers.data.polygons:
        p.use_smooth = True
    me = trousers.data
    low = [v for v in me.vertices if not v.groups or sum(g.weight for g in v.groups) < 0.5]
    if low:
        print('DIAG low-weight verts z range', min(v.co.z for v in low), max(v.co.z for v in low), 'x range', min(v.co.x for v in low), max(v.co.x for v in low),
              'no groups:', sum(1 for v in low if not v.groups))
    print('DIAG validate (True = had problems):', me.validate(verbose=True))
    print(trousers.name, 'verts', len(me.vertices), 'polys', len(me.polygons), 'verts with weight<0.5:',
          sum(1 for v in me.vertices if not v.groups or sum(g.weight for g in v.groups) < 0.5))
    for l in list(me.uv_layers):
        me.uv_layers.remove(l)
    box_uv(trousers, TROUSER_TILE)

    mt = bpy.data.materials.new('Cloth_Trouser')
    mt.use_nodes = True
    nt = mt.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL':
            nt.nodes.remove(n)
    out_n = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Metallic'].default_value = 0.0
    bsdf.inputs['Roughness'].default_value = 0.8
    nt.links.new(bsdf.outputs['BSDF'], out_n.inputs['Surface'])
    ta = nt.nodes.new('ShaderNodeTexImage')
    ta.image = bpy.data.images.load(TEX + '/trouser_wool_albedo_1k.jpg')
    tn = nt.nodes.new('ShaderNodeTexImage')
    tn.image = bpy.data.images.load(TEX + '/trouser_wool_nor_folds_1k.png')
    tn.image.colorspace_settings.name = 'Non-Color'
    nm = nt.nodes.new('ShaderNodeNormalMap')
    nm.inputs['Strength'].default_value = 1.0
    nt.links.new(tn.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
    mix = nt.nodes.new('ShaderNodeMix')
    mix.data_type = 'RGBA'
    mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 1.0
    mix.inputs[7].default_value = (0.25, 0.21, 0.16, 1)   # dark brown-charcoal wool from the neutral grey herringbone
    nt.links.new(ta.outputs['Color'], mix.inputs[6])
    nt.links.new(mix.outputs[2], bsdf.inputs['Base Color'])
    trousers.data.materials.append(mt)
    bind(trousers)
    bind(dbody_t)
    export([trousers], f'{name}_trousers.glb')
    export([dbody_t], f'{name}_body_derived_trousers.glb')

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'blend', f'{name}_shells.blend' if WANT_BG else f'{name}_trousers.blend'))
print('DONE', name)
