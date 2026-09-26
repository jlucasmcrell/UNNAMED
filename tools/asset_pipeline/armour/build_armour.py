"""Animated Armour v2 - rigid segmented plate armour built on the old 20-bone skeleton (Route B).

blender -b --factory-startup -P build_armour.py -- <out.blend> [report.json]

The old rigged GLB supplies the armature (names, hierarchy, rest). Its shattered mesh is discarded. Every plate is a
parametric patch in the rest pose, thickened with Solidify (inner shell -> void material), rimmed with a rolled edge
and weighted 100% to the one bone it rides on.
"""
import bpy, bmesh, math, sys, os, json
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(__file__))
import armour_geo as G
from armour_geo import Patch, Frame, Z, cr, sect, smoothstep, lerp

OLD = r"G:\UNNAMED_PHASEB\assets\rigged\creature_animated_armour\creature_animated_armour_rigged.glb"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else r"G:\UNNAMED_PHASEB\assets\_staging\armour\armour_v2_build.blend"
REPORT = argv[1] if len(argv) > 1 else OUT.replace(".blend", "_report.json")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=OLD)
ARM = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for o in list(bpy.data.objects):
    if o.type != "ARMATURE":
        bpy.data.objects.remove(o, do_unlink=True)
ARM.name = "creature_animated_armour_rig"
B = {b.name: (ARM.matrix_world @ b.head_local, ARM.matrix_world @ b.tail_local) for b in ARM.data.bones}

# ---------------------------------------------------------------- materials (build-time kinds; baked later)
M_STEEL = G.mat("k_steel", (0.42, 0.42, 0.44))
M_RIM = G.mat("k_rim", (0.62, 0.62, 0.64))
M_BRASS = G.mat("k_brass", (0.62, 0.45, 0.18))
M_LEATHER = G.mat("k_leather", (0.25, 0.14, 0.08))
M_CLOTH = G.mat("k_cloth", (0.07, 0.065, 0.06))
M_VOID = G.mat("k_void", (0.01, 0.01, 0.01))

THK = 0.006          # plate thickness
PIECES = []          # (object, bone)

# ---------------------------------------------------------------- torso frame
sh_l, sh_r = B["upper_arm.L"][0], B["upper_arm.R"][0]
hp_l, hp_r = B["thigh.L"][0], B["thigh.R"][0]
lat = (sh_l - sh_r); lat.z = 0
lat2 = (hp_l - hp_r); lat2.z = 0
U = (lat.normalized() + lat2.normalized()).normalized()     # character's left
F = U.cross(Z).normalized()                                  # character's front (torso)
O = B["hips"][0].copy(); O.z = 0.0
TF = Frame(O, U, F, Z)
FWD = Vector((0, -1, 0))                                     # legs / head face the travel direction (glTF +Z)


def T(u, v, z):
    return O + U * u + F * v + Z * z


def tuv(p):
    d = p - O
    return d.dot(U), d.dot(F)


# torso section knots: z -> (u_c, v_c, half-width, front, back)
TORSO = [
    (0.80, (0.000, -0.045, 0.262, 0.168, 0.168)),
    (0.86, (0.000, -0.050, 0.255, 0.165, 0.165)),
    (0.95, (0.000, -0.058, 0.236, 0.156, 0.150)),
    (1.03, (0.002, -0.066, 0.212, 0.150, 0.140)),
    (1.12, (0.004, -0.076, 0.200, 0.156, 0.132)),
    (1.25, (0.006, -0.090, 0.218, 0.176, 0.136)),
    (1.40, (0.008, -0.106, 0.246, 0.198, 0.146)),
    (1.52, (0.009, -0.118, 0.262, 0.206, 0.151)),
    (1.62, (0.009, -0.128, 0.258, 0.196, 0.152)),
    (1.70, (0.009, -0.138, 0.232, 0.166, 0.146)),
    (1.765, (0.009, -0.146, 0.178, 0.122, 0.126)),
]


def torso_pt(theta, z, inset=0.0, ridge=0.0, ridge_w=0.16, p=2.3):
    uc, vc, a, f, b = cr(TORSO, z)
    x, y = sect(theta, a - inset, f - inset, b - inset, p)
    y += ridge * math.exp(-(theta / ridge_w) ** 2)
    return T(uc + x, vc + y, z)


def torso_axis(p):
    uc, vc, a, f, b = cr(TORSO, p.z)
    return T(uc, vc, p.z)


def rad(d):
    return math.radians(d)


def plate(name, bone, patch, material, inside, thickness=THK, rims=("loop",), rim_r=0.0035, rivets=None,
          rivet_mat=None, bosses=None, skip=None, attr_along=None):
    """Build a thickened, rimmed plate from a patch. inside: point or closest-point fn (for normal orientation)."""
    patch.calibrate(inside)
    bm = bmesh.new()
    G.build_patch(bm, patch, skip=skip)
    if patch.sign < 0:
        bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
    specs = G.rim_specs(patch, rims, thickness, extra_r=rim_r) if rims else []
    ob = G.to_object(name, bm, bone, material, M_VOID, thickness=thickness,
                     edge=(M_RIM if material in (M_STEEL, M_RIM) else material))
    builders = []
    if specs:
        builders.append((G.rim_builder(specs), M_RIM if material in (M_STEEL, M_RIM) else material))
    if rivets:
        builders.append((G.rivet_builder(patch, rivets), rivet_mat or M_RIM))
    if bosses:
        builders.append((G.boss_builder(bosses), M_BRASS))
    if builders:
        G.add_parts(ob, builders)
    if attr_along is not None:
        set_along(ob, attr_along)
    PIECES.append((ob, bone))
    return ob


def solid(name, bone, bm, material, inside=None):
    """A closed bmesh solid (no thickness added)."""
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    ob = G.to_object(name, bm, bone, material, M_VOID, thickness=0.0)
    PIECES.append((ob, bone))
    return ob


def set_along(ob, axis):
    """Float attribute 'rib' = position along axis (origin, dir): the quilting's stripe coordinate."""
    o, d = axis
    me = ob.data
    at = me.attributes.get("rib") or me.attributes.new("rib", "FLOAT", "POINT")
    vals = [(v.co - o).dot(d) for v in me.vertices]
    at.data.foreach_set("value", vals)


def rows_of(fn, s_list):
    return [(s, t) for s, t in s_list]


