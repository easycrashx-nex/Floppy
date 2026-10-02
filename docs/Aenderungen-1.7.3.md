# Floppy 1.7.3

## Minecraft Dungeons II: Spielupdate unterstützen

Nach dem Steam-Update auf Build **25647713** blieben auch im Offline-Spiel alle
Aktionen gesperrt. Der Adapter für Build 25041023 erkannte die geänderte EXE
und konnte deren verschobene Unreal-Wurzeln nicht verwenden.

Floppy unterstützt nun beide geprüften Fassungen. Die SHA-256 der tatsächlichen
Spiel-EXE entscheidet, welche Objekt- und Namenswurzeln verwendet werden:

| Steam-Build | EXE-SHA-256 |
|---|---|
| 25041023 | `7c83afbf0ad34a40b853cdb25a22fffb605d08e2a1e2d431974d7c7c1ee0ba54` |
| 25647713 | `231147bd0c655a4ae73f90873675d42917f2bfb3a9ee164fc64f217d6d6bd4ef` |

Die neuen Wurzeln sind `GUObjectArray = 0xbf35a70` und `NamePool = 0xbe51ec0`.
Eigenschaftsabstände werden weiterhin über die Namen und Typen aufgelöst.
Unbekannte EXE-Fassungen bleiben gesperrt; eine Steam-Buildnummer allein reicht
nicht zur Freigabe. Übersicht und Diagnose nennen nun die ausgewählte Fassung.

Die [Server-Erkennung aus 1.7.2](Aenderungen-1.7.2.md) bleibt erhalten.
Serververwaltete und unbekannte Spielrollen erlauben nur Anzeigen.

## Prüfung

- **130 native Fixture-Prüfungen** im eigenen Testprozess bestanden. Neue Checks
  prüfen die Auswahl beider Fassungen, deren unterschiedliche Wurzeln und die
  Abweisung unbekannter EXE-Prüfsummen sowie bloßer Steam-Buildnummern.
- Der aktuelle Offline-Spielstand meldete `Role = 3`. Das echte IPC-Schema
  erkannte die Figur und meldete **68 verfügbare Spielaktionen** sowie echte
  Bestands- und Fortschrittsanzeigen.
- Alle **58 Regler** wurden im laufenden Spiel mit kleinen Werten geschrieben,
  zurückgesetzt und mit ihren ursprünglichen Werten verglichen: **58/58**.
- **Leben auffüllen** wurde einmal ausgeführt und mit **100 / 100** bestätigt.
  Das Spiel blieb während der Prüfung geöffnet.

Diese Prüfungen bestätigen die Speicherzuordnung und das Zurücksetzen; einzelne
Kampfeffekte, Beutechancen, Währungsgutschriften und dauerhafte Speicherung
wurden für diesen neuen Spielbuild nicht separat überprüft.

Alle 17 Regression-Suites und drei Python-Fixtures bestanden. Eine Fokusprüfung
des Overlay-Fixtures wurde wegen verweigerter Windows-Vordergrundberechtigung
ausgelassen; ebenso die optionalen PDB-Fixtures des unveränderten Symboltests.
Die Paketprüfungen gehören zum Releasebau.
Portable-EXE, vollständiges Windows-ZIP und beide SHA-256-Dateien werden
versioniert veröffentlicht; ältere Release-Dateien bleiben erhalten.
