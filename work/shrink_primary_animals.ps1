# Downscale the five species' textures inside the farm's shared primary root, so every build on the farm can draw them without the 2K rule firing.
$python = 'C:\Python314\python.exe'
$shrinker = 'D:\Otherreach_Scratch\ds_fish2_wt\work\shrink_glb_textures.py'
$root = 'C:\Otherreach_Farm\builds\f1_gate_d_20261029_01\primary\rigged'
foreach ($name in @('elk','moose','duck','crow','trout')) {
    $file = "$root\creature_wm_$name\creature_wm_$name" + "_rigged.glb"
    if (-not (Test-Path $file)) { "missing $name"; continue }
    & $python -X utf8 $shrinker $file 2>&1 | Select-Object -First 1
    $mb = [math]::Round((Get-Item $file).Length / 1MB, 1)
    "$name : $mb MB"
}
