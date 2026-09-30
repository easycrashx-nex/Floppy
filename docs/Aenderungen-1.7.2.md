# Floppy 1.7.2

## Minecraft Dungeons II: Server-Sitzungen erkennen

In einer Online-Runde konnte Floppy lokale Änderungen als verfügbar anbieten,
obwohl die Spielfigur vom Server verwaltet wurde. Ein sofortiges Zurücklesen
aus dem lokalen Speicher bestätigte dort keine tatsächliche Spielwirkung.

Der Adapter liest nun die reflektierte Netzwerkrolle der lokalen Figur.
Spieländerungen sind nur bei `ROLE_Authority` verfügbar. Bei einer Client-Figur
bleiben Lebenspunkte, Level, Erfahrung, Smaragde, geladene Munition und
Ressourcen sichtbar. Übersicht und Verbindungsstatus erklären ausdrücklich:
**Server-Spiel · nur Anzeigen unterstützt**.

Die 58 Regler, Währungsgutschriften und Auffüllfunktionen bleiben in diesen
Server-Sitzungen gesperrt. Laufende Schalter werden ausgeschaltet und Regler
auf ihren Normalwert gesetzt. Auch die Schreibfunktionen selbst prüfen die
Spielrolle; eine Änderung nach der letzten Abfrage wird vor dem Schreiben
erneut geprüft. Unbekannte Rollen erlauben ebenfalls nur Anzeigen.

Diese Version ergänzt **keine funktionierenden Online-Spielmodifikationen**.
Die bisherige externe Speicheranbindung steuert den entfernten Server nicht.
Eine vom Benutzer erstellte Lobby kann trotzdem serververwaltet sein.
Lokale Spielwerte mit bestätigter Authority behalten die bisherigen Optionen.

Die Rolleninterpretation folgt der [Unreal-Dokumentation von Epic](https://dev.epicgames.com/documentation/unreal-engine/actor-role-and-remote-role-in-unreal-engine?lang=en-US).

## Prüfung

- **126 native Fixture-Prüfungen**: einschließlich Client- und unbekannter Rollen,
  unveränderter Lebens-, Währungs-, Bewegungs- und Ressourcenwerte bei abgewiesenen
  Aktionen, lesbarer Ressourcen, Rückkehr zu Authority und Rollenwechsel zwischen
  Abfrage und Schreibversuch. Die normalen Tests greifen nur auf den Testprozess zu.
- In der laufenden Online-Runde wurden bei Figur und Controller `Role = 2`
  sowie `RemoteRole = 3` gelesen; der NetDriver besaß eine ServerConnection und
  die Spielwelt keinen lokalen AuthorityGameMode.
- Die neue lesende IPC-Prüfung zeigte eine verbundene Figur, den Server-Status,
  **0 verfügbare Spieländerungen** sowie echte Werte für alle fünf geprüften
  Anzeigen. Es wurden keine schreibenden Spieltests ausgeführt.

Alle 17 Regression-Suites und drei Python-Fixtures bestanden. Windows verweigerte
dem Overlay-Fixture den Vordergrundzugriff; dessen Fokus-/Hotkey-Teil wurde
ausgelassen. Die optionalen PDB-Fixtures des unveränderten Symboltests wurden
ebenfalls ausgelassen. Die Paketprüfungen werden beim Releasebau erneut ausgeführt.
Die Release-Ausgaben enthalten Portable-EXE, vollständiges ZIP und SHA-256-Dateien.
Die Grenzen der Einzelspieler-Tests aus 1.7.0 gelten weiterhin.

```powershell
dotnet run --project tests/Floppy.Dungeons2.Tests -c Release -p:BuildGameModules=false
```

Bewusste lesende Prüfung einer aktiven Server-Sitzung:

```powershell
dotnet run --project tests/Floppy.Dungeons2.Tests -c Release -p:BuildGameModules=false -- --probe-online
```

Hierfür muss der andere Floppy-Dienst beendet sein, da die Prüfung einen eigenen
Dienst am normalen lokalen IPC-Port verwendet. Die Prüfung verändert keine Spielwerte.
