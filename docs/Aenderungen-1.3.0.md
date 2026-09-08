# Floppy 1.3.0

Floppy verwendet jetzt ein externes Windows-Overlay. Die Graphit-Mint-Oberfläche der
Desktop-App bleibt erhalten, einschließlich Suche, Favoriten, Tastenkürzeln und Profilen.
Das Overlay zeigt die Optionen des verbundenen Spiels ohne die Desktop-Bibliotheksleiste.

## Bedienung

- Floppy geöffnet lassen und mit dem gestarteten Spiel verbinden.
- Im Spiel **Randloses Vollbild / Borderless Fullscreen** oder den Fenstermodus wählen.
- **F1** öffnet und schließt das Overlay. Die Schaltfläche **Overlay** öffnet es ebenfalls.
- **Esc** oder die Schließen-Schaltfläche kehren zum Spiel zurück.
- **Desktop** stellt das vorherige App-Fenster wieder her.
- Beim Wechsel in eine andere Anwendung blendet sich das Overlay aus.

Das Overlay folgt dem verbundenen Spielfenster und passt sich an dessen Größe und Monitor an.
Ein nicht verfügbares Spielfenster oder eine verlorene Verbindung beendet die Overlay-Ansicht.
Echtes exklusives Vollbild ist nicht unterstützt; Floppy verändert keine Anzeigeeinstellungen.
[Microsoft erläutert den Zusammenhang zwischen Vollbildoptimierungen und Overlays.](https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/)

## Vorhandene Installation aktualisieren

Das Spiel beenden, die neue Floppy-App starten und unter **Werkzeuge → Floppy einrichten /
reparieren** das Spielmodul aktualisieren. Danach das Spiel neu starten. Ein bereits laufendes
altes Modul kann weiterhin sein altes Menü anzeigen, bis das Spiel neu gestartet wurde.
Für das damalige Release 1.3.0 das vollständige Paket entpacken und den Ordner `runtime`
neben der EXE behalten. Ab 1.4.3 gibt es zusätzlich eine allein weitergebbare Portable-EXE;
die aktuellen Schritte stehen unter [Benutzen und weitergeben](../README.md#benutzen-und-weitergeben).

Die Unity-Menüoberfläche und das P.I.T.T.-Menüskript wurden entfernt. Die P.I.T.T.-Reparatur
migriert bestehende verwaltete Installationen und behält Sicherungen und Wiederherstellung bei.
Die Module im Spiel führen weiterhin die Spielfunktionen aus; die Menüanzeige läuft in Floppy.

## Verbindung und Eingaben

Unity und P.I.T.T. verwenden eine an die Verbindung und Spielsitzung gebundene Freigabe.
Floppy erneuert sie während des geöffneten Overlays. Nach einem Abbruch läuft sie spätestens
nach drei Sekunden ab und gibt den vom Modul verwalteten Cursor-/Eingabezustand zurück.
Verspätete Öffnungsanfragen werden verworfen. Bereits vorhandene modulabhängige Eingabesperren
bleiben erhalten; eine Pause oder vollständige Eingabesperre ist nicht für jedes Spiel verfügbar.

## Prüfungen

Die automatischen Prüfungen verwenden synthetische Spielpakete, lokale Testverbindungen,
Unity-Ersatztypen und eigene Windows-Testfenster. Sie prüfen Verbindung, Freigabe, Installation,
Migration, Wiederherstellung und Fensterverwaltung ohne echte Spiele oder Spielstände zu ändern.
Der Godot-Test führt das echte P.I.T.T.-Backend in einer isolierten Godot-Instanz aus.
Die UI-Prüfungen melden vom Betriebssystem verweigerte Vordergrundwechsel ausdrücklich als
übersprungen. Die Prüfungen ersetzen keinen Test im tatsächlichen Spiel.

Am 5. September 2026 bestanden: 116 Modellprüfungen, 36 App-Prüfungen, 56 Layoutprüfungen,
19 Installertests, 19 Godot-Backendprüfungen sowie Host-, Fenstergeometrie- und Python-Tests.
Der zusätzliche Overlay-UI-Test bestand 10 Prüfungen für App, Verbindung, Zielerkennung,
Skalierung und Wiederherstellung. Windows verweigerte den Vordergrundwechsel zum Testfenster;
der native Öffnen-/F1-/Erneuern-/Schließen-Prüfzweig wurde deshalb ausdrücklich übersprungen.
