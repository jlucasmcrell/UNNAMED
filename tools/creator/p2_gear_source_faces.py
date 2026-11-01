"""Track original source faces through native garment preparation."""
def tag_source_faces(obj,correspondence):
    faces={p.index:[correspondence[i] for i in p.vertices] for p in obj.data.polygons}
    attribute=obj.data.attributes.new('p2_source_face','INT','FACE')
    attribute.data.foreach_set('value',list(range(len(obj.data.polygons))))
    return faces
