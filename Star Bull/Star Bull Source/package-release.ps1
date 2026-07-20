$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceRoot = Split-Path -Parent $ProjectDir
$ReleaseRoot = Join-Path $SourceRoot "Release"
$ReleaseParent = Join-Path $ReleaseRoot "Star Bull"
$ModOutputDir = Join-Path $ReleaseParent "Star Bull"
$TmfSourceDir = Join-Path $SourceRoot "The Muttering Farmer - Star Bull"
$TmfOutputDir = Join-Path $ReleaseParent "The Muttering Farmer - Star Bull"
$BuildDir = Join-Path $ProjectDir "bin\Release\net6.0"

Write-Host "Building Star Bull..."
dotnet build $ProjectDir -c Release

if (Test-Path $ReleaseRoot) {
    Remove-Item $ReleaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ModOutputDir | Out-Null

$files = @(
    "manifest.json",
    "StarBull.dll",
    "StarBull.pdb",
    "LICENSE"
)

foreach ($file in $files) {
    $source = Join-Path $BuildDir $file
    if (Test-Path $source) {
        Copy-Item $source $ModOutputDir -Force
    }
}

Copy-Item $TmfSourceDir $TmfOutputDir -Recurse -Force

Write-Host "Release folder created:"
Write-Host "  $ReleaseParent"
Write-Host "Install both folders inside it together."
