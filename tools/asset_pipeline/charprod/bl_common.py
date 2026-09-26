"""Shared Blender-side helpers of the character production pipeline (imported by the stage scripts)."""
import random

import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def remove_interior(bm, rays=64, min_interior=200):
    """Delete the enclosed inner skin an unsigned-distance-field mesh wraps every surface in: a face is interior when
    no ray from it (over its outward hemisphere) escapes the mesh; connected interior sets of at least min_interior
    faces go (a smaller set is a crevice and stays). bm must be in world space. Returns (deleted, deleted area, kept)."""
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    bvh = BVHTree.FromBMesh(bm)
    rng = random.Random(1)
    dirs = []
    while len(dirs) < rays:
        v = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1)))
        if v.length > 1e-6:
            dirs.append(v.normalized())
    interior = set()
    for fc in bm.faces:
        p, n = fc.calc_center_median(), fc.normal
        if not any(bvh.ray_cast(p + n * 1e-4, d, 100.0)[0] is None for d in dirs if d.dot(n) > 0.05):
            interior.add(fc.index)

    def neighbours(fi):
        return {l.face.index for e in bm.faces[fi].edges for l in e.link_loops if l.face.index != fi}

    doomed, seen, small = set(), set(), 0
    for fi in interior:
        if fi in seen:
            continue
        stack, comp = [fi], set()
        while stack:
            x = stack.pop()
            if x not in comp:
                comp.add(x)
                stack.extend(n for n in neighbours(x) if n in interior and n not in comp)
        seen |= comp
        if len(comp) >= min_interior:
            doomed |= comp
        else:
            small += len(comp)
    area = sum(bm.faces[i].calc_area() for i in doomed)
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in doomed], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    return len(doomed), area, small


def boundary_loops(bm, edges):
    """Group boundary edges into closed loops (lists of edges)."""
    left, loops = set(edges), []
    while left:
        start = left.pop()
        loop, frontier = [start], [start]
        while frontier:
            e = frontier.pop()
            for v in e.verts:
                for n in v.link_edges:
                    if n in left:
                        left.discard(n)
                        loop.append(n)
                        frontier.append(n)
        loops.append(loop)
    return loops
