# Floppy 1.2.0 – neue Desktop-Oberfläche

Die Windows-App verwendet jetzt eine ruhige Graphit-Mint-Farbpalette, klarere
Typografie und eine übersichtlichere Anordnung der vorhandenen Funktionen.

- Spielebibliothek mit Coverbildern, Auswahlmarkierung und kompaktem Suchlauf-Knopf.
- Ausgewähltes Spiel, Starten und Verbinden in einer eigenen Kopfzeile.
- Breites Suchfeld; Werkzeuge und „Alles aus“ bleiben direkt erreichbar.
- Links ausgerichtete Rubriken mit Anzahl und Tastaturbedienung.
- Optionszeilen mit mehr Abstand, rechts angeordneten Schaltern und Eingaben
  sowie breiteren Reglern unter ihrer Beschriftung. Lange Texte umbrechen.
- Einheitliche Hover-, Fokus- und deaktivierte Zustände, kontrastreiche Knopftexte,
  dunkle Kontextmenüs und Tooltips.
- Feste Statusleiste, besser gestalteter Leerzustand und sichtbare Angabe der
  aktuellen Verbindung direkt über den Optionen.
- Kürzere Übergänge; die Bewegung der Optionszeilen berücksichtigt die
  Windows-Einstellung für Animationen.
- Die native Titelleiste erhält unter Windows 11 passende Farben über die
  [offizielle DWM-Schnittstelle](https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute).

Die Überarbeitung betrifft die Desktop-App. Die Spielmodule bleiben auf ihrem
vorherigen Stand; Profile, Favoriten, Tastenkürzel und Spielordner werden weiterverwendet.

## Prüfung

- 56 UI-Prüfungen: gefüllte und leere Ansicht bei 980×560, 1180×760, 1440×900
  und zusätzlich 960×520 für den Platzbedarf des Fensterrahmens.
- Alle Eingabearten, lange Beschreibungstexte, Favoriten und aktive Zustände geprüft.
- 19 Desktop-Prüfungen zu Zahleneingabe, Einstellungen und Verbindungswechseln.
- 16 Installer-/Paketprüfungen gegen temporäre Dateien und das ausgelieferte Paket.
- Vollständiger Windows-x64-Publish mit eigener .NET-Laufzeit; keine Build-Warnungen.
- Die veröffentlichte EXE und die aus dem ZIP entpackte EXE bestehen den Selbsttest,
  einschließlich unsichtbarer Windows-Fensterinitialisierung und Laufzeitdateien.
- Gefüllte, schmale und leere Oberfläche als WPF-Bilder gerendert und visuell geprüft.

Die Vorschau verwendet gekennzeichnete Beispieldaten und vorhandene lokale Coverbilder.
Für die Prüfung wurden keine Spiele gestartet oder Spielstände verändert.
Tests im laufenden Spiel wurden nicht durchgeführt.

## Starten

`Floppy-1.2.0-win-x64.zip` vollständig entpacken und `Floppy.exe` starten.
Den Ordner `runtime` und die übrigen Dateien neben der EXE behalten.
Ein separates .NET ist für dieses Paket nicht erforderlich.
