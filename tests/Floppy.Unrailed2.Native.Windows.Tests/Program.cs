using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Floppy.Unrailed2;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    private static readonly Type Adapter = typeof(NativeCheats).GetNestedType("ProcessMemory", BindingFlags.NonPublic)!;

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--counter") return Counter(int.Parse(args[1]));
        if (args.Length > 0 && args[0] == "--holder") return Holder(int.Parse(args[1]), args[2]);
        if (!NativeCheats.CanWrite)
        {
            Console.WriteLine("SKIP Native.Windows: StateChange requires Windows 11 x64; no helper process started.");
            return 0;
        }

        Process child = null;
        Process holder = null;
        INativeCheatMemory memory = null;
        INativeCheatFreeze lease = null;
        string identity = Path.Combine(Path.GetTempPath(), "Floppy-SyntheticFreeze-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(identity, "Own synthetic test process only.");
            Console.WriteLine("Windows: " + Environment.OSVersion.Version + "; x64: " + Environment.Is64BitProcess);
            child = StartOwn("--counter", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            string hello = ReadLine(child);
            if (!hello.StartsWith("COUNTER ", StringComparison.Ordinal)) throw new Exception("Unexpected child response: " + hello);
            ulong address = ulong.Parse(hello[8..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            memory = CreateMemory(child.Id, identity);
            long initial = Read(memory, address);
            long running = WaitForAdvance(memory, address, initial);
            Console.WriteLine($"PASS own counter runs: {initial} -> {running}; child PID {child.Id}");

            CheckMissingSetInformation(child.Id, identity, address);

            if (!memory.TryFreeze(out lease, out string reason) || lease == null) throw new Exception("TryFreeze: " + reason);
            long paused = CheckStable(memory, address);
            if (!lease.Release(out reason)) throw new Exception("Release: " + reason);
            lease.Dispose();
            lease = null;
            long resumed = WaitForAdvance(memory, address, paused);
            Console.WriteLine($"PASS Release: counter remained {paused} for 200 ms, resumed to {resumed}");

            if (!memory.TryFreeze(out lease, out reason) || lease == null) throw new Exception("Second TryFreeze: " + reason);
            paused = CheckStable(memory, address);
            lease.Dispose();
            lease = null;
            resumed = WaitForAdvance(memory, address, paused);
            Console.WriteLine($"PASS Dispose: counter remained {paused} for 200 ms, resumed to {resumed}");

            // A separate owner tests the kernel cleanup guarantee when the lease is
            // never explicitly disposed. Both target and holder are our own children.
            holder = StartOwn("--holder", child.Id.ToString(CultureInfo.InvariantCulture), identity);
            string held = ReadLine(holder);
            if (held != "FROZEN") throw new Exception("Holder did not freeze: " + held);
            paused = CheckStable(memory, address);
            StopOwn(holder);
            holder.Dispose();
            holder = null;
            resumed = WaitForAdvance(memory, address, paused);
            Console.WriteLine($"PASS owner termination: counter remained {paused} for 200 ms, resumed to {resumed}");
            Console.WriteLine("PASS 4 native StateChange checks; no game discovery, game process, or remote write.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL " + error);
            return 1;
        }
        finally
        {
            try { lease?.Dispose(); }
            finally
            {
                try { if (holder != null) { StopOwn(holder); holder.Dispose(); } }
                finally
                {
                    try { (memory as IDisposable)?.Dispose(); }
                    finally
                    {
                        if (child != null) { StopOwn(child); child.Dispose(); }
                        File.Delete(identity);
                    }
                }
            }
        }
    }

    private static Process StartOwn(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new Exception("Could not start own helper.");
    }

    private static string ReadLine(Process process)
    {
        var line = process.StandardOutput.ReadLineAsync();
        if (!line.Wait(5000)) throw new Exception("Own helper did not become ready in 5 s.");
        return line.Result ?? throw new Exception("Own helper exited: " + process.StandardError.ReadToEnd());
    }

    private static INativeCheatMemory CreateMemory(int pid, string identity)
    {
        Process process = Process.GetProcessById(pid);
        SafeProcessHandle handle = null;
        try
        {
            handle = (SafeProcessHandle)Adapter.GetMethod("OpenProcess", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { 0x1010u, false, pid })!;
            if (handle.IsInvalid) throw new Exception("Own process read handle was rejected.");
            return (INativeCheatMemory)Activator.CreateInstance(Adapter,
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { process, handle, 0UL, identity }, CultureInfo.InvariantCulture)!;
        }
        catch { handle?.Dispose(); process.Dispose(); throw; }
    }

    private static long Read(INativeCheatMemory memory, ulong address)
    {
        var bytes = new byte[8];
        if (!memory.Read(address, bytes, out string reason)) throw new Exception("Counter read: " + reason);
        return BitConverter.ToInt64(bytes);
    }

    private static void OverrideWriteHandle(INativeCheatMemory memory, int pid, uint access)
    {
        var handle = (SafeProcessHandle)Adapter.GetMethod("OpenProcess", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { access, false, pid })!;
        if (handle.IsInvalid) { handle.Dispose(); throw new Exception("Test access mask was rejected."); }
        Adapter.GetField("_writeHandle", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(memory, handle);
    }

    private static void CheckMissingSetInformation(int pid, string identity, ulong address)
    {
        var restricted = CreateMemory(pid, identity);
        INativeCheatFreeze lease = null;
        try
        {
            // The old production mask lacked exactly PROCESS_SET_INFORMATION
            // (0x200), which NtCreateProcessStateChange requires on Windows 11.
            OverrideWriteHandle(restricted, pid, 0x1838);
            if (restricted.TryFreeze(out lease, out string reason) || lease != null)
                throw new Exception("Missing PROCESS_SET_INFORMATION unexpectedly allowed a freeze.");
            if (!reason.Contains("0xC0000022", StringComparison.Ordinal))
                throw new Exception("Missing access did not report STATUS_ACCESS_DENIED: " + reason);
            long before = Read(restricted, address);
            long after = WaitForAdvance(restricted, address, before);
            Console.WriteLine($"PASS missing 0x200: STATUS_ACCESS_DENIED, counter continues {before} -> {after}");
        }
        finally { lease?.Dispose(); (restricted as IDisposable)?.Dispose(); }
    }

    private static long WaitForAdvance(INativeCheatMemory memory, ulong address, long baseline)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            long value = Read(memory, address);
            if (value > baseline) return value;
            Thread.Sleep(5);
        } while (timer.ElapsedMilliseconds < 2000);
        throw new Exception("Own counter did not advance after release; baseline " + baseline);
    }

    private static long CheckStable(INativeCheatMemory memory, ulong address)
    {
        long baseline = Read(memory, address);
        for (int sample = 0; sample < 8; sample++)
        {
            Thread.Sleep(25);
            long value = Read(memory, address);
            if (value != baseline) throw new Exception($"Counter advanced while frozen: {baseline} -> {value}");
        }
        return baseline;
    }

    private static void StopOwn(Process process)
    {
        if (!process.HasExited) process.Kill();
        if (!process.WaitForExit(5000)) throw new Exception("Own helper could not be terminated.");
    }

    private static unsafe int Counter(int parentPid)
    {
        long* counter = (long*)Marshal.AllocHGlobal(8);
        *counter = 0;
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            Console.WriteLine("COUNTER " + ((ulong)counter).ToString("X", CultureInfo.InvariantCulture));
            Console.Out.Flush();
            var limit = Stopwatch.StartNew();
            while (limit.ElapsedMilliseconds < 25000 && !parent.HasExited)
            {
                Interlocked.Increment(ref *counter);
                Thread.Sleep(3);
            }
            return 0;
        }
        finally { Marshal.FreeHGlobal((IntPtr)counter); }
    }

    private static int Holder(int targetPid, string identity)
    {
        INativeCheatMemory memory = CreateMemory(targetPid, identity);
        INativeCheatFreeze lease = null;
        try
        {
            if (!memory.TryFreeze(out lease, out string reason) || lease == null)
            { Console.WriteLine("FAIL " + reason); return 1; }
            Console.WriteLine("FROZEN");
            Console.Out.Flush();
            Thread.Sleep(15000);
            return 0;
        }
        finally { lease?.Dispose(); (memory as IDisposable)?.Dispose(); }
    }
}
