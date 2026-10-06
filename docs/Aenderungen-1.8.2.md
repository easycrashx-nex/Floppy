# Floppy 1.8.2 – Freischaltungen für Dumb Ways to Build

Der neue Bereich **Freischaltungen** zeigt den tatsächlichen Profilfortschritt und
bietet **Alles freischalten** sowie einzelne Aktionen für Level, schweren Modus,
Kosmetik, Emotes und lokale Zertifikate. Änderungen bleiben nach Neustarts erhalten.
Unterstützt bleibt **Spielversion 2.1.89 / Steam-Build 25644049**.

## Verwendung

Lade deine eigene Lobby, öffne Floppy und wähle **Freischaltungen**.
**Alles freischalten** schaltet die erkannten regulären Inhalte gesammelt frei.
Kosmetik, Emotes und lokale Zertifikate lassen sich auch einzeln freischalten;
Level und schwerer Modus benötigen eine lokal verwaltete Sitzung mit Levelauswahl.
Öffne eine bereits sichtbare Kosmetik- oder Zertifikatsansicht anschließend erneut,
damit das Spiel ihre Bedienelemente aktualisiert.

Die Funktion verwendet die Spielkataloge und die reguläre Speicherung des Spiels.
Level und schwerer Modus verwenden die vorhandenen Freischaltungsfunktionen.
Zertifikate werden als lokaler Fortschritt gespeichert; Floppy ruft deren
Plattform-Erfolgsvergabe nicht auf. Twitch-Drops, Besitznachweise und nicht verfügbare
Kosmetik werden nicht übergangen. Multiplayer wurde nicht praktisch geprüft.

## Sicherung

Vor jeder Freischaltungsaktion speichert Floppy den aktuellen Spielstandpuffer und
sichert die vorhandenen `.data`-Dateien unter
`%APPDATA%\Floppy\Backups\DumbWays\<Zeitpunkt-ID>`.
Die Sicherung enthält eine Dateiliste mit SHA-256-Prüfsummen. Bestehende Sicherungen
werden nicht überschrieben. Bei einem Sicherungsfehler beginnt die Freischaltung nicht.
**Spielstand jetzt sichern** erstellt zusätzlich eine Sicherung ohne Freischaltung.

„Alles aus“ setzt diese dauerhaften Freischaltungen nicht zurück. Eine Rücksetzung
braucht die vorherige Sicherung und ein beendetes Spiel. Neu entstandene Spielstände
können zusätzlich über den Speicherdienst des Spiels zurückgesetzt werden müssen;
das Entfernen einer lokalen Datei allein reichte beim schweren Modus im Test nicht.

## Prüfung

- Live in einer eigenen Lobby: **102/102 Kosmetik- und Emote-Einträge**,
  **56/56 lokale Zertifikate**, **3/3 Level** und **3/3 schwere Level** bestätigt.
  Normallevel waren bereits vorher spielbar; diese Prüfung war dort idempotent.
- Nach einem Spielneustart dieselben Freischaltungsstände erneut ausgelesen.
- Die sechs vor dem Test vorhandenen Spielstanddateien gesichert und ihre Kopien
  per SHA-256 bestätigt. Den bisherigen Fortschritt nach dem Test wiederhergestellt.
- 42 Checkpoint- und Sicherungsprüfungen mit temporären Dateien: genaue Kopien,
  Originalerhalt, getrennte Sicherungen, Dateisperren, leere Ordner und Grenzen.
- Alle 18 Prüfsuiten und drei Python-Fixtures bestanden. Zusätzliche native
  Symbol-Fixtures waren nicht eingerichtet; Windows verweigerte beim separaten
  Overlay-Test die Vordergrundfreigabe, weshalb dessen Fokus-/Hotkey-Prüfung entfiel.
- Portable-EXE und ZIP erhalten Paket-, Laufzeit- und Installationsprüfungen sowie
  SHA-256-Dateien. Diese Paketprüfungen ändern keine Spiele.
