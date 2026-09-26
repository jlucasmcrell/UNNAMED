"""Bake the armour's procedural PBR (steel/rim/brass/leather/cloth kinds) into a 2k atlas and export the rigged GLB.

blender -b --factory-startup -P bake_export.py -- <build.blend> <out_dir> <asset_id> [size] [samples_ao]

Maps: <id>_basecolor.jpg (sRGB), <id>_normal.png (tangent, OpenGL +Y), <id>_orm.jpg (R occlusion, G roughness, B metal).
Sources: ambientCG Rust009 (rust colour/height), ambientCG Leather014 (belt/strap/glove leather) - both CC0 1.0,
extracted from F:\\Otherreach_External_Assets\\materials. Everything else is procedural (Blender noise/voronoi).
"""
import bpy, sys, os, math, json
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUTDIR, AID = argv[0], argv[1], argv[2]
SIZE = int(argv[3]) if len(argv) > 3 else 2048
AO_SAMPLES = int(argv[4]) if len(argv) > 4 else 48
TEX = r"G:\UNNAMED_PHASEB\assets\_staging\armour\tex_src"
LEATHER = r"F:\Otherreach_External_Assets\materials\ambientcg__Leather014"
STAGE = os.path.join(os.path.dirname(SRC), "bake")
os.makedirs(STAGE, exist_ok=True)
os.makedirs(OUTDIR, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=SRC)
sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.device = "CPU"
sc.cycles.samples = 4
sc.render.bake.margin = 12
sc.render.bake.margin_type = "EXTEND"
if sc.world is None:
    sc.world = bpy.data.worlds.new("bakeworld")
sc.world.light_settings.distance = 0.03
BAND_V = 0.07   # trim band (rims, rivets, edges): overlapped UVs, AO is meaningless there

body = next(o for o in bpy.data.objects if o.type == "MESH")
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
me = body.data
for o in bpy.data.objects:
    o.select_set(False)
body.select_set(True)
bpy.context.view_layer.objects.active = body
# bake in the rest pose
body.modifiers["Armature"].show_render = False
body.modifiers["Armature"].show_viewport = False


def new_img(name, colorspace, float_buf=False):
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    im = bpy.data.images.new(name, SIZE, SIZE, alpha=False, float_buffer=float_buf)
    im.colorspace_settings.name = colorspace
    return im


IMG = {
    "ao": new_img("bake_ao", "Non-Color", True),
    "col": new_img("bake_col", "Linear Rec.709", True),
    "rough": new_img("bake_rough", "Non-Color", True),
    "metal": new_img("bake_metal", "Non-Color", True),
    "nrm": new_img("bake_nrm", "Non-Color", True),
}


def load(path, cs):
    im = bpy.data.images.load(path, check_existing=True)
    im.colorspace_settings.name = cs
    return im


RUST_C = load(os.path.join(TEX, "Rust009_2K-JPG_Color.jpg"), "sRGB")
RUST_H = load(os.path.join(TEX, "Rust009_2K-JPG_Displacement.jpg"), "Non-Color")
LEA_C = load(os.path.join(LEATHER, "Leather014_2K-PNG_Color.png"), "sRGB")
LEA_H = load(os.path.join(LEATHER, "Leather014_2K-PNG_Displacement.png"), "Non-Color")
LEA_R = load(os.path.join(LEATHER, "Leather014_2K-PNG_Roughness.png"), "Non-Color")


