extends RefCounted
## Read-only topology/posed-vertex measurement. Does not alter mesh, skin or rig.
## Native GPU pose readback: MeshInstance3D.bake_mesh_from_current_skeleton_pose.

static func boundary_loops(mi: MeshInstance3D, surface: int) -> Array:
	var arrays := mi.mesh.surface_get_arrays(surface)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	var ids := {}
	var refs := []
	var canonical := []
	for v in vertices:
		var key := Vector3i(roundi(v.x * 1000000),roundi(v.y * 1000000),roundi(v.z * 1000000))
		if not ids.has(key):
			ids[key] = refs.size()
			refs.append(canonical.size())
		canonical.append(ids[key])
	var edges := {}
	for t in range(0,indices.size(),3):
		for e in range(3):
			var a: int = canonical[indices[t+e]]
			var b: int = canonical[indices[t+(e+1)%3]]
			var key := Vector2i(mini(a,b),maxi(a,b))
			edges[key] = int(edges.get(key,0))+1
	var adj := {}
	for edge in edges:
		if edges[edge] != 1: continue
		for pair in [[edge.x,edge.y],[edge.y,edge.x]]:
			if not adj.has(pair[0]): adj[pair[0]]=[]
			adj[pair[0]].append(pair[1])
	var visited := {}
	var loops := []
	for start in adj:
		if visited.has(start): continue
		var pending := [start]
		var loop := []
		while not pending.is_empty():
			var i: int = pending.pop_back()
			if visited.has(i): continue
			visited[i]=true
			loop.append(refs[i])
			pending.append_array(adj[i])
		loops.append(loop)
	return loops

static func points(mi: MeshInstance3D, surface: int, indices: Array, posed: bool) -> PackedVector3Array:
	var mesh: ArrayMesh = mi.bake_mesh_from_current_skeleton_pose() if posed else mi.mesh
	var vertices: PackedVector3Array = mesh.surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]
	var result := PackedVector3Array()
	for i in indices: result.append(mi.global_transform * vertices[i])
	return result

static func center(points_: PackedVector3Array) -> Vector3:
	var result := Vector3.ZERO
	for p in points_: result += p
	return result / points_.size()

static func pair_nearest(a: PackedVector3Array,b: PackedVector3Array) -> Array:
	var pairs := []
	for p in a:
		var best := INF
		var index := -1
		for i in b.size():
			var d := p.distance_squared_to(b[i])
			if d < best: best=d; index=i
		pairs.append(index)
	return pairs

static func max_distance(a: PackedVector3Array,b: PackedVector3Array,pairs: Array) -> float:
	var worst := 0.0
	for i in a.size(): worst=maxf(worst,a[i].distance_to(b[pairs[i]]))
	return worst
