param([string]$Package, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($ArtifactsPath) { $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath) }
foreach ($suite in 'Model', 'App', 'Discovery', 'Host', 'Unreal', 'Dungeons2', 'DumbWays', 'Unrailed2', 'Unrailed2.Native', 'Unrailed2.Native.Windows', 'Symbol', 'Ui', 'Overlay', 'Overlay.Ui', 'Install', 'Update', 'Update.Install', 'Settings.Ui') {
    Write-Host "Prüfe Floppy.$suite..."
    $project = Join-Path $projectRoot "tests\Floppy.$suite.Tests\Floppy.$suite.Tests.csproj"
    $arguments = @('run', '--project', $project, '-c', 'Release', '-p:BuildGameModules=false')
    if ($ArtifactsPath) { $arguments += @('--artifacts-path', $ArtifactsPath) }
    if ($suite -eq 'Install' -and $Package) {
        $arguments += @('--', (Resolve-Path -LiteralPath $Package).Path)
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Floppy.$suite.Tests fehlgeschlagen" }
}
if (Get-Command python -ErrorAction SilentlyContinue) {
    & python -m unittest discover -s $PSScriptRoot -p test_rauchtest.py
    if ($LASTEXITCODE -ne 0) { throw 'Rauchtest-Prüfung fehlgeschlagen' }
} else {
    Write-Warning 'Python fehlt: die drei Python-Rauchtest-Fixtures wurden ausgelassen.'
}