class NB:
    """Tiny node-building helper."""

    def __init__(self, mat):
        mat.use_nodes = True
        self.nt = mat.node_tree
        self.nt.nodes.clear()
        self.out = self.n("ShaderNodeOutputMaterial")
        self.bake_target = self.n("ShaderNodeTexImage")

    def n(self, kind, **props):
        node = self.nt.nodes.new(kind)
        for k, v in props.items():
            setattr(node, k, v)
        return node

    def link(self, a, b):
        self.nt.links.new(a, b)

    def val(self, v):
        node = self.n("ShaderNodeValue")
        node.outputs[0].default_value = v
        return node.outputs[0]

    def rgb(self, c):
        node = self.n("ShaderNodeRGB")
        node.outputs[0].default_value = (*c, 1)
        return node.outputs[0]

    def math(self, op, a, b=None, clamp=False):
        node = self.n("ShaderNodeMath", operation=op, use_clamp=clamp)
        for i, x in enumerate((a, b)):
            if x is None:
                continue
            if isinstance(x, (int, float)):
                node.inputs[i].default_value = x
            else:
                self.link(x, node.inputs[i])
        return node.outputs[0]

    def mix(self, fac, a, b, blend="MIX"):
        node = self.n("ShaderNodeMix", data_type="RGBA", blend_type=blend)
        for sock, x in ((node.inputs["Factor"], fac), (node.inputs["A"], a), (node.inputs["B"], b)):
            if isinstance(x, (int, float)):
                sock.default_value = x
            elif isinstance(x, tuple):
                sock.default_value = (*x, 1)
            else:
                self.link(x, sock)
        return node.outputs["Result"]

    def fmix(self, fac, a, b):
        node = self.n("ShaderNodeMix", data_type="FLOAT")
        for sock, x in ((node.inputs["Factor"], fac), (node.inputs["A"], a), (node.inputs["B"], b)):
            if isinstance(x, (int, float)):
                sock.default_value = x
            else:
                self.link(x, sock)
        return node.outputs["Result"]

    def ramp(self, x, lo, hi):
        """smoothstep-ish remap of a scalar via Map Range."""
        node = self.n("ShaderNodeMapRange", interpolation_type="SMOOTHSTEP", clamp=True)
        self.link(x, node.inputs["Value"])
        node.inputs["From Min"].default_value = lo
        node.inputs["From Max"].default_value = hi
        return node.outputs["Result"]

    def coords(self):
        tc = self.n("ShaderNodeTexCoord")
        return tc.outputs["Object"]

    def noise(self, vec, scale, detail=4.0, rough=0.55, dim="3D"):
        node = self.n("ShaderNodeTexNoise", noise_dimensions=dim)
        self.link(vec, node.inputs["Vector"])
        node.inputs["Scale"].default_value = scale
        node.inputs["Detail"].default_value = detail
        node.inputs["Roughness"].default_value = rough
        return node.outputs["Fac"]

    def voronoi(self, vec, scale, feature="F1", out="Distance"):
        node = self.n("ShaderNodeTexVoronoi", feature=feature)
        self.link(vec, node.inputs["Vector"])
        node.inputs["Scale"].default_value = scale
        return node.outputs[out]

    def boxtex(self, img, vec, scale, blend=0.3):
        mp = self.n("ShaderNodeMapping")
        self.link(vec, mp.inputs["Vector"])
        mp.inputs["Scale"].default_value = (scale, scale, scale)
        node = self.n("ShaderNodeTexImage", projection="BOX", projection_blend=blend)
        node.image = img
        self.link(mp.outputs["Vector"], node.inputs["Vector"])
        return node

    def ao(self):
        node = self.n("ShaderNodeTexImage")
        node.image = IMG["ao"]
        return node.outputs["Color"]

    def finish(self, color, rough, metal, height, bump_strength=1.0, bump_dist=0.002):
        bsdf = self.n("ShaderNodeBsdfPrincipled")
        self.link(color, bsdf.inputs["Base Color"])
        self.link(rough, bsdf.inputs["Roughness"])
        self.link(metal, bsdf.inputs["Metallic"])
        bump = self.n("ShaderNodeBump")
        bump.inputs["Strength"].default_value = bump_strength
        bump.inputs["Distance"].default_value = bump_dist
        self.link(height, bump.inputs["Height"])
        self.link(bump.outputs["Normal"], bsdf.inputs["Normal"])
        emit = self.n("ShaderNodeEmission")
        emit.inputs["Strength"].default_value = 1.0
        self.sock = {"col": color, "rough": rough, "metal": metal, "bsdf": bsdf.outputs[0], "emit": emit}
        return self


