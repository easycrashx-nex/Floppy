$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'The optional native fixtures require Visual Studio C++ build tools.' }
$version = Get-ChildItem -LiteralPath (Join-Path $installation 'VC\Tools\MSVC') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $version.FullName 'bin\Hostx64\x64\cl.exe'
$linker = Join-Path $version.FullName 'bin\Hostx64\x64\link.exe'
$output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\scratch\ms2-symbol-fixture'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$object = Join-Path $output 'fixture.obj'
$pdb = Join-Path $output 'fixture.pdb'
$exe = Join-Path $output 'fixture.exe'
& $compiler /nologo /c /Zi /Od /GS- "/Fo$object" "/Fd$(Join-Path $output 'types.pdb')" (Join-Path $PSScriptRoot 'NativeFixture.cpp') | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Native fixture compilation failed: $LASTEXITCODE" }
& $linker /NOLOGO /DEBUG:FULL /OPT:NOREF /NODEFAULTLIB /ENTRY:main /SUBSYSTEM:CONSOLE "/PDB:$pdb" "/OUT:$exe" $object | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Native fixture linking failed: $LASTEXITCODE" }
# The synthetic native EXE is only inspected, never launched.
$exe
