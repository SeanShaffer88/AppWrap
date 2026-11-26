using System.Windows;
using System.Windows.Media;

namespace AppWrap.Gui;

// Theme palettes. "FluentGruv" (dark) and "FluentGruv Light" implement the
// FluentGruv design system v1.3.0 (github.com/SeanShaffer88/FluentGruv,
// tokens.json themes.dark / themes.light) token for token — see
// docs/DESIGN.md for the mapping. The remaining palettes are alternates kept
// for user choice: Catppuccin Mocha, Nord and OLED Black use their
// author-published palette values, "Gruvbox Light" is the legacy pre-
// FluentGruv light palette, and AppWrap is the original app palette. For the
// extended slots (Canvas, Elevated, TextSecondary, BorderStrong,
// AccentSubtle, AccentPressed, Success, Warning) the alternates reuse their
// own nearest existing value, so their rendering is unchanged from before
// the slots existed. No theme framework is required.
internal static class ThemeCatalog
{
    private sealed record Palette(string Surface, string Text, string Muted, string Accent,
        string AccentText, string InputSurface, string Border, string Error, string StepAccent,
        string Canvas, string Elevated, string TextSecondary, string BorderStrong,
        string AccentSubtle, string AccentPressed, string Success, string Warning);

    // FluentGruv v1.3.0, tokens.json themes.dark — the default theme.
    // Field-to-token map: Surface=bg.surface, Text=fg.primary, Muted=fg.muted,
    // Accent=accent.primary, AccentText=fg.inverse, InputSurface=bg.sunken,
    // Border=border.subtle, Error=status.danger, StepAccent=accent.primary
    // (focus ring / step indicator), Canvas=bg.canvas, Elevated=bg.elevated,
    // TextSecondary=fg.secondary, BorderStrong=border.strong,
    // AccentSubtle=accent.subtle, AccentPressed=accent.secondary,
    // Success=status.success, Warning=status.warning.
    private static readonly Palette FluentGruvDark = new(
        Surface: "#282828", Text: "#EBDBB2", Muted: "#A89984", Accent: "#FB4934",
        AccentText: "#1D2021", InputSurface: "#171819", Border: "#3C3836", Error: "#FB4934",
        StepAccent: "#FB4934",
        Canvas: "#1D2021", Elevated: "#32302F", TextSecondary: "#D5C4A1", BorderStrong: "#504945",
        AccentSubtle: "#3B1D1B", AccentPressed: "#CC241D", Success: "#B8BB26", Warning: "#FABD2F");

    // FluentGruv v1.3.0, tokens.json themes.light — same field-to-token map.
    private static readonly Palette FluentGruvLight = new(
        Surface: "#FBF1C7", Text: "#282828", Muted: "#665C54", Accent: "#CC241D",
        AccentText: "#FBF1C7", InputSurface: "#F2E5BC", Border: "#D5C4A1", Error: "#CC241D",
        StepAccent: "#CC241D",
        Canvas: "#F9F5D7", Elevated: "#EBDBB2", TextSecondary: "#3C3836", BorderStrong: "#BDAE93",
        AccentSubtle: "#F4D2AE", AccentPressed: "#9D0006", Success: "#79740E", Warning: "#AF3A03");

    private static readonly IReadOnlyDictionary<string, Palette> Palettes = new Dictionary<string, Palette>
    {
        ["FluentGruv"] = FluentGruvDark,
        ["FluentGruv Light"] = FluentGruvLight,
        ["AppWrap"] = new("#202226", "#F2F2F3", "#BEC2CB", "#76172A", "#FFFFFF", "#181A1D", "#747A86", "#FFD0D8", "#D3ABB4",
            "#202226", "#181A1D", "#BEC2CB", "#D3ABB4", "#181A1D", "#76172A", "#F2F2F3", "#FFD0D8"),
        ["Catppuccin Mocha"] = new("#1E1E2E", "#CDD6F4", "#BAC2DE", "#CBA6F7", "#1E1E2E", "#313244", "#7F849C", "#F38BA8", "#CBA6F7",
            "#1E1E2E", "#313244", "#BAC2DE", "#CBA6F7", "#313244", "#CBA6F7", "#CDD6F4", "#F38BA8"),
        ["Nord"] = new("#2E3440", "#ECEFF4", "#D8DEE9", "#88C0D0", "#2E3440", "#3B4252", "#8E99AC", "#FFB8BF", "#88C0D0",
            "#2E3440", "#3B4252", "#D8DEE9", "#88C0D0", "#3B4252", "#88C0D0", "#ECEFF4", "#FFB8BF"),
        ["Gruvbox Light"] = new("#FBF1C7", "#3C3836", "#504945", "#076678", "#FFFFFF", "#EBDBB2", "#7C6F64", "#9D0006", "#076678",
            "#FBF1C7", "#EBDBB2", "#504945", "#076678", "#EBDBB2", "#076678", "#3C3836", "#9D0006"),
        ["OLED Black"] = new("#000000", "#FFFFFF", "#C4C4C4", "#FFFFFF", "#000000", "#121212", "#9B9B9B", "#FFB8C6", "#FFFFFF",
            "#000000", "#121212", "#C4C4C4", "#FFFFFF", "#121212", "#FFFFFF", "#FFFFFF", "#FFB8C6")
    };

    public const string DefaultName = "FluentGruv";

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
            resources["Canvas"] = SystemColors.WindowBrush; resources["Elevated"] = SystemColors.ControlBrush;
            resources["TextSecondary"] = SystemColors.WindowTextBrush; resources["BorderStrong"] = SystemColors.WindowTextBrush;
            resources["AccentSubtle"] = SystemColors.HighlightBrush; resources["AccentPressed"] = SystemColors.HighlightBrush;
            resources["Success"] = SystemColors.WindowTextBrush; resources["Warning"] = SystemColors.WindowTextBrush;
            return;
        }
        var palette = Palettes.TryGetValue(name, out var selected) ? selected : Palettes[DefaultName];
        Put("Surface", palette.Surface); Put("Text", palette.Text); Put("Muted", palette.Muted);
        Put("Accent", palette.Accent); Put("AccentText", palette.AccentText);
        Put("InputSurface", palette.InputSurface); Put("Border", palette.Border);
        Put("Error", palette.Error); Put("StepAccent", palette.StepAccent);
        Put("Canvas", palette.Canvas); Put("Elevated", palette.Elevated);
        Put("TextSecondary", palette.TextSecondary); Put("BorderStrong", palette.BorderStrong);
        Put("AccentSubtle", palette.AccentSubtle); Put("AccentPressed", palette.AccentPressed);
        Put("Success", palette.Success); Put("Warning", palette.Warning);

        void Put(string key, string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            resources[key] = brush;
        }
    }
}
