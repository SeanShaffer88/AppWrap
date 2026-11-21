using AppWrap.Core;
using System.Text;

namespace AppWrap.Generators;

public static class PortableAppsGenerator
{
    public static string PackageId(AppWrapInput input) => InputRules.SafeId(input.AppName).Replace("-", "", StringComparison.Ordinal) + "Portable";
    public static string Generate(AppWrapInput input, string? previousMetadata = null)
    {
        static string Safe(string value)
        {
            if (value.Any(char.IsControl) || value.IndexOfAny(['$', '`', '"']) >= 0 || value.Contains("{#", StringComparison.Ordinal))
                throw new UserInputException("For a portable package, choose a name and launch file without quote, dollar, or backtick symbols.");
            return value;
        }
        var old = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string section = "";
        if (previousMetadata is { Length: > 65536 }) throw new UserInputException("The app's existing package details are too large.");
        foreach (var line in (previousMetadata ?? "").Split('\n'))
        {
            var text = line.Trim();
            if (text.StartsWith('[') && text.EndsWith(']')) section = text[1..^1];
            else if (!text.StartsWith(';') && text.IndexOf('=') is > 0 and var index)
                old[section + "/" + text[..index].Trim()] = text[(index + 1)..].Trim();
        }
        string Keep(string key, string fallback) => old.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? Safe(value) : fallback;
        string License(string key) => old.TryGetValue("License/" + key, out var value) && (value == "true" || value == "false") ? value : "false";
        var categories = new[] { "Accessibility", "Development", "Education", "Games", "Graphics & Pictures", "Internet", "Music & Video", "Office", "Security", "Utilities" };
        var category = Keep("Details/Category", "Utilities"); if (!categories.Contains(category)) category = "Utilities";
        return $"""
            [Format]
            Type=PortableApps.comFormat
            Version=3.9
            [Details]
            Name={Safe(input.AppName)} Portable
            AppID={PackageId(input)}
            Publisher={Keep("Details/Publisher", "Publisher not supplied")}
            Homepage={Keep("Details/Homepage", "https://example.invalid/")}
            Category={category}
            Description={Keep("Details/Description", "Application packaged with AppWrap")}
            Language={Keep("Details/Language", "English")}
            [License]
            Shareable={License("Shareable")}
            OpenSource={License("OpenSource")}
            Freeware={License("Freeware")}
            CommercialUse={License("CommercialUse")}
            [Version]
            PackageVersion={InputRules.PackageVersion(input.AppVersion)}
            DisplayVersion={Safe(input.AppVersion)}
            [Control]
            Icons=1
            Start={PackageId(input)}.exe
            BaseAppID=NOT-NEEDED

            """;
    }
}
