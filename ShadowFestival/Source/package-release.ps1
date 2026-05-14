$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ReleaseRoot = Join-Path (Split-Path -Parent $ProjectDir) "Release"
$OutputDir = Join-Path $ReleaseRoot "ShadowFestival"
$BuildDir = Join-Path $ProjectDir "bin\Release\net6.0"

Write-Host "Building ShadowFestival..."
dotnet build $ProjectDir -c Release

if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

$files = @(
    "manifest.json",
    "ShadowFestival.dll",
    "ShadowFestival.pdb",
    "data.json",
    "LICENSE"
)

foreach ($file in $files) {
    $source = Join-Path $BuildDir $file
    if (Test-Path $source) {
        Copy-Item $source $OutputDir -Force
    }
}

Copy-Item (Join-Path $BuildDir "assets") $OutputDir -Recurse -Force
Copy-Item (Join-Path $BuildDir "i18n") $OutputDir -Recurse -Force

Write-Host "Release folder created: $OutputDir"
Write-Host "This is the folder to install or zip for Nexus."