def steel(mat, bright=0.0, rust_amt=1.0, use_ao=True):
    b = NB(mat)
    co = b.coords()
    ao = b.math("POWER", b.ao(), 1.0) if use_ao else b.val(1.0)
    # mottled large-scale tone, fine blotches, hammer dents
    m1 = b.noise(co, 2.6, 8.0, 0.62)
    m2 = b.noise(co, 16.0, 5.0, 0.6)
    dents = b.voronoi(co, 24.0, "SMOOTH_F1")
    # scratch network (voronoi edges, two scales) masked by a noise field, plus fine streaks
    edges = b.voronoi(co, 17.0, "DISTANCE_TO_EDGE")
    scr = b.math("SUBTRACT", 1.0, b.ramp(edges, 0.0, 0.012))
    scr_mask = b.ramp(b.noise(co, 5.0, 2.0), 0.5, 0.64)
    scr = b.math("MULTIPLY", scr, scr_mask)
    edges2 = b.voronoi(co, 41.0, "DISTANCE_TO_EDGE")
    scr2 = b.math("SUBTRACT", 1.0, b.ramp(edges2, 0.0, 0.02))
    scr2 = b.math("MULTIPLY", scr2, b.ramp(b.noise(co, 7.0, 2.0), 0.55, 0.7))
    scr = b.math("MAXIMUM", scr, b.math("MULTIPLY", scr2, 0.6))
    # small dark pits / specks
    pits = b.ramp(b.voronoi(co, 70.0, "F1"), 0.09, 0.03)
    pits = b.math("MULTIPLY", pits, b.ramp(b.noise(co, 3.0, 2.0), 0.4, 0.6))
    mp = b.n("ShaderNodeMapping")
    b.link(co, mp.inputs["Vector"])
    mp.inputs["Scale"].default_value = (70.0, 3.0, 3.0)
    mp.inputs["Rotation"].default_value = (0.4, 0.7, 0.2)
    streak = b.ramp(b.noise(mp.outputs["Vector"], 1.0, 3.0), 0.64, 0.72)
    scr = b.math("MAXIMUM", scr, b.math("MULTIPLY", streak, 0.7))
    # rust in crevices (low AO) and a few blotches
    rust_n = b.noise(co, 5.0, 6.0, 0.7)
    cav = b.math("SUBTRACT", 1.0, ao)
    patch = b.ramp(b.noise(co, 6.5, 3.0), 0.38, 0.68)   # breaks edge rust into patches instead of painted bands
    rust = b.ramp(b.math("ADD", b.math("MULTIPLY", b.math("MULTIPLY", cav, 2.4), patch), b.math("MULTIPLY", rust_n, 0.45)), 0.66, 0.98)
    rust = b.math("MULTIPLY", rust, 0.72 * rust_amt)
    rc = b.boxtex(RUST_C, co, 2.2)
    rh = b.boxtex(RUST_H, co, 2.2)
    # colour
    tone = b.mix(m1, (0.18 + bright, 0.188 + bright, 0.202 + bright), (0.41 + bright, 0.42 + bright, 0.44 + bright))
    tone = b.mix(b.math("MULTIPLY", m2, 0.55), tone, (0.1, 0.1, 0.108), "MULTIPLY")
    tone = b.mix(scr, tone, (0.6, 0.61, 0.63))
    tone = b.mix(b.math("MULTIPLY", pits, 0.8), tone, (0.03, 0.03, 0.032))
    grime = b.ramp(ao, 0.25, 0.9)
    tone = b.mix(b.math("SUBTRACT", 1.0, grime), tone, (0.04, 0.038, 0.036))
    col = b.mix(rust, tone, b.mix(0.7, rc.outputs["Color"], (0.3, 0.16, 0.09), "MULTIPLY"))
    # roughness / metal: satin, not mirror; scratches catch light
    r0 = b.fmix(m1, 0.56 - bright * 0.5, 0.4 - bright * 0.5)
    r0 = b.fmix(b.math("MULTIPLY", m2, 0.5), r0, 0.62)
    r0 = b.fmix(scr, r0, 0.28)
    r0 = b.fmix(pits, r0, 0.75)
    r0 = b.fmix(b.math("SUBTRACT", 1.0, grime), r0, 0.68)
    rough = b.fmix(rust, r0, 0.88)
    metal = b.math("SUBTRACT", 1.0, b.math("MULTIPLY", rust, 0.92))
    metal = b.math("SUBTRACT", metal, b.math("MULTIPLY", b.math("SUBTRACT", 1.0, grime), 0.25), True)
    # height: dents (shallow), scratches (grooves), rust pitting
    h = b.math("MULTIPLY", dents, 0.5)
    h = b.math("SUBTRACT", h, b.math("MULTIPLY", scr, 0.35))
    h = b.math("SUBTRACT", h, b.math("MULTIPLY", pits, 0.4))
    h = b.math("ADD", h, b.math("MULTIPLY", b.math("MULTIPLY", rh.outputs["Color"], rust), 0.9))
    return b.finish(col, rough, metal, h, bump_strength=0.55, bump_dist=0.0015)


