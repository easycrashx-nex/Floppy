<#
    Builds Floppy before any game files are changed, then uses the app's backed-up installer.
    .\deploy.ps1 -GameDir "D:\...\How to Fish" -App
    .\deploy.ps1 -BuildOnly
    Additional paths can be set in Directory.Build.local.props.
#>
param(
    [string]$GameDir,
    [string]$StonewardsDir,
    [string]$OddcoreDir,
    [switch]$App,
    [switch]$BuildOnly,
    [switch]$Konsole
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$AppId = '4001890'
function Finde-SteamPfad {
    $kandidaten = @(
        (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue).SteamPath,
        (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -Name InstallPath -ErrorAction SilentlyContinue).InstallPath,
        'C:\Program Files (x86)\Steam'
    )
    foreach ($k in $kandidaten) {
        if ($k -and (Test-Path $k)) { return $k }
    }
    return $null
}

function Finde-Spielordner {
    $steam = Finde-SteamPfad
    if (-not $steam) { return $null }

    # Steam verteilt Spiele auf mehrere Laufwerke - alle Bibliotheken durchgehen
    $bibliotheken = @(Join-Path $steam 'steamapps')
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'

    if (Test-Path $vdf) {
        foreach ($treffer in ([regex]'"path"\s+"([^"]+)"').Matches((Get-Content $vdf -Raw))) {
            $pfad = Join-Path ($treffer.Groups[1].Value -replace '\\\\', '\') 'steamapps'
            if ((Test-Path $pfad) -and ($bibliotheken -notcontains $pfad)) { $bibliotheken += $pfad }
        }
    }

    foreach ($lib in $bibliotheken) {
        $manifest = Join-Path $lib "appmanifest_$AppId.acf"
        if (-not (Test-Path $manifest)) { continue }

        $treffer = [regex]::Match((Get-Content $manifest -Raw), '"installdir"\s+"([^"]+)"')
        if (-not $treffer.Success) { continue }

        $ordner = Join-Path $lib "common\$($treffer.Groups[1].Value)"

        # Die Exe liegt bei diesem Spiel eine Ebene tiefer
        $exe = Get-ChildItem $ordner -Filter '*.exe' -Recurse -ErrorAction SilentlyContinue |
               Where-Object { $_.Name -notlike 'UnityCrashHandler*' } |
               Select-Object -First 1

        if ($exe) { return $exe.DirectoryName }
    }
    return $null
}

if (-not $GameDir -and -not $BuildOnly) { $GameDir = Finde-Spielordner }
if (-not $BuildOnly -and (-not $GameDir -or -not (Test-Path -LiteralPath (Join-Path $GameDir 'How to Fish_Data')))) {
    throw 'How to Fish nicht gefunden. Mit -GameDir den Ordner neben How to Fish_Data angeben.'
}

$arguments = @('build', (Join-Path $root 'src\Floppy.App\Floppy.App.csproj'), '-c', 'Release', '-v', 'minimal')
if ($GameDir) { $arguments += "-p:GameDir=$GameDir" }
if ($StonewardsDir) { $arguments += "-p:StonewardsDir=$StonewardsDir" }
if ($OddcoreDir) { $arguments += "-p:OddcoreDir=$OddcoreDir" }
Write-Host 'Baue Floppy und alle Laufzeitdateien...' -ForegroundColor Cyan
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen. Im Spiel wurde nichts verändert.' }

$exe = Join-Path $root 'src\Floppy.App\bin\Release\Floppy.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Build-Ausgabe fehlt: $exe" }
if (-not $BuildOnly) {
    $resultFile = Join-Path $env:TEMP ('floppy-deploy-' + [guid]::NewGuid().ToString('N') + '.txt')
    try {
        # ProcessStartInfo argument quoting is explicit because Windows PowerShell 5 lacks ArgumentList.
        if ($GameDir.Contains('"')) { throw 'Ungültiger Spielpfad.' }
        $installArgs = @('--install-game', ('"{0}"' -f $GameDir), '"How to Fish"', ('"{0}"' -f $resultFile))
        $process = Start-Process -FilePath $exe -ArgumentList $installArgs -Wait -PassThru -WindowStyle Hidden
        if (Test-Path -LiteralPath $resultFile) { Write-Host (Get-Content -LiteralPath $resultFile -Raw) }
        if ($process.ExitCode -ne 0) { throw 'Installation fehlgeschlagen; Details stehen in der Meldung oben.' }
    }
    finally { if (Test-Path -LiteralPath $resultFile) { Remove-Item -LiteralPath $resultFile -Force } }
}
if ($Konsole) { Write-Host 'Bestehende BepInEx-Konsoleneinstellungen bleiben erhalten.' }
Write-Host "Desktop-App: $exe" -ForegroundColor Green
if ($App) { Start-Process -FilePath $exe }
