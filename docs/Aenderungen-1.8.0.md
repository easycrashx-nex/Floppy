# Floppy 1.8.0 – Dumb Ways to Build

Dumb Ways to Build ist jetzt in Floppys Bibliothek enthalten. Die App richtet den
Loader und das passende Modul mit Sicherung und Wiederherstellung ein. Das Menü
bleibt in der Desktop-App beziehungsweise im externen Overlay.

## Funktionen

- **Überleben:** Lebenspunkte anzeigen, Unverwundbarkeit, Leben auffüllen,
  Betäubung aufheben und eigene tote Figur wiederbeleben.
- **Bewegung:** Ausdauer anzeigen und halten, Laufgeschwindigkeit und Sprungkraft
  bis Faktor 3, No-Clip und Windschutz. Die Anzeige liest die tatsächlichen Spielwerte.
- **Werkzeuge:** Haltbarkeit des aktiven Werkzeugs anzeigen, reparieren und halten.
  Reparieren setzt ein ausgerüstetes, lokal verwaltetes Werkzeug mit Haltbarkeit voraus.
- **Wiederbelebungsmarken:** Gemeinsamen Bestand anzeigen und 1–999 Marken hinzufügen.
  Die Gutschrift wird unmittelbar am Spielbestand geprüft.

Temporäre Optionen starten ausgeschaltet und lassen sich mit „Alles aus“ zurücksetzen.
Bewegungsfaktoren verwenden einen eigenen Modifikatorsatz; andere Spielmodifikatoren
bleiben erhalten. No-Clip an einer freien Stelle ausschalten. Bereits vorhandene
Schutz-, No-Clip- und Windschutz-Zustände werden beim Ausschalten wiederhergestellt.

## Voraussetzungen und Grenzen

Geprüft: **Spielversion 2.1.89, Steam-Build 25644049, Windows x64, Unity 6000.3.13f1**.
Spielcode und Metadaten müssen ihren geprüften SHA-256-Werten entsprechen. Andere
Versionen zeigen eine verständliche Meldung und erlauben keine Spieländerungen.

Die Funktionen greifen auf die lokale Spielfigur zu. Sie benötigen deren
Zustandsautorität; Marken benötigen zusätzlich die Zustandsautorität am gemeinsamen
Bestand. Multiplayer wurde nicht praktisch geprüft. Anzeigen ersetzen keine
Bestätigung, dass Änderungen in einer fremd verwalteten Sitzung unterstützt werden.
In der Lobby gibt es keine Wiederbelebungsmarken. Wiederbeleben und Betäubung aufheben
sind nur im jeweiligen Zustand verfügbar.

Beim ersten Start lädt BepInEx die passenden Unity-Basisbibliotheken herunter und
erzeugt Interop-Dateien. Internet ist dafür erforderlich; spätere Starts verwenden
den Cache. Die veröffentlichte Floppy-EXE enthält keine Spiel-DLLs oder Spielstände.

## Prüfung

- Vollständiger Build von Desktop-App und allen Spielmodulen.
- Alle 17 bestehenden Prüfsuiten und drei Python-Fixtures bestanden.
  Erweiterte native Symbol-Fixtures waren nicht eingerichtet; ein Windows-Fokustest
  wurde wegen verweigerter Vordergrundfreigabe ausdrücklich ausgelassen.
- Neue Discovery-Fixtures erkennen die genaue EXE samt Data-Ordner; falsche und
  unvollständige Installationen werden abgewiesen.
- Neue Installer-Fixture prüft das genaue Modul, fehlende fremde Module und die
  Wiederherstellung vorher vorhandener Dateien.
- Im laufenden Spiel: Modulstart, IPC, aktive Spielfigur, Heilungsaufruf,
  temporäre Schalter und „Alles aus“ geprüft.
- Tatsächliche Bewegung geprüft: Tempo **15 → 30 → 15**, Sprungkraft
  **10,65 → 21,3 → 10,65**. Eine Marke gutgeschrieben: **5 → 6**.
- Werkzeugreparatur, anhaltende Haltbarkeit unter Nutzung, Wiederbelebung und
  Betäubungsaufhebung noch nicht in ihrem erforderlichen Spielzustand praktisch geprüft.
- Portable-EXE und ZIP erhalten Paket-, Laufzeit- und Installationsprüfungen sowie
  SHA-256-Dateien. Diese Paketprüfungen ändern keine Spiele.
