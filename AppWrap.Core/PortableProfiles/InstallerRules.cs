namespace AppWrap.Core.PortableProfiles;

/// <summary>One numbered installer list entry (<c>PreserveDirectory3</c>, <c>OptionalFile2</c>, …) with its source line.</summary>
public sealed record InstallerListEntry(int Index, string Value, int Line);

/// <summary>[DirectoriesToPreserve] / [DirectoriesToRemove] / [FilesToPreserve] / [FilesToRemove] upgrade lists.</summary>
public sealed record PreserveRemoveRules(
    IReadOnlyList<InstallerListEntry> DirectoriesToPreserve,
    IReadOnlyList<InstallerListEntry> DirectoriesToRemove,
    IReadOnlyList<InstallerListEntry> FilesToPreserve,
    IReadOnlyList<InstallerListEntry> FilesToRemove);

/// <summary>
/// [MainDirectories] upgrade switches. <see cref="HasRemoveDataDirectory"/> records the
/// mere presence of the <c>RemoveDataDirectory</c> key: it is not part of the current
/// published PortableApps.com Format (see docs/portable-capability-matrix.md), so the
/// validator rejects it rather than treating it as a user-data wipe switch.
/// </summary>
public sealed record MainDirectoriesRules(
    bool? RemoveAppDirectory,
    bool? RemoveOtherDirectory,
    bool HasRemoveDataDirectory);

/// <summary>
/// [OptionalComponents] family. The payload entries are the numbered
/// <c>OptionalDirectoryN</c> / <c>OptionalFileN</c> lists; everything else describes
/// the optional section itself. <c>OptionalSectionInstalledWhenSilentOn32Bit</c> is a
/// source-supported extension (verified in the pinned installer wizard), not part of
/// the public manual, and is therefore Expert.
/// </summary>
public sealed record OptionalComponentsRules(
    bool? OptionalComponents,
    string? MainSectionTitle,
    string? MainSectionDescription,
    string? OptionalSectionTitle,
    string? OptionalSectionDescription,
    string? OptionalSectionSelectedInstallType,
    string? OptionalSectionNotSelectedInstallType,
    bool? OptionalSectionPreSelectedIfNonEnglishInstall,
    bool? OptionalSectionPreSelectedIfWindows32Bit,
    string? OptionalSectionPreSelectedIfLessThanWindows,
    bool? OptionalSectionInstalledWhenSilent,
    bool? OptionalSectionInstalledWhenSilentOn32Bit,
    IReadOnlyList<InstallerListEntry> OptionalDirectories,
    IReadOnlyList<InstallerListEntry> OptionalFiles);

/// <summary>
/// One download block of [DownloadFiles]. The primary block uses the unprefixed keys
/// (<c>DownloadURL</c>, …); the secondary block uses the <c>Download2</c>-prefixed
/// keys. <see cref="Prefix"/> is "" or "2".
/// </summary>
public sealed record DownloadBlock(
    string Prefix,
    string? Url,
    string? KnockUrl,
    string? Name,
    string? Filename,
    string? Sha256,
    string? To,
    string? InnoExtractTo,
    string? InnoExtractFrom,
    IReadOnlyList<KeyValuePair<string, string>> AdvancedExtract,
    string? DoubleExtractFilename,
    IReadOnlyList<KeyValuePair<string, string>> DoubleExtract)
{
    /// <summary>A block is enabling when it names a download URL.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Url);
}

/// <summary>[DownloadFiles] family: two download blocks plus extraction/7-Zip switches.</summary>
public sealed record DownloadFilesRules(
    bool HasSection,
    int EntryCount,
    string? AdditionalInstallSize,
    DownloadBlock Primary,
    DownloadBlock Secondary,
    bool? CustomCodeUses7zip,
    bool? UseLightweight7Zip);

/// <summary>
/// [CopyLocalFiles] family: reuse of an existing local installation at package
/// install time. Enabling means <c>CopyLocalFiles=true</c> or any
/// <c>CopyFrom*</c>/<c>CopyTo*</c> key is set.
/// </summary>
public sealed record CopyLocalFilesRules(
    bool? CopyLocalFiles,
    string? CopyFromRegPath,
    string? CopyFromRegKey,
    string? CopyFromRegRemoveDirectories,
    string? CopyFromDirectory,
    string? CopyToDirectory)
{
    public bool IsEnabled => CopyLocalFiles == true
        || CopyFromRegPath is not null || CopyFromRegKey is not null
        || CopyFromRegRemoveDirectories is not null || CopyFromDirectory is not null
        || CopyToDirectory is not null;
}

