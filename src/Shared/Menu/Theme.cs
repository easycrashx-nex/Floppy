using UnityEngine;

namespace Floppy.Core.Menu
{
    /// <summary>Erscheinungsbild des Overlays im Spiel - dieselbe Farbwelt und Formensprache
    /// wie die Desktop-App.
    ///
    /// Unitys IMGUI kennt keine abgerundeten Ecken. Die Flächen werden deshalb hier als
    /// Texturen erzeugt und als Neunfelder-Rahmen gedehnt, damit sie in jeder Größe
    /// sauber bleiben.</summary>
    public class Theme
    {
        // Dieselben Töne wie App.xaml
        public static readonly Color Accent = new Color32(58, 166, 255, 255);
        public static readonly Color AccentSoft = new Color32(43, 95, 143, 255);
        public static readonly Color Background = new Color32(15, 17, 21, 250);
        public static readonly Color PanelColor = new Color32(22, 25, 32, 255);
        public static readonly Color RowColor = new Color32(28, 32, 41, 255);
        public static readonly Color RowHover = new Color32(35, 40, 52, 255);
        public static readonly Color TextColor = new Color32(226, 230, 238, 255);
        public static readonly Color MutedColor = new Color32(139, 147, 163, 255);
        public static readonly Color OkColor = new Color32(95, 217, 139, 255);
        public static readonly Color WarnColor = new Color32(255, 184, 77, 255);
        public static readonly Color SwitchOff = new Color32(51, 58, 71, 255);
        public static readonly Color KnobOff = new Color32(154, 163, 178, 255);

        public GUIStyle Window, Panel, Row, Title, Subtitle, Label, Value, Hint;
        public GUIStyle NavItem, NavItemActive, Action, AccentAction, CloseButton, TextField;
        public GUIStyle StatusOk, StatusWarn, ScopeWarn, ShareLabel;
        public GUIStyle EmptyTitle, Placeholder, InfoWert;
        public GUIStyle Dropdown, Chevron, ListBox, ListItem, ListItemActive;

        public Texture2D Pill, Round, Flat, Circle;

        /// <summary>Bahn des Schiebeschalters und Spur des Reglers - weiß hinterlegt,
        /// damit sie über GUI.color eingefärbt werden können.</summary>
        public GUIStyle SwitchTrack, SliderTrack;

        private bool _built;

        public void Apply()
        {
            if (_built) return;
            _built = true;

            Flat = Solid(Color.white);
            Round = Rounded(8, Color.white);
            Pill = Rounded(11, Color.white);
            Circle = Kreis(32);

            // Neunfeld-Kacheln: die Ecken bleiben beim Dehnen rund
            SwitchTrack = new GUIStyle
            {
                normal = { background = Pill },
                border = Compat.Offset(11, 11, 11, 11)
            };

            SliderTrack = new GUIStyle
            {
                normal = { background = Rounded(3, Color.white) },
                border = Compat.Offset(3, 3, 3, 3)
            };

            Window = Box(Background, 10, Compat.Offset(16, 16, 14, 16));
            Panel = Box(PanelColor, 10, Compat.Offset(10, 10, 10, 10));

            Row = new GUIStyle
            {
                normal = { background = Tinted(Round, RowColor) },
                border = Compat.Offset(9, 9, 9, 9),
                padding = Compat.Offset(12, 12, 10, 10),
                margin = Compat.Offset(0, 0, 0, 6)
            };

            Title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Accent }
            };

            Subtitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = MutedColor },
                margin = Compat.Offset(10, 0, 7, 0)
            };

            Label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = TextColor }
            };

            Value = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Accent }
            };

            Hint = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = MutedColor }
            };

            StatusOk = new GUIStyle(Hint) { fontSize = 12, normal = { textColor = OkColor } };
            StatusWarn = new GUIStyle(Hint) { fontSize = 12, normal = { textColor = WarnColor } };

            ScopeWarn = new GUIStyle(Hint)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                margin = Compat.Offset(0, 0, 3, 0),
                normal = { textColor = WarnColor }
            };

            ShareLabel = new GUIStyle(Hint)
            {
                fontSize = 11,
                normal = { textColor = MutedColor }
            };

            // Wie die Ueberschrift im leeren Zustand der Desktop-App
            EmptyTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = TextColor }
            };

            // Zugeklapptes Auswahlfeld - sieht aus wie ein Textfeld, verhaelt sich wie ein Knopf
            Dropdown = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                border = Compat.Offset(9, 9, 9, 9),
                padding = Compat.Offset(12, 30, 7, 7),
                normal = { background = Tinted(Round, RowHover), textColor = TextColor },
                hover = { background = Tinted(Round, RowHover), textColor = Color.white },
                active = { background = Tinted(Round, RowHover), textColor = Color.white },
                focused = { background = Tinted(Round, RowHover), textColor = TextColor }
            };

            Chevron = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = MutedColor }
            };

            // Die aufgeklappte Liste - dunkler Rahmen mit Rand, wie im Client
            ListBox = new GUIStyle
            {
                normal = { background = Tinted(Rounded(8, Color.white), RowColor) },
                border = Compat.Offset(9, 9, 9, 9)
            };

            ListItem = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                border = Compat.Offset(6, 6, 6, 6),
                padding = Compat.Offset(10, 10, 4, 4),
                normal = { background = null, textColor = TextColor },
                hover = { background = Tinted(Rounded(5, Color.white), RowHover), textColor = Color.white },
                active = { background = Tinted(Rounded(5, Color.white), AccentSoft), textColor = Color.white }
            };

            ListItemActive = new GUIStyle(ListItem)
            {
                fontStyle = FontStyle.Bold,
                normal = { background = Tinted(Rounded(5, Color.white), AccentSoft), textColor = Color.white },
                hover = { background = Tinted(Rounded(5, Color.white), AccentSoft), textColor = Color.white }
            };

            InfoWert = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Accent }
            };

            Placeholder = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = MutedColor }
            };

            NavItem = Chip(PanelColor, MutedColor, TextAnchor.MiddleLeft);
            NavItem.hover.background = Tinted(Round, RowColor);
            NavItem.hover.textColor = TextColor;

            NavItemActive = Chip(AccentSoft, Color.white, TextAnchor.MiddleLeft);
            NavItemActive.fontStyle = FontStyle.Bold;
            NavItemActive.hover.background = Tinted(Round, AccentSoft);
            NavItemActive.hover.textColor = Color.white;

            Action = Chip(RowHover, TextColor, TextAnchor.MiddleCenter);
            Action.hover.background = Tinted(Round, AccentSoft);
            Action.hover.textColor = Color.white;
            Action.active.background = Tinted(Round, Accent);
            Action.active.textColor = Color.white;

            AccentAction = Chip(AccentSoft, Color.white, TextAnchor.MiddleCenter);
            AccentAction.fontStyle = FontStyle.Bold;
            AccentAction.hover.background = Tinted(Round, Accent);

            CloseButton = new GUIStyle(Action) { fontStyle = FontStyle.Bold };

            TextField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                normal = { background = Tinted(Round, RowHover), textColor = TextColor },
                focused = { background = Tinted(Round, RowHover), textColor = Color.white },
                hover = { background = Tinted(Round, RowHover), textColor = TextColor },
                border = Compat.Offset(9, 9, 9, 9),
                padding = Compat.Offset(10, 10, 7, 7),
                margin = Compat.Offset(0, 0, 2, 2)
            };
        }

        // --- Bausteine ---

        private GUIStyle Box(Color color, int radius, RectOffset padding)
        {
            return new GUIStyle
            {
                normal = { background = Tinted(Rounded(radius, Color.white), color) },
                border = Compat.Offset(radius + 1, radius + 1, radius + 1, radius + 1),
                padding = padding
            };
        }

        private GUIStyle Chip(Color color, Color text, TextAnchor anchor)
        {
            return new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = anchor,
                border = Compat.Offset(9, 9, 9, 9),
                padding = Compat.Offset(12, 12, 8, 8),
                margin = Compat.Offset(0, 0, 0, 4),
                normal = { background = Tinted(Round, color), textColor = text },
                active = { background = Tinted(Round, color), textColor = text },
                focused = { background = Tinted(Round, color), textColor = text }
            };
        }

        // --- Texturen ---

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        /// <summary>Eine weiße Fläche mit abgerundeten Ecken. Wird später eingefärbt und
        /// als Neunfelder-Rahmen gedehnt, deshalb genügt eine kleine Kachel.</summary>
        private static Texture2D Rounded(int radius, Color color)
        {
            int size = radius * 2 + 3;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Abstand zum nächsten Eckmittelpunkt bestimmt die Deckkraft
                    float dx = Mathf.Max(0, Mathf.Max(radius - x, x - (size - 1 - radius)));
                    float dy = Mathf.Max(0, Mathf.Max(radius - y, y - (size - 1 - radius)));
                    float abstand = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = Mathf.Clamp01(radius - abstand + 0.5f);
                    pixels[y * size + x] = new Color(color.r, color.g, color.b, color.a * alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        /// <summary>Ein gefüllter Kreis mit weichem Rand - für den Knopf des Schalters
        /// und den Griff des Reglers. Quadratisch, damit er beim Zeichnen nicht verzerrt.</summary>
        private static Texture2D Kreis(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            float mitte = (size - 1) / 2f;
            float radius = size / 2f - 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float abstand = Mathf.Sqrt((x - mitte) * (x - mitte) + (y - mitte) * (y - mitte));
                    float alpha = Mathf.Clamp01(radius - abstand + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        /// <summary>Färbt eine Vorlage ein, ohne sie neu zu berechnen.</summary>
        private static Texture2D Tinted(Texture2D vorlage, Color color)
        {
            var texture = new Texture2D(vorlage.width, vorlage.height, TextureFormat.RGBA32, false);
            var quelle = vorlage.GetPixels();

            for (int i = 0; i < quelle.Length; i++)
                quelle[i] = new Color(color.r, color.g, color.b, color.a * quelle[i].a);

            texture.SetPixels(quelle);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}