# ================================================================ TORSO
# --- breastplate (chest)
def bp_fn(s, t):
    x = 2 * s - 1
    tm = rad(100) - rad(38) * smoothstep(0.45, 1.0, t)
    th = tm * math.copysign(abs(x) ** 1.25, x)
    zb = 1.215 + 0.07 * (abs(th) / rad(100)) ** 1.4
    zt = 1.756 - 0.05 * math.exp(-(th / 0.42) ** 2)
    z = zb + (zt - zb) * t
    return torso_pt(th, z, inset=-0.004, ridge=0.018, ridge_w=0.14, p=2.1)


bp = Patch(bp_fn, 28, 13)
chest_boss = []
for sgn in (-1, 1):
    th, z = sgn * 0.56, 1.625
    p = torso_pt(th, z, inset=-0.004, p=2.1)
    chest_boss.append((p, (p - torso_axis(p)).normalized(), 0.024, 0.014))
bp_rivets = [(s, 0.955) for s in (0.1, 0.18, 0.26, 0.74, 0.82, 0.9)] + [(s, 0.05) for s in (0.12, 0.24, 0.36, 0.64, 0.76, 0.88)]
plate("breastplate", "chest", bp, M_STEEL, torso_axis, rivets=bp_rivets, bosses=chest_boss)

# chevrons on the upper chest (three stacked V's)
def chevrons(bm):
    faces = []
    for k in range(3):
        zc = 1.585 - k * 0.03
        pts, nrm = [], []
        for i in range(9):
            u = (i / 8) * 2 - 1
            th = u * 0.11
            z = zc + 0.03 * abs(u)
            p = torso_pt(th, z, inset=-0.004, ridge=0.018, ridge_w=0.14, p=2.1)
            n = (p - torso_axis(p)).normalized()
            pts.append(p + n * 0.002)
            nrm.append(n)
        faces += G.sweep(bm, pts, nrm, 0.0045, sides=5)
    return faces
G.add_parts(PIECES[-1][0], [(chevrons, M_RIM)])

# --- backplate (chest)
def back_fn(s, t):
    x = 2 * s - 1
    half = rad(102) - rad(44) * smoothstep(0.45, 1.0, t)
    th = math.pi + half * x
    zb = 1.225 + 0.05 * abs(x) ** 1.6
    zt = 1.775 - 0.03 * math.exp(-((th - math.pi) / 0.45) ** 2)
    z = zb + (zt - zb) * t
    return torso_pt(th, z, inset=-0.012, ridge=0.0)


bk = Patch(back_fn, 24, 12)
plate("backplate", "chest", bk, M_STEEL, torso_axis, rivets=[(s, 0.95) for s in (0.15, 0.3, 0.7, 0.85)])

# --- quilted waist (spine): dark padding showing at the waist sides between breastplate and belt
def waist_fn(s, t):
    th = 2 * math.pi * s
    z = 1.08 + (1.42 - 1.08) * t
    return torso_pt(th, z, inset=0.016, p=2.2)


plate("waist_quilt", "spine", Patch(waist_fn, 24, 7, closed_s=True), M_CLOTH, torso_axis, thickness=0.004, rims=None,
      attr_along=(Vector((0, 0, 0)), Z))

# --- belt (hips): sits where the concept's belt is, just under the breastplate
BELT0, BELT1 = 1.125, 1.182


def belt_fn(s, t):
    th = 2 * math.pi * s
    z = BELT0 + (BELT1 - BELT0) * t
    return torso_pt(th, z, inset=-0.02, p=2.2)


belt = Patch(belt_fn, 28, 1, closed_s=True)
buckle = []
pb = torso_pt(rad(38), (BELT0 + BELT1) / 2, inset=-0.028)
buckle.append((pb, (pb - torso_axis(pb)).normalized(), 0.03, 0.012))
plate("belt", "hips", belt, M_LEATHER, torso_axis, thickness=0.007, rims=None, bosses=buckle)

# --- faulds: three lames front/sides + culet at the back (hips); each lame's top tucks under the one above
def fauld(k, back=False):
    z1 = 1.14 - 0.062 * k
    z0 = z1 - 0.078
    half = rad(78) if back else rad(116)
    base = 0.012 + 0.013 * k - (0.004 if back else 0.0)

    def fn(s, t):
        x = 2 * s - 1
        th = (math.pi if back else 0.0) + half * x
        z = z0 + (z1 - z0) * t
        return torso_pt(th, z, inset=-(base + 0.022 * (1 - t)), ridge=(0.0 if back else 0.008), ridge_w=0.2)
    return Patch(fn, 22 if not back else 14, 2)


for k in range(3):
    fp = fauld(k)
    plate(f"fauld_{k}", "hips", fp, M_STEEL, torso_axis, rims=("t0", "s0", "s1"),
          rivets=[(s, 0.72) for s in (0.1, 0.22, 0.34, 0.66, 0.78, 0.9)] if k != 1 else None, rivet_mat=M_BRASS)
    plate(f"culet_{k}", "hips", fauld(k, back=True), M_STEEL, torso_axis, rims=("t0", "s0", "s1"))

# --- central plate hanging from the belt over the faulds (hips)
CP0 = 0.70


def cp_fn(s, t):
    z = CP0 + (BELT0 - CP0) * t
    hw = 0.07 if z > 0.84 else lerp(0.006, 0.07, smoothstep(CP0, 0.84, z) ** 0.8)
    x = (2 * s - 1) * hw
    uc, vc, a, f, b = cr(TORSO, max(z, 0.86))
    r = f + 0.028 + 0.052 * smoothstep(BELT0, 0.94, z)
    th = x / r
    y = r * math.cos(th) + 0.012 * math.exp(-(x / 0.018) ** 2)
    return T(uc + r * math.sin(th), vc + y, z)


cp = Patch(cp_fn, 8, 12)
cp_boss = []
ptop = cp_fn(0.5, 0.93)
cp_boss.append((ptop, F.copy(), 0.02, 0.012))
plate("central_plate", "hips", cp, M_STEEL, lambda p: T(0.0, -0.05, p.z), rims=("loop",),
      rivets=[(0.15, t) for t in (0.35, 0.55, 0.75)] + [(0.85, t) for t in (0.35, 0.55, 0.75)], rivet_mat=M_BRASS, bosses=cp_boss)

# --- quilted trunks (hips): dark padding below the faulds, between the legs
def trunk_fn(s, t):
    th = 2 * math.pi * s
    z = 0.74 + (1.13 - 0.74) * t
    k = lerp(0.78, 1.0, smoothstep(0.0, 0.5, t))
    uc, vc, a, f, b = cr(TORSO, max(z, 0.86))
    x, y = sect(th, (a - 0.03) * k, (f - 0.025) * k, (b - 0.025) * k, 2.2)
    return T(uc + x, vc + y, z)