/// <summary>
/// [LegacyVersions] family, kept as two raw groups because the published key
/// spellings combine a group marker (32-bit / OS) with a suffix
/// (AppIDSuffix, AppNameSuffix, CutoffAppVersion, CutoffOSVersion,
/// RemoveAppSubDirectory, CustomLegacyAppID). Any entry makes the family present.
/// </summary>
public sealed record LegacyVersionsRules(
    bool HasSection,
    IReadOnlyList<KeyValuePair<string, string>> Legacy32,
    IReadOnlyList<KeyValuePair<string, string>> LegacyOs)
{
    public bool HasAnyEntry => Legacy32.Count > 0 || LegacyOs.Count > 0;
}

/// <summary>
/// Plugin / common-files package mode, declared through [Details] PluginName /
/// PluginType in the metadata (appinfo) document or an installer document.
/// </summary>
public sealed record PluginModeRules(bool IsPluginMode, string? PluginName, string? PluginType);

/// <summary>All typed views derived from a profile's installer (installer.ini) document.</summary>
public sealed record InstallerDocumentRules(
    IniDocument Document,
    PreserveRemoveRules PreserveRemove,
    MainDirectoriesRules MainDirectories,
    OptionalComponentsRules OptionalComponents,
    bool HasLanguagesSection,
    IReadOnlyList<KeyValuePair<string, bool?>> Languages,
    DownloadFilesRules Downloads,
    CopyLocalFilesRules CopyLocal,
    LegacyVersionsRules Legacy,
    PluginModeRules Plugin,
    string? CheckRunningCloseExe,
    string? CheckRunningCloseName,
    bool? IncludeInstallerSource);

/// <summary>appinfo.ini [Dependencies] declarations. Declarations only — they install nothing.</summary>
public sealed record MetadataDependencies(
    string? UsesGhostscript,
    string? UsesJava,
    string? UsesDotNetVersion,
    bool? Requires64bitOS,
    string? RequiresPortableApp,
    bool? RequiresAdmin);

/// <summary>One additional [Control] start entry (<c>Start2</c> with its optional <c>Name2</c>).</summary>
public sealed record MetadataControlStart(int Index, string Start, string? Name);

/// <summary>appinfo.ini [Control] section.</summary>
public sealed record MetadataControl(
    string? Icons,
    string? Start,
    IReadOnlyList<MetadataControlStart> AdditionalStarts,
    string? ExtractIcon,
    string? ExtractName,
    string? BaseAppID,
    string? BaseAppID64,
    string? BaseAppIDARM64,
    string? ExitEXE,
    string? ExitParameters);

/// <summary>
/// All typed views derived from a profile's metadata (appinfo.ini) document.
/// <see cref="DeclaredInstallTypes"/> is the comma-separated token list of
/// [Details] InstallType, in declared order.
/// </summary>
public sealed record MetadataDocumentRules(
    IniDocument? Document,
    string? FormatType,
    string? FormatVersion,
    string? Name,
    string? AppID,
    string? BaseAppName,
    string? Publisher,
    string? Homepage,
    string? Donate,
    string? Category,
    string? Description,
    string? Language,
    string? Trademarks,
    string? InstallType,
    IReadOnlyList<string> DeclaredInstallTypes,
    bool? LicenseShareable,
    bool? LicenseOpenSource,
    bool? LicenseFreeware,
    bool? LicenseCommercialUse,
    string? LicenseEulaVersion,
    string? PackageVersion,
    string? DisplayVersion,
    string? PluginsPath,
    MetadataDependencies Dependencies,
    MetadataControl Control);

/// <summary>The installer + metadata typed surface of one profile.</summary>
public sealed record InstallerSurface(InstallerDocumentRules Installer, MetadataDocumentRules Metadata);

