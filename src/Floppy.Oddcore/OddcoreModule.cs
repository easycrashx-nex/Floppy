using System;
using System.Collections.Generic;
using System.Linq;
using Floppy.Core;
using Floppy.Core.Api;
using UnityEngine;

namespace Floppy.Oddcore
{
    /// <summary>Cheats für ODDCORE.
    ///
    /// Ein Einzelspieler-Titel - alles hier betrifft nur dich. Die einzige Ausnahme sind
    /// die öffentlichen Bestenlisten, und die schirmt Stealth ab.</summary>
    public class OddcoreModule : IGameModule
    {
        public string ProductName { get { return "ODDCORE"; } }
        public string DisplayName { get { return "ODDCORE"; } }

        public void Initialize()
        {
            Stealth.Apply(Plugin.Harmony);
        }

        public bool IsReady(out string status)
        {
            if (!Game.ImSpiel)
            {
                status = "Warte auf Spielstart";
                return false;
            }

            status = "Bereit";
            return true;
        }

        public void Update()
        {
            Stealth.Tick();
            Loops.Tick();
        }

        public void SetMenuOpen(bool open)
        {
            InputLock.SetMenuOpen(open);
        }

        private static Func<bool> ImSpiel { get { return () => Game.ImSpiel; } }

        /// <summary>Eine reine Anzeigezeile. Der Text wird laufend aus dem Spiel gefüllt.</summary>
        private static CheatOption Anzeige(string id, string label)
        {
            return new CheatOption
            {
                Id = id,
                Label = label,
                Kind = OptionKind.Info,
                TextValue = "-"
            };
        }


        public List<CheatCategory> BuildCategories()
        {
            return new List<CheatCategory>
            {
                Tarnung(), Spieler(), Bewegung(), Waffen(),
                Geld(), Runde(), Gegner(), Statistik()
            };
        }

        // ---------------------------------------------------------------- Tarnung

        private CheatCategory Tarnung()
        {
            var k = new CheatCategory("Tarnung");

            k.Add(new CheatOption
            {
                Id = "stealth.hide",
                Label = "Cheat-Erkennung verbergen",
                Description = "ODDCORE merkt sich, ob geschummelt wurde. Dieser Schalter setzt " +
                              "die Markierung laufend zurück, dein Spielstand bleibt unauffällig.",
                Kind = OptionKind.Toggle,
                BoolValue = true,
                Ruhebool = true,
                OnChanged = o => { Loops.Benutzt(o.Id); Stealth.VerbergeCheats = o.BoolValue; }
            });

            k.Add(new CheatOption
            {
                Id = "stealth.noscore",
                Label = "Punktzahlen zurückhalten",
                Description = "Schickt keine Ergebnisse mehr an die Steam-Bestenliste. Ohne das " +
                              "könnten erfundene Punktzahlen die Platzierung anderer Spieler " +
                              "verdrängen - deshalb ist es voreingestellt an.",
                Kind = OptionKind.Toggle,
                BoolValue = true,
                Ruhebool = true,
                OnChanged = o => { Loops.Benutzt(o.Id); Stealth.HaltePunkteZurueck = o.BoolValue; }
            });

            k.Add(new CheatOption
            {
                Id = "stealth.reset",
                Label = "Markierung jetzt zurücksetzen",
                Kind = OptionKind.Button,
                IsAvailable = ImSpiel,
                OnInvoke = o => { string m; Stealth.Zuruecksetzen(out m); o.Message = m; }
            });

            return k;
        }

        // ---------------------------------------------------------------- Spieler

        private CheatCategory Spieler()
        {
            var k = new CheatCategory("Spieler");

            k.Add(Anzeige("player.info", "Aktueller Stand"));

            k.Add(new CheatOption
            {
                Id = "player.godmode",
                Label = "Unverwundbar",
                Description = "Das Spiel bringt dafür einen eigenen Schalter mit - wir legen ihn um " +
                              "und setzen ihn nach jedem Szenenwechsel neu.",
                Kind = OptionKind.Toggle,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Unverwundbar = o.BoolValue; Game.SetzeUnverwundbar(o.BoolValue); }
            });

            k.Add(new CheatOption
            {
                Id = "player.energy.keep",
                Label = "Energie immer voll",
                Description = "Füllt die Energieleiste laufend wieder auf.",
                Kind = OptionKind.Toggle,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Loops.EnergieHalten = o.BoolValue; }
            });

            k.Add(new CheatOption
            {
                Id = "player.energy.fill",
                Label = "Energie jetzt auffüllen",
                Kind = OptionKind.Button,
                IsAvailable = ImSpiel,
                OnInvoke = o => { Game.FuelleEnergie(); o.Message = "Energie aufgefüllt"; }
            });