plate("trunk_quilt", "hips", Patch(trunk_fn, 20, 6, closed_s=True), M_CLOTH, torso_axis, thickness=0.004, rims=None,
      attr_along=(Vector((0, 0, 0)), Z))

# ================================================================ NECK + HEAD
NK = B["neck"][0]
nu, nv = tuv(NK)

# helm frame (faces the travel direction); gorget and collar share its centre so their clearances are uniform
HD = B["head"][0]
HX, HY = Vector((1, 0, 0)), FWD.copy()
HO = Vector((HD.x, HD.y + 0.004, 0.0))


def H(x, y, z):
    return HO + HX * x + HY * y + Z * z


HELM_Z0 = 1.772         # the helm sits above the breastplate's neckline; the gorget fills the neck below it


def gorget_fn(s, t):
    th = 2 * math.pi * s
    z = 1.70 + (1.772 - 1.70) * t
    k = smoothstep(0.0, 1.0, t)
    a, f, b = lerp(0.12, 0.116, k), lerp(0.11, 0.106, k), lerp(0.114, 0.11, k)
    x, y = sect(th, a, f, b, 2.2)
    return H(x, y, z)


gg = Patch(gorget_fn, 24, 3, closed_s=True)
plate("gorget", "neck", gg, M_STEEL, lambda p: H(0, 0, p.z), rims=None,
      rivets=[(s, 0.45) for s in (0.03, 0.1, 0.17, 0.83, 0.9, 0.97)])


def collar_fn(s, t):
    x = 2 * s - 1
    th = math.pi + rad(118) * x
    zt = 1.945 - 0.035 * abs(x) ** 2
    z = 1.80 + (zt - 1.80) * t
    fl = smoothstep(0.0, 1.0, t) ** 1.2
    a, f, b = 0.165 + 0.035 * fl, 0.15 + 0.02 * fl, 0.175 + 0.035 * fl
    xx, yy = sect(th, a, f, b, 2.2)
    return H(xx, yy, z)


plate("collar", "neck", Patch(collar_fn, 20, 4), M_STEEL, lambda p: H(0, 0, p.z), rims=("loop",))

# --- helm (head): great helm facing the travel direction
HELM = [
    (1.772, (0.121, 0.132, 0.124)),
    (1.800, (0.123, 0.146, 0.13)),
    (1.850, (0.124, 0.151, 0.131)),
    (1.900, (0.122, 0.147, 0.13)),
    (1.940, (0.117, 0.136, 0.124)),
    (1.965, (0.106, 0.119, 0.113)),
    (1.985, (0.084, 0.092, 0.09)),
    (1.997, (0.048, 0.052, 0.051)),
    (2.002, (0.004, 0.004, 0.004)),
]
SLIT = (1.884, 1.905)


def helm_pt(th, z):
    a, f, b = cr(HELM, z)
    x, y = sect(th, a * 1.05, f * 1.05, b * 1.05, 2.35)
    prow = 0.016 * math.exp(-(th / 0.30) ** 2) * smoothstep(1.975, 1.91, z) * smoothstep(HELM_Z0, 1.80, z)
    y += prow
    return H(x, y, z)


HZ = [HELM_Z0, 1.79, 1.825, 1.86, SLIT[0], 1.8945, SLIT[1], 1.93, 1.955, 1.972, 1.986, 1.996, 2.002]
TH_COLS = [0.0, 0.055, 0.12, 0.2, 0.3, 0.42, 0.6, 0.78, 0.95, 1.15, 1.4, 1.7, 2.0, 2.35, 2.75]
TH = sorted(set([-t for t in TH_COLS] + TH_COLS + [math.pi]))   # -pi..pi
TH = [t for t in TH if -math.pi < t <= math.pi]


def helm_fn(s, t):
    # s indexes TH (closed), t indexes HZ
    i = s * len(TH)
    i0 = int(math.floor(i)) % len(TH)
    fr = i - math.floor(i)
    a0, a1 = TH[i0], TH[(i0 + 1) % len(TH)]
    if a1 <= a0:
        a1 += 2 * math.pi
    th = a0 + (a1 - a0) * fr
    j = t * (len(HZ) - 1)
    j0 = min(int(math.floor(j)), len(HZ) - 2)
    z = HZ[j0] + (HZ[j0 + 1] - HZ[j0]) * (j - j0)
    return helm_pt(th, z)


def slit_skip(s, t):
    i = int(s * len(TH))
    th = (TH[i] + TH[(i + 1) % len(TH)]) / 2 if TH[(i + 1) % len(TH)] > TH[i] else math.pi
    j = int(t * (len(HZ) - 1))
    zc = (HZ[j] + HZ[j + 1]) / 2
    return SLIT[0] < zc < SLIT[1] and 0.05 < abs(th) < 0.62


hp = Patch(helm_fn, len(TH), len(HZ) - 1, closed_s=True)
side_boss = []
for sgn in (-1, 1):
    p = helm_pt(sgn * math.pi / 2, 1.875)
    side_boss.append((p, (p - H(0, 0, 1.875)).normalized(), 0.024, 0.013))
helm = plate("helm", "head", hp, M_STEEL, lambda p: H(0, 0, min(max(p.z, 1.78), 1.95)), rims=("t0",), skip=slit_skip,
             rivets=[(s / 16, 0.06) for s in range(16)], bosses=side_boss)


def helm_details(bm):
    faces = []
    # brow band above the slit
    pts, nrm = [], []
    for i in range(15):
        th = -1.15 + 2.3 * i / 14
        p = helm_pt(th, 1.915)
        n = (p - H(0, 0, 1.915)).normalized()
        pts.append(p + n * 0.001); nrm.append(n)
    faces += G.sweep(bm, pts, nrm, 0.0055, sides=6)
    # lower band under the slit
    pts, nrm = [], []
    for i in range(15):
        th = -1.15 + 2.3 * i / 14
        p = helm_pt(th, 1.876)
        n = (p - H(0, 0, 1.876)).normalized()
        pts.append(p + n * 0.001); nrm.append(n)
    faces += G.sweep(bm, pts, nrm, 0.0045, sides=6)
    # crest ridge front->top->back
    pts, nrm = [], []
    for i in range(17):
        u = i / 16
        if u < 0.5:
            z = lerp(1.92, 2.002, (u / 0.5) ** 0.7)
            th = 0.0
        else:
            z = lerp(2.002, 1.90, ((u - 0.5) / 0.5) ** 1.4)
            th = math.pi
        p = helm_pt(th, z) if z < 2.0 else H(0, 0, 2.002)
        c = H(0, 0, 1.86)
        n = (p - c).normalized()
        pts.append(p + n * 0.003); nrm.append(n)
    faces += G.sweep(bm, pts, nrm, 0.007, sides=6)
    # vertical bar splitting the slit
    return faces


