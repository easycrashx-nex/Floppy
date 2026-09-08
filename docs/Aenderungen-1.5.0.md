# Floppy 1.5.0 – Einstellungen und direkte Updates

## App-Einstellungen

- Ein eigener Einstellungsdialog ist über die Kopfzeile und das Werkzeugmenü erreichbar, auch ohne Verbindung zu einem Spiel.
- Automatisches Verbinden lässt sich abschalten; manuelles Verbinden bleibt verfügbar.
- Floppy kann minimiert starten und Fenstergröße sowie Position auf Wunsch wiederherstellen.
- Die Overlay-Taste lässt sich ändern. Eine bereits belegte Taste führt zu einer Meldung; Speichern übernimmt nur gültige Einstellungen.
- Die automatische Updateprüfung beim Start ist abschaltbar. Änderungen werden mit **Speichern** übernommen; Spielordner, Favoriten und Profile bleiben erhalten.

## Updates

- Versionsprüfung und Releaseverlauf verwenden das öffentliche Repository `easycrashx-nex/Floppy` ohne GitHub-Anmeldung.
- Nur neuere stabile Versionen mit einer vollständigen Windows-Portable-EXE und passender SHA256-Datei werden angeboten.
- Der Download startet ausdrücklich per Knopfdruck, zeigt den Fortschritt und prüft Dateigröße sowie Prüfsumme vor dem Start des Updatehelfers.
- Floppy beendet seine Verbindungen und Dienste regulär. Der Helfer wartet auf das Ende des ursprünglichen Prozesses und ersetzt die gestartete EXE. Eine Sicherung der bisherigen EXE bleibt erhalten; ein fehlgeschlagener Austausch überschreibt sie nicht.
- Einstellungen und Profile in AppData bleiben erhalten. Spielmodule werden bei Bedarf weiterhin bei beendetem Spiel über **Einrichten / reparieren** aktualisiert.
- Bei fehlendem Internet, einem beschädigten Download, verweigerten Rechten oder gesperrter EXE wird der Fehler gemeldet.

## Veröffentlichung

Floppy wird erstmals öffentlich auf GitHub bereitgestellt. Die neun vorhandenen älteren
Versionen von 1.1.0 bis 1.4.3 werden mit ihren unveränderten Paketen, Prüfsummen und
eigenen Release-Texten nachträglich veröffentlicht. GitHub zeigt das tatsächliche
Upload-Datum. Ein Paket für 1.0.0 wurde nicht gefunden und wird nicht rekonstruiert.

Für die öffentliche Git-Historie wurde die private Commit-E-Mail durch die
GitHub-No-Reply-Adresse ersetzt. Quellbäume und historische Downloadpakete sind
unverändert; ihre Release-Texte nennen die Zuordnung der ursprünglichen Build-ID
zum öffentlichen Quellstand.

## Prüfung

Die Tests prüfen Speicherung, vorhandene Einstellungen, Dialogbedienung, Updatefehler,
Weiterleitungen, Downloadintegrität, Dateiaustausch und Wiederherstellung mit isolierten
Fixtures. Vollständige Releasepakete enthalten Laufzeit, Module, Loader und Bilder.
Die Paketprüfung startet die Portable-EXE allein aus einem anderen Unicode-Pfad mit
leerem Arbeitsordner und isolierter Laufzeitumgebung. Die Änderungen dieser Version
umfassen keine neuen Spieloptionen.
