# Floppy 1.8.1 – Checkpoints und Gegenstände

Dumb Ways to Build bekommt zwei zusätzliche Bereiche im externen Overlay und in
der Desktop-App. Unterstützt bleibt Spielversion **2.1.89 / Steam-Build 25644049**.

## Checkpoints

- Aktuelle Position unter einem eigenen Namen speichern; ein leeres Namensfeld
  erzeugt automatisch „Checkpoint 1“, „Checkpoint 2“ usw.
- Gespeicherten Punkt auswählen, dorthin teleportieren oder ihn löschen.
- Derselbe Name ersetzt den vorhandenen Punkt im jeweiligen Level.
- Punkte bleiben nach Neustarts erhalten. Floppy zeigt nur Punkte des aktuellen
  Levels an und verhindert Teleports zu Punkten anderer Levels.
- Maximal 128 Punkte, Namen bis 48 Zeichen. Die Datei liegt unter
  `%APPDATA%\Floppy\Checkpoints\DumbWays-2.1.89.json`.
  Fehlerhafte Dateien bleiben erhalten und werden nicht überschrieben.

## Gegenstände spawnen

- Aus den registrierten, aufnehmbaren Spielobjekten auswählen, gefiltert nach
  „Gegenstände & Werkzeuge“ oder „Materialien & Objekte“.
- 1–10 Objekte vor der eigenen Figur erzeugen. Die Liste wird schrittweise geladen;
  interne Vorlagen und Platzhalter werden ausgefiltert.
- Die Position und Entfernung des zuletzt erzeugten Objekts werden angezeigt.
- Benötigt eine eigene oder von dir verwaltete Sitzung. Erzeugte Objekte gehören
  zur Runde; die Aktion ist deshalb als gemeinsame Wirkung gekennzeichnet.

Checkpoint-Teleports verwenden die Teleportfunktion des Spiels und setzen den
Fallzustand zurück. Spawns verwenden den Netzwerk-Spawner des Spiels. Floppy
ändert dafür keine Spielstände und verteilt keine Spielobjektdateien.

## Korrektur

„Unverwundbar“ stellte den Schutz in Version 1.8.0 versehentlich im nächsten Frame
wieder zurück. Der Schutz bleibt jetzt aktiv, bis der Schalter ausgeschaltet wird.
Ein bereits vorhandener Schutzstatus wird weiterhin wiederhergestellt.

## Prüfung

- 34 Prüfungen für Persistenz, getrennte Levels, Namensersetzung, Löschen, ungültige Koordinaten,
  gesperrte Dateien, beschädigte Daten und Kapazitätsgrenze mit temporären Dateien geprüft.
- Im eigenen Bauauftrag den Checkpoint gespeichert, die Figur bewegt und exakt
  zum gespeicherten Punkt zurückteleportiert.
- Checkpoint nach einem Spielneustart erneut geladen und erfolgreich verwendet.
- 215 Gegenstände aus 514 registrierten Vorlagen erkannt. Einen Vorschlaghammer
  erzeugt; der Spiel-Spawner bestätigte ein gültiges Netzwerkobjekt. Sichtbarkeit
  und Aufnehmen wurden vom Spieler bestätigt.
- Tatsächlicher Schutzstatus über mehrere Sekunden durchgehend aktiv gelesen;
  nach Ausschalten wieder deaktiviert.
- Vollständiger Build ohne Fehler oder Warnungen. Alle 18 Prüfsuiten und drei
  Python-Fixtures bestanden. Zusätzliche native Symbol-Fixtures waren nicht
  eingerichtet; Windows verweigerte beim separaten Overlay-Test die
  Vordergrundfreigabe, weshalb dessen Fokus-/Hotkey-Prüfung ausgelassen wurde.
- Portable-EXE und ZIP werden mit Paket-, Laufzeit- und Installationsprüfungen
  sowie SHA-256-Dateien veröffentlicht.

Multiplayer und sämtliche einzelnen Gegenstandstypen sind nicht praktisch geprüft.
Punkte identifizieren das Level, nicht den Zustand beweglicher Bauobjekte. Speichere
sie daher an einer Stelle, die beim späteren Besuch noch frei und tragfähig ist.