/// <summary>
/// Typed, ordered views derived from a profile's installer and metadata
/// <see cref="IniDocument"/>s. Like <see cref="ProfileRules"/>, these are computed
/// views only — the documents remain the single persisted model. Semantics follow
/// docs/portable-capability-matrix.md (PortableApps.com Format 3.9 /
/// Installer 3.9.24 configuration surface).
/// </summary>
public static class InstallerRules
{
    /// <summary>
    /// Documented [Languages] keys of the PortableApps.com Format: the installer's
    /// language names in their published upper-case form. A key outside this set is
    /// not automatically wrong (the pinned source supports extensions), so the
    /// validator only warns about it.
    /// </summary>
    public static IReadOnlySet<string> KnownLanguages { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AFRIKAANS", "ALBANIAN", "ARABIC", "ARMENIAN", "BASQUE", "BELARUSIAN", "BOSNIAN", "BRETON",
        "BULGARIAN", "CATALAN", "CIBEMBA", "CROATIAN", "CZECH", "DANISH", "DUTCH", "EFIK", "ENGLISH",
        "ESTONIAN", "FARSI", "FINNISH", "FRENCH", "GALICIAN", "GEORGIAN", "GERMAN", "GREEK", "HEBREW",
        "HUNGARIAN", "ICELANDIC", "IGBO", "INDONESIAN", "IRISH", "ITALIAN", "JAPANESE", "KHMER",
        "KOREAN", "KURDISH", "LATVIAN", "LITHUANIAN", "LUXEMBOURGISH", "MACEDONIAN", "MALAY",
        "MALTESE", "MONGOLIAN", "NORWEGIAN", "NORWEGIANNYNORSK", "PASHTO", "POLISH", "PORTUGUESE",
        "PORTUGUESEBR", "ROMANIAN", "RUSSIAN", "SERBIAN", "SERBIANLATIN", "SIMPCHINESE", "SLOVAK",
        "SLOVENIAN", "SOMALI", "SPANISH", "SPANISHINTERNATIONAL", "SWAHILI", "SWEDISH", "SYRIAC",
        "THAI", "TRADCHINESE", "TURKISH", "UKRAINIAN", "UZBEK", "VIETNAMESE", "WELSH", "YORUBA",
    };

    /// <summary>
    /// The PortableApps.com Format's built-in install types. Optional-section
    /// install-type keys may also name any type the package itself declares in
    /// metadata [Details] InstallType (see <see cref="MetadataDocumentRules.DeclaredInstallTypes"/>).
    /// </summary>
    public static IReadOnlyList<string> BuiltInInstallTypes { get; } = ["English", "Multilingual"];

    public static InstallerSurface From(PortableProfile profile)
    {
        var installer = FromInstaller(profile.Installer);
        var metadata = FromMetadata(profile.Metadata);
        // Plugin mode may be declared in either document; the combined surface sees both.
        return new InstallerSurface(
            installer with { Plugin = ParsePlugin(profile.Installer, profile.Metadata) },
            metadata);
    }

    public static InstallerDocumentRules FromInstaller(IniDocument doc)
    {
        string? Get(string section, string key) => doc.GetValue(section, key);
        bool? GetBool(string section, string key) => ParseBool(doc.GetValue(section, key));

        var languages = doc.Entries("Languages")
            .Select(e => new KeyValuePair<string, bool?>(e.Key, ParseBool(e.Value)))
            .ToList();

        return new InstallerDocumentRules(
            doc,
            new PreserveRemoveRules(
                Numbered(doc, "DirectoriesToPreserve", "PreserveDirectory"),
                Numbered(doc, "DirectoriesToRemove", "RemoveDirectory"),
                Numbered(doc, "FilesToPreserve", "PreserveFile"),
                Numbered(doc, "FilesToRemove", "RemoveFile")),
            new MainDirectoriesRules(
                GetBool("MainDirectories", "RemoveAppDirectory"),
                GetBool("MainDirectories", "RemoveOtherDirectory"),
                doc.Entries("MainDirectories").Any(e => string.Equals(e.Key, "RemoveDataDirectory", StringComparison.OrdinalIgnoreCase))),
            new OptionalComponentsRules(
                GetBool("OptionalComponents", "OptionalComponents"),
                Get("OptionalComponents", "MainSectionTitle"),
                Get("OptionalComponents", "MainSectionDescription"),
                Get("OptionalComponents", "OptionalSectionTitle"),
                Get("OptionalComponents", "OptionalSectionDescription"),
                Get("OptionalComponents", "OptionalSectionSelectedInstallType"),
                Get("OptionalComponents", "OptionalSectionNotSelectedInstallType"),
                GetBool("OptionalComponents", "OptionalSectionPreSelectedIfNonEnglishInstall"),
                GetBool("OptionalComponents", "OptionalSectionPreSelectedIfWindows32Bit"),
                Get("OptionalComponents", "OptionalSectionPreSelectedIfLessThanWindows"),
                GetBool("OptionalComponents", "OptionalSectionInstalledWhenSilent"),
                GetBool("OptionalComponents", "OptionalSectionInstalledWhenSilentOn32Bit"),
                Numbered(doc, "OptionalComponents", "OptionalDirectory"),
                Numbered(doc, "OptionalComponents", "OptionalFile")),
            doc.HasSection("Languages"),
            languages,
            ParseDownloads(doc),
            new CopyLocalFilesRules(
                GetBool("CopyLocalFiles", "CopyLocalFiles"),
                Get("CopyLocalFiles", "CopyFromRegPath"),
                Get("CopyLocalFiles", "CopyFromRegKey"),
                Get("CopyLocalFiles", "CopyFromRegRemoveDirectories"),
                Get("CopyLocalFiles", "CopyFromDirectory"),
                Get("CopyLocalFiles", "CopyToDirectory")),
            ParseLegacy(doc),
            ParsePlugin(doc, null),
            Get("CheckRunning", "CloseEXE"),
            Get("CheckRunning", "CloseName"),
            GetBool("Source", "IncludeInstallerSource"));
    }

