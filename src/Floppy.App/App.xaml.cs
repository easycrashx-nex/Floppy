using System.Windows;

using System;
using System.IO;

namespace Floppy.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--install-game")
        {
            if (e.Args.Length != 4) { Shutdown(2); return; }
            try
            {
                bool ok = Installer.Einrichten(e.Args[1], out string message, e.Args[2]);
                File.WriteAllText(e.Args[3], message);
                Shutdown(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(e.Args[3], ex.Message); } catch (Exception) { }
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--self-test")
        {
            int exit = SelfCheck.Run(e.Args.Length > 1 ? e.Args[1] : null);
            Shutdown(exit);
            return;
        }
        base.OnStartup(e);
    }
}
