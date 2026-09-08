using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Floppy.Unrailed2
{
    /// <summary>Buildgebundener Zugang zum Spieleingabe-Zustand, ohne Code im Spiel auszuführen.</summary>
    internal static class NativeCheats
    {
        internal const string ExpectedSha256 = "989a6d1c74ab31fd7fe9cae1b565b674ad086aa290d5890899d25b67bca2d114";
        internal const string WriteUnavailable = "Native Änderungen benötigen Windows 11 und einen aktuellen Spielzustand";
        private static readonly object Gate = new object();
        private static ProcessMemory _memory;
        private static NativeCheatLayout _layout;
        private static long _nextConnectAttempt;
        private static string _connectFailure = "";

        public static bool CanWrite => Environment.Is64BitProcess && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

        public static bool TryRead(out byte[] values, out string reason)
            => TryRead(out values, out _, out reason);

        public static bool TryRead(out byte[] values, out ulong identity, out string reason)
        {
            lock (Gate)
            {
                values = Array.Empty<byte>();
                identity = 0;
                try
                {
                    if (_memory == null)
                    {
                        if (Environment.TickCount64 < _nextConnectAttempt)
                        { reason = _connectFailure; return false; }
                        if (!ProcessMemory.TryOpen(out _memory, out reason))
                        {
                            _connectFailure = reason;
                            _nextConnectAttempt = Environment.TickCount64 + 2000;
                            return false;
                        }
                        _nextConnectAttempt = 0;
                        _connectFailure = "";
                    }
                    if (!_memory.Current(out reason)) { Reset(); return false; }
                    // Menu/loading states invalidate heap paths, not the verified
                    // process handle. Resolve every path afresh without rehashing
                    // the complete DLL on each unavailable update.
                    _layout ??= new NativeCheatLayout(_memory);
                    return _layout.TryRead(out values, out identity, out reason);
                }
                catch (Exception error)
                {
                    Reset();
                    reason = "Nativer Spielzustand nicht lesbar: " + error.Message;
                    return false;
                }
            }
        }

        public static bool TrySet(int offset, byte[] value, out string reason)
        {
            reason = "Änderung ohne aktuelle Zielidentität abgewiesen";
            return false;
        }

        public static bool TrySet(int offset, byte[] value, ulong expectedIdentity, out string reason)
        {
            lock (Gate)
            {
                reason = WriteUnavailable;
                if (!CanWrite || _memory == null || _layout == null) return false;
                try
                {
                    if (!_memory.Current(out reason)) { Reset(); return false; }
                    return _layout.TrySet(offset, value, expectedIdentity, out reason);
                }
                catch (Exception error) { Reset(); reason = "Native Änderung fehlgeschlagen: " + error.Message; return false; }
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                _memory?.Dispose(); _memory = null; _layout = null;
                _nextConnectAttempt = 0; _connectFailure = "";
            }
        }

        private sealed class ProcessMemory : INativeCheatMemory, IDisposable
        {
            private readonly Process _process;
            private readonly SafeProcessHandle _handle;
            private SafeProcessHandle _writeHandle;
            private bool _frozen;
            private readonly string _path;
            private readonly long _length;
            private readonly DateTime _modified;
            public ulong ModuleBase { get; }

            private ProcessMemory(Process process, SafeProcessHandle handle, ulong moduleBase, string path)
            {
                _process = process; _handle = handle; ModuleBase = moduleBase; _path = path;
                var file = new FileInfo(path);
                _length = file.Length; _modified = file.LastWriteTimeUtc;
            }

            internal static bool TryOpen(out ProcessMemory memory, out string reason)
            {
                memory = null;
                reason = "Unrailed 2 läuft nicht";
                Process[] processes = Array.Empty<Process>();
                Process selected = null;
                SafeProcessHandle handle = null;
                try
                {
                    if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                    { reason = "Nativer Spielzustand benötigt Windows x64"; return false; }
                    processes = Process.GetProcessesByName("Unrailed2");
                    if (processes.Length != 1)
                    { if (processes.Length > 1) reason = "Mehrere Unrailed-2-Prozesse; kein eindeutiges Ziel"; return false; }
                    selected = processes[0];
                    string executable = selected.MainModule?.FileName;
                    if (string.IsNullOrEmpty(executable))
                    { reason = "Spielpfad nicht lesbar"; return false; }
                    string expectedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable),
                        "data_UnrailedGodot_windows_x86_64", "UnrailedGodot.dll"));
                    var modules = selected.Modules.Cast<ProcessModule>()
                        .Where(m => string.Equals(m.ModuleName, "UnrailedGodot.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (modules.Length != 1 || !string.Equals(Path.GetFullPath(modules[0].FileName), expectedPath,
                        StringComparison.OrdinalIgnoreCase))
                    { reason = "Erwartetes Spielmodul nicht eindeutig geladen"; return false; }
                    using (var file = File.Open(expectedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        string hash = Convert.ToHexString(SHA256.HashData(file));
                        if (!hash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                        { reason = "Diese Unrailed-2-Version wird vom nativen Zugang noch nicht unterstützt"; return false; }
                    }
                    // Availability reads need no write or freeze permission.
                    handle = OpenProcess(0x1010, false, selected.Id);
                    if (handle.IsInvalid) { reason = AccessError(Marshal.GetLastWin32Error()); return false; }
                    memory = new ProcessMemory(selected, handle, (ulong)modules[0].BaseAddress.ToInt64(), expectedPath);
                    handle = null;
                    reason = "";
                    return true;
                }
                catch (Win32Exception error) { reason = AccessError(error.NativeErrorCode); return false; }
                catch (Exception error) { reason = "Nativer Spielzugang nicht verfügbar: " + error.Message; return false; }
                finally
                {
                    handle?.Dispose();
                    foreach (var process in processes)
                        if (memory == null || !ReferenceEquals(process, selected)) process.Dispose();
                }
            }

            internal bool Current(out string reason)
            {
                reason = "Spiel oder Spielmodul hat sich geändert";
                if (_process.HasExited || _handle.IsInvalid || _handle.IsClosed) return false;
                var file = new FileInfo(_path);
                if (!file.Exists || file.Length != _length || file.LastWriteTimeUtc != _modified) return false;
                reason = "";
                return true;
            }

            public bool Read(ulong address, byte[] buffer, out string reason)
            {
                if (ReadProcessMemory(_handle, (IntPtr)(long)address, buffer, (UIntPtr)buffer.Length, out var read)
                    && read.ToUInt64() == (ulong)buffer.Length)
                { reason = ""; return true; }
                reason = "Spielzustand konnte nicht vollständig gelesen werden: " + AccessError(Marshal.GetLastWin32Error());
                return false;
            }

            public bool Write(ulong address, byte[] bytes, out string reason)
            {
                reason = "Schreibzugriff außerhalb des bestätigten Freeze abgewiesen";
                if (!_frozen || _writeHandle == null) return false;
                if (WriteProcessMemory(_writeHandle, (IntPtr)(long)address, bytes, (UIntPtr)bytes.Length, out var written)
                    && written.ToUInt64() == (ulong)bytes.Length)
                { reason = ""; return true; }
                reason = "Spielzustand nicht vollständig geschrieben: " + AccessError(Marshal.GetLastWin32Error());
                return false;
            }

            public bool TryFreeze(out INativeCheatFreeze freeze, out string reason)
            {
                freeze = null;
                reason = WriteUnavailable;
                if (!CanWrite || _frozen) return false;
                StateChangeHandle state = null;
                try
                {
                    if (_writeHandle == null || _writeHandle.IsInvalid)
                    {
                        _writeHandle?.Dispose();
                        // QUERY_LIMITED_INFORMATION | SET_INFORMATION (create state object)
                        // | SUSPEND_RESUME (apply freeze) | VM_READ | VM_WRITE | VM_OPERATION.
                        _writeHandle = OpenProcess(0x1A38, false, _process.Id);
                        if (_writeHandle.IsInvalid) { reason = AccessError(Marshal.GetLastWin32Error()); return false; }
                    }
                    int status = NtCreateProcessStateChange(out state, 1, IntPtr.Zero, _writeHandle, 0);
                    if (status < 0 || state == null || state.IsInvalid)
                    { reason = "ProcessStateChange nicht verfügbar (NTSTATUS 0x" + status.ToString("X8") + ")"; return false; }
                    status = NtChangeProcessState(state, _writeHandle, 0, IntPtr.Zero, UIntPtr.Zero, 0);
                    if (status < 0)
                    { reason = "Spiel konnte nicht eingefroren werden (NTSTATUS 0x" + status.ToString("X8") + ")"; return false; }
                    _frozen = true;
                    freeze = new FreezeLease(this, state);
                    state = null;
                    reason = "";
                    return true;
                }
                catch (Exception error) { reason = "Freeze nicht verfügbar: " + error.Message; return false; }
                finally { if (freeze == null) _frozen = false; state?.Dispose(); }
            }

            // Windows 11 StateChange records the freeze in a kernel handle. Closing
            // the sole handle reverses it, including when Floppy exits unexpectedly.
            // There is deliberately no NtSuspendProcess or thread-suspend fallback.
            private sealed class FreezeLease : INativeCheatFreeze
            {
                private readonly ProcessMemory _owner;
                private readonly StateChangeHandle _state;
                internal FreezeLease(ProcessMemory owner, StateChangeHandle state) { _owner = owner; _state = state; }
                public bool Release(out string reason)
                {
                    try { _state.Dispose(); }
                    finally { _owner._frozen = false; }
                    reason = _state.ReleaseSucceeded ? "" : "Freeze-Handle konnte nicht geschlossen werden";
                    return _state.ReleaseSucceeded;
                }
                public void Dispose() => Release(out _);
            }

            private sealed class StateChangeHandle : SafeHandleZeroOrMinusOneIsInvalid
            {
                public StateChangeHandle() : base(true) { }
                internal bool ReleaseSucceeded { get; private set; }
                protected override bool ReleaseHandle() => ReleaseSucceeded = CloseHandle(handle);
            }

            public void Dispose() { _writeHandle?.Dispose(); _handle.Dispose(); _process.Dispose(); }
            private static string AccessError(int code) => code == 5
                ? "Zugriff verweigert (Win32 5). Spiel und Floppy mit denselben Rechten starten; Spiel bevorzugt normal über Steam"
                : new Win32Exception(code).Message + " (Win32 " + code + ")";

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address,
                [Out] byte[] buffer, UIntPtr size, out UIntPtr read);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool WriteProcessMemory(SafeProcessHandle process, IntPtr address,
                byte[] buffer, UIntPtr size, out UIntPtr written);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CloseHandle(IntPtr handle);
            [DllImport("ntdll.dll", ExactSpelling = true)]
            private static extern int NtCreateProcessStateChange(out StateChangeHandle state, uint access,
                IntPtr attributes, SafeProcessHandle process, uint reserved);
            [DllImport("ntdll.dll", ExactSpelling = true)]
            private static extern int NtChangeProcessState(StateChangeHandle state, SafeProcessHandle process,
                int change, IntPtr information, UIntPtr length, uint reserved);
        }
    }

    internal interface INativeCheatMemory
    {
        ulong ModuleBase { get; }
        bool Read(ulong address, byte[] buffer, out string reason);
        bool Write(ulong address, byte[] buffer, out string reason);
        bool TryFreeze(out INativeCheatFreeze freeze, out string reason);
    }

    internal interface INativeCheatFreeze : IDisposable
    {
        bool Release(out string reason);
    }

    /// <summary>Reine Layoutlogik: dieselben gelesenen Wurzeln vor und nach dem Snapshot.</summary>
    internal sealed class NativeCheatLayout
    {
        private readonly INativeCheatMemory _memory;
        private readonly Action<int> _wait;
        private ulong[] _lastPath;
        private ulong _identity;
        private static long _nextIdentity;
        internal NativeCheatLayout(INativeCheatMemory memory, Action<int> wait = null)
        { _memory = memory; _wait = wait ?? Thread.Sleep; }

        internal bool TryRead(out byte[] values, out string reason)
            => TryRead(out values, out _, out reason);

        internal bool TryRead(out byte[] values, out ulong identity, out string reason)
        {
            identity = 0;
            if (!Snapshot(out var path, out values, out reason)) { Forget(); return false; }
            if (_lastPath == null || !_lastPath.SequenceEqual(path))
                _identity = unchecked((ulong)Interlocked.Increment(ref _nextIdentity));
            _lastPath = path;
            identity = _identity;
            return true;
        }

        private void Forget() { _lastPath = null; _identity = 0; }

        private bool Snapshot(out ulong[] path, out byte[] values, out string reason)
        {
            path = null;
            values = Array.Empty<byte>();
            try
            {
                if (!Resolve(out var before, out reason)) return false;
                var data = new byte[56];
                if (!_memory.Read(before[6] + 0x20, data, out reason)) return false;
                if (!Resolve(out var after, out reason)) return false;
                if (!before.SequenceEqual(after)) { reason = "Spieleingabe hat während des Lesens gewechselt"; return false; }
                var again = new byte[56];
                if (!_memory.Read(after[6] + 0x20, again, out reason)) return false;
                if (!data.SequenceEqual(again)) { reason = "Spieleingabe hat sich während des Lesens geändert"; return false; }
                path = before;
                values = data;
                reason = "";
                return true;
            }
            catch (Exception error) { reason = "Spieleingabe nicht lesbar: " + error.Message; return false; }
        }

        internal bool TrySet(int offset, byte[] value, ulong expectedIdentity, out string reason)
        {
            reason = "Unbekanntes Feld oder ungültiger Wert";
            if (!ValidWrite(offset, value)) return false;
            reason = "Spielziel hat sich geändert; bitte den aktuellen Zustand abwarten";
            if (expectedIdentity == 0 || expectedIdentity != _identity || _lastPath == null) return false;
            var expected = (ulong[])_lastPath.Clone();
            var requested = (byte[])value.Clone();
            bool posted = false;
            INativeCheatFreeze freeze = null;
            try
            {
                if (!_memory.TryFreeze(out freeze, out reason) || freeze == null) return false;
                posted = Publish(expected, offset, requested, out reason);
            }
            catch (Exception error) { reason = "Native Änderung fehlgeschlagen: " + error.Message; }
            finally
            {
                if (!Release(freeze, out string releaseError)) { reason = releaseError; posted = false; }
            }
            if (!posted) { Forget(); return false; }

            // A single write was published. Only reads are retried; every read lease
            // closes before waiting so the game can consume its own input protocol.
            int confirmed = 0;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                _wait(40);
                freeze = null;
                bool same = false;
                bool matched = false;
                bool changed = false;
                try
                {
                    if (_memory.TryFreeze(out freeze, out reason) && freeze != null && GcReady(out reason)
                        && Snapshot(out var path, out var current, out reason))
                    {
                        same = expected.SequenceEqual(path);
                        changed = !same;
                        matched = same && current.Skip(offset).Take(requested.Length).SequenceEqual(requested);
                        if (!same) reason = "Spielziel wechselte nach der Übergabe; Änderung nicht bestätigt";
                    }
                }
                catch (Exception error) { reason = "Bestätigung nicht lesbar: " + error.Message; }
                finally
                {
                    if (!Release(freeze, out string releaseError)) { reason = releaseError; matched = false; }
                }
                if (matched && ++confirmed == 2) { reason = "Eingabe vom Spiel übernommen"; return true; }
                if (!matched) confirmed = 0;
                if (changed) break;
            }
            Forget();
            reason = "Änderung übergeben, aber nicht bestätigt; vor erneutem Versuch Spielzustand prüfen. " + reason;
            return false;
        }

        internal static bool ValidWrite(int offset, byte[] value)
        {
            if (value == null) return false;
            if (offset == 52 && value.Length == 4)
            { int count = BitConverter.ToInt32(value, 0); return count >= 0 && count <= 30; }
            // Offset 0 is rebuilt by createCheatMessage every frame, so it cannot
            // be exposed as a persistent native toggle.
            return value.Length == 1 && (value[0] == 0 || value[0] == 255)
                && (offset == 2 || offset == 3 || offset == 4 || offset == 5
                    || offset == 8 || offset == 9 || offset == 16 || offset == 17 || offset == 49);
        }

        private bool Publish(ulong[] expected, int offset, byte[] requested, out string reason)
        {
            reason = "Spielziel hat sich geändert";
            if (!GcReady(out reason) || !Snapshot(out var path, out var values, out reason)) return false;
            if (!expected.SequenceEqual(path)) { reason = "Spielziel hat sich geändert"; return false; }
            ulong address = path[6] + 0x20 + (ulong)offset, dirty = path[6] + 0x1B;
            byte[] old = values.Skip(offset).Take(requested.Length).ToArray();
            bool attempted = false, published = false;
            try
            {
                attempted = true; // A failed OS call can still have written a prefix.
                if (!_memory.Write(address, requested, out reason)
                    || !_memory.Write(dirty, new byte[] { 1 }, out reason)) return false;
                var check = new byte[requested.Length];
                var marker = new byte[1];
                if (!_memory.Read(address, check, out reason) || !_memory.Read(dirty, marker, out reason)) return false;
                if (!check.SequenceEqual(requested) || marker[0] != 1)
                { reason = "Native Übergabe konnte nicht zurückgelesen werden"; return false; }
                published = true;
                return true;
            }
            catch (Exception error) { reason = "Native Übergabe fehlgeschlagen: " + error.Message; return false; }
            finally
            {
                if (attempted && !published)
                {
                    // Still frozen, still the same verified addresses. Restore only
                    // our field and the marker; never overwrite the complete struct.
                    bool restored = Restore(address, old) & Restore(dirty, new byte[] { 0 });
                    if (!restored) reason += "; Rücknahme nicht vollständig bestätigt, Spielzustand unklar";
                }
            }
        }

        private bool Restore(ulong address, byte[] value)
        {
            try
            {
                if (!_memory.Write(address, value, out _)) return false;
                var check = new byte[value.Length];
                return _memory.Read(address, check, out _) && check.SequenceEqual(value);
            }
            catch { return false; }
        }

        private static bool Release(INativeCheatFreeze freeze, out string reason)
        {
            reason = "";
            if (freeze == null) return true;
            bool released = false;
            try { released = freeze.Release(out reason); }
            catch (Exception error) { reason = "Freeze-Freigabe fehlgeschlagen: " + error.Message; }
            finally
            {
                try { freeze.Dispose(); }
                catch (Exception error) { reason = "Freeze-Cleanup fehlgeschlagen: " + error.Message; released = false; }
            }
            return released;
        }

        private bool GcReady(out string reason)
        {
            reason = "GC-Zustand nicht freigegeben";
            foreach (ulong rva in new ulong[] { 0x5207A80, 0x520718C, 0x520637C })
            {
                if (!Int32(_memory.ModuleBase + rva, out int value, out reason)) return false;
                if (value != 0) { reason = "Speicherbereinigung läuft; bitte erneut versuchen"; return false; }
            }
            if (!Pointer(_memory.ModuleBase + 0x52019B0, out ulong heap, out reason)
                || !Pointer(heap, out ulong vtable, out reason)) return false;
            if (vtable != _memory.ModuleBase + 0x4D1C020)
            { reason = "Unbekannte GC-Implementierung"; return false; }
            if (!Pointer(_memory.ModuleBase + 0x52019D8, out ulong dac, out reason)) return false;
            if (dac != _memory.ModuleBase + 0x52019E0)
            { reason = "GC-Diagnosedaten noch nicht initialisiert"; return false; }
            var version = new byte[2];
            if (!_memory.Read(dac, version, out reason)) return false;
            if (version[0] != 2 || version[1] != 1)
            { reason = "Unbekannte GC-Diagnoseversion"; return false; }
            if (!Pointer(dac + 0xC0, out ulong invalid, out reason, aligned: false)) return false;
            if (invalid != _memory.ModuleBase + 0x513D3F4)
            { reason = "Unbekannter GC-Strukturzähler"; return false; }
            if (!Int32(invalid, out int count, out reason)) return false;
            if (count != 0) { reason = "GC-Strukturen werden verändert; bitte erneut versuchen"; return false; }
            reason = "";
            return true;
        }

        private bool Int32(ulong address, out int value, out string reason)
        {
            value = 0;
            var bytes = new byte[4];
            if (!_memory.Read(address, bytes, out reason)) return false;
            value = BitConverter.ToInt32(bytes, 0);
            return true;
        }

        private bool Resolve(out ulong[] path, out string reason)
        {
            path = new ulong[9];
            if (!Pointer(_memory.ModuleBase + 0x512BEF0, out path[0], out reason)
                || !Object(path[0] + 8, 0x3187A58, out path[1], out reason)
                || !Object(path[1] + 0x28, 0x314BC48, out path[2], out reason)
                || !Object(path[2] + 8, 0x313D338, out path[3], out reason)
                || !Pointer(path[3] + 0x48, out path[4], out reason)
                || !Pointer(path[4], out var screenType, out reason)) return false;
            ulong creatorOffset, creatorType, builderOffset;
            if (screenType == _memory.ModuleBase + 0x3152188)
            { creatorOffset = 0x40; creatorType = 0x3152060; builderOffset = 0x30; }
            else if (screenType == _memory.ModuleBase + 0x3152B80)
            { creatorOffset = 0x40; creatorType = 0x314D170; builderOffset = 0x48; }
            else if (screenType == _memory.ModuleBase + 0x3152E78)
            { creatorOffset = 0x48; creatorType = 0x314D170; builderOffset = 0x48; }
            else { reason = "Aktueller Spielbildschirm wird nicht unterstützt"; return false; }
            if (!Object(path[4] + creatorOffset, creatorType, out path[5], out reason)
                || !Object(path[5] + builderOffset, 0x3178F38, out path[6], out reason)) return false;
            if (!Pointer(path[5] + 0x10, out path[7], out reason)) return false;
            if (creatorType == 0x3152060)
            {
                if (!Pointer(path[5] + 0x18, out path[8], out reason)) return false;
            }
            else
            {
                var parallel = new byte[8];
                if (!_memory.Read(path[5] + 0x50, parallel, out reason)) return false;
                if (BitConverter.ToUInt64(parallel, 0) != 0)
                { reason = "Parallelwelt nicht für direkte Eingaben freigegeben"; return false; }
            }
            var state = new byte[2];
            if (!_memory.Read(path[6] + 0x1B, state, out reason)) return false;
            if (state[1] != 1) { reason = "Spieleingabe ist noch nicht initialisiert"; return false; }
            if (state[0] != 0) { reason = "Spiel verarbeitet noch eine Eingabeänderung"; return false; }
            return true;
        }

        private bool Object(ulong address, ulong typeRva, out ulong pointer, out string reason)
        {
            if (!Pointer(address, out pointer, out reason) || !Pointer(pointer, out var type, out reason)) return false;
            if (type == _memory.ModuleBase + typeRva) return true;
            reason = "Spielobjekt hat einen unerwarteten Typ";
            return false;
        }

        private bool Pointer(ulong address, out ulong pointer, out string reason, bool aligned = true)
        {
            pointer = 0;
            var bytes = new byte[8];
            if (!_memory.Read(address, bytes, out reason)) return false;
            pointer = BitConverter.ToUInt64(bytes, 0);
            if (pointer >= 0x10000 && pointer < 0x0000800000000000 && (!aligned || (pointer & 7) == 0)) return true;
            reason = "Spielobjekt nicht verfügbar";
            return false;
        }
    }
}