    public static MetadataDocumentRules FromMetadata(IniDocument? doc)
    {
        if (doc is null)
        {
            return new MetadataDocumentRules(
                null, null, null, null, null, null, null, null, null, null, null, null, null, null,
                [], null, null, null, null, null, null, null, null,
                new MetadataDependencies(null, null, null, null, null, null),
                new MetadataControl(null, null, [], null, null, null, null, null, null, null));
        }
        string? Get(string section, string key) => doc.GetValue(section, key);
        bool? GetBool(string section, string key) => ParseBool(doc.GetValue(section, key));

        var installType = Get("Details", "InstallType");
        var declaredInstallTypes = installType is null
            ? (IReadOnlyList<string>)[]
            : installType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var starts = doc.Entries("Control")
            .Select(e => (Entry: e, Number: NumberSuffix(e.Key, "Start")))
            .Where(x => x.Number is not null)
            .OrderBy(x => x.Number)
            .Select(x => new MetadataControlStart(
                x.Number!.Value,
                x.Entry.Value,
                doc.GetValue("Control", "Name" + x.Number.Value)))
            .ToList();

        return new MetadataDocumentRules(
            doc,
            Get("Format", "Type"),
            Get("Format", "Version"),
            Get("Details", "Name"),
            Get("Details", "AppID"),
            Get("Details", "BaseAppName"),
            Get("Details", "Publisher"),
            Get("Details", "Homepage"),
            Get("Details", "Donate"),
            Get("Details", "Category"),
            Get("Details", "Description"),
            Get("Details", "Language"),
            Get("Details", "Trademarks"),
            installType,
            declaredInstallTypes,
            GetBool("License", "Shareable"),
            GetBool("License", "OpenSource"),
            GetBool("License", "Freeware"),
            GetBool("License", "CommercialUse"),
            Get("License", "EULAVersion"),
            Get("Version", "PackageVersion"),
            Get("Version", "DisplayVersion"),
            Get("SpecialPaths", "Plugins"),
            new MetadataDependencies(
                Get("Dependencies", "UsesGhostscript"),
                Get("Dependencies", "UsesJava"),
                Get("Dependencies", "UsesDotNetVersion"),
                GetBool("Dependencies", "Requires64bitOS"),
                Get("Dependencies", "RequiresPortableApp"),
                GetBool("Dependencies", "RequiresAdmin")),
            new MetadataControl(
                Get("Control", "Icons"),
                Get("Control", "Start"),
                starts,
                Get("Control", "ExtractIcon"),
                Get("Control", "ExtractName"),
                Get("Control", "BaseAppID"),
                Get("Control", "BaseAppID64"),
                Get("Control", "BaseAppIDARM64"),
                Get("Control", "ExitEXE"),
                Get("Control", "ExitParameters")));
    }

    /// <summary>Plugin mode can be declared from either document; metadata wins when both are present.</summary>
    internal static PluginModeRules ParsePlugin(IniDocument installerDoc, IniDocument? metadataDoc)
    {
        var name = metadataDoc?.GetValue("Details", "PluginName") ?? installerDoc.GetValue("Details", "PluginName");
        var type = metadataDoc?.GetValue("Details", "PluginType") ?? installerDoc.GetValue("Details", "PluginType");
        return new PluginModeRules(
            !string.IsNullOrWhiteSpace(type) || !string.IsNullOrWhiteSpace(name), name, type);
    }

