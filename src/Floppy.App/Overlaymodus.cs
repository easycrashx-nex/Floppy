using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Floppy.App;

/// <summary>Der Client legt sich selbst über das Spiel.
///
/// Bei Unity und Godot zeichnet Floppy sein Menü im Spiel. Bei Mortal Shell II geht das
/// nicht: Dort läuft nichts von uns im Spiel, wir lesen und schreiben nur von außen.
/// Also legen wir stattdessen das Fenster darüber, das die Oberfläche ohnehin schon hat.
///
/// Der Vorteil gegenüber einem nachgebauten Menü: Es ist nicht "sieht aus wie der
/// Client", es *ist* der Client. Nichts kann auseinanderlaufen.
///
/// Die Grenze ist der Vollbildmodus: Über echtes Exklusiv-Vollbild legt sich kein
/// Fenster. Im randlosen Fenstermodus - heute fast überall die Voreinstellung -
/// funktioniert es.</summary>
public partial class MainWindow
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x4653;      // "FS"
    private const uint VK_F1 = 0x70;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr fenster, int id, uint zusatz, uint taste);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr fenster, int id);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr fenster);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr fenster, out RECT rechteck);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Links, Oben, Rechts, Unten; }

    private HwndSource? _quelle;
    private bool _tasteAngemeldet;

    private bool _imOverlay;
    private WindowState _zustandVorher;
    private double _obenVorher, _linksVorher, _breiteVorher, _hoeheVorher;

    /// <summary>Meldet die Taste an oder ab - je nachdem, ob dieses Spiel ein eigenes
    /// Menü im Spiel hat.
    ///
    /// Für Unity und Godot bleibt F1 dem Menü im Spiel vorbehalten, sonst würden beide
    /// auf denselben Tastendruck reagieren. Nur wo es kein Menü im Spiel gibt, greift
    /// der Client selbst nach der Taste.</summary>
    private void TasteNachfuehren()
    {
        bool gebraucht = Floppy.Unreal.Host.Laeuft || Floppy.Unrailed2.Host.Laeuft;

        if (gebraucht == _tasteAngemeldet) return;

        var griff = new WindowInteropHelper(this).Handle;
        if (griff == IntPtr.Zero) return;

        if (gebraucht)
        {
            _tasteAngemeldet = RegisterHotKey(griff, HOTKEY_ID, 0x4000, VK_F1);
            if (!_tasteAngemeldet) SetStatus("F1 ist von einer anderen Anwendung belegt", true, transient: true);
        }
        else
        {
            UnregisterHotKey(griff, HOTKEY_ID);
            _tasteAngemeldet = false;
        }
    }

    private void OverlayVorbereiten()
    {
        _quelle = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _quelle?.AddHook(FensterNachricht);
    }

    private IntPtr FensterNachricht(IntPtr fenster, int nachricht, IntPtr wparam,
                                    IntPtr lparam, ref bool behandelt)
    {
        if (nachricht != WM_HOTKEY) return IntPtr.Zero;
        if (_shortcutIds.TryGetValue(wparam.ToInt32(), out string? option))
        { _ = ExecuteShortcut(option); behandelt = true; return IntPtr.Zero; }
        if (wparam.ToInt32() != HOTKEY_ID) return IntPtr.Zero;

        OverlayUmschalten();
        behandelt = true;
        return IntPtr.Zero;
    }

    /// <summary>F1: über das Spiel legen, nochmal F1: wieder wegräumen.</summary>
    private void OverlayUmschalten()
    {
        if (_imOverlay) { OverlayVerlassen(); return; }

        IntPtr spiel = SpielFenster();
        if (spiel == IntPtr.Zero) return;

        _zustandVorher = WindowState;
        _obenVorher = Top;
        _linksVorher = Left;
        _breiteVorher = Width;
        _hoeheVorher = Height;

        // Mittig über das Spielfenster, mit etwas Luft am Rand
        if (GetWindowRect(spiel, out RECT r))
        {
            // Win32 liefert Pixel, WPF erwartet geräteunabhängige Einheiten.
            var transform = _quelle?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var obenLinks = transform.Transform(new Point(r.Links, r.Oben));
            var untenRechts = transform.Transform(new Point(r.Rechts, r.Unten));
            double breite = Math.Min(Width, untenRechts.X - obenLinks.X - 80);
            double hoehe = Math.Min(Height, untenRechts.Y - obenLinks.Y - 80);

            WindowState = WindowState.Normal;
            Width = Math.Max(MinWidth, breite);
            Height = Math.Max(MinHeight, hoehe);
            Left = obenLinks.X + ((untenRechts.X - obenLinks.X) - Width) / 2;
            Top = obenLinks.Y + ((untenRechts.Y - obenLinks.Y) - Height) / 2;
        }

        Topmost = true;
        Show();
        Activate();

        _imOverlay = true;
        SetStatus("Über dem Spiel - F1 legt es wieder weg", warn: false);
    }

    private void OverlayVerlassen()
    {
        Topmost = false;
        WindowState = _zustandVorher;

        if (_zustandVorher == WindowState.Normal)
        {
            Top = _obenVorher;
            Left = _linksVorher;
            Width = _breiteVorher;
            Height = _hoeheVorher;
        }

        _imOverlay = false;

        // Den Fokus zurückgeben, sonst tippt man ins Leere statt ins Spiel
        IntPtr spiel = SpielFenster();
        if (spiel != IntPtr.Zero) SetForegroundWindow(spiel);
    }

    /// <summary>Das Fenster des Spiels, über das wir uns legen sollen.</summary>
    private static IntPtr SpielFenster()
    {
        // Beide Spiele ohne eigenes Menü im Spiel. Es läuft immer höchstens eins
        // davon mit Floppy, weil sich beide denselben Dienst teilen.
        foreach (string name in new[] { Floppy.Unreal.Spiel.Prozess, Floppy.Unrailed2.Host.Prozessname })
            foreach (var prozess in Process.GetProcessesByName(name))
                if (prozess.MainWindowHandle != IntPtr.Zero)
                    return prozess.MainWindowHandle;

        return IntPtr.Zero;
    }
}
