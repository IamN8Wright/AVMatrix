param([switch]$TestsOnly)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$qcDir = Join-Path $PSScriptRoot 'artifacts\qc'
New-Item -ItemType Directory -Force -Path $qcDir | Out-Null
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 SDK, then run QC again.' }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Restore-BrandAssets.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Branding restore failed.' }
$qcLog = Join-Path $qcDir 'tests.txt'
$qcProjects = @('tests\InNasc.CoreTests\InNasc.CoreTests.csproj', 'tests\InNasc.SmokeTests\InNasc.SmokeTests.csproj', 'tests\InNasc.GlobalAdmin.SmokeTests\InNasc.GlobalAdmin.SmokeTests.csproj')
Set-Content -Path $qcLog -Value 'InNasc QC test log'
foreach ($qcProject in $qcProjects) {
    & dotnet run --project $qcProject --configuration Release 2>&1 | Tee-Object -FilePath $qcLog -Append
    if ($LASTEXITCODE -ne 0) { throw "QC failed: $qcProject. See artifacts\qc\tests.txt." }
}
if ($TestsOnly) { Write-Host 'Automated tests passed. LLM review and a real 20-minute cloud soak remain separate checks.'; exit 0 }
if (-not (Get-Command codex -ErrorAction SilentlyContinue)) { throw 'Tests passed. Install and sign in to Codex CLI to run the LLM reviewer. See qc\README.md.' }
$qcReport = Join-Path $qcDir 'review.json'
# Remove the previous report so an unsuccessful run cannot appear to have passed.
Remove-Item -Path $qcReport -Force -ErrorAction SilentlyContinue
$qcPrompt = (Get-Content 'qc\review-prompt.txt' -Raw) + "`n`nVerified test output:`n" + (Get-Content $qcLog -Raw)
$qcPrompt | & codex exec --ephemeral --sandbox read-only --output-schema 'qc\review.schema.json' -o $qcReport -
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $qcReport)) { throw 'LLM QC did not complete. No passing review was recorded.' }
$qcResult = Get-Content $qcReport -Raw | ConvertFrom-Json
if ($qcResult.status -ne 'pass' -or $qcResult.blocking_findings.Count -ne 0 -or $qcResult.remaining_validation.Count -ne 0) {
    throw 'QC needs attention. Open artifacts\qc\review.json for the findings and remaining validation.'
}
Write-Host 'Automated tests and LLM review passed. See artifacts\qc\review.json.'
