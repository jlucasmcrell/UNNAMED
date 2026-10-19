extends RefCounted
## Rest-pose section definition matches measure_ue_sources.py exactly.
static func measure(points: PackedVector3Array) -> Dictionary:
	var low := INF
	var high := -INF
	for p in points: low=minf(low,p.y); high=maxf(high,p.y)
	var height := high-low
	var result := {"height":height}
	for spec in [["shoulders",.82],["chest",.72],["waist",.62],["hips",.54]]:
		var central_width := .12 if spec[0]=="chest" else .18
		var section := PackedVector2Array()
		var min_x := INF
		var max_x := -INF
		for p in points:
			if absf(p.y-(low+height*spec[1]))<=height*.005 and absf(p.x)<=height*central_width:
				section.append(Vector2(p.x,p.z))
				min_x=minf(min_x,p.x);max_x=maxf(max_x,p.x)
		assert(section.size()>10)
		if spec[0]=="shoulders":result[spec[0]]=max_x-min_x
		else:
			var polygon := Geometry2D.convex_hull(section)
			var perimeter := 0.0
			for i in range(polygon.size()-1):perimeter+=polygon[i].distance_to(polygon[i+1])
			result[spec[0]]=perimeter
	return result
