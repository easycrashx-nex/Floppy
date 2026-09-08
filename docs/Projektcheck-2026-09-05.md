# Floppy – Projektcheck und nächste Schritte

Historische Bestandsaufnahme vor den Änderungen. Die anschließende Umsetzung und
ihre Prüfergebnisse stehen in [Änderungen 1.1.0](Aenderungen-1.1.0.md).

Stand: 5. September 2026. Geprüft wurden Quellcode, Build, Paketierung, Modelllogik und ausgewählte WPF-Bedienelemente. Diese Bestandsaufnahme enthält Empfehlungen; es wurden keine Funktionsänderungen eingebaut.

Floppy hat eine brauchbare Grundlage: ein gemeinsames Optionsmodell, eine dynamisch aufgebaute Desktop-App, gemeinsame Unity-Oberflächen und getrennte Spielmodule. Ich würde diesen Aufbau behalten. Der größte unmittelbare Nutzen liegt in verlässlichen vorhandenen Funktionen und einer vollständigen Auslieferung. Danach lohnen sich Favoriten, bessere Zustandsanzeigen und ein einheitlicher Schnellzugriff.

## Was überprüft wurde

| Prüfung | Ergebnis und Grenze |
|---|---|
| Release-Build aus einer separaten Quellkopie ohne vorhandene bin/obj | Erfolgreich: 0 Fehler, 4 NU1603-Warnungen. Nutzt weiterhin die auf diesem Rechner installierten Spiel-DLLs und ODDCORE-Interop-Dateien. |
| Reguläres Publish nach diesem Build | Erfolgreicher Befehl, aber unvollständige Auslieferung: 7 Dateien, kein runtime-Verzeichnis. Die Build-Ausgabe enthält dagegen die benötigten Unity-Loader und Plugins. |
| Isoliertes Prüfprogramm mit aktuellen Model-Quelldateien | Profilfehler, fehlerhafte Rücksetzsemantik, Ausführung nach Timeout und fehlende Zahlenvalidierung reproduziert. Nur der Profilpfad einer temporären Quellkopie wurde auf ein Testverzeichnis umgestellt. |
| WPF-Messung ohne angezeigtes Fenster oder laufenden Polling-Timer | Überlappung der Kopfzeile bei 980 Pixeln gemessen. Die aktuell verwendete Zahlenkonvertierung interpretiert 1,5 als 15. |
| Vergleich vor und nach der Prüfung | 96 kopierte Quell-, Konfigurations- und Vendor-Dateien per SHA-256 verglichen; keine Änderung. |
| Spieltests | Nicht durchgeführt. Keine realen Profilbestände, Spielstände, Spieleinstellungen oder Spielinstallationen verändert. Statische Befunde sind unten ausdrücklich als solche bezeichnet. |

## Zuerst verbessern

### 1. Profile tatsächlich wieder laden können

**Priorität: hoch. Isoliert reproduziert.**

`Profile.Speichern()` schreibt ein JSON-Array. `Profile.Laden()` erwartet dieses Array als Liste von Objekten. Der eigene JSON-Leser versteht bisher ausschließlich primitive Werte. Damit versteht Floppy sein selbst geschriebenes Profilformat nicht.

- Beleg: `src/Floppy.Model/Profile.cs:123`, `src/Floppy.Model/Json.cs:182`.
- Prüfergebnis: „Profil gespeichert (5 Werte)“, anschließend „Profil ist leer“; der gespeicherte Schalter wird nicht wieder eingeschaltet.
- Änderung: JSON-Leser und tatsächlich verwendetes Profilformat zusammenbringen. Eine kleine, geprüfte Erweiterung um verschachtelte Objekte und Arrays ist eine mögliche Lösung. Einen weiteren selbstgebauten Spezialparser würde ich vermeiden.
- Abnahme: Speichern und Laden aller unterstützten Zustandstypen, einschließlich Änderungs-Callbacks; beschädigte Dateien verständlich ablehnen. Später Profilformat versionieren und Auswahlen möglichst über stabile Kennungen speichern.

### 2. „Alles aus“ und „Aktiv“ auf verlässliche Zustände stellen

**Priorität: hoch. Modellverhalten mit entsprechenden Optionsdefinitionen reproduziert; Spielwirkung im Code belegt.**

How to Fish setzt für jeden Slider mit Minimum null pauschal den Ruhewert auf null. Bei Streuung und Rückstoß ist der normale Multiplikator jedoch eins. Null aktiviert dort perfekte Präzision beziehungsweise unterdrückt Rückstoß. Schaden und Feuerrate besitzen dagegen keinen Ruhewert und bleiben verändert. Auswahllisten werden vom gemeinsamen Reset nicht zurückgesetzt.

