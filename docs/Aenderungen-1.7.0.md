# Floppy 1.7.0

## Minecraft Dungeons II: Währungen und zusätzliche Funktionen

- **Währungen:** Smaragde und Springstone mit eigener Mengeneingabe geben. Die
  Gutschrift wird bis zum vom Spiel gemeldeten Maximum begrenzt; negative,
  gebrochene und übergroße Mengen werden abgewiesen. „Alles aus“ entfernt keine
  geschenkte Währung. Die Spielanzeige kann bis zum nächsten normalen
  Währungsereignis verzögert sein.
- **58 Regler** für maximales Leben, Heilung, Lebensraub, eingehenden Schaden,
  Rüstung, Nahkampf, Fernkampf, kritische Treffer, Elementarschaden, Bewegung,
  Artefakte, Seelen, Beute, Erfahrungsausbeute, Begleiter und Effektdauer.
- **Heiltrank-, Rollen- und Seelenvorrat:** Bestand anzeigen, einmal auffüllen
  oder regelmäßig auf Maximum halten. Auffüllen setzt ausschließlich den
  Ressourcenbestand; es entfernt keine bereits laufenden Cooldown-Effekte.
- **Übersicht:** Lebenspunkte, Level, Erfahrung, nächste Levelgrenze und Smaragde.
  Geladene Munition wird als Prozentwert unter Kampf angezeigt. Die separaten
  Regler für Nachladezeit und Nachladeverzögerung beschleunigen das normale
  Nachladen. Ein Reservewert wird nicht als geladene Munition ausgegeben.
- Die bestehende Lebensauffüllung und „Leben halten“ bleiben verfügbar. Die
  Abfrage alle 100 ms garantiert keinen Schutz gegen tödliche Treffer.

Regler beginnen beim Normalwert. Dieser Wert oder **Alles aus** setzt die
temporäre Anpassung zurück. Floppy merkt sich Objektidentität einschließlich
Seriennummer sowie den ursprünglichen Wert; fremde Figuren, Vorlagen und
wiederverwendete Objekte erhalten keine alten Rücksetzungen. Änderungen durch
Ausrüstungswechsel oder das Spiel werden beim Rücksetzen berücksichtigt.
Beim Trennen wird das Rücksetzen versucht. Ist die ursprüngliche Figur nicht
mehr lesbar, wird das im Log gemeldet; ein erfolgreiches Rücksetzen wird dann
nicht behauptet.

Erfahrungsregler gelten für neu verdiente XP. Es gibt keinen direkten
Levelsetzer. Schattenform- und Begleiterregler verstärken vorhandene Fähigkeiten;
sie erzeugen keine fehlende Fähigkeit und keinen neuen Begleiter.

## Unterstützte Fassung und Prüfung

Weiterhin unterstützt: Steam-Build **25041023**, EXE-SHA-256
`7c83afbf0ad34a40b853cdb25a22fffb605d08e2a1e2d431974d7c7c1ee0ba54`.
Andere EXE-Fassungen bleiben gesperrt, bis ihr Adapter geprüft wurde.

115 Prüfungen im eigenen nativen Testprozess prüfen Besitz, Objektidentität,
ungültige Werte, Währungsgutschriften, Ressourcen, Bewegungswerte,
nicht kumulierende Regler, Ausrüstungswechsel und Rücksetzen. Sie greifen
standardmäßig auf kein Spiel zu.

Im laufenden Spiel wurden alle **58 Regler** mit kleinen Werten geschrieben,
anschließend auf den Normalwert gesetzt und unverändert zurückgelesen. Dieser
Test bestätigt die aufgelösten Werte und das Zurücksetzen; er bestätigt nicht
jeden Kampfeffekt, jede Beutechance oder Mehrspielerfunktion. **Smaragde geben**
wurde mit genau einem Smaragd geprüft: Bestand 28 → 29, über 20 Sekunden stabil.
Nach einem normalen Smaragdfund bestätigte der Spieler 33; eine spätere lesende
Prüfung zeigte 53. Speichern und erneutes Laden wurden nicht erzwungen.
Springstone und sämtliche Ressourcen wurden noch nicht einzeln im Spiel
verbraucht und erneut aufgefüllt.

Das echte IPC-Schema meldete die aktive Spielfigur und die neuen Funktionen als
verfügbar. Alle 17 Regression-Suites und drei Python-Fixtures bestanden; die
abschließende Dungeons-Prüfung enthielt 115 Checks. Windows verweigerte dem
Overlay-Fixture den Vordergrundzugriff, deshalb wurde dessen Fokus-/Hotkey-Teil
ausgelassen. Der Symboltest ließ die optionalen PDB-Fixtures aus; die gemeinsame
Symbolauflösung wurde in dieser Version nicht geändert.

Die vollständigen Regressionstests und die Paketprüfung gehören zum Releasebau.
Die Portable-EXE und das vollständige ZIP enthalten die .NET-Laufzeit,
Spielmodule, Loader und Bilder. Die Veröffentlichung enthält SHA-256-Dateien;
die auf GitHub hochgeladenen Dateien werden mit den lokalen Prüfsummen verglichen.

```powershell
dotnet run --project tests/Floppy.Dungeons2.Tests -c Release -p:BuildGameModules=false
```

Zusatzparameter sind bewusste Spielprüfungen: `--probe-game` liest den Status;
`--probe-host` prüft das echte IPC-Schema. `--probe-features` schreibt und setzt
die Regler zurück; `--probe-emerald` gibt genau einen Smaragd. Diese schreibenden
Prüfungen laufen niemals als Teil der normalen Regressionstests.
