using HarmonyLib;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Stilles Zielen: Das Fadenkreuz bleibt stehen, nur das Geschoss biegt ab.
    ///
    /// Warum das hier sauber geht - und warum es nicht der Kamera-Weg ist:
    ///
    /// Weapon.Shoot() baut die Schussrichtung aus Attachments.FirePoint.forward (dem
    /// Muendungs-Transform, nicht der Kamera), wuerfelt die Streuung darauf und uebergibt
    /// fertige Geschwindigkeitsvektoren an ProjectileManager.AddProjectile bzw.
    /// AddProjectiles. Diese beiden Methoden sind der einzige Durchgang: Sie legen die
    /// oertliche Simulation an *und* schicken denselben Wert per Server-RPC an alle
    /// anderen. Wer hier eingreift, aendert also beides zugleich - kein Auseinanderlaufen
    /// zwischen dem, was du siehst, und dem, was die Mitspieler sehen.
    ///
    /// Und weil der Schuetzen-Client die Treffer selbst entscheidet, kommt der Schaden
    /// auch da an, wo das Geschoss hinfliegt.
    ///
    /// Kamera, Waffenmodell, Rueckstoss und Laserpunkt bleiben unberuehrt.
    ///
    /// Eine Grenze hat es: Steht jemand direkt vor der Muendung, wirft Weapon.Shoot()
    /// gar kein Geschoss, sondern trifft ueber einen Strahl sofort. Auf Tuchfuehlung
    /// nuetzt stilles Zielen deshalb nichts - auf Entfernung dafuer umso mehr.</summary>
    internal static class StillesZielen
    {
        public static bool Aktiv;

        /// <summary>Streuung erhalten oder alles auf einen Punkt legen. Bei Schrot heisst
        /// "erhalten": Die Garbe behaelt ihre Form und wird nur als Ganzes gedreht.</summary>
        public static bool StreuungBehalten = true;

        /// <summary>Vorhalten auf bewegte Ziele - das Geschoss braucht Zeit.</summary>
        public static bool Vorhalten = true;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(global::ProjectileManager), "AddProjectile"),
                prefix: new HarmonyMethod(typeof(StillesZielen), nameof(VorEinzelschuss)));

            harmony.Patch(
                original: AccessTools.Method(typeof(global::ProjectileManager), "AddProjectiles"),
                prefix: new HarmonyMethod(typeof(StillesZielen), nameof(VorGarbe)));
        }

        /// <summary>Nur die eigenen Schuesse anfassen. isLocal unterscheidet den echten
        /// Schuss von der nachgespielten Kopie fremder Schuesse - ohne diese Pruefung
        /// wuerden wir auch die Geschosse der Mitspieler umlenken.</summary>
        private static bool Meiner(global::Player owner, bool isLocal)
        {
            return Aktiv && isLocal && owner != null && owner == global::Player.LocalPlayer;
        }

        private static void VorEinzelschuss(global::Player owner, bool isLocal,
                                            Vector3 pos, ref Vector3 velocity)
        {
            if (!Meiner(owner, isLocal)) return;

            Vector3 hin = Richtung(pos, velocity.magnitude);
            if (hin == Vector3.zero) return;

            // Tempo behalten, nur die Richtung tauschen: Die Flugbahn und die
            // Schadensberechnung haengen an der Laenge des Vektors.
            velocity = hin * velocity.magnitude;
        }

        private static void VorGarbe(global::Player owner, bool isLocal,
                                     Vector3 pos, Vector3[] velocities)
        {
            if (!Meiner(owner, isLocal)) return;
            if (velocities == null || velocities.Length == 0) return;

            float tempo = velocities[0].magnitude;

            Vector3 hin = Richtung(pos, tempo);
            if (hin == Vector3.zero) return;

            if (!StreuungBehalten)
            {
                for (int i = 0; i < velocities.Length; i++)
                    velocities[i] = hin * velocities[i].magnitude;

                return;
            }

            // Die Garbe als Ganzes drehen: Wir nehmen die mittlere Richtung als Achse und
            // drehen alle Schrotkugeln um denselben Betrag mit. So bleibt die Streuung
            // erhalten, statt dass alles auf einem Punkt landet - das faellt sonst auf.
            Vector3 mitte = Vector3.zero;
            foreach (var v in velocities) mitte += v;

            if (mitte.sqrMagnitude < 0.0001f) return;

            Quaternion drehung = Quaternion.FromToRotation(mitte.normalized, hin);

            for (int i = 0; i < velocities.Length; i++)
                velocities[i] = drehung * velocities[i];
        }

        /// <summary>Wohin das Geschoss soll - von der Muendung aus, nicht vom Auge.
        ///
        /// Beim Vorhalten wird die Flugzeit geschaetzt und das Ziel um seine eigene
        /// Bewegung versetzt. Das ist bewusst nur ein Schritt und keine Iteration: Ein
        /// Fisch, der die Richtung wechselt, waere ohnehin nicht vorhersagbar, und zwei
        /// Runden Nachrechnen bringen bei diesen Geschwindigkeiten nichts.</summary>
        private static Vector3 Richtung(Vector3 muendung, float tempo)
        {
            var ziel = Zielhilfe.Ziel;
            if (ziel == null) return Vector3.zero;

            Vector3 ort = Zielhilfe.Zielort(ziel);

            if (Vorhalten && tempo > 0.01f && ziel.Rig != null)
            {
                float zeit = Vector3.Distance(muendung, ort) / tempo;
                ort += ziel.Rig.velocity * zeit;
            }

            Vector3 hin = ort - muendung;
            return hin.sqrMagnitude < 0.0001f ? Vector3.zero : hin.normalized;
        }
    }
}