def brass(mat):
    b = NB(mat)
    co = b.coords()
    ao = b.ao()
    m1 = b.noise(co, 9.0, 5.0)
    tone = b.mix(m1, (0.42, 0.27, 0.08), (0.62, 0.44, 0.16))
    grime = b.ramp(ao, 0.3, 0.95)
    tone = b.mix(b.math("SUBTRACT", 1.0, grime), tone, (0.08, 0.06, 0.03))
    rough = b.fmix(m1, 0.42, 0.3)
    metal = b.val(1.0)
    return b.finish(tone, rough, metal, b.math("MULTIPLY", m1, 0.2), 0.3)


def leather(mat):
    b = NB(mat)
    co = b.coords()
    ao = b.ao()
    lc = b.boxtex(LEA_C, co, 3.0)
    lh = b.boxtex(LEA_H, co, 3.0)
    lr = b.boxtex(LEA_R, co, 3.0)
    col = b.mix(0.55, lc.outputs["Color"], (0.2, 0.1, 0.05), "MULTIPLY")
    col = b.mix(b.math("SUBTRACT", 1.0, b.ramp(ao, 0.3, 0.95)), col, (0.03, 0.02, 0.012))
    rough = b.fmix(0.5, lr.outputs["Color"], 0.7)
    return b.finish(col, rough, b.val(0.0), lh.outputs["Color"], 0.6, 0.002)


def cloth(mat):
    b = NB(mat)
    co = b.coords()
    ao = b.ao()
    attr = b.n("ShaderNodeAttribute", attribute_name="rib", attribute_type="GEOMETRY")
    rib = b.math("ABSOLUTE", b.math("SINE", b.math("MULTIPLY", attr.outputs["Fac"], math.pi / 0.03)))
    rib = b.math("POWER", rib, 0.45)
    weave = b.noise(co, 240.0, 2.0)
    m1 = b.noise(co, 7.0, 4.0)
    tone = b.mix(m1, (0.03, 0.027, 0.024), (0.055, 0.048, 0.04))
    tone = b.mix(b.math("SUBTRACT", 1.0, rib), tone, (0.012, 0.011, 0.01))
    tone = b.mix(b.math("SUBTRACT", 1.0, b.ramp(ao, 0.3, 0.95)), tone, (0.008, 0.007, 0.006))
    h = b.math("ADD", b.math("MULTIPLY", rib, 1.0), b.math("MULTIPLY", weave, 0.08))
    return b.finish(tone, b.val(0.9), b.val(0.0), h, 0.8, 0.004)


# ---------------------------------------------------------------- pass 1: AO
kinds = {m.name: m for m in me.materials}
for m in me.materials:
    nb = NB(m)
    nb.bake_target.image = IMG["ao"]
    nb.nt.nodes.active = nb.bake_target
    bsdf = nb.n("ShaderNodeBsdfPrincipled")
    nb.link(bsdf.outputs[0], nb.out.inputs["Surface"])
