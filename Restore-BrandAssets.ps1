$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bundleDirectory = Join-Path $root 'BrandAssets'
$parts = @(Get-ChildItem $bundleDirectory -Filter 'assets.part*.b64' | Sort-Object Name)
if ($parts.Count -eq 0) {
    throw 'The supplemental InNasc branding bundle is missing.'
}

$base64 = ($parts | ForEach-Object { (Get-Content $_.FullName -Raw).Trim() }) -join ''
$zipPath = Join-Path $env:TEMP ("InNascBrandAssets-{0}.zip" -f [guid]::NewGuid().ToString('N'))
$assetsPath = Join-Path $root 'Assets'

try {
    [IO.File]::WriteAllBytes($zipPath, [Convert]::FromBase64String($base64))
    New-Item -ItemType Directory -Force -Path $assetsPath | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $assetsPath -Force

    $required = @(
        'AVMatrixStudio.ico',
        'AVMatrixStudioLogo.png',
        'InN8LabsMascot.png',
        'N8LogoDark.png',
        'N8LogoLight.png'
    )
    foreach ($name in $required) {
        if (-not (Test-Path (Join-Path $assetsPath $name))) {
            throw "Recovered production brand asset is missing: $name"
        }
    }

    Write-Host 'InNasc supplemental branding restored.'
}
finally {
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
}
