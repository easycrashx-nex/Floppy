# Floppy 1.2

Floppy ist ein modulares Windows-Modmenü mit Desktop-App und spielabhängigem Ingame-Menü.
Die Module definieren ihre Optionen einmal; die Oberflächen verwenden das gemeinsame Schema.

Die Desktop-App hat in Version 1.2 eine neue Graphit-Mint-Oberfläche erhalten.
Details und Prüfergebnisse: [Änderungen 1.2.0](docs/Aenderungen-1.2.0.md).

## Spiele und Module

| Spiel | Technik | Einrichtung |
|---|---|---|
| How to Fish | Unity Mono / BepInEx | App installiert Loader und passendes Modul |
| Stonewards | Unity Mono / BepInEx | App installiert Loader und passendes Modul |
| ODDCORE | Unity IL2CPP / BepInEx | App installiert Loader und passendes Modul; erster Start erzeugt Interop-Dateien |
| Project P.I.T.T. | Godot 4, PCK-Format 4 | App ergänzt die beiden Skripte und sichert das vorherige Paket |
| Mortal Shell II | Externer Unreal-Adapter | Dienst in der Desktop-App |
| Unrailed! 2 | Entwicklerschnittstelle | Dienst in der Desktop-App |

Die Unterstützung einzelner Funktionen hängt von der jeweiligen Spielversion und dem Spielzustand ab.
Ein erfolgreicher Build bestätigt keine Funktion im laufenden Spiel.

## Benutzen

Das vollständige Release-ZIP in einen eigenen Ordner entpacken und `Floppy.exe` starten.
Den Ordner `runtime` und die übrigen Dateien neben der EXE behalten. Das eigenständige
Windows-x64-Release bringt die .NET-Laufzeit mit; ein normaler Entwicklerbuild benötigt
.NET 8 Desktop Runtime.

Die App erkennt Steam-Bibliotheken. Unter **Werkzeuge** stehen Diagnose, Reparatur,
Wiederherstellung und die manuelle Ordnerauswahl zur Verfügung. Favoriten und die Suche
helfen beim Navigieren. `F1` öffnet das Ingame-Menü, sofern der jeweilige Adapter es unterstützt.

- **Suche:** sucht im Optionsbestand; `Strg+F` fokussiert die Desktop-Suche. Auch die Unity- und Pitt-Menüs bieten ein Suchfeld.
- **Favoriten:** der Stern an einer Option speichert sie pro Spiel im Bereich Favoriten.
- **Tastenkürzel:** Rechtsklick auf den Stern weist einer Toggle-/Button-Option ein Kürzel mit Strg oder Alt zu. Es wirkt bei aktivem Spiel oder aktiver Floppy-App.
- **Diagnose:** zeigt Installationszustand, fehlende/geänderte Dateien und Verbindungsinformationen.
- **Spielordner:** manuell gewählte Ordner können wieder auf Steam-Erkennung zurückgestellt werden.
- **Fenster:** Größe und Position bleiben zwischen Starts erhalten.

Beim Einrichten muss das Spiel beendet sein. Floppy prüft alle benötigten Quelldateien,
bereitet die Installation vor und sichert ersetzte Dateien unter `.floppy` im Spielordner.
Schlägt ein Kopiervorgang fehl, wird der vorherige Stand dieses Versuchs wiederhergestellt.
**Wiederherstellen** entfernt nur verwaltete Dateien bzw. stellt deren vorherigen Inhalt wieder her.
Dateien, die seitdem durch das Spiel, ein Update oder eine andere Mod verändert wurden,
bleiben erhalten; die App meldet diese Ausnahmen. Original-Sicherungen bleiben für manuelle
Wiederherstellung im Ordner `.floppy/original` erhalten.

Bei P.I.T.T. wird vor dem Austausch das PCK-Format geprüft. Nach einem Spielupdate mit
verändertem Paket zuerst **Wiederherstellen** und anschließend **Reparieren** wählen.
Das veränderte Spielpaket wird dabei erhalten und zur neuen Installationsgrundlage.
Verschlüsselte und andere PCK-Formate werden mit einer Fehlermeldung abgelehnt.

## Entwicklung

- .NET SDK 8 oder neuer, Windows.
- Lokale Spiel-DLLs für How to Fish und Stonewards.
- Für ODDCORE die vom passenden BepInEx erzeugten Interop-DLLs.
- BepInEx-Archive liegen in `vendor`; die Installation selbst braucht keinen Download.

