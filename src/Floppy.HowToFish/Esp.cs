using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Zeichnet Markierungen für Kreaturen, Mitspieler und Gegenstände.
    /// Hängt als eigenes Objekt in der Szene, damit es sein eigenes OnGUI bekommt.</summary>
    internal class Esp : MonoBehaviour
    {
        public static bool ShowCreatures;
        public static bool ShowPlayers;
        public static bool ShowItems;
        public static float MaxDistance = 150f;

        private static GUIStyle _style;
        private static Texture2D _dot;

        public static void Spawn()
        {
            var host = new GameObject("Floppy.Esp");
            host.AddComponent<Esp>();
            DontDestroyOnLoad(host);
        }

        private void OnGUI()
        {
            if (!ShowCreatures && !ShowPlayers && !ShowItems) return;

            var camera = global::GameInfo.CurCamera;
            if (camera == null || global::Player.LocalPlayer == null) return;

            EnsureStyle();

            Vector3 eye = camera.transform.position;

            if (ShowPlayers) DrawPlayers(camera, eye);
            if (ShowCreatures) DrawCreatures(camera, eye);
            if (ShowItems) DrawItems(camera, eye);
        }

        private void DrawPlayers(Camera camera, Vector3 eye)
        {
            var players = global::PlayerManager.AlivePlayers;
            if (players == null) return;

            foreach (var player in players)
            {
                if (player == null || player == global::Player.LocalPlayer) continue;
                Mark(camera, eye, player.Transform.position + Vector3.up, "Mitspieler", Color.cyan);
            }
        }

        /// <summary>Die Viecher kommen aus Viecher.Lebende() und nicht mehr aus
        /// CreatureManager - dessen Liste fuellt nur der Gastgeber, der ESP blieb als
        /// Gast deshalb leer. Markiert wird die Koerpermitte statt des Transform-
        /// Ursprungs: Bei Fischen und Voegeln liegt der irgendwo unter dem Tier.</summary>
        private void DrawCreatures(Camera camera, Vector3 eye)
        {
            foreach (var creature in Viecher.Lebende())
                Mark(camera, eye, Viecher.Koerpermitte(creature), CleanName(creature.name),
                     new Color(1f, 0.75f, 0.2f));
        }

        private void DrawItems(Camera camera, Vector3 eye)
        {
            foreach (var item in UnityEngine.Object.FindObjectsByType<global::Item>(FindObjectsSortMode.None))
            {
                if (item == null || item.SyncedHolder != null) continue;
                Mark(camera, eye, item.transform.position, CleanName(item.name), new Color(0.6f, 1f, 0.6f));
            }
        }

        private void Mark(Camera camera, Vector3 eye, Vector3 worldPos, string label, Color color)
        {
            float distance = Vector3.Distance(eye, worldPos);
            if (distance > MaxDistance) return;

            Vector3 screen = camera.WorldToScreenPoint(worldPos);
            if (screen.z <= 0f) return; // hinter uns

            float x = screen.x;
            float y = Screen.height - screen.y;

            // Weiter entfernt = kleiner und blasser, damit es nicht zumüllt.
            float fade = Mathf.Clamp01(1f - distance / MaxDistance);
            var faded = new Color(color.r, color.g, color.b, 0.35f + fade * 0.65f);

            GUI.color = faded;
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, 4f, 4f), _dot);

            _style.normal.textColor = faded;
            var text = label + "  " + Mathf.RoundToInt(distance) + "m";
            var size = _style.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(x - size.x / 2f, y + 5f, size.x, size.y), text, _style);

            GUI.color = Color.white;
        }

        private static string CleanName(string name)
        {
            // Unity hängt an geklonten Objekten "(Clone)" an.
            int marker = name.IndexOf("(Clone)");
            return marker < 0 ? name : name.Substring(0, marker);
        }

        private static void EnsureStyle()
        {
            if (_style != null) return;

            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter
            };

            _dot = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _dot.SetPixel(0, 0, Color.white);
            _dot.Apply();
            _dot.hideFlags = HideFlags.HideAndDontSave;
        }
    }
}
