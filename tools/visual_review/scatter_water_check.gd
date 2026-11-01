extends RefCounted
## Run against the real, built Scatter node in a starting_frontier capture.
## The inner eight metres of the millpond are below its surface for every shoreline wobble.
## A dry annulus supplies a positive control: an empty/disabled scatter layer cannot pass.
static func check(scatter: Node) -> Dictionary:
    var open_pond := 0
    var dry_near_pond := 0
    var kinds := {}
    for node in scatter.get_children():
        if not node is MultiMeshInstance3D or node.visibility_range_begin > 0:
            continue
        for i in node.multimesh.instance_count:
            var at: Vector3 = node.global_transform * node.multimesh.get_instance_transform(i).origin
            var distance := Vector2(at.x - 958, at.z - 700).length()
            if distance < 8:
                open_pond += 1
                kinds[str(node.name)] = true
            elif distance > 17 and distance < 27:
                dry_near_pond += 1
    return {"passed": open_pond == 0 and dry_near_pond > 0,
        "open_millpond_placements": open_pond, "dry_near_pond_placements": dry_near_pond,
        "offending_chunks": kinds.keys()}
