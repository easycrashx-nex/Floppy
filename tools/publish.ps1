<#
.SYNOPSIS
Builds and checks both complete Floppy Windows releases without replacing existing releases.
.EXAMPLE
.\tools\publish.ps1 -WhatIf
.EXAMPLE
.\tools\publish.ps1 -ArtifactsPath .\artifacts\release-build
.NOTES
Requires the .NET SDK, vendored loaders, and game build references configured in
Directory.Build.local.props or the optional game-directory parameters.
The package checks use offline self-tests and synthetic installer fixtures only.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ArtifactsPath,
    [string]$GameDir,
    [string]$StonewardsDir,
    [string]$OddcoreDir
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $projectRoot 'src\Floppy.App\Floppy.App.csproj'
[xml]$projectXml = Get-Content -LiteralPath $appProject -Raw
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode) { throw 'App.csproj has no explicit Version.' }
$version = $versionNode.InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(?:[.-][0-9A-Za-z.-]+)?$') { throw "Invalid release version: $version" }

$releaseRoot = Join-Path $projectRoot 'releases'
$packageName = "Floppy-$version-win-x64"
$portableName = "Floppy-$version-Portable.exe"
$packageTarget = Join-Path $releaseRoot $packageName
$zipTarget = Join-Path $releaseRoot ($packageName + '.zip')
$portableTarget = Join-Path $releaseRoot $portableName
$targets = @($packageTarget, $zipTarget, ($zipTarget + '.sha256'), $portableTarget, ($portableTarget + '.sha256'))

function Assert-NewReleaseTargets {
    foreach ($target in $targets) {
        if (Test-Path -LiteralPath $target) { throw "Release target already exists; nothing will be overwritten: $target" }
    }
}

function Assert-Within([string]$Path, [string]$Root) {
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside its intended directory: $resolved"
    }
}

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

