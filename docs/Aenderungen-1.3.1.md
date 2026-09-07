# Floppy 1.3.1 – Mortal Shell II

Das installierte Spielupdate (Steam-Build 25133113) verschob alle vier vom Adapter
verwendeten Adressen. Zusätzlich verhinderte ein Rechtekonflikt den Prozesszugriff;
die fehlgeschlagene Initialisierung blockierte bisher die Antworten an die Oberfläche.

## Korrekturen

- Objektliste, Namen, Welt und ProcessEvent werden aus der lokalen, zur Spiel-EXE
  passenden PDB ermittelt. Alte feste Adressen entfallen. Die Kennung der PDB und
  die Speicherbereiche werden vor Verwendung geprüft; unbekannte Daten werden abgelehnt.
- Der Dienst beantwortet Anfragen auch dann, wenn das Spiel noch nicht erreichbar ist.
  Windows-Fehler 5 wird als fehlende Zugriffsberechtigung angezeigt.
- Fehlgeschlagene Spielaktionen und Schreibvorgänge melden einen Fehler an die UI.
  Fehlgeschlagene Werte werden nicht für spätere Ladebildschirme vorgemerkt.
- Fehlerhafte oder unlesbare Property-Offsets gelten nicht mehr als gültiger Offset null.
- Unter **Werkzeuge** kann Floppy für Mortal Shell II mit Administratorrechten neu
  gestartet werden. Windows fragt dabei nach Bestätigung; ein Abbruch lässt Floppy geöffnet.

## Starten

Das vollständige Paket entpacken und `Floppy.exe` starten. Das Mortal-Shell-II-Modul läuft
in dieser App; eine Installation im Spielordner ist nicht nötig. Die vom Spiel mitgelieferte
PDB muss neben `MortalShell2-Win64-Shipping.exe` erhalten bleiben. Im Spiel einen Spielstand
laden und für das externe Overlay randloses Vollbild wählen. **F1** öffnet das Overlay.

Spiel und Floppy benötigen dieselben Zugriffsrechte. Bei erhöht gestartetem Spiel den
Neustart unter **Werkzeuge** verwenden oder beide Programme ohne Administratorrechte starten.

## Entwicklung und gezielte Prüfung

`tools/test.ps1` enthält jetzt auch die Unreal-Speicher- und Symboltests.
Die regulären Tests verwenden ausschließlich eigene Testprozesse und temporäre Fixtures.
Eine ausdrückliche, lesende Prüfung des laufenden Spiels ist separat möglich:

```powershell
dotnet run --project tests/Floppy.Unreal.Tests -c Release -- --probe-game
```

Diese Diagnose verändert keine Spieloption. Für das aktuell erhöhte Spiel muss sie
mit entsprechenden Benutzerrechten laufen.

Geprüft: vollständige Testsuite; zusätzlich 13 Symbolprüfungen mit der installierten
Spielfassung und eigenen nativen Fixtures, 23 Unreal-Speicherprüfungen und 6 Host-Testgruppen.
Der Overlay-Test bestand 28 Prüfungen; ein einzelner Wechsel des Testfensterfokus wurde
vom Betriebssystem verweigert und ausdrücklich übersprungen. Die lesende Spielprobe
bestätigt derzeit den Rechtekonflikt. Der vollständige Funktionstest im Spiel bleibt offen,
bis Spiel und Diagnose/Floppy mit passenden Rechten laufen.
