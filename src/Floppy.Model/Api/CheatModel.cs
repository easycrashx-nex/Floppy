using System;
using System.Collections.Generic;

namespace Floppy.Core.Api
{
    /// <summary>Welche Art von Bedienelement ein Cheat bekommt. Overlay und externe App
    /// rendern beide anhand dieses Werts - ein Cheat wird also nur einmal beschrieben.</summary>
    public enum OptionKind
    {
        Toggle,   // An/Aus-Schalter
        Button,   // Einmal-Aktion
        Slider,   // Kommazahl in einem Bereich
        Number,   // freie Zahleingabe
        Text,     // freie Texteingabe (+ Ausführen-Knopf)
        Choice,   // Auswahlliste

        /// <summary>Reine Anzeige, nichts zum Anklicken. Der Text kommt laufend aus dem
        /// Spiel - damit man sieht, wo man gerade steht, bevor man etwas verstellt.</summary>
        Info
    }

    /// <summary>Wen ein Cheat trifft. Als Host läuft die Spiellogik für alle Mitspieler
    /// in deinem Prozess - manche Eingriffe lassen sich deshalb nicht auf dich eingrenzen.</summary>
    public enum CheatScope
    {
        /// <summary>Wirkt nur bei dir. Mitspieler merken nichts davon.</summary>
        OnlyMe,

        /// <summary>Wirkt auf die ganze Runde - Mitspieler bekommen es mit.</summary>
        Everyone,

        /// <summary>Kann beides. Der Schalter ShareWithOthers entscheidet.</summary>
        Selectable
    }

    public class CheatOption
    {
        public string Id;
        public string Label;
        public string Description = "";
        public OptionKind Kind = OptionKind.Toggle;

        /// <summary>Voreinstellung ist OnlyMe - wer die ganze Runde betrifft, muss das
        /// ausdrücklich sagen.</summary>
        public CheatScope Scope = CheatScope.OnlyMe;

        /// <summary>Nur bei Scope.Selectable von Bedeutung: aus = wirkt nur bei dir,
        /// an = wirkt auch bei den Mitspielern. Startet immer aus.</summary>
        public bool ShareWithOthers;

        /// <summary>Was bei diesem Cheat "aus" bedeutet - oder null, wenn es das nicht gibt.
        ///
        /// Bei einem Regler, der einen Zuschlag beschreibt, ist es die Null. Bei einem
        /// Feld, das einen Spielwert wie das Maximalleben zeigt, gibt es keinen solchen
        /// Wert: Dort wäre eine Null nicht "aus", sondern tödlich. "Alles aus" fasst
        /// deshalb nur an, was hier ausdrücklich einen Ruhewert nennt.</summary>
        public float? Ruhewert;

        /// <summary>Normalzustand für Schalter bzw. zustandsbehaftete Auswahllisten.</summary>
        public bool Ruhebool;
        public int? Ruheauswahl;

        // Nur für Slider/Number
        public float Min;
        public float Max = 100f;
        public float Step = 1f;

        // Nur für Choice
        public string[] Choices = Array.Empty<string>();

        // Aktueller Zustand
        public bool BoolValue;
        public float NumberValue;
        public string TextValue = "";
        public int ChoiceIndex;

        /// <summary>Wird bei jeder Wertänderung gerufen (Toggle/Slider/Number/Text/Choice).</summary>
        public Action<CheatOption> OnChanged;

        /// <summary>Wird bei Button gerufen - und bei Text zusätzlich beim Ausführen.</summary>
        public Action<CheatOption> OnInvoke;

        /// <summary>Kurze Rückmeldung des letzten Aufrufs, z.B. "Nichts in der Hand".
        /// Wird nach dem Abholen geleert, damit sie nicht kleben bleibt.</summary>
        public string Message = "";
        public bool MessageIsError;

        /// <summary>Optional: false blendet den Cheat aus bzw. graut ihn aus.</summary>
        public Func<bool> IsAvailable;

        public bool Available => IsAvailable == null || IsAvailable();

        public bool Active
        {
            get
            {
                if (Kind == OptionKind.Toggle) return BoolValue != Ruhebool;
                if (Kind == OptionKind.Slider || Kind == OptionKind.Number)
                    return Ruhewert.HasValue && Math.Abs(NumberValue - Ruhewert.Value) > 0.0001f;
                if (Kind == OptionKind.Choice)
                    return Ruheauswahl.HasValue && ChoiceIndex != Ruheauswahl.Value;
                return false;
            }
        }

        public void Fire()
        {
            Message = "";
            MessageIsError = false;
            OnInvoke?.Invoke(this);
        }

        public void Fail(string message)
        {
            Message = message;
            MessageIsError = true;
        }

        /// <summary>Holt die Rückmeldung ab und räumt sie weg.</summary>
        public string TakeMessage()
        {
            string message = Message;
            Message = "";
            return message;
        }

        public void NotifyChanged()
        {
            OnChanged?.Invoke(this);
        }
    }

    public class CheatCategory
    {
        public string Name;
        public List<CheatOption> Options = new List<CheatOption>();

        public CheatCategory(string name) { Name = name; }

        public CheatCategory Add(CheatOption option)
        {
            Options.Add(option);
            return this;
        }
    }
}