G.add_parts(helm, [(helm_details, M_RIM)])

# ================================================================ ARMS
for side, sgn in (("L", 1.0), ("R", -1.0)):
    OUTW = U * sgn
    J = B["upper_arm." + side][0]
    E = B["forearm." + side][0]
    W = B["hand." + side][0]

    # --- pauldron cap (shoulder): dome over the shoulder joint, resting on the chest top
    PC = J + Z * (-0.045) + OUTW * 0.015

    def pa_pt(phi, lam, PC=PC, OUTW=OUTW):
        rx = 0.168 if math.cos(phi) >= 0 else 0.19
        ry, rz = 0.172, 0.14
        d = OUTW * (rx * math.cos(lam) * math.cos(phi)) + F * (ry * math.cos(lam) * math.sin(phi)) + Z * (rz * math.sin(lam))
        return PC + d

    def pa_fn(s, t, pa_pt=pa_pt):
        phi = (s * 2 - 1) * math.pi
        g = ((1 - math.cos(phi)) / 2) ** 0.6
        lam_min = rad(0) + rad(38) * g
        lam = lam_min + (rad(84) - lam_min) * t
        return pa_pt(phi, lam)

    pa = Patch(pa_fn, 28, 7, closed_s=True)

    def pa_cap(bm, pa_pt=pa_pt):
        # close the top hole with a fan
        ring = [pa_pt((i / 28 * 2 - 1) * math.pi, rad(84)) for i in range(28)]
        top = pa_pt(0.0, rad(90))
        vs = [bm.verts.new(p) for p in ring]
        tv = bm.verts.new(top)
        return [bm.faces.new((vs[i], vs[(i + 1) % 28], tv)) for i in range(28)]

    pa_boss = []
    for ph in (rad(40), rad(-40)):
        p = pa_pt(ph, rad(-5))
        pa_boss.append((p, (p - PC).normalized(), 0.022, 0.013))
    cap = plate("pauldron_" + side, "shoulder." + side, pa, M_STEEL, PC, rims=("t0",),
                rivets=[(s / 18 + 0.02, 0.16) for s in range(18) if abs(s / 18 - 0.5) > 0.17], rivet_mat=M_BRASS,
                bosses=pa_boss)
    # the fan cap needs thickness too: build it as a separate thin plate on the same bone
    bmc = bmesh.new()
    pa_cap(bmc)
    bmesh.ops.remove_doubles(bmc, verts=bmc.verts, dist=1e-6)
    G.orient(bmc, PC)
    capob = G.to_object("pauldron_top_" + side, bmc, "shoulder." + side, M_STEEL, M_VOID, thickness=THK, edge=M_RIM)
    PIECES.append((capob, "shoulder." + side))

    # --- pauldron lames (upper arm)
    fr_u, Lu = G.limb_frame(J, E, F, OUTW)
    for k in range(3):
        z0 = 0.045 + 0.055 * k
        z1 = z0 + 0.075
        r0 = 0.13 - 0.006 * k

        def lame_fn(s, t, z0=z0, z1=z1, r0=r0, fr=fr_u):
            x = 2 * s - 1
            th = rad(112) * x                          # 0 = outward
            z = z1 + (z0 - z1) * t                     # t=0 bottom edge
            r = r0 + (0.01 if k == 0 else 0.015) * (1 - t)
            xx, yy = math.cos(th) * r, math.sin(th) * r * 1.02
            return fr.p(xx - 0.006, yy, z)

        lp = Patch(lame_fn, 18, 2)
        plate(f"pauldron_lame{k}_{side}", "upper_arm." + side, lp, M_STEEL,
              G.seg_closest(fr_u.p(0, 0, -0.2), fr_u.p(0, 0, 0.4)), rims=("t0", "s0", "s1"),
              rivets=[(s, 0.55) for s in (0.2, 0.35, 0.5, 0.65, 0.8)] if k == 0 else None, rivet_mat=M_BRASS)

    # --- quilted sleeve (upper arm)
    def sleeve_fn(s, t, fr=fr_u, Lu=Lu):
        th = 2 * math.pi * s
        z = -0.03 + (Lu + 0.01) * t
        kk = 1.0 + 0.45 * (1.0 - smoothstep(0.18, 0.29, z))   # padded roll under the pauldron lames
        x, y = sect(th, 0.066 * kk, 0.07 * kk, 0.068 * kk, 2.1)
        return fr.p(y, x, z)

    plate("sleeve_" + side, "upper_arm." + side, Patch(sleeve_fn, 12, 9, closed_s=True), M_CLOTH,
          G.seg_closest(fr_u.p(0, 0, -0.2), fr_u.p(0, 0, 0.6)), thickness=0.004, rims=None,
          attr_along=(J, fr_u.A))

    # leather strap above the elbow (upper arm)
    def strap_fn(s, t, fr=fr_u, Lu=Lu):
        th = 2 * math.pi * s
        z = Lu - 0.115 + 0.03 * t
        x, y = sect(th, 0.073, 0.077, 0.075, 2.1)
        return fr.p(y, x, z)

    plate("armstrap_" + side, "upper_arm." + side, Patch(strap_fn, 12, 1, closed_s=True), M_LEATHER,
          G.seg_closest(fr_u.p(0, 0, -0.2), fr_u.p(0, 0, 0.6)), thickness=0.005, rims=None)

    # --- couter (upper arm): dome over the elbow's back-outer side + a wing to the front
    CE = E - F * 0.012 + OUTW * 0.01
    Dc = (-F * 0.55 + OUTW * 0.84).normalized()

    def dome_fn_factory(center, D, r, half, flat=1.0, ref=F, squash=None):
        t1 = (ref - D * ref.dot(D)).normalized()
        t2 = D.cross(t1)

        def fn(s, t):
            phi = 2 * math.pi * s
            beta = half * (1 - t) + 0.001
            d = D * math.cos(beta) + (t1 * math.cos(phi) + t2 * math.sin(phi)) * math.sin(beta)
            # flatten along D
            dd = d.dot(D)
            d = d - D * dd * (1 - flat)
            if squash is not None and d.dot(squash[0]) > 0:
                d = d - squash[0] * d.dot(squash[0]) * (1 - squash[1])
            return center + d * r
        return fn

    # upper half-dome (toward the upper arm) + a skirt down the forearm's back-outer side; the vambrace top sits inside
    Af = (W - E).normalized()
    ct1 = (-Af - Dc * (-Af).dot(Dc)).normalized()
    ct2 = Dc.cross(ct1)

    def cou_fn(s, t, CE=CE, Dc=Dc, ct1=ct1, ct2=ct2):
        phi = -math.pi / 2 + math.pi * s                  # ct1-component >= 0: the half toward the upper arm
        beta = rad(70) * (1 - t) + 0.001
        d = Dc * (0.85 * math.cos(beta)) + (ct1 * math.cos(phi) + ct2 * math.sin(phi)) * math.sin(beta)
        return CE + d * 0.105

    plate("couter_" + side, "upper_arm." + side, Patch(cou_fn, 14, 5), M_STEEL, CE, rims=("t0",),
          bosses=[(CE + Dc * 0.105 * 0.85, Dc, 0.02, 0.011)])

    def cou_skirt(s, t, CE=CE, Dc=Dc, ct2=ct2, Af=Af):
        b = rad(-70) + rad(140) * s
        k = 1.0 + 0.05 * t
        return CE + Af * (0.07 * t) + (Dc * (0.85 * math.cos(b)) + ct2 * math.sin(b)) * (0.105 * k)

    plate("couter_skirt_" + side, "upper_arm." + side, Patch(cou_skirt, 12, 2), M_STEEL, G.seg_closest(E - Af * 0.2, E + Af * 0.2),
          rims=("t1", "s0", "s1"))
    Dw = (OUTW * 0.9 - F * 0.3 - fr_u.A * 0.1).normalized()
    wf = dome_fn_factory(E + OUTW * 0.006, Dw, 0.066, rad(36), flat=0.45, ref=Z)
    plate("couter_wing_" + side, "upper_arm." + side, Patch(wf, 14, 3, closed_s=True), M_STEEL, E, rims=("t0",))

    # elbow padding ball (upper arm)
    bme = bmesh.new()
    bmesh.ops.create_uvsphere(bme, u_segments=12, v_segments=8, radius=0.062, matrix=Matrix.Translation(E))
    solid("elbow_pad_" + side, "upper_arm." + side, bme, M_CLOTH)

    # --- vambrace (forearm)
    fr_f, Lf = G.limb_frame(E, W, F, OUTW)

    def vamb_fn(s, t, fr=fr_f, Lf=Lf):
        th = 2 * math.pi * s
        z = 0.035 + (Lf - 0.04) * t
        bell = 0.026 * smoothstep(0.55, 1.0, t) ** 1.5
        r0 = 0.072
        x, y = sect(th, r0 + bell, r0 + 0.002 + bell, r0 + 0.004 + bell, 2.15)
        return fr.p(x, y, z)

    vp = Patch(vamb_fn, 16, 7, closed_s=True)
    vriv = [(s, t) for s in (0.2, 0.3, 0.7, 0.8) for t in (0.2, 0.45, 0.7)]
    plate("vambrace_" + side, "forearm." + side, vp, M_STEEL, G.seg_closest(fr_f.p(0, 0, -0.2), fr_f.p(0, 0, 0.6)),
          rims=("t0", "t1"), rivets=vriv, rivet_mat=M_BRASS)

    # --- gauntlet (hand)
    fr_h, Lh = G.limb_frame(W, B["hand." + side][1], F, OUTW)
    hax = G.seg_closest(fr_h.p(0, 0, -0.2), fr_h.p(0, 0, 0.4))

    def cuff_fn(s, t, fr=fr_h):
        th = 2 * math.pi * s
        z = -0.035 + 0.09 * t
        r = lerp(0.06, 0.054, t)
        x, y = sect(th, r, r * 0.98, r * 0.98, 2.2)
        return fr.p(x, y, z)

    plate("gauntlet_cuff_" + side, "hand." + side, Patch(cuff_fn, 16, 3, closed_s=True), M_STEEL, hax, rims=("t1",))

    def palm_fn(s, t, fr=fr_h):
        th = 2 * math.pi * s
        z = 0.035 + 0.105 * t
        k = 1.0 - 0.15 * t
        x, y = sect(th, 0.030 * k, 0.05 * k, 0.047 * k, 2.8, side_neg=0.022 * k)
        return fr.p(x, y, z)

    # palm block as a closed solid (leather glove)
    bmp = bmesh.new()
    rows = G.build_patch(bmp, Patch(palm_fn, 16, 3, closed_s=True))
    bmp.faces.new(list(reversed(rows[0])))
    bmp.faces.new(rows[-1])
    solid("glove_" + side, "hand." + side, bmp, M_LEATHER)

    def back_fn2(s, t, fr=fr_h):
        x = 2 * s - 1
        th = rad(70) * x          # around outward axis
        z = 0.04 + 0.1 * t
        r = 0.036
        return fr.p(0.004 + math.cos(th) * r * 0.9, math.sin(th) * 0.058, z)

    plate("gauntlet_back_" + side, "hand." + side, Patch(back_fn2, 10, 4), M_STEEL, hax, thickness=0.005, rims=("t1",))

    def fingers(bm, fr=fr_h, sgn=sgn):
        faces = []
        # index at the front
        offs = [(0.035, 0.050), (0.012, 0.054), (-0.012, 0.050), (-0.034, 0.042)]
        for fy, flen in offs:
            p = fr.p(0.004, fy, 0.14)
            d = fr.A.copy()
            curl_axis = fr.Y
            seg_l = [flen * 0.42, flen * 0.33, flen * 0.27]
            pts = [p.copy()]
            ang = 0.0
            for k, sl in enumerate(seg_l):
                ang += rad(38 if k == 0 else 42)
                # curl toward the palm (-X)
                dd = (fr.A * math.cos(ang) - fr.X * math.sin(ang)).normalized()
                p = p + dd * sl
                pts.append(p.copy())
            nrm = [fr.Y.copy() for _ in pts]
            rr = [0.0125, 0.0118, 0.011, 0.0095]
            faces += G.sweep(bm, pts, nrm, rr, sides=6)
            # knuckle plates on top of each segment
        # thumb from the front side
        p = fr.p(-0.005, 0.05, 0.06)
        pts = [p.copy()]
        dd = (fr.A * 0.6 + fr.Y * 0.8).normalized()
        p = p + dd * 0.035; pts.append(p.copy())
        dd = (fr.A * 0.7 + fr.Y * 0.3 - fr.X * 0.64).normalized()
        p = p + dd * 0.03; pts.append(p.copy())
        faces += G.sweep(bm, pts, [fr.X.copy() for _ in pts], [0.014, 0.012, 0.0105], sides=6)
        return faces

    fingob = bmesh.new()
    ff = fingers(fingob)
    solid("fingers_" + side, "hand." + side, fingob, M_STEEL)

    # knuckle guard
    def knuckle(bm, fr=fr_h):
        pts = [fr.p(0.03, y, 0.138) for y in (-0.05, -0.025, 0.0, 0.025, 0.05)]
        return G.sweep(bm, pts, [fr.X.copy() for _ in pts], 0.012, sides=6)

    kb = bmesh.new()
    knuckle(kb)
    solid("knuckle_" + side, "hand." + side, kb, M_RIM)

    # the concept's gauntlets are heavy: scale the whole hand group about the wrist
    for ob, bone in PIECES:
        if bone == "hand." + side:
            for v in ob.data.vertices:
                v.co = W + (v.co - W) * 1.28

