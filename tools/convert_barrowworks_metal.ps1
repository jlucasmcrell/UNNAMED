param([string]$Depot = 'D:\Otherreach_Depot\barrowworks_codex', [string]$Scratch = 'D:\Otherreach_Scratch\barrowworks\codex')
$ErrorActionPreference = 'Stop'
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$converter = Join-Path $PSScriptRoot 'asset_pipeline\convert_asset.py'
$root = 'C:\Otherreach_External_Assets\Sketchfab_20261016'
$jobs = @(
    @{ Id='bw_codex_boiler'; Folder='ScottishMaritimeMuseum__Boiler_from_the_puffer_VIC_32'; Mesh='default'; Height='1.5'; Triangles='60000'; Licence='CC0 1.0'; Url='https://sketchfab.com/3d-models/884ed11e38054aa48a3db1d9025c881d' },
    @{ Id='bw_codex_pipe'; Folder='SLASH-RENDAR__Old_Industrial_Pipe_Pack_PBR'; Mesh='Pipe_2m_Modular'; Height='1.0'; Triangles='8000'; Licence='CC-BY 4.0; SLASH-RENDAR'; Url='https://sketchfab.com/3d-models/a10a6011334e4d11a7023d67705ac1d4' },
    @{ Id='bw_codex_valve'; Folder='SLASH-RENDAR__Old_Industrial_Pipe_Pack_PBR'; Mesh='Pipe_valve_Modular'; Height='0.5'; Triangles='8000'; Licence='CC-BY 4.0; SLASH-RENDAR'; Url='https://sketchfab.com/3d-models/a10a6011334e4d11a7023d67705ac1d4' },
    @{ Id='bw_codex_elbow'; Folder='SLASH-RENDAR__Old_Industrial_Pipe_Pack_PBR'; Mesh='Pipe_90_degree_1_Modular'; Height='0.5'; Triangles='8000'; Licence='CC-BY 4.0; SLASH-RENDAR'; Url='https://sketchfab.com/3d-models/a10a6011334e4d11a7023d67705ac1d4' }
)
foreach ($job in $jobs) {
    $sourceFolder = Join-Path "$Scratch\sources" $job.Folder
    if (-not (Test-Path -LiteralPath $sourceFolder)) {
        Expand-Archive -LiteralPath "$root\$($job.Folder)\gltf.zip" -DestinationPath $sourceFolder
    }
    $sourceFile = Get-ChildItem -LiteralPath $sourceFolder -Recurse -Filter '*.gltf' | Select-Object -First 1 -ExpandProperty FullName
    $ErrorActionPreference = 'Continue' # Blender's deprecation warnings are stderr; exit status is authoritative.
    & $blender --background --factory-startup --threads 4 --python-exit-code 1 --python $converter -- `
        --source $sourceFile --asset-id $job.Id --mesh $job.Mesh --target-height $job.Height `
        --max-tris $job.Triangles --res 2048 --save-blend --out "$Depot\ready" --work "$Scratch\convert" `
        --licence $job.Licence --licence-evidence "$root\$($job.Folder)\LICENCE.txt" --source-url $job.Url `
        *> "$Scratch\convert-$($job.Id).log"
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) { throw "Conversion failed: $($job.Id)" }
}
