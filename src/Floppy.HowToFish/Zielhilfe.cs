using System;
using HarmonyLib;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Zielhilfe: einmal anvisieren, dann bleibt der Blick dran.
    ///
    /// Bewusst kein Rundumschlag, der immer aufs naechstbeste Vieh springt. Du suchst dir
    /// das Ziel aus, indem du es kurz ins Fadenkreuz nimmst - danach haelt der Blick es
    /// fest, bis es tot ist, zu weit weg, verdeckt oder du es loslaesst.
    ///
    /// Wo wir eingreifen, und warum ausgerechnet dort:
    ///
    /// PlayerCamera haelt ein privates Vector3 _rot mit Neigung (x) und Drehung (y). Das
    /// ist die einzige Stelle, an der die Blickrichtung dauerhaft steht - jede Maus-
    /// bewegung addiert dort drauf. Am Ende von Update() baut SetCamPosRot() daraus die
    /// eulerAngles der Kamera. Wer stattdessen den Kamera-Transform direkt setzt, dessen
    /// Wert ist einen Frame spaeter wieder weg.
    ///
    /// Der Zeitpunkt ist genauso wichtig wie der Ort. Update() ruft der Reihe nach
    /// MouseMovement() und dann SetCamPosRot(). Ein Postfix auf MouseMovement liegt genau
    /// dazwischen: nach Mauseingabe und Waffenrueckstoss, aber noch vor dem Anwenden -
    /// unsere Korrektur wirkt also im selben Bild. Ein LateUpdate haette einen Frame
    /// Verzug gehabt, weil es erst nach dem ganzen Update laeuft.
    ///
    /// Die Controller-Zielhilfe des Spiels (PlayerAimAssist) schreibt ins selbe Feld,
    /// kommt sich aber nicht in die Quere: Sie liefert nur etwas, wenn ein Controller
    /// aktiv ist und ueber Kimme und Korn gezielt wird.</summary>
    internal static class Zielhilfe
    {
        public static bool Aktiv;

        /// <summary>Wie schnell der Blick nachzieht. 1 waere ein harter Sprung.</summary>
        public static float Klebekraft = 0.35f;

        /// <summary>Wie weit ein Ziel hoechstens weg sein darf.</summary>
        public static float Reichweite = 120f;

        /// <summary>Wie nah am Fadenkreuz ein Vieh sein muss, damit es ueberhaupt
        /// aufgenommen wird - in Grad.</summary>
        public static float Fangwinkel = 12f;

        /// <summary>0 = Koerpermitte, 1 = Kopf.</summary>
        public static int Zielpunkt;

        /// <summary>Ziele nicht durch Felsen und Bootswaende hindurch.</summary>
        public static bool NurSichtbare = true;

        private static global::Creature _ziel;

        /// <summary>Ein direkter Zeiger auf das private Feld PlayerCamera._rot.
        ///
        /// Harmony kann private Felder auch ueber die Namenskonvention hereinreichen,
        /// aber die ist hier eine Falle: Der Praefix sind drei Unterstriche, und der
        /// Feldname faengt selbst mit einem an. Es muessten also VIER sein. Mit dreien
        /// sucht Harmony ein Feld namens "rot", findet keines, und wirft beim Patchen -
        /// wodurch das ganze Modul nicht mehr laedt. Genau das ist passiert.
        ///
        /// Ein FieldRef umgeht das Ratespiel: Er wird beim Laden einmal aufgeloest und
        /// gibt danach eine echte Referenz auf das Feld zurueck, ohne Boxen.</summary>
        private static readonly AccessTools.FieldRef<global::PlayerCamera, Vector3> RotFeld =
            AccessTools.FieldRefAccess<global::PlayerCamera, Vector3>("_rot");

        /// <summary>Das gemerkte Ziel - das stille Zielen benutzt dasselbe.</summary>
        public static global::Creature Ziel => _ziel;

        /// <summary>Das Ziel wird auch dann nachgehalten, wenn nur still gezielt wird und
        /// die Kamera gar nicht gezogen werden soll.</summary>
        private static bool Verfolgen => Aktiv || StillesZielen.Aktiv;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(global::PlayerCamera), "MouseMovement"),
                postfix: new HarmonyMethod(typeof(Zielhilfe), nameof(NachDerMaus)));
        }

        public static void Loslassen()
        {
            _ziel = null;
        }

        public static string ZielName
        {
            get
            {
                if (!Verfolgen) return "aus";
                if (_ziel == null) return "keins";

                var spieler = global::Player.LocalPlayer;
                float weg = spieler == null || spieler.CamObject == null
                    ? 0f
                    : Vector3.Distance(spieler.CamObject.position, Zielort(_ziel));

                return Sauber(_ziel.name) + "   " + Mathf.RoundToInt(weg) + "m";
            }
        }

        /// <summary>Laeuft direkt vor SetCamPosRot, also im selben Bild.</summary>
        private static void NachDerMaus(global::PlayerCamera __instance)
        {
            if (!Verfolgen) { _ziel = null; return; }

            var spieler = global::Player.LocalPlayer;
            if (spieler == null || spieler.Camera != __instance) return;

            var auge = __instance.CamTransform;
            if (auge == null) return;

            Vector3 augenort = auge.position;

            PruefeZiel(augenort);
            _ziel ??= SucheZiel(augenort, auge.forward);
            if (_ziel == null) return;

            // Nur die Kamera ziehen, wenn das auch gewuenscht ist. Beim stillen Zielen
            // bleibt der Blick, wo er ist - dort wird nur das Geschoss gebogen.
            if (Aktiv) Ziehe(ref RotFeld(__instance), augenort);
        }

        /// <summary>Ist das gemerkte Ziel noch brauchbar?</summary>
        private static void PruefeZiel(Vector3 auge)
        {
            if (_ziel == null) return;

            if (!Viecher.Lebt(_ziel)) { _ziel = null; return; }

            Vector3 ort = Zielort(_ziel);

            if ((ort - auge).sqrMagnitude > Reichweite * Reichweite) { _ziel = null; return; }
            if (NurSichtbare && Viecher.Verdeckt(auge, ort)) _ziel = null;
        }

        /// <summary>Nimmt das Vieh, das dem Fadenkreuz am naechsten ist - aber nur, wenn
        /// es innerhalb des Fangwinkels liegt. Dadurch entscheidest du, worauf gezielt
        /// wird, statt dass die Zielhilfe sich etwas aussucht.</summary>
        private static global::Creature SucheZiel(Vector3 auge, Vector3 blick)
        {
            global::Creature bestes = null;
            float besterWinkel = Fangwinkel;

            foreach (var vieh in Viecher.Lebende())
            {
                Vector3 ort = Zielort(vieh);
                Vector3 hin = ort - auge;

                if (hin.sqrMagnitude > Reichweite * Reichweite) continue;

                float winkel = Vector3.Angle(blick, hin);
                if (winkel >= besterWinkel) continue;

                if (NurSichtbare && Viecher.Verdeckt(auge, ort)) continue;

                besterWinkel = winkel;
                bestes = vieh;
            }

            return bestes;
        }

        public static Vector3 Zielort(global::Creature vieh)
        {
            return Zielpunkt == 1 ? Viecher.Kopf(vieh) : Viecher.Koerpermitte(vieh);
        }

        /// <summary>Zieht die Blickrichtung zum Ziel.
        ///
        /// Die Neigung ist in Unity andersherum, als man denkt: Ein positiver Wert auf
        /// der x-Achse schaut nach unten. Deshalb das Minus vor dem Arkussinus.</summary>
        private static void Ziehe(ref Vector3 rot, Vector3 auge)
        {
            Vector3 hin = Zielort(_ziel) - auge;
            if (hin.sqrMagnitude < 0.0001f) return;

            hin.Normalize();

            float drehungSoll = Mathf.Atan2(hin.x, hin.z) * Mathf.Rad2Deg;
            float neigungSoll = -Mathf.Asin(Mathf.Clamp(hin.y, -1f, 1f)) * Mathf.Rad2Deg;

            float anteil = Mathf.Clamp01(Klebekraft);

            // Ueber die Runde: 359 Grad und 1 Grad liegen einen Grad auseinander, nicht
            // 358. Ohne LerpAngle wuerde der Blick beim Ueberlauf den langen Weg nehmen.
            rot.y = Mathf.LerpAngle(rot.y, drehungSoll, anteil);
            rot.x = Mathf.LerpAngle(rot.x, neigungSoll, anteil);

            // Nachklemmen: Der Clamp des Spiels lief eine Zeile vor uns.
            rot.x = Mathf.Clamp(rot.x, -90f, 90f);
        }

        private static string Sauber(string name)
        {
            int marke = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return marke < 0 ? name : name.Substring(0, marke);
        }
    }
}
