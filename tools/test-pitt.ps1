param(
    [Parameter(Mandatory = $true)]
    [string]$Godot
)

$ErrorActionPreference = 'Stop'
$engine = (Resolve-Path -LiteralPath $Godot).Path
$projectRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('floppy-pitt-overlay-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
@'
[application]
config/name="Floppy backend fixture"
[rendering]
renderer/rendering_method="gl_compatibility"
'@ | Set-Content -LiteralPath (Join-Path $fixture 'project.godot') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $projectRoot 'src/Floppy.Pitt/floppy.gd') -Destination $fixture
Copy-Item -LiteralPath (Join-Path $projectRoot 'tests/Floppy.Install.Tests/pitt_overlay_test.gd') -Destination $fixture
& $engine --headless --path $fixture --script pitt_overlay_test.gd
if ($LASTEXITCODE -ne 0) { throw "P.I.T.T. tests failed (exit $LASTEXITCODE). Fixture: $fixture" }
Write-Host "P.I.T.T. fixture: $fixture"
