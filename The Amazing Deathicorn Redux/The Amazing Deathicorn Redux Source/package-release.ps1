$ErrorActionPreference = "Stop"

function Invoke-DotNetBuild {
    param([string]$ProjectFile)

    & dotnet build $ProjectFile -c Release --no-incremental
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

function Require-Path {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required path not found: $Path"
    }
}

function Require-MatchingVersion {
    param(
        [string]$MainManifest,
        [string[]]$PackManifests
    )

    $mainVersion = (Get-Content -LiteralPath $MainManifest -Raw | ConvertFrom-Json).Version
    foreach ($packManifest in $PackManifests) {
        $packVersion = (Get-Content -LiteralPath $packManifest -Raw | ConvertFrom-Json).Version
        if ($packVersion -ne $mainVersion) {
            throw "Version mismatch: $packManifest is $packVersion but the main mod is $mainVersion."
        }
    }
}


function Get-GameModsPath {
    param([string]$ProjectFile)

    $pathFile = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
    try {
        & dotnet msbuild $ProjectFile -nologo -t:WriteGameModsPath "-p:GameModsPathOutputFile=$pathFile"
        if ($LASTEXITCODE -ne 0) {
            throw "Could not resolve the Stardew Valley Mods folder."
        }

        Require-Path $pathFile
        $modsPath = (Get-Content -LiteralPath $pathFile -Raw).Trim()
        if ([string]::IsNullOrWhiteSpace($modsPath)) {
            throw "The Stardew Valley Mods folder could not be detected."
        }

        return $modsPath
    }
    finally {
        if (Test-Path -LiteralPath $pathFile) {
            Remove-Item -LiteralPath $pathFile -Force
        }
    }
}

function Install-ReleasePackage {
    param(
        [string]$ProjectFile,
        [string]$ReleasePackage
    )

    Require-Path $ReleasePackage
    $modsPath = Get-GameModsPath $ProjectFile
    New-Item -ItemType Directory -Path $modsPath -Force | Out-Null

    $installDir = Join-Path $modsPath (Split-Path -Leaf $ReleasePackage)
    if (Test-Path -LiteralPath $installDir) {
        Remove-Item -LiteralPath $installDir -Recurse -Force
    }

    Copy-Item -LiteralPath $ReleasePackage -Destination $installDir -Recurse -Force
    Assert-MatchingTrees $ReleasePackage $installDir

    Write-Host "Verified complete release package installed:"
    Write-Host "  $installDir"
}

function Copy-RequiredFile {
    param(
        [string]$Source,
        [string]$Destination
    )

    Require-Path $Source
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Assert-MatchingTrees {
    param(
        [string]$Source,
        [string]$Destination
    )

    $sourceRoot = (Resolve-Path -LiteralPath $Source).Path.TrimEnd([char[]]'\/')
    $destinationRoot = (Resolve-Path -LiteralPath $Destination).Path.TrimEnd([char[]]'\/')

    $sourceFiles = @{}
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        $relative = $file.FullName.Substring($sourceRoot.Length).TrimStart([char[]]'\/')
        $sourceFiles[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }

    $destinationFiles = @{}
    foreach ($file in Get-ChildItem -LiteralPath $destinationRoot -Recurse -File) {
        $relative = $file.FullName.Substring($destinationRoot.Length).TrimStart([char[]]'\/')
        $destinationFiles[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }

    if ($sourceFiles.Count -ne $destinationFiles.Count) {
        throw "Packaged folder does not match source: $Destination"
    }

    foreach ($relative in $sourceFiles.Keys) {
        if (-not $destinationFiles.ContainsKey($relative) -or $destinationFiles[$relative] -ne $sourceFiles[$relative]) {
            throw "Packaged file is missing or stale: $relative"
        }
    }
}

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceRoot = Split-Path -Parent $ProjectDir
$ReleaseRoot = Join-Path $SourceRoot "Release"
$ReleaseParent = Join-Path $ReleaseRoot "The Amazing Deathicorn Redux"
$ModOutputDir = Join-Path $ReleaseParent "The Amazing Deathicorn Redux"
$CpSourceDir = Join-Path $SourceRoot "[CP] The Amazing Deathicorn Redux"
$CpOutputDir = Join-Path $ReleaseParent "[CP] The Amazing Deathicorn Redux"
$ProjectFile = Join-Path $ProjectDir "TheAmazingDeathicornRedux.csproj"
$BuildDir = Join-Path $ProjectDir "bin\Release\net6.0"
$MainManifest = Join-Path $ProjectDir "manifest.json"

Require-Path $CpSourceDir
Require-MatchingVersion -MainManifest $MainManifest -PackManifests @(
    (Join-Path $CpSourceDir "manifest.json")
)

foreach ($path in @((Join-Path $ProjectDir "bin"), (Join-Path $ProjectDir "obj"), $ReleaseRoot)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

Write-Host "Building The Amazing Deathicorn Redux..."
Invoke-DotNetBuild $ProjectFile

New-Item -ItemType Directory -Path $ModOutputDir -Force | Out-Null

foreach ($file in @("manifest.json", "TheAmazingDeathicornRedux.dll", "LICENSE", "config.json")) {
    Copy-RequiredFile (Join-Path $BuildDir $file) $ModOutputDir
}

$pdb = Join-Path $BuildDir "TheAmazingDeathicornRedux.pdb"
if (Test-Path -LiteralPath $pdb) {
    Copy-Item -LiteralPath $pdb -Destination $ModOutputDir -Force
}

$assetsSource = Join-Path $BuildDir "assets"
Require-Path $assetsSource
Copy-Item -LiteralPath $assetsSource -Destination (Join-Path $ModOutputDir "assets") -Recurse -Force

Copy-Item -LiteralPath $CpSourceDir -Destination $CpOutputDir -Recurse -Force
Assert-MatchingTrees $CpSourceDir $CpOutputDir

Write-Host "Verified release folder created:"
Write-Host "  $ReleaseParent"

Install-ReleasePackage -ProjectFile $ProjectFile -ReleasePackage $ReleaseParent
