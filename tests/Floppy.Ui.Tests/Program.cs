using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floppy.App;

internal static class Program
{
    private static int _checks;
    private const string FixtureId = "UI-Fixture";
    private const string LongDescription = "Passe dein Bewegungstempo in kleinen Schritten an. Diese längere Beispielbeschreibung prüft, dass Hinweise auch im kleinsten Fenster vollständig umbrechen und die Bedienelemente erreichbar bleiben.";
    private static readonly Size[] Sizes = { new(980, 560), new(1180, 760), new(1440, 900), new(960, 520) };

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Floppy.App.App();
            app.InitializeComponent();
            var window = new MainWindow(offline: true);
            var root = (Grid)window.Content;
            PopulateFixture(window);

            foreach (Size size in Sizes)
            {
                Arrange(root, size);
                CheckLayout(window, root, size);
                CheckOptionWidths(window, root, size);
            }

            var rows = Named<StackPanel>(window, "OptionsPanel");
            var controls = Descendants(rows).OfType<Control>().ToList();
            Check(controls.OfType<CheckBox>().Any() && controls.OfType<Slider>().Any()
                && controls.OfType<ComboBox>().Any() && controls.OfType<TextBox>().Count() >= 2
                && controls.OfType<Button>().Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Position zurücksetzen"
                    || Equals(b.Content, "Position zurücksetzen") || b.Content is TextBlock { Text: "Position zurücksetzen" }),
                "toggle, slider, choice, number, text and action are rendered");

            Set(window, "_selectedCategory", -2);
            Render(window);
            Check(Named<TextBlock>(window, "PaneTitle").Text == "Favoriten" && rows.Children.Count == 1,
                "favorites render selected function only");
            var inactive = new OptionInfo { Id = "inactive", Kind = "Toggle", BoolValue = true, Active = false };
            Check(!(bool)typeof(MainWindow).GetMethod("IstAn", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, new object[] { inactive })!, "server active state overrides displayed toggle value");

            Set(window, "_selectedCategory", 0);
            Render(window);
            if (args.Length > 0)
            {
                string screenshot = Path.GetFullPath(args[0]);
                SaveScreenshot(root, Sizes[1], screenshot);
                SaveScreenshot(root, Sizes[0], Sibling(screenshot, "-small"));
                ShowEmptyFixture(window);
                SaveScreenshot(root, Sizes[1], Sibling(screenshot, "-empty"));
            }
            else ShowEmptyFixture(window);

            foreach (Size size in Sizes)
            {
                Arrange(root, size);
                CheckLayout(window, root, size);
                var empty = Named<FrameworkElement>(window, "EmptyHint");
                Check(Inside(Bounds(empty, root), Bounds(Named<Border>(window, "OptionsPane"), root))
                    && Separate(Bounds(empty, root), Bounds(Named<TextBlock>(window, "PaneTitle"), root)),
                    "empty state stays inside options pane at " + size);
            }
            Check(Named<StackPanel>(window, "OptionsPanel").Children.Count == 0,
                "offline empty state contains no fixture options");
            CheckItemPicker(args.Length > 0 ? Path.GetFullPath(args[0]) : null);
            CheckItemImages();
            CheckItemPickerIntegration();
            CheckItemPickerWindow(args.Length > 0 ? Path.GetFullPath(args[0]) : null);
            Console.WriteLine($"{_checks} UI checks passed");
            app.Shutdown();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void PopulateFixture(MainWindow window)
    {
        // Explicit test data; no IPC connection, executable launch or settings persistence.
        string? steam = SteamLibrary.FindSteamPath();
        var games = new List<SupportedGame>
        {
            new() { Name = "How to Fish", AppId = "4001890", ProductName = FixtureId, Installed = true, Status = "UI-Vorschau" },
            new() { Name = "Mortal Shell II", AppId = "2584270", ProductName = "Fixture-Mortal", Installed = true, Status = "Beispielspiel" },
            new() { Name = "ODDCORE", AppId = "2896260", ProductName = "Fixture-Oddcore", Installed = true, Status = "Beispielspiel" },
            new() { Name = "Project P.I.T.T.", AppId = "4026250", ProductName = "Fixture-Pitt", Installed = true, Status = "Beispielspiel" },
            new() { Name = "Stonewards", AppId = "4502710", ProductName = "Fixture-Stonewards", Installed = true, Status = "Beispielspiel" },
            new() { Name = "Unrailed! 2", AppId = "2211170", ProductName = "Fixture-Unrailed", Status = "Beispielspiel" }
        };
        if (steam != null)
            foreach (var game in games) game.CoverPath = SteamLibrary.FindCoverImage(steam, game.AppId);
        Set(window, "_games", games);
        Set(window, "_selectedGame", games[0]);
        Set(window, "_connectedGameId", FixtureId);
        Invoke(window, "BindGameList");
        Named<TextBlock>(window, "GameLabel").Text = "How to Fish";
        Named<TextBlock>(window, "StatusLabel").Text = "UI-Vorschau · Beispieldaten · kein Spiel verbunden";
        Named<TextBlock>(window, "LibraryHint").Text = "Beispielbibliothek für den Oberflächentest";
        var categories = new List<CategoryInfo>
        {
            new() { Name = "Spieler", Options = new List<OptionInfo>
            {
                new() { Id = "god", Label = "Unverwundbarkeit", Kind = "Toggle", BoolValue = true, Active = true,
                    Description = "Schützt deinen Charakter vor Schaden." },
                new() { Id = "speed", Label = "Bewegungstempo", Kind = "Slider", Min = .5, Max = 3,
                    NumberValue = 1.5, Step = .1, Active = true, Description = LongDescription },
                new() { Id = "choice", Label = "Bewegungsart", Kind = "Choice", Choices = new[] { "Normal", "Freies Erkunden", "Präzise Bewegung" }, ChoiceIndex = 0 },
                new() { Id = "amount", Label = "Ausdauer", Kind = "Number", Min = 0, Max = 100, NumberValue = 100 },
                new() { Id = "reset", Label = "Position zurücksetzen", Kind = "Button" },
                new() { Id = "name", Label = "Markierung benennen", Kind = "Text", TextValue = "Am See" },
                new() { Id = "location", Label = "Aktuelles Gebiet", Kind = "Info", TextValue = "Seeufer (Beispiel)" }
            } },
            new() { Name = "Ausrüstung", Options = new List<OptionInfo>
            {
                new() { Id = "durability", Label = "Haltbarkeit bewahren", Kind = "Toggle" },
                new() { Id = "inventory", Label = "Inventargröße", Kind = "Number", NumberValue = 20, Min = 1, Max = 100 }
            } },
            new() { Name = "Welt & Umgebung", Options = new List<OptionInfo>
            {
                new() { Id = "day", Label = "Tageszeit", Kind = "Slider", NumberValue = 12, Min = 0, Max = 24, Step = .5 }
            } },
            new() { Name = "Profile", Options = new List<OptionInfo>
            {
                new() { Id = "profile", Label = "Profil speichern", Kind = "Button" }
            } }
        };
        Set(window, "_categories", categories);
        Set(window, "_selectedCategory", 0);
        var settings = (UserSettings)Get(window, "_settings");
        settings.Favorites[FixtureId] = new List<string> { "god" };
        Render(window);
        Named<FrameworkElement>(window, "EmptyHint").Visibility = Visibility.Collapsed;
    }

    private static void Render(MainWindow window)
    {
        Set(window, "_suppressEvents", true);
        try { Invoke(window, "RenderCategoryList"); Invoke(window, "RenderOptions"); }
        finally { Set(window, "_suppressEvents", false); }
        foreach (UIElement row in Named<StackPanel>(window, "OptionsPanel").Children)
        {
            row.BeginAnimation(UIElement.OpacityProperty, null);
            row.Opacity = 1;
            row.RenderTransform = Transform.Identity;
        }
    }

    private static void CheckLayout(MainWindow window, Grid root, Size size)
    {
        string[] names = { "LibraryPane", "HeaderPanel", "HeaderActions", "SearchBox", "CategoriesPane", "OptionsPane", "SessionBar" };
        var bounds = names.ToDictionary(n => n, n => Bounds(Named<FrameworkElement>(window, n), root));
        Rect available = new(0, 0, root.ActualWidth, root.ActualHeight);
        Check(bounds.Values.All(b => b.Width > 0 && b.Height > 0 && Inside(b, available)),
            "all named layout regions fit at " + size);
        Check(Separate(bounds["LibraryPane"], bounds["OptionsPane"])
            && Separate(bounds["LibraryPane"], bounds["CategoriesPane"])
            && Separate(bounds["CategoriesPane"], bounds["OptionsPane"])
            && Separate(bounds["HeaderPanel"], bounds["OptionsPane"])
            && Separate(bounds["HeaderActions"], bounds["OptionsPane"])
            && Separate(bounds["SessionBar"], bounds["OptionsPane"]),
            "library, navigation, header and session do not overlap content at " + size);
        Check(Separate(bounds["SearchBox"], bounds["HeaderActions"]),
            "search and toolbar do not overlap at " + size);
        var actions = Named<WrapPanel>(window, "HeaderActions");
        var buttons = new[] { Named<Button>(window, "AllOffButton"), Named<Button>(window, "ToolsButton") };
        Check(buttons.All(b => Inside(Bounds(b, actions), new Rect(0, 0, actions.ActualWidth, actions.ActualHeight)))
            && Separate(Bounds(buttons[0], actions), Bounds(buttons[1], actions)),
            "toolbar actions fit without overlapping at " + size);
        var launch = Named<Button>(window, "LaunchButton");
        var connect = Named<Button>(window, "ConnectButton");
        Check(Separate(Bounds(launch, root), Bounds(connect, root))
            && Separate(Bounds(Named<TextBlock>(window, "SelectedGameTitle"), root), Bounds(launch, root))
            && Inside(Bounds(launch, root), bounds["HeaderPanel"])
            && Inside(Bounds(connect, root), bounds["HeaderPanel"]),
            "game heading and connection actions fit at " + size);
    }

    private static void CheckOptionWidths(MainWindow window, Grid root, Size size)
    {
        var rows = Named<StackPanel>(window, "OptionsPanel");
        var available = Bounds(rows, root);
        var controls = Descendants(rows).OfType<Control>()
            .Where(c => c.Visibility == Visibility.Visible && (c is Button or CheckBox or Slider or TextBox or ComboBox)).ToList();
        var unreachable = controls.Where(c =>
        {
            var b = Bounds(c, root);
            return b.Width <= 0 || b.Left < available.Left - .5 || b.Right > available.Right + .5;
        }).ToList();
        Check(unreachable.Count == 0, "all option controls remain horizontally reachable at " + size
            + (unreachable.Count == 0 ? "" : ": " + string.Join(", ", unreachable.Select(c => c.GetType().Name + " " + Bounds(c, root)))));
        var description = Descendants(rows).OfType<TextBlock>().Single(t => t.Text == LongDescription);
        Check(description.TextWrapping == TextWrapping.Wrap && description.ActualHeight > 24,
            "long option description wraps at " + size);
    }

    private static void ShowEmptyFixture(MainWindow window)
    {
        Set(window, "_categories", new List<CategoryInfo>());
        Set(window, "_connectedGameId", "");
        Named<StackPanel>(window, "OptionsPanel").Children.Clear();
        Named<StackPanel>(window, "CategoryList").Children.Clear();
        Invoke(window, "UpdateEmptyHint");
        Named<TextBlock>(window, "GameLabel").Text = "How to Fish";
        Named<TextBlock>(window, "StatusLabel").Text = "UI-Vorschau · keine Verbindung";
    }

    private static void CheckItemPicker(string? screenshot)
    {
        string missingIcon = Path.Combine(Path.GetTempPath(), "floppy-missing-item-" + Guid.NewGuid().ToString("N") + ".png");
        var entries = new[]
        {
            new ItemEntry(0, "iron", "Eisenerz", "Materialien", null),
            new ItemEntry(1, "chapel-key", "Schlüssel der Kapelle", "Schlüssel & Quests", missingIcon),
            new ItemEntry(2, "resin", "Harzflasche", "Verbrauchsgegenstände", null),
            new ItemEntry(3, "bright-iron", "Glänzendes Eisenerz", "Materialien", null),
            new ItemEntry(4, "chapel-letter", "Brief aus der verlassenen Kapelle", "Schlüssel & Quests", null)
        };
        var picker = new ItemPicker(entries, selectedIndex: 2);
        var events = new List<int>();
        picker.SelectionChanged += events.Add;
        var host = new Grid { Background = (Brush)Application.Current.FindResource("Bg"), Margin = new Thickness(16) };
        host.Children.Add(picker);
        var categories = Named<ComboBox>(picker, "CategoryFilter");
        var search = Named<TextBox>(picker, "ItemSearch");
        var list = Named<ListBox>(picker, "ItemList");
        var selected = Named<TextBlock>(picker, "SelectedItemName");
        var count = Named<TextBlock>(picker, "ResultCount");
        Arrange(host, new Size(440, 640));
        Check(list.Items.Count == entries.Length && selected.Text.Contains(entries[2].Name),
            "item picker initially shows all entries and the current selection");
        Check(Descendants(categories).OfType<TextBlock>().Any(t => t.Text == ((ItemCategory)categories.SelectedItem).Label),
            "closed item category filter displays its readable label and count");

        categories.SelectedValue = "Materialien";
        Check(list.Items.Cast<ItemEntry>().Select(e => e.Index).SequenceEqual(new[] { 0, 3 }),
            "item category filter retains original item identities");
        Check(list.SelectedItem == null && selected.Text.Contains(entries[2].Name) && events.Count == 0,
            "filtering out the current item preserves its selection without emitting a change");
        search.Text = "GLÄNZEND";
        Check(list.Items.Count == 1 && ((ItemEntry)list.Items[0]).Index == 3 && events.Count == 0,
            "item search combines with category and ignores letter case without selecting automatically");

        // Changing the standalone ListBox selection represents a user choosing a
        // visible row. This control has no IPC client or game attached.
        list.SelectedItem = list.Items[0];
        Check(events.SequenceEqual(new[] { 3 }) && selected.Text.Contains(entries[3].Name),
            "choosing the first filtered row emits its original index, not visible index zero");
        picker.SetSelectedIndex(1);
        Check(events.Count == 1 && list.SelectedItem == null && selected.Text.Contains(entries[1].Name),
            "server selection updates remain visible when filtered out and never emit a command");
        search.Text = "";
        categories.SelectedValue = "Schlüssel & Quests";
        Check(list.Items.Cast<ItemEntry>().Select(e => e.Index).SequenceEqual(new[] { 1, 4 })
            && list.SelectedItem is ItemEntry { Index: 1 } && events.Count == 1,
            "restoring a selected item through category changes remains silent");
        search.Text = "kapelle";
        Check(list.Items.Count == 2 && events.Count == 1, "search matches item names within the selected category");
        search.Text = "kein solches Gegenstandsfragment";
        Check(list.Items.Count == 0 && count.Text.Contains("0") && selected.Text.Contains(entries[1].Name) && events.Count == 1,
            "empty search results retain the current selection and report zero results");
        search.Text = "";
        picker.SetSelectedIndex(4);
        picker.SetSelectedIndex(1);
        Check(events.Count == 1, "repeated programmatic selection updates do not emit selection events");

        foreach (double width in new[] { 320d, 480d })
        {
            Arrange(host, new Size(width, 640));
            Rect available = new(0, 0, picker.ActualWidth, picker.ActualHeight);
            Check(new FrameworkElement[] { categories, search, list, selected, count }
                .All(e => Inside(Bounds(e, picker), available)), "item picker controls fit at width " + width);
            Check(list.ActualHeight > 0 && list.ActualHeight <= 260
                && Separate(Bounds(list, picker), Bounds(selected, picker)),
                "bounded item list leaves its persistent selection visible at width " + width);
        }
        var row = list.ItemContainerGenerator.ContainerFromItem(entries[1]) as ListBoxItem;
        Check(row != null, "item with a missing image still creates a selectable row");
        var badge = Descendants(row!).OfType<Border>().Single(e => e.Name == "FallbackBadge");
        var fallback = Descendants(row!).OfType<System.Windows.Shapes.Path>().Single(e => e.Name == "ItemFallback");
        Check(badge.Visibility == Visibility.Visible && badge.ActualWidth > 0
            && fallback.Visibility == Visibility.Visible && fallback.Data != null,
            "a missing item image renders the visible fallback icon");
        Check(selected.Text.Contains(entries[1].Name), "a missing image does not lose the selected item's name");

        if (screenshot != null)
        {
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(picker, 1);
            host.Children.Add(new TextBlock
            {
                Text = "UI-Vorschau · synthetische Gegenstände", FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(0, 0, 0, 16)
            });
            SaveScreenshot(host, new Size(480, 640), Sibling(screenshot, "-item-picker"));
        }

        var empty = new ItemPicker(Array.Empty<ItemEntry>(), selectedIndex: -1);
        int emptyEvents = 0;
        empty.SelectionChanged += _ => emptyEvents++;
        empty.SetSelectedIndex(-1);
        Check(Named<ListBox>(empty, "ItemList").Items.Count == 0 && emptyEvents == 0,
            "an unloaded item catalogue remains empty without emitting a selection");
    }

    private static void CheckItemImages()
    {
        string prefix = Path.Combine(Path.GetTempPath(), "floppy-ui-image-" + Guid.NewGuid().ToString("N"));
        string valid = prefix + ".png", broken = prefix + "-broken.png";
        try
        {
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 0, 180, 80, 255, 0, 180, 80, 255, 0, 180, 80, 255, 0, 180, 80, 255 }, 8);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(valid)) encoder.Save(output);
            File.WriteAllBytes(broken, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 1, 2, 3 });
            var entries = new[]
            {
                new ItemEntry(0, "valid-image", "PNG-Testbild", "Materialien", valid),
                new ItemEntry(1, "broken-image", "Beschädigtes Testbild", "Materialien", broken)
            };
            var picker = new ItemPicker(entries, selectedIndex: 0);
            var host = new Grid();
            host.Children.Add(picker);
            Arrange(host, new Size(420, 640));
            var list = Named<ListBox>(picker, "ItemList");
            foreach (var entry in entries)
            {
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(entry);
                var icon = Descendants(row).OfType<Image>().Single(e => e.Name == "ItemIcon");
                var badge = Descendants(row).OfType<Border>().Single(e => e.Name == "FallbackBadge");
                Check(entry.Index == 0
                    ? icon.Source != null && icon.Visibility == Visibility.Visible && badge.Visibility == Visibility.Collapsed
                    : icon.Source == null && icon.Visibility == Visibility.Collapsed && badge.Visibility == Visibility.Visible,
                    entry.Index == 0 ? "a decoded PNG replaces its fallback in the item template"
                        : "a damaged PNG retains the visible item fallback without breaking the picker");
            }
        }
        finally
        {
            File.Delete(valid);
            File.Delete(broken);
        }
    }

    private static void CheckItemPickerIntegration()
    {
        var window = new MainWindow(offline: true);
        var option = new OptionInfo { Id = "item.was", Label = "Gegenstand", Kind = "Choice", Choices = new[] { "Fixture_Item_A", "Fixture_Item_B" }, ChoiceIndex = 0 };
        FrameworkElement Build(string game, string id)
        {
            Set(window, "_connectedGameId", game);
            option.Id = id;
            var control = (FrameworkElement)typeof(MainWindow).GetMethod("BuildChoice", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, new object[] { option })!;
            control.Measure(new Size(420, 500));
            control.Arrange(new Rect(0, 0, 420, 500));
            control.UpdateLayout();
            return control;
        }
        IEnumerable<DependencyObject> IncludingSelf(DependencyObject element) => new[] { element }.Concat(Descendants(element));
        var mortal = Build("MortalShell2", "item.was");
        var picker = IncludingSelf(mortal).OfType<ItemPicker>().SingleOrDefault();
        Check(picker != null, "Mortal Shell's item choice renders the item picker");
        int changes = 0;
        picker!.SelectionChanged += _ => changes++;
        var refreshers = (Dictionary<string, Action<OptionInfo>>)Get(window, "_refreshers");
        refreshers["item.was"](new OptionInfo { Id = "item.was", Kind = "Choice", Choices = option.Choices, ChoiceIndex = 1, Available = false });
        Check(changes == 0 && !picker.IsEnabled && ((HashSet<string>)Get(window, "_angefasst")).Count == 0,
            "IPC refresh updates item selection and availability without generating a user action");
        Check(Named<ListBox>(picker, "ItemList").SelectedItem is ItemEntry { Index: 1 },
            "item picker integration applies the original index from server state");
        var ordinary = Build("MortalShell2", "other.choice");
        Check(!IncludingSelf(ordinary).OfType<ItemPicker>().Any() && IncludingSelf(ordinary).OfType<ComboBox>().Any(),
            "other Mortal Shell choices retain their existing control");
        var otherGame = Build("ODDCORE", "item.was");
        Check(!IncludingSelf(otherGame).OfType<ItemPicker>().Any() && IncludingSelf(otherGame).OfType<ComboBox>().Any(),
            "another game's identically named choice retains its existing control");
    }

    private static void CheckItemPickerWindow(string? screenshot)
    {
        var window = new MainWindow(offline: true);
        PopulateFixture(window);
        var game = ((List<SupportedGame>)Get(window, "_games"))[1];
        Set(window, "_selectedGame", game);
        Set(window, "_connectedGameId", "MortalShell2");
        Invoke(window, "BindGameList");
        Named<TextBlock>(window, "GameLabel").Text = "Mortal Shell II";
        Named<TextBlock>(window, "StatusLabel").Text = "UI-Vorschau · synthetische Auswahl · kein Spiel verbunden";
        string[] choices = { "Gloom", "Coin", "Ventrium", "Dorsalite", "MushroomVillageKey", "Mango", "Weltcap" };
        string metadataPath = Path.Combine(AppContext.BaseDirectory, "Assets", "MortalShell2", "items.json");
        if (File.Exists(metadataPath))
        {
            using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            choices = metadata.RootElement.EnumerateObject().Select(item => item.Name).ToArray();
        }
        Set(window, "_categories", new List<CategoryInfo>
        {
            new() { Name = "Inventar", Options = new List<OptionInfo>
            {
                new() { Id = "item.was", Label = "Gegenstand", Kind = "Choice", ChoiceIndex = 2,
                    Choices = choices },
                new() { Id = "item.amount", Label = "Anzahl", Kind = "Number", NumberValue = 1, Min = 1, Max = 99 },
                new() { Id = "item.add", Label = "Gegenstand hinzufügen", Kind = "Button" }
            } }
        });
        Render(window);
        var root = (Grid)window.Content;
        var pane = Named<Border>(window, "OptionsPane");
        var rows = Named<StackPanel>(window, "OptionsPanel");
        var picker = Descendants(rows).OfType<ItemPicker>().Single();
        var outerScroll = Descendants(pane).OfType<ScrollViewer>().Single(s => ReferenceEquals(s.Content, rows));
        foreach (Size size in Sizes)
        {
            Arrange(root, size);
            CheckLayout(window, root, size);
            Rect area = Bounds(rows, root);
            var controls = new FrameworkElement[]
            {
                Named<ComboBox>(picker, "CategoryFilter"), Named<TextBox>(picker, "ItemSearch"),
                Named<ListBox>(picker, "ItemList"), Named<TextBlock>(picker, "SelectedItemName")
            };
            Check(controls.All(e => Bounds(e, root).Left >= area.Left - .5 && Bounds(e, root).Right <= area.Right + .5),
                "embedded item picker remains horizontally reachable at " + size);
            outerScroll.ScrollToEnd();
            root.UpdateLayout();
            Check(outerScroll.VerticalOffset >= outerScroll.ScrollableHeight - .5
                && Inside(Bounds((FrameworkElement)rows.Children[rows.Children.Count - 1], root), Bounds(outerScroll, root)),
                "inventory action remains reachable below the item picker at " + size);
            outerScroll.ScrollToTop();
            root.UpdateLayout();
        }
        if (screenshot != null)
        {
            SaveScreenshot(root, Sizes[2], Sibling(screenshot, "-full-overlay"));
            SaveScreenshot(root, Sizes[0], Sibling(screenshot, "-full-overlay-small"));
        }
    }

    private static void Arrange(Grid root, Size size)
    {
        root.Measure(size);
        root.Arrange(new Rect(new Point(), size));
        root.UpdateLayout();
    }

    private static void SaveScreenshot(Grid root, Size size, string path)
    {
        Arrange(root, size);
        var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        encoder.Save(file);
        Console.WriteLine("Screenshot: " + path);
    }

    private static string Sibling(string path, string suffix) =>
        Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + suffix + Path.GetExtension(path));
    private static Rect Bounds(FrameworkElement child, Visual parent) =>
        child.TransformToAncestor(parent).TransformBounds(new Rect(0, 0, child.ActualWidth, child.ActualHeight));
    private static bool Inside(Rect inner, Rect outer) => inner.Left >= outer.Left - .5 && inner.Top >= outer.Top - .5
        && inner.Right <= outer.Right + .5 && inner.Bottom <= outer.Bottom + .5;
    private static bool Separate(Rect a, Rect b) => a.Right <= b.Left + .5 || b.Right <= a.Left + .5
        || a.Bottom <= b.Top + .5 || b.Bottom <= a.Top + .5;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static T Named<T>(FrameworkElement window, string name) where T : FrameworkElement =>
        window.FindName(name) as T ?? throw new Exception($"Missing {typeof(T).Name}: {name}");
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object Get(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
}
