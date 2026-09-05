using System;
using Floppy.Core;
using Floppy.Core.Api;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Die Rubrik "Mitspieler" - Streiche für die Runde mit Freunden.
    ///
    /// Getrennt vom Rest, weil hier andere Regeln gelten: Jeder Eintrag betrifft
    /// jemand anderen, jeder hat ein ausdrückliches Ziel, und keiner davon macht
    /// etwas Unwiderrufliches.</summary>
    public partial class HowToFishModule
    {
        /// <summary>Die Auswahlliste der Mitspieler - wird laufend nachgeführt,
        /// weil jemand dazukommen oder gehen kann.</summary>
        private static void AktualisiereMitspieler()
        {
            var wer = Registry.Find("wem.ziel");
            if (wer == null) return;

            var namen = Mitspieler.Namen();
            if (namen.Length == wer.Choices.Length) return;

            wer.Choices = namen;
            wer.ChoiceIndex = Mathf.Clamp(wer.ChoiceIndex, 0, Math.Max(0, namen.Length - 1));
        }

        private static int Ziel()
        {
            var wer = Registry.Find("wem.ziel");
            return wer?.ChoiceIndex ?? 0;
        }

        private static float Kraft()
        {
            var regler = Registry.Find("wem.kraft");
            return regler?.NumberValue ?? 20f;
        }

        private CheatCategory Streiche()
        {
            var kategorie = new CheatCategory("Mitspieler");

            kategorie.Add(new CheatOption
            {
                Id = "wem.info",
                Label = "In der Runde",
                Kind = OptionKind.Info,
                Scope = CheatScope.Everyone,
                OnChanged = o =>
                {
                    int andere = Mitspieler.Andere.Count;
                    o.TextValue = andere == 0
                        ? "nur du"
                        : andere + (andere == 1 ? " Mitspieler" : " Mitspieler")
                          + (Game.IsHost ? "   |   du hostest" : "   |   du bist Gast");
                }
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.ziel",
                Label = "Wen?",
                Description = "Gilt für alles darunter. \"Alle\" trifft jeden außer dir.",
                Kind = OptionKind.Choice,
                Scope = CheatScope.Everyone,
                Choices = new[] { "Alle" }
            });

            // ---------------------------------------------------------- Schubsen

            kategorie.Add(new CheatOption
            {
                Id = "wem.kraft",
                Label = "Wucht",
                Description = "Wie kräftig geschubst wird.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                Min = 5f, Max = 120f, Step = 5f,
                NumberValue = 25f
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.schubs",
                Label = "Wegschleudern",
                Description = "Fliegt von dir weg, ohne Schaden. Der Stoß geht überwiegend nach " +
                              "oben - waagerechte Wucht klemmt das Spiel im nächsten Physikschritt " +
                              "wieder auf Gehtempo zurück.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = HostOnly,
                OnInvoke = o => o.Message = Mitspieler.Schubse(Ziel(), Kraft(), nurHoch: false)
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.hoch",
                Label = "Senkrecht hochschleudern",
                Description = "Die zuverlässigste Variante - senkrechte Wucht übernimmt das Spiel " +
                              "voll, samt normaler Fallkurve.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = HostOnly,
                OnInvoke = o => o.Message = Mitspieler.Schubse(Ziel(), Kraft(), nurHoch: true)
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.falschertreffer",
                Label = "Falscher Treffer",
                Description = "Blut, Aufschrei, Trefferwirkung - und kein einziger Schadenspunkt. " +
                              "Er hört sich selbst schreien und sieht Blut, seine Lebensanzeige " +
                              "rührt sich nicht.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = HostOnly,
                OnInvoke = o => o.Message = Mitspieler.FalscherTreffer(Ziel(), 2)
            });

            // ---------------------------------------------------------- Umsetzen

            kategorie.Add(new CheatOption
            {
                Id = "wem.hierher",
                Label = "Zu mir holen",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o => o.Message = Mitspieler.ZuMirHolen(Ziel())
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.hoehe",
                Label = "In die Luft setzen",
                Description = "Zehn Meter über die eigene Position. Der Fall danach tut weh - " +
                              "am besten vorher jemanden ins Wasser stellen.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o => o.Message = Mitspieler.InDieLuft(Ziel(), 10f)
            });

            // ---------------------------------------------------------- Sachen

            kategorie.Add(new CheatOption
            {
                Id = "wem.fallenlassen",
                Label = "Alles fallen lassen",
                Description = "Was er in der Hand hat, landet vor seinen Füßen. Aufheben geht.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o => o.Message = Mitspieler.AllesFallenLassen(Ziel())
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.platz",
                Label = "Gürtelplatz umschalten",
                Description = "Wechselt, was er gerade in der Hand hält.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                Min = 0f, Max = 5f, Step = 1f,
                OnChanged = o => o.Message = Mitspieler.Platzwechsel(Ziel(), (int)o.NumberValue)
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.taschen",
                Label = "Alle Taschen aufmachen",
                Description = "Freundlicher Streich: schaltet ihm die Gürtelplätze frei.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o => o.Message = Mitspieler.Taschen(Ziel())
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.koeder",
                Label = "Köder umstellen",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                Min = 0f, Max = 8f, Step = 1f,
                OnChanged = o => o.Message = Mitspieler.KoederWechseln(Ziel(), (int)o.NumberValue)
            });

            // ---------------------------------------------------------- Zustand

            kategorie.Add(new CheatOption
            {
                Id = "wem.ducken",
                Label = "Ducken erzwingen",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                OnChanged = o => o.Message = Mitspieler.Ducken(Ziel(), o.BoolValue)
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.essen",
                Label = "Essen erzwingen",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                OnChanged = o => o.Message = Mitspieler.Essen(Ziel(), o.BoolValue)
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.abwesend",
                Label = "Als abwesend führen",
                Description = "Setzt die Abwesend-Anzeige. Er merkt es an seinem eigenen Bildschirm.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                OnChanged = o => o.Message = Mitspieler.AlsAbwesend(Ziel(), o.BoolValue)
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.steuer",
                Label = "Ans Steuer setzen",
                Description = "Macht ihn zum Fahrer des Boots - auch wenn er gerade woanders steht.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o => o.Message = Mitspieler.AnsSteuer(Ziel())
            });

            // ---------------------------------------------------------- Reden

            kategorie.Add(new CheatOption
            {
                Id = "wem.sagen",
                Label = "In den Chat schreiben",
                Description = "Im eigenen Namen. Früher nahm das Spiel die Absenderkennung als " +
                              "Parameter - damit ging es auch im Namen anderer. Das ist inzwischen " +
                              "geschlossen.",
                Kind = OptionKind.Text,
                Scope = CheatScope.Everyone,
                OnInvoke = o => o.Message = Mitspieler.Sagen(o.TextValue)
            });

            // ---------------------------------------------------------- Rundherum

            kategorie.Add(new CheatOption
            {
                Id = "wem.boot",
                Label = "Boot umlackieren",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                Min = 0f, Max = 10f, Step = 1f,
                OnChanged = o => o.Message = Mitspieler.BootLackieren((int)o.NumberValue)
            });

            kategorie.Add(new CheatOption
            {
                Id = "wem.radio",
                Label = "Radio umstellen",
                Description = "Stellt jedes Radio in der Runde auf dieselbe Frequenz.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                Min = 0f, Max = 1f, Step = 0.05f,
                OnChanged = o => o.Message = Mitspieler.RadioStellen(o.NumberValue)
            });

            return kategorie;
        }
    }
}
