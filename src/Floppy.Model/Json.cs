using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Floppy.Core
{
    /// <summary>Gerade so viel JSON, wie das Protokoll braucht.
    ///
    /// Warum nicht Newtonsoft? Bei einem Mono-Spiel könnten wir uns dessen DLL ausleihen,
    /// aber IL2CPP-Spiele bringen überhaupt keine verwalteten Bibliotheken mit. Eine
    /// eigene, kleine Umsetzung läuft überall gleich - und die Nachrichten hier sind
    /// flach und überschaubar.</summary>
    public static class Json
    {
        // ------------------------------------------------------------ Schreiben

        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Text(string value)
        {
            return value == null ? "null" : "\"" + Escape(value) + "\"";
        }

        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "JSON-Zahlen müssen endlich sein");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        /// <summary>Baut ein Objekt aus fertig geschriebenen Werten.</summary>
        public sealed class Writer
        {
            private readonly StringBuilder _sb = new StringBuilder("{");

            private void Trenner()
            {
                if (_sb.Length > 1) _sb.Append(',');
            }

            public Writer Raw(string name, string rohwert)
            {
                Trenner();
                _sb.Append(Text(name)).Append(':').Append(rohwert);
                return this;
            }

            public Writer Set(string name, string value) { return Raw(name, Text(value)); }
            public Writer Set(string name, bool value) { return Raw(name, Bool(value)); }
            public Writer Set(string name, double value) { return Raw(name, Number(value)); }
            public Writer Set(string name, int value) { return Raw(name, value.ToString(CultureInfo.InvariantCulture)); }

            public override string ToString()
            {
                return _sb.ToString() + "}";
            }
        }

        public static string Array(IEnumerable<string> rohwerte)
        {
            var sb = new StringBuilder("[");
            bool erstes = true;

            foreach (string wert in rohwerte)
            {
                if (!erstes) sb.Append(',');
                sb.Append(wert);
                erstes = false;
            }

            return sb.Append(']').ToString();
        }

        public static string TextArray(IEnumerable<string> werte)
        {
            var roh = new List<string>();
            foreach (string w in werte) roh.Add(Text(w));
            return Array(roh);
        }

        // ------------------------------------------------------------ Lesen

        /// <summary>Liest ein JSON-Objekt einschließlich verschachtelter Profile.</summary>
        public static Dictionary<string, object> Parse(string json)
        {
            return new Reader(json).ReadObject();
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _position;

            internal Reader(string text)
            {
                if (text == null || text.Length > 1024 * 1024)
                    throw new FormatException("JSON fehlt oder ist zu groß");
                _text = text;
            }

            internal Dictionary<string, object> ReadObject()
            {
                var result = Value(0) as Dictionary<string, object>;
                Space();
                if (result == null || _position != _text.Length) throw Invalid();
                return result;
            }

            private FormatException Invalid() => new FormatException("Ungültiges JSON bei Zeichen " + _position);
            private void Space()
            {
                while (_position < _text.Length && (_text[_position] == ' ' || _text[_position] == '\t' ||
                    _text[_position] == '\r' || _text[_position] == '\n')) _position++;
            }
            private bool Take(char c)
            {
                Space();
                if (_position >= _text.Length || _text[_position] != c) return false;
                _position++;
                return true;
            }
            private void Expect(char c) { if (!Take(c)) throw Invalid(); }

            private object Value(int depth)
            {
                if (depth > 64) throw new FormatException("JSON ist zu tief verschachtelt");
                Space();
                if (_position >= _text.Length) throw Invalid();
                if (_text[_position] == '"') return String();
                if (Take('{'))
                {
                    var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    if (Take('}')) return result;
                    do
                    {
                        Space();
                        string name = String();
                        Expect(':');
                        if (result.ContainsKey(name)) throw new FormatException("Doppelter JSON-Schlüssel: " + name);
                        result.Add(name, Value(depth + 1));
                    } while (Take(','));
                    Expect('}');
                    return result;
                }
                if (Take('['))
                {
                    var result = new List<object>();
                    if (Take(']')) return result;
                    do { result.Add(Value(depth + 1)); } while (Take(','));
                    Expect(']');
                    return result;
                }
                if (Literal("true")) return true;
                if (Literal("false")) return false;
                if (Literal("null")) return null;
                return Number();
            }

            private bool Literal(string value)
            {
                if (_position + value.Length > _text.Length ||
                    string.CompareOrdinal(_text, _position, value, 0, value.Length) != 0) return false;
                _position += value.Length;
                return true;
            }

            private string String()
            {
                Expect('"');
                var result = new StringBuilder();
                while (_position < _text.Length)
                {
                    char c = _text[_position++];
                    if (c == '"') return result.ToString();
                    if (c < ' ') throw Invalid();
                    if (c != '\\') { result.Append(c); continue; }
                    if (_position >= _text.Length) throw Invalid();
                    switch (_text[_position++])
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (_position + 4 > _text.Length || !ushort.TryParse(_text.Substring(_position, 4),
                                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code)) throw Invalid();
                            result.Append((char)code);
                            _position += 4;
                            break;
                        default: throw Invalid();
                    }
                }
                throw Invalid();
            }

            private bool Digit() => _position < _text.Length && _text[_position] >= '0' && _text[_position] <= '9';
            private void Digits()
            {
                if (!Digit()) throw Invalid();
                do { _position++; } while (Digit());
            }
            private double Number()
            {
                int start = _position;
                if (_position < _text.Length && _text[_position] == '-') _position++;
                if (_position < _text.Length && _text[_position] == '0') _position++;
                else Digits();
                if (_position < _text.Length && _text[_position] == '.') { _position++; Digits(); }
                if (_position < _text.Length && (_text[_position] == 'e' || _text[_position] == 'E'))
                {
                    _position++;
                    if (_position < _text.Length && (_text[_position] == '+' || _text[_position] == '-')) _position++;
                    Digits();
                }
                if (!double.TryParse(_text.Substring(start, _position - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value))
                    throw Invalid();
                return value;
            }
        }

        // ------------------------------------------------------------ Bequemlichkeit

        public static string AlsText(Dictionary<string, object> daten, string name)
        {
            object wert;
            return daten.TryGetValue(name, out wert) ? wert as string : null;
        }

        public static bool? AlsBool(Dictionary<string, object> daten, string name)
        {
            object wert;
            if (!daten.TryGetValue(name, out wert) || wert == null) return null;
            if (wert is bool b) return b;
            return null;
        }

        public static double? AlsZahl(Dictionary<string, object> daten, string name)
        {
            object wert;
            if (!daten.TryGetValue(name, out wert) || wert == null) return null;
            if (wert is double d) return d;
            return null;
        }
    }
}
