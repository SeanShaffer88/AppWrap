namespace AppWrap.Core.PortableProfiles;

/// <summary>
/// P4 validation of a profile's installer (installer.ini) and metadata (appinfo.ini)
/// documents. Configuration-only: no host access, no downloads, nothing is executed.
///
/// Two structural rules dominate:
///  * The upgrade guard owns a fixed set of installer settings (the keys written by
///    <c>UpgradeGuardGenerator.InstallerSettings</c>: [Source] IncludeInstallerSource,
///    [MainDirectories] RemoveAppDirectory/RemoveOtherDirectory/RemoveDataDirectory
///    and [CheckRunning] CloseEXE). A profile may not set them at all — the merged
///    document always keeps the guard's values (see InstallerDocumentMerger).
///  * The download, copy-local, legacy-version and plugin families can act before
///    the existing PreInstall guard runs, and the reviewed earlier-safe guard order
///    (the internal AppWrapPreMutation gate, docs/portable-profiles-contract.md)
///    does not exist yet. Those families are typed and discoverable, but any
///    enabling use is blocked with <c>GuardOrderBlocked</c> instead of being
///    silently enabled or dropped.
/// </summary>
public static class InstallerValidator
{
    /// <summary>The upstream per-list limit for the preserve/remove families.</summary>
    public const int MaxListEntries = 10;

    /// <summary>Section/key pairs owned by the trusted upgrade guard; profiles must not set them.</summary>
    private static readonly (string Section, string Key)[] GuardOwnedKeys =
    [
        ("Source", "IncludeInstallerSource"),
        ("MainDirectories", "RemoveAppDirectory"),
        ("MainDirectories", "RemoveOtherDirectory"),
        ("MainDirectories", "RemoveDataDirectory"),
        ("CheckRunning", "CloseEXE"),
    ];

    private static string InstallerPath(IniEntryNode e) => $"Installer/[{e.Section}] {e.Key} line {e.LineStart}";
    private static string MetadataPath(IniEntryNode e) => $"Metadata/[{e.Section}] {e.Key} line {e.LineStart}";

    /// <summary>
    /// Validates the installer and metadata documents of a profile. When
    /// <paramref name="wizardAppId"/> is supplied (the wizard-derived package
    /// identity, e.g. <c>PortableAppsGenerator.PackageId</c>), a conflicting
    /// metadata AppID is an error: the wizard-owned identity is canonical and a
    /// conflict is diagnosed, never silently overwritten in either direction.
    /// </summary>
    public static IReadOnlyList<ProfileDiagnostic> ValidateInstaller(PortableProfile profile, string? wizardAppId = null)
    {
        var result = new List<ProfileDiagnostic>();
        if (profile.ExecutionKind == PortableExecutionKind.PackagingOnly) return result;

        var installerDoc = profile.Installer;
        var metadataDoc = profile.Metadata;
        var surface = InstallerRules.From(profile);
        var installer = surface.Installer;
        var metadata = surface.Metadata;

        if (installerDoc is not null)
            foreach (var issue in installerDoc.ParseIssues)
                result.Add(new ProfileDiagnostic("ControlCharacters", $"Installer line {issue.Line}", ProfileSeverity.Error, CapabilityStatus.Unrecognized, issue.Message));
        if (metadataDoc is not null)
            foreach (var issue in metadataDoc.ParseIssues)
                result.Add(new ProfileDiagnostic("ControlCharacters", $"Metadata line {issue.Line}", ProfileSeverity.Error, CapabilityStatus.Unrecognized, issue.Message));
        if (installerDoc is null) return result;

        ValidateGuardOwnership(result, installerDoc);
        ValidatePreserveRemove(result, installerDoc, installer);
        ValidateMainDirectories(result, installerDoc, installer);
        ValidateOptionalComponents(result, installerDoc, installer, metadata);
        ValidateLanguages(result, installerDoc, installer);
        ValidateDownloads(result, installerDoc, installer);
        ValidateCopyLocal(result, installerDoc, installer);
        ValidateLegacy(result, installerDoc, installer);
        ValidatePlugin(result, installerDoc, metadataDoc, installer);
        ValidateMetadata(result, metadataDoc, metadata, wizardAppId);
        return result;
    }

