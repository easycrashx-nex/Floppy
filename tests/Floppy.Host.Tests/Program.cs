extern alias Unrailed;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Floppy.Core;
using Floppy.Hosting;
using Floppy.Unreal;
using Einstellungen = Unrailed::Floppy.Unrailed2.Einstellungen;

// Ausschließlich synthetische Zustände: keine Spiele, keine Sockets, keine Nutzereinstellungen.
internal static class Program
{
    private static async Task Main()
    {
        await Prüfe("Spieltakt überlappt nicht; Shutdown bleibt begrenzt", HostShutdown);
        await Prüfe("Timeout behält laufende Puffer bis zum Endsignal", RemoteLebenszyklus);
        await Prüfe("Verweigerter Zugriff lässt Diagnoseaufträge weiterlaufen", InitialisierungFehlgeschlagen);
        await Prüfe("Mortal Shell meldet abgewiesene Aktionen und verwirft falsche UI-Werte", AktionenFehlgeschlagen);
        await Prüfe("Mortal Shell ergänzt Schema und wiederholt Laden", SchemaUndLaden);
        await Prüfe("Einstellungsfehler bewahren Original und Sicherung", EinstellungenSichern);
        Console.WriteLine("6/6 Host-Prüfungen erfolgreich.");
    }

    private static async Task Prüfe(string name, Func<Task> test)
    {
        await test();
        Console.WriteLine("PASS " + name);
    }

    private static void Wahr(bool bedingung, string nachricht)
    {
        if (!bedingung) throw new InvalidOperationException(nachricht);
    }

    private static async Task HostShutdown()
    {
        using var freigabe = new ManualResetEventSlim();
        var betreten = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int aufrufe = 0, aufgeräumt = 0;
        var fehler = new List<Exception>();
        using var takt = new HostTakt(() =>
        {
            Interlocked.Increment(ref aufrufe);
            betreten.TrySetResult(true);
            freigabe.Wait();
        }, () => Interlocked.Increment(ref aufgeräumt), ex => fehler.Add(ex), 1, 5);
        try
        {
            await betreten.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(60);
            Wahr(aufrufe == 1, "Ein langsamer Takt darf keinen zweiten starten");
            Wahr(!await takt.StoppeAsync(TimeSpan.FromMilliseconds(30)), "Laufende Arbeit muss als noch nicht beendet gemeldet werden");
            Wahr(aufgeräumt == 0, "Aufräumen vor Arbeitsende ist verboten");
        }
        finally { freigabe.Set(); }
        await takt.Beendet.WaitAsync(TimeSpan.FromSeconds(3));
        takt.Dispose();
        Wahr(await takt.StoppeAsync(TimeSpan.Zero), "Bereits beendeter Takt muss sofort bestätigen");
        Wahr(aufrufe == 1 && aufgeräumt == 1 && fehler.Count == 0, "Beenden darf nicht doppelt aufräumen oder neue Arbeit starten");
    }

    private static async Task RemoteLebenszyklus()
    {
        // Ein selbst angelegtes Windows-Ereignis steht für einen noch laufenden Faden.
        // Die Testpuffer liegen ausschließlich im eigenen Testprozess.
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset);
        Wahr(Native.DuplicateHandle(new IntPtr(-1), signal.SafeWaitHandle.DangerousGetHandle(),
            new IntPtr(-1), out var faden, 0, false, 2), "Test-Wartehandle fehlt");
        var griff = Native.OpenProcess(0x0400 | 0x0008, false, Environment.ProcessId);
        Wahr(griff != IntPtr.Zero, "Eigener Testprozess muss zugänglich sein");
        var puffer = Native.VirtualAllocEx(griff, IntPtr.Zero, 4096, 0x3000, 0x04);
        Wahr(puffer != IntPtr.Zero, "Testpuffer fehlt");
        var aufruf = new Aufruf(new Speicher(), null);
        SetzePrivat(aufruf, "_griff", griff);
        SetzePrivat(aufruf, "_faden", faden);
        SetzePrivat(aufruf, "_parameter", (ulong)puffer.ToInt64());
        try
        {
            Wahr(!aufruf.WarteAufAufruf(10), "Timeout darf keinen Erfolg liefern");
            Wahr(aufruf.LetzterFehler.Contains("Zeitüberschreitung"), "Timeout muss verständlich gemeldet werden");
            Wahr(aufruf.Rufe(1, 1, Array.Empty<byte>()) == null, "Während eines laufenden Aufrufs darf kein weiterer starten");
            Wahr(aufruf.LetzterFehler.Contains("läuft noch"), "Laufender Aufruf muss Wiederverwendung sperren");
            aufruf.Dispose();
            Wahr(Native.Belegt(puffer), "Dispose darf einen noch verwendeten Puffer nicht freigeben");
        }
        finally
        {
            aufruf.Dispose();
            signal.Set();
        }
        var ende = Stopwatch.StartNew();
        while (Native.Belegt(puffer) && ende.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(10);
        Wahr(!Native.Belegt(puffer), "Nach dem Endsignal müssen Testpuffer freigegeben werden");
        Wahr(!aufruf.Oeffne(), "Eine beendete Verbindung darf sich nicht neu öffnen");
    }

