# Floppy 1.4.0 – Gegenstände erkennen und finden

Die Gegenstandsauswahl von Mortal Shell II zeigt einen Katalog anstelle der bisherigen
einfachen Auswahlliste. Kategorien mit Anzahl und eine eigene Suche nach Itemname,
ursprünglicher Kennung oder Kategorie erleichtern das Finden. Interne Namen werden
lesbar getrennt; die ursprüngliche Kennung bleibt als Hinweis beim Eintrag verfügbar.

Der geprüfte Bestand umfasst 77 Gegenstände und 58 echte, direkt aus den zugehörigen
Spieldefinitionen zugeordnete Itembilder. Links neben jedem Namen steht das verfügbare
Itembild. Für Gegenstände ohne zugeordnetes
Bild erscheint ein Kategoriesymbol; der Hinweis am Symbol kennzeichnet diesen Ersatz.
Die Bilddateien liegen lokal unter `Assets/MortalShell2`; beim Spielen werden keine
Bilder aus dem Internet nachgeladen.
Die lokale Extraktion ist unter [tools/item-assets](../tools/item-assets/README.md) dokumentiert.

Die Anzeige unter der Liste nennt immer den Gegenstand, den **Ins Inventar legen**
verwenden wird – auch wenn er durch einen Filter gerade nicht in der Liste erscheint.
Kategorienwechsel und Suche lösen keine Spielbefehle aus. Erst die Auswahl eines Eintrags
überträgt dessen ursprünglichen Index; Anzahl und Hinzufügen bleiben eigene Aktionen.

Desktop und externes Overlay verwenden denselben Katalog. Andere Auswahllisten behalten
ihre bisherige Darstellung. Unbekannte Gegenstände nach einem Spielupdate bleiben unter
**Sonstiges** erreichbar. Fehlende oder beschädigte Bilddateien verhindern die Auswahl nicht.

## Überprüfung

Die isolierten Tests prüfen insbesondere die unveränderte Identität nach Filtern und
Sortieren, stille Aktualisierung vom Spiel, leere Suchergebnisse, fehlende Bilder und
die Bedienbarkeit bei schmaler Fensterbreite. Die Paketprüfung lädt zusätzlich die
ausgelieferte Gegenstandsauswahl und dekodiert sämtliche im Katalog referenzierten Bilder.
Die Tests führen keine Inventar- oder Spielstandsänderungen im echten Spiel aus.

## Starten

Das vollständige neue ZIP in einen eigenen Ordner entpacken. Die bisherige Floppy-App
schließen und `Floppy.exe` aus dem neuen Ordner starten. `Assets`, `runtime` und die
übrigen Dateien im Ordner behalten. Wenn Mortal Shell II mit Administratorrechten läuft,
auch diese Floppy-Version über **Werkzeuge → Floppy als Administrator neu starten** öffnen.
