# Floppy 1.7.1

Die Erweiterung aus [Version 1.7.0](Aenderungen-1.7.0.md) enthält 58 Regler,
Ressourcenfunktionen und Währungsgutschriften für Minecraft Dungeons II.

Diese Korrektur aktualisiert die Anzeigen für Smaragde, Level, Erfahrung,
nächste Levelgrenze und geladene Munition im normalen Modultakt. In 1.7.0 waren
sie an Änderungshandler gebunden, die bei reinen Anzeigen nicht aufgerufen
werden; dadurch blieb dort „–“ stehen. Die Aktionen waren davon unabhängig.

115 native Fixture-Prüfungen bestanden. Die bewusste lesende IPC-Prüfung
fordert zusätzlich für alle fünf Anzeigen einen tatsächlich befüllten Text.
Portable-EXE und vollständiges ZIP durchlaufen erneut die Paketprüfungen,
einschließlich Start ohne installierte .NET-Laufzeit, Weitergabe als einzelne
EXE und entpacktem ZIP. Die Grenzen der Spieltests aus 1.7.0 gelten weiterhin.
