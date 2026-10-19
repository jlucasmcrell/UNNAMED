param(
    [string[]]$Parts=@('lamework_jack','lame_coif','lame_sabatons'),
    [switch]$NoRender,
    [string]$Tag='standard5',
    [string]$Candidate='region_candidate5',
    [string[]]$Fits=@('veth_f','veth_m','veth_f_lean','veth_m_lean')
)
$ErrorActionPreference='Stop'
$root='D:\Otherreach_Scratch\frontier_native_20261018'
$env:OTHERREACH_REGION_CANDIDATE="$root\$Candidate"
$blender='C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
New-Item -ItemType Directory -Path "$root\$Candidate" -Force | Out-Null
$writer=[IO.File]::Open("$root\$Candidate\.pipeline.lock",[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
$models=@($Fits | ForEach-Object { "mh_$_" })
python $PSScriptRoot\split_standard_body_regions.py @models
if($LASTEXITCODE -ne 0){throw 'Body region staging failed'}
foreach($fit in $Fits) {
    if($fit -ne 'veth_f') {
        & $blender --background --threads 2 --factory-startup --python-exit-code 1 --python $PSScriptRoot\bl_transfer_outfit.py -- $fit @Parts *> "$root\auto_transfer_$fit.log"
        if($LASTEXITCODE -ne 0){throw "Native wrap transfer failed:$fit"}
    }
    $env:OTHERREACH_CLOTHING_BODY=$fit
    python $PSScriptRoot\stage_approved_garments.py $fit
    if($LASTEXITCODE -ne 0){throw "Source staging failed:$fit"}
    # Loose gloves/mantle are not inputs to the combined torso/head/foot sets.
    if(@($Parts | Where-Object { $_ -notin @('gloves','cloak') }).Count -gt 0 -or !(Test-Path "$root\combined_sources\$fit\frontier_linen_trousers_set_native.glb")) {
    $env:OTHERREACH_COMBINE_PARTS=if(Test-Path "$root\combined_sources\$fit\frontier_linen_trousers_set_native.glb") { ConvertTo-Json -InputObject @($Parts) -Compress } else { '[]' }
    & $blender --background --threads 2 --factory-startup --python-exit-code 1 --python $PSScriptRoot\bl_combine_frontier_sets.py *> "$root\auto_combined_$fit.log"
    if($LASTEXITCODE -ne 0){throw "Combined set bake failed:$fit"}
    }
    python $PSScriptRoot\stage_approved_garments.py $fit
    if($LASTEXITCODE -ne 0){throw "Combined staging failed:$fit"}
    python $PSScriptRoot\stage_region_outfits.py "mh_$fit"
    if($LASTEXITCODE -ne 0){throw "Outfit staging failed:$fit"}
    Write-Output "FINISHED: automated outfit pass for $fit; visual acceptance pending"
    if(!$NoRender) {
        & $PSScriptRoot\run_revised_wardrobe_capture.ps1 -Tag $Tag -Candidate $Candidate -Fit "mh_$fit"
        foreach($variant in @('frontier','armour')) {
            python $PSScriptRoot\wardrobe_contact.py "$root\$Candidate\shots_$($Tag)_mh_$($fit)_$variant" "mh_$fit"
            if($LASTEXITCODE -ne 0){throw "Contact sheet failed:$fit/$variant"}
        }
        # The shared GPU helper requires a cooldown between this lane's holds.
        if($fit -ne $Fits[-1]){Start-Sleep -Seconds 60}
    }
}

} finally { $writer.Dispose() }