            k.Add(new CheatOption
            {
                Id = "player.energy.max",
                Label = "Maximale Energie",
                Kind = OptionKind.Slider,
                Min = 10f, Max = 1000f, Step = 10f, NumberValue = 100f,
                IsAvailable = ImSpiel,
                OnChanged = o =>
                {
                    Loops.Benutzt(o.Id);
                    Game.SetzeEnergie("maxEnergy", o.NumberValue);
                    Game.SetzeEnergie("totalMaxEnergyCap", o.NumberValue);
                }
            });

            k.Add(new CheatOption
            {
                Id = "player.health",
                Label = "Leben",
                Kind = OptionKind.Slider,
                Min = 1f, Max = 999f, Step = 1f, NumberValue = 100f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeWert("health", Mathf.RoundToInt(o.NumberValue)); }
            });

            return k;
        }

        // ---------------------------------------------------------------- Bewegung

        private CheatCategory Bewegung()
        {
            var k = new CheatCategory("Bewegung");

            k.Add(new CheatOption
            {
                Id = "move.speed",
                Label = "Laufgeschwindigkeit",
                Kind = OptionKind.Slider,
                Min = 1f, Max = 40f, Step = 0.5f, NumberValue = 5f,
                IsAvailable = ImSpiel,
                OnChanged = o =>
                {
                    Loops.Benutzt(o.Id);
                    Loops.Tempo = o.NumberValue;
                    Game.SetzeBewegung("MoveSpeed", o.NumberValue);
                    Game.SetzeBewegung("baseMoveSpeed", o.NumberValue);
                }
            });

            k.Add(new CheatOption
            {
                Id = "move.sprint",
                Label = "Sprintgeschwindigkeit",
                Kind = OptionKind.Slider,
                Min = 1f, Max = 60f, Step = 0.5f, NumberValue = 8f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeBewegung("SprintSpeed", o.NumberValue); }
            });

            k.Add(new CheatOption
            {
                Id = "move.jump",
                Label = "Sprunghöhe",
                Kind = OptionKind.Slider,
                Min = 0.5f, Max = 20f, Step = 0.5f, NumberValue = 2f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeBewegung("JumpHeight", o.NumberValue); }
            });

            k.Add(new CheatOption
            {
                Id = "move.jumps",
                Label = "Sprünge in der Luft",
                Description = "Wie oft du springen kannst, ohne den Boden zu berühren.",
                Kind = OptionKind.Slider,
                Min = 1f, Max = 20f, Step = 1f, NumberValue = 1f,
                IsAvailable = ImSpiel,
                OnChanged = o =>
                {
                    Loops.Benutzt(o.Id);
                    Loops.Spruenge = Mathf.RoundToInt(o.NumberValue);
                    Game.SetzeSpruenge(Loops.Spruenge);
                }
            });

            k.Add(new CheatOption
            {
                Id = "move.gravity",
                Label = "Schwerkraft",
                Description = "Kleinere Werte lassen dich schweben. Negativ ist nicht empfehlenswert.",
                Kind = OptionKind.Slider,
                Min = -30f, Max = 0f, Step = 0.5f, NumberValue = -15f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeBewegung("Gravity", o.NumberValue); }
            });

            return k;
        }

        // ---------------------------------------------------------------- Waffen

        private CheatCategory Waffen()
        {
            var k = new CheatCategory("Waffen");

            k.Add(new CheatOption
            {
                Id = "weapon.firerate",
                Label = "Feuerrate",
                Description = "Multiplikator des Spiels für die Schussfolge.",
                Kind = OptionKind.Slider,
                Min = 0.25f, Max = 10f, Step = 0.25f, NumberValue = 1f,
                Ruhewert = 1f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Feuerrate = o.NumberValue; Game.SetzeFeuerrate(o.NumberValue); }
            });

            k.Add(new CheatOption
            {
                Id = "weapon.damage",
                Label = "Schaden an Gegnern",
                Description = "Zusätzlicher Multiplikator auf alles, was du triffst.",
                Kind = OptionKind.Slider,
                Min = 1f, Max = 50f, Step = 0.5f, NumberValue = 1f,
                Ruhewert = 1f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Loops.SchadenMulti = o.NumberValue; }
            });

            k.Add(new CheatOption
            {
                Id = "weapon.bossdamage",
                Label = "Zusatzschaden an Bossen",
                Kind = OptionKind.Slider,
                Min = 0f, Max = 100f, Step = 1f, NumberValue = 0f,
                Ruhewert = 0f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Loops.BossSchaden = o.NumberValue; }
            });

            return k;
        }

        // ---------------------------------------------------------------- Geld

        private CheatCategory Geld()
        {
            var k = new CheatCategory("Geld");

            k.Add(Anzeige("money.info", "Aktueller Stand"));

            k.Add(new CheatOption
            {
                Id = "money.amount",
                Label = "Betrag",
                Description = "Wird von den Knöpfen darunter benutzt. Der Höchstbetrag " +
                              "wird bei Bedarf automatisch mit angehoben - ODDCORE deckelt das Geld sonst.",
                Kind = OptionKind.Number,
                Min = 0f, Max = 9999999f, NumberValue = 10000f
            });

            k.Add(new CheatOption
            {
                Id = "money.add",
                Label = "Geld hinzufügen",
                Kind = OptionKind.Button,
                IsAvailable = ImSpiel,
                OnInvoke = o => { Game.GibGeld(Zahl("money.amount")); o.Message = "Neuer Stand: " + Game.Geld; }
            });

            k.Add(new CheatOption
            {
                Id = "money.set",
                Label = "Geld genau setzen",
                Kind = OptionKind.Button,
                IsAvailable = ImSpiel,
                OnInvoke = o => { Game.SetzeGeld(Zahl("money.amount")); o.Message = "Neuer Stand: " + Game.Geld; }
            });

            k.Add(new CheatOption
            {
                Id = "money.keep",
                Label = "Geld auffüllen",
                Description = "Hält den Betrag dauerhaft auf dem eingestellten Wert.",
                Kind = OptionKind.Toggle,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Loops.GeldHalten = o.BoolValue; }
            });

            k.Add(new CheatOption
            {
                Id = "money.max",
                Label = "Höchstbetrag",
                Kind = OptionKind.Number,
                Min = 0f, Max = 9999999f, NumberValue = 999999f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeWert("maxMoney", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "money.tokens",
                Label = "Marken",
                Kind = OptionKind.Number,
                Min = 0f, Max = 99999f, NumberValue = 100f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeWert("tokens", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "money.tickets",
                Label = "Tickets",
                Kind = OptionKind.Number,
                Min = 0f, Max = 99999f, NumberValue = 100f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeWert("tickets", Mathf.RoundToInt(o.NumberValue)); }
            });

            return k;
        }

        // ---------------------------------------------------------------- Runde

        private CheatCategory Runde()
        {
            var k = new CheatCategory("Runde");

            k.Add(Anzeige("round.info.wave", "Aktuelle Welle"));
            k.Add(Anzeige("round.info.time", "Zeit"));
            k.Add(Anzeige("round.info.enemies", "Gegner"));
            k.Add(Anzeige("round.info.boss", "Boss"));
            k.Add(Anzeige("round.info.shop", "Laden"));

            k.Add(new CheatOption
            {
                Id = "round.addtime",
                Label = "+60 Sekunden",
                Kind = OptionKind.Button,
                IsAvailable = ImSpiel,
                OnInvoke = o =>
                {
                    var r = Game.Runde;
                    if (r == null) { o.Message = "Keine Runde"; return; }
                    if (r.maxTime < r.curTimer + 60f) r.maxTime = r.curTimer + 60f;
                    r.curTimer += 60f;
                    o.Message = "Zeit jetzt " + Mathf.RoundToInt(r.curTimer) + "s";
                }
            });

            k.Add(new CheatOption
            {
                Id = "round.filltime",
                Label = "Zeit auf Maximum",
                Kind = OptionKind.Button,
                IsAvailable = ImSpiel,
                OnInvoke = o =>
                {
                    var r = Game.Runde;
                    if (r == null) { o.Message = "Keine Runde"; return; }
                    r.curTimer = r.maxTime;
                    o.Message = "Aufgefüllt auf " + Mathf.RoundToInt(r.maxTime) + "s";
                }
            });

            k.Add(new CheatOption
            {
                Id = "round.nextwave",
                Label = "Welle hochzählen",
                Description = "Setzt nur den Zähler - die Runde selbst läuft normal weiter.",
                Kind = OptionKind.Button,
                IsAvailable = ImSpiel,
                OnInvoke = o =>
                {
                    var r = Game.Runde;
                    if (r == null) { o.Message = "Keine Runde"; return; }
                    r.curWave += 1;
                    o.Message = "Jetzt Welle " + r.curWave;
                }
            });

            k.Add(new CheatOption
            {
                Id = "round.timefreeze",
                Label = "Zeit anhalten",
                Description = "Der Rundentimer läuft nicht mehr weiter.",
                Kind = OptionKind.Toggle,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.ZeitAnhalten = o.BoolValue; Game.SetzeRundeSchalter("pauseTimer", o.BoolValue); }
            });

            k.Add(new CheatOption
            {
                Id = "round.slowtime",
                Label = "Zeit läuft langsam",
                Kind = OptionKind.Toggle,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeSchalter("permaTimeSlow", o.BoolValue); }
            });

            k.Add(new CheatOption
            {
                Id = "round.drain",
                Label = "Zeitverbrauch",
                Description = "Wie schnell die Zeit abläuft. 0 = gar nicht.",
                Kind = OptionKind.Slider,
                Min = 0f, Max = 5f, Step = 0.1f, NumberValue = 1f,
                Ruhewert = 1f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("timeDrainRate", o.NumberValue); }
            });

            k.Add(new CheatOption
            {
                Id = "round.maxtime",
                Label = "Höchstzeit",
                Kind = OptionKind.Slider,
                Min = 10f, Max = 3600f, Step = 10f, NumberValue = 300f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("maxTime", o.NumberValue); }
            });

            k.Add(new CheatOption
            {
                Id = "round.time",
                Label = "Verbleibende Zeit",
                Kind = OptionKind.Slider,
                Min = 0f, Max = 3600f, Step = 10f, NumberValue = 300f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("curTimer", o.NumberValue); }
            });

            k.Add(new CheatOption
            {
                Id = "round.wave",
                Label = "Welle",
                Description = "Die aktuelle Wellennummer. Wirkt ab der nächsten Welle.",
                Kind = OptionKind.Number,
                Min = 0f, Max = 9999f, NumberValue = 1f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("curWave", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "round.freeshop",
                Label = "Kostenlose Läden",
                Kind = OptionKind.Number,
                Min = 0f, Max = 999f, NumberValue = 5f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("freeShopCount", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "round.shopchance",
                Label = "Ladenchance",
                Kind = OptionKind.Slider,
                Min = 0f, Max = 100f, Step = 1f, NumberValue = 20f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("shopChance", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "round.frenzy",
                Label = "Rausch-Chance",
                Kind = OptionKind.Slider,
                Min = 0f, Max = 1f, Step = 0.05f, NumberValue = 0.1f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("frenzyChance", o.NumberValue); }
            });

            k.Add(new CheatOption
            {
                Id = "round.tokens",
                Label = "Marken pro Runde",
                Kind = OptionKind.Number,
                Min = 0f, Max = 999f, NumberValue = 10f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("tokensToGive", Mathf.RoundToInt(o.NumberValue)); }
            });

            return k;
        }

        // ---------------------------------------------------------------- Gegner

        private CheatCategory Gegner()
        {
            var k = new CheatCategory("Gegner");

            k.Add(new CheatOption
            {
                Id = "enemy.next",
                Label = "Gegner in der nächsten Welle",
                Kind = OptionKind.Number,
                Min = 0f, Max = 9999f, NumberValue = 5f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("nextWaveEnemies", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "enemy.add",
                Label = "Zuwachs pro Welle",
                Kind = OptionKind.Number,
                Min = 0f, Max = 999f, NumberValue = 1f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeRundeZahl("nextWaveAddEnemies", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "enemy.bossdown",
                Label = "Boss als besiegt eintragen",
                Kind = OptionKind.Button,
                IsAvailable = ImSpiel,
                OnInvoke = o => { Game.SetzeRundeSchalter("hasKilledBoss", true); o.Message = "Eingetragen"; }
            });

            return k;
        }

        // ---------------------------------------------------------------- Statistik

        private CheatCategory Statistik()
        {
            var k = new CheatCategory("Statistik");

            k.Add(new CheatOption
            {
                Id = "stat.coins",
                Label = "Münzen gesamt",
                Kind = OptionKind.Number,
                Min = 0f, Max = 9999999f, NumberValue = 0f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeWert("totalCoins", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "stat.kills",
                Label = "Gegner getötet",
                Kind = OptionKind.Number,
                Min = 0f, Max = 9999999f, NumberValue = 0f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeWert("totalEnemiesKilled", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "stat.wins",
                Label = "Siege",
                Kind = OptionKind.Number,
                Min = 0f, Max = 99999f, NumberValue = 0f,
                IsAvailable = ImSpiel,
                OnChanged = o => { Loops.Benutzt(o.Id); Game.SetzeWert("totalWins", Mathf.RoundToInt(o.NumberValue)); }
            });

            k.Add(new CheatOption
            {
                Id = "stat.read",
                Label = "Aktuelle Werte anzeigen",
                Kind = OptionKind.Button,
                IsAvailable = ImSpiel,
                OnInvoke = o => o.Message = "Geld " + Game.Geld +
                                            " | Marken " + Game.LiesWert("tokens") +
                                            " | Kills " + Game.LiesWert("totalEnemiesKilled")
            });

            return k;
        }

        // ----------------------------------------------------------------

        private static int Zahl(string id)
        {
            var o = Registry.Find(id);
            return o == null ? 0 : Mathf.RoundToInt(o.NumberValue);
        }
    }
}