- Beleg: `src/Floppy.HowToFish/HowToFishModule.cs:104`, `:698`, `:711`, `:724`; `src/Floppy.Model/Profile.cs:181`.
- Prüfergebnis: Streuung 1 → 0, Schaden bleibt 3, Fangmodus bleibt 2; trotzdem meldet der Reset eine ausgeschaltete Funktion.
- Änderung: Einen ausdrücklich definierten Normalzustand pro rücksetzbarer Option verwenden. Direkte Spielwerte wie Maximalleben benötigen eine eigene Entscheidung; null ist dort kein allgemeiner Ausschaltwert. Auswahlen nur mit ausdrücklich festgelegtem Normalwert zurücksetzen.
- Zusätzlich: Die Desktop-Anzeige „Aktiv“ verwendet für viele Werte lediglich „in dieser Sitzung angefasst“. Änderungen über Overlay oder Profile werden dadurch nicht zuverlässig erfasst. Nach „Alles aus“ wird diese Merkliste auch bei Fehlern gelöscht.
- Abnahme: Aus einem normalen Ausgangszustand darf Reset keine Modifikation aktivieren; nach Fehlern müssen verbleibende Änderungen sichtbar bleiben. Bereits veränderte Spielstände lassen sich durch Ausschalten eines Toggles nicht allgemein zurückdrehen.

### 3. Zahlen überall gleich und gültig verarbeiten

**Priorität: hoch. Isoliert reproduziert.**

Die deutsche Desktop-Oberfläche versucht zuerst eine Konvertierung mit invariantem Zahlenformat und erlaubten Tausendertrennzeichen. Deshalb wird `1,5` erfolgreich als `15` gelesen; die anschließende Prüfung mit lokaler Kultur wird nicht mehr erreicht. Gleichzeitig prüft die IPC-Seite Werte nicht ausreichend.

- Beleg: `src/Floppy.App/MainWindow.xaml.cs:1172`, `src/Floppy.Model/IpcServer.cs:211`, `src/Floppy.Model/Json.cs:46`.
- Prüfergebnisse: `1,5` → 15, `0,5` → 5. IPC akzeptiert 999 bei einem Bereich von 0 bis 10. `1e100` wird beim Float-Cast unendlich und erzeugt eine ungültige JSON-Antwort.
- Änderung: Dezimalpunkt und Dezimalkomma bewusst behandeln, mehrdeutige Gruppierungszeichen vermeiden. Im gemeinsamen Modell Typ, endliche Zahl, Darstellbarkeit, Bereich und gültigen Auswahlindex vor der Änderung prüfen. Dieselbe Validierung beim Laden von Profilen nutzen.
- Abnahme: Deutsche und schweizerische Eingaben, Grenzen, Überläufe und ungültige Auswahlindizes. Abgewiesene Werte dürfen weder Zustand noch Verbindung beschädigen.

### 4. Befehle, Zeitüberschreitungen und Spielwechsel zuverlässig behandeln

**Priorität: hoch. Dispatcher-Problem reproduziert; Host- und Client-Probleme statisch geprüft.**

Ein Dispatcher-Timeout beendet nur das Warten. Die Aktion bleibt in der Warteschlange und wird beim späteren Pumpen ausgeführt. Wiederholt der Nutzer einen vermeintlich fehlgeschlagenen Befehl, kann die Wirkung doppelt eintreten.

- Beleg: `src/Floppy.Model/Dispatcher.cs:41`.
- Prüfergebnis: Timeout bei Effektzähler 0; späteres Pumpen erhöht den Zähler auf 1.
- Änderung: Abgelaufene, noch nicht gestartete Aufträge überspringen. Bei bereits gestarteten Aktionen keinen sicheren Abbruch behaupten, sondern den Ausgang ausdrücklich behandeln.

Die externen Hosts arbeiten zusätzlich mit Timern ohne Schutz vor überlappenden Aufrufen. Unreal tickt alle 200 ms, während einzelne Aufrufe bis zu zwei Sekunden warten. Unrailed tickt jede Sekunde und erlaubt HTTP-Wartezeiten bis zu 30 Sekunden. Das kann mehrere Spielzugriffe gleichzeitig auslösen. Unreal verwendet dabei gemeinsame Aufrufpuffer und prüft den Ausgang des Wartens auf den Remote-Thread nicht ausreichend.

