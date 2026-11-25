namespace AppWrap.Core.PortableProfiles;

/// <summary>
/// Typed, ordered views derived from a profile's launcher <see cref="IniDocument"/>.
/// These are computed views only — the document remains the single persisted model,
/// and nothing here is stored or executed. Semantics follow the pinned PAL 2.2.9
/// manual and segment sources (see docs/portable-capability-matrix.md).
/// </summary>
public sealed record LaunchSettings(
    string? ProgramExecutable,
    string? ProgramExecutable64,
    string? ProgramExecutableWhenParameters,
    string? ProgramExecutableWhenParameters64,
    string? CommandLineArguments,
    string? WorkingDirectory,
    string? MinOS,
    string? MaxOS,
    string? RunAsAdmin,
    bool? SinglePortableAppInstance,
    bool? SingleAppInstance,
    bool? WaitForProgram,
    bool? WaitForOtherInstances,
    bool? CleanTemp,
    bool? HideCommandLineWindow,
    int? SplashTime,
    bool? LaunchAppAfterSplash,
    string? RefreshShellIcons,
    string? DirectoryMoveOK,
    string? NoSpacesInPath,
    string? SupportsUNC,
    string? CloseEXE,
    IReadOnlyList<string> WaitForExe)
{
    /// <summary>PAL default: the launcher waits for the program unless told not to.</summary>
    public bool EffectiveWaitForProgram => WaitForProgram ?? true;
    /// <summary>PAL default: other running instances are waited for as well.</summary>
    public bool EffectiveWaitForOtherInstances => WaitForOtherInstances ?? true;
    /// <summary>PAL default: the contained temp folder is cleaned up.</summary>
    public bool EffectiveCleanTemp => CleanTemp ?? true;
}

public sealed record ActivateSettings(bool? Registry, string? Java, bool? Xml);

/// <summary>
/// One [FilesMove] or [DirectoriesMove] rule. For files, <see cref="HostTarget"/> is the
/// target directory; for directories it is the full target location (directory name
/// included). A portable-side key of <c>-</c> is PAL's protect-local form: the host
/// copy is backed up and kept, and portable changes are discarded
/// (<see cref="IsDiscard"/>, with an empty <see cref="PortablePath"/>).
/// </summary>
public sealed record MoveRule(string PortablePath, string HostTarget, bool IsDiscard, string Section, int Line);

/// <summary>
/// One [RegistryKeys] rule: portable changes to <see cref="HiveKey"/> are saved to
/// <c>Data\settings\<see cref="StorageName"/>.reg</c>. A storage name of <c>-</c>
/// discards portable changes while protecting the host key.
/// </summary>
public sealed record RegistryKeyRule(string StorageName, string HiveKey, bool IsDiscard, int Line);

/// <summary>
/// One [RegistryValueWrite] rule, parsed with the exact pinned PAL format verified in
/// <c>Other/Source/Segments/RegistryValueWrite.nsh</c> and the manual page
/// <c>Manual/ref/launcher.ini/registry.rst</c>: the entry key is the full registry
/// path with the value name appended after the final backslash (a trailing backslash
/// means the key's default value), and the entry value is <c>[REG_TYPE:]data</c>
/// where the type prefix is optional and defaults to <c>REG_SZ</c>. The PAL segment
/// scans the value for its first colon to split the type; this typed view only
/// accepts a well-formed <c>REG_*</c> token as the type prefix (the manual advises
/// writing the type explicitly whenever the data itself may contain a colon), so
/// colon-bearing string data without a type prefix stays intact as REG_SZ data.
/// </summary>
public sealed record RegistryValueWrite(string KeyPath, string ValueName, string Type, string Data, int Line)
{
    public static RegistryValueWrite Parse(IniEntryNode entry)
    {
        var full = entry.Key.Trim();
        string keyPath, valueName;
        if (full.EndsWith('\\'))
        {
            keyPath = full[..^1];
            valueName = "";
        }
        else
        {
            var cut = full.LastIndexOf('\\');
            if (cut < 0) { keyPath = full; valueName = ""; }
            else { keyPath = full[..cut]; valueName = full[(cut + 1)..]; }
        }
        var raw = entry.Value;
        var type = "REG_SZ";
        var data = raw;
        var colon = raw.IndexOf(':');
        if (colon > 0)
        {
            var prefix = raw[..colon];
            if (prefix.StartsWith("REG_", StringComparison.OrdinalIgnoreCase) && prefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                type = prefix.ToUpperInvariant();
                data = raw[(colon + 1)..];
            }
        }
        return new RegistryValueWrite(keyPath, valueName, type, data, entry.LineStart);
    }
}

public sealed record EnvironmentAssignment(string Name, string Value, int Line);

public sealed record FileWriteRule(int Index, IReadOnlyDictionary<string, string> Fields)
{
    public string? Get(string field) => Fields.TryGetValue(field, out var value) ? value : null;
}

public sealed record LanguageSettings(
    string? Base,
    string? Default,
    string? CheckIfExists,
    string? DefaultIfNotExists,
    IReadOnlyList<KeyValuePair<string, string>> Strings,
    IReadOnlyDictionary<string, string> FileFields)
{
    public string? FileField(string field) => FileFields.TryGetValue(field, out var value) ? value : null;
}

