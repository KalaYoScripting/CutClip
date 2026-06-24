param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repoRoot "artifacts\build\CutClip.exe"

if (-not (Test-Path $exe)) {
    Write-Error @"
CutClip.exe not found at:
  $exe

Publish the app first:
  dotnet publish src/CutClip/CutClip.csproj -c Release -o ./artifacts/build
"@
}

$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Error @"
Inno Setup 6 not found.

Install from: https://jrsoftware.org/isinfo.php
Or with winget:  winget install --id JRSoftware.InnoSetup
"@
}

Write-Host "Building CutClip-Setup-$Version.exe ..."
& $iscc "$PSScriptRoot\CutClip.iss" "/DMyAppVersion=$Version"

$output = Join-Path $repoRoot "artifacts\installer\CutClip-Setup-$Version.exe"
Write-Host ""
Write-Host "Installer created:"
Write-Host "  $output"
