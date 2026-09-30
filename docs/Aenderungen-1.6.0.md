# Floppy 1.6.0

## Minecraft Dungeons II: erster externer Adapter

- Steam erkennt das Spiel einschließlich der EXE unter `Dungeons/Binaries/Win64`.
- Floppy verbindet sich mit dem bereits laufenden Spiel, ohne Installation, Neustart
  oder Änderungen an seinen Dateien. F1 öffnet das vorhandene externe Overlay im
  Fenster oder randlosen Vollbild.
- **Spielfigur → Lebenspunkte** zeigt die aktuellen und maximalen Lebenspunkte.
- **Leben auffüllen** füllt den aktuellen Wert einmal bis zum aktuellen Maximum.
- **Leben halten** wiederholt das Auffüllen alle 100 ms. Tödliche Treffer zwischen
  diesen Abfragen können weiterhin töten; die Funktion ist keine Unverwundbarkeit.
- Beim Laden, einer besiegten Figur oder einer ungültigen Verbindung bleiben
  die Aktionen gesperrt. Der Adapter prüft die lokale Spieler-, Controller-, Figur-
  und Ability-System-Zuordnung vor jedem Schreibzugriff. Attribute fremder Figuren,
  Vorlagen, doppelte Treffer und ungültige Objektzeiger werden nicht verwendet.
- Lebensmaximum, Grundwerte, Währungen, Inventar und Speicherdateien werden nicht
  verändert. Das Ausschalten beendet die laufenden Auffüllungen.

## Unterstützte Fassung

Der Prototyp unterstützt Steam-Build **25041023** mit folgender EXE-Prüfsumme:

`7c83afbf0ad34a40b853cdb25a22fffb605d08e2a1e2d431974d7c7c1ee0ba54`

Die beiden Unreal-Wurzeln sind für diese Fassung ermittelt; Feldabstände werden
über die Namen und Eigenschaften des Spiels aufgelöst. Eine andere EXE wird mit
einem erklärenden Status abgewiesen, bis der Adapter dafür geprüft wurde.
Der bestehende Mortal-Shell-Adapter behält sein eigenes Property-Layout.

## Prüfung

20 native Fixture-Prüfungen decken die erfolgreiche Auffüllung, unveränderte
Grundwerte und fremde Attribute, die Spielerkette, ungültige Werte, veraltete
Objektzeiger, unlesbare und mehrdeutige Attributlisten sowie die Wiederherstellung
der Verbindung sowie einen Wechsel der Spielfigur ab. Diese Prüfungen greifen ausschließlich auf den eigenen
Testprozess zu.

Im laufenden Spiel wurde die lokale Figur erkannt und einmal **Leben auffüllen**
ausgeführt. Anschließend wurden **125 / 125** Lebenspunkte zurückgelesen.
Der Prozess blieb geöffnet (PID 33180). Dieser Test bestätigt keine dauerhafte
Unverwundbarkeit, keinen Mehrspielermodus und keine andere Spielversion.
Auch der externe Dienst wurde gegen das laufende Spiel geprüft: Das echte
IPC-Schema meldete die lokale Spielfigur als bereit und beide Aktionen als verfügbar.
Die 17 Regression-Suites sowie drei Python-Fixtures bestanden. Windows verweigerte
dem Overlay-Fixture den Vordergrundzugriff; dieser Fokus-/Hotkey-Teil wurde ausdrücklich
ausgelassen. F1 im echten Spiel wurde dadurch nicht nachgewiesen.

Reguläre Modulprüfung:

```powershell
dotnet run --project tests/Floppy.Dungeons2.Tests -c Release -p:BuildGameModules=false
```

Bewusst gestartete lesende Prüfung eines laufenden Spiels:

```powershell
dotnet run --project tests/Floppy.Dungeons2.Tests -c Release -p:BuildGameModules=false -- --probe-game
```

Nur der zusätzliche Parameter `--heal` führt einmal eine Auffüllung aus.