- Beleg: `src/Floppy.Unreal/Host.cs:48`, `src/Floppy.Unreal/Aufruf.cs:158`, `src/Floppy.Unrailed2/Host.cs:55`, `src/Floppy.Unrailed2/Debugger.cs:33`.
- Änderung: Pro Host genau eine ausführende Schleife; Stoppen mit geordnetem Abschluss und Freigabe der Ressourcen. Aufrufpuffer erst nach bestätigtem Abschluss wiederverwenden.
- Client ergänzend: Antwortfrist für `IpcClient.SendAsync()`, höchstens eine laufende Statusabfrage und Zusammenfassung schnell aufeinanderfolgender Sliderwerte. Aktuell kann das Lesen unbegrenzt warten, während der UI-Timer weitere Anfragen einreiht.
- Abnahme: Langsamer Fake-Host, zwei Befehle, Abbruch während einer Anfrage, Spielwechsel. Keine verspätete wartende Mutation, keine parallele Ausführung und keine endlos anwachsende Anfragewarteschlange.

### 5. Installation und Veröffentlichung vollständig machen

**Priorität: hoch. Publish-Lücke reproduziert; übrige Punkte statisch geprüft.**

Der dokumentierte Einstieg baut noch das nicht vorhandene `Floppy.Core`. Ein erkannter oder manuell angegebener Spielpfad wird nicht an den Build weitergegeben. Das Skript kann schon vorher BepInEx installieren und anschließend beim Build scheitern.

- Beleg: `deploy.ps1:98`, `:112`, `:122`; `Directory.Build.props:4`.
- Änderung: Build zuerst abschließen, anschließend vollständige Artefakte prüfen und erst dann installieren. Aktuellen Projektgraphen verwenden und lokale Spielpfade tatsächlich weiterreichen. README synchronisieren.

Das normale Publish-Paket enthält keine Unity-Loader oder Plugins. Die eigenen Kopierregeln laufen nur nach Build und deklarieren die Dateien nicht als Publish-Inhalt.

- Beleg: `src/Floppy.App/Floppy.App.csproj:37`.
- Abnahme: Aus einer leeren Ausgabe bauen und veröffentlichen; das fertige Paket muss alle vorgesehenen Laufzeitdateien enthalten. Die bereitgestellte EXE zusammen mit genau diesem Paket testen.

Project P.I.T.T. wird in der Bibliothek angeboten, seine Skripte und PCK-Einrichtung werden aber nicht mit ausgeliefert. Der Installer meldet bei allen fremden Engines pauschal „bereit“ beziehungsweise „Kein Einrichten nötig“.

- Beleg: `src/Floppy.App/GameCatalog.cs:40`, `src/Floppy.App/Installer.cs:61`, `:104`.
- Änderung: Die Installationsart je Spiel ausdrücklich beschreiben. Pitt installieren und reparieren können; bis dahin seinen tatsächlichen Einrichtungszustand anzeigen. Die PCK-Datei vor Änderungen sichern und eine Wiederherstellung anbieten.

### 6. Dateien und Einstellungen bei Fehlern erhalten

**Priorität: hoch. Statisch geprüft.**

Der Unity-Installer prüft für „bereit“ lediglich `winhttp.dll` und `Floppy.Model.dll`. Fehlende Engine- oder Spielplugins bleiben unentdeckt. Beim Einrichten reicht bereits irgendeine kopierte DLL für die Erfolgsmeldung. Teilweise fehlgeschlagene Kopiervorgänge hinterlassen einen gemischten Stand.

- Beleg: `src/Floppy.App/Installer.cs:52`, `:142`.
- Änderung: Kleines Manifest der erforderlichen Dateien pro Spiel; Quellen vorab prüfen, Änderungen vorbereiten und bei Fehlern den bisherigen Stand wiederherstellen. Beim Entfernen nur Dateien anfassen, deren Zugehörigkeit zu Floppy feststeht.

Unrailed behandelt außerdem jeden Lesefehler seiner Einstellungsdatei wie eine leere Datei. Gelingt danach das Schreiben, können die sonstigen Einstellungen verloren gehen. Das Pitt-Werkzeug ersetzt das PCK ohne integriertes Originalbackup.

- Beleg: `src/Floppy.Unrailed2/Spiel.cs:55`, `:78`, `:106`; `tools/godot_pck.py:213`.
- Änderung: Fehlende Datei und Lesefehler unterscheiden, nach einem Lesefehler abbrechen, vor dem Ersetzen eine Sicherung anlegen. Erfolgreiche Änderungen sollen ausschließlich die vorgesehenen Einstellungen verändern.
- Abnahme: Temporäre Beispieldateien und simulierte Lese-/Kopierfehler; unverwandte Inhalte müssen bytegenau erhalten bleiben. Keine echten Spielinstallationen für diese Tests verwenden.

