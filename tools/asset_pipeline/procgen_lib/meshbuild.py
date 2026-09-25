"""Mesh accumulation: parts as closed solids in their own member frames, then one Blender object.

Extracted from the Builder/Part accumulators of _procgen_chest.py (member-frame positions, per-part grain, close()),
_procgen_cart.py (transform stack, mark/rollback) and _procgen_crate.py (Part frames, attributes), plus the
make_object / select_only / finish_topology / separate-lid code all three carried.

API (numpy only until make_object; the Blender calls import bpy lazily)
    Builder(rng)
      part(name, frame=None, group='body', slot=0, grain=None, wear=0.0, texel=1.0) -> Part   start a part
      v(local) -> index            vertex given in the part's member frame (x along the grain)
      vw(world, mpos) -> index     vertex given in world space with explicit member-frame coordinates (sweeps)
      f(idx, uv, island='side', slot=None, end=False, texel=None)   face: corner UVs in island metres
      close()                      turn the current part outward if its signed volume is negative
      xf(M)                        context manager composing a 4x4 transform onto everything built inside it
      mark() / rollback(mark), islands(), group_faces(group), bounds()
    frame_matrix(origin, R) -> 4x4;  ATTRIBUTES: the attribute schema make_object writes
    make_object(b, name, loop_uv, slot_count=None) -> bpy object (mesh, UVMap, material indices, attributes)
    select_only(obj);  finish_topology(obj, sharp_deg=50)  triangulate, sharp by angle, face-area weighted normals
    split_group(obj, group, name, pivot) -> child object whose origin is `pivot` (lids, doors)
    remove_attributes(obj, names=ATTRIBUTES);  face_array(me, name)
"""
import math
from contextlib import contextmanager

import numpy as np

# Attributes make_object writes; shadergraph.surface_context reads them.
ATTRIBUTES = {
    "mpos": ("FLOAT_VECTOR", "POINT"),     # member-frame position, x along the grain
    "pgrain": ("FLOAT_VECTOR", "FACE"),    # pith line y, z (m, member frame) and ring-width factor
    "pslope": ("FLOAT_VECTOR", "FACE"),    # pith drift dy/dx, dz/dx and knot density 0..1
    "prand": ("FLOAT", "FACE"),            # per-part random
    "pend": ("FLOAT", "FACE"),             # 1 on end grain
    "pwear": ("FLOAT", "FACE"),            # per-part extra wear 0..1
    "pzoff": ("FLOAT", "FACE"),            # height the part is lifted by during a bake (runner sets it)
    "pgroup": ("INT", "FACE"),             # group index (Builder.groups)
}


def frame_matrix(origin, R):
    M = np.eye(4)
    M[:3, :3] = np.asarray(R, np.float64)
    M[:3, 3] = np.asarray(origin, np.float64)
    return M


class Part:
    __slots__ = ("name", "group", "slot", "rand", "grain", "slope", "wear", "M", "v0", "f0", "texel", "index")

    def __init__(self, name, group, slot, rand, grain, slope, wear, M, v0, f0, texel, index):
        self.name, self.group, self.slot, self.rand = name, group, slot, rand
        self.grain, self.slope, self.wear, self.M = grain, slope, wear, M
        self.v0, self.f0, self.texel, self.index = v0, f0, texel, index


def wood_grain(rng, hy, hz):
    """Pith line and drift for a member of half section hy x hz (member frame): boxed heart for square timbers,
    else flat-sawn (pith off a broad face, cathedral figure) or rift-sawn (pith off an edge, straight lines)."""
    if max(hy, hz) / max(min(hy, hz), 1e-6) < 1.6:
        ang, dist = rng.uniform(0, 2 * math.pi), rng.uniform(0.02, 0.09)
        py0, pz0 = dist * math.cos(ang), dist * math.sin(ang)
    elif rng.random() < 0.7:
        py0 = rng.uniform(-0.8, 0.8) * hy
        pz0 = (1 if rng.random() < 0.5 else -1) * rng.uniform(0.12, 0.30)
    else:
        py0 = (1 if rng.random() < 0.5 else -1) * rng.uniform(0.08, 0.22)
        pz0 = rng.uniform(-1.5, 1.5) * hz
    grain = (py0, pz0, rng.uniform(0.8, 1.25))
    slope = (rng.normal(0, 0.015), rng.normal(0, 0.015), rng.uniform(0.2, 0.6))
    return grain, slope


