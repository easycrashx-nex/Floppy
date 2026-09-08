using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Floppy.Core;
using Floppy.Core.Api;

internal static class ProfileOrderTests
{
    internal static void Run(Action<bool, string> check)
    {
        DependentChoices(check);
        DependentNumber(check);
    }

    private static void DependentChoices(Action<bool, string> check)
    {
        var callbacks = new List<string>();
        var group = new CheatOption
        {
            Id = "wert.gruppe", Label = "Bereich", Kind = OptionKind.Choice,
            Choices = new[] { "A", "B" }, ChoiceIndex = 1
        };
        var field = new CheatOption
        {
            Id = "wert.feld", Label = "Wert", Kind = OptionKind.Choice,
            Choices = new[] { "B1", "B2" }, ChoiceIndex = 1
        };
        group.OnChanged = _ =>
        {
            callbacks.Add("group");
            // A changed component replaces and reorders the available fields.
            field.Choices = group.ChoiceIndex == 0 ? new[] { "A1", "A2" } : new[] { "B2", "B1" };
            field.ChoiceIndex = 0;
        };
        field.OnChanged = _ => callbacks.Add("field:" + group.Choices[group.ChoiceIndex] + "/" + field.Choices[field.ChoiceIndex]);
        Registry.SetModule(new Fixture(new CheatCategory("Werte") { Options = new List<CheatOption> { group, field } }));
        check(Profile.TrySave("dependent-field-order", out _), "dependent field fixture saves");
        string saved = File.ReadAllText(Path.Combine(Profile.Ordner, "dependent-field-order.json"));
        check(saved.IndexOf("wert.feld", StringComparison.Ordinal) < saved.IndexOf("wert.gruppe", StringComparison.Ordinal),
            "the existing alphabetical file format deliberately lists the child first");
        group.ChoiceIndex = 0;
        group.NotifyChanged();
        callbacks.Clear();
        bool loaded = Profile.TryLoad("dependent-field-order", out string message);
        check(loaded && callbacks.SequenceEqual(new[] { "group", "field:B/B2" }),
            "profile load must apply the registered parent before its alphabetically earlier child: " + message);
        check(group.ChoiceIndex == 1 && field.ChoiceIndex == 0 && field.Choices[field.ChoiceIndex] == "B2",
            "the child must resolve its saved identity against the freshly replaced list, not reuse its old index");
    }

    private static void DependentNumber(Action<bool, string> check)
    {
        var callbacks = new List<string>();
        var appliedToTeams = new Dictionary<string, float>();
        var team = new CheatOption
        {
            Id = "mut.team", Label = "Team", Kind = OptionKind.Choice,
            Choices = new[] { "Team A", "Team B" }, ChoiceIndex = 1
        };
        var amount = new CheatOption
        {
            Id = "mut.menge", Label = "Menge", Kind = OptionKind.Number,
            Min = 0, Max = 1000, NumberValue = 700
        };
        team.OnChanged = _ => { callbacks.Add("team"); amount.NumberValue = 0; };
        amount.OnChanged = _ =>
        {
            callbacks.Add("amount");
            appliedToTeams[team.Choices[team.ChoiceIndex]] = amount.NumberValue;
        };
        // The ordered module categories, not alphabetical category or option names, define the dependency order.
        Registry.SetModule(new Fixture(
            new CheatCategory("Ziel") { Options = new List<CheatOption> { team } },
            new CheatCategory("Aktion") { Options = new List<CheatOption> { amount } }));
        check(Profile.TrySave("dependent-team-order", out _), "dependent number fixture saves");
        team.ChoiceIndex = 0;
        amount.NumberValue = 5;
        bool loaded = Profile.TryLoad("dependent-team-order", out string message);
        check(loaded && callbacks.SequenceEqual(new[] { "team", "amount" }),
            "a target selector in an earlier registered category must be applied before its numeric action: " + message);
        check(appliedToTeams.Count == 1 && appliedToTeams.TryGetValue("Team B", out float value) && value == 700 && amount.NumberValue == 700,
            "the saved amount must never be sent to the previously selected team or cleared by a late parent callback");

        callbacks.Clear();
        appliedToTeams.Clear();
        team.ChoiceIndex = 0;
        string path = Path.Combine(Profile.Ordner, "invalid-dependent-order.json");
        File.WriteAllText(path,
            "{\"werte\":[{\"id\":\"mut.team\",\"choice\":1},{\"id\":\"mut.menge\",\"number\":2000}]}");
        check(!Profile.TryLoad("invalid-dependent-order", out _) && team.ChoiceIndex == 0 && callbacks.Count == 0 && appliedToTeams.Count == 0,
            "all saved value types and bounds must still be validated before any dependency callback runs");

        File.WriteAllText(Path.Combine(Profile.Ordner, "partial-dependent-order.json"),
            "{\"werte\":[{\"id\":\"MUT.TEAM\",\"choice\":1},{\"id\":\"removed.option\",\"number\":3}]}");
        check(Profile.TryLoad("partial-dependent-order", out message) && callbacks.SequenceEqual(new[] { "team" }) &&
            appliedToTeams.Count == 0 && message.Contains("nicht mehr vorhanden"),
            "ordered loading preserves case-insensitive IDs, unknown-entry reporting and partial profiles without inventing missing values");
    }

    private sealed class Fixture : IGameModule
    {
        private readonly List<CheatCategory> _categories;
        internal Fixture(params CheatCategory[] categories) { _categories = categories.ToList(); }
        public string ProductName => "profile-order-fixture";
        public string DisplayName => "Profile order fixture";
        public void Initialize() { }
        public void Update() { }
        public void SetMenuOpen(bool open) { }
        public bool IsReady(out string status) { status = "fixture"; return true; }
        public List<CheatCategory> BuildCategories() => _categories;
    }
}
