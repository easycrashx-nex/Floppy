# Floppy 1.4.3

## Weitergabe

`Floppy-1.4.3-Portable.exe` lässt sich allein weitergeben. Sie enthält die .NET- und
WPF-Laufzeit, Spielmodule, Loader und Itembilder. Beim ersten Start entpackt sie ihre
Dateien in den benutzereigenen Bundlecache. Eine separate .NET-Installation oder der
Floppy-Projektordner wird zum Benutzen nicht benötigt. Zielsystem: Windows x64.

Alternativ das vollständige `Floppy-1.4.3-win-x64.zip` entpacken und dessen
`Floppy.exe` mit sämtlichen Begleitdateien behalten. Spielinstallation und Einrichtung
des jeweiligen Spielmoduls erfolgen auf jedem PC separat.

Die Spielsuche erkennt das tatsächliche Spiel auch unterhalb seines Steam-Ordners,
überspringt unlesbare Bibliotheken und verwechselt Launcher nicht mit der Spiel-EXE.
Manuell ausgewählte Ordner werden zum ausgewählten Spiel geprüft. Der Administrator-
Neustart verwendet den tatsächlichen Pfad der laufenden EXE, auch nach Umbenennen.

## Unrailed! 2

Die aktuelle NativeAOT-Spielversion stellt ihre ByteBool-Schalter im Webdebugger als
leere JSON-Objekte dar. Schreibaufrufe werden angenommen, verändern diese Werte aber
nicht. Floppy verwendet deshalb für die neun dauerhaften Schalter und die Zahl der
Boss-Abschnitte den vorhandenen Spieleingabe-Zustand. Die ursprüngliche Ursache wird
nicht durch bloßes Freischalten der Oberfläche verdeckt.

Der Zugang ist an die geprüfte Spiel-DLL gebunden
(`989a6d1c74ab31fd7fe9cae1b565b674ad086aa290d5890899d25b67bca2d114`)
und benötigt Windows 11 für die Prozesssynchronisation. Während kurzer Zugriffe prüft
Floppy Speicherbereinigung, Objekttypen, Rundenidentität und ausstehende Eingaben.
Ein Schreibversuch wird durch Rücklesen nach der Verarbeitung bestätigt. Bei einer
abweichenden Spielversion oder einem nicht bestätigten Zugriff wird ein Fehler gemeldet.
Nach einem Spielupdate können diese Schalter bis zur Adapterprüfung unverfügbar sein.
Die bisherigen HTTP-Zahlenfunktionen bleiben unabhängig davon nutzbar.

Der irreführende Schalter **Gleise ohne Material** wurde entfernt: Das Spielfeld
steuert eine einmalige Entwickler-Pfadbauaktion zum Mausziel, keinen dauerhaften
Schalter für kostenloses Bauen.

Die Oberfläche liest aktuelle Schalterwerte aus dem Spiel. Fehlgeschlagene Änderungen
werden auch beim Profilladen und Zurücksetzen als Fehler behandelt. Profile laden
Auswahlgeber vor abhängigen Feldern. Der Zahleneditor bietet nur tatsächliche Zahlen
an und verwirft Auswahlen bei Rundenwechseln. Gesicherte Zwischenstände werden anhand
ihres Dateinamens ausgewählt, sodass eine geänderte Liste kein anderes Ziel auswählt.

## Prüfung und Reproduktion

Im laufenden Sandbox-Spiel wurden alle neun verbliebenen Schalter einzeln ein- und
wieder ausgeschaltet, ihre übernommenen Eingabewerte zurückgelesen und die vorherigen
Werte wiederhergestellt. Auch die Boss-Abschnittszahl wurde geändert und zurückgesetzt.
Der Mutternbestand wurde separat mit einer einzelnen Addition von 0 auf 1 bestätigt.
Diese Zustandsprüfungen ersetzen keine vollständigen Durchläufe sämtlicher Spielmodi.
Fortschritt wurde für diese Prüfung nicht gelöscht oder zurückgesetzt.

Automatisiert bestanden die Modul-, Profil-, UI-, Speicher- und Installationsprüfungen,
darunter 18 Unrailed-Regressionsgruppen und 392 native Speicher-Fixture-Prüfungen.
Die Windows-Prozessfreigabe wurde zusätzlich an einem eigenen Testprozess bei normaler
Freigabe, Dispose und abruptem Ende des Besitzers geprüft. Ein Test zur Fokusübergabe
des Overlays wurde von Windows verhindert und ausdrücklich übersprungen.

`tools/test.ps1 -ArtifactsPath artifacts/tests` führt die automatisierten Prüfungen
aus. `tools/publish.ps1` baut beide vollständigen Varianten, führt EXE-Selbsttests,
Installationsprüfungen, den isolierten Ein-Datei-Start sowie eine frische ZIP-Entpackung
aus und erzeugt SHA256-Dateien. Bestehende Releaseziele werden nicht überschrieben.

Der Weitergabetest kopiert nur die EXE in einen temporären Pfad mit Leerzeichen und
Umlauten, benennt sie um und startet sie mit leerem Arbeitsordner, eigener
Laufzeitkonfiguration und frischem Bundlecache. Das ist ein lokaler Isolationstest,
kein Test an einem zweiten physischen PC. Paketprüfungen bestätigen keine ungetesteten
Spielmodi oder Online-Runden.