class Builder:
    """Vertices, faces, per-corner island UVs (metres), per-vertex member-frame positions and per-part attributes.
    Parts never share vertices; each is expected to be a closed solid."""

    def __init__(self, rng):
        self.rng = rng
        self.verts, self.mpos, self.vpart = [], [], []
        self.faces, self.fuv, self.fisland, self.fslot, self.fpart, self.fend = [], [], [], [], [], []
        self.parts, self.groups = [], []
        self.island_scale = {}
        self._stack = np.eye(4)

    # ---- parts
    def part(self, name, frame=None, group="body", slot=0, grain=None, wear=0.0, texel=1.0):
        """frame: 4x4 member frame (x along the grain). grain: (hy, hz) half section for wood figure, or None
        (then the shader uses world-scale figure)."""
        if group not in self.groups:
            self.groups.append(group)
        g, s = wood_grain(self.rng, *grain) if grain is not None else ((0.0, 0.0, 1.0), (0.0, 0.0, 0.0))
        M = self._stack @ (np.eye(4) if frame is None else np.asarray(frame, np.float64))
        p = Part(name, group, slot, float(self.rng.random()), g, s, float(wear), M, len(self.verts), len(self.faces),
                 float(texel), len(self.parts))
        self.parts.append(p)
        return p

    @property
    def current(self):
        return self.parts[-1]

    def v(self, local):
        local = np.asarray(local, np.float64)
        M = self.current.M
        self.verts.append(M[:3, :3] @ local + M[:3, 3])
        self.mpos.append(local)
        self.vpart.append(len(self.parts) - 1)
        return len(self.verts) - 1

    def vw(self, world, mpos):
        w = np.asarray(world, np.float64)
        self.verts.append(self._stack[:3, :3] @ w + self._stack[:3, 3])
        self.mpos.append(np.asarray(mpos, np.float64))
        self.vpart.append(len(self.parts) - 1)
        return len(self.verts) - 1

    def f(self, idx, uv, island="side", slot=None, end=False, texel=None):
        p = self.current
        key = (p.index, island)
        if key not in self.island_scale:
            self.island_scale[key] = p.texel if texel is None else float(texel)
        self.faces.append(tuple(int(i) for i in idx))
        self.fuv.append(tuple((float(a), float(c)) for a, c in uv))
        self.fisland.append(key)
        self.fslot.append(p.slot if slot is None else slot)
        self.fpart.append(p.index)
        self.fend.append(1.0 if end else 0.0)

    def signed_volume(self, f0, f1):
        vol = 0.0
        for fi in range(f0, f1):
            idx = self.faces[fi]
            p0 = self.verts[idx[0]]
            for k in range(1, len(idx) - 1):
                vol += p0.dot(np.cross(self.verts[idx[k]], self.verts[idx[k + 1]])) / 6.0
        return vol

    def close(self):
        """Turn the current part outward if its volume came out negative (a mirrored frame or a CW profile)."""
        p = self.current
        if self.signed_volume(p.f0, len(self.faces)) < 0:
            for fi in range(p.f0, len(self.faces)):
                self.faces[fi] = tuple(reversed(self.faces[fi]))
                self.fuv[fi] = tuple(reversed(self.fuv[fi]))

    @contextmanager
    def xf(self, M):
        old = self._stack
        self._stack = old @ np.asarray(M, np.float64)
        try:
            yield
        finally:
            self._stack = old

    def mark(self):
        return len(self.verts), len(self.faces), len(self.parts)

    def rollback(self, mk):
        nv, nf, np_ = mk
        for lst in (self.verts, self.mpos, self.vpart):
            del lst[nv:]
        for lst in (self.faces, self.fuv, self.fisland, self.fslot, self.fpart, self.fend):
            del lst[nf:]
        del self.parts[np_:]
        self.island_scale = {k: v for k, v in self.island_scale.items() if k[0] < np_}

    # ---- queries
    def part_faces(self, pi):
        p = self.parts[pi]
        f1 = self.parts[pi + 1].f0 if pi + 1 < len(self.parts) else len(self.faces)
        return range(p.f0, f1)

    def group_faces(self, group):
        return [fi for fi, pi in enumerate(self.fpart) if self.parts[pi].group == group]

    def bounds(self):
        V = np.asarray(self.verts)
        return V.min(axis=0), V.max(axis=0)

    def shift(self, d):
        d = np.asarray(d, np.float64)
        self.verts = [v + d for v in self.verts]

    def islands(self):
        """{key: {'faces': [...], 'scale': s}} in first-use order."""
        out = {}
        for fi, key in enumerate(self.fisland):
            out.setdefault(key, {"faces": [], "scale": self.island_scale[key]})["faces"].append(fi)
        return out


