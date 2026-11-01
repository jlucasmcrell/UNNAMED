extends RefCounted
## Observe the actual nodes created by ArenaMarks.Build in the production frontier scene.
static func check(hollow: Node) -> Dictionary:
    var field_marks: Array[String] = []
    var trial_mark := false
    for node in hollow.get_children():
        var key := str(node.name)
        if not key.begins_with("Arena"):
            continue
        if key.contains("wayholder_trial"):
            trial_mark = true
        elif not key.contains("custodian"):
            field_marks.append(key)
    return {"passed": trial_mark and field_marks.is_empty(),
        "wayholder_trial_mark": trial_mark, "field_marks": field_marks}
