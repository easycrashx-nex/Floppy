using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private static T Named<T>(MainWindow window, string name) where T : FrameworkElement =>
        window.FindName(name) as T ?? throw new Exception($"Missing {typeof(T).Name}: {name}");
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object Get(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
}
