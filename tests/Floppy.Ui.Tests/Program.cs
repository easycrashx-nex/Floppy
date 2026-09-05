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
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); _checks++; Console.WriteLine("PASS " + message); }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Floppy.App.App(); app.InitializeComponent();
            var window = new MainWindow(offline: true);
            var root = (Grid)window.Content;
            ((TextBlock)window.FindName("GameLabel")).Text = "How to Fish · Vorschau";
            ((TextBlock)window.FindName("StatusLabel")).Text = "UI-Test · keine Verbindung zu einem Spiel";
            var category = new CategoryInfo { Name = "Spieler", Options = new List<OptionInfo>
            {
                new() { Id="god", Label="Gottmodus", Kind="Toggle", BoolValue=true, Active=true, Description="Ein Beispieldatensatz für den Oberflächentest." },
                new() { Id="speed", Label="Bewegungstempo", Kind="Slider", Min=.5, Max=6, NumberValue=1.5, Step=.1, Active=true, Description="Komma und Punkt werden als Dezimalzeichen erkannt." },
                new() { Id="amount", Label="Menge", Kind="Number", Min=0, Max=100, NumberValue=12 },
                new() { Id="choice", Label="Auswahl", Kind="Choice", Choices=new[]{"Normal", "Alternative"}, ChoiceIndex=0 }
            }};
            Set(window, "_categories", new List<CategoryInfo>{category});
            Set(window, "_connectedGameId", "Fixture");
            Set(window, "_selectedCategory", 0);
            var settings = (UserSettings)Get(window,"_settings"); settings.Favorites["Fixture"]=new List<string>{"god"};
            Invoke(window, "RenderCategoryList"); Invoke(window, "RenderOptions");
            var rows = (StackPanel)window.FindName("OptionsPanel");
            foreach (UIElement row in rows.Children) { row.BeginAnimation(UIElement.OpacityProperty, null); row.Opacity=1; row.RenderTransform=Transform.Identity; }
            foreach (double width in new[] {980d,1180d})
            {
                root.Measure(new Size(width, 760)); root.Arrange(new Rect(0,0,width,760)); root.UpdateLayout();
                var header=(Grid)root.Children[0];
                var actions=header.Children.OfType<WrapPanel>().Single();
                var title=header.Children.OfType<StackPanel>().First();
                Check(actions.TranslatePoint(new Point(),header).Y >= title.ActualHeight, "header separates actions at " + width);
                foreach (FrameworkElement child in actions.Children)
                {
                    var pos=child.TranslatePoint(new Point(),actions);
                    Check(pos.X >= 0 && pos.X+child.ActualWidth <= actions.ActualWidth+.1, "toolbar fits: " + (child is Button b ? b.Content : child.GetType().Name));
                }
            }
            Set(window, "_selectedCategory", -2); Invoke(window,"RenderOptions");
            Check(((TextBlock)window.FindName("PaneTitle")).Text=="Favoriten" && rows.Children.Count==1,"favorites render selected function only");
            var inactive=new OptionInfo{Id="inactive",Kind="Toggle",BoolValue=true,Active=false};
            Check(!(bool)typeof(MainWindow).GetMethod("IstAn",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,new object[]{inactive})!,"server active state overrides displayed toggle value");
            if(args.Length>0)
            {
                Set(window,"_selectedCategory",0); Invoke(window,"RenderOptions");
                ((StackPanel)window.FindName("EmptyHint")).Visibility=Visibility.Collapsed;
                foreach(UIElement row in rows.Children){row.BeginAnimation(UIElement.OpacityProperty,null);row.Opacity=1;row.RenderTransform=Transform.Identity;}
                root.Measure(new Size(1180,760));root.Arrange(new Rect(0,0,1180,760));root.UpdateLayout();
                var image=new RenderTargetBitmap(1180,760,96,96,PixelFormats.Pbgra32);image.Render(root);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                using var file=File.Create(args[0]);encoder.Save(file);
            }
            Console.WriteLine($"{_checks} UI checks passed");
            app.Shutdown();return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(target,value);
    private static object Get(object target,string name)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    private static void Invoke(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,null);
}