    private static DownloadFilesRules ParseDownloads(IniDocument doc)
    {
        DownloadBlock Block(string prefix)
        {
            // Primary keys are DownloadURL, DownloadName, …; the secondary block
            // inserts the "2" after "Download": Download2URL, Download2Name, …
            string? Field(string field) => doc.GetValue("DownloadFiles", "Download" + prefix + field);

            // AdvancedExtract / DoubleExtract keys embed their number mid-key
            // (AdvancedExtract1To, AdvancedExtract1Filter, …), so membership is
            // "prefix followed by a digit", ordered by that number then key.
            IReadOnlyList<KeyValuePair<string, string>> NumberedPairs(string keyPrefix) => doc.Entries("DownloadFiles")
                .Where(e => e.Key.Length > keyPrefix.Length
                    && e.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)
                    && char.IsAsciiDigit(e.Key[keyPrefix.Length]))
                .OrderBy(e => EmbeddedNumber(e.Key, keyPrefix.Length))
                .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .Select(e => new KeyValuePair<string, string>(e.Key, e.Value))
                .ToList();

            var advancedPrefix = prefix.Length == 0 ? "AdvancedExtract" : "Download2AdvancedExtract";
            var doublePrefix = prefix.Length == 0 ? "DoubleExtract" : "Download2DoubleExtract";
            return new DownloadBlock(
                prefix,
                Field("URL"),
                Field("KnockURL"),
                Field("Name"),
                Field("Filename"),
                Field("SHA256"),
                Field("To"),
                Field("InnoExtractTo"),
                Field("InnoExtractFrom"),
                NumberedPairs(advancedPrefix),
                prefix.Length == 0
                    ? doc.GetValue("DownloadFiles", "DoubleExtractFilename")
                    : doc.GetValue("DownloadFiles", "Download2DoubleExtractFilename"),
                NumberedPairs(doublePrefix));
        }

        return new DownloadFilesRules(
            doc.HasSection("DownloadFiles"),
            doc.Entries("DownloadFiles").Count(),
            doc.GetValue("DownloadFiles", "AdditionalInstallSize"),
            Block(""),
            Block("2"),
            ParseBool(doc.GetValue("DownloadFiles", "CustomCodeUses7zip")),
            ParseBool(doc.GetValue("DownloadFiles", "UseLightweight7Zip")));
    }

    private static LegacyVersionsRules ParseLegacy(IniDocument doc)
    {
        var all = doc.Entries("LegacyVersions")
            .Select(e => new KeyValuePair<string, string>(e.Key, e.Value))
            .ToList();
        bool Is32(KeyValuePair<string, string> pair) =>
            pair.Key.Contains("32", StringComparison.Ordinal);
        return new LegacyVersionsRules(
            doc.HasSection("LegacyVersions"),
            all.Where(Is32).ToList(),
            all.Where(p => !Is32(p)).ToList());
    }

    /// <summary>Entries of a numbered key family (<c>PreserveDirectory1…N</c>) in numeric order.</summary>
    internal static IReadOnlyList<InstallerListEntry> Numbered(IniDocument doc, string section, string keyPrefix) =>
        doc.Entries(section)
            .Select(e => (Entry: e, Number: NumberSuffix(e.Key, keyPrefix)))
            .Where(x => x.Number is not null)
            .OrderBy(x => x.Number)
            .Select(x => new InstallerListEntry(x.Number!.Value, x.Entry.Value, x.Entry.LineStart))
            .ToList();

    /// <summary>Count of entries whose key belongs to a numbered family, whatever the number.</summary>
    internal static int NumberedCount(IniDocument doc, string section, string keyPrefix) =>
        doc.Entries(section).Count(e => NumberSuffix(e.Key, keyPrefix) is not null);

    /// <summary>The numeric suffix of a key like <c>PreserveDirectory3</c>, or null when the key is not in the family.</summary>
    internal static int? NumberSuffix(string key, string keyPrefix)
    {
        if (key.Length <= keyPrefix.Length || !key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var suffix = key.AsSpan(keyPrefix.Length);
        return suffix.ToString().All(char.IsAsciiDigit) && int.TryParse(suffix, out var n) ? n : null;
    }

    /// <summary>The run of digits starting at <paramref name="start"/> in a key, as a number (0 when absent).</summary>
    private static int EmbeddedNumber(string key, int start)
    {
        var end = start;
        while (end < key.Length && char.IsAsciiDigit(key[end])) end++;
        return end > start && int.TryParse(key.AsSpan(start, end - start), out var n) ? n : 0;
    }

    internal static bool? ParseBool(string? value)
    {
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }
}
