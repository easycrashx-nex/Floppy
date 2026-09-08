# Floppy 1.4.1 – Unrailed-2-Abfragen korrigiert

Der Unrailed-2-Adapter hat die Typ-Hashes aus der Namenstabelle als laufende
Komponentennummern verwendet. Das Spiel behandelt einen solchen ungültigen Filter als
ungefilterte Weltabfrage. Floppy fand darin anschließend die gesuchte Komponente nicht.
Abfragen verwenden jetzt die Nummer aus `componentMap.id`; Namen und Schreibbefehle
verwenden den Typ-Hash aus `componentMap.name`, entsprechend der Original-Weboberfläche.

Die Verfügbarkeit der Optionen liest einen gemeinsamen Zustand pro Aktualisierung.
Das wiederholte Zeichnen und Abfragen der Oberfläche löst keine zusätzlichen
Komponentenabfragen mehr aus. Infotexte werden aktualisiert; Weltwechsel verwerfen
gemerkte Nummern auch dann, wenn der Spielmodus gleich bleibt. Verschwindende Felder
und Bereiche verlieren ihre Auswahl, damit ein späterer Klick keinen Ersatzwert ändert.

Einzelne Schreibbefehle bestätigen die angegebene Entität. Bei Änderungen an vielen
Entitäten erfolgt eine gemeinsame Rückleseprüfung; eine andere Entität mit bereits
passendem Wert zählt nicht als Erfolg. Unbekannte Komponenten lösen keine ungefilterte
Weltabfrage aus.

## Grenze der aktuellen Spielversion

Mit Steam-Build 24389330 wurde eine laufende Sandbox-Runde direkt ausgelesen. Die Runde
wurde erkannt und die Abfragen des korrigierten Adapters dauerten bei dieser Messung
173 ms. Der Spielserver registriert `CheatSingleton` als Komponente 46, meldet aber
`count: 0`; auch `listEntities?cTypes=46` liefert keine Instanz. Die Entwicklerschalter
`EnableCheats` und `EnableWebDebug` sind dabei bereits aktiviert.

Die Zug-, Bau-, Muttern- und Automatikaktionen benötigen diese Instanz. Sie bleiben
deshalb gesperrt, solange das Spiel sie nicht bereitstellt. Die neue Statusmeldung
unterscheidet diesen Fall vom Hauptmenü und einer fehlenden Verbindung. Diese Version
behebt den nachgewiesenen Adapterfehler, stellt aber noch keine vollständige Reparatur
dieser Aktionen für den genannten Spielbuild dar. Über die dokumentierten Routen der
mitgelieferten Weboberfläche konnte keine Aktivierung der fehlenden Instanz belegt werden.

## Prüfung und Start

Die Regressionstests verwenden ausschließlich einen Fake-Spielserver. Sie prüfen
Zuordnung, Weltwechsel, Verbindungsabbruch, Schreibbestätigung, Auswahlverlust und die
Zahl der Abfragen. Die weiteren Model-, App-, Host- und UI-Prüfungen sowie Releasebuild,
Paketprüfung und Selbsttest werden für das ausgelieferte Paket ausgeführt.
Im echten Spiel wurden ausschließlich Status und Metadaten gelesen; kein Spielwert
und kein Spielstand wurden verändert. Die Wirksamkeit von Spielaktionen ist damit
nicht bestätigt.

Die alte Floppy-App schließen und `Floppy.exe` aus dem vollständig entpackten
1.4.1-Ordner starten. `Assets`, `runtime` und die übrigen Dateien neben der EXE behalten.
Für diesen externen Adapter ist kein Mod-Austausch im Spielordner nötig.
