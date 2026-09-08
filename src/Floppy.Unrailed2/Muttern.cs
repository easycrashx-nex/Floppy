using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Floppy.Core;
using Floppy.Core.Api;

namespace Floppy.Unrailed2
{
    /// <summary>Der echte Teambestand, unabhängig vom optionalen CheatSingleton.</summary>
    internal static class Muttern
    {
        private const string TeamTyp = "1077059188";
        private const string TeamId = "1702275985";
        private const string Bolts = "437256907";

        private sealed class Team
        {
            public long Eid;
            public int Id;
            public int Bestand;
        }

        private static List<Team> _teams = new List<Team>();
        private static Team _wahl;
        private static string _welt;

        public static bool Verfuegbar => Debugger.Erreichbar && Welt.ImSpiel && _welt == Welt.Kennung && _wahl != null;

        public static void Vergiss()
        {
            _teams.Clear();
            _wahl = null;
            _welt = null;
        }

        private static List<Team> LiesTeams()
        {
            var teams = new List<Team>();
            if (!Welt.ImSpiel || !Debugger.Erreichbar) return teams;
            foreach (var gruppe in Welt.Felder(TeamTyp).GroupBy(f => f.Entitaet))
            {
                var ids = gruppe.Where(f => f.Schluessel == TeamId).ToArray();
                var bestandsfelder = gruppe.Where(f => f.Schluessel == Bolts).ToArray();
                if (ids.Length != 1 || bestandsfelder.Length != 1 || gruppe.Key < 0
                    || !int.TryParse(ids[0].Wert, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0
                    || !int.TryParse(bestandsfelder[0].Wert, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bestand) || bestand < 0)
                    return new List<Team>();
                teams.Add(new Team { Eid = gruppe.Key, Id = id, Bestand = bestand });
            }
            return teams.Select(t => t.Id).Distinct().Count() == teams.Count
                ? teams.OrderBy(t => t.Id).ToList() : new List<Team>();
        }

        public static void Aktualisiere()
        {
            if (_welt != Welt.Kennung) _wahl = null;
            _welt = Welt.Kennung;
            _teams = LiesTeams();
            _wahl = _teams.FirstOrDefault(t => t.Eid == _wahl?.Eid && t.Id == _wahl?.Id)
                ?? (_teams.Count == 1 ? _teams[0] : null);
            var option = Registry.Find("mut.team");
            if (option != null)
            {
                option.Choices = new[] { "(Team wählen)" }.Concat(_teams.Select(t => "Team " + ((long)t.Id + 1))).ToArray();
                option.ChoiceIndex = _wahl == null ? 0 : _teams.IndexOf(_wahl) + 1;
            }
        }

        private static void Gib(CheatOption option)
        {
            double menge = Registry.Find("mut.menge")?.NumberValue ?? 0;
            if (double.IsNaN(menge) || double.IsInfinity(menge) || menge < 1 || menge > 100000 || menge != Math.Truncate(menge))
            { option.Fail("Bitte eine ganze Anzahl zwischen 1 und 100000 eingeben"); return; }
            var gewaehlt = _wahl;
            string welt = _welt;
            if (gewaehlt == null) { option.Fail("Bitte zuerst ein Team auswählen"); return; }

            Welt.Aktualisiere();
            if (!Debugger.Erreichbar || !Welt.ImSpiel || welt != Welt.Kennung)
            { Aktualisiere(); option.Fail("Die Runde hat gewechselt; Team erneut prüfen"); return; }
            if (!Schutz.Erlaubt(out string grund)) { option.Fail(grund); return; }
            var team = LiesTeams().FirstOrDefault(t => t.Id == gewaehlt.Id && t.Eid == gewaehlt.Eid);
            Welt.Aktualisiere();
            if (team == null || !Debugger.Erreichbar || !Welt.ImSpiel || welt != Welt.Kennung)
            { Aktualisiere(); option.Fail("Das ausgewählte Team ist nicht mehr verfügbar"); return; }
            if (!Schutz.Erlaubt(out grund)) { option.Fail(grund); return; }
            long ziel = (long)team.Bestand + (long)menge;
            if (ziel > int.MaxValue) { option.Fail("Der Mutternbestand wäre zu hoch"); return; }

            bool bestaetigt = Welt.Setze(team.Eid, TeamTyp, Bolts, ziel.ToString(CultureInfo.InvariantCulture), leseVersuche: 4);
            Welt.Aktualisiere();
            bestaetigt &= Debugger.Erreichbar && Welt.ImSpiel && welt == Welt.Kennung;
            Aktualisiere();
            Registry.Find("mut.jetzt")?.NotifyChanged();
            if (!bestaetigt)
            { option.Fail("Mutternänderung nicht bestätigt. Bestand prüfen, bevor du erneut klickst."); return; }
            option.Message = "+" + menge.ToString(CultureInfo.InvariantCulture) + " Muttern für Team " + ((long)team.Id + 1)
                + " (Bestand: " + ziel + ")";
        }

        public static CheatCategory Kategorie()
        {
            var k = new CheatCategory("Muttern");
            k.Add(new CheatOption
            {
                Id = "mut.team", Label = "Team", Kind = OptionKind.Choice, Scope = CheatScope.Everyone,
                Choices = new[] { "(Team wählen)" }, IsAvailable = () => Debugger.Erreichbar && Welt.ImSpiel && _teams.Count > 0,
                OnChanged = o => _wahl = o.ChoiceIndex > 0 && o.ChoiceIndex <= _teams.Count ? _teams[o.ChoiceIndex - 1] : null
            });
            k.Add(new CheatOption
            {
                Id = "mut.jetzt", Label = "Bestand", Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Verfuegbar ? _wahl.Bestand.ToString(CultureInfo.InvariantCulture)
                    : Welt.ImSpiel ? "Kein Team ausgewählt oder verfügbar" : "Noch keine laufende Partie"
            });
            k.Add(new CheatOption
            {
                Id = "mut.menge", Label = "Wie viele", Kind = OptionKind.Number, Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar, Min = 1, Max = 100000, Step = 1, NumberValue = 100, Ruhewert = 100
            });
            k.Add(new CheatOption
            {
                Id = "mut.geben", Label = "Muttern geben", Kind = OptionKind.Button, Scope = CheatScope.Everyone,
                Description = "Addiert die Anzahl zum aktuellen Mutternbestand des ausgewählten Teams.",
                IsAvailable = () => Verfuegbar, OnInvoke = Gib
            });
            return k;
        }
    }
}
