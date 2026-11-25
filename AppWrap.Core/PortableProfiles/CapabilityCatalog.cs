namespace AppWrap.Core.PortableProfiles;

public enum CapabilityStatus { Supported, Expert, Unsupported, Deprecated, Unrecognized }

public enum PortableDocumentKind { Launcher, Installer, Metadata, UserOverrides }

/// <summary>
/// Versioned knowledge of the pinned PAL 2.2.9 launcher surface (see docs/portable-capability-matrix.md).
/// Canonical section spellings matter: PAL's ForEachINIPair enumerates raw section headers case-sensitively.
/// </summary>
public static class CapabilityCatalog
{
    public const string CatalogVersion = "pal-2.2.9-1";

    private static readonly Dictionary<string, CapabilityStatus> LaunchKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProgramExecutable"] = CapabilityStatus.Supported,
        ["ProgramExecutable64"] = CapabilityStatus.Supported,
        ["ProgramExecutableWhenParameters"] = CapabilityStatus.Supported,
        ["ProgramExecutableWhenParameters64"] = CapabilityStatus.Supported,
        ["CommandLineArguments"] = CapabilityStatus.Supported,
        ["WorkingDirectory"] = CapabilityStatus.Supported,
        ["MinOS"] = CapabilityStatus.Expert,
        ["MaxOS"] = CapabilityStatus.Expert,
        ["RunAsAdmin"] = CapabilityStatus.Expert,
        ["RunAsAdmin2000"] = CapabilityStatus.Expert,
        ["RunAsAdminXP"] = CapabilityStatus.Expert,
        ["RunAsAdmin2003"] = CapabilityStatus.Expert,
        ["RunAsAdminVista"] = CapabilityStatus.Expert,
        ["RunAsAdmin2008"] = CapabilityStatus.Expert,
        ["RunAsAdmin7"] = CapabilityStatus.Expert,
        ["RunAsAdmin2008R2"] = CapabilityStatus.Expert,
        ["SinglePortableAppInstance"] = CapabilityStatus.Supported,
        ["SingleAppInstance"] = CapabilityStatus.Supported,
        ["CloseEXE"] = CapabilityStatus.Supported,
        ["WaitForProgram"] = CapabilityStatus.Supported,
        ["WaitForOtherInstances"] = CapabilityStatus.Supported,
        ["CleanTemp"] = CapabilityStatus.Supported,
        ["SplashTime"] = CapabilityStatus.Supported,
        ["LaunchAppAfterSplash"] = CapabilityStatus.Supported,
        ["RefreshShellIcons"] = CapabilityStatus.Supported,
        ["HideCommandLineWindow"] = CapabilityStatus.Supported,
        ["DirectoryMoveOK"] = CapabilityStatus.Supported,
        ["NoSpacesInPath"] = CapabilityStatus.Supported,
        ["SupportsUNC"] = CapabilityStatus.Supported,
        ["AppName"] = CapabilityStatus.Deprecated,
    };

    private static readonly Dictionary<string, CapabilityStatus> ActivateKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Registry"] = CapabilityStatus.Supported,
        ["Java"] = CapabilityStatus.Supported,
        ["XML"] = CapabilityStatus.Supported,
    };

    private static readonly Dictionary<string, CapabilityStatus> LiveModeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CopyApp"] = CapabilityStatus.Expert,
        ["CopyData"] = CapabilityStatus.Unsupported,
    };

    private static readonly Dictionary<string, CapabilityStatus> LanguageKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Base"] = CapabilityStatus.Supported,
        ["Default"] = CapabilityStatus.Supported,
        ["CheckIfExists"] = CapabilityStatus.Supported,
        ["DefaultIfNotExists"] = CapabilityStatus.Supported,
    };

    private static readonly Dictionary<string, CapabilityStatus> LanguageFileKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Type"] = CapabilityStatus.Supported, ["File"] = CapabilityStatus.Supported, ["Entry"] = CapabilityStatus.Supported,
        ["Section"] = CapabilityStatus.Supported, ["Key"] = CapabilityStatus.Supported, ["Attribute"] = CapabilityStatus.Supported,
        ["XPath"] = CapabilityStatus.Supported, ["CaseSensitive"] = CapabilityStatus.Supported, ["TrimRight"] = CapabilityStatus.Supported,
    };

    private static readonly Dictionary<string, CapabilityStatus> FileWriteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Type"] = CapabilityStatus.Supported, ["File"] = CapabilityStatus.Supported, ["Entry"] = CapabilityStatus.Supported,
        ["Section"] = CapabilityStatus.Supported, ["Key"] = CapabilityStatus.Supported, ["Value"] = CapabilityStatus.Supported,
        ["Find"] = CapabilityStatus.Supported, ["Replace"] = CapabilityStatus.Supported, ["Attribute"] = CapabilityStatus.Supported,
        ["XPath"] = CapabilityStatus.Supported, ["CaseSensitive"] = CapabilityStatus.Supported, ["Encoding"] = CapabilityStatus.Supported,
    };

    private static readonly Dictionary<string, CapabilityStatus> ServiceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Name"] = CapabilityStatus.Unsupported, ["Path"] = CapabilityStatus.Unsupported, ["Type"] = CapabilityStatus.Unsupported,
        ["User"] = CapabilityStatus.Unsupported, ["Display"] = CapabilityStatus.Unsupported, ["Dependencies"] = CapabilityStatus.Unsupported,
        ["Description"] = CapabilityStatus.Unsupported, ["IfExists"] = CapabilityStatus.Unsupported,
    };

    /// <summary>Sections whose keys are arbitrary (any key carries the section's status).</summary>
    private static readonly Dictionary<string, CapabilityStatus> ArbitrarySections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Environment"] = CapabilityStatus.Supported,
        ["RegistryKeys"] = CapabilityStatus.Expert,
        ["RegistryValueWrite"] = CapabilityStatus.Expert,
        ["FilesMove"] = CapabilityStatus.Supported,
        ["DirectoriesMove"] = CapabilityStatus.Expert,
        ["LanguageStrings"] = CapabilityStatus.Supported,
    };

    /// <summary>Sections whose keys are sequential numbers (1, 2, ...).</summary>
    private static readonly Dictionary<string, CapabilityStatus> SequentialSections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RegistryValueBackupDelete"] = CapabilityStatus.Supported,
        ["RegistryCleanupIfEmpty"] = CapabilityStatus.Expert,
        ["RegistryCleanupForce"] = CapabilityStatus.Expert,
        ["QtKeysCleanup"] = CapabilityStatus.Expert,
        ["DirectoriesCleanupIfEmpty"] = CapabilityStatus.Supported,
        ["DirectoriesCleanupForce"] = CapabilityStatus.Expert,
    };

    private static readonly Dictionary<string, Dictionary<string, CapabilityStatus>> FixedSections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Launch"] = LaunchKeys,
        ["Activate"] = ActivateKeys,
        ["LiveMode"] = LiveModeKeys,
        ["Language"] = LanguageKeys,
        ["LanguageFile"] = LanguageFileKeys,
    };

    /// <summary>Canonical spellings of all non-pattern launcher sections.</summary>
    public static IReadOnlyList<string> CanonicalSections { get; } =
        [.. FixedSections.Keys, .. ArbitrarySections.Keys, .. SequentialSections.Keys];

    /// <summary>Sections PAL enumerates with ForEachINIPair (case-sensitive raw headers).</summary>
    public static IReadOnlySet<string> EnumeratedSections { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Environment", "RegistryKeys", "RegistryValueWrite", "RegistryValueBackupDelete", "RegistryCleanupIfEmpty",
        "RegistryCleanupForce", "QtKeysCleanup", "FilesMove", "DirectoriesMove", "DirectoriesCleanupIfEmpty",
        "DirectoriesCleanupForce", "LanguageStrings",
    };

    /// <summary>Documented PAL/host environment tokens (names without surrounding % signs).</summary>
    public static IReadOnlySet<string> DocumentedTokens { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PAL:AppDir", "PAL:DataDir", "PAL:PortableAppsDir", "PAL:PortableAppsBaseDir", "PAL:LastPortableAppsBaseDir",
        "PAL:PackagePartialDir", "PAL:LastPackagePartialDir",
        "APPDATA", "LOCALAPPDATA", "USERPROFILE", "ALLUSERSPROFILE", "ALLUSERSAPPDATA", "DOCUMENTS", "TEMP",
        "PAL:Drive", "PAL:LastDrive", "PAL:DriveLetter", "PAL:LastDriveLetter",
        "JAVA_HOME",
    };

    public static bool IsPatternSection(string section, out string prefix, out int number)
    {
        prefix = ""; number = 0;
        foreach (var p in new[] { "FileWrite", "Service" })
        {
            if (section.Length > p.Length && section.StartsWith(p, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(section.AsSpan(p.Length), out var n) && n >= 1 && section.AsSpan(p.Length).ToString().All(char.IsAsciiDigit))
            {
                prefix = p; number = n; return true;
            }
        }
        return false;
    }

    public static bool IsKnownSection(string section)
    {
        if (CanonicalSections.Any(s => string.Equals(s, section, StringComparison.OrdinalIgnoreCase))) return true;
        return IsPatternSection(section, out _, out _);
    }

    public static bool TryGetCanonicalSection(string section, out string canonical)
    {
        canonical = CanonicalSections.FirstOrDefault(s => string.Equals(s, section, StringComparison.OrdinalIgnoreCase)) ?? "";
        if (canonical.Length > 0) return true;
        if (IsPatternSection(section, out var prefix, out var n)) { canonical = prefix + n.ToString(); return true; }
        return false;
    }

    public static bool IsSequentialSection(string section) => SequentialSections.ContainsKey(section);
    public static bool IsArbitrarySection(string section) => ArbitrarySections.ContainsKey(section);

    /// <summary>Singleton keys are the fixed catalog keys of [Launch], [Activate], [LiveMode] and [Language].</summary>
    public static bool IsSingleton(string section, string key)
    {
        if (string.Equals(section, "Launch", StringComparison.OrdinalIgnoreCase) && IsWaitForExeKey(key)) return false;
        return (string.Equals(section, "Launch", StringComparison.OrdinalIgnoreCase) && LaunchKeys.ContainsKey(key))
            || (string.Equals(section, "Activate", StringComparison.OrdinalIgnoreCase) && ActivateKeys.ContainsKey(key))
            || (string.Equals(section, "LiveMode", StringComparison.OrdinalIgnoreCase) && LiveModeKeys.ContainsKey(key))
            || (string.Equals(section, "Language", StringComparison.OrdinalIgnoreCase) && LanguageKeys.ContainsKey(key));
    }

    public static bool IsWaitForExeKey(string key) =>
        key.Length > "WaitForEXE".Length && key.StartsWith("WaitForEXE", StringComparison.OrdinalIgnoreCase) &&
        key.AsSpan("WaitForEXE".Length).ToString().All(char.IsAsciiDigit);

    public static CapabilityStatus Classify(PortableDocumentKind documentKind, string section, string key)
    {
        if (documentKind != PortableDocumentKind.Launcher) return CapabilityStatus.Unrecognized;
        return Classify(section, key);
    }

    public static CapabilityStatus Classify(string documentKind, string section, string key) =>
        Enum.TryParse<PortableDocumentKind>(documentKind, ignoreCase: true, out var kind)
            ? Classify(kind, section, key)
            : CapabilityStatus.Unrecognized;

    public static CapabilityStatus Classify(string section, string key)
    {
        if (IsPatternSection(section, out var prefix, out _))
        {
            var table = prefix == "FileWrite" ? FileWriteKeys : ServiceKeys;
            return table.TryGetValue(key, out var s) ? s : CapabilityStatus.Unrecognized;
        }
        if (ArbitrarySections.TryGetValue(section, out var arb)) return arb;
        if (SequentialSections.TryGetValue(section, out var seq))
            return key.Length > 0 && key.All(char.IsAsciiDigit) ? seq : CapabilityStatus.Unrecognized;
        if (FixedSections.TryGetValue(section, out var fixedTable))
        {
            if (string.Equals(section, "Launch", StringComparison.OrdinalIgnoreCase) && IsWaitForExeKey(key))
                return CapabilityStatus.Supported;
            return fixedTable.TryGetValue(key, out var s) ? s : CapabilityStatus.Unrecognized;
        }
        return CapabilityStatus.Unrecognized;
    }
}