# ================================================================ LEGS
for side, sgn in (("L", 1.0), ("R", -1.0)):
    OUTW = U * sgn
    HJ = B["thigh." + side][0]
    K = B["shin." + side][0]
    AK = B["foot." + side][0]
    fr_t, Lt = G.limb_frame(HJ, K, FWD, OUTW)
    tax = G.seg_closest(fr_t.p(0, 0, -0.3), fr_t.p(0, 0, 0.6))

    def thigh_q(s, t, fr=fr_t, Lt=Lt):
        th = 2 * math.pi * s
        z = -0.07 + (Lt + 0.05) * t
        k = lerp(1.0, 0.86, smoothstep(0.3, 1.0, t))
        x, y = sect(th, 0.104 * k, 0.11 * k, 0.108 * k, 2.1)
        return fr.p(x, y, z)

    plate("thigh_quilt_" + side, "thigh." + side, Patch(thigh_q, 14, 6, closed_s=True), M_CLOTH, tax, thickness=0.004,
          rims=None, attr_along=(HJ, fr_t.A))

    def cuisse_fn(s, t, fr=fr_t, Lt=Lt):
        x = 2 * s - 1
        th = rad(-70) + (rad(128) - rad(-70)) * s       # 0 front, + outward
        z = 0.07 + (Lt - 0.17) * t
        k = lerp(1.0, 0.88, t)
        xx, yy = sect(th, 0.122 * k, 0.128 * k, 0.124 * k, 2.1)
        yy += 0.008 * math.exp(-(th / 0.25) ** 2)
        return fr.p(xx, yy, z)

    cu = Patch(cuisse_fn, 16, 6)
    plate("cuisse_" + side, "thigh." + side, cu, M_STEEL, tax, rims=("loop",),
          rivets=[(s, 0.9) for s in (0.15, 0.3, 0.45, 0.6, 0.75, 0.9)])

    # tassets (thigh): two lames hanging over the front-outer thigh
    for k in range(2):
        z0 = -0.10 + 0.1 * k
        z1 = z0 + 0.125

        def tas_fn(s, t, z0=z0, z1=z1, k=k, fr=fr_t):
            th = rad(-50) + rad(88) * s
            z = z1 + (z0 - z1) * t
            r = 0.146 - 0.007 * k + 0.012 * (1 - t)
            xx, yy = sect(th, r, r * 1.02, r, 2.1)
            return fr.p(xx, yy, z)

        tp = Patch(tas_fn, 12, 3)
        plate(f"tasset{k}_{side}", "thigh." + side, tp, M_STEEL, tax, rims=("t0", "s0", "s1"),
              rivets=[(s, 0.8) for s in (0.15, 0.38, 0.62, 0.85)], rivet_mat=M_BRASS)

    # poleyn (thigh): knee cop + outer wing + boss
    PKc = K + fr_t.Y * 0.02

    # upper half-dome over the knee + a skirt below it; the greave's top sits inside the skirt with clearance,
    # so the knee reads continuous without the two plates crossing (a closed dome's lower half would cut the greave)
    def pol_fn(s, t, fr=fr_t, c=PKc):
        phi = math.pi + math.pi * s                       # upper half: A-component <= 0 (above the knee)
        beta = rad(80) * (1 - t) + 0.001
        d = fr.Y * math.cos(beta) + (fr.X * math.cos(phi) + fr.A * math.sin(phi)) * math.sin(beta)
        return c + fr.Y * (d.dot(fr.Y) * 0.115) + fr.X * (d.dot(fr.X) * 0.115) + fr.A * (d.dot(fr.A) * 0.12)

    plate("poleyn_" + side, "thigh." + side, Patch(pol_fn, 16, 5), M_STEEL, PKc, rims=("t0",))

    def pol_skirt(s, t, fr=fr_t, c=PKc):
        b = rad(-80) + rad(160) * s
        k = 1.0 + 0.05 * t
        return K + fr.A * (0.085 * t) + (fr.Y * (0.02 + 0.115 * math.cos(b)) + fr.X * (0.115 * math.sin(b))) * k

    plate("poleyn_skirt_" + side, "thigh." + side, Patch(pol_skirt, 16, 2), M_STEEL, G.seg_closest(K - fr_t.A * 0.2, K + fr_t.A * 0.2),
          rims=("t1", "s0", "s1"), rivets=[(s, 0.6) for s in (0.25, 0.5, 0.75)], rivet_mat=M_BRASS)
    Dw = (fr_t.X * 0.9 + fr_t.Y * 0.35).normalized()

    def wing_fn(s, t, fr=fr_t, D=Dw):
        t1 = fr.A
        t2 = D.cross(t1)
        phi = 2 * math.pi * s
        beta = rad(50) * (1 - t) + 0.001
        d = D * math.cos(beta) + (t1 * math.cos(phi) * 1.1 + t2 * math.sin(phi)) * math.sin(beta)
        dd = d.dot(D)
        d = d - D * dd * 0.55
        return K + fr.Y * 0.006 + d * 0.095

    wp = Patch(wing_fn, 14, 3, closed_s=True)
    wc = wing_fn(0.0, 1.0)
    plate("poleyn_wing_" + side, "thigh." + side, wp, M_STEEL, K, rims=("t0",), bosses=[(wc, Dw, 0.024, 0.014)])
    bmk = bmesh.new()
    bmesh.ops.create_uvsphere(bmk, u_segments=12, v_segments=8, radius=0.074, matrix=Matrix.Translation(K))
    solid("knee_pad_" + side, "thigh." + side, bmk, M_CLOTH)

    # greave (shin)
    fr_s, Ls = G.limb_frame(K, AK, FWD, OUTW)
    sax = G.seg_closest(fr_s.p(0, 0, -0.3), fr_s.p(0, 0, 0.6))
    GRV = [(0.0, (0.092, 0.097, 0.09)), (0.12, (0.09, 0.098, 0.09)), (0.35, (0.087, 0.095, 0.1)),
           (0.7, (0.076, 0.084, 0.086)), (0.92, (0.084, 0.092, 0.09)), (1.0, (0.094, 0.1, 0.098))]

    def greave_fn(s, t, fr=fr_s, Ls=Ls):
        th = 2 * math.pi * s
        c = (1 - math.cos(th)) / 2           # 0 front, 1 back
        ztop = -0.02 + 0.08 * c ** 1.2
        zbot = Ls - 0.012 - 0.035 * (1 - c) ** 1.5      # arched over the instep: the idle stance tilts the shin forward
        z = ztop + (zbot - ztop) * t
        a, f, b = cr(GRV, min(max(z / Ls, 0.0), 1.0))
        xx, yy = sect(th, a, f, b, 2.1)
        yy += 0.009 * math.exp(-(th / 0.22) ** 2)
        return fr.p(xx, yy, z)

    gp = Patch(greave_fn, 18, 9, closed_s=True)
    griv = [(s, 0.06) for s in (0.05, 0.12, 0.88, 0.95)] + [(s, 0.93) for s in (0.1, 0.25, 0.4, 0.6, 0.75, 0.9)]
    plate("greave_" + side, "shin." + side, gp, M_STEEL, sax, rims=("t0", "t1"), rivets=griv, rivet_mat=M_BRASS)

    # sabaton (foot): built flat on the ground plane of the idle stance (ankle 0.113 above the sole)
    Dh = Vector((B["foot." + side][1].x - AK.x, B["foot." + side][1].y - AK.y, 0)).normalized()
    ang = math.atan2(Dh.x, -Dh.y)
    splay = math.radians(12) * sgn
    fwd = Vector((math.sin(splay), -math.cos(splay), 0))
    sidev = Vector((-fwd.y, fwd.x, 0)) * (1 if Vector((-fwd.y, fwd.x, 0)).dot(OUTW) > 0 else -1)
    SOLE = AK.z - 0.113
    base = Vector((AK.x, AK.y, SOLE)) - fwd * 0.012
    SAB = [(-0.09, (0.035, 0.065)), (-0.072, (0.052, 0.088)), (-0.04, (0.06, 0.106)), (0.0, (0.064, 0.118)),
           (0.04, (0.067, 0.11)), (0.08, (0.072, 0.1)), (0.13, (0.069, 0.087)), (0.17, (0.06, 0.075)),
           (0.2, (0.047, 0.065)), (0.217, (0.03, 0.055)), (0.228, (0.012, 0.046))]

    # Rocker sole. The clips were solved so the OLD mesh's lowest foot vertex met the ground, and they pitch planted
    # feet toe-down about 20 deg; a flat, long sole would pierce the floor. Toe spring and a slight heel lift roll it.
    def ROCK(x):
        return 0.046 * smoothstep(0.04, 0.225, x) ** 1.35 + 0.018 * smoothstep(-0.03, -0.085, x)

    def sab_fn(s, t, base=base, fwd=fwd, sidev=sidev):
        x = -0.085 + (0.228 + 0.085) * t
        w, top = cr(SAB, x)
        th = 2 * math.pi * s
        zc = 0.034
        sx, sy = math.sin(th), math.cos(th)
        px = w * math.copysign(abs(sx) ** (2 / 2.4), sx)
        up = (top - zc) if sy >= 0 else (zc - 0.012)
        pz = zc + up * math.copysign(abs(sy) ** (2 / 2.4), sy)
        return base + fwd * x + sidev * px + Z * (pz + ROCK(x))

    sp = Patch(sab_fn, 16, 12, closed_s=True)
    bms = bmesh.new()
    rows = G.build_patch(bms, sp)
    bms.faces.new(list(reversed(rows[0])))
    bms.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bms, faces=list(bms.faces))
    sab = G.to_object("sabaton_" + side, bms, "foot." + side, M_STEEL, M_VOID, thickness=0.0)
    PIECES.append((sab, "foot." + side))
    # instep lames
    for k in range(3):
        x0 = 0.085 + 0.04 * k
        x1 = x0 + 0.05

        def lam_fn(s, t, x0=x0, x1=x1, k=k, base=base, fwd=fwd, sidev=sidev):
            x = x1 + (x0 - x1) * t
            w, top = cr(SAB, x)
            th = rad(-100) + rad(200) * s
            zc = 0.034
            e = 0.007 + 0.004 * (1 - t) - 0.002 * k
            sx, sy = math.sin(th), math.cos(th)
            px = (w + e) * math.copysign(abs(sx) ** (2 / 2.4), sx)
            pz = zc + (top - zc + e) * math.copysign(abs(sy) ** (2 / 2.4), sy)
            return base + fwd * x + sidev * px + Z * (pz + ROCK(x))

        plate(f"sabaton_lame{k}_{side}", "foot." + side, Patch(lam_fn, 12, 2), M_STEEL,
              lambda p, base=base: Vector((base.x, base.y, base.z + 0.03)) + (p - base).project(fwd), rims=("t0",))
    # sole (leather slab)
    bmo = bmesh.new()

    def sole_fn(s, t, base=base, fwd=fwd, sidev=sidev):
        x = -0.089 + (0.233 + 0.089) * t
        w, top = cr(SAB, min(x, 0.228))
        th = 2 * math.pi * s
        return base + fwd * x + sidev * (w + 0.006) * math.sin(th) + Z * (0.013 * (0.5 + 0.5 * math.cos(th)) + ROCK(x))

    rows = G.build_patch(bmo, Patch(sole_fn, 10, 8, closed_s=True))
    bmo.faces.new(list(reversed(rows[0])))
    bmo.faces.new(rows[-1])
    solid("sole_" + side, "foot." + side, bmo, M_LEATHER)
    # ankle boss
    ab = bmesh.new()
    pb = Vector((AK.x, AK.y, SOLE + 0.072)) + sidev * 0.071
    G.dome(ab, pb, sidev, 0.021, 0.013, segs=10, rings=3)
    solid("ankle_boss_" + side, "foot." + side, ab, M_BRASS)

    # ankle lame (foot): fills the front of the ankle under the greave's arched hem, tucked inside the greave
    def ankle_fn(s, t, AK=AK, fwd=fwd, sidev=sidev):
        th = rad(-78) + rad(156) * s
        z = AK.z - 0.028 + 0.072 * t
        return Vector((AK.x, AK.y, z)) + (fwd * (0.07 * math.cos(th)) + sidev * (0.064 * math.sin(th))) * lerp(1.15, 1.0, smoothstep(0.0, 0.6, t))

    plate("ankle_lame_" + side, "foot." + side, Patch(ankle_fn, 12, 2), M_STEEL, Vector((AK.x, AK.y, AK.z)),
          rims=("t0", "s0", "s1"))

