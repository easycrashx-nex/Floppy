param([string]$Package)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
foreach ($suite in 'Model', 'App', 'Host', 'Ui', 'Overlay', 'Overlay.Ui', 'Install') {
    Write-Host "Prüfe Floppy.$suite..."
    $project = Join-Path $projectRoot "tests\Floppy.$suite.Tests\Floppy.$suite.Tests.csproj"
    $arguments = @('run', '--project', $project, '-c', 'Release', '-p:BuildGameModules=false')
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
