"""Make the scenery height read the surface the renderer actually draws (the carved, imported grid), not the plain domain field.

Root cause: `Images()` builds the imported heightmap from `ground.Carved(x, z, grid.HeightAtMm(...))`, but everything that *stands* on the
ground - authored dressing, the ravine's rock, the den gorge walls, the ravine river - asked `SceneryHeight`, which returned the plain
`ground.Height`. Wherever a water shore carves the bed, the mesh is lower than the field by the cut, so the piece stands that far in the air.
"""
from pathlib import Path

PATH = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Greybox\Terrain3DView.cs')
text = PATH.read_text(encoding='utf-8')

FIELD_ANCHOR = '    private static readonly int[] SlotLayer = { 3, 0, 2, 1 };'
FIELD_NEW = (FIELD_ANCHOR + '\n\n'
             '    /// <summary>The grid the imported mesh is built from, so anything standing on the ground can read the drawn surface.</summary>\n'
             '    private static TerrainGrid? _drawn;')

BIND_OLD = '        int rocks = RavineDressing.Dress(parent, art, ground.Region, (x, z) => SceneryHeight(ground, x, z), art.Coverage);'
BIND_NEW = ('        _drawn = grid;\n'
            '        int rocks = RavineDressing.Dress(parent, art, ground.Region, (x, z) => SceneryHeight(ground, x, z), art.Coverage);')

HEIGHT_OLD = '''    public static float SceneryHeight(GroundField ground, float x, float z)
    {
        var r = ground.Region;
        bool inside = x >= r.Position.X && x <= r.End.X && z >= r.Position.Y && z <= r.End.Y;
        return inside ? ground.Height(x, z) : Outside(ground, x, z, Noise);
    }'''

HEIGHT_NEW = '''    public static float SceneryHeight(GroundField ground, float x, float z)
    {
        var r = ground.Region;
        bool inside = x >= r.Position.X && x <= r.End.X && z >= r.Position.Y && z <= r.End.Y;
        if (!inside)
            return Outside(ground, x, z, Noise);
        // THE SAME SURFACE THE MESH IS BUILT FROM. Images() bakes ground.Carved(domain height), so anything placed on the plain domain
        // height stands by the cut wherever a water shore lowered the bed - metres over a creek, more over a lake. That is what a rock
        // floating above the ground looks like from inside the world.
        float drawn = _drawn is { } grid
            ? grid.HeightAtMm((long)MathF.Round(x * 1000), (long)MathF.Round(z * 1000)) / 1000f
            : ground.Height(x, z);
        return ground.Carved(x, z, drawn);
    }'''

for old, new, label in ((FIELD_ANCHOR, FIELD_NEW, 'field'), (BIND_OLD, BIND_NEW, 'bind'), (HEIGHT_OLD, HEIGHT_NEW, 'height')):
    if new.split('\n')[0] in text and label != 'height':
        print(label, 'already applied')
        continue
    assert old in text, f'anchor missing: {label}'
    text = text.replace(old, new, 1)
    print(label, 'patched')

PATH.write_text(text, encoding='utf-8')
