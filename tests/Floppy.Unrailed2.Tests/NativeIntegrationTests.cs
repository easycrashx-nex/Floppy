using Floppy.Core;
using Floppy.Core.Api;
using Floppy.Unrailed2;
using System.Reflection;
using System.Text.Json;

internal static class NativeIntegrationTests
{
    private static readonly (string Id, int Offset)[] Toggles =
    {
        ("zug.kaputt", 4), ("zug.brennt", 5), ("zug.haltan", 3),
        ("bau.physik", 2), ("bau.beacons", 9), ("auto.an", 8), ("auto.stopp", 17),
        ("auto.unten", 16), ("auto.mapgen", 49)
    };

    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static Unrailed2Module Start()
    {
        Debugger.Reset();
        Debugger.Responses["worldInfo"] = "{\"id\":0,\"config\":[0,\"Menu\"]}";
        Welt.Aktualisiere();
        Debugger.Responses["worldInfo"] = "{\"id\":10,\"config\":[0,\"Sandbox\"]}";
        Debugger.Responses["typeNames"] = "{}";
        Debugger.Responses["componentMap"] = "{\"46\":{\"id\":46,\"name\":\"-486817492\",\"count\":1}}";
        Debugger.Responses["listEntities?cTypes=46"] = "{\"entities\":[{\"eid\":0,\"components\":[{\"id\":46,\"fields\":[{\"name\":\"953417568\",\"value\":{}}]}]}]}";
        var module = new Unrailed2Module();
        Registry.SetModule(module); module.Initialize();
        Welt.Aktualisiere();
        NativeCheats.Values = new byte[56]; NativeCheats.Writable = true;
        Cheats.AktualisiereSnapshot();
        return module;
    }

    public static void Run()
    {
        try
        {
            Start();
            foreach (var field in Toggles)
            {
                var option = Registry.Find(field.Id);
                Check(option.Available && !option.BoolValue, field.Id + " must use native state despite unreadable ByteBool JSON");
                option.BoolValue = true; option.NotifyChanged();
                Check(!option.MessageIsError && NativeCheats.Writes.Last().Offset == field.Offset
                    && NativeCheats.Writes.Last().Value.SequenceEqual(new byte[] { 255 }), field.Id + " maps to its exact native byte");
            }
            var number = Registry.Find("auto.bosse");
            number.NumberValue = 7; number.NotifyChanged();
            Check(!number.MessageIsError && NativeCheats.Writes.Last().Offset == 52
                && BitConverter.ToInt32(NativeCheats.Values, 52) == 7, "numeric option uses the same input state");
            var reset = Registry.Find("u2.allesaus"); reset.Fire();
            Check(!reset.MessageIsError && Toggles.All(t => NativeCheats.Values[t.Offset] == 0
                && !Registry.Find(t.Id).BoolValue), "all-off clears actual native flags and refreshes displayed values");
            Check(BitConverter.ToInt32(NativeCheats.Values, 52) == 7, "all-off preserves the separate boss setting");

            NativeCheats.Values[4] = 255;
            Cheats.AktualisiereSnapshot();
            Check(Registry.Find("zug.kaputt").BoolValue, "changes from the game refresh UI without callbacks");
            int writes = NativeCheats.Writes.Count;
            NativeCheats.RejectWrite = true;
            reset.Fire();
            Check(reset.MessageIsError && Registry.Find("zug.kaputt").BoolValue && NativeCheats.Writes.Count == writes,
                "failed native reset retains the actual active state and reports failure");

            Start();
            NativeCheats.Identity++;
            var toggle = Registry.Find("zug.kaputt"); toggle.BoolValue = true; toggle.NotifyChanged();
            Check(toggle.MessageIsError && NativeCheats.Writes.Count == 0, "a stale native identity cannot be written");
            Start();
            Debugger.Responses["worldInfo"] = "{\"id\":11,\"config\":[0,\"Sandbox\"]}";
            toggle = Registry.Find("zug.kaputt"); toggle.BoolValue = true; toggle.NotifyChanged();
            Check(toggle.MessageIsError && NativeCheats.Writes.Count == 0, "same-mode world replacement rejects queued native changes");

            Start(); NativeCheats.Writable = false; Cheats.AktualisiereSnapshot();
            Check(!Registry.Find("zug.kaputt").Available, "read-only access cannot enable a toggle");
            NativeCheats.Values = null; Cheats.AktualisiereSnapshot();
            Check(!Registry.Find("zug.kaputt").Available && !Cheats.Verfuegbar,
                "an empty JSON object is never treated as a writable boolean");
            Check(!Debugger.Requests.Any(p => p.StartsWith("changeValue?", StringComparison.Ordinal)),
                "native writes never fall back to the JSON path that silently loses ByteBool values");

            HttpFallbackAndUnavailableReset();
            NativeProfileSync();
        }
        finally { Debugger.Reset(); Cheats.VergissSnapshot(); }
    }

