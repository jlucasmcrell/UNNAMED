param(
    [string[]]$Parts=@('lamework_jack','lame_coif','lame_sabatons'),
    [switch]$NoRender
)
$ErrorActionPreference='Stop'
$root='D:\Otherreach_Scratch\frontier_native_20261018'
$env:OTHERREACH_REGION_CANDIDATE="$root\region_candidate3"
$blender='C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
python $PSScriptRoot\split_standard_body_regions.py
if($LASTEXITCODE -ne 0){throw 'Body region staging failed'}
foreach($fit in @('veth_m_lean','veth_f','veth_m','veth_f_lean')) {
    if($fit -ne 'veth_f') {
        & $blender --background --threads 2 --factory-startup --python-exit-code 1 --python "$PSScriptRoot\bl_transfer_outfit.py" -- $fit @Parts *> "$root\auto_transfer_$fit.log"
        if($LASTEXITCODE -ne 0){throw "Surface transfer failed:$fit"}
    }
    $env:OTHERREACH_CLOTHING_BODY=$fit
    python $PSScriptRoot\stage_approved_garments.py $fit
    if($LASTEXITCODE -ne 0){throw "Source staging failed:$fit"}
    & $blender --background --threads 2 --factory-startup --python-exit-code 1 --python "$PSScriptRoot\bl_combine_frontier_sets.py" *> "$root\auto_combined_$fit.log"
    if($LASTEXITCODE -ne 0){throw "Combined set bake failed:$fit"}
    python $PSScriptRoot\stage_approved_garments.py $fit
    if($LASTEXITCODE -ne 0){throw "Combined staging failed:$fit"}
    python $PSScriptRoot\stage_region_outfits.py "mh_$fit"
    if($LASTEXITCODE -ne 0){throw "Outfit staging failed:$fit"}
    Write-Output "FINISHED: automated outfit pass for $fit; visual acceptance pending"
    if(!$NoRender) {
        & $PSScriptRoot\run_revised_wardrobe_capture.ps1 -Tag production -Candidate region_candidate3 -Fit "mh_$fit"
        foreach($variant in @('frontier','armour')) {
            python $PSScriptRoot\wardrobe_contact.py "$root\region_candidate3\shots_production_mh_$($fit)_$variant" "mh_$fit"
            if($LASTEXITCODE -ne 0){throw "Contact sheet failed:$fit/$variant"}
        }
        # The shared GPU helper requires a cooldown between this lane's holds.
        if($fit -ne 'veth_f_lean'){Start-Sleep -Seconds 60}
    }
}
