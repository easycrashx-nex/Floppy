param([Parameter(Mandatory = $true)][string]$Exe)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $Exe).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Floppy-Weitergabe-' + [guid]::NewGuid().ToString('N'))
$destination = Join-Path $testRoot 'Anderer Benutzer mit Umlauten äöü'
$work = Join-Path $testRoot 'Leerer Arbeitsordner'
$emptyDotnet = Join-Path $testRoot 'Keine installierte Laufzeit'
$cache = Join-Path $testRoot 'Bundle-Cache'
foreach ($folder in $destination, $work, $emptyDotnet, $cache) {
    New-Item -ItemType Directory -Path $folder | Out-Null
}
$testExe = Join-Path $destination 'Floppy weitergegeben.exe'
Copy-Item -LiteralPath $source -Destination $testExe
$report = Join-Path $testRoot 'Selbsttest.txt'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $testExe
$start.Arguments = '--self-test "' + $report + '"'
$start.WorkingDirectory = $work
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.EnvironmentVariables['DOTNET_ROOT'] = $emptyDotnet
$start.EnvironmentVariables['DOTNET_ROOT_X64'] = $emptyDotnet
$start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$start.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $cache
$start.EnvironmentVariables['COREHOST_TRACE'] = '1'
$start.EnvironmentVariables['COREHOST_TRACEFILE'] = (Join-Path $testRoot 'Hostlaufzeit.txt')
$start.EnvironmentVariables['PATH'] = "$env:WINDIR\System32;$env:WINDIR"
$process = [Diagnostics.Process]::Start($start)
try {
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()
        throw 'Die kopierte EXE hat den Selbsttest nicht innerhalb von 60 Sekunden beendet.'
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }
        throw "Die kopierte EXE ist fehlgeschlagen (Exitcode $($process.ExitCode))."
    }
    $text = Get-Content -LiteralPath $report -Raw
    if ($text -notmatch '(?m)^Fehler:\s*0\s*$' -or $text -match '(?m)^FAIL ') { throw "Selbsttest fehlgeschlagen: $text" }
    $trace = Get-Content -LiteralPath (Join-Path $testRoot 'Hostlaufzeit.txt') -Raw
    if ($trace -notmatch 'Executing as a self-contained app' -or $trace -notmatch 'Using internal hostpolicy') {
        throw 'Der EXE-Start hat die eingebettete Laufzeit nicht bestätigt.'
    }
    $bundleFiles = Get-ChildItem -LiteralPath $cache -Recurse -File
    foreach ($name in 'System.Private.CoreLib.dll', 'Floppy.Model.dll', 'Floppy.runtimeconfig.json') {
        if (-not ($bundleFiles | Where-Object Name -eq $name)) { throw "Eigenständige Laufzeit fehlt im Bundle: $name" }
    }
    $runtimeConfig = $bundleFiles | Where-Object Name -eq 'Floppy.runtimeconfig.json' | Select-Object -First 1
    $config = Get-Content -LiteralPath $runtimeConfig.FullName -Raw | ConvertFrom-Json
    if ($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks -or
        -not ($config.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App') -or
        -not ($config.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App')) {
        throw 'Das Bundle benötigt eine separat installierte .NET-Laufzeit.'
    }
    if (@(Get-ChildItem -LiteralPath $destination -File).Count -ne 1) {
        throw 'Die Weitergabe hat zusätzliche Dateien neben der EXE benötigt.'
    }
    Get-Content -LiteralPath $report
    Write-Output "PASS: Nur die EXE kopiert, fremder Arbeitsordner, Unicode-Pfad, eigene .NET-Laufzeit. Bericht: $report"
}
finally { $process.Dispose() }
