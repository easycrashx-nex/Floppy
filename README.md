# Floppy 1.3.2

Floppy ist ein modulares Windows-Modmenü mit Desktop-App und externem Overlay.
Die Module definieren ihre Optionen einmal; Desktop und Overlay verwenden dieselbe Oberfläche.

Version 1.3 bringt die Graphit-Mint-Oberfläche als externes Overlay ins Spiel.
Die bisherigen Unity- und P.I.T.T.-Ingame-Menüs entfallen.
Details und Grenzen: [Änderungen 1.3.0](docs/Aenderungen-1.3.0.md).

Version 1.3.1 repariert den Mortal-Shell-II-Adapter für aktualisierte Spielfassungen und
meldet Zugriffsfehler verständlich: [Änderungen 1.3.1](docs/Aenderungen-1.3.1.md).
Version 1.3.2 erkennt auch das Fenster eines erhöht gestarteten Spiels mit normalen
Benutzerrechten: [Änderungen 1.3.2](docs/Aenderungen-1.3.2.md).

Für Mortal Shell II bleibt die vom Spiel mitgelieferte `.pdb` neben der eigentlichen
Spiel-EXE erforderlich. Floppy liest daraus die zur EXE passenden Adressen lokal.
Wenn das Spiel mit Administratorrechten läuft, in Floppy **Werkzeuge → Floppy als
Administrator neu starten** wählen und die Windows-Abfrage bestätigen. Beide Programme
mit normalen Benutzerrechten zu starten funktioniert ebenfalls. Für dieses externe Modul
ist keine Installation oder Reparatur im Spielordner nötig.

## Spiele und Module

| Spiel | Technik | Einrichtung |
|---|---|---|
| How to Fish | Unity Mono / BepInEx | App installiert Loader und passendes Modul |
| Stonewards | Unity Mono / BepInEx | App installiert Loader und passendes Modul |
| ODDCORE | Unity IL2CPP / BepInEx | App installiert Loader und passendes Modul; erster Start erzeugt Interop-Dateien |
| Project P.I.T.T. | Godot 4, PCK-Format 4 | App ergänzt das Backend-Skript und sichert das vorherige Paket |
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
helfen beim Navigieren. Die App muss während des Spielens geöffnet bleiben.
Nach dem Verbinden öffnet `F1` das externe Overlay über dem aktiven Spielfenster.
Alternativ öffnet die Schaltfläche **Overlay** die Oberfläche über dem verbundenen Spiel.
`F1`, `Esc` oder die Schließen-Schaltfläche schließen das Overlay; Floppy bleibt minimiert erreichbar.
**Desktop** wechselt zurück zur normalen App. Beim Wechsel in eine andere App blendet sich das Overlay aus.

- **Suche:** sucht im Optionsbestand; `Strg+F` fokussiert die Suche, auch im Overlay.
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

## Vollbild und Upgrade

Im Spiel **Randloses Vollbild / Borderless Fullscreen** oder den Fenstermodus wählen.
Floppy ändert die Anzeigeeinstellungen des Spiels nicht. Echtes exklusives Vollbild wird
vom externen Overlay nicht unterstützt. Windows kann Vollbildoptimierungen verwenden;
für ein verlässliches Ergebnis den randlosen Modus ausdrücklich im Spiel einstellen.
Hintergrund: [Microsoft zu Vollbildoptimierungen und Overlays](https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/).

Beim Wechsel von 1.1/1.2 zuerst das Spiel beenden, mit der neuen App unter **Werkzeuge →
Floppy einrichten / reparieren** das Modul aktualisieren und das Spiel neu starten.
Ein bereits geladenes altes Modul wird durch den Austausch der Desktop-EXE nicht entfernt.
Bei P.I.T.T. entfernt die Reparatur auch die bisher verwalteten Menü-Skripte.

Unity und P.I.T.T. geben den Cursor für das Overlay frei und stellen den vorherigen Zustand
beim Schließen wieder her. Die Verbindung erneuert dafür eine zeitlich begrenzte Freigabe;
bei Verbindungsabbruch läuft sie spätestens nach drei Sekunden ab.
Ob zusätzlich Spieleingaben gesperrt oder das Spiel pausiert werden, hängt vom Modul ab.
Die externen Adapter für Mortal Shell II und Unrailed! 2 verwenden den Windows-Fensterfokus.

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
dotnet publish src/Floppy.App/Floppy.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/Floppy-1.3.0-win-x64

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
dotnet run --project tests/Floppy.Install.Tests -c Release -- artifacts/Floppy-1.3.0-win-x64

# Die ausgelieferte EXE selbst prüfen (ohne Spielzugriff oder sichtbares Fenster):
.\artifacts\Floppy-1.3.0-win-x64\Floppy.exe --self-test "$env:TEMP\floppy-self-test.txt"

# P.I.T.T.-Backend zusätzlich mit einer vorhandenen Godot-4-Konsole prüfen:
.\tools\test-pitt.ps1 -Godot "C:\Pfad\Godot_console.exe"
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
| `src/Shared/ExternalOverlay.cs` | Unity-Cursor und Hintergrundbetrieb für das externe Overlay |
| `src/Floppy.<Spiel>` | Spielabhängige Funktionen bzw. Godot-Skripte |
| `tests`, `tools` | Isolierte Regressionstests und Entwicklungshelfer |

## Wirkungsbereich und Spielstände

Optionen können nur den eigenen Spieler, ausgewählte Mitspieler oder die ganze Runde
betreffen. Die Oberflächen zeigen die vom Modul angegebene Kennzeichnung.
Host-/Verfügbarkeitsprüfungen sind je nach Modul unterschiedlich.
Geld, Freischaltungen, Weltfortschritt und Erfolge können dauerhaft gespeichert werden;
das Zurückschalten einer Option oder die Deinstallation macht diese Spieländerungen nicht
rückgängig. Die Installationssicherung betrifft Programmdateien, keine Spielstände.
