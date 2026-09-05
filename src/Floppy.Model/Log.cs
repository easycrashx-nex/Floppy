using System;

namespace Floppy.Core
{
    /// <summary>Protokoll ohne Bindung an eine bestimmte Ladeumgebung.
    ///
    /// Der engine-freie Teil von Floppy soll nichts von BepInEx wissen - er muss sich
    /// sonst für Mono und IL2CPP zweimal übersetzen lassen, was nicht geht. Deshalb
    /// meldet sich das jeweilige Plugin beim Start hier an und leitet weiter.</summary>
    public static class Log
    {
        public static Action<string> OnInfo;
        public static Action<string> OnWarning;
        public static Action<string> OnError;

        public static void Info(string message)
        {
            var ziel = OnInfo;
            if (ziel != null) ziel(message);
        }

        public static void Warning(string message)
        {
            var ziel = OnWarning;
            if (ziel != null) ziel(message);
        }

        public static void Error(string message)
        {
            var ziel = OnError;
            if (ziel != null) ziel(message);
        }
    }
}