### 7. Menüs nach dem Laden eines Spielstands aktualisieren

**Priorität: mittel. Statisch geprüft.**

Mortal Shell II baut dynamische Attribut- und Fortschrittskategorien nur bei der ersten Modulregistrierung auf. Verbindet Floppy sich im Hauptmenü, können diese Listen noch leer sein. Spätere Suchläufe finden die Figur und melden Bereitschaft, erstellen aber die fehlenden Kategorien nicht neu.

- Beleg: `src/Floppy.Unreal/MortalShellModule.cs:145`; `src/Floppy.Model/Registry.cs:30`; `src/Floppy.Model/IpcServer.cs:127`.
- Änderung: Nach erfolgreicher Erkennung fehlende Kategorien ergänzen und dem Client eine Änderung des Schemas melden. Auswahl und gesetzte Werte dabei erhalten.

Auch das automatische Wiederanwenden kann nach einem Ladebildschirm ausbleiben: Der neue Weltname wird vor einer erfolgreichen Suche gespeichert. Ein später erfolgreicher Versuch ruft die Wiederherstellung nicht mehr auf.

- Beleg: `src/Floppy.Unreal/MortalShellModule.cs:81`, `:88`.
- Abnahme: Hauptmenü → Spielstand laden sowie Suchfolge „nicht gefunden, nicht gefunden, gefunden“. Neue Funktionen müssen erscheinen; Wiederanwenden muss genau einmal nach erfolgreicher Suche stattfinden.

### 8. Tests, Projektpflege und Oberfläche gezielt aufräumen

**Priorität: mittel. Gemischte Prüfung, unten unterschieden.**

- **Rauchtest:** Er führt alle nicht ausgeschlossenen Buttons aus und kann echte Welt-/Kontostände verändern. Zurückschalten stellt diese Wirkungen nicht wieder her; außerdem endet er auch bei gesammelten Fehlern mit Exitcode 0. Statisch belegt in `tools/rauchtest.py:124` und `:151`. Standardmäßig nur Verbindung und Schema prüfen; ausdrückliche reversible Testfälle, Wiederherstellung in `finally` und korrekter Exitcode. Den vorhandenen Test nicht pauschal gegen eine normale Spielsitzung laufen lassen.
- **Gezielte Tests:** Profil-Roundtrip, Normalzustände, Zahlenvalidierung, Timeouts und Paketvollständigkeit als kleine Regressionstests aufnehmen. Diese Tests benötigen keine installierten Spiele. Ein Fake-Modul kann zugleich die Desktop-Oberfläche mit Beispieldaten versorgen.
- **Build-Entkopplung:** UI und gemeinsames Modell sollten sich ohne sämtliche unterstützten Spiele entwickeln lassen. Spielmodule getrennt bauen; vollständige Paketierung in einem eigenen Buildschritt zusammenführen. Keine zweite Architektur dafür einführen.
- **Abhängigkeiten:** Der aktuelle Restore ersetzt eine nicht verfügbare Cpp2IL-Abhängigkeitsversion und meldet NU1603. Eine bewusst geprüfte, reproduzierbare Auflösung festhalten; die Warnung nicht lediglich unterdrücken.
- **Git:** `.gitignore` für Build-Ausgaben und Python-Caches ergänzen, notwendige Vendor-Dateien bewusst auswählen und einen ersten nachvollziehbaren Ausgangsstand versionieren. Aktuell existiert noch kein Commit.
- **WPF-Kopfzeile:** Bei der erlaubten Mindestbreite von 980 Pixeln überlappt der Statusbereich im isolierten Layouttest den Aktionsbereich um rund 67 Pixel. Bei 1180 Pixeln trat dies mit denselben Beispieldaten nicht auf. Suche/Aktionen bei wenig Platz auf eine zweite Zeile verteilen; ausführliche Statusmeldungen umbrechen oder kürzen.
- **Rückmeldungen:** Erfolg und Fehler nutzen dieselbe Statuszeile wie das 700-ms-Polling und können dadurch sofort überschrieben werden. Aktionsmeldungen kurz sichtbar halten und zusätzlich in einer kleinen lokalen Ereignisliste erfassen.
- **Struktur:** `MainWindow.xaml.cs` bündelt Datenabgleich, Verbindungssteuerung und alle Renderer auf über 1.300 Zeilen. Beim Bearbeiten entlang dieser drei Verantwortungen aufteilen. WPF und die vorhandenen Renderfunktionen beibehalten; kein vollständiger UI-Neubau nötig.

