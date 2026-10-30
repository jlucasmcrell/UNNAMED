# Submit the whole fauna capture set to the 6900XT farm, one job per place, all on the cheap build.
# Each job stands 12-15 m from its spawn anchor looking west (the framing that has worked), and writes frames to results\<job>\.
# Nothing here needs the gpu_lock: the farm has its own GPU and runs one job at a time.
param([switch]$IncludeDone)
$ErrorActionPreference = 'Stop'
$farm = 'C:\Otherreach_Farm'
$build = 'claude_min'

# place name, stand x, stand z, yaw, pitch, what it should show
$places = @(
    @{ name = 'claude_cap_moor_crows';   xy = '1012,690,90,-0.05,6';  about = 'five crows on the open moor east of the village' },
    @{ name = 'claude_cap_charwood_deer'; xy = '628,722,90,-0.12,6';   about = 'the Charwood hind and stag band' },
    @{ name = 'claude_cap_elk_band';      xy = '1024,644,90,-0.08,6';  about = 'the western elk band' },
    @{ name = 'claude_cap_sedge_ducks';   xy = '4882,740,90,-0.18,6';  about = 'mallards on the sedge pools' },
    @{ name = 'claude_cap_oldlate_hares'; xy = '656,726,90,-0.10,6';   about = 'hares beside the Old Late road' },
    @{ name = 'claude_cap_field_deer';    xy = '852,768,90,-0.12,6';   about = 'deer on the field edge by the gate' },
    @{ name = 'claude_cap_millpond';      xy = '978,706,90,-0.18,6';   about = 'mallards on the millpond and trout in the race' }
)

$template = @'
{
"build": "claude_min",
"exe": "game/Otherreach.exe",
"args": [
"--resolution", "1280x720", "--windowed", "--",
"--region", "region.starting_frontier",
"--content-root", "C:/Otherreach_Test/claude_min/game/content",
"--bindings-overlay", "C:/Otherreach_Test/claude_min/game/content/_art_bindings.json",
"--asset-root", "C:/Otherreach_Test/f1_gate_d_20261029_01/primary",
"--asset-fallback", "C:/Otherreach_Test/claude_min/game/animals",
"--scatter-rules", "C:/Otherreach_Test/claude_min/game/content/_scatter_rules.json",
"--profile", "{OUT}/profile",
"--shot-out", "{OUT}",
"--shot", "SHOT",
"--visual", "terrain=terrain3d,sky=sky3d,clouds=none,plants=classic,aa=off,tier=low,wind=none,vfx=flipbooks,life=none,weather=fair,textures=cache"
],
"timeout_s": 900,
"record": false,
"env": {
"OTHERREACH_CHARACTER_ROOT": "{BUILD}/game/humgen_characters",
"OTHERREACH_LIGHTING_FAST": "1"
}
}
'@

foreach ($place in $places) {
    $job = Join-Path $farm "jobs\$($place.name).json"
    $done = Join-Path $farm "jobs\$($place.name).running"
    if (-not $IncludeDone -and ((Test-Path $job) -or (Test-Path $done))) { "skip $($place.name) (already queued or running)"; continue }
    Set-Content $job -Value $template.Replace('SHOT', $place.xy) -Encoding UTF8
    "queued $($place.name): $($place.about) at $($place.xy)"
}
