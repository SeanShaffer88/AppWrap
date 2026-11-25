using System.Text.RegularExpressions;

namespace AppWrap.Core.Audit;

/// <summary>One registry value assignment parsed from a .reg file.</summary>
public sealed record RegValueEntry(string KeyPath, string ValueName, string Data, int Line);

/// <summary>One key deletion (<c>[-key]</c>) parsed from a .reg file.</summary>
public sealed record RegKeyDeletion(string KeyPath, int Line);

/// <summary>The parsed content of one .reg file: value writes and key deletions, in file order.</summary>
public sealed record RegFileParseResult(IReadOnlyList<RegValueEntry> Values, IReadOnlyList<RegKeyDeletion> Deletions, IReadOnlyList<string> Issues);

/// <summary>
/// A bounded parser for Windows Registry Editor (.reg) files, versions 4 and
/// 5.00. It is a data parser only: nothing is imported or executed. Hives are
/// normalized to the short forms PAL uses (HKCU, HKLM, HKCR, HKU, HKCC).
/// String data is unescaped (\\ and \"); dword: values keep their hex text;
/// hex(...) values keep their byte text. Line continuations in hex data are
/// joined. Malformed lines are collected as issues, never thrown.
/// </summary>
public static partial class RegFileParser
{
    public const int MaxBytes = 4_000_000;

    private static readonly Dictionary<string, string> Hives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HKEY_CURRENT_USER"] = "HKCU",
        ["HKEY_LOCAL_MACHINE"] = "HKLM",
        ["HKEY_CLASSES_ROOT"] = "HKCR",
        ["HKEY_USERS"] = "HKU",
        ["HKEY_CURRENT_CONFIG"] = "HKCC",
        ["HKCU"] = "HKCU", ["HKLM"] = "HKLM", ["HKCR"] = "HKCR", ["HKU"] = "HKU", ["HKCC"] = "HKCC",
    };

    /// <summary>Normalizes a registry path's hive to its short form; returns the input unchanged when the hive is unknown.</summary>
    public static string NormalizeKeyPath(string path)
    {
        var trimmed = path.Trim().Trim('[', ']');
        var cut = trimmed.IndexOf('\\');
        var hive = cut < 0 ? trimmed : trimmed[..cut];
        return Hives.TryGetValue(hive, out var shortHive)
            ? cut < 0 ? shortHive : shortHive + trimmed[cut..]
            : trimmed;
    }

    public static RegFileParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxBytes)
            throw new UserInputException("A .reg file in the source is too large to audit (up to 4 MB).");
        var values = new List<RegValueEntry>();
        var deletions = new List<RegKeyDeletion>();
        var issues = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string? currentKey = null;
        var i = 0;
        while (i < lines.Length)
        {
            var lineNo = i + 1;
            var line = lines[i].Trim();
            i++;
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith("Windows Registry Editor", StringComparison.OrdinalIgnoreCase)
                || line.Equals("REGEDIT4", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']')) { issues.Add($"Line {lineNo}: malformed key header."); continue; }
                var inner = line[1..^1];
                if (inner.StartsWith('-'))
                {
                    deletions.Add(new RegKeyDeletion(NormalizeKeyPath(inner[1..]), lineNo));
                    currentKey = null;
                }
                else currentKey = NormalizeKeyPath(inner);
                continue;
            }
            if (currentKey is null) { issues.Add($"Line {lineNo}: value outside any key section."); continue; }
            // Join hex continuations: a trailing backslash continues the data.
            while (line.EndsWith('\\') && i < lines.Length)
            {
                line = line[..^1] + lines[i].Trim();
                i++;
            }
            var match = ValueLine().Match(line);
            if (!match.Success) { issues.Add($"Line {lineNo}: unrecognized .reg line."); continue; }
            var name = match.Groups[1].Success ? Unescape(match.Groups[1].Value) : "";
            var data = match.Groups[2].Value.Trim();
            if (data == "-") continue; // value deletion: noted by absence, not a write
            values.Add(new RegValueEntry(currentKey, name, UnescapeData(data), lineNo));
        }
        return new RegFileParseResult(values, deletions, issues);
    }

    private static string UnescapeData(string data)
    {
        if (data.Length >= 2 && data[0] == '"' && data[^1] == '"') return Unescape(data[1..^1]);
        return data;
    }

    private static string Unescape(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && (value[i + 1] == '\\' || value[i + 1] == '"')) { sb.Append(value[i + 1]); i++; }
            else sb.Append(value[i]);
        }
        return sb.ToString();
    }

    // "name"=data  or  @=data  (the default value)
    [GeneratedRegex(@"^(?:""((?:\\.|[^""\\])*)""|@)\s*=\s*(.*)$")]
    private static partial Regex ValueLine();
}
