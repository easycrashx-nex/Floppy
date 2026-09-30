# Floppy 1.7.2

Floppy ist ein modulares Windows-Modmenü mit Desktop-App und externem Overlay.
Die Module definieren ihre Optionen einmal; Desktop und Overlay verwenden dieselbe Oberfläche.

**[Aktuelle Downloads](https://github.com/easycrashx-nex/Floppy/releases/latest)** ·
**[Alle Versionen](https://github.com/easycrashx-nex/Floppy/releases)**

Version 1.7.2 erkennt serververwaltete Minecraft-Dungeons-II-Figuren. In diesen
Sitzungen bleiben Anzeigen verfügbar; lokale Spieländerungen werden als nicht
unterstützt erklärt und gesperrt. Eine eigene Lobby ist nicht automatisch ein
lokal verwaltetes Spiel. [Änderungen 1.7.2](docs/Aenderungen-1.7.2.md).

Version 1.7.1 korrigiert die laufende Anzeige von Smaragden, Level, Erfahrung und
geladener Munition: [Änderungen 1.7.1](docs/Aenderungen-1.7.1.md).

Version 1.7.0 erweitert **Minecraft Dungeons II** um Smaragde und Springstone mit
Mengeneingabe, Ressourcen und 58 Regler für Kampf, Schutz, Bewegung, Artefakte,
Beute und Erfahrungsausbeute. Temporäre Anpassungen lassen sich auf ihren
Normalwert zurücksetzen. Details und tatsächlicher Testumfang:
[Änderungen 1.7.0](docs/Aenderungen-1.7.0.md).

Version 1.6.0 ergänzt einen ersten externen Adapter für **Minecraft Dungeons II**:
Lebenspunkte anzeigen, Leben auffüllen und Leben halten. Das laufende Spiel braucht
dafür keinen Neustart. Der Prototyp ist auf Steam-Build 25041023 begrenzt.
Details und Testumfang: [Änderungen 1.6.0](docs/Aenderungen-1.6.0.md).

Version 1.5.0 ergänzt einen eigenen **Einstellungsbereich** für Floppy. Dort lassen sich
Verbindungsautomatik, minimierter Start, Fensterspeicherung, Overlay-Taste und die
Updateprüfung beim Start einstellen. Neue stabile Versionen können direkt heruntergeladen,
geprüft und mit einem Neustart installiert werden. Details: [Änderungen 1.5.0](docs/Aenderungen-1.5.0.md).

Version 1.4.3 ergänzt die einzeln weitergebbare **`Floppy-1.4.3-Portable.exe`** für Windows x64.
Die Spielsuche prüft die zum gewählten Spiel gehörende EXE und gegebenenfalls dessen
Datenordner bzw. PCK-Datei, auch in Unterordnern. Unzugängliche Suchpfade werden behandelt.
Profile laden übergeordnete Auswahlen vor den davon abhängigen Werten: beispielsweise
erst das Team und dann die Menge oder erst die Komponentengruppe und dann das Feld.
Nicht bestätigte Änderungen der Unrailed-2-Entwickleroptionen werden als Fehler gemeldet;
beim Profilladen zählen diese Werte nicht als erfolgreich angewendet.
Die neun dauerhaften Unrailed-2-Schalter und die Boss-Abschnittszahl verwenden auf
Windows 11 einen versionsgeprüften Zugang zur Spieleingabe, da der aktuelle Webdebugger
die Schalterwerte nicht übertragen kann. Details: [Änderungen 1.4.3](docs/Aenderungen-1.4.3.md).

Version 1.3 bringt die Graphit-Mint-Oberfläche als externes Overlay ins Spiel.
Die bisherigen Unity- und P.I.T.T.-Ingame-Menüs entfallen.
Details und Grenzen: [Änderungen 1.3.0](docs/Aenderungen-1.3.0.md).

Version 1.3.1 repariert den Mortal-Shell-II-Adapter für aktualisierte Spielfassungen und
meldet Zugriffsfehler verständlich: [Änderungen 1.3.1](docs/Aenderungen-1.3.1.md).
Version 1.3.2 erkennt auch das Fenster eines erhöht gestarteten Spiels mit normalen
Benutzerrechten: [Änderungen 1.3.2](docs/Aenderungen-1.3.2.md).
Version 1.4.0 ergänzt für Mortal Shell II einen Gegenstandskatalog mit Kategorien,
Itemsuche und Bildern: [Änderungen 1.4.0](docs/Aenderungen-1.4.0.md).
Version 1.4.1 korrigiert die Unrailed-2-Komponentenabfragen und Statusaktualisierung.
In den damaligen Prüfungen fehlte die benötigte Cheat-Komponente; Zug-, Bau- und
Automatikoptionen blieben deshalb eingeschränkt:
[Änderungen und Prüfgrenzen 1.4.1](docs/Aenderungen-1.4.1.md).
Version 1.4.2 verwendet für **Muttern geben** den echten Teambestand und ergänzt
direkte Zugaktionen zum Anhalten, Weiterfahren und Abkühlen:
[Änderungen und Testhinweise 1.4.2](docs/Aenderungen-1.4.2.md).

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
| Minecraft Dungeons II | Externer Unreal-Adapter, Prototyp | Dienst in der Desktop-App; Steam-Build 25041023 |

Die Unterstützung einzelner Funktionen hängt von der jeweiligen Spielversion und dem Spielzustand ab.
Ein erfolgreicher Build bestätigt keine Funktion im laufenden Spiel.

## Benutzen und weitergeben

Floppy benötigt **Windows x64**. Für die vollständigen Release-Ausgaben ist keine
separate .NET-Installation erforderlich. Zwei Varianten stehen zur Verfügung:

- **Einzelne Portable-EXE:** `Floppy-1.7.2-Portable.exe` speichern und starten. Diese eine
  Datei kann allein weitergegeben werden. Sie enthält die .NET-Laufzeit, alle mitgelieferten
  Loader, Module und Bilder. Beim ersten Start entpackt sie ihre Dateien automatisch in
  einen eigenen Bundlecache; der erste Start kann deshalb etwas länger dauern.
- **ZIP-Paket:** Das vollständige ZIP in einen eigenen Ordner entpacken und die darin
  enthaltene `Floppy.exe` starten. Bei dieser Variante gehören `runtime`, `Assets` und
  sämtliche übrigen Paketdateien dazu; die `Floppy.exe` daraus allein reicht nicht.

Die Portable-EXE enthält Floppys Installationsdateien, keine Spiele. Erforderliche
Spielmodule werden weiterhin über die App eingerichtet. Ein normaler Entwicklerbuild
benötigt gegebenenfalls die .NET 8 Desktop Runtime.

Die App durchsucht Steam-Bibliotheken nach dem passenden Spiel. Unter **Werkzeuge** stehen Diagnose, Reparatur,
Wiederherstellung und die manuelle Ordnerauswahl zur Verfügung. Favoriten und die Suche
helfen beim Navigieren. Die App muss während des Spielens geöffnet bleiben.
Nach dem Verbinden öffnet die gewählte Overlay-Taste (standardmäßig `F1`) das externe Overlay über dem aktiven Spielfenster.
Alternativ öffnet die Schaltfläche **Overlay** die Oberfläche über dem verbundenen Spiel.
Die Overlay-Taste, `Esc` oder die Schließen-Schaltfläche schließen das Overlay; Floppy bleibt minimiert erreichbar.
**Desktop** wechselt zurück zur normalen App. Beim Wechsel in eine andere App blendet sich das Overlay aus.

- **Suche:** sucht im Optionsbestand; `Strg+F` fokussiert die Suche, auch im Overlay.
- **Favoriten:** der Stern an einer Option speichert sie pro Spiel im Bereich Favoriten.
- **Mortal-Shell-II-Gegenstände:** Kategorie wählen, Itemnamen suchen und den gewünschten
  Eintrag anklicken. Die Auswahl unter der Liste zeigt, welches Item **Ins Inventar legen**
  verwenden wird. Ein Filterwechsel ändert diese Auswahl nicht.
- **Tastenkürzel:** Rechtsklick auf den Stern weist einer Toggle-/Button-Option ein Kürzel mit Strg oder Alt zu. Es wirkt bei aktivem Spiel oder aktiver Floppy-App.
- **Diagnose:** zeigt Installationszustand, fehlende/geänderte Dateien und Verbindungsinformationen.
- **Spielordner:** manuell gewählte Ordner können wieder auf Steam-Erkennung zurückgestellt werden.
- **Einstellungen:** oben in der App oder unter **Werkzeuge → Einstellungen**, auch ohne laufendes Spiel.
- **Fenster:** Größe und Position bleiben zwischen Starts erhalten, wenn die Fensterspeicherung eingeschaltet ist.

## Einstellungen und Updates

Die App-Einstellungen gelten für alle Spiele. Änderungen mit **Speichern** übernehmen.
Automatisches Verbinden und die Overlay-Taste ändern sich sofort; der minimierte Start
und das Wiederherstellen des Fensters wirken beim nächsten Start. Ohne Verbindungsautomatik
bleibt die Schaltfläche **Verbinden** verfügbar. Favoriten, Spielordner und eigene Tastenkürzel bleiben erhalten.

Unter **Einstellungen → Updates** zeigt Floppy die installierte Version und den letzten
Prüfstatus. **Nach Updates suchen** prüft das öffentliche GitHub-Repository ohne Anmeldung.
Die optionale Prüfung beim Start installiert nichts automatisch. Bei einer neuen stabilen
Version startet **Herunterladen und neu starten** den Download mit Fortschrittsanzeige.
Floppy prüft Dateigröße und SHA256, beendet seine Verbindungen und Dienste und ersetzt
anschließend die gestartete EXE. Die bisherige EXE bleibt als `.Floppy-backup-….exe`
im selben Ordner erhalten. Benutzereinstellungen und Profile bleiben in AppData.

Das funktioniert für die Portable-EXE und die EXE aus dem vollständigen ZIP. Nach einem
Update erhält auch die ZIP-Ausgabe die vollständige Portable-EXE. Liegt die EXE in einem
geschützten Ordner, kann Windows für den Austausch Administratorrechte anfordern.
Bereits im Spiel geladene Module werden erst durch **Einrichten / reparieren** bei
beendetem Spiel aktualisiert. **Releaseverlauf** öffnet alle veröffentlichten Versionen.

Die neun vorhandenen Pakete von 1.1.0 bis 1.4.3 wurden unverändert nachträglich hochgeladen.
Die Release-Texte unterscheiden den ursprünglichen Quellstand vom tatsächlichen
GitHub-Veröffentlichungsdatum. Für 1.0.0 liegt kein verlässlich zuordenbares Paket vor.

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

# Beide Releasevarianten mit Paketprüfungen und SHA256 bauen:
.\tools\publish.ps1

# Desktop/Kern entwickeln, ohne installierte Unity-Spiele oder deren DLLs:
dotnet build src/Floppy.App/Floppy.App.csproj -c Release -p:BuildGameModules=false

# Vollständiges eigenständiges Windows-Paket für die ZIP-Weitergabe:
dotnet publish src/Floppy.App/Floppy.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/Floppy-1.6.0-win-x64

# Einzelne EXE mit eingebetteter Laufzeit und sämtlichen Installationsdateien:
dotnet publish src/Floppy.App/Floppy.App.csproj -c Release -p:PublishProfile=Portable -o artifacts/Floppy-1.6.0-portable
Copy-Item -LiteralPath .\artifacts\Floppy-1.6.0-portable\Floppy.exe -Destination .\artifacts\Floppy-1.6.0-Portable.exe

# Bauen und How to Fish einrichten – Build erfolgt vor jeder Spieländerung:
.\deploy.ps1 -GameDir "D:\SteamLibrary\steamapps\common\How to Fish\How to Fish" -App

# Nur bauen:
.\deploy.ps1 -BuildOnly
```

Ein Desktop-Build mit `BuildGameModules=false` enthält keine Unity-Installationspakete
und ist deshalb kein vollständiges Release.

### Neue Version auf GitHub veröffentlichen

Die App-Version in `src/Floppy.App/Floppy.App.csproj` erhöhen, Änderungen dokumentieren,
`tools/test.ps1` ausführen und den Quellstand committen. Anschließend erstellt
`tools/publish.ps1` beide geprüften Release-Ausgaben. Einen Tag `vX.Y.Z` auf diesen
Commit setzen und mit dem Quellstand hochladen.

Für den GitHub-Release die Portable-EXE, das ZIP und beide `.sha256`-Dateien aus
`releases` anhängen. Erst nach vollständigem Upload als stabilen **Latest**-Release
veröffentlichen. Der Updater erwartet exakt `Floppy-X.Y.Z-Portable.exe` und
`Floppy-X.Y.Z-Portable.exe.sha256` unter dem Tag `vX.Y.Z`; Entwürfe, Vorabversionen
und ältere Versionen werden nicht installiert. Die Änderungshinweise aus der
passenden Datei in `docs` als Release-Text verwenden.

## Prüfen

Die Tests verwenden temporäre Dateien, synthetische Spielpakete und Fake-Verbindungen:

```powershell
# Alle isolierten Prüfungen:
.\tools\test.ps1

dotnet run --project tests/Floppy.Install.Tests -c Release
python -m unittest discover -s tools -p "test_rauchtest.py"

# Zusätzlich das tatsächliche Publish-Paket auf Vollständigkeit prüfen:
dotnet run --project tests/Floppy.Install.Tests -c Release -- artifacts/Floppy-1.6.0-win-x64

# Die ausgelieferte EXE selbst prüfen (ohne Spielzugriff oder sichtbares Fenster):
.\artifacts\Floppy-1.6.0-win-x64\Floppy.exe --self-test "$env:TEMP\floppy-self-test.txt"

# Nur die Portable-EXE in einen isolierten Ordner kopieren und prüfen:
.\tools\test-portable.ps1 -Exe .\artifacts\Floppy-1.6.0-Portable.exe

# P.I.T.T.-Backend zusätzlich mit einer vorhandenen Godot-4-Konsole prüfen:
.\tools\test-pitt.ps1 -Godot "C:\Pfad\Godot_console.exe"
```

Die Portable-Prüfung verwendet auf demselben Rechner einen leeren Arbeitsordner,
einen Pfad mit Umlauten und einen eigenen Bundlecache. Sie prüft die eingebettete
Laufzeit und Paketdateien. Das ist kein bestätigter Test auf einem zweiten echten PC
und kein Nachweis, dass sämtliche Unrailed-2-Schalter im Spiel funktionieren.

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