sc.cycles.samples = AO_SAMPLES
# Each rigid piece occludes only itself: pieces move against each other in every clip, so rest-pose
# cross-piece occlusion (and the grime/rust it drives) would show as dark bands wherever a joint opens.
co0 = np.empty(len(me.vertices) * 3, dtype=np.float32)
me.vertices.foreach_get("co", co0)
pid = np.zeros(len(me.vertices), dtype=np.int32)
if "piece_id" in me.attributes:
    me.attributes["piece_id"].data.foreach_get("value", pid)
spread = co0.reshape(-1, 3).copy()
spread[:, 0] += pid * 4.0
me.vertices.foreach_set("co", spread.ravel())
me.update()
bpy.ops.object.bake(type="AO", margin=12, use_clear=True)
me.vertices.foreach_set("co", co0)
me.update()
_a = np.empty(SIZE * SIZE * 4, dtype=np.float32)
IMG["ao"].pixels.foreach_get(_a)
_a = _a.reshape(SIZE, SIZE, 4)
_a[: int(BAND_V * SIZE) + 2, :, :3] = 1.0
IMG["ao"].pixels.foreach_set(_a.ravel())
IMG["ao"].update()
print("BAKED ao")

# ---------------------------------------------------------------- pass 2: procedural kinds
builders = {"k_steel": lambda m: steel(m), "k_rim": lambda m: steel(m, bright=0.14, rust_amt=0.0, use_ao=False),
            "k_brass": brass, "k_leather": leather, "k_cloth": cloth}
NBS = {}
for m in me.materials:
    if m.name in builders:
        NBS[m.name] = builders[m.name](m)
    else:  # void: flat dark, still needs a target node
        nb = NB(m)
        bsdf = nb.n("ShaderNodeBsdfPrincipled")
        nb.link(bsdf.outputs[0], nb.out.inputs["Surface"])
        nb.sock = None
        NBS[m.name] = nb


def route(channel, image):
    for name, nb in NBS.items():
        nb.bake_target.image = image
        nb.nt.nodes.active = nb.bake_target
        if nb.sock is None:
            continue
        for l in list(nb.out.inputs["Surface"].links):
            nb.nt.links.remove(l)
        if channel == "bsdf":
            nb.link(nb.sock["bsdf"], nb.out.inputs["Surface"])
        else:
            e = nb.sock["emit"]
            for l in list(e.inputs["Color"].links):
                nb.nt.links.remove(l)
            nb.link(nb.sock[channel], e.inputs["Color"])
            nb.link(e.outputs[0], nb.out.inputs["Surface"])


sc.cycles.samples = 4
for ch, key in (("col", "col"), ("rough", "rough"), ("metal", "metal")):
    route(ch, IMG[key])
    bpy.ops.object.bake(type="EMIT", margin=12, use_clear=True)
    print("BAKED", ch)
route("bsdf", IMG["nrm"])
bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", margin=12, use_clear=True)
print("BAKED normal")


# ---------------------------------------------------------------- write maps
def px(im):
    a = np.empty(SIZE * SIZE * 4, dtype=np.float32)
    im.pixels.foreach_get(a)
    return a.reshape(SIZE, SIZE, 4)


def save(arr, name, fmt, cs, quality=92):
    im = bpy.data.images.new(name, SIZE, SIZE, alpha=False, float_buffer=False)
    im.colorspace_settings.name = cs
    im.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    ext = {"JPEG": ".jpg", "PNG": ".png"}[fmt]
    path = os.path.join(STAGE, name + ext)   # maps stay in staging; the GLB embeds them
    im.filepath_raw = path
    im.file_format = fmt
    try:
        im.save(filepath=path, quality=quality)
    except TypeError:
        im.save()
    im2 = bpy.data.images.load(path, check_existing=False)
    im2.colorspace_settings.name = cs
    im2.name = name
    return im2, path


