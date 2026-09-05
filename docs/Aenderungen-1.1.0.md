# Floppy 1.1.0

Stand: 5. September 2026. Diese Version setzt die Korrekturen und die erste Runde
Bedienungsverbesserungen aus dem Projektcheck um.

## Korrigiert

- Profile lassen sich wieder speichern und laden. Das Format ist versioniert;
  ältere Profile bleiben lesbar. Typen, Zahlenbereiche und Auswahlen werden vor
  dem Anwenden geprüft. Fehler und teilweise angewandte Profile werden gemeldet.
- „Alles aus“ verwendet ausdrücklich definierte Normalwerte. „Aktiv“ folgt dem
  tatsächlichen Optionszustand, auch nach Änderungen durch Profile oder Menüs.
- Dezimalkomma und Dezimalpunkt werden unterstützt. Ungültige, unendliche und
  außerhalb des erlaubten Bereichs liegende Zahlen werden abgewiesen.
- Verbindungen besitzen Antwortfristen und eine Sitzungskennung. Wartende Befehle
  werden bei Spielwechseln verworfen; noch nicht gestartete, abgelaufene Aktionen
  werden nicht nachträglich ausgeführt. Bei bereits laufenden Aktionen wird ein
  unbekannter Ausgang ausdrücklich gemeldet.
- Externe Hosts führen ihre Arbeit nacheinander aus und schließen geordnet.
  Unreal-Aufrufpuffer bleiben bis zum bestätigten Abschluss gültig.
- Mortal Shell II aktualisiert dynamische Menüs nach erfolgreicher Spielerkennung
  und wendet gespeicherte Zustände nach Ladewechseln erneut an.
- Installationen prüfen die benötigten Dateien vorab, sichern ersetzte Inhalte
  und nehmen fehlgeschlagene Änderungen zurück. Wiederherstellung erhält Dateien,
  die zwischenzeitlich durch ein Spielupdate oder eine andere Mod verändert wurden.
- P.I.T.T. erhält eine integrierte PCK-Einrichtung mit Sicherung und Wiederherstellung.
  Unrailed-Einstellungen werden nach Lesefehlern nicht überschrieben.
- Build, Deploy und Publish liefern alle vorgesehenen Module, Loader und Skripte.
  Die Abhängigkeitsauflösung wurde festgelegt; der Release-Build ist warnungsfrei.

## Neu in der Bedienung

- Favoriten pro Spiel in der Desktop-App.
- Frei belegbare Tastenkürzel für Schalter und Aktionen, mit Konfliktmeldung.
  Sie wirken bei aktivem Spiel oder aktiver Floppy-App.
- Werkzeuge für Diagnose, Reparatur, Wiederherstellung und manuelle Spielordner.
- Suche in den Unity- und P.I.T.T.-Menüs.
- Gespeicherte Fenstergröße und Position sowie korrigierte DPI-Umrechnung des Overlays.
- Kopfzeile ohne Überlappung bei Mindestbreite; länger sichtbare Rückmeldungen und
  eine lokale Ereignisliste. Schnelle Sliderbewegungen werden zusammengefasst.

## Geprüft

| Prüfung | Ergebnis |
|---|---|
| Gemeinsames Modell: Profile, Reset, Validierung, Dispatcher und IPC | 85 Assertions bestanden |
| Desktop: Zahlen, Einstellungen und Verbindungswechsel mit Fake-Server | 19 Prüfungen bestanden |
| Externe Hosts und Dateieinstellungen mit isolierten Fixtures | 4 Tests bestanden |
| WPF-Layout, Favoriten und Zustandsanzeige | 16 Prüfungen bestanden |
| Installer: Rollback, Wiederherstellung, PCK und tatsächliches Publish-Paket | 16 Tests bestanden |
| Python-Rauchtest gegen Fake-Verbindungen | 3 Tests bestanden |
| Vollständiger Release-Build und Windows-x64-Publish aus separater Quellkopie | 0 Fehler, 0 Warnungen |
| Ausgelieferte EXE: WPF, Zahleneingabe und alle 11 Laufzeitdateien | Selbsttest mit Exitcode 0 |

Die Tests verwenden temporäre Dateien, synthetische Daten und Fake-Verbindungen.
Zusätzlich wurde eine vorhandene P.I.T.T.-PCK nur gelesen und in einer temporären
Kopie geprüft; das Original blieb unverändert. Reale Spielinstallationen,
Einstellungen, Profile und Spielstände wurden für die Tests nicht verändert.
**Die Änderungen sind noch nicht in laufenden Spielen getestet.**

## Paket und Entwicklung

`releases/Floppy-1.1.0-win-x64.zip` enthält die eigenständige Windows-x64-App mit
.NET-Laufzeit. Vollständig entpacken und `Floppy.exe` starten; die übrigen Dateien
und den Ordner `runtime` daneben behalten.

Alle isolierten Tests sind über `tools/test.ps1` ausführbar. Die EXE bietet
`--self-test <Berichtsdatei>`. UI und Kern lassen sich mit
`-p:BuildGameModules=false` ohne die Spiel-DLLs bauen. Für ein vollständiges Release
werden weiterhin die passenden lokalen Unity-Spielreferenzen benötigt.

Die weiterführenden Ideen Profilvorschau mit gezieltem Anwenden und Favoriten
direkt in den Ingame-Menüs bleiben mögliche nächste Ergänzungen. Die Favoriten
dieser Version gehören zur Desktop-App.