function Test-PackageExe([string]$Exe, [string]$Report) {
    $process = Start-Process -FilePath $Exe -ArgumentList @('--self-test', ('"' + $Report + '"')) `
        -WorkingDirectory ([IO.Path]::GetDirectoryName($Exe)) -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(60000)) {
            $process.Kill()
            $process.WaitForExit(5000) | Out-Null
            throw "EXE self-test timed out: $Exe"
        }
        $process.Refresh()
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $Report)) {
            throw "EXE self-test failed (exit $($process.ExitCode)); report: $Report"
        }
        $text = Get-Content -LiteralPath $Report -Raw
        if ($text -notmatch '(?m)^Fehler:\s*0\s*$' -or $text -match '(?m)^FAIL ') {
            throw "EXE self-test reported a failure: $Report"
        }
    }
    finally { $process.Dispose() }
}

function Write-Sha256([string]$Payload, [string]$Destination, [string]$Name) {
    $hash = (Get-FileHash -LiteralPath $Payload -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $Destination -Value ($hash + '  ' + $Name) -Encoding ascii
}

function Test-UpdateHelperDispatch([string]$Exe) {
    # Missing helper arguments must exit before normal UI startup; this writes no update plan or result.
    $process = Start-Process -FilePath $Exe -ArgumentList '--apply-update' -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(60000)) {
            $process.Kill()
            throw 'The update helper did not exit on missing arguments.'
        }
        $process.Refresh()
        if ($process.ExitCode -ne 2) { throw "Update helper dispatch failed (exit $($process.ExitCode))." }
    }
    finally { $process.Dispose() }
}

Assert-NewReleaseTargets
foreach ($required in 'README.md', 'docs', 'src\Floppy.App\Properties\PublishProfiles\Portable.pubxml',
        'tools\test-portable.ps1', 'tests\Floppy.Install.Tests\Floppy.Install.Tests.csproj') {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $required))) { throw "Required release input is missing: $required" }
}
Get-Command dotnet -ErrorAction Stop | Out-Null
if (-not $PSCmdlet.ShouldProcess($releaseRoot, "Build, test and package $packageName plus $portableName")) { return }

if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $projectRoot 'artifacts\publish' }
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
$stage = Join-Path $ArtifactsPath ("Floppy-$version-" + [guid]::NewGuid().ToString('N'))
$package = Join-Path $stage $packageName
$portable = Join-Path $stage 'portable'
$zip = Join-Path $stage ($packageName + '.zip')
$portableExe = Join-Path $portable 'Floppy.exe'
$portableHash = Join-Path $stage ($portableName + '.sha256')
New-Item -ItemType Directory -Path $stage | Out-Null

try {
    $properties = @('-p:BuildGameModules=true')
    if ($GameDir) { $properties += "-p:GameDir=$GameDir" }
    if ($StonewardsDir) { $properties += "-p:StonewardsDir=$StonewardsDir" }
    if ($OddcoreDir) { $properties += "-p:OddcoreDir=$OddcoreDir" }
    $publish = @('publish', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true')
    Write-Host "Building complete folder release $version..."
    Invoke-Dotnet ($publish + $properties + @('-p:PublishSingleFile=false', '--artifacts-path', (Join-Path $stage 'build-folder'), '-o', $package))
    Write-Host "Building single-file portable release $version..."
    Invoke-Dotnet ($publish + $properties + @('-p:PublishProfile=Portable', '--artifacts-path', (Join-Path $stage 'build-portable'), '-o', $portable))

    $portableFiles = @(Get-ChildItem -LiteralPath $portable -Recurse -File)
    if ($portableFiles.Count -ne 1 -or $portableFiles[0].FullName -ne $portableExe) {
        throw 'The Portable profile did not produce exactly one Floppy.exe.'
    }
    $runtime = Get-Content -LiteralPath (Join-Path $package 'Floppy.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or
        -not ($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App') -or
        -not ($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App')) {
        throw 'The folder release requires an installed .NET runtime.'
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $package
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $package -Recurse
    Test-PackageExe (Join-Path $package 'Floppy.exe') (Join-Path $package 'EXE-Selbstpruefung.txt')
    Test-UpdateHelperDispatch (Join-Path $package 'Floppy.exe')
    Invoke-Dotnet @('run', '--project', (Join-Path $projectRoot 'tests\Floppy.Install.Tests\Floppy.Install.Tests.csproj'),
        '-c', 'Release', '-p:BuildGameModules=false', '--artifacts-path', (Join-Path $stage 'install-tests'), '--', $package)
    & (Join-Path $PSScriptRoot 'test-portable.ps1') -Exe $portableExe |
        Tee-Object -FilePath (Join-Path $stage 'Portable-Selbstpruefung.txt')
    Test-UpdateHelperDispatch $portableExe

    $sourceCommit = 'unavailable'
    $dirty = 'unknown'
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $commit = & git -C $projectRoot rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            $sourceCommit = ($commit -join '').Trim()
            $changes = @(& git -C $projectRoot status --porcelain)
            if ($LASTEXITCODE -eq 0) { $dirty = [string]($changes.Count -gt 0) }
        }
    }
    @(
        "Floppy $version - Windows x64",
        "Source commit: $sourceCommit; uncommitted changes: $dirty",
        'Folder release: self-contained .NET and WPF runtime verified.',
        'EXE self-test and synthetic installer/package checks: passed.',
        'Folder and Portable update helper dispatch: passed without starting the main UI.',
        'Portable EXE: copied alone to a Unicode path, empty working directory and isolated runtime cache; passed.',
        'No live game actions were performed by these package checks.'
    ) | Set-Content -LiteralPath (Join-Path $package 'Release-Pruefung.txt') -Encoding utf8

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($package, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $unpacked = Join-Path $stage 'zip-check'
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $unpacked)
    $unpackedExe = Join-Path (Join-Path $unpacked $packageName) 'Floppy.exe'
    if ((Get-FileHash -LiteralPath $unpackedExe).Hash -ne (Get-FileHash -LiteralPath (Join-Path $package 'Floppy.exe')).Hash) {
        throw 'The ZIP contains a different EXE.'
    }
    Test-PackageExe $unpackedExe (Join-Path $stage 'ZIP-Selbstpruefung.txt')
    Write-Sha256 $zip ($zip + '.sha256') ([IO.Path]::GetFileName($zipTarget))
    Write-Sha256 $portableExe $portableHash $portableName

    # All outputs are verified before exposing them. Moves never replace existing files/directories.
    Assert-NewReleaseTargets
    foreach ($target in $targets) { Assert-Within $target $releaseRoot }
    foreach ($source in $package, $zip, ($zip + '.sha256'), $portableExe, $portableHash) { Assert-Within $source $stage }
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
    [IO.Directory]::Move($package, $packageTarget)
    [IO.File]::Move($zip, $zipTarget)
    [IO.File]::Move(($zip + '.sha256'), ($zipTarget + '.sha256'))
    [IO.File]::Move($portableExe, $portableTarget)
    [IO.File]::Move($portableHash, ($portableTarget + '.sha256'))
    Write-Output "Folder: $packageTarget"
    Write-Output "ZIP: $zipTarget"
    Write-Output "Portable EXE: $portableTarget"
    Write-Output "SHA256: $zipTarget.sha256 and $portableTarget.sha256"
    Write-Output "Build and test evidence: $stage"
}
catch {
    Write-Warning "Release did not complete. Existing targets were not overwritten; build/test evidence remains in $stage"
    throw
}
