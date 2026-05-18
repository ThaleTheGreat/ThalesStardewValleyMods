$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceRoot = Split-Path -Parent $ProjectDir
$ReleaseRoot = Join-Path $SourceRoot "Release"
$ReleaseParent = Join-Path $ReleaseRoot "Festival of the Mundane Redux v3.1.0"
$ModOutputDir = Join-Path $ReleaseParent "Festival of the Mundane Redux"
$CpSourceDir = Join-Path $SourceRoot "[CP] Festival of the Mundane Redux"
$CpOutputDir = Join-Path $ReleaseParent "[CP] Festival of the Mundane Redux"
$BuildDir = Join-Path $ProjectDir "bin\Release\net6.0"

Write-Host "Building Festival of the Mundane Redux..."
dotnet build $ProjectDir -c Release

if (Test-Path $ReleaseRoot) {
    Remove-Item $ReleaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ModOutputDir | Out-Null

$files = @(
    "manifest.json",
    "ShadowFestival.dll",
    "ShadowFestival.pdb",
    "LICENSE",
    "data.json"
)

foreach ($file in $files) {
    $source = Join-Path $BuildDir $file
    if (Test-Path $source) {
        Copy-Item $source $ModOutputDir -Force
    }
}

$assetsSource = Join-Path $BuildDir "assets"
if (Test-Path $assetsSource) {
    Copy-Item $assetsSource $ModOutputDir -Recurse -Force
}

$i18nSource = Join-Path $BuildDir "i18n"
if (Test-Path $i18nSource) {
    Copy-Item $i18nSource $ModOutputDir -Recurse -Force
}

Copy-Item $CpSourceDir $CpOutputDir -Recurse -Force

Write-Host "Release folder created:"
Write-Host "  $ReleaseParent"
Write-Host "Install the two folders inside it together."