## Was ich hinzufügen würde

| Ergänzung | Nutzen | Reihenfolge |
|---|---|---|
| Favoriten pro Spiel | Häufig verwendete Funktionen oben anheften; vorhandene Suche und Rubrik „Aktiv“ weiterverwenden. | Nach den Funktionskorrekturen zuerst |
| Diagnoseansicht | Erkanntes Spiel, Modul-/Floppy-Version, Einrichtungszustand, letzter Verbindungsfehler und Protokoll öffnen. Bei nicht verfügbaren Funktionen einen konkreten Grund zeigen. | Zusammen mit Installation/Verbindung |
| Reparieren und Wiederherstellen | Fehlende eigene Dateien gezielt ergänzen, vorherige Installation bzw. gesicherte PCK-/Einstellungsdatei wiederherstellen. | Zusammen mit vollständiger Paketierung |
| Frei belegbare Tastenkürzel | Wenige ausgewählte Funktionen ohne Menünavigation bedienen; pro Spiel speichern und Konflikte anzeigen. Bestehende F1-Bedienung erhalten. | Nach stabilen Zuständen und Reset |
| Suche und Favoriten im Ingame-Menü | Auch bei großen Modulen schnell zur gewünschten Funktion kommen. Desktop-Suche existiert bereits; Unity-Overlay hat bisher Kategorienavigation. | Nach Desktop-Favoriten |
| Fenstergröße, Position und Skalierung merken | Nutzung an kleineren Displays und mit höherer Windows-Skalierung erleichtern. DPI-Umrechnung beim externen Overlay ebenfalls gezielt prüfen. | Im Zuge der UI-Korrekturen |
| Profilvorschau und gezieltes Anwenden | Vor dem Laden sehen, welche Zustände geändert werden; unbekannte Optionen und Fehler verständlich melden. | Erst wenn normales Speichern/Laden zuverlässig funktioniert |
| Optionaler Spielordner von Hand | Installationen ergänzen, die Steam-Erkennung nicht findet; Pfad pro Spiel speichern und prüfen. | Nach sauberem Installationsmodell |

Diese Ergänzungen bauen auf vorhandenen Funktionen auf. Suche, Profile, Wirkungsbereich für Mitspieler und die Rubrik „Aktiv“ existieren bereits und müssen nicht erneut entwickelt werden.

## Sinnvolle Umsetzung in drei Etappen

1. **Verhalten verlässlich machen:** Profile, Reset, Zahleneingaben und zentrale Validierung korrigieren; Dispatcher und externe Hosts stabilisieren. Dazu die wenigen gezielten Regressionstests. Ergebnis: eine neue gebaute und überprüfte Windows-Version, deren Grundfunktionen verlässlich sind.
2. **Einrichtung und Auslieferung vervollständigen:** Build/Deploy aktualisieren, Publish-Inhalt prüfen, tatsächliche Bereitschaft anzeigen, Pitt-Einrichtung und Wiederherstellung ergänzen, Dokumentation und Git-Grundlage aufräumen. Ergebnis: ein vollständiges, versioniertes Paket mit testbarer EXE.
3. **Tägliche Bedienung verbessern:** Favoriten, Diagnose, übersichtliche Rückmeldungen und anpassbare Fenster; anschließend Tastenkürzel und Suche im Overlay. Ergebnis: dieselben Funktionen sind schneller auffindbar und Fehler leichter erklärbar.

Vorerst zurückstellen würde ich zusätzliche Spiele, einen Wechsel des UI-Frameworks, Benutzerkonten, Cloud-Synchronisation, einen Plugin-Marktplatz und eine automatische Update-Infrastruktur. Dafür gibt es aus dieser Prüfung keinen dringenden Bedarf. Die bestehende Unterstützung zu vervollständigen bringt früher einen überprüfbaren Nutzen.

## Lokale Prüfnachweise

Die Hilfsprogramme und Build-Ausgaben liegen ausschließlich in temporären Verzeichnissen:

- Build, Publish und WPF-Messung: `%TEMP%\floppy-review-20260905-160435`.
- Modellprüfungen: `%TEMP%\floppy-model-review-6ce1fd18c0f74f7794559ce634aa6b6d`.

Die isolierten Tests bestätigen die beschriebenen Modell- und Layoutfehler. Sie ersetzen keine Spieltests der späteren Korrekturen. Dieser Bericht ist die einzige im Projekt hinzugefügte Datei.
