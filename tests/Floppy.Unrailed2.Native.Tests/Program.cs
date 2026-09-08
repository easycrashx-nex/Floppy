using Floppy.Unrailed2;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string reason)
    { assertions++; if (!condition) throw new Exception(reason); }

    private static int Main()
    {
        try
        {
            foreach (int screen in new[] { 0, 1, 2 })
            {
                var memory = new FakeMemory(screen);
                Check(new NativeCheatLayout(memory).TryRead(out var values, out _), "known screen must resolve");
                Check(values.Length == 56 && values[5] == 255 && BitConverter.ToInt32(values, 52) == 2,
                    "exact inline struct including last Int32 must be copied");
                values[5] = 0;
                Check(memory.Bytes[FakeMemory.Builder + 0x25] == 255, "snapshot must be detached from memory");
            }
            foreach (ulong obj in new[] { FakeMemory.Main, FakeMemory.Manager, FakeMemory.Context,
                FakeMemory.Screen, FakeMemory.Creator, FakeMemory.Builder })
            {
                var memory = new FakeMemory(); memory.Pointer(obj, memory.ModuleBase + 0x1234);
                Check(!new NativeCheatLayout(memory).TryRead(out var values, out _) && values.Length == 0,
                    "unknown method table must reject the complete snapshot");
            }
            foreach (byte ready in new byte[] { 0, 2, 255 })
            {
                var memory = new FakeMemory(); memory.Bytes[FakeMemory.Builder + 0x1C] = ready;
                Check(!new NativeCheatLayout(memory).TryRead(out _, out _), "only cheatsRead=1 is ready");
            }
            {
                var memory = new FakeMemory(); memory.Bytes[FakeMemory.Builder + 0x1B] = 1;
                Check(!new NativeCheatLayout(memory).TryRead(out _, out _), "pending input is not a confirmed snapshot");
            }
            foreach (bool throwError in new[] { false, true })
            {
                var memory = new FakeMemory { FailRead = true, ThrowRead = throwError };
                Check(!new NativeCheatLayout(memory).TryRead(out var values, out var reason)
                    && values.Length == 0 && reason.Length > 0, "read failures must not escape or publish defaults");
            }
            {
                var memory = new FakeMemory();
                memory.AfterSnapshot = () => memory.Pointer(memory.ModuleBase + 0x512BEF0, 0x90000);
                Check(!new NativeCheatLayout(memory).TryRead(out _, out _), "root change during snapshot must fail");
            }
            {
                var memory = new FakeMemory();
                memory.AfterSnapshot = () => memory.Bytes[FakeMemory.Builder + 0x1B] = 1;
                Check(!new NativeCheatLayout(memory).TryRead(out _, out _), "new dirty state during snapshot must fail");
            }
            WriterTests();
            Check(!NativeCheats.TrySet(0, new byte[] { 255 }, out _),
                "old overload without identity must reject without opening a real process");
            NativeCheats.Reset();
            Console.WriteLine($"PASS: {assertions} native-layout assertions; fake memory only, no process/game access.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void WriterTests()
    {
        (FakeMemory memory, NativeCheatLayout layout, ulong id) Ready(int screen = 0)
        {
            var memory = new FakeMemory(screen);
            var layout = new NativeCheatLayout(memory, memory.Wait);
            Check(layout.TryRead(out _, out ulong id, out string reason), "initial snapshot: " + reason);
            return (memory, layout, id);
        }

        foreach (int screen in new[] { 0, 1, 2 })
        {
            var (m, layout, id) = Ready(screen);
            Check(layout.TrySet(2, new byte[] { 255 }, id, out string reason), "write+consume: " + reason);
            Check(m.Writes.Count == 2 && m.Writes[0].address == FakeMemory.Builder + 0x22
                && m.Writes[0].value.SequenceEqual(new byte[] { 255 })
                && m.Writes[1].address == FakeMemory.Builder + 0x1B, "only target byte then dirty may be written");
            Check(m.Consumed == 1 && m.Waits == 2 && !m.Frozen && m.Freezes == m.Releases,
                "consumption must be stable twice; every freeze released before wait");
            Check(m.Bytes[FakeMemory.Builder + 0x25] == 255, "unrelated flags remain unchanged");
        }
        foreach (int amount in new[] { 0, 30 })
        {
            var (m, layout, id) = Ready();
            Check(layout.TrySet(52, BitConverter.GetBytes(amount), id, out _), "allowed Int32 boundary");
            Check(m.Writes[0].value.Length == 4 && m.Writes[0].address == FakeMemory.Builder + 0x54,
                "numeric write must affect exactly the final four bytes");
        }
        foreach (int flag in new[] { 2, 3, 4, 5, 8, 9, 16, 17, 49 })
            foreach (byte value in new byte[] { 0, 255 })
                Check(NativeCheatLayout.ValidWrite(flag, new[] { value }), "all agreed flags accept only ByteBool encoding");
        foreach (var item in new (int offset, byte[] bytes)[] {
            (-1, new byte[]{0}), (0,new byte[]{0}), (0,new byte[]{255}), (1,new byte[]{0}), (6,new byte[]{0}), (7,new byte[]{0}),
            (18,new byte[]{0}), (48,new byte[]{0}), (50,new byte[]{0}), (56,new byte[]{0}),
            (2,new byte[]{1}), (2,new byte[]{0,0}), (52,new byte[]{0}),
            (52,BitConverter.GetBytes(-1)), (52,BitConverter.GetBytes(31)), (0,null) })
        {
            var (m, layout, id) = Ready();
            Check(!layout.TrySet(item.offset, item.bytes, id, out _) && m.Freezes == 0 && m.Writes.Count == 0,
                "unknown offset, width, or value must fail before freeze");
        }
        {
            var (m, layout, id) = Ready();
            Check(!layout.TrySet(2, new byte[] { 255 }, id + 1, out _) && m.Freezes == 0,
                "stale snapshot identity cannot freeze/write");
            Check(layout.TryRead(out _, out ulong same, out _) && same == id, "unchanged roots retain identity");
            m.Pointer(FakeMemory.Creator + 0x18, 0xB0000);
            Check(layout.TryRead(out _, out ulong next, out _) && next != id, "world replacement changes identity");
        }
        foreach (ulong slot in new[] { FakeMemory.Context + 0x48, FakeMemory.Creator + 0x30,
            FakeMemory.Creator + 0x10, FakeMemory.Creator + 0x18 })
        {
            var (m, layout, id) = Ready();
            m.OnFreeze = () => m.Pointer(slot, 0xB0000);
            Check(!layout.TrySet(2, new byte[] { 255 }, id, out _) && m.Writes.Count == 0 && !m.Frozen,
                "a target replaced after UI read must not receive the old click");
        }
        foreach (ulong rva in new ulong[] { 0x5207A80, 0x520718C, 0x520637C, 0x513D3F4 })
        {
            var (m, layout, id) = Ready();
            m.Int32(m.ModuleBase + rva, 1);
            Check(!layout.TrySet(2, new byte[] { 255 }, id, out _) && m.Writes.Count == 0
                && m.Freezes == 1 && m.Releases == 1 && !m.Frozen, "active/invalid GC must release without writing");
        }
        foreach (Action<FakeMemory> corrupt in new Action<FakeMemory>[] {
            m => m.Pointer(m.ModuleBase + 0x52019B0,0),
            m => m.Pointer(0xD0000,m.ModuleBase + 0x1234),
            m => m.Pointer(m.ModuleBase + 0x52019D8,0),
            m => m.Bytes[m.ModuleBase + 0x52019E0] = 3,
            m => m.Pointer(m.ModuleBase + 0x52019E0 + 0xC0,m.ModuleBase + 0x513D3F8),
            m => m.Bytes.Remove(m.ModuleBase + 0x5207A80),
            m => m.Bytes[FakeMemory.Builder + 0x1B] = 1,
            m => m.Bytes[FakeMemory.Builder + 0x1C] = 0 })
        {
            var (m, layout, id) = Ready(); corrupt(m);
            Check(!layout.TrySet(2,new byte[]{255},id,out _) && m.Writes.Count == 0 && !m.Frozen,
                "uninitialized DAC/GC or pending input cannot write");
        }
        foreach (bool throws in new[] { false, true })
        {
            var (m, layout, id) = Ready(); m.FailFreeze = true; m.ThrowFreeze = throws;
            Check(!layout.TrySet(2,new byte[]{255},id,out _) && m.Writes.Count == 0 && !m.Frozen,
                "failed/throwing freeze never writes");
        }
        foreach (int failedWrite in new[] { 1, 2 })
            foreach (bool throws in new[] { false, true })
            {
                var (m, layout, id) = Ready();
                m.FailWrite = n => n == failedWrite; m.ThrowWrite = throws;
                Check(!layout.TrySet(2,new byte[]{255},id,out _) && !m.Frozen && m.Releases == 1,
                    "failed/throwing field or marker write always releases");
                Check(m.Bytes[FakeMemory.Builder + 0x22] == 0 && m.Bytes[FakeMemory.Builder + 0x1B] == 0,
                    "including partial writes: exact field and original dirty marker must be restored");
            }
        {
            var (m, layout, id) = Ready();
            m.FailReadOnceAfterWrite = true;
            Check(!layout.TrySet(2,new byte[]{255},id,out _) && m.Writes.Count == 4
                && m.Bytes[FakeMemory.Builder + 0x22] == 0 && !m.Frozen,
                "failed pre-resume readback must restore both writes");
        }
        {
            var (m, layout, id) = Ready();
            m.FailWrite = n => n >= 2;
            Check(!layout.TrySet(2,new byte[]{255},id,out string reason)
                && reason.Contains("Rücknahme") && !m.Frozen, "rollback failure must report uncertainty and still release");
        }
        foreach (bool throws in new[] { false, true })
        {
            var (m, layout, id) = Ready(); m.FailRelease = true; m.ThrowRelease = throws;
            Check(!layout.TrySet(2,new byte[]{255},id,out _) && m.DisposeCalls >= 1 && !m.Frozen,
                "release failure must not report success; Dispose cleanup must still run");
        }
        {
            var (m, layout, id) = Ready(); m.ConsumeOnRelease = false;
            Check(!layout.TrySet(2,new byte[]{255},id,out string reason)
                && reason.Contains("nicht bestätigt") && m.Writes.Count == 2 && m.Waits == 12 && !m.Frozen,
                "pending dirty is bounded and never retried as a write");
        }
        {
            var (m, layout, id) = Ready(); m.ConsumeOnRelease = false;
            m.OnWait = n => { if (n == 3) m.Consume(); };
            Check(layout.TrySet(2,new byte[]{255},id,out _) && m.Writes.Count == 2 && m.Waits == 4,
                "delayed consumption confirms with reads only");
        }
        {
            var (m, layout, id) = Ready();
            m.OnFirstRelease = () => m.Pointer(FakeMemory.Creator + 0x10, 0xB0000);
            Check(!layout.TrySet(2,new byte[]{255},id,out _) && m.Writes.Count == 2 && !m.Frozen,
                "replacement world after publish cannot confirm old action");
        }
    }

    private sealed class FakeMemory : INativeCheatMemory
    {
        internal const ulong Main = 0x20000, Manager = 0x30000, Context = 0x40000,
            Screen = 0x50000, Creator = 0x60000, Builder = 0x70000;
        public ulong ModuleBase => 0x180000000;
        internal readonly Dictionary<ulong, byte> Bytes = new();
        internal bool FailRead, ThrowRead;
        internal Action AfterSnapshot;
        internal bool Frozen, FailFreeze, ThrowFreeze, ThrowWrite, FailRelease, ThrowRelease;
        internal bool FailReadOnceAfterWrite, ConsumeOnRelease = true;
        internal int Freezes, Releases, DisposeCalls, Waits, Consumed;
        internal Action OnFreeze, OnFirstRelease;
        internal Action<int> OnWait;
        internal Func<int, bool> FailWrite;
        internal List<(ulong address, byte[] value)> Writes = new();

        internal FakeMemory(int screen = 0)
        {
            Pointer(ModuleBase + 0x512BEF0, 0x10000);
            Pointer(0x10008, Main); Pointer(Main, ModuleBase + 0x3187A58);
            Pointer(Main + 0x28, Manager); Pointer(Manager, ModuleBase + 0x314BC48);
            Pointer(Manager + 8, Context); Pointer(Context, ModuleBase + 0x313D338);
            Pointer(Context + 0x48, Screen);
            Pointer(Screen, ModuleBase + (screen == 0 ? 0x3152188UL : screen == 1 ? 0x3152B80UL : 0x3152E78UL));
            Pointer(Screen + (screen == 2 ? 0x48UL : 0x40UL), Creator);
            Pointer(Creator, ModuleBase + (screen == 0 ? 0x3152060UL : 0x314D170UL));
            Pointer(Creator + (screen == 0 ? 0x30UL : 0x48UL), Builder);
            Pointer(Builder, ModuleBase + 0x3178F38);
            Pointer(Creator + 0x10, 0x80000); Pointer(Creator + 0x18, 0x90000); Pointer(Creator + 0x50, 0);
            Bytes[Builder + 0x1B] = 0; Bytes[Builder + 0x1C] = 1;
            for (ulong i = 0; i < 56; i++) Bytes[Builder + 0x20 + i] = 0;
            Bytes[Builder + 0x25] = 255; Bytes[Builder + 0x20 + 52] = 2;
            foreach (ulong rva in new ulong[] { 0x5207A80, 0x520718C, 0x520637C, 0x513D3F4 }) Int32(ModuleBase + rva, 0);
            Pointer(ModuleBase + 0x52019B0, 0xD0000); Pointer(0xD0000, ModuleBase + 0x4D1C020);
            Pointer(ModuleBase + 0x52019D8, ModuleBase + 0x52019E0);
            Bytes[ModuleBase + 0x52019E0] = 2; Bytes[ModuleBase + 0x52019E0 + 1] = 1;
            Pointer(ModuleBase + 0x52019E0 + 0xC0, ModuleBase + 0x513D3F4);
        }

        internal void Int32(ulong address, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++) Bytes[address + (ulong)i] = bytes[i];
        }

        internal void Pointer(ulong address, ulong value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++) Bytes[address + (ulong)i] = bytes[i];
        }

        public bool Read(ulong address, byte[] buffer, out string reason)
        {
            reason = "simulated incomplete read";
            if (ThrowRead) throw new IOException("simulated read exception");
            if (FailRead) return false;
            if (FailReadOnceAfterWrite && Writes.Count == 2)
            { FailReadOnceAfterWrite = false; return false; }
            if (address == ModuleBase + 0x5207A80) Check(Frozen, "GC guard is read only under freeze");
            for (int i = 0; i < buffer.Length; i++)
                if (Bytes.TryGetValue(address + (ulong)i, out byte b)) buffer[i] = b;
                else return false;
            if (address == Builder + 0x20) AfterSnapshot?.Invoke();
            reason = "";
            return true;
        }

        public bool Write(ulong address, byte[] buffer, out string reason)
        {
            Check(Frozen, "every write including rollback must occur under freeze");
            Writes.Add((address, (byte[])buffer.Clone()));
            // Deliberately mutate before failing: OS failures can leave a written prefix.
            for (int i = 0; i < buffer.Length; i++) Bytes[address + (ulong)i] = buffer[i];
            reason = "simulated write failure";
            if (FailWrite?.Invoke(Writes.Count) == true)
            { if (ThrowWrite) throw new IOException(reason); return false; }
            reason = "";
            return true;
        }

        public bool TryFreeze(out INativeCheatFreeze freeze, out string reason)
        {
            freeze = null; reason = "simulated freeze failure"; Freezes++;
            Check(!Frozen, "no nested freeze");
            if (ThrowFreeze) throw new IOException(reason);
            if (FailFreeze) return false;
            Frozen = true; freeze = new Lease(this); OnFreeze?.Invoke(); reason = ""; return true;
        }

        internal void Consume()
        {
            Check(!Frozen, "game cannot consume input while frozen");
            if (Bytes[Builder + 0x1B] != 0) { Bytes[Builder + 0x1B] = 0; Consumed++; }
        }

        internal void Wait(int milliseconds)
        { Check(!Frozen, "never wait while frozen"); Waits++; OnWait?.Invoke(Waits); }

        private sealed class Lease : INativeCheatFreeze
        {
            private readonly FakeMemory m;
            private bool done;
            internal Lease(FakeMemory memory) { m = memory; }
            private void Close()
            {
                if (done) return;
                done = true; m.Frozen = false; m.Releases++;
                if (m.ConsumeOnRelease) m.Consume();
                if (m.Releases == 1) m.OnFirstRelease?.Invoke();
            }
            public bool Release(out string reason)
            {
                reason = "simulated release failure";
                if (m.ThrowRelease) throw new IOException(reason);
                Close(); return !m.FailRelease;
            }
            public void Dispose() { m.DisposeCalls++; Close(); }
        }
    }
}
