using System.Collections.Generic;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Woher man die lebenden Viecher bekommt - und warum nicht von der
    /// naheliegenden Stelle.
    ///
    /// Es gibt einen CreatureManager mit einer Liste _aliveCreatures. Die sieht aus wie
    /// die richtige Quelle, ist es aber nicht: Sie wird nur beim Gastgeber gefuellt.
    /// Creature.OnStartClient traegt das Vieh nur ein, wenn IsServerInitialized gilt.
    /// Als Gast in der Runde eines Freundes bleibt sie schlicht leer - Zielhilfe und ESP
    /// haetten dort also gar nichts gefunden.
    ///
    /// Verlaesslich ist ItemManager.Items. Das benutzt das Spiel fuer seine eigene
    /// Controller-Zielhilfe. Ein Haken: Das Dictionary bildet Transform auf Item ab, und
    /// ein Vieh bringt mehrere Kollisionskoerper mit - dasselbe Tier steht also mehrfach
    /// drin. Deshalb die Aussortierung ueber die Instanzkennung.</summary>
    internal static class Viecher
    {
        /// <summary>Alle lebenden Viecher in der Runde, jedes genau einmal.</summary>
        public static List<global::Creature> Lebende()
        {
            var raus = new List<global::Creature>();
            var gesehen = new HashSet<int>();

            foreach (var sache in global::ItemManager.Items.Values)
            {
                if (sache == null) continue;

                var vieh = sache.Creature;
                if (!Lebt(vieh)) continue;
                if (!gesehen.Add(vieh.GetInstanceID())) continue;

                raus.Add(vieh);
            }

            return raus;
        }

        /// <summary>Derselbe Test, den das Spiel selbst fuer seine Zielhilfe benutzt.
        /// IsDeinitializing ist wichtig: Ein Vieh, das gerade abgebaut wird, lebt formal
        /// noch, laesst sich aber nicht mehr treffen.</summary>
        public static bool Lebt(global::Creature vieh)
        {
            return vieh != null
                && vieh.isActiveAndEnabled
                && !vieh.IsDeinitializing
                && !vieh.IsDead;
        }

        /// <summary>Der Schwerpunkt des Koerpers - derselbe Punkt, auf den die
        /// Controller-Zielhilfe des Spiels zielt. Deutlich besser als der Fusspunkt des
        /// Transforms, der bei Fischen und Voegeln irgendwo unter dem Tier liegt.</summary>
        public static Vector3 Koerpermitte(global::Creature vieh)
        {
            return vieh.Rig != null ? vieh.Rig.worldCenterOfMass : vieh.transform.position;
        }

        /// <summary>Der Kopf - und zwar genau so, wie das Spiel ihn beim Treffer prueft:
        /// Creature.Hit rechnet den Trefferpunkt in den lokalen Raum des Viehs und
        /// vergleicht dessen z-Wert mit HeadPos. Wir gehen den Weg rueckwaerts und legen
        /// den Zielpunkt knapp hinter diese Grenze, damit der Kopftreffer-Aufschlag
        /// (GameInfo.HeadShotDamageMulti) sicher greift.</summary>
        public static Vector3 Kopf(global::Creature vieh)
        {
            return vieh.transform.TransformPoint(new Vector3(0f, 0f, vieh.HeadPos + 0.1f));
        }

        /// <summary>Steht etwas dazwischen? Geprueft wird gegen Gelaende und Boot - die
        /// beiden Ebenen, die auch das Spiel bei seiner Sichtpruefung nimmt. Andere
        /// Viecher blocken bewusst nicht, sonst verliert man das Ziel im Schwarm.</summary>
        public static bool Verdeckt(Vector3 auge, Vector3 ziel)
        {
            return Physics.Linecast(auge, ziel,
                (int)global::GameInfo.LevelLayer | (int)global::GameInfo.BoatLayer,
                QueryTriggerInteraction.Ignore);
        }
    }
}
