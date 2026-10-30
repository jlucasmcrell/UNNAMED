# Downscale the embedded textures of the five farm-overlay species so a packaged build accepts them (the 2K rule blocked the farm run).
$python = 'C:\Python314\python.exe'
$root = 'C:\Otherreach_Farm\builds\claude_fauna01\game\animals\rigged'
$shrinker = 'D:\Otherreach_Scratch\ds_fish2_wt\work\shrink_glb_textures.py'
foreach ($name in @('elk','duck','crow','trout')) {
    $file = "$root\creature_wm_$name\creature_wm_$name" + "_rigged.glb"
    if (-not (Test-Path $file)) { "missing $name"; continue }
    & $python -X utf8 $shrinker $file 2>&1 | Select-Object -First 2
    $mb = [math]::Round((Get-Item $file).Length / 1MB, 1)
    "$name : $mb MB"
}