    private static void SetzePrivat(object ziel, string name, object wert) =>
        ziel.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ziel, wert);

    private static Task InitialisierungFehlgeschlagen()
    {
        var spiel = new FehlerSpiel();
        using var modul = new MortalShellModule(spiel);
        bool initialisiert = false;
        int bedient = 0;
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.Enqueue(() => bedient++);
            Host.Bediene(modul, ref initialisiert, () => false);
        }
        Wahr(bedient == 4, "Diagnoseaufträge dürfen bei fehlgeschlagener Initialisierung nicht verhungern");
        Wahr(initialisiert && spiel.Versuche <= 2, "Fehlerhafte Initialisierung darf nicht in jedem 200-ms-Takt wiederholt werden");
        Wahr(!modul.IsReady(out string status) && status.Contains("Zugriff verweigert") && status.Contains("5"),
            "Zugriffsfehler muss als konkrete Bereitschaftsmeldung sichtbar bleiben");

        spiel.Verweigert = false;
        modul.Aktualisiere();
        Wahr(!modul.IsReady(out status) && status == "Warte auf geladenes Spiel",
            "Nach behobenem Zugriff muss der alte Fehler einer ehrlichen Ladeanzeige weichen");
        Dispatcher.Enqueue(() => bedient++);
        Host.Bediene(modul, ref initialisiert, () => true);
        Wahr(bedient == 4, "Ein beendeter Host darf keine neue Arbeit beginnen");
        Dispatcher.Pump(); // Nur den synthetischen Auftrag für nachfolgende Tests entfernen.
        return Task.CompletedTask;
    }

    private static Task AktionenFehlgeschlagen()
    {
        using (var getrennt = new MortalShellModule())
        {
            var optionen = getrennt.BuildCategories().SelectMany(k => k.Options).ToList();
            var gold = optionen.Single(o => o.Id == "cheat.S_AddGold");
            Wahr(!gold.Available, "Ohne Verbindung dürfen Spielaktionen nicht verfügbar erscheinen");
            gold.Fire();
            Wahr(gold.MessageIsError && gold.Message.Contains("läuft nicht"), "Auch ein veralteter direkter Aufruf muss einen Fehler melden");
            Wahr(optionen.Single(o => o.Id == "info.neusuchen").Available,
                "Die Diagnose muss trotz nicht verfügbarem Spiel erreichbar bleiben");
        }

        // Die Fake-Figur liefert Metadaten, hat aber absichtlich keinen Speicherzugriff.
        // Damit muss der tatsächliche Schreibpfad scheitern statt die UI zu bestätigen.
        var spiel = new TestSpiel { FindetFigur = true };
        spiel.Suche();
        spiel.Speicherstand.Add(new Spiel.Zahl { Name = "Gold", Adresse = 0 });
        using var modul = new MortalShellModule(spiel);
        Registry.SetModule(modul);
        string antwort = (string)typeof(IpcServer).GetMethod("ApplyInvoke", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { "cheat.S_AddGold" });
        Wahr(antwort.Contains("\"ok\":false") && antwort.Contains("Keine Spielsteuerung"),
            "Fehlgeschlagene Entwickleraktion muss im echten IPC-Antwortpfad ok:false liefern");
        PruefeSetFehler("cheat.S_BlockExperience", "bool", true);
        Wahr(!Registry.Find("cheat.S_BlockExperience").BoolValue, "Abgewiesener Schalter muss seinen alten UI-Wert behalten");
        PruefeSetFehler("attr.TestSet.Health", "number", 123.0);
        Wahr(Registry.Find("attr.TestSet.Health").NumberValue == 0, "Fehlgeschlagener Attributwrite darf keinen neuen UI-Wert bestätigen");
        PruefeSetFehler("stand.Gold", "number", 123.0);
        Wahr(Registry.Find("stand.Gold").NumberValue == 0, "Fehlgeschlagener Speicherstandwrite darf keinen neuen UI-Wert bestätigen");
        var gesetzt = (HashSet<string>)typeof(MortalShellModule).GetField("_gesetzt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(modul);
        Wahr(gesetzt.Count == 0, "Abgewiesene Werte dürfen nicht für automatische Wiederherstellung vorgemerkt werden");
        gesetzt.Add("attr.TestSet.Health"); // Simuliert einen früher erfolgreichen Wert, dessen Ziel inzwischen fehlt.
        Wahr(modul.WendeWiederAn() == 0 && Registry.Find("attr.TestSet.Health").MessageIsError,
            "Ein fehlgeschlagener Wiederholungswrite darf nicht als Erfolg gezählt werden");
        return Task.CompletedTask;
    }

    private static void PruefeSetFehler(string id, string key, object value)
    {
        var request = new Dictionary<string, object> { ["id"] = id, [key] = value };
        bool fehlgeschlagen = false;
        try
        {
            typeof(IpcServer).GetMethod("ApplySet", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { request });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            fehlgeschlagen = true;
        }
        Wahr(fehlgeschlagen && Registry.Find(id).MessageIsError, id + " muss die Änderung mit einem Fehler ablehnen");
    }

    private static Task SchemaUndLaden()
    {
        var spiel = new TestSpiel();
        using var modul = new MortalShellModule(spiel);
        Registry.SetModule(modul);
        Wahr(Registry.Find("attr.TestSet.Health") == null, "Im Hauptmenü gibt es noch keine Attribute");
        modul.Aktualisiere();
        spiel.FindetFigur = true;
        modul.Aktualisiere();
        var gesundheit = Registry.Find("attr.TestSet.Health");
        Wahr(gesundheit != null, "Später gefundene Attribute müssen im bestehenden Schema erscheinen");
        gesundheit.NumberValue = 123f;
        // Dieser Test prüft Ladeübergänge. Einen zuvor bestätigten Wert darstellen;
        // Fehler des echten Schreibpfads werden separat in AktionenFehlgeschlagen geprüft.
        ((HashSet<string>)typeof(MortalShellModule).GetField("_gesetzt", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(modul)).Add(gesundheit.Id);
        ((HashSet<string>)typeof(MortalShellModule).GetField("_gesetzt", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(modul)).Add("attr.FrueheresSet.NichtMehrVorhanden");

        int angewandt = 0;
        gesundheit.OnChanged = _ => angewandt++;
        spiel.Kennung++;
        spiel.FindetFigur = false;
        modul.Aktualisiere();
        modul.Aktualisiere();
        Wahr(angewandt == 0, "Während des Ladens darf nichts wieder angewandt werden");
        spiel.FindetFigur = true;
        modul.Aktualisiere();
        modul.Aktualisiere();
        Wahr(angewandt == 1, "Erfolgreicher Wiederholungsversuch muss genau einmal wieder anwenden");

        spiel.Attribute.Clear();
        spiel.FindetFigur = false;
        modul.Aktualisiere();
        spiel.FindetFigur = true;
        modul.Aktualisiere();
        Wahr(angewandt == 2, "Auch eine neue Figur in derselben Welt muss ihre Werte wieder erhalten");

        var version = Registry.SchemaVersion;
        spiel.ZweitesAttribut = true;
        spiel.Kennung++;
        modul.Aktualisiere();
        Wahr(Registry.Find("attr.TestSet.Mana") != null && Registry.SchemaVersion > version,
            "Strukturänderungen benötigen neue Optionen und eine neue Schema-Version");
        Wahr(Registry.Find("attr.TestSet.Health").NumberValue == 123f, "Schema-Aktualisierung muss gesetzte Werte bewahren");
        return Task.CompletedTask;
    }

    private static Task EinstellungenSichern()
    {
        string ordner = Path.Combine(Path.GetTempPath(), "Floppy.Host.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordner);
        try
        {
            string datei = Path.Combine(ordner, "settings.txt");
            byte[] original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Volume 17\r\nName Grüße\r\nEnableCheats False\r\n")).ToArray();
            File.WriteAllBytes(datei, original);
            using (var sperre = new FileStream(datei, FileMode.Open, FileAccess.Write, FileShare.Write | FileShare.Delete))
            {
                bool fehlgeschlagen = false;
                try { Einstellungen.Setze(datei, true); }
                catch (IOException) { fehlgeschlagen = true; }
                Wahr(fehlgeschlagen, "Ein Lesefehler darf nicht als leere Datei behandelt werden");
            }
            Wahr(File.ReadAllBytes(datei).SequenceEqual(original), "Lesefehler darf Original nicht ändern");
            Wahr(!File.Exists(Einstellungen.Sicherung(datei)), "Bei Lesefehler darf keine leere Sicherung entstehen");

            Einstellungen.Setze(datei, true);
            Wahr(Einstellungen.Angeschaltet(datei), "Beide Entwicklerschalter müssen gesetzt sein");
            Wahr(File.ReadAllText(datei).Contains("Volume 17") && File.ReadAllText(datei).Contains("Name Grüße"),
                "Andere Einstellungen müssen erhalten bleiben");
            Wahr(File.ReadAllBytes(Einstellungen.Sicherung(datei)).SequenceEqual(original), "Sicherung muss bytegenau dem Original entsprechen");
            Einstellungen.Setze(datei, false);
            Wahr(!Einstellungen.Angeschaltet(datei), "Ausschalten muss die Schalter entfernen");
            Wahr(File.ReadAllBytes(Einstellungen.Sicherung(datei)).SequenceEqual(original), "Erneutes Einrichten darf Original-Sicherung nicht überschreiben");
            Einstellungen.StelleWiederHer(datei);
            Wahr(File.ReadAllBytes(datei).SequenceEqual(original), "Wiederherstellen muss die vollständige Originaldatei zurückbringen");
            Wahr(!Directory.EnumerateFiles(ordner, "*.tmp").Any(), "Temporäre Dateien müssen aufgeräumt sein");
        }
        finally
        {
            // Ein selbst erzeugter, bereits absoluter Testordner; keine Spiel-/Nutzerdaten.
            string absolut = Path.GetFullPath(ordner);
            Wahr(absolut.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                 Path.GetFileName(absolut).StartsWith("Floppy.Host.Tests-", StringComparison.Ordinal),
                 "Aufräumen ist nur im eigenen temporären Testordner erlaubt");
            Directory.Delete(ordner, true);
        }
        return Task.CompletedTask;
    }

    private sealed class TestSpiel : Spiel
    {
        public bool FindetFigur;
        public bool ZweitesAttribut;
        public ulong Kennung = 1;
        public override bool Verbunden => true;
        public override bool Bereit => Attribute.Count > 0;
        public override string Welt => "Testwelt";
        public override ulong WeltKennung => Kennung;
        public override bool Verbinde() => Suche();
        public override void Trenne() { Attribute.Clear(); }
        public override bool Suche()
        {
            Attribute.Clear();
            if (!FindetFigur) return false;
            Attribute.Add(new Attribut { Satz = "TestSet", Name = "Health" });
            if (ZweitesAttribut) Attribute.Add(new Attribut { Satz = "TestSet", Name = "Mana" });
            return true;
        }
    }

    private sealed class FehlerSpiel : Spiel
    {
        public bool Verweigert = true;
        public int Versuche;
        private bool _verbunden;
        public override bool Verbunden => _verbunden;
        public override bool Bereit => false;
        public override string LetzterFehler => Verweigert ? "Zugriff verweigert (Win32 5)" : "";
        public override bool Verbinde()
        {
            Versuche++;
            if (Verweigert) throw new System.ComponentModel.Win32Exception(5, "Zugriff verweigert (Win32 5)");
            _verbunden = true;
            return true;
        }
        public override void Trenne() { _verbunden = false; }
        public override bool Suche() => false;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, out IntPtr target, uint access, bool inherit, uint options);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size, uint type, uint protect);
        [DllImport("kernel32.dll")] private static extern UIntPtr VirtualQuery(IntPtr address, out SpeicherInfo info, UIntPtr size);
        [StructLayout(LayoutKind.Sequential)] private struct SpeicherInfo
        {
            public IntPtr Basis, Zuweisungsbasis;
            public uint Zuweisungsschutz;
            public UIntPtr Größe;
            public uint Zustand, Schutz, Typ;
        }
        internal static bool Belegt(IntPtr puffer)
        {
            Wahr(VirtualQuery(puffer, out var info, (UIntPtr)Marshal.SizeOf<SpeicherInfo>()) != UIntPtr.Zero,
                "Testpuffer-Zustand muss lesbar sein");
            return info.Zustand == 0x1000;
        }
    }
}
