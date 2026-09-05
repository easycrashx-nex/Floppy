using System.Collections.Generic;
using System.Reflection;
using Floppy.Core;
using HarmonyLib;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Waffenverhalten: Rückstoß, Streuung, Feuerrate, Schaden, Munition.
    ///
    /// Die interessanten Werte sind private Felder auf der Waffe, die aber alle innerhalb von
    /// Shoot() benutzt werden. Wir biegen sie deshalb kurz vor dem Schuss zurecht und stellen
    /// danach das Original wieder her - so bleibt der Spielstand unangetastet und jede Waffe
    /// im Spiel ist automatisch abgedeckt.</summary>
    internal static class WeaponPatches
    {
        // 1 = unverändert. 0 beim Rückstoß = gar keiner.
        public static float RecoilMultiplier = 1f;
        public static float SpreadMultiplier = 1f;
        public static float FireRateMultiplier = 1f;
        public static float DamageMultiplier = 1f;
        public static float ProjectileMultiplier = 1f;
        public static bool InfiniteAmmo;

        /// <summary>Zuletzt abgefeuerte Waffe - für Cheats, die eine konkrete Waffe brauchen.</summary>
        public static global::Weapon LastWeapon;

        private static readonly FieldInfo SpreadField = AccessTools.Field(typeof(global::Weapon), "_spread");
        private static readonly FieldInfo TimeBetweenShotsField = AccessTools.Field(typeof(global::Weapon), "_timeBetweenShots");
        private static readonly FieldInfo ProjectileCountField = AccessTools.Field(typeof(global::Weapon), "_projectileCountPerShot");
        private static readonly FieldInfo QueueReloadField = AccessTools.Field(typeof(global::Weapon), "_queueReload");
        private static readonly PropertyInfo AmmoProperty = AccessTools.Property(typeof(global::Weapon), "Ammo");
        private static readonly FieldInfo HasCoolDownField = AccessTools.Field(typeof(global::Weapon), "_hasCoolDown");

        /// <summary>Die Werte, wie die Waffe sie ohne uns hätte.</summary>
        private struct Original
        {
            public float Spread;
            public float TimeBetweenShots;
            public int ProjectileCount;
        }

        /// <summary>Einmal je Waffe gemerkt - und nie aus dem aktuellen Feld abgeleitet.
        ///
        /// Genau daran ist es vorher gescheitert: Wurde der veränderte Wert beim nächsten
        /// Schuss als "Original" gelesen, schaukelte er sich von Schuss zu Schuss auf, bis
        /// die Waffe ohne Pause feuerte - und Zurückstellen half nicht mehr, weil der echte
        /// Ausgangswert längst verloren war.</summary>
        private static readonly Dictionary<global::Weapon, Original> _originals =
            new Dictionary<global::Weapon, Original>();

        private static Original GetOriginal(global::Weapon weapon)
        {
            if (_originals.TryGetValue(weapon, out var original)) return original;

            original = new Original
            {
                Spread = (float)SpreadField.GetValue(weapon),
                TimeBetweenShots = (float)TimeBetweenShotsField.GetValue(weapon),
                ProjectileCount = (int)ProjectileCountField.GetValue(weapon)
            };

            // Zerstörte Waffen ab und zu aussortieren, damit die Liste nicht wächst.
            if (_originals.Count > 32)
            {
                var tot = new List<global::Weapon>();
                foreach (var eintrag in _originals)
                    if (eintrag.Key == null) tot.Add(eintrag.Key);
                foreach (var w in tot) _originals.Remove(w);
            }

            _originals[weapon] = original;
            return original;
        }

        /// <summary>Die tatsächliche Pause zwischen zwei Schüssen bei aktueller Einstellung.</summary>
        private static float Abklingzeit(Original original)
        {
            return original.TimeBetweenShots / Mathf.Max(0.05f, FireRateMultiplier);
        }

        /// <summary>Rückstoß-Knockback nur während des Schusses dämpfen - Explosionen
        /// und andere Stöße sollen sich normal anfühlen.</summary>
        private static bool _inShoot;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(global::Weapon), "Shoot"),
                prefix: new HarmonyMethod(typeof(WeaponPatches), nameof(BeforeShoot)),
                postfix: new HarmonyMethod(typeof(WeaponPatches), nameof(AfterShoot)));

            harmony.Patch(
                original: AccessTools.Method(typeof(global::PlayerCamera), "Recoil"),
                prefix: new HarmonyMethod(typeof(WeaponPatches), nameof(BeforeScreenRecoil)));

            harmony.Patch(
                original: AccessTools.Method(typeof(global::PlayerToolMovement), "Recoil"),
                prefix: new HarmonyMethod(typeof(WeaponPatches), nameof(BeforeToolRecoil)));

            harmony.Patch(
                original: AccessTools.Method(typeof(global::Weapon), "AddModelRecoil"),
                prefix: new HarmonyMethod(typeof(WeaponPatches), nameof(BeforeModelRecoil)));

            harmony.Patch(
                original: AccessTools.Method(typeof(global::PlayerMovement), "Knockback"),
                prefix: new HarmonyMethod(typeof(WeaponPatches), nameof(BeforeKnockback)));

            harmony.Patch(
                original: AccessTools.PropertyGetter(typeof(global::Attachments), "Damage"),
                postfix: new HarmonyMethod(typeof(WeaponPatches), nameof(AfterGetDamage)));
        }

        // --- Rund um den Schuss ---

        private static void BeforeShoot(global::Weapon __instance)
        {
            // Schüsse der Mitspieler laufen auf deren Rechnern, nicht hier. Diese Cheats
            // sind deshalb immer nur für dich - teilen ließe sich hier nichts.
            if (!Ownership.IsMine(__instance)) return;

            LastWeapon = __instance;
            _inShoot = true;

            var original = GetOriginal(__instance);

            SpreadField.SetValue(__instance, original.Spread * Mathf.Max(0f, SpreadMultiplier));
            TimeBetweenShotsField.SetValue(__instance, Abklingzeit(original));
            ProjectileCountField.SetValue(__instance,
                Mathf.Max(1, Mathf.RoundToInt(original.ProjectileCount * ProjectileMultiplier)));
        }

        private static void AfterShoot(global::Weapon __instance)
        {
            if (!Ownership.IsMine(__instance)) return;

            _inShoot = false;
            var original = GetOriginal(__instance);

            if (InfiniteAmmo) RefillAmmo(__instance, original);

            // Immer zurückstellen - auch bei unendlicher Munition. Bleibt hier ein
            // veränderter Wert stehen, wächst er mit jedem weiteren Schuss.
            SpreadField.SetValue(__instance, original.Spread);
            TimeBetweenShotsField.SetValue(__instance, original.TimeBetweenShots);
            ProjectileCountField.SetValue(__instance, original.ProjectileCount);
        }

        /// <summary>Magazin sofort wieder voll - dadurch entfällt auch das Nachladen.</summary>
        private static void RefillAmmo(global::Weapon weapon, Original original)
        {
            var attachments = weapon.Attachments;
            if (attachments == null) return;

            bool wollteNachladen = QueueReloadField != null
                                   && (bool)QueueReloadField.GetValue(weapon);

            AmmoProperty?.SetValue(weapon, attachments.AmmoPerMag);
            QueueReloadField?.SetValue(weapon, false);

            // Beim letzten Schuss eines Magazins setzt das Spiel bewusst keine Abklingzeit -
            // es rechnet fest damit, dass jetzt nachgeladen wird. Da wir das überspringen,
            // müssen wir die Pause selbst setzen. Sonst feuert die Waffe genau an dieser
            // Stelle ohne jede Verzögerung.
            if (wollteNachladen && HasCoolDownField != null)
            {
                HasCoolDownField.SetValue(weapon, true);
                weapon.Invoke("CoolDown", Abklingzeit(original));
            }
        }

        // --- Rückstoß ---

        private static void BeforeScreenRecoil(ref Vector2 recoil)
        {
            recoil *= Mathf.Max(0f, RecoilMultiplier);
        }

        private static void BeforeToolRecoil(ref Vector2 recoil)
        {
            recoil *= Mathf.Max(0f, RecoilMultiplier);
        }

        private static void BeforeModelRecoil(ref float recoilDir)
        {
            recoilDir *= Mathf.Max(0f, RecoilMultiplier);
        }

        private static void BeforeKnockback(ref Vector3 force)
        {
            if (_inShoot)
                force *= Mathf.Max(0f, RecoilMultiplier);
        }

        // --- Schaden ---

        private static void AfterGetDamage(global::Attachments __instance, ref int __result)
        {
            if (Mathf.Approximately(DamageMultiplier, 1f)) return;
            if (!Ownership.IsMine(__instance?.Weapon)) return; // nur mein Schaden
            __result = Mathf.Max(1, Mathf.RoundToInt(__result * DamageMultiplier));
        }

        // --- Aufsätze ---

        /// <summary>Setzt an der gehaltenen Waffe alle Aufsätze auf die beste Stufe.</summary>
        public static bool UpgradeHeldWeapon(out string message)
        {
            var weapon = HeldWeapon() ?? LastWeapon;
            if (weapon == null)
            {
                message = "Keine Waffe in der Hand";
                return false;
            }

            var attachments = weapon.Attachments;
            if (attachments == null || !attachments.IsServerInitialized)
            {
                message = "Waffe lässt sich gerade nicht verändern";
                return false;
            }

            attachments._syncedExtendedMag.Value = true;
            attachments._syncedLaserSight.Value = true;

            // Die Listen sind je Waffe unterschiedlich lang - der letzte Eintrag ist der beste.
            attachments._syncedSight.Value = LastIndex(attachments, "_sights");
            attachments._syncedBarrelAttachment.Value = LastIndex(attachments, "_barrelAttachments");
            attachments._syncedBulletIndex.Value = LastIndex(attachments, "_bulletUpgrades");

            message = "Aufsätze gesetzt";
            return true;
        }

        private static byte LastIndex(global::Attachments attachments, string fieldName)
        {
            var field = AccessTools.Field(typeof(global::Attachments), fieldName);
            if (field?.GetValue(attachments) is not System.Array array || array.Length == 0)
                return 0;
            return (byte)(array.Length - 1);
        }

        private static global::Weapon HeldWeapon()
        {
            var player = global::Player.LocalPlayer;
            var item = player?.Holding?.HeldItem;
            return item == null ? null : item.GetComponent<global::Weapon>();
        }
    }
}