# ================================================================ assemble
objs = [o for o, _ in PIECES]
for pid, (o, bone) in enumerate(PIECES):
    vg = o.vertex_groups.new(name=bone)
    vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    o["piece"] = o.name
    # piece id per vertex: the bake spreads pieces apart so each one's occlusion is its own
    at = o.data.attributes.new("piece_id", "INT", "POINT")
    at.data.foreach_set("value", [pid] * len(o.data.vertices))

piece_tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs}
piece_bone = {o.name: b for o, b in PIECES}

ctx = bpy.context
target = objs[0]
for o in objs:
    o.select_set(True)
ctx.view_layer.objects.active = target
with ctx.temp_override(active_object=target, selected_objects=objs, selected_editable_objects=objs, object=target):
    bpy.ops.object.join()
body = target
body.name = "creature_animated_armour_v2"
body.data.name = "creature_animated_armour_v2"

# sharp edges by angle, smooth shading
bm = bmesh.new()
bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
# n-gon caps would stop the glTF exporter computing tangents (it needs tris/quads)
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], quad_method="BEAUTY", ngon_method="BEAUTY")
bm.normal_update()
for f in bm.faces:
    f.smooth = True
for e in bm.edges:
    if len(e.link_faces) == 2:
        a, b = e.link_faces
        if a.normal.angle(b.normal, 0) > math.radians(48) or a.material_index != b.material_index:
            e.smooth = False
    else:
        e.smooth = False
