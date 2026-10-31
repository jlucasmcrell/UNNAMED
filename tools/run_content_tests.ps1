# Content.Tests in two batches, because one run takes longer than a gate should.
#
# The cause is measured, not guessed: a full load of `content/` costs about 62.5 s here (`UNNAMED.Content lint` loads once and is
# a minute), the read-only tests share one such load (`GameContentFixture`), and every edited-content test pays its own - it
# applies one edit to a hard-linked tree and parses the whole content again. Run end to end that is hours, and before this the run
# used to die of it: collections ran in parallel, several loads were alive at once, the host was seen at 2-9 GB and left a 9.2 GB
# crash dump, after which `dotnet test` sat silent for twenty minutes to an hour and a half. That silence is what the gates were
# waiting on.
#
# Two batches, so each one reports:
#   read      - the tests that read the game's own content through the shared loader
#   edited    - the classes whose tests edit a copy of the content tree
#   all       - both, sequentially (the default)
#
# Usage:  powershell -File tools\run_content_tests.ps1 [-Batch read|edited|all]
param([ValidateSet('read', 'edited', 'all')][string]$Batch = 'all')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\Content.Tests'

# Tests that edit a copy of content/ (they use EditedContent): each of these pays a full load.
$edited = @(
    'CharacterContentTests', 'CombatContentTests', 'CraftingContentTests', 'ItemizationContentTests', 'MagicContentTests',
    'NpcLifeLinksContentTests', 'QuestContentTests', 'SocialContentTests', 'TeachingContentTests', 'WorldContentTests'
)

function Invoke-Batch {
    param([string]$Name, [string]$Filter)

    Write-Host "== $Name ==" -ForegroundColor Cyan
    $log = Join-Path $env:TEMP "content-tests-$Name.log"
    $started = Get-Date
    & dotnet test $project --nologo -v q --filter $Filter *> $log
    $code = $LASTEXITCODE
    $verdict = (Select-String -Path $log -Pattern 'Passed!|Failed!|Aborted' | Select-Object -First 1).Line
    $minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
    Write-Host "$Name : ${minutes}m : $verdict"
    if ($code -ne 0) { Write-Host "  (log: $log)" -ForegroundColor Yellow }
    return $code
}

$failed = 0
if ($Batch -ne 'edited') {
    $filter = ($edited | ForEach-Object { "FullyQualifiedName!~$_" }) -join '&'
    if ((Invoke-Batch -Name 'read' -Filter $filter) -ne 0) { $failed++ }
}
if ($Batch -ne 'read') {
    $filter = ($edited | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
    if ((Invoke-Batch -Name 'edited' -Filter $filter) -ne 0) { $failed++ }
}

exit $failed
