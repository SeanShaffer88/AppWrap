using System.Windows;
using System.Windows.Media;

namespace AppWrap.Gui;

// Palette values from the author-published Catppuccin, Nord and Gruvbox palettes.
// AppWrap and OLED Black are original app palettes. No theme framework is required.
internal static class ThemeCatalog
{
    private sealed record Palette(string Surface, string Text, string Muted, string Accent,
        string AccentText, string InputSurface, string Border, string Error, string StepAccent);

    private static readonly IReadOnlyDictionary<string, Palette> Palettes = new Dictionary<string, Palette>
    {
        ["AppWrap"] = new("#202226", "#F2F2F3", "#BEC2CB", "#76172A", "#FFFFFF", "#181A1D", "#747A86", "#FFD0D8", "#D3ABB4"),
        ["Catppuccin Mocha"] = new("#1E1E2E", "#CDD6F4", "#BAC2DE", "#CBA6F7", "#1E1E2E", "#313244", "#7F849C", "#F38BA8", "#CBA6F7"),
        ["Nord"] = new("#2E3440", "#ECEFF4", "#D8DEE9", "#88C0D0", "#2E3440", "#3B4252", "#8E99AC", "#FFB8BF", "#88C0D0"),
        ["Gruvbox Light"] = new("#FBF1C7", "#3C3836", "#504945", "#076678", "#FFFFFF", "#EBDBB2", "#7C6F64", "#9D0006", "#076678"),
        ["OLED Black"] = new("#000000", "#FFFFFF", "#C4C4C4", "#FFFFFF", "#000000", "#121212", "#9B9B9B", "#FFB8C6", "#FFFFFF")
    };

    public static IReadOnlyList<string> Names { get; } = Palettes.Keys.ToArray();

    public static void Apply(string name, ResourceDictionary resources)
    {
        if (SystemParameters.HighContrast)
        {
            resources["Surface"] = SystemColors.WindowBrush; resources["InputSurface"] = SystemColors.ControlBrush;
            resources["Text"] = SystemColors.WindowTextBrush; resources["Muted"] = SystemColors.WindowTextBrush;
            resources["Border"] = SystemColors.WindowTextBrush; resources["Error"] = SystemColors.WindowTextBrush;
            resources["Accent"] = SystemColors.HighlightBrush; resources["AccentText"] = SystemColors.HighlightTextBrush;
            resources["StepAccent"] = SystemColors.HighlightBrush;
            return;
        }
        var palette = Palettes.TryGetValue(name, out var selected) ? selected : Palettes["AppWrap"];
        Put("Surface", palette.Surface); Put("Text", palette.Text); Put("Muted", palette.Muted);
        Put("Accent", palette.Accent); Put("AccentText", palette.AccentText);
        Put("InputSurface", palette.InputSurface); Put("Border", palette.Border);
        Put("Error", palette.Error); Put("StepAccent", palette.StepAccent);

        void Put(string key, string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            resources[key] = brush;
        }
    }
}
