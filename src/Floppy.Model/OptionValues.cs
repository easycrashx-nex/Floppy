using System;
using System.Collections.Generic;
using Floppy.Core.Api;

namespace Floppy.Core
{
    /// <summary>Gemeinsame Zustandsprüfung für IPC, Profile und Schemawechsel.</summary>
    internal sealed class OptionValues
    {
        internal bool Bool;
        internal float Number;
        internal string Text;
        internal int Choice;
        internal bool Share;

        internal static OptionValues Capture(CheatOption option) => new OptionValues
        {
            Bool = option.BoolValue, Number = option.NumberValue, Text = option.TextValue ?? "",
            Choice = option.ChoiceIndex, Share = option.ShareWithOthers
        };

        internal static string ValueKey(OptionKind kind)
        {
            switch (kind)
            {
                case OptionKind.Toggle: return "bool";
                case OptionKind.Slider:
                case OptionKind.Number: return "number";
                case OptionKind.Text: return "text";
                case OptionKind.Choice: return "choice";
                default: throw new InvalidOperationException("Diese Option hat keinen bearbeitbaren Wert");
            }
        }

        internal static OptionValues Read(CheatOption option, Dictionary<string, object> input, bool profile = false,
            bool deferChoiceValidation = false)
        {
            string key = ValueKey(option.Kind);
            if (profile && input.TryGetValue("kind", out object kind) &&
                (!(kind is string name) || name != option.Kind.ToString()))
                throw new FormatException("Optionstyp hat sich geändert: " + option.Label);
            if (!profile)
                foreach (string other in new[] { "bool", "number", "text", "choice" })
                    if (other != key && input.ContainsKey(other))
                        throw new FormatException(option.Label + " erwartet " + key);

            var values = Capture(option);
            bool supplied = input.TryGetValue(key, out object raw);
            if (profile && !supplied) throw new FormatException("Profilwert fehlt: " + option.Label);
            if (!supplied && !input.ContainsKey("share")) throw new FormatException("Kein Wert angegeben");
            if (supplied)
            {
                switch (key)
                {
                    case "bool":
                        if (!(raw is bool boolean)) throw new FormatException("An/Aus-Wert erwartet");
                        values.Bool = boolean;
                        break;
                    case "number":
                        if (!(raw is double number) || double.IsNaN(number) || double.IsInfinity(number) ||
                            number < -float.MaxValue || number > float.MaxValue || number < option.Min || number > option.Max)
                            throw new FormatException("Zahl außerhalb des erlaubten Bereichs: " + option.Label);
                        values.Number = (float)number;
                        break;
                    case "text":
                        if (!(raw is string text)) throw new FormatException("Text erwartet");
                        values.Text = text;
                        break;
                    case "choice":
                        if (!(raw is double choice) || double.IsNaN(choice) || choice < 0 || choice > int.MaxValue || choice != Math.Truncate(choice))
                            throw new FormatException("Gültiger Auswahlindex erwartet");
                        values.Choice = (int)choice;
                        if (profile && input.TryGetValue("choiceText", out object selected))
                        {
                            if (!(selected is string label)) throw new FormatException("Ungültiger Auswahltext");
                            if (!deferChoiceValidation && label.Length > 0)
                            {
                                values.Choice = Array.IndexOf(option.Choices ?? Array.Empty<string>(), label);
                                if (values.Choice < 0) throw new FormatException("Auswahl nicht mehr vorhanden: " + label);
                            }
                        }
                        int count = option.Choices?.Length ?? 0;
                        if (!deferChoiceValidation && (values.Choice < 0 || values.Choice >= count && !(profile && count == 0 && values.Choice == 0)))
                            throw new FormatException("Auswahl außerhalb der Liste: " + option.Label);
                        break;
                }
            }
            if (input.TryGetValue("share", out object share))
            {
                if (!(share is bool flag)) throw new FormatException("An/Aus-Wert für Mitspieler erwartet");
                if (!profile && option.Scope != CheatScope.Selectable)
                    throw new FormatException("Mitspieler-Schalter ist hier nicht verfügbar");
                values.Share = option.Scope == CheatScope.Selectable && flag;
            }
            return values;
        }

        internal void Write(CheatOption option)
        {
            option.BoolValue = Bool;
            option.NumberValue = Number;
            option.TextValue = Text;
            option.ChoiceIndex = Choice;
            option.ShareWithOthers = Share;
        }

        internal void Apply(CheatOption option)
        {
            var previous = Capture(option);
            Write(option);
            option.Message = "";
            option.MessageIsError = false;
            try
            {
                option.NotifyChanged();
                if (option.MessageIsError) throw new InvalidOperationException(option.Message);
            }
            catch { previous.Write(option); throw; }
        }
    }
}