# ------------------------------------------------------------------------------------------------ Blender side

def make_object(b, name, loop_uv, slot_count=None):
    """Blender mesh object from a Builder: faces, UVMap from loop_uv ((n_loops, 2), face corner order), material
    index = slot, and the ATTRIBUTES. Refuses a mesh that mesh.validate() would change."""
    import bpy
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in b.verts], [], [list(f) for f in b.faces])
    if me.validate():
        raise SystemExit("mesh.validate() changed the generated mesh")
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    uvl.data.foreach_set("uv", np.asarray(loop_uv, np.float32).ravel())
    me.polygons.foreach_set("material_index", [int(s) for s in b.fslot])
    parts = [b.parts[pi] for pi in b.fpart]
    values = {
        "mpos": np.asarray(b.mpos, np.float32),
        "pgrain": np.array([p.grain for p in parts], np.float32),
        "pslope": np.array([p.slope for p in parts], np.float32),
        "prand": np.array([p.rand for p in parts], np.float32),
        "pend": np.asarray(b.fend, np.float32),
        "pwear": np.array([p.wear for p in parts], np.float32),
        "pzoff": np.zeros(len(parts), np.float32),
        "pgroup": np.array([b.groups.index(p.group) for p in parts], np.int32),
    }
    for aname, (atype, domain) in ATTRIBUTES.items():
        a = me.attributes.new(aname, atype, domain)
        a.data.foreach_set("vector" if atype == "FLOAT_VECTOR" else "value", values[aname].ravel())
    me.uv_layers.active = uvl
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def select_only(obj):
    import bpy
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def finish_topology(obj, sharp_deg=50.0, weight=50):
    """Triangulate (beauty), mark sharp by angle, then face-area weighted custom normals so chamfers catch light."""
    import bmesh
    import bpy
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(sharp_deg))
    select_only(obj)
    mod = obj.modifiers.new("weighted", "WEIGHTED_NORMAL")
    mod.mode = "FACE_AREA"
    mod.weight = weight
    mod.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=mod.name)


def face_array(me, name, dtype=np.float32):
    out = np.empty(len(me.polygons), dtype)
    me.attributes[name].data.foreach_get("value", out)
    return out


def group_vertex_mask(me, group_index):
    fg = face_array(me, "pgroup", np.int32)
    lt = np.empty(len(me.polygons), np.int64)
    me.polygons.foreach_get("loop_total", lt)
    lv = np.empty(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", lv)
    mask = np.zeros(len(me.vertices), bool)
    mask[lv[np.repeat(fg == group_index, lt)]] = True
    return mask, fg


def split_group(obj, group_index, name, pivot):
    """Split the faces of one group into their own object `name`, origin at `pivot` (world), parented to obj."""
    import bpy
    from mathutils import Matrix, Vector
    me = obj.data
    vsel, fg = group_vertex_mask(me, group_index)
    ev = np.empty(len(me.edges) * 2, np.int64)
    me.edges.foreach_get("vertices", ev)
    me.vertices.foreach_set("select", vsel)
    me.edges.foreach_set("select", vsel[ev.reshape(-1, 2)].all(axis=1))
    me.polygons.foreach_set("select", fg == group_index)
    me.update()
    select_only(obj)
    before = set(bpy.context.scene.objects)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    new = [o for o in bpy.context.scene.objects if o not in before and o.type == "MESH"]
    assert len(new) == 1, new
    child = new[0]
    child.name = name
    child.data.name = name
    child.data.transform(Matrix.Translation(-Vector(pivot)))
    child.location = Vector(pivot)
    child.parent = obj
    child.matrix_parent_inverse = Matrix.Identity(4)
    assert (face_array(obj.data, "pgroup", np.int32) != group_index).all()
    return child


def remove_attributes(obj, names=tuple(ATTRIBUTES)):
    me = obj.data
    for n in names:
        if n in me.attributes:
            me.attributes.remove(me.attributes[n])
