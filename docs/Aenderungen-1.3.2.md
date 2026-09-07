# Floppy 1.3.2 – Fenstererkennung für F1

Die Overlay-Fenstererkennung benötigt jetzt ausschließlich lesenden Zugriff auf
Prozessmetadaten. Bisher scheiterte sie bei einem mit Administratorrechten gestarteten
Spiel bereits beim Lesen der EXE-Modulliste. Dadurch blieb F1 ohne Wirkung.

Pfad, Startzeit und Prozessstatus werden über einen gemeinsamen Handle mit eingeschränkten
Leserechten geprüft. Die Bindung an Prozesskennung, Startzeit, Installationsordner und
Spielfenster bleibt erhalten. Das Overlay kann damit sein Spielfenster auch erkennen,
wenn der Spieladapter wegen fehlender Speicherzugriffsrechte noch nicht bereit ist.

Diese Änderung erteilt keine zusätzlichen Rechte für Spielfunktionen. Wenn das Spiel mit
Administratorrechten läuft, Floppy unter **Werkzeuge → Floppy als Administrator neu starten**
mit passenden Rechten öffnen. Windows fragt nach Bestätigung. Erst bei erfolgreichem
Spielzugriff sind die Optionen verfügbar.

## Geprüft

- Am laufenden Mortal Shell II ließ sich der bisherige Fehler ohne Spieländerungen
  reproduzieren: alte Fenstererkennung erfolglos, neue Fenstererkennung erfolgreich.
- Prozesskennung, Startzeit, Installationspfad und Fenstergröße 2560 × 1440 wurden bestätigt.
- Die isolierten Fensterprüfungen für falsche Pfade, wiederverwendete Prozesskennungen,
  Größenänderung, Minimierung, Schließen und DPI-Kontext bestehen.
- Die anschließend vom Benutzer neu gestartete Floppy-Instanz meldet im echten Spiel
  Bereitschaft mit 162 Attributen und einer geladenen Spielfigur. Spieleingaben und
  Optionsänderungen testet der Benutzer selbst.