bm.to_mesh(body.data)
bm.free()

body.parent = ARM
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = ARM

# UVs: smart project every face but the void
me = body.data
void_idx = [m.name for m in me.materials].index("k_void")
if not me.uv_layers:
    me.uv_layers.new(name="UVMap")
bpy.context.view_layer.objects.active = body
body.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
# atlas layout: a trim band at the bottom (v < 0.07) holds every rim tube, rivet, boss and plate edge, overlapped by kind;
# plate surfaces are smart-projected and packed above it.
MATN = [m.name for m in me.materials]
REG = {(1, "k_rim"): (0.0, 0.0, 1.0, 0.034), (1, "k_steel"): (0.0, 0.036, 0.5, 0.018),
       (2, "k_brass"): (0.51, 0.036, 0.028, 0.028), (2, "k_rim"): (0.545, 0.036, 0.028, 0.028),
       (2, "k_steel"): (0.545, 0.036, 0.028, 0.028), (0, "k_rim"): (0.58, 0.036, 0.028, 0.028)}
VOID_UV = (0.66, 0.05)
MAIN_V0, MAIN_S = 0.072, 0.928
bm = bmesh.from_edit_mesh(me)
pl = bm.faces.layers.int.get("preuv")
for f in bm.faces:
    pre = f[pl] if pl else 0
    name = MATN[f.material_index]
    f.select = (name not in ("k_void", "k_rim")) and pre == 0
