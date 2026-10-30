"""Probe the scatter once its chunks exist: at load time it has placed nothing yet, which is why the instance count came back 0 of 0."""
from pathlib import Path

PATH = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.cs')
text = PATH.read_text(encoding='utf-8')

OLD = '''        if (_ground is { } scatterGround)
            SceneryProbe.ReportInstances(_scatter, (x, z) => Terrain3DView.SceneryHeight(scatterGround, x, z), "scatter");'''

NEW = '''        if (_ground is { } scatterGround)
        {
            // Chunks are placed as the camera asks for them, so the probe runs two seconds in rather than at load, where it counted nothing.
            var probe = new Timer { Name = "SceneryProbe", OneShot = true, WaitTime = 2.0, Autostart = true };
            probe.Timeout += () => SceneryProbe.ReportInstances(_scatter, (x, z) => Terrain3DView.SceneryHeight(scatterGround, x, z), "scatter");
            AddChild(probe);
        }'''

if 'var probe = new Timer' in text:
    print('already deferred')
else:
    assert OLD in text, 'anchor missing'
    PATH.write_text(text.replace(OLD, NEW, 1), encoding='utf-8')
    print('scatter probe deferred by two seconds')