Lokale Pfade in einer nicht versionierten `Directory.Build.local.props` setzen:

```xml
<Project>
  <PropertyGroup>
    <GameDir>D:\SteamLibrary\steamapps\common\How to Fish\How to Fish</GameDir>
    <StonewardsDir>D:\SteamLibrary\steamapps\common\Stonewards</StonewardsDir>
    <OddcoreDir>D:\SteamLibrary\steamapps\common\ODDCORE</OddcoreDir>
  </PropertyGroup>
</Project>
```

Alternativ funktionieren MSBuild-Parameter wie `-p:GameDir="D:\..."`.
`GameManaged`, `StonewardsManaged` und `OddcoreInterop` können bei abweichender Struktur
direkt gesetzt werden. Fehlende Spielreferenzen ergeben eine gezielte Build-Fehlermeldung.

```powershell
# Vollständige App mit allen Mod-Dateien, ohne Installation ins Spiel:
dotnet build src/Floppy.App/Floppy.App.csproj -c Release

# Desktop/Kern entwickeln, ohne installierte Unity-Spiele oder deren DLLs:
dotnet build src/Floppy.App/Floppy.App.csproj -c Release -p:BuildGameModules=false

# Vollständiges eigenständiges Windows-Paket:
dotnet publish src/Floppy.App/Floppy.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/Floppy-1.2.0-win-x64

# Bauen und How to Fish einrichten – Build erfolgt vor jeder Spieländerung:
.\deploy.ps1 -GameDir "D:\SteamLibrary\steamapps\common\How to Fish\How to Fish" -App

# Nur bauen:
.\deploy.ps1 -BuildOnly
```

Ein Desktop-Build mit `BuildGameModules=false` enthält keine Unity-Installationspakete
und ist deshalb kein vollständiges Release.

## Prüfen

Die Tests verwenden temporäre Dateien, synthetische Spielpakete und Fake-Verbindungen:

```powershell
# Alle isolierten Prüfungen:
.\tools\test.ps1

dotnet run --project tests/Floppy.Install.Tests -c Release
python -m unittest discover -s tools -p "test_rauchtest.py"

# Zusätzlich das tatsächliche Publish-Paket auf Vollständigkeit prüfen:
dotnet run --project tests/Floppy.Install.Tests -c Release -- artifacts/Floppy-1.2.0-win-x64

# Die ausgelieferte EXE selbst prüfen (ohne Spielzugriff oder sichtbares Fenster):
.\artifacts\Floppy-1.2.0-win-x64\Floppy.exe --self-test "$env:TEMP\floppy-self-test.txt"
```

`python tools/rauchtest.py` prüft nur Verbindung und Schema eines laufenden Spiels.
Es verändert keine Option. Das ausdrückliche `--exercise` schaltet ausschließlich die
im Skript freigegebenen Anzeigeoptionen kurz um und stellt sie in `finally` zurück.
Buttons, Geld, Fortschritt, Bewegung und Erfolge werden nie automatisch ausgeführt.
Fehler ergeben einen von null verschiedenen Exitcode. Das ist kein vollständiger Gameplay-Test.

## Quellstruktur

| Ordner | Aufgabe |
|---|---|
| `src/Floppy.Model` | Enginefreies Optionsmodell, Registry, Profile, lokales IPC |
| `src/Floppy.App` | WPF-App, Bibliothek, Installation/Restore und externe Adapter-Anbindung |
| `src/Floppy.Unity.Mono`, `src/Floppy.Unity.IL2CPP` | BepInEx-Einstieg für die beiden Unity-Laufzeiten |
| `src/Shared/Menu` | Gemeinsame Unity-Menüoberfläche |
| `src/Floppy.<Spiel>` | Spielabhängige Funktionen bzw. Godot-Skripte |
| `tests`, `tools` | Isolierte Regressionstests und Entwicklungshelfer |

## Wirkungsbereich und Spielstände

Optionen können nur den eigenen Spieler, ausgewählte Mitspieler oder die ganze Runde
betreffen. Die Oberflächen zeigen die vom Modul angegebene Kennzeichnung.
Host-/Verfügbarkeitsprüfungen sind je nach Modul unterschiedlich.
Geld, Freischaltungen, Weltfortschritt und Erfolge können dauerhaft gespeichert werden;
das Zurückschalten einer Option oder die Deinstallation macht diese Spieländerungen nicht
rückgängig. Die Installationssicherung betrifft Programmdateien, keine Spielstände.
