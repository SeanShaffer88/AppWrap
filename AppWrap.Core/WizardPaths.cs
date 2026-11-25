namespace AppWrap.Core;

/// <summary>
/// Text helpers behind the wizard's editable path fields. Every path the
/// wizard shows in an editable box can also be chosen in a dialog; these
/// helpers normalize what a user types or pastes (dialogs never produce
/// surrounding whitespace or quotes, pasted text often does) and decide how
/// an explicitly chosen destination reacts when the suggested destination
/// moves because the app name, version, mode or filename style changed.
/// Pure text logic — no host access — so the rules are check-covered.
/// </summary>
public static class WizardPaths
{
    /// <summary>
    /// Trims surrounding whitespace and one pair of surrounding double
    /// quotes (the shape Explorer's "Copy as path" produces). Returns ""
    /// for blank input; the interior of the path is never altered.
    /// </summary>
    public static string NormalizePathText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var value = text.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Trim();
        return value;
    }

    /// <summary>
    /// Case-insensitive path equality tolerant of surrounding quotes,
    /// whitespace and trailing directory separators. An empty path is
    /// never equal to anything, including another empty path: "no path"
    /// is the absence of a choice, not a location.
    /// </summary>
    public static bool SamePath(string? first, string? second)
    {
        var a = Comparable(first);
        var b = Comparable(second);
        return a.Length != 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The suggested-path reconciliation rule for an explicitly chosen
    /// destination: when the current value merely echoes the previously
    /// suggested path, it follows the suggestion to <paramref name="newSuggested"/>;
    /// a diverged (custom) value is the user's own text and is returned
    /// unchanged. An empty current value stays empty (no override).
    /// </summary>
    public static string ReconcileSuggestion(string? current, string? previousSuggested, string newSuggested)
    {
        var value = NormalizePathText(current);
        if (value.Length == 0) return "";
        return SamePath(value, previousSuggested) ? newSuggested : value;
    }

    private static string Comparable(string? path)
    {
        var value = NormalizePathText(path);
        if (value.Length == 0) return "";
        try { value = Path.GetFullPath(value); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
