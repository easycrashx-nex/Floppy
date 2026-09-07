using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floppy.App;

/// <summary>Filters presentation only. Every selection retains the original protocol index.</summary>
public partial class ItemPicker : UserControl
{
    private readonly IReadOnlyList<ItemEntry> _entries;
    private int _selectedIndex = -1;
    private bool _updating;
    public event Action<int>? SelectionChanged;

    public ItemPicker(IReadOnlyList<ItemEntry> entries, int selectedIndex)
    {
        _entries = entries;
        InitializeComponent();
        CategoryFilter.ItemsSource = new[] { new ItemCategory(ItemCatalog.AllCategories, entries.Count) }
            .Concat(entries.GroupBy(e => e.Category).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new ItemCategory(g.Key, g.Count()))).ToArray();
        CategoryFilter.SelectedIndex = 0;
        CategoryFilter.SelectionChanged += (_, _) => RefreshFilter();
        ItemSearch.TextChanged += (_, _) => RefreshFilter();
        ClearSearch.Click += (_, _) => { ItemSearch.Clear(); ItemSearch.Focus(); };
        ItemList.SelectionChanged += (_, _) =>
        {
            if (_updating || ItemList.SelectedItem is not ItemEntry selected) return;
            if (selected.Index == _selectedIndex) return;
            SetSelectedIndex(selected.Index);
            SelectionChanged?.Invoke(selected.Index);
        };
        RefreshFilter();
        SetSelectedIndex(selectedIndex);
    }

    public void SetSelectedIndex(int index)
    {
        _updating = true;
        try
        {
            _selectedIndex = index;
            var selected = _entries.FirstOrDefault(e => e.Index == index);
            ItemList.SelectedItem = ItemList.Items.Cast<ItemEntry>().FirstOrDefault(e => e.Index == index);
            SelectedPreview.Content = selected;
            SelectedItemName.Text = selected?.Name ?? "Kein Gegenstand ausgewählt";
            SelectedItemName.ToolTip = selected?.Id;
            SelectionHint.Text = selected == null ? "Auswahl" : ItemList.SelectedItem == null
                ? "Für das Inventar ausgewählt · außerhalb des Filters" : "Für das Inventar ausgewählt";
        }
        finally { _updating = false; }
    }

    private void RefreshFilter()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var filtered = ItemCatalog.Filter(_entries,
                CategoryFilter.SelectedValue as string ?? ItemCatalog.AllCategories, ItemSearch.Text);
            ItemList.ItemsSource = filtered;
            ResultCount.Text = $"{filtered.Count} von {_entries.Count} Gegenständen";
            NoResults.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updating = false; }
        SetSelectedIndex(_selectedIndex);
    }
}

public sealed record ItemCategory(string Name, int Count)
{
    public string Label => $"{Name} · {Count}";
    public override string ToString() => Label;
}

public sealed class ItemImageConverter : IValueConverter
{
    private readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        if (_cache.TryGetValue(path, out var cached)) return cached;
        BitmapSource? image = null;
        try
        {
            // Read and close immediately: an image must never lock the release folder.
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException)
        { /* Missing or unreadable artwork uses the category symbol. */ }
        _cache[path] = image;
        return image;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ItemCategoryIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string path = (value as string) switch
        {
            "Währungen" => "M12,2 A10,10 0 1 1 11.99,2 M9,7 H15 M12,7 V17 M9,17 H15",
            "Heilung & Verbrauch" => "M9,2 H15 M10,2 V8 L5,17 Q3,22 8,22 H16 Q21,22 19,17 L14,8 V2 M7,14 H17",
            "Materialien" => "M2,18 L8,4 L17,2 L22,10 L17,22 Z M8,4 L13,12 L2,18 M13,12 L17,22 M13,12 L22,10",
            "Verbesserungen" => "M12,2 L21,11 H16 V22 H8 V11 H3 Z",
            "Sammlerstücke" => "M5,2 H19 V22 H5 Z M8,7 H16 M8,11 H16 M8,15 H13",
            "Schlüssel & Quests" => "M14,7 A5,5 0 1 1 9,2 A5,5 0 0 1 14,7 M12,11 L22,21 M17,16 L20,13 M20,19 L23,16",
            "Karten & Freischaltungen" => "M2,5 L8,2 L16,5 L22,2 V19 L16,22 L8,19 L2,22 Z M8,2 V19 M16,5 V22",
            _ => "M12,2 L22,12 L12,22 L2,12 Z M7,12 H17 M12,7 V17"
        };
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