col = px(IMG["col"])
# the float bake holds scene-linear values; a byte image's pixels are stored as given, so encode to sRGB here
lin = np.clip(col[..., :3], 0.0, 1.0)
col_s = col.copy()
col_s[..., :3] = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(lin, 1 / 2.4) - 0.055)
col_s[..., 3] = 1.0
colimg, colpath = save(col_s, f"{AID}_basecolor", "JPEG", "sRGB")
nrm = px(IMG["nrm"])
nrmimg, nrmpath = save(nrm, f"{AID}_normal", "PNG", "Non-Color")
orm = np.zeros_like(col)
orm[..., 0] = px(IMG["ao"])[..., 0]
orm[..., 1] = px(IMG["rough"])[..., 0]
orm[..., 2] = px(IMG["metal"])[..., 0]
orm[..., 3] = 1.0
ormimg, ormpath = save(orm, f"{AID}_orm", "JPEG", "Non-Color")
print("MAPS", colpath, nrmpath, ormpath)

# ---------------------------------------------------------------- final materials
final = bpy.data.materials.new(f"{AID}_metal")
final.use_nodes = True
nt = final.node_tree
nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputMaterial")
bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
tc = nt.nodes.new("ShaderNodeTexImage"); tc.image = colimg
tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = nrmimg
to = nt.nodes.new("ShaderNodeTexImage"); to.image = ormimg
nt.links.new(tc.outputs["Color"], bsdf.inputs["Base Color"])
sep = nt.nodes.new("ShaderNodeSeparateColor")
nt.links.new(to.outputs["Color"], sep.inputs["Color"])
nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
nm = nt.nodes.new("ShaderNodeNormalMap")
nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
# glTF occlusion goes through the exporter's "glTF Material Output" group
grp = bpy.data.node_groups.get("glTF Material Output")
if grp is None:
    grp = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
    grp.interface.new_socket("Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
gnode = nt.nodes.new("ShaderNodeGroup")
gnode.node_tree = grp
nt.links.new(sep.outputs["Red"], gnode.inputs["Occlusion"])

void = bpy.data.materials.new(f"{AID}_void")
void.use_nodes = True
vb = void.node_tree.nodes["Principled BSDF"]
vb.inputs["Base Color"].default_value = (0.006, 0.0055, 0.005, 1)
vb.inputs["Roughness"].default_value = 0.95
vb.inputs["Metallic"].default_value = 0.0

names = [m.name for m in me.materials]
void_i = names.index("k_void")
idx = np.empty(len(me.polygons), dtype=np.int32)
me.polygons.foreach_get("material_index", idx)
idx = np.where(idx == void_i, 1, 0).astype(np.int32)
me.materials.clear()
me.materials.append(final)
me.materials.append(void)
me.polygons.foreach_set("material_index", idx)
me.update()
for m in list(bpy.data.materials):
    if m.name.startswith("k_") and m.users == 0:
        bpy.data.materials.remove(m)

body.modifiers["Armature"].show_render = True
body.modifiers["Armature"].show_viewport = True
body.name = AID
me.name = AID
for _a in ("rib", "piece_id", "preuv"):
    if _a in me.attributes:
        me.attributes.remove(me.attributes[_a])

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(STAGE, f"{AID}_final.blend"))

# ---------------------------------------------------------------- export
for o in bpy.data.objects:
    o.select_set(o in (body, arm))
glb = os.path.join(OUTDIR, f"{AID}_rigged.glb")
bpy.ops.export_scene.gltf(
    filepath=glb, export_format="GLB", use_selection=True, export_yup=True, export_apply=False,
    export_skins=True, export_animations=False, export_tangents=True, export_normals=True, export_texcoords=True,
    export_materials="EXPORT", export_image_format="AUTO", export_image_quality=92, export_jpeg_quality=92,
    export_vertex_color="NONE", export_attributes=False, export_influence_nb=4, export_def_bones=False,
    export_rest_position_armature=True, export_extras=False, export_lights=False, export_cameras=False)
print("EXPORTED", glb, os.path.getsize(glb))
tris = sum(len(p.vertices) - 2 for p in me.polygons)
print("FINAL tris", tris, "verts", len(me.vertices))