    private static void ValidateGuardOwnership(List<ProfileDiagnostic> result, IniDocument doc)
    {
        foreach (var e in doc.AllEntries)
        {
            if (e.Section is null) continue;
            if (GuardOwnedKeys.Any(g => string.Equals(g.Section, e.Section, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(g.Key, e.Key, StringComparison.OrdinalIgnoreCase)))
                result.Add(new ProfileDiagnostic("GuardSettingOverride", InstallerPath(e), ProfileSeverity.Error, CapabilityStatus.Expert,
                    $"[{e.Section}] {e.Key} is owned by the trusted upgrade guard and cannot be set by a profile; the guard's value always wins."));
        }
    }

    private static void ValidatePreserveRemove(List<ProfileDiagnostic> result, IniDocument doc, InstallerDocumentRules installer)
    {
        var lists = new[]
        {
            (Section: "DirectoriesToPreserve", Prefix: "PreserveDirectory", Entries: installer.PreserveRemove.DirectoriesToPreserve),
            (Section: "DirectoriesToRemove", Prefix: "RemoveDirectory", Entries: installer.PreserveRemove.DirectoriesToRemove),
            (Section: "FilesToPreserve", Prefix: "PreserveFile", Entries: installer.PreserveRemove.FilesToPreserve),
            (Section: "FilesToRemove", Prefix: "RemoveFile", Entries: installer.PreserveRemove.FilesToRemove),
        };
        foreach (var list in lists)
        {
            var count = InstallerRules.NumberedCount(doc, list.Section, list.Prefix);
            if (count > MaxListEntries || list.Entries.Any(e => e.Index > MaxListEntries))
                result.Add(new ProfileDiagnostic("InstallerListLimit", $"Installer/[{list.Section}]", ProfileSeverity.Error, CapabilityStatus.Expert,
                    $"[{list.Section}] holds {Math.Max(count, list.Entries.Count)} {list.Prefix} entries; the upstream limit is {MaxListEntries}."));
        }

        var preserved = installer.PreserveRemove.DirectoriesToPreserve.Cast<InstallerListEntry>()
            .Concat(installer.PreserveRemove.FilesToPreserve).ToList();
        var removed = installer.PreserveRemove.DirectoriesToRemove.Cast<InstallerListEntry>()
            .Concat(installer.PreserveRemove.FilesToRemove).ToList();
        foreach (var keep in preserved)
        foreach (var drop in removed)
        {
            if (PathsOverlap(keep.Value, drop.Value))
                result.Add(new ProfileDiagnostic("PreserveRemoveOverlap", $"Installer preserve '{keep.Value}' / remove '{drop.Value}'", ProfileSeverity.Error, CapabilityStatus.Expert,
                    $"Preserve entry '{keep.Value}' and remove entry '{drop.Value}' name the same path or one contains the other; an upgrade cannot both keep and delete it."));
        }

        foreach (var drop in removed)
        {
            var normalized = NormalizePath(drop.Value);
            if (normalized.Equals("Data", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase))
                result.Add(new ProfileDiagnostic("RemoveTargetsData", $"Installer remove '{drop.Value}' line {drop.Line}", ProfileSeverity.Error, CapabilityStatus.Expert,
                    $"Remove entry '{drop.Value}' targets the package Data folder. User data is preserved by default and is never an upgrade remove target."));
        }
    }

    private static void ValidateMainDirectories(List<ProfileDiagnostic> result, IniDocument doc, InstallerDocumentRules installer)
    {
        if (!installer.MainDirectories.HasRemoveDataDirectory) return;
        var entry = doc.Entries("MainDirectories")
            .First(e => string.Equals(e.Key, "RemoveDataDirectory", StringComparison.OrdinalIgnoreCase));
        result.Add(new ProfileDiagnostic("RemovedDataDirectoryNotSupported", InstallerPath(entry), ProfileSeverity.Error, CapabilityStatus.Unsupported,
            "[MainDirectories] RemoveDataDirectory is not part of the current published PortableApps.com Format; it survives only as a source-history leftover and cannot become a user-data wipe switch."));
    }

    private static void ValidateOptionalComponents(List<ProfileDiagnostic> result, IniDocument doc, InstallerDocumentRules installer, MetadataDocumentRules metadata)
    {
        var optional = installer.OptionalComponents;
        var payloadEntries = optional.OptionalDirectories.Cast<InstallerListEntry>()
            .Concat(optional.OptionalFiles).ToList();
        if (payloadEntries.Count > 0 && optional.OptionalComponents != true)
        {
            var first = doc.Entries("OptionalComponents").First(e => payloadEntries.Any(p => p.Line == e.LineStart));
            result.Add(new ProfileDiagnostic("OptionalEntriesWithoutComponents", InstallerPath(first), ProfileSeverity.Error, CapabilityStatus.Expert,
                "Optional payload entries (OptionalDirectoryN / OptionalFileN) require OptionalComponents=true in [OptionalComponents]; otherwise they name files no optional section will ever install."));
        }

        var validTypes = new HashSet<string>(InstallerRules.BuiltInInstallTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var declared in metadata.DeclaredInstallTypes) validTypes.Add(declared);
        foreach (var (key, value) in new[]
                 {
                     ("OptionalSectionSelectedInstallType", optional.OptionalSectionSelectedInstallType),
                     ("OptionalSectionNotSelectedInstallType", optional.OptionalSectionNotSelectedInstallType),
                 })
        {
            if (value is null || validTypes.Contains(value)) continue;
            var entry = doc.Entries("OptionalComponents")
                .First(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
            result.Add(new ProfileDiagnostic("OptionalInstallTypeUnknown", InstallerPath(entry), ProfileSeverity.Error, CapabilityStatus.Expert,
                $"Install type '{value}' is not one of the built-in install types ({string.Join(", ", InstallerRules.BuiltInInstallTypes)}) or the types declared in the package metadata [Details] InstallType."));
        }
    }

    private static void ValidateLanguages(List<ProfileDiagnostic> result, IniDocument doc, InstallerDocumentRules installer)
    {
        if (!installer.HasLanguagesSection) return;
        var english = doc.Entries("Languages")
            .FirstOrDefault(e => string.Equals(e.Key, "ENGLISH", StringComparison.OrdinalIgnoreCase));
        if (english is null || InstallerRules.ParseBool(english.Value) != true)
            result.Add(new ProfileDiagnostic("InstallerLanguageEnglishRequired", "Installer/[Languages]", ProfileSeverity.Error, CapabilityStatus.Supported,
                "A [Languages] section must keep ENGLISH=true; English is the installer's required fallback language."));
        foreach (var e in doc.Entries("Languages"))
        {
            if (!InstallerRules.KnownLanguages.Contains(e.Key))
                result.Add(new ProfileDiagnostic("UnknownInstallerLanguage", InstallerPath(e), ProfileSeverity.Warning, CapabilityStatus.Unrecognized,
                    $"Language key '{e.Key}' is not in the documented PortableApps.com Format language list. It is kept as-is; only the pinned installer source can say whether it takes effect."));
        }
    }

    private static void ValidateDownloads(List<ProfileDiagnostic> result, IniDocument doc, InstallerDocumentRules installer)
    {
        var downloads = installer.Downloads;
        if (downloads.EntryCount == 0) return;

        result.Add(new ProfileDiagnostic("GuardOrderBlocked", "Installer/[DownloadFiles]", ProfileSeverity.Error, CapabilityStatus.Expert,
            "The DownloadFiles family is blocked: downloads can run before the existing upgrade guard, and the reviewed earlier-safe guard order (AppWrapPreMutation) does not exist yet. The settings are kept and shown, but a package using them cannot be built."));

        foreach (var block in new[] { downloads.Primary, downloads.Secondary })
        {
            if (block.Url is not null && !block.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var entry = EntryForDownloadField(doc, block, "URL");
                result.Add(new ProfileDiagnostic("DownloadUrlNotHttps", entry is null ? "Installer/[DownloadFiles]" : InstallerPath(entry), ProfileSeverity.Error, CapabilityStatus.Expert,
                    $"Download{block.Prefix}URL must be an https:// address; downloads are never fetched over plain http."));
            }
            if (block.IsEnabled || block.Sha256 is not null)
            {
                if (block.Sha256 is null || !IsSha256(block.Sha256))
                {
                    var entry = EntryForDownloadField(doc, block, "SHA256");
                    result.Add(new ProfileDiagnostic("DownloadHashInvalid", entry is null ? "Installer/[DownloadFiles]" : InstallerPath(entry), ProfileSeverity.Error, CapabilityStatus.Expert,
                        $"Download{block.Prefix}SHA256 must be the download's 64-character hexadecimal SHA-256 so the fetched file can be verified before anything runs it."));
                }
            }
        }
    }

    private static IniEntryNode? EntryForDownloadField(IniDocument doc, DownloadBlock block, string field) =>
        doc.Entries("DownloadFiles").FirstOrDefault(e =>
            string.Equals(e.Key, "Download" + block.Prefix + field, StringComparison.OrdinalIgnoreCase));

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static void ValidateCopyLocal(List<ProfileDiagnostic> result, IniDocument doc, InstallerDocumentRules installer)
    {
        if (!installer.CopyLocal.IsEnabled) return;
        result.Add(new ProfileDiagnostic("GuardOrderBlocked", "Installer/[CopyLocalFiles]", ProfileSeverity.Error, CapabilityStatus.Expert,
            "The CopyLocalFiles family is blocked: copying a local installation can run before the existing upgrade guard, and the reviewed earlier-safe guard order (AppWrapPreMutation) does not exist yet. The settings are kept and shown, but a package using them cannot be built."));
    }

    private static void ValidateLegacy(List<ProfileDiagnostic> result, IniDocument doc, InstallerDocumentRules installer)
    {
        if (!installer.Legacy.HasAnyEntry) return;
        result.Add(new ProfileDiagnostic("GuardOrderBlocked", "Installer/[LegacyVersions]", ProfileSeverity.Error, CapabilityStatus.Expert,
            "The LegacyVersions family is blocked: legacy migration can rename or remove directories before the existing upgrade guard runs, and the reviewed earlier-safe guard order (AppWrapPreMutation) does not exist yet. The settings are kept and shown, but a package using them cannot be built."));
    }

    private static void ValidatePlugin(List<ProfileDiagnostic> result, IniDocument installerDoc, IniDocument? metadataDoc, InstallerDocumentRules installer)
    {
        var plugin = InstallerRules.ParsePlugin(installerDoc, metadataDoc);
        if (!plugin.IsPluginMode) return;
        result.Add(new ProfileDiagnostic("GuardOrderBlocked", "Metadata/[Details] PluginType", ProfileSeverity.Error, CapabilityStatus.Expert,
            "Plugin / common-files package mode is blocked: it is a distinct package mode with its own install and upgrade semantics, not an ordinary app package, and it has no reviewed guard order yet. The declaration is kept and shown, but such a package cannot be built."));
    }

    private static void ValidateMetadata(List<ProfileDiagnostic> result, IniDocument? doc, MetadataDocumentRules metadata, string? wizardAppId)
    {
        if (doc is null) return;
        if (wizardAppId is not null && metadata.AppID is not null
            && !string.Equals(metadata.AppID, wizardAppId, StringComparison.OrdinalIgnoreCase))
        {
            var entry = doc.Entries("Details")
                .FirstOrDefault(e => string.Equals(e.Key, "AppID", StringComparison.OrdinalIgnoreCase));
            result.Add(new ProfileDiagnostic("MetadataAppIdConflict",
                entry is null ? "Metadata/[Details] AppID" : MetadataPath(entry), ProfileSeverity.Error, CapabilityStatus.Supported,
                $"Metadata AppID '{metadata.AppID}' conflicts with the wizard-derived package identity '{wizardAppId}'. The wizard identity is canonical; reconcile the profile metadata explicitly instead of overwriting either one."));
        }
        if (metadata.PackageVersion is not null)
        {
            var valid = true;
            try { InputRules.PackageVersion(metadata.PackageVersion); }
            catch (UserInputException) { valid = false; }
            if (!valid)
            {
                var entry = doc.Entries("Version")
                    .FirstOrDefault(e => string.Equals(e.Key, "PackageVersion", StringComparison.OrdinalIgnoreCase));
                result.Add(new ProfileDiagnostic("MetadataVersionInvalid",
                    entry is null ? "Metadata/[Version] PackageVersion" : MetadataPath(entry), ProfileSeverity.Error, CapabilityStatus.Supported,
                    $"Metadata PackageVersion '{metadata.PackageVersion}' is not a numeric version with one to four components (for example 1.2.3.0)."));
            }
        }
    }

    private static string NormalizePath(string path) => path.Trim().TrimEnd('\\', '/').Replace('/', '\\');

    private static bool PathsOverlap(string a, string b)
    {
        var x = NormalizePath(a);
        var y = NormalizePath(b);
        if (x.Length == 0 || y.Length == 0) return false;
        return x.Equals(y, StringComparison.OrdinalIgnoreCase)
            || x.StartsWith(y + "\\", StringComparison.OrdinalIgnoreCase)
            || y.StartsWith(x + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
