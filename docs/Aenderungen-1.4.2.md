# Floppy 1.4.2 – Muttern und direkte Zugaktionen

**Muttern geben** verwendet jetzt den tatsächlichen Mutternbestand des Teams
(`TeamComponent.Bolts`). Der bisher verwendete `CheatSingleton.AddBolts` war ein
Entwicklerbefehl und kein Bestand; die entsprechende Komponente war in den geprüften
Runden nicht angelegt. Der neue Zugriff benötigt sie nicht.

Bei einem einzigen Team wird dieses automatisch gewählt. Bei mehreren Teams muss
ein Team ausdrücklich ausgewählt werden. Der Klick liest den Bestand frisch und
addiert die gewünschte ganze Anzahl von 1 bis 100000. Er verändert keine Statistik-,
Fortschritts- oder Sicherungsfelder. Ungültige Werte, Überlauf und zwischenzeitliche
Weltwechsel führen zu einer Fehlermeldung. Die Bestätigung liest ausschließlich nach;
sie wiederholt die Addition nicht.

Unter **Zug** stehen drei direkte Aktionen für einen eindeutig erkannten Zug:

- **Zug anhalten:** setzt einen fahrenden Zug auf Pause.
- **Zug weiterfahren:** nimmt einen von dieser Floppy-Sitzung bestätigten Halt desselben
  Zugs in derselben Runde zurück.
- **Zug abkühlen:** setzt die vorhandene Hitze der an diesem Zug befestigten Wagen
  einmalig auf null. Danach kann der Zug wieder warm werden.

Unkaputtbarkeit, Gratisgleise, globale Physikschalter und Automatik benötigen weiterhin
die vom Spiel nicht bereitgestellte Entwicklerkomponente. Diese zusätzlichen Optionen
sind durch die Änderung nicht repariert und bleiben bei fehlender Unterstützung gesperrt.

## Prüfung

In der laufenden Story-Runde von Steam-Build 24389330 wurden Spieler, fahrender Zug und
Team mit derselben Team-ID ausgelesen. Die korrigierte App erkannte den Mutternbestand
und gab alle vier Muttern-Bedienelemente frei. Vorhandene Hitze und Zugzustand wurden
ebenfalls ausschließlich gelesen. Auf Wunsch des Nutzers wurden keine Spielaktionen
ausgeführt; insbesondere wurde keine Test-Mutter hinzugefügt. Ob die Änderung in den
verschiedenen Shop-/Fortschrittsanzeigen erscheint, ist im echten Spiel noch zu testen.

Zwölf isolierte Regressionstestgruppen prüfen unter anderem Teamwahl, frischen Bestand,
Zahlenlimits, Fehlerweitergabe, verzögerte Bestätigung ohne wiederholtes Schreiben,
Weltwechsel, Zugzuordnung und das Weiterfahren nach eigenem Halt. Der Releasebuild,
die vollständige Paketprüfung und die Selbstprüfung der frisch entpackten EXE gehören
ebenfalls zur Auslieferungsprüfung. Die Spielwirkung der neuen Aktionen ist damit noch
nicht live bestätigt.

## Selbst testen

Die bisherige Floppy-App schließen und `Floppy.exe` aus dem vollständigen
1.4.2-Ordner starten. In einer laufenden Runde **Muttern → Wie viele → Muttern geben**
verwenden; bei mehreren Teams zuerst das Team wählen. **Bestand** zeigt den gelesenen
Teambestand. Eine nicht bestätigte Änderung erscheint als Fehler, nicht als Erfolg.
Vor einem erneuten Klick zunächst den Bestand im Spiel prüfen.
