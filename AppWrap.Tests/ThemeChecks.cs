using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// FluentGruv conformance checks (portable: pure source inspection, so they
/// run in the Linux harness). Expected values are the FluentGruv design
/// system v1.3.0 tokens — github.com/SeanShaffer88/FluentGruv, tokens.json
/// (themes.dark / themes.light) and TOKENS.md. They pin three things: the
/// FluentGruv palettes in ThemeCatalog.cs match the spec token for token,
/// FluentGruv is the default theme with App.xaml's startup brushes equal to
/// the dark palette, and no hardcoded colors or off-scale corner radii
/// survive anywhere else in the Gui project.
/// </summary>
public static class ThemeChecks
{
    // tokens.json v1.3.0 field order shared by both FluentGruv palettes.
    private static readonly string[] Fields =
    [
        "Surface", "Text", "Muted", "Accent", "AccentText", "InputSurface", "Border", "Error", "StepAccent",
        "Canvas", "Elevated", "TextSecondary", "BorderStrong", "AccentSubtle", "AccentPressed", "Success", "Warning"
    ];

    // tokens.json v1.3.0 themes.dark: bg.surface/fg.primary/fg.muted/
    // accent.primary/fg.inverse/bg.sunken/border.subtle/status.danger/
    // accent.primary(focus)/bg.canvas/bg.elevated/fg.secondary/
    // border.strong/accent.subtle/accent.secondary/status.success/
    // status.warning.
    private static readonly string[] DarkValues =
    [
        "#282828", "#EBDBB2", "#A89984", "#FB4934", "#1D2021", "#171819", "#3C3836", "#FB4934", "#FB4934",
        "#1D2021", "#32302F", "#D5C4A1", "#504945", "#3B1D1B", "#CC241D", "#B8BB26", "#FABD2F"
    ];

    // tokens.json v1.3.0 themes.light, same token order.
    private static readonly string[] LightValues =
    [
        "#FBF1C7", "#282828", "#665C54", "#CC241D", "#FBF1C7", "#F2E5BC", "#D5C4A1", "#CC241D", "#CC241D",
        "#F9F5D7", "#EBDBB2", "#3C3836", "#BDAE93", "#F4D2AE", "#9D0006", "#79740E", "#AF3A03"
    ];

    private static readonly string[] ResourceKeys =
    [
        "Surface", "Text", "Muted", "Accent", "AccentText", "InputSurface", "Border", "Error", "StepAccent",
        "Canvas", "Elevated", "TextSecondary", "BorderStrong", "AccentSubtle", "AccentPressed", "Success", "Warning"
    ];

    public static void Run(string root, Action<string, bool> check)
    {
        var gui = Path.Combine(root, "AppWrap.Gui");
        var catalog = File.ReadAllText(Path.Combine(gui, "ThemeCatalog.cs"));
        var appXaml = File.ReadAllText(Path.Combine(gui, "App.xaml"));
        var windowCode = File.ReadAllText(Path.Combine(gui, "MainWindow.xaml.cs"));

        PaletteMatches(catalog, "FluentGruvDark", "themes.dark", DarkValues, check);
        PaletteMatches(catalog, "FluentGruvLight", "themes.light", LightValues, check);

        check("FluentGruv is the catalog default theme (tokens.json: dark is the default)",
            catalog.Contains("public const string DefaultName = \"FluentGruv\";")
            && catalog.Contains("Palettes[DefaultName]"));
        check("fresh installs start on FluentGruv (saved-state default and restore fallback)",
            windowCode.Contains("private string theme = ThemeCatalog.DefaultName;")
            && windowCode.Contains("string Theme = ThemeCatalog.DefaultName")
            && windowCode.Contains("? saved.Theme : ThemeCatalog.DefaultName"));
        check("alternate themes remain available beside FluentGruv",
            new[] { "AppWrap", "Catppuccin Mocha", "Nord", "Gruvbox Light", "OLED Black" }
                .All(name => catalog.Contains("[\"" + name + "\"]")));

        // App.xaml carries the startup brushes, shown before Apply runs:
        // they must be the FluentGruv dark palette itself.
        for (var i = 0; i < ResourceKeys.Length; i++)
        {
            var match = Regex.Match(appXaml,
                "<SolidColorBrush x:Key=\"" + ResourceKeys[i] + "\" Color=\"(#[0-9A-Fa-f]{6})\"/>");
            check($"App.xaml startup brush {ResourceKeys[i]} equals FluentGruv dark ({DarkValues[i]})",
                match.Success && string.Equals(match.Groups[1].Value, DarkValues[i], StringComparison.OrdinalIgnoreCase));
        }

        check("focus ring is the 2px accent ring (TOKENS.md section 2: 2px solid accent.primary)",
            appXaml.Contains("TargetName=\"FocusRing\" Property=\"BorderBrush\" Value=\"{DynamicResource StepAccent}\"")
            && appXaml.Contains("x:Name=\"FocusRing\" BorderBrush=\"Transparent\" BorderThickness=\"2\""));
        check("disabled text uses fg.muted at full strength, never a faded alpha blend (TOKENS.md section 4)",
            !appXaml.Contains("Property=\"Opacity\" Value=\"0.5\"")
            && appXaml.Contains("Property=\"IsEnabled\" Value=\"False\"><Setter Property=\"Foreground\" Value=\"{DynamicResource Muted}\""));

        // No hardcoded colors outside the two theme-definition files.
        var hex = new Regex("#[0-9A-Fa-f]{6,8}\\b");
        var offenders = Directory.EnumerateFiles(gui, "*", SearchOption.AllDirectories)
            .Where(path => (path.EndsWith(".xaml") || path.EndsWith(".cs"))
                && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                && Path.GetFileName(path) is not ("App.xaml" or "ThemeCatalog.cs"))
            .Where(path => hex.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetFileName(path)).ToList();
        check("no hardcoded hex colors in Gui XAML/code outside App.xaml and ThemeCatalog.cs",
            offenders.Count == 0);

        // Corner radii stay on the FluentGruv scale: standard 4, compact 2,
        // focus-ring wrapper 6 (4 + the 2px ring) — TOKENS.md section 2.
        var radii = new List<int>();
        foreach (var path in Directory.EnumerateFiles(gui, "*", SearchOption.AllDirectories)
            .Where(path => (path.EndsWith(".xaml") || path.EndsWith(".cs"))
                && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            var text = File.ReadAllText(path);
            radii.AddRange(Regex.Matches(text, "CornerRadius=\"(\\d+)\"").Select(m => int.Parse(m.Groups[1].Value)));
            radii.AddRange(Regex.Matches(text, "CornerRadius = new\\((\\d+)\\)").Select(m => int.Parse(m.Groups[1].Value)));
        }
        check("every corner radius in the Gui is on the FluentGruv scale (2/4/6)",
            radii.Count > 0 && radii.All(radius => radius is 2 or 4 or 6));
    }

    private static void PaletteMatches(string catalog, string field, string theme, string[] expected,
        Action<string, bool> check)
    {
        var block = Regex.Match(catalog, field + @" = new\((.*?)\);", RegexOptions.Singleline);
        var pairs = block.Success
            ? Regex.Matches(block.Groups[1].Value, "(\\w+):\\s*\"(#[0-9A-Fa-f]{6})\"")
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value)
            : new Dictionary<string, string>();
        for (var i = 0; i < Fields.Length; i++)
            check($"FluentGruv tokens.json v1.3.0 {theme} {Fields[i]} = {expected[i]}",
                pairs.TryGetValue(Fields[i], out var actual)
                && string.Equals(actual, expected[i], StringComparison.OrdinalIgnoreCase));
    }
}
