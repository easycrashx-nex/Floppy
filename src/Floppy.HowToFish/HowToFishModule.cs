using System;
using System.Collections.Generic;
using System.Linq;
using Floppy.Core;
using Floppy.Core.Api;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Cheats für "How to Fish".
    ///
    /// Das Spiel bringt bereits ein Entwickler-Cheatsystem mit (DazedCommands), das nur hinter
    /// ClientSettings.CheatsEnabled verriegelt ist. Wir schließen es auf und bauen die
    /// Oberfläche darum - plus ein paar Sachen, die es dort nicht gibt (Tempo, Werte setzen).
    ///
    /// Alles läuft über den Server. Deshalb greifen die Cheats nur, wenn du selbst hostest
    /// oder allein spielst - in einer fremden Lobby passiert schlicht nichts.</summary>
    public partial class HowToFishModule : IGameModule
    {
        public string ProductName => "How to Fish";
        public string DisplayName => "How to Fish";

        public void Initialize()
        {
            // Jede Gruppe einzeln absichern.
            //
            // Vorher stand hier eine schlichte Aufzaehlung, und das ist einmal teuer
            // geworden: Ein einziger falsch geschriebener Feldname in der Zielhilfe hat
            // beim Patchen geworfen, Initialize ist durchgefallen, und damit war das
            // ganze Modul weg - alle Cheats, wegen einer Kleinigkeit in einem davon.
            // Ein Spielupdate, das eine Methode umbenennt, haette dieselbe Wirkung.
            //
            // Jetzt faellt nur die betroffene Gruppe aus. Was sie kaputtgemacht hat,
            // steht im Protokoll, der Rest laeuft weiter.
            Sicher("Grundlagen", () => Patches.Apply(Plugin.Harmony));
            Sicher("Spieler", () => PlayerPatches.Apply(Plugin.Harmony));
            Sicher("Waffen", () => WeaponPatches.Apply(Plugin.Harmony));
            Sicher("Angeln", () => FishingPatches.Apply(Plugin.Harmony));
            Sicher("Geld", () => EconomyPatches.Apply(Plugin.Harmony));
            Sicher("Eingabesperre", () => InputLock.Apply(Plugin.Harmony));
            Sicher("ESP", Esp.Spawn);
            Sicher("Zielhilfe", () => Zielhilfe.Apply(Plugin.Harmony));
            Sicher("Stilles Zielen", () => StillesZielen.Apply(Plugin.Harmony));
        }

        /// <summary>Fuehrt einen Einhaengeschritt aus und ueberlebt sein Scheitern.</summary>
        private static void Sicher(string was, Action tue)
        {
            try
            {
                tue();
            }
            catch (Exception ex)
            {
                Log.Error("Floppy: \"" + was + "\" konnte nicht eingehängt werden - " +
                          ex.Message + " (der Rest läuft weiter)");
            }
        }

        public bool IsReady(out string status)
        {
            if (!Game.InGame)
            {
                status = "Warte auf Spielstart";
                return false;
            }
            // Früher haben wir hier als Gast komplett abgeschaltet. Das war zu
            // vorsichtig: Ein Teil der Netzwerkbefehle nimmt das Spiel von jedem
            // Mitspieler entgegen. Die host-gebundenen Cheats sind einzeln
            // gekennzeichnet und ausgegraut - der Rest geht auch in fremden Runden.
            if (!Game.IsHost)
            {
                status = "Bereit als Gast - alles unter \"Mitspieler\" geht, der Rest braucht den Host";
                return true;
            }

            status = Game.CheatsEnabled ? "Bereit" : "Bereit (Dev-Cheats noch aus)";
            return true;
        }

        public void Update()
        {
            Loops.Tick();
            AktualisiereMitspieler();
            WorldExtras.TickFly();
            InputLock.Tick();
        }

        /// <summary>Menü offen: Maus und Steuerung gehören dem Menü.</summary>
        public void SetMenuOpen(bool open)
        {
            InputLock.SetMenuOpen(open);
        }

        // ------------------------------------------------------------------


        public List<CheatCategory> BuildCategories()
        {
            return new List<CheatCategory>
            {
                General(),
                Fishing(),
                Player(),
                Weapons(),
                Combat(),
                Zielen(),
                Money(),
                Movement(),
                Vision(),
                World(),
                Streiche(),
                Unlocks(),
                Spawning()
            };
        }

        private static Func<bool> HostOnly => () => Game.IsHost;

        /// <summary>Zielhilfe - du suchst das Ziel aus, sie hält es fest.</summary>
        private CheatCategory Zielen()
        {
            var kategorie = new CheatCategory("Zielen");

            kategorie.Add(new CheatOption
            {
                Id = "ziel.info",
                Label = "Aktuelles Ziel",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Zielhilfe.ZielName
            });

            kategorie.Add(new CheatOption
            {
                Id = "ziel.an",
                Label = "Zielhilfe",
                Description = "Nimm ein Vieh kurz ins Fadenkreuz - danach bleibt der Blick daran " +
                              "hängen, bis es tot ist, zu weit weg oder du loslässt.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Zielhilfe.Aktiv = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ziel.kleben",
                Label = "Klebekraft",
                Description = "Wie schnell der Blick nachzieht. Niedrig fühlt sich nach Magnet an, " +
                              "1 ist ein harter Sprung.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 0.05f, Max = 1f, Step = 0.05f,
                NumberValue = 0.35f,
                Ruhewert = 0.35f,
                OnChanged = o => Zielhilfe.Klebekraft = o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ziel.fangwinkel",
                Label = "Fangwinkel",
                Description = "Wie nah am Fadenkreuz ein Vieh sein muss, damit es aufgenommen wird. " +
                              "Klein heißt: du entscheidest genau, groß heißt: es schnappt schneller zu.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 2f, Max = 60f, Step = 2f,
                NumberValue = 12f,
                Ruhewert = 12f,
                OnChanged = o => Zielhilfe.Fangwinkel = o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ziel.reichweite",
                Label = "Reichweite",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 10f, Max = 300f, Step = 10f,
                NumberValue = 120f,
                Ruhewert = 120f,
                OnChanged = o => Zielhilfe.Reichweite = o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ziel.punkt",
                Label = "Zielpunkt",
                Description = "Der Kopf zählt: Das Spiel rechnet den Trefferpunkt in den " +
                              "Körper des Viehs um und schlägt oberhalb der Kopfgrenze den " +
                              "Kopftreffer-Faktor auf. Die Körpermitte trifft dafür " +
                              "zuverlässiger, weil sie größer ist.",
                Kind = OptionKind.Choice,
                Scope = CheatScope.OnlyMe,
                Choices = new[] { "Körpermitte", "Kopf" },
                OnChanged = o => Zielhilfe.Zielpunkt = o.ChoiceIndex
            });

            kategorie.Add(new CheatOption
            {
                Id = "ziel.durchwand",
                Label = "Durch Wände zielen",
                Description = "Normalerweise lässt die Zielhilfe ein Vieh los, sobald ein Fels " +
                              "oder eine Bootswand dazwischen kommt. Hier an, hält sie es fest.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Zielhilfe.NurSichtbare = !o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "still.an",
                Label = "Stilles Zielen",
                Description = "Das Fadenkreuz bleibt stehen, nur das Geschoss biegt ab. Geht " +
                              "auch ohne Zielhilfe - dann wird gar nichts an deinem Blick " +
                              "verändert. Auf Tuchfühlung wirkungslos: Da trifft das Spiel " +
                              "direkt per Strahl, ohne überhaupt ein Geschoss zu werfen.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => StillesZielen.Aktiv = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "still.streuung",
                Label = "Streuung auf einen Punkt",
                Description = "Normalerweise behält eine Schrotgarbe ihre Form und wird nur als " +
                              "Ganzes gedreht. Hier an, landet jede Kugel auf demselben Punkt - " +
                              "wirksamer, fällt aber auf.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => StillesZielen.StreuungBehalten = !o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "still.vorhalten",
                Label = "Nicht vorhalten",
                Description = "Geschosse brauchen Zeit. Normalerweise wird auf die Stelle " +
                              "gezielt, an der das Vieh sein wird - hier an, auf die, wo es " +
                              "gerade ist.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => StillesZielen.Vorhalten = !o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ziel.los",
                Label = "Ziel loslassen",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => { Zielhilfe.Loslassen(); o.Message = "Losgelassen"; }
            });

            return kategorie;
        }

        /// <summary>Welche Rubrik zuletzt in die Gegenstandsliste geschrieben wurde.
        /// -1 erzwingt einen Neuaufbau.</summary>
        private static int _spawnKategorie = -1;

        /// <summary>Füllt die Gegenstandsliste passend zur gewählten Rubrik - aber nur,
        /// wenn sich wirklich etwas geändert hat.</summary>
        private static void AktualisiereSpawnListe()
        {
            if (!Game.InGame) return;

            var pick = Registry.Find("spawn.pick");
            if (pick == null) return;

            var rubrik = Registry.Find("spawn.category");
            int kategorie = rubrik?.ChoiceIndex ?? 0;

            if (kategorie == _spawnKategorie && pick.Choices.Length > 0) return;

            pick.Choices = Game.SpawnableNames(kategorie);
            pick.ChoiceIndex = Mathf.Clamp(pick.ChoiceIndex, 0, Mathf.Max(0, pick.Choices.Length - 1));
            _spawnKategorie = kategorie;
        }

        private CheatCategory General()
        {
            var category = new CheatCategory("Allgemein");

            category.Add(new CheatOption
            {
                Id = "dev.cheats",
                Scope = CheatScope.Everyone,
                Label = "Entwickler-Cheats aktivieren",
                Description = "Schaltet das im Spiel eingebaute Cheatsystem frei. Gibt dir außerdem passiv " +
                              "das Boot, alle Inseln und die Dev-Tasten (M/N Geld, O nächste Insel, T Kamera). " +
                              "Am besten VOR dem Laden einer Runde einschalten.",
                Kind = OptionKind.Toggle,
                OnChanged = o => Game.SetCheatsEnabled(o.BoolValue)
            });

            category.Add(new CheatOption
            {
                Id = "dev.console",
                Scope = CheatScope.Everyone,
                Label = "Befehl",
                Description = "Sendet einen Befehl an das Spiel, ohne Schrägstrich. " +
                              "Zum Beispiel: godmode, addmoney, nextisland, killboss, allskins, finishgame.",
                Kind = OptionKind.Text,
                IsAvailable = HostOnly,
                OnInvoke = o =>
                {
                    string command = (o.TextValue ?? "").Trim().TrimStart('/');
                    if (command.Length == 0) return;
                    Game.RunCommand(command);
                }
            });

            return category;
        }

        private CheatCategory Fishing()
        {
            var category = new CheatCategory("Angeln");

            category.Add(new CheatOption
            {
                Id = "fish.instant",
                Scope = CheatScope.Selectable,
                Label = "Sofort-Biss",
                Description = "Kein Warten mehr. Der Köder muss weiterhin im Wasser hängen - " +
                              "an Land beißt nichts.",
                Kind = OptionKind.Toggle,
                IsAvailable = HostOnly,
                OnChanged = o => FishingPatches.InstantBite = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "fish.mode",
                Ruheauswahl = 0,
                Scope = CheatScope.Selectable,
                Label = "Was anbeißt",
                Description = "Seltenster nimmt den Fang mit der kleinsten Chance, wertvollster den " +
                              "mit dem höchsten Grundwert.",
                Kind = OptionKind.Choice,
                Choices = new[] { "Zufall (normal)", "Immer der seltenste", "Immer der wertvollste" },
                IsAvailable = HostOnly,
                OnChanged = o => FishingPatches.CatchMode = o.ChoiceIndex
            });

            category.Add(new CheatOption
            {
                Id = "fish.forced",
                Ruheauswahl = 0,
                Label = "Bestimmter Fang",
                Description = "Liste füllt sich, sobald du einmal an der Stelle geangelt hast - " +
                              "sie zeigt genau das, was dort beißen kann.",
                Kind = OptionKind.Choice,
                Choices = new[] { "Aus" },
                IsAvailable = () =>
                {
                    var option = Registry.Find("fish.forced");
                    if (option != null)
                    {
                        // Bekannte Fänge nachziehen, sobald das Spiel welche gemeldet hat.
                        var known = FishingPatches.KnownCatches;
                        if (known.Length > 0 && option.Choices.Length != known.Length + 1)
                        {
                            var list = new List<string> { "Aus" };
                            list.AddRange(known);
                            option.Choices = list.ToArray();
                        }
                    }
                    return Game.IsHost;
                },
                OnChanged = o =>
                {
                    FishingPatches.ForcedCatch = o.ChoiceIndex <= 0 || o.ChoiceIndex >= o.Choices.Length
                        ? ""
                        : o.Choices[o.ChoiceIndex];
                }
            });

            category.Add(new CheatOption
            {
                Id = "fish.perfectcook",
                Scope = CheatScope.Selectable,
                Label = "Immer perfekte Garstufe",
                Description = "Alles auf dem Grill rastet sofort auf die wertvollste Garstufe ein " +
                              "und bleibt dort. Anbrennen unmöglich.",
                Kind = OptionKind.Toggle,
                IsAvailable = HostOnly,
                OnChanged = o => EconomyPatches.AlwaysPerfectCookness = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "fish.cookspeed",
                Ruhewert = 1f,
                Scope = CheatScope.Selectable,
                Label = "Grill-Tempo",
                Description = "Wie schnell etwas durchgart.",
                Kind = OptionKind.Slider,
                Min = 0.5f,
                Max = 20f,
                Step = 0.5f,
                NumberValue = 1f,
                OnChanged = o => EconomyPatches.CookSpeedMultiplier = o.NumberValue
            });

            category.Add(new CheatOption
            {
                Id = "fish.weight",
                Label = "Wunschgewicht",
                Description = "Gewicht, das der Knopf darunter setzt. Das Gewicht geht direkt in " +
                              "den Verkaufspreis ein.",
                Kind = OptionKind.Slider,
                Min = 1f,
                Max = 25f,
                Step = 0.5f,
                NumberValue = 5f,
                OnChanged = o => { }
            });

            category.Add(new CheatOption
            {
                Id = "fish.perfectheld",
                Label = "Fang in der Hand aufwerten",
                Description = "Setzt Gewicht und Garstufe auf Bestwerte.",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o =>
                {
                    string message;
                    EconomyPatches.PerfectHeldItem(NumberFrom("fish.weight"), out message);
                    o.Message = message;
                    Plugin.Log.LogInfo(message);
                }
            });

            return category;
        }

        private CheatCategory Movement()
        {
            var category = new CheatCategory("Bewegung");

            category.Add(new CheatOption
            {
                Id = "move.fly",
                Label = "Fliegen",
                Description = "WASD plus Leertaste hoch und Strg runter. Shift beschleunigt. " +
                              "Am besten zusammen mit Gottmodus.",
                Kind = OptionKind.Toggle,
                OnChanged = o => WorldExtras.FlyEnabled = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "move.flyspeed",
                Label = "Flugtempo",
                Kind = OptionKind.Slider,
                Min = 2f,
                Max = 60f,
                Step = 1f,
                NumberValue = 12f,
                OnChanged = o => WorldExtras.FlySpeed = o.NumberValue
            });

            category.Add(new CheatOption
            {
                Id = "move.timescale",
                Ruhewert = 1f,
                Scope = CheatScope.Everyone,
                Label = "Zeittempo",
                Description = "Unter 1 ist Zeitlupe, darüber Zeitraffer. Betrifft das ganze Spiel, " +
                              "also auch deine Mitspieler.",
                Kind = OptionKind.Slider,
                Min = 0.1f,
                Max = 5f,
                Step = 0.1f,
                NumberValue = 1f,
                OnChanged = o =>
                {
                    WorldExtras.TimeScale = o.NumberValue;
                    WorldExtras.ApplyTimeScale();
                }
            });

            category.Add(new CheatOption
            {
                Id = "move.target",
                Label = "Mitspieler",
                Description = "Ziel für den Teleport.",
                Kind = OptionKind.Choice,
                Choices = new string[0],
                IsAvailable = () =>
                {
                    var option = Registry.Find("move.target");
                    if (option != null && Game.InGame)
                    {
                        var names = WorldExtras.OtherPlayerNames();
                        if (option.Choices.Length != names.Length)
                            option.Choices = names;
                    }
                    return Game.InGame;
                }
            });

            category.Add(new CheatOption
            {
                Id = "move.teleport",
                Label = "Zum Mitspieler teleportieren",
                Kind = OptionKind.Button,
                IsAvailable = () => Game.InGame,
                OnInvoke = o =>
                {
                    var target = Registry.Find("move.target");
                    string name = target != null && target.Choices.Length > 0
                        ? target.Choices[Mathf.Clamp(target.ChoiceIndex, 0, target.Choices.Length - 1)]
                        : "";

                    string message;
                    WorldExtras.TeleportToPlayer(name, out message);
                    o.Message = message;
                    Plugin.Log.LogInfo(message);
                }
            });

            category.Add(new CheatOption
            {
                Id = "move.callboat",
                Scope = CheatScope.Everyone,
                Label = "Boot zu mir holen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o =>
                {
                    string message;
                    WorldExtras.CallBoat(out message);
                    o.Message = message;
                    Plugin.Log.LogInfo(message);
                }
            });

            return category;
        }

        private CheatCategory Vision()
        {
            var category = new CheatCategory("Sicht");

            category.Add(new CheatOption
            {
                Id = "esp.creatures",
                Label = "Kreaturen anzeigen",
                Description = "Zeigt Fische und Tiere durch Wasser und Wände, mit Entfernung.",
                Kind = OptionKind.Toggle,
                OnChanged = o => Esp.ShowCreatures = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "esp.players",
                Label = "Mitspieler anzeigen",
                Kind = OptionKind.Toggle,
                OnChanged = o => Esp.ShowPlayers = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "esp.items",
                Label = "Gegenstände anzeigen",
                Description = "Findet verlorenes Zeug wieder. Kostet etwas Leistung, weil dafür " +
                              "die ganze Szene durchsucht wird.",
                Kind = OptionKind.Toggle,
                OnChanged = o => Esp.ShowItems = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "esp.range",
                Label = "Reichweite",
                Description = "In Metern. Weiter entfernte Markierungen werden blasser.",
                Kind = OptionKind.Slider,
                Min = 20f,
                Max = 500f,
                Step = 10f,
                NumberValue = 150f,
                OnChanged = o => Esp.MaxDistance = o.NumberValue
            });

            return category;
        }

        private CheatCategory Player()
        {
            var category = new CheatCategory("Spieler");

            category.Add(new CheatOption
            {
                Id = "player.godmode",
                Scope = CheatScope.Selectable,
                Label = "Gottmodus",
                Description = "Du nimmst keinen Schaden mehr - und nur du. Deine Mitspieler " +
                              "bleiben verwundbar.",
                Kind = OptionKind.Toggle,
                IsAvailable = HostOnly,
                OnChanged = o => PlayerPatches.GodMode = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "player.infinitehealth",
                Scope = CheatScope.Selectable,
                Label = "Leben immer voll",
                Description = "Füllt dein Leben laufend auf 100 auf.",
                Kind = OptionKind.Toggle,
                IsAvailable = HostOnly,
                OnChanged = o => Loops.InfiniteHealth = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "player.nohunger",
                Scope = CheatScope.Selectable,
                Label = "Kein Hunger",
                Description = "Hält die Sättigung oben, damit du nicht verhungerst.",
                Kind = OptionKind.Toggle,
                IsAvailable = HostOnly,
                OnChanged = o => Loops.NoHunger = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "player.cleanse",
                Scope = CheatScope.Selectable,
                Label = "Gift und Feuer entfernen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.Cleanse()
            });

            category.Add(new CheatOption
            {
                Id = "player.heal",
                Scope = CheatScope.Selectable,
                Label = "Voll heilen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.HealLocalPlayer()
            });

            category.Add(new CheatOption
            {
                Id = "player.speed",
                Ruhewert = 1f,
                Label = "Tempo",
                Description = "Multiplikator für Lauf- und Sprinttempo. 1 = normal.",
                Kind = OptionKind.Slider,
                Min = 0.5f,
                Max = 6f,
                Step = 0.1f,
                NumberValue = 1f,
                OnChanged = o => Patches.SpeedMultiplier = Mathf.Max(0.1f, o.NumberValue)
            });

            return category;
        }

        private CheatCategory Weapons()
        {
            var category = new CheatCategory("Waffen");

            category.Add(new CheatOption
            {
                Id = "weapon.recoil",
                Ruhewert = 1f,
                Label = "Rückstoß",
                Description = "0 = gar kein Rückstoß, 1 = normal. Wirkt auf Bildschirm, Waffenmodell " +
                              "und den Schubs nach hinten - nur bei dir, nicht bei den anderen.",
                Kind = OptionKind.Slider,
                Min = 0f,
                Max = 2f,
                Step = 0.05f,
                NumberValue = 1f,
                OnChanged = o => WeaponPatches.RecoilMultiplier = o.NumberValue
            });

            category.Add(new CheatOption
            {
                Id = "weapon.spread",
                Ruhewert = 1f,
                Label = "Streuung",
                Description = "0 = jeder Schuss sitzt genau im Fadenkreuz.",
                Kind = OptionKind.Slider,
                Min = 0f,
                Max = 2f,
                Step = 0.05f,
                NumberValue = 1f,
                OnChanged = o => WeaponPatches.SpreadMultiplier = o.NumberValue
            });

            category.Add(new CheatOption
            {
                Id = "weapon.firerate",
                Ruhewert = 1f,
                Label = "Feuerrate",
                Description = "Höher = schneller. Ab etwa 3 wird die Waffe zur Gießkanne.",
                Kind = OptionKind.Slider,
                Min = 0.25f,
                Max = 8f,
                Step = 0.25f,
                NumberValue = 1f,
                OnChanged = o => WeaponPatches.FireRateMultiplier = o.NumberValue
            });

            category.Add(new CheatOption
            {
                Id = "weapon.damage",
                Ruhewert = 1f,
                Label = "Schaden",
                Description = "Multiplikator auf den Waffenschaden.",
                Kind = OptionKind.Slider,
                Min = 1f,
                Max = 25f,
                Step = 0.5f,
                NumberValue = 1f,
                OnChanged = o => WeaponPatches.DamageMultiplier = o.NumberValue
            });

            category.Add(new CheatOption
            {
                Id = "weapon.projectiles",
                Ruhewert = 1f,
                Label = "Geschosse pro Schuss",
                Description = "Macht aus jeder Waffe eine Schrotflinte.",
                Kind = OptionKind.Slider,
                Min = 1f,
                Max = 12f,
                Step = 1f,
                NumberValue = 1f,
                OnChanged = o => WeaponPatches.ProjectileMultiplier = o.NumberValue
            });

            category.Add(new CheatOption
            {
                Id = "weapon.infiniteammo",
                Label = "Unendlich Munition",
                Description = "Magazin bleibt voll - damit entfällt auch das Nachladen.",
                Kind = OptionKind.Toggle,
                OnChanged = o => WeaponPatches.InfiniteAmmo = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "weapon.upgrade",
                Label = "Alle Aufsätze auf Maximum",
                Description = "Setzt an der Waffe in deiner Hand Visier, Lauf, Munition, " +
                              "erweitertes Magazin und Laser auf die beste Stufe.",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o =>
                {
                    string message;
                    WeaponPatches.UpgradeHeldWeapon(out message);
                    o.Message = message;
                    Plugin.Log.LogInfo(message);
                }
            });

            return category;
        }

        private CheatCategory Combat()
        {
            var category = new CheatCategory("Kampf");

            category.Add(new CheatOption
            {
                Id = "combat.oneshot",
                Scope = CheatScope.Everyone,
                Label = "Ein-Schuss-Tötung",
                Description = "Alles stirbt beim ersten Treffer.",
                Kind = OptionKind.Toggle,
                IsAvailable = HostOnly,
                OnChanged = o => Game.SetOneShot(o.BoolValue)
            });

            category.Add(new CheatOption
            {
                Id = "combat.friendlyfire",
                Ruhebool = true,
                Scope = CheatScope.Everyone,
                Label = "Freundbeschuss",
                Description = "Aus = ihr könnt euch gegenseitig nicht mehr verletzen.",
                Kind = OptionKind.Toggle,
                BoolValue = true,
                IsAvailable = HostOnly,
                OnChanged = o => Game.SetFriendlyFire(o.BoolValue)
            });

            category.Add(new CheatOption
            {
                Id = "combat.difficulty",
                Ruheauswahl = 0,
                Scope = CheatScope.Everyone,
                Label = "Schwierigkeit",
                Kind = OptionKind.Choice,
                Choices = new[] { "Normal", "Leicht", "Schwer" },
                IsAvailable = HostOnly,
                OnChanged = o => Game.SetDifficulty(o.ChoiceIndex)
            });

            category.Add(new CheatOption
            {
                Id = "combat.killboss",
                Scope = CheatScope.Everyone,
                Label = "Boss töten",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("killboss")
            });

            category.Add(new CheatOption
            {
                Id = "combat.killcreatures",
                Scope = CheatScope.Everyone,
                Label = "Alle Kreaturen töten",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("killallcreatures")
            });

            category.Add(new CheatOption
            {
                Id = "combat.resetcreatures",
                Scope = CheatScope.Everyone,
                Label = "Alle Kreaturen zurücksetzen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("resetallcreatures")
            });

            return category;
        }

        private CheatCategory Money()
        {
            var category = new CheatCategory("Geld");

            category.Add(new CheatOption
            {
                Id = "money.amount",
                Scope = CheatScope.Everyone,
                Label = "Betrag",
                Description = "Wird von den Knöpfen darunter benutzt.",
                Kind = OptionKind.Number,
                Min = 0f,
                Max = 9999999f,
                NumberValue = 99999f,
                OnInvoke = o => { }
            });

            category.Add(new CheatOption
            {
                Id = "money.add",
                Scope = CheatScope.Everyone,
                Label = "Geld hinzufügen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.AddMoney(AmountFrom("money.amount"))
            });

            category.Add(new CheatOption
            {
                Id = "money.remove",
                Scope = CheatScope.Everyone,
                Label = "Geld abziehen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RemoveMoney(AmountFrom("money.amount"))
            });

            category.Add(new CheatOption
            {
                Id = "money.set",
                Scope = CheatScope.Everyone,
                Label = "Geld genau setzen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.SetMoney(AmountFrom("money.amount"))
            });

            category.Add(new CheatOption
            {
                Id = "money.freeshopping",
                Scope = CheatScope.Everyone,
                Label = "Kostenlos einkaufen",
                Description = "Alle Käufe im Spiel laufen über dieselbe Prüfung - die sagt " +
                              "dann immer ja, und abgebucht wird auch nichts.",
                Kind = OptionKind.Toggle,
                OnChanged = o => EconomyPatches.FreeShopping = o.BoolValue
            });

            category.Add(new CheatOption
            {
                Id = "money.worth",
                Ruhewert = 1f,
                Scope = CheatScope.Selectable,
                Label = "Verkaufswert",
                Description = "Multiplikator auf den Wert von allem, was du verkaufst.",
                Kind = OptionKind.Slider,
                Min = 1f,
                Max = 50f,
                Step = 0.5f,
                NumberValue = 1f,
                OnChanged = o => EconomyPatches.WorthMultiplier = o.NumberValue
            });

            category.Add(new CheatOption
            {
                Id = "money.roulette",
                Scope = CheatScope.Everyone,
                Label = "Roulette gewinnen lassen",
                Description = "Dreht die laufende Runde auf die Farbe, auf die gesetzt wurde. " +
                              "Es muss ein Einsatz auf dem Tisch liegen.",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o =>
                {
                    string message;
                    WorldExtras.WinRoulette(out message);
                    o.Message = message;
                    Plugin.Log.LogInfo(message);
                }
            });

            category.Add(new CheatOption
            {
                Id = "money.keeprich",
                Scope = CheatScope.Everyone,
                Label = "Geld auffüllen",
                Description = "Hält dein Geld dauerhaft beim eingestellten Betrag.",
                Kind = OptionKind.Toggle,
                IsAvailable = HostOnly,
                OnChanged = o =>
                {
                    Loops.KeepMoney = o.BoolValue;
                    Loops.KeepMoneyAmount = AmountFrom("money.amount");
                }
            });

            return category;
        }

        private CheatCategory World()
        {
            var category = new CheatCategory("Welt");

            category.Add(new CheatOption
            {
                Id = "world.nextisland",
                Scope = CheatScope.Everyone,
                Label = "Nächste Insel",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("nextisland")
            });

            category.Add(new CheatOption
            {
                Id = "world.previsland",
                Scope = CheatScope.Everyone,
                Label = "Vorherige Insel",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("previsland")
            });

            category.Add(new CheatOption
            {
                Id = "world.boat",
                Scope = CheatScope.Everyone,
                Label = "Boot freischalten",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.UnlockBoat()
            });

            category.Add(new CheatOption
            {
                Id = "world.boatradar",
                Scope = CheatScope.Everyone,
                Label = "Boots-Radar freischalten",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.UnlockBoatRadar()
            });

            category.Add(new CheatOption
            {
                Id = "world.grill",
                Scope = CheatScope.Everyone,
                Label = "Grill freischalten",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("grill")
            });

            category.Add(new CheatOption
            {
                Id = "world.finish",
                Scope = CheatScope.Everyone,
                Label = "Spiel beenden (Abspann)",
                Description = "Löst das Spielende aus. Kein Zurück - vorher speichern lassen.",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("finishgame")
            });

            return category;
        }

        private CheatCategory Unlocks()
        {
            var category = new CheatCategory("Freischalten");

            category.Add(new CheatOption
            {
                Id = "unlock.allskins",
                Label = "Alle Skins freischalten",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("allskins")
            });

            category.Add(new CheatOption
            {
                Id = "unlock.noskins",
                Label = "Alle Skins sperren",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("noskins")
            });

            category.Add(new CheatOption
            {
                Id = "unlock.achievements",
                Label = "Alle Erfolge freischalten",
                Description = "Schreibt echte Steam-Erfolge. Ueberleg dir das einmal.",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("unlockachievements")
            });

            category.Add(new CheatOption
            {
                Id = "unlock.lockachievements",
                Label = "Alle Erfolge zurücksetzen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.RunCommand("lockachievements")
            });

            return category;
        }

        private CheatCategory Spawning()
        {
            var category = new CheatCategory("Spawnen");

            category.Add(new CheatOption
            {
                Id = "spawn.category",
                Scope = CheatScope.Everyone,
                Label = "Rubrik",
                Description = "Die Einteilung stammt aus dem Spiel selbst.",
                Kind = OptionKind.Choice,
                Choices = Game.SpawnKategorien,
                IsAvailable = HostOnly,
                OnChanged = o =>
                {
                    // Rubrik gewechselt: Liste beim nächsten Zeichnen neu aufbauen
                    _spawnKategorie = -1;

                    var pick = Registry.Find("spawn.pick");
                    if (pick != null) pick.ChoiceIndex = 0;
                }
            });

            category.Add(new CheatOption
            {
                Id = "spawn.pick",
                Scope = CheatScope.Everyone,
                Label = "Gegenstand",
                Description = "Liste kommt aus dem Spiel und richtet sich nach der Rubrik.",
                Kind = OptionKind.Choice,
                Choices = new string[0],
                IsAvailable = () =>
                {
                    AktualisiereSpawnListe();
                    return Game.IsHost;
                }
            });

            category.Add(new CheatOption
            {
                Id = "spawn.do",
                Scope = CheatScope.Everyone,
                Label = "Ausgewähltes spawnen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.Spawn(SelectedSpawnable(), false, false)
            });

            category.Add(new CheatOption
            {
                Id = "spawn.dead",
                Scope = CheatScope.Everyone,
                Label = "Ausgewähltes tot spawnen",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.Spawn(SelectedSpawnable(), true, false)
            });

            category.Add(new CheatOption
            {
                Id = "spawn.drip",
                Scope = CheatScope.Everyone,
                Label = "Ausgewähltes als Drip spawnen",
                Description = "Die seltene Glitzer-Variante.",
                Kind = OptionKind.Button,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.Spawn(SelectedSpawnable(), false, true)
            });

            category.Add(new CheatOption
            {
                Id = "spawn.byname",
                Scope = CheatScope.Everyone,
                Label = "Nach Name spawnen",
                Description = "Falls etwas nicht in der Liste steht.",
                Kind = OptionKind.Text,
                IsAvailable = HostOnly,
                OnInvoke = o => Game.Spawn((o.TextValue ?? "").Trim(), false, false)
            });

            return category;
        }

        // ------------------------------------------------------------------

        private static float NumberFrom(string optionId)
        {
            var option = Registry.Find(optionId);
            return option == null ? 0f : option.NumberValue;
        }

        private static int AmountFrom(string optionId)
        {
            var option = Registry.Find(optionId);
            return option == null ? 0 : Mathf.RoundToInt(option.NumberValue);
        }

        private static string SelectedSpawnable()
        {
            var option = Registry.Find("spawn.pick");
            if (option == null || option.Choices == null || option.Choices.Length == 0) return "";
            int index = Mathf.Clamp(option.ChoiceIndex, 0, option.Choices.Length - 1);
            return option.Choices[index];
        }
    }
}

