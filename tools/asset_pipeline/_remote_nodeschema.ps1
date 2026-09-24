# Dump the declared input schema for the concept-generation nodes on a ComfyUI server.
# Written as a file because nested quoting through ssh/pwsh mangles inline PowerShell.
$ErrorActionPreference = 'Stop'
$server = if ($args.Count -ge 1) { $args[0] } else { 'http://127.0.0.1:18190' }
$info = Invoke-RestMethod "$server/object_info"
foreach ($node in @('DiffusionModelLoaderKJ', 'CLIPLoader', 'VAELoader',
                    'Z_ImageAPIConfig', 'Z_ImageIntegratedKSampler')) {
    Write-Output "=== $node ==="
    if (-not $info.$node) { Write-Output "  NOT PRESENT"; continue }
    $req = $info.$node.input.required
    $opt = $info.$node.input.optional
    if ($req) {
        foreach ($k in $req.PSObject.Properties.Name) {
            $v = $req.$k
            $type = if ($v -is [array]) { $v[0] } else { $v }
            if ($type -is [array]) { $type = 'COMBO(' + ($type -join ',') + ')' }
            $default = ''
            if ($v -is [array] -and $v.Count -gt 1 -and $v[1] -is [hashtable] -and $v[1].ContainsKey('default')) {
                $default = ' default=' + $v[1]['default']
            }
            Write-Output ("  REQ " + $k + " : " + $type + $default)
        }
    }
    if ($opt) {
        foreach ($k in $opt.PSObject.Properties.Name) {
            $v = $opt.$k
            $type = if ($v -is [array]) { $v[0] } else { $v }
            if ($type -is [array]) { $type = 'COMBO(' + ($type -join ',') + ')' }
            Write-Output ("  OPT " + $k + " : " + $type)
        }
    }
    Write-Output ("  outputs: " + ($info.$node.output -join ','))
}