/// <summary>All typed launcher views for one profile, in document order.</summary>
public sealed record LauncherRules(
    IniDocument Document,
    LaunchSettings Launch,
    ActivateSettings Activate,
    IReadOnlyList<MoveRule> FileMoves,
    IReadOnlyList<MoveRule> DirectoryMoves,
    IReadOnlyList<RegistryKeyRule> RegistryKeys,
    IReadOnlyList<RegistryValueWrite> RegistryValueWrites,
    IReadOnlyList<string> RegistryValueBackupDelete,
    IReadOnlyList<string> RegistryCleanupIfEmpty,
    IReadOnlyList<string> RegistryCleanupForce,
    IReadOnlyList<string> QtKeysCleanup,
    IReadOnlyList<string> DirectoriesCleanupIfEmpty,
    IReadOnlyList<string> DirectoriesCleanupForce,
    IReadOnlyList<EnvironmentAssignment> Environment,
    IReadOnlyList<FileWriteRule> FileWrites,
    LanguageSettings Language);

public static class ProfileRules
{
    public static LauncherRules From(PortableProfile profile) => FromLauncher(profile.Launcher);

    public static LauncherRules FromLauncher(IniDocument doc)
    {
        string? Get(string section, string key) => doc.GetValue(section, key);
        bool? GetBool(string section, string key) => ParseBool(doc.GetValue(section, key));
        int? GetInt(string section, string key) =>
            int.TryParse(doc.GetValue(section, key), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

        var waitForExe = doc.Entries("Launch")
            .Where(e => CapabilityCatalog.IsWaitForExeKey(e.Key))
            .OrderBy(e => int.Parse(e.Key.AsSpan("WaitForEXE".Length)))
            .Select(e => e.Value)
            .ToList();

        var launch = new LaunchSettings(
            Get("Launch", "ProgramExecutable"),
            Get("Launch", "ProgramExecutable64"),
            Get("Launch", "ProgramExecutableWhenParameters"),
            Get("Launch", "ProgramExecutableWhenParameters64"),
            Get("Launch", "CommandLineArguments"),
            Get("Launch", "WorkingDirectory"),
            Get("Launch", "MinOS"),
            Get("Launch", "MaxOS"),
            Get("Launch", "RunAsAdmin"),
            GetBool("Launch", "SinglePortableAppInstance"),
            GetBool("Launch", "SingleAppInstance"),
            GetBool("Launch", "WaitForProgram"),
            GetBool("Launch", "WaitForOtherInstances"),
            GetBool("Launch", "CleanTemp"),
            GetBool("Launch", "HideCommandLineWindow"),
            GetInt("Launch", "SplashTime"),
            GetBool("Launch", "LaunchAppAfterSplash"),
            Get("Launch", "RefreshShellIcons"),
            Get("Launch", "DirectoryMoveOK"),
            Get("Launch", "NoSpacesInPath"),
            Get("Launch", "SupportsUNC"),
            Get("Launch", "CloseEXE"),
            waitForExe);

        var activate = new ActivateSettings(
            GetBool("Activate", "Registry"),
            Get("Activate", "Java"),
            GetBool("Activate", "XML"));

        List<MoveRule> Moves(string section) => doc.Entries(section)
            .Select(e => new MoveRule(
                e.Key == "-" ? "" : e.Key,
                e.Value,
                e.Key == "-",
                e.Section!,
                e.LineStart))
            .ToList();

        var registryKeys = doc.Entries("RegistryKeys")
            .Select(e => new RegistryKeyRule(e.Key, e.Value, e.Key == "-", e.LineStart))
            .ToList();

        var registryValueWrites = doc.Entries("RegistryValueWrite")
            .Select(RegistryValueWrite.Parse)
            .ToList();

        var fileWrites = doc.Sections
            .Where(s => CapabilityCatalog.IsPatternSection(s.Name, out var p, out _) && p == "FileWrite")
            .Select(s =>
            {
                CapabilityCatalog.IsPatternSection(s.Name, out _, out var n);
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in doc.Entries(s.Name))
                    fields.TryAdd(e.Key, e.Value);
                return new FileWriteRule(n, fields);
            })
            .OrderBy(r => r.Index)
            .ToList();

        var language = new LanguageSettings(
            Get("Language", "Base"),
            Get("Language", "Default"),
            Get("Language", "CheckIfExists"),
            Get("Language", "DefaultIfNotExists"),
            doc.Entries("LanguageStrings").Select(e => new KeyValuePair<string, string>(e.Key, e.Value)).ToList(),
            doc.Entries("LanguageFile").Aggregate(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                (fields, e) => { fields.TryAdd(e.Key, e.Value); return fields; }));

        return new LauncherRules(
            doc,
            launch,
            activate,
            Moves("FilesMove"),
            Moves("DirectoriesMove"),
            registryKeys,
            registryValueWrites,
            SequentialValues(doc, "RegistryValueBackupDelete"),
            SequentialValues(doc, "RegistryCleanupIfEmpty"),
            SequentialValues(doc, "RegistryCleanupForce"),
            SequentialValues(doc, "QtKeysCleanup"),
            SequentialValues(doc, "DirectoriesCleanupIfEmpty"),
            SequentialValues(doc, "DirectoriesCleanupForce"),
            doc.Entries("Environment").Select(e => new EnvironmentAssignment(e.Key, e.Value, e.LineStart)).ToList(),
            fileWrites,
            language);
    }

    /// <summary>Values of a sequential (1, 2, 3…) section in numeric order.</summary>
    internal static IReadOnlyList<string> SequentialValues(IniDocument doc, string section) =>
        doc.Entries(section)
            .Where(e => int.TryParse(e.Key, out _))
            .OrderBy(e => int.Parse(e.Key))
            .Select(e => e.Value)
            .ToList();

    /// <summary>Sequential entries with their source lines, for planning and diagnostics.</summary>
    internal static IReadOnlyList<IniEntryNode> SequentialEntries(IniDocument doc, string section) =>
        doc.Entries(section)
            .Where(e => int.TryParse(e.Key, out _))
            .OrderBy(e => int.Parse(e.Key))
            .ToList();

    private static bool? ParseBool(string? value)
    {
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }
}