    private static void HttpFallbackAndUnavailableReset()
    {
        Start();
        NativeCheats.Writable = false;
        BitConverter.GetBytes(21).CopyTo(NativeCheats.Values, 52);
        Debugger.Responses["listEntities?cTypes=46"] = HttpFields(3);
        Cheats.AktualisiereSnapshot();
        var number = Registry.Find("auto.bosse");
        Check(number.Available && number.NumberValue == 3 && !Registry.Find("zug.kaputt").Available,
            "read-only native access preserves writable HTTP numbers and displays their HTTP state");

        string expected = "changeValue?command=" + Debugger.Verpacke("0." + Cheats.Singleton + ".338793709=9");
        Debugger.OnRequest = path =>
        {
            if (!path.StartsWith("changeValue?", StringComparison.Ordinal)) return;
            Check(path == expected, "numeric fallback must retain the exact HTTP type, field and value");
            Debugger.Responses[path] = "";
            Debugger.Responses["listEntities?cTypes=46"] = HttpFields(9);
        };
        number.NumberValue = 9; number.NotifyChanged();
        Check(!number.MessageIsError && NativeCheats.Writes.Count == 0
            && Debugger.Requests.Count(p => p == expected) == 1,
            "a number uses one confirmed HTTP write when the native snapshot is read-only");
        Cheats.AktualisiereSnapshot();
        Check(number.NumberValue == 9, "polling must not replace the confirmed HTTP number with a stale native value");

        Start();
        NativeCheats.Values[4] = 255;
        Cheats.AktualisiereSnapshot();
        var reset = Registry.Find("u2.allesaus");
        Check(reset.Available, "reset starts available with a readable native input");
        NativeCheats.Values = null; // The fresh read in the reset now fails (e.g. input still busy).
        reset.Fire();
        Check(reset.MessageIsError && NativeCheats.Writes.Count == 0
            && !Debugger.Requests.Any(p => p.StartsWith("changeValue?", StringComparison.Ordinal)),
            "native state loss before all-off must fail without sending ineffective ByteBool HTTP commands");

        var toggle = Registry.Find("zug.kaputt");
        toggle.BoolValue = false; toggle.NotifyChanged();
        number = Registry.Find("auto.bosse");
        number.NumberValue = 9; number.NotifyChanged();
        Check(toggle.MessageIsError && number.MessageIsError
            && !Debugger.Requests.Any(p => p.StartsWith("changeValue?", StringComparison.Ordinal)),
            "direct callbacks also reject unavailable wrapper and missing numeric fields before any HTTP write");
    }

    private static string HttpFields(int number) => JsonSerializer.Serialize(new
    {
        entities = new[] { new { eid = 0, components = new[] { new { id = 46,
            fields = new object[] { new { name = "953417568", value = new { } },
                new { name = "338793709", value = number } } } } } }
    });

    private static void NativeProfileSync()
    {
        var storage = typeof(Profile).GetField("StorageRoot", BindingFlags.Static | BindingFlags.NonPublic);
        object previousRoot = storage.GetValue(null);
        string temporary = Path.Combine(Path.GetTempPath(), "Floppy.NativeProfile.Tests-" + Guid.NewGuid().ToString("N"));
        storage.SetValue(null, temporary);
        try
        {
            Start();
            Directory.CreateDirectory(Profile.Ordner);
            File.WriteAllText(Path.Combine(Profile.Ordner, "native.json"), JsonSerializer.Serialize(new
            {
                version = 1, spiel = "Unrailed2", werte = new object[]
                {
                    new { id = "zug.kaputt", @bool = true },
                    new { id = "zug.brennt", @bool = true },
                    new { id = "auto.bosse", number = 7 }
                }
            }));
            Registry.Find("profil.name").TextValue = "native";
            Registry.Find("profil.liste").Choices = new[] { "native" };
            var load = Registry.Find("profil.laden"); load.Fire();
            Check(!load.MessageIsError && NativeCheats.Values[4] == 255 && NativeCheats.Values[5] == 255
                && BitConverter.ToInt32(NativeCheats.Values, 52) == 7
                && Registry.Find("zug.kaputt").BoolValue && Registry.Find("zug.brennt").BoolValue
                && Registry.Find("auto.bosse").NumberValue == 7,
                "per-write native refresh must preserve later profile entries and leave UI at the applied state");

            int writes = NativeCheats.Writes.Count;
            NativeCheats.RejectWrite = true;
            var reset = Registry.Find("profil.zuruecksetzen"); reset.Fire();
            Check(reset.MessageIsError && NativeCheats.Writes.Count == writes
                && Registry.Find("zug.kaputt").BoolValue && Registry.Find("zug.brennt").BoolValue
                && Registry.Find("auto.bosse").NumberValue == 7,
                "failed native profile reset keeps the actual active values in the model");
        }
        finally
        {
            storage.SetValue(null, previousRoot);
            string resolved = Path.GetFullPath(temporary);
            string expected = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Floppy.NativeProfile.Tests-");
            if (resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }
}