bmesh.update_edit_mesh(me)
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
bm = bmesh.from_edit_mesh(me)
uvl = bm.loops.layers.uv.active
# cloth and leather get less texel density than steel
for f in bm.faces:
    if f.select and MATN[f.material_index] in ("k_cloth", "k_leather"):
        pass
bpy.ops.uv.pack_islands(rotate=True, margin=0.003, shape_method="CONCAVE")
bm = bmesh.from_edit_mesh(me)
uvl = bm.loops.layers.uv.active
pl = bm.faces.layers.int.get("preuv")
import random
rng = random.Random(7)
for f in bm.faces:
    pre = f[pl] if pl else 0
    name = MATN[f.material_index]
    if name == "k_void":
        for l in f.loops:
            l[uvl].uv = VOID_UV
    elif f.select:
        for l in f.loops:
            u, v = l[uvl].uv
            l[uvl].uv = (u * MAIN_S, MAIN_V0 + v * MAIN_S)
    elif (pre, name) in REG:
        u0, v0, w, h = REG[(pre, name)]
        if pre == 0:   # plate edges: a small patch inside the edge square
            cu, cv = rng.random(), rng.random()
            for i, l in enumerate(f.loops):
                l[uvl].uv = (u0 + w * (0.1 + 0.8 * cu) + 0.002 * (i % 2), v0 + h * (0.1 + 0.8 * cv) + 0.002 * (i // 2))
        else:
            for l in f.loops:
                u, v = l[uvl].uv
                l[uvl].uv = (u0 + min(max(u, 0.0), 1.0) * w, v0 + min(max(v, 0.0), 1.0) * h)
    else:
        print("UNMAPPED face", pre, name)
bmesh.update_edit_mesh(me)
bpy.ops.object.mode_set(mode="OBJECT")

# report
tris = sum(len(p.vertices) - 2 for p in me.polygons)
pts = [v.co for v in me.vertices]
mn = [min(p[i] for p in pts) for i in range(3)]
mx = [max(p[i] for p in pts) for i in range(3)]
by_bone = {}
for n, t in piece_tris.items():
    by_bone[piece_bone[n]] = by_bone.get(piece_bone[n], 0) + t
rep = {"triangles": tris, "vertices": len(me.vertices), "pieces": len(PIECES), "bounds_min": mn, "bounds_max": mx,
       "by_bone": by_bone, "piece_tris": piece_tris, "piece_bone": piece_bone,
       "materials": [m.name for m in me.materials]}
with open(REPORT, "w") as h:
    json.dump(rep, h, indent=1)
print("BUILD tris", tris, "verts", len(me.vertices), "pieces", len(PIECES), "bounds", mn, mx)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("SAVED", OUT)
