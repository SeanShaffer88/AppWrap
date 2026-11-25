using System.Text.Json;

namespace AppWrap.Core.PortableProfiles;

/// <summary>The ten workspace categories of the portable-profile editor, in contract order.</summary>
public enum ProfileCategory
{
    OverviewLaunch,
    DataFilesFolders,
    Registry,
    ConfigPathUpdates,
    EnvironmentLanguage,
    ProcessesCompatibility,
    InstallUpgradeRules,
    PackageDetails,
    ConfigurationText,
    TrustedExtensions,
}

/// <summary>One workspace category: its title, plain-language description and grid column headers (after the rule column).</summary>
public sealed record ProfileCategoryInfo(ProfileCategory Category, string Title, string Description, IReadOnlyList<string> Columns);

/// <summary>
/// One editable rule row of the workspace grid. Rows are derived from the profile's
/// documents on every call — the documents remain the single persisted model.
/// <see cref="Cells"/> aligns with the category's columns: the runtime column set
/// (When / Operation / Host location / Portable location / Existing host data /
/// After exit / Risk) for launcher categories, the installer column set
/// (On upgrade / Inside package path / Preserve/remove / Condition / Data impact)
/// for installer and metadata categories. The two sets are never mixed.
/// </summary>
public sealed record ProfileRuleRow(
    string Id,
    ProfileCategory Category,
    PortableDocumentKind Document,
    string Section,
    string Key,
    string Value,
    int Line,
    CapabilityStatus Status,
    bool IsBlocked,
    string StatusNote,
    string Display,
    IReadOnlyList<string> Cells,
    bool CanEditKey,
    bool CanEditValue,
    bool CanRemove,
    bool CanReorder);

/// <summary>One page of rule rows for a category, with the totals the pager displays.</summary>
public sealed record ProfileRulePage(IReadOnlyList<ProfileRuleRow> Rows, int TotalCount, int PageCount);

/// <summary>An editable convenience preset for adding a rule. Presets are starting points, not the feature surface.</summary>
public sealed record ProfileRulePreset(
    string Label, ProfileCategory Category, PortableDocumentKind Document,
    string Section, string Key, string Value, bool CreatesFileWriteSection = false);

/// <summary>
/// Testable state and editing logic behind the WPF portable-profile workspace
/// (contract P5). The GUI binds to this class; everything here is pure
/// configuration work over the profile's <see cref="IniDocument"/>s — no host
/// access, nothing executed. Switching between "Already portable"
/// (<see cref="PortableExecutionKind.PackagingOnly"/>) and "Configure data and
/// cleanup" (<see cref="PortableExecutionKind.Pal"/>) never discards the edited
/// profile: the working Pal profile is kept in session either way, and
/// <see cref="EffectiveProfile"/> says whether it applies to the next build.
/// </summary>
public sealed class ProfileWorkspaceState
{
    private static readonly string[] RuntimeColumns =
        ["When", "Operation", "Host location", "Portable location", "Existing host data", "After exit", "Risk"];
    private static readonly string[] InstallerColumns =
        ["On upgrade", "Inside package path", "Preserve/remove", "Condition", "Data impact"];

    public static IReadOnlyList<ProfileCategoryInfo> Categories { get; } =
    [
        new(ProfileCategory.OverviewLaunch, "Overview/Launch", "What the portable app runs and which launcher features it turns on.", RuntimeColumns),
        new(ProfileCategory.DataFilesFolders, "Data files & folders", "Files and folders the app keeps in Data and uses at the matching place on the PC.", RuntimeColumns),
        new(ProfileCategory.Registry, "Registry", "Registry keys and values saved with the portable data and restored on the PC.", RuntimeColumns),
        new(ProfileCategory.ConfigPathUpdates, "Config & path updates", "Settings files rewritten at launch so stored paths point at the portable copy.", RuntimeColumns),
        new(ProfileCategory.EnvironmentLanguage, "Environment & language", "Environment variables for the app process and how its language is chosen.", RuntimeColumns),
        new(ProfileCategory.ProcessesCompatibility, "Processes & compatibility", "Waiting, single-instance, splash and compatibility behaviour while the app runs.", RuntimeColumns),
        new(ProfileCategory.InstallUpgradeRules, "Install/upgrade rules", "What an upgrade keeps, removes or adds, and which installer languages are offered.", InstallerColumns),
        new(ProfileCategory.PackageDetails, "Package details & distribution", "The appinfo details shown by the portable app menu and the package page.", InstallerColumns),
        new(ProfileCategory.ConfigurationText, "Configuration text", "The exact launcher, installer and package-details text. Edits here update the same rules.", []),
        new(ProfileCategory.TrustedExtensions, "Trusted extensions", "Custom launcher source files, trusted by exact path and hash only after an explicit action here.", ["SHA-256", "Purpose", "Trust"]),
    ];

    public static IReadOnlyList<ProfileRulePreset> Presets { get; } =
    [
        new("Portable data argument", ProfileCategory.OverviewLaunch, PortableDocumentKind.Launcher, "Launch", "CommandLineArguments", "--data-dir=%PAL:DataDir%"),
        new("Roaming AppData folder", ProfileCategory.DataFilesFolders, PortableDocumentKind.Launcher, "DirectoriesMove", "YourAppData", "%APPDATA%\\YourApp"),
        new("Local AppData folder", ProfileCategory.DataFilesFolders, PortableDocumentKind.Launcher, "DirectoriesMove", "YourAppData", "%LOCALAPPDATA%\\YourApp"),
        new("Settings file in AppData", ProfileCategory.DataFilesFolders, PortableDocumentKind.Launcher, "FilesMove", "settings.ini", "%APPDATA%\\YourApp"),
        new("Documents folder", ProfileCategory.DataFilesFolders, PortableDocumentKind.Launcher, "DirectoriesMove", "YourApp", "%DOCUMENTS%\\YourApp"),
        new("Protect a host folder (discard changes)", ProfileCategory.DataFilesFolders, PortableDocumentKind.Launcher, "DirectoriesMove", "-", "%LOCALAPPDATA%\\YourApp"),
        new("Exact registry key", ProfileCategory.Registry, PortableDocumentKind.Launcher, "RegistryKeys", "YourApp", "HKCU\\Software\\YourApp"),
        new("Protect a host registry key (discard changes)", ProfileCategory.Registry, PortableDocumentKind.Launcher, "RegistryKeys", "-", "HKCU\\Software\\YourApp"),
        new("Exact registry value", ProfileCategory.Registry, PortableDocumentKind.Launcher, "RegistryValueWrite", "HKCU\\Software\\YourApp\\Portable", "REG_SZ:true"),
        new("Config file update", ProfileCategory.ConfigPathUpdates, PortableDocumentKind.Launcher, "FileWrite", "", "", CreatesFileWriteSection: true),
        new("Environment override", ProfileCategory.EnvironmentLanguage, PortableDocumentKind.Launcher, "Environment", "YOURAPP_HOME", "%PAL:DataDir%"),
        new("Preserve a folder on upgrade", ProfileCategory.InstallUpgradeRules, PortableDocumentKind.Installer, "DirectoriesToPreserve", "PreserveDirectory", "App\\YourApp"),
    ];

    private PortableProfile _profile;

    public ProfileWorkspaceState()
    {
        _profile = EmptyPalProfile();
        Mode = PortableExecutionKind.PackagingOnly;
    }

    /// <summary>The wizard's portable-behaviour choice. Switching it never discards <see cref="WorkingProfile"/>.</summary>
    public PortableExecutionKind Mode { get; private set; }

    public void SetMode(PortableExecutionKind mode) => Mode = mode;

    /// <summary>The Pal profile being edited, kept in session whether or not it currently applies.</summary>
    public PortableProfile WorkingProfile => _profile;

    /// <summary>True when the edited profile will be used by the next portable build.</summary>
    public bool ProfileApplies => Mode == PortableExecutionKind.Pal;

    /// <summary>The profile to hand to the build: the edited Pal profile in Pal mode, otherwise null (existing packaging-only behaviour).</summary>
    public PortableProfile? EffectiveProfile => ProfileApplies ? _profile : null;

    /// <summary>The wizard-derived package identity (for example from PortableAppsGenerator.PackageId), used only to diagnose metadata conflicts.</summary>
    public string? WizardAppId { get; set; }

    /// <summary>True when the kept-in-session profile contains any rule, document text or extension.</summary>
    public bool HasWorkingEdits =>
        _profile.Launcher.AllEntries.Any() || _profile.Installer.AllEntries.Any()
        || (_profile.Metadata?.AllEntries.Any() ?? false) || (_profile.UserOverrides?.AllEntries.Any() ?? false)
        || _profile.TrustedExtensions.Count > 0 || _profile.Assets.Count > 0 || _profile.ForceCleanupAcknowledged;

    /// <summary>Explicit in-product acknowledgment for force-cleanup rules; imported profiles start unacknowledged.</summary>
    public bool ForceCleanupAcknowledged
    {
        get => _profile.ForceCleanupAcknowledged;
        set => _profile = _profile with { ForceCleanupAcknowledged = value };
    }

    /// <summary>True when any force-cleanup rule exists, so the workspace can ask for the acknowledgment.</summary>
    public bool HasForceCleanupEntries =>
        _profile.Launcher.Entries("RegistryCleanupForce").Any() || _profile.Launcher.Entries("DirectoriesCleanupForce").Any();

    private static PortableProfile EmptyPalProfile() =>
        PortableProfileCodec.FromIniTexts("", "", null, null, PortableExecutionKind.Pal);

    /// <summary>Starts a fresh, empty Pal profile and switches the workspace to Configure mode.</summary>
    public void NewProfile()
    {
        _profile = EmptyPalProfile();
        Mode = PortableExecutionKind.Pal;
    }

    /// <summary>
    /// Replaces the working profile with an imported one and switches to Configure
    /// mode. Import never grants trust: every extension is forced back to
    /// untrusted, whatever the source claimed.
    /// </summary>
    public void LoadProfile(PortableProfile profile)
    {
        _profile = profile with
        {
            ExecutionKind = PortableExecutionKind.Pal,
            TrustedExtensions = profile.TrustedExtensions.Select(e => e with { Trusted = false }).ToList(),
        };
        Mode = PortableExecutionKind.Pal;
    }

    // ----- Diagnostics and plan -----

    /// <summary>Combined planner (launcher validator + cross-rule checks) and installer-validator diagnostics for the working profile.</summary>
    public IReadOnlyList<ProfileDiagnostic> Diagnostics
    {
        get
        {
            if (Mode != PortableExecutionKind.Pal) return [];
            var (_, plannerDiagnostics) = ProfilePlanner.Prepare(_profile, WizardAppId ?? "AppPortable");
            return plannerDiagnostics.Concat(InstallerValidator.ValidateInstaller(_profile, WizardAppId)).ToList();
        }
    }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProfileSeverity.Error);

    /// <summary>The immutable operation plan, present only when the profile has no error diagnostics.</summary>
    public PreparedPlan? Plan =>
        Mode == PortableExecutionKind.Pal ? ProfilePlanner.Prepare(_profile, WizardAppId ?? "AppPortable").Plan : null;

    /// <summary>Plain-language review lines for the Overview category: planned operations, host backup locations and the evidence level.</summary>
    public IReadOnlyList<string> PlanSummaryLines()
    {
        if (Mode != PortableExecutionKind.Pal) return [];
        var plan = Plan;
        if (plan is null) return ["Fix the problems below to see what this profile will do."];
        var lines = plan.Operations.Select(o => o.Summary).ToList();
        if (plan.HostBackupLocations.Count > 0)
        {
            lines.Add("Host backups created while the app runs:");
            lines.AddRange(plan.HostBackupLocations);
        }
        lines.Add("Evidence level: configuration validated. Runtime portability stays Unverified until the Windows fixture suite runs.");
        return lines;
    }

    // ----- Documents and raw text -----

    /// <summary>The document behind a text-editor tab. Reading Metadata never creates it; editing does.</summary>
    public IniDocument DocumentFor(PortableDocumentKind kind) => kind switch
    {
        PortableDocumentKind.Launcher => _profile.Launcher,
        PortableDocumentKind.Installer => _profile.Installer,
        PortableDocumentKind.Metadata => _profile.Metadata ?? CreateMetadata(),
        PortableDocumentKind.UserOverrides => _profile.UserOverrides ?? CreateUserOverrides(),
        _ => throw new UserInputException("This profile part has no editable configuration text."),
    };

    private IniDocument CreateMetadata()
    {
        var doc = IniDocument.Parse("");
        _profile = _profile with { Metadata = doc };
        return doc;
    }

    private IniDocument CreateUserOverrides()
    {
        var doc = IniDocument.Parse("");
        _profile = _profile with { UserOverrides = doc };
        return doc;
    }

    public string GetDocumentText(PortableDocumentKind kind) => kind switch
    {
        PortableDocumentKind.Launcher => _profile.Launcher.Serialize(),
        PortableDocumentKind.Installer => _profile.Installer.Serialize(),
        PortableDocumentKind.Metadata => _profile.Metadata?.Serialize() ?? "",
        PortableDocumentKind.UserOverrides => _profile.UserOverrides?.Serialize() ?? "",
        _ => throw new UserInputException("This profile part has no editable configuration text."),
    };

    /// <summary>
    /// Replaces a document from edited text. The text is re-parsed through
    /// <see cref="IniDocument"/>: content problems never throw here — they are
    /// retained as raw lines and surface as diagnostics — so an invalid draft is
    /// kept, not silently discarded, until it is corrected.
    /// </summary>
    public void SetDocumentText(PortableDocumentKind kind, string text)
    {
        var doc = IniDocument.Parse(text ?? "");
        _profile = kind switch
        {
            PortableDocumentKind.Launcher => _profile with { Launcher = doc },
            PortableDocumentKind.Installer => _profile with { Installer = doc },
            PortableDocumentKind.Metadata => _profile with { Metadata = doc },
            PortableDocumentKind.UserOverrides => _profile with { UserOverrides = doc },
            _ => throw new UserInputException("This profile part has no editable configuration text."),
        };
    }

    // ----- Profile files (New / Load / Save) -----

    private sealed class ExtensionDto
    {
        public string RelativePath { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Purpose { get; set; } = "";
    }

    private sealed class AssetDto
    {
        public string RelativePath { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Purpose { get; set; } = "";
    }

    private sealed class ProfileFileDto
    {
        public int SchemaVersion { get; set; }
        public string? Launcher { get; set; }
        public string? Installer { get; set; }
        public string? Metadata { get; set; }
        public string? UserOverrides { get; set; }
        public bool ForceCleanupAcknowledged { get; set; }
        public List<ExtensionDto> Extensions { get; set; } = [];
        public List<AssetDto> Assets { get; set; } = [];
    }

    /// <summary>
    /// Serializes the working profile for a user-selected file. The documents are
    /// carried as their exact INI texts (the <see cref="PortableProfileCodec"/>
    /// representation); trust is deliberately not serialized — a saved profile
    /// can never carry a trusted flag back in.
    /// </summary>
    public string ExportProfileText()
    {
        var dto = new ProfileFileDto
        {
            SchemaVersion = PortableProfile.CurrentSchemaVersion,
            Launcher = PortableProfileCodec.LauncherText(_profile),
            Installer = PortableProfileCodec.InstallerText(_profile),
            Metadata = PortableProfileCodec.MetadataText(_profile),
            UserOverrides = PortableProfileCodec.UserOverridesText(_profile),
            ForceCleanupAcknowledged = _profile.ForceCleanupAcknowledged,
            Extensions = _profile.TrustedExtensions.Select(e => new ExtensionDto { RelativePath = e.RelativePath, Sha256 = e.Sha256, Purpose = e.Purpose }).ToList(),
            Assets = _profile.Assets.Select(a => new AssetDto { RelativePath = a.RelativePath, Sha256 = a.Sha256, Purpose = a.Purpose }).ToList(),
        };
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Loads a profile saved by <see cref="ExportProfileText"/> (or an equivalent
    /// hand-written file). Documents roundtrip through
    /// <see cref="PortableProfileCodec.FromIniTexts"/>; imported extensions are
    /// always untrusted and force-cleanup starts unacknowledged.
    /// </summary>
    public void ImportProfileText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4_000_000)
            throw new UserInputException("Choose an AppWrap portable profile file.");
        ProfileFileDto? dto;
        try { dto = JsonSerializer.Deserialize<ProfileFileDto>(text); }
        catch (JsonException) { throw new UserInputException("This file is not an AppWrap portable profile."); }
        if (dto is null || dto.SchemaVersion != PortableProfile.CurrentSchemaVersion)
            throw new UserInputException("This portable profile uses an unsupported schema version.");
        var profile = PortableProfileCodec.FromIniTexts(dto.Launcher, dto.Installer, dto.Metadata, dto.UserOverrides, PortableExecutionKind.Pal) with
        {
            ForceCleanupAcknowledged = false,
            TrustedExtensions = dto.Extensions.Select(e => new TrustedExtension(e.RelativePath, e.Sha256, e.Purpose, false)).ToList(),
            Assets = dto.Assets.Select(a => new ProfileAsset(a.RelativePath, a.Sha256, a.Purpose)).ToList(),
        };
        _profile = profile;
        Mode = PortableExecutionKind.Pal;
    }

    // ----- Rule rows -----

    /// <summary>All rule rows of one category, derived fresh from the current documents.</summary>
    public IReadOnlyList<ProfileRuleRow> Rows(ProfileCategory category)
    {
        if (category == ProfileCategory.ConfigurationText) return [];
        if (category == ProfileCategory.TrustedExtensions) return ExtensionRows();
        var diagnostics = Diagnostics;
        var rows = new List<ProfileRuleRow>();
        if (category == ProfileCategory.InstallUpgradeRules)
            BuildDocumentRows(PortableDocumentKind.Installer, _profile.Installer, category, diagnostics, rows);
        else if (category == ProfileCategory.PackageDetails)
        {
            if (_profile.Metadata is not null)
                BuildDocumentRows(PortableDocumentKind.Metadata, _profile.Metadata, category, diagnostics, rows);
        }
        else
            BuildLauncherRows(category, diagnostics, rows);
        return rows;
    }

    /// <summary>One page of a category's rows; the page index is clamped into range.</summary>
    public ProfileRulePage Page(ProfileCategory category, int pageIndex, int pageSize)
    {
        var all = Rows(category);
        var size = Math.Max(1, pageSize);
        var pageCount = Math.Max(1, (all.Count + size - 1) / size);
        var index = Math.Clamp(pageIndex, 0, pageCount - 1);
        return new ProfileRulePage(all.Skip(index * size).Take(size).ToList(), all.Count, pageCount);
    }

    private void BuildLauncherRows(ProfileCategory category, IReadOnlyList<ProfileDiagnostic> diagnostics, List<ProfileRuleRow> rows)
    {
        var doc = _profile.Launcher;
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in doc.AllEntries)
        {
            if (entry.Section is null) continue;
            var section = entry.Section;
            var occurrence = NextOccurrence(occurrences, entry);
            if (LauncherCategory(section, entry.Key) != category) continue;
            var status = CapabilityCatalog.Classify(section, entry.Key);
            var note = NoteFor(diagnostics, "Launcher", entry, out _);
            rows.Add(new ProfileRuleRow(
                RowId(PortableDocumentKind.Launcher, section, entry.Key, occurrence),
                category, PortableDocumentKind.Launcher, section, entry.Key, entry.Value, entry.LineStart,
                status, false, note,
                $"[{section}] {entry.Key} = {entry.Value}",
                LauncherCells(entry, status),
                KeyEditable(PortableDocumentKind.Launcher, section, entry.Key),
                true, true,
                Reorderable(PortableDocumentKind.Launcher, section, entry.Key)));
        }
    }

    private void BuildDocumentRows(PortableDocumentKind kind, IniDocument doc, ProfileCategory category,
        IReadOnlyList<ProfileDiagnostic> diagnostics, List<ProfileRuleRow> rows)
    {
        var label = kind == PortableDocumentKind.Installer ? "Installer" : "Metadata";
        var blocked = BlockedSections(kind, diagnostics);
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pluginMode = InstallerRules.ParsePlugin(_profile.Installer, _profile.Metadata).IsPluginMode;
        foreach (var entry in doc.AllEntries)
        {
            if (entry.Section is null) continue;
            var section = entry.Section;
            var occurrence = NextOccurrence(occurrences, entry);
            var isBlocked = blocked.ContainsKey(section)
                || (pluginMode
                    && (string.Equals(entry.Key, "PluginName", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(entry.Key, "PluginType", StringComparison.OrdinalIgnoreCase))
                    && string.Equals(section, "Details", StringComparison.OrdinalIgnoreCase));
            var note = isBlocked
                ? blocked.TryGetValue(section, out var familyNote) ? familyNote
                    : diagnostics.FirstOrDefault(d => d.Code == "GuardOrderBlocked")?.Message ?? ""
                : NoteFor(diagnostics, label, entry, out _);
            var status = kind == PortableDocumentKind.Installer
                ? string.Equals(section, "Languages", StringComparison.OrdinalIgnoreCase) ? CapabilityStatus.Supported : CapabilityStatus.Expert
                : CapabilityStatus.Supported;
            rows.Add(new ProfileRuleRow(
                RowId(kind, section, entry.Key, occurrence),
                category, kind, section, entry.Key, entry.Value, entry.LineStart,
                status, isBlocked, note,
                $"[{section}] {entry.Key} = {entry.Value}",
                kind == PortableDocumentKind.Installer ? InstallerCells(entry, isBlocked) : MetadataCells(isBlocked),
                false, true, true,
                Reorderable(kind, section, entry.Key)));
        }
    }

    private List<ProfileRuleRow> ExtensionRows()
    {
        var rows = new List<ProfileRuleRow>();
        for (var i = 0; i < _profile.TrustedExtensions.Count; i++)
        {
            var extension = _profile.TrustedExtensions[i];
            rows.Add(new ProfileRuleRow(
                "ext:" + i, ProfileCategory.TrustedExtensions, PortableDocumentKind.Extensions,
                "TrustedExtensions", extension.RelativePath, extension.Sha256, 0,
                CapabilityStatus.Expert, false,
                extension.Trusted ? "" : "Not trusted: this source will not be compiled until you trust it here.",
                extension.RelativePath,
                [extension.Sha256, extension.Purpose, extension.Trusted ? "Trusted" : "Not trusted"],
                false, false, true, false));
        }
        return rows;
    }

    /// <summary>Sections of an installer/metadata document whose enabling entries draw a GuardOrderBlocked error, with that diagnostic's message.</summary>
    private Dictionary<string, string> BlockedSections(PortableDocumentKind kind, IReadOnlyList<ProfileDiagnostic> diagnostics)
    {
        var blocked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (kind != PortableDocumentKind.Installer) return blocked;
        var surface = InstallerRules.From(_profile);
        void Block(string section)
        {
            var diagnostic = diagnostics.FirstOrDefault(d =>
                d.Code == "GuardOrderBlocked" && d.DocumentPath.Equals($"Installer/[{section}]", StringComparison.OrdinalIgnoreCase));
            if (diagnostic is not null) blocked[section] = diagnostic.Message;
        }
        if (surface.Installer.Downloads.EntryCount > 0) Block("DownloadFiles");
        if (surface.Installer.CopyLocal.IsEnabled) Block("CopyLocalFiles");
        if (surface.Installer.Legacy.HasAnyEntry) Block("LegacyVersions");
        return blocked;
    }

    private static string NoteFor(IReadOnlyList<ProfileDiagnostic> diagnostics, string label, IniEntryNode entry, out bool isError)
    {
        var exact = $"{label}/[{entry.Section}] {entry.Key} line {entry.LineStart}";
        var matches = diagnostics.Where(d => string.Equals(d.DocumentPath, exact, StringComparison.Ordinal)).ToList();
        isError = matches.Any(d => d.Severity == ProfileSeverity.Error);
        return matches.Count == 0 ? "" : string.Join(" ", matches.Select(d => d.Message));
    }

    private static int NextOccurrence(Dictionary<string, int> occurrences, IniEntryNode entry)
    {
        var key = entry.Section + "\u001f" + entry.Key;
        var occurrence = occurrences.GetValueOrDefault(key);
        occurrences[key] = occurrence + 1;
        return occurrence;
    }

    private static string RowId(PortableDocumentKind kind, string section, string key, int occurrence) =>
        $"{(int)kind}:{section}\u001f{key}\u001f{occurrence}";

    private static ProfileCategory LauncherCategory(string section, string key)
    {
        if (CapabilityCatalog.IsPatternSection(section, out var prefix, out _))
            return prefix == "FileWrite" ? ProfileCategory.ConfigPathUpdates : ProfileCategory.ProcessesCompatibility;
        if (string.Equals(section, "FilesMove", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "DirectoriesMove", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "DirectoriesCleanupIfEmpty", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "DirectoriesCleanupForce", StringComparison.OrdinalIgnoreCase))
            return ProfileCategory.DataFilesFolders;
        if (string.Equals(section, "RegistryKeys", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "RegistryValueWrite", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "RegistryValueBackupDelete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "RegistryCleanupIfEmpty", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "RegistryCleanupForce", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "QtKeysCleanup", StringComparison.OrdinalIgnoreCase))
            return ProfileCategory.Registry;
        if (string.Equals(section, "Environment", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "Language", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "LanguageStrings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "LanguageFile", StringComparison.OrdinalIgnoreCase))
            return ProfileCategory.EnvironmentLanguage;
        if (string.Equals(section, "LiveMode", StringComparison.OrdinalIgnoreCase))
            return ProfileCategory.ProcessesCompatibility;
        if (string.Equals(section, "Launch", StringComparison.OrdinalIgnoreCase) && IsProcessKey(key))
            return ProfileCategory.ProcessesCompatibility;
        return ProfileCategory.OverviewLaunch;
    }

    private static bool IsProcessKey(string key) =>
        CapabilityCatalog.IsWaitForExeKey(key)
        || key.Equals("CloseEXE", StringComparison.OrdinalIgnoreCase)
        || key.Equals("WaitForProgram", StringComparison.OrdinalIgnoreCase)
        || key.Equals("WaitForOtherInstances", StringComparison.OrdinalIgnoreCase)
        || key.Equals("SinglePortableAppInstance", StringComparison.OrdinalIgnoreCase)
        || key.Equals("SingleAppInstance", StringComparison.OrdinalIgnoreCase)
        || key.Equals("CleanTemp", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("RunAsAdmin", StringComparison.OrdinalIgnoreCase)
        || key.Equals("MinOS", StringComparison.OrdinalIgnoreCase)
        || key.Equals("MaxOS", StringComparison.OrdinalIgnoreCase)
        || key.Equals("SupportsUNC", StringComparison.OrdinalIgnoreCase)
        || key.Equals("NoSpacesInPath", StringComparison.OrdinalIgnoreCase)
        || key.Equals("DirectoryMoveOK", StringComparison.OrdinalIgnoreCase)
        || key.Equals("HideCommandLineWindow", StringComparison.OrdinalIgnoreCase)
        || key.Equals("RefreshShellIcons", StringComparison.OrdinalIgnoreCase)
        || key.Equals("SplashTime", StringComparison.OrdinalIgnoreCase)
        || key.Equals("LaunchAppAfterSplash", StringComparison.OrdinalIgnoreCase);

    private static string RiskText(CapabilityStatus status) => status switch
    {
        CapabilityStatus.Supported => "Low",
        CapabilityStatus.Expert => "Expert — verify the exact key",
        CapabilityStatus.Unsupported => "Unsupported in the pinned engine",
        CapabilityStatus.Deprecated => "Deprecated",
        _ => "Not recognized",
    };

    private string[] LauncherCells(IniEntryNode entry, CapabilityStatus status)
    {
        var appId = WizardAppId ?? "<AppID>";
        string[] cells = ["At launch", $"Set {entry.Key}", "—", "—", "Unchanged", "—", RiskText(status)];
        var section = entry.Section!;
        var discard = string.Equals(entry.Key, "-", StringComparison.Ordinal);
        if (string.Equals(section, "FilesMove", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "DirectoriesMove", StringComparison.OrdinalIgnoreCase))
        {
            var isDirectory = string.Equals(section, "DirectoriesMove", StringComparison.OrdinalIgnoreCase);
            cells = ["While the program runs",
                discard ? "Protect the host copy; discard portable changes" : "Use the portable copy at the host location",
                entry.Value,
                discard ? "— (changes discarded)" : entry.Key,
                discard ? "Kept safe (backed up)" : $"Backed up beside the host copy (-BackupBy{appId})",
                "Host copy restored",
                isDirectory ? RiskText(CapabilityStatus.Expert) : RiskText(CapabilityStatus.Supported)];
        }
        else if (string.Equals(section, "DirectoriesCleanupIfEmpty", StringComparison.OrdinalIgnoreCase))
            cells = ["At exit", "Remove the folder if it is empty", entry.Value, "—", "Removed only when empty", "—", RiskText(CapabilityStatus.Supported)];
        else if (string.Equals(section, "DirectoriesCleanupForce", StringComparison.OrdinalIgnoreCase))
            cells = ["At exit", "Remove the folder and everything in it", entry.Value, "—", "Deleted", "—", "High — deletes data; needs acknowledgment"];
        else if (string.Equals(section, "RegistryKeys", StringComparison.OrdinalIgnoreCase))
            cells = ["While the program runs",
                discard ? "Back up the host key; discard portable changes" : "Save portable changes; restore the host key",
                entry.Value,
                discard ? "— (changes discarded)" : $"Data\\settings\\{entry.Key}.reg",
                @"Backed up under HKCU\Software\PortableApps.com\Keys",
                "Host key restored", RiskText(CapabilityStatus.Expert)];
        else if (string.Equals(section, "RegistryValueWrite", StringComparison.OrdinalIgnoreCase))
        {
            var write = RegistryValueWrite.Parse(entry);
            var shown = write.ValueName.Length == 0 ? write.KeyPath + @"\ (default value)" : write.KeyPath + "\\" + write.ValueName;
            cells = ["Before launch", $"Write this registry value as {write.Type}", shown, "—",
                "Backed up when a covering rule exists", "Host value restored", RiskText(CapabilityStatus.Expert)];
        }
        else if (string.Equals(section, "RegistryValueBackupDelete", StringComparison.OrdinalIgnoreCase))
            cells = ["Before launch", "Back up and delete this host value", entry.Value, "—",
                @"Backed up under HKCU\Software\PortableApps.com\Values", "Host value restored", RiskText(CapabilityStatus.Supported)];
        else if (string.Equals(section, "RegistryCleanupIfEmpty", StringComparison.OrdinalIgnoreCase))
            cells = ["At exit", "Remove the registry key if it is empty", entry.Value, "—", "Removed only when empty", "—", RiskText(CapabilityStatus.Expert)];
        else if (string.Equals(section, "RegistryCleanupForce", StringComparison.OrdinalIgnoreCase))
            cells = ["At exit", "Remove the registry key and everything under it", entry.Value, "—", "Deleted", "—", "High — deletes data; needs acknowledgment"];
        else if (string.Equals(section, "QtKeysCleanup", StringComparison.OrdinalIgnoreCase))
            cells = ["At exit", "Remove Qt plug-in cache keys", entry.Value, "—", "Removed", "—", RiskText(CapabilityStatus.Expert)];
        else if (string.Equals(section, "Environment", StringComparison.OrdinalIgnoreCase))
            cells = ["At launch", $"Set {entry.Key} for the program only", "—", "—", "Host environment unchanged", "Discarded when the program exits", RiskText(CapabilityStatus.Supported)];
        else if (string.Equals(section, "Language", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "LanguageStrings", StringComparison.OrdinalIgnoreCase))
            cells = ["At launch", "Choose the program language", "—", "—", "Unchanged", "—", RiskText(status)];
        else if (string.Equals(section, "LanguageFile", StringComparison.OrdinalIgnoreCase))
        {
            var file = _profile.Launcher.GetValue("LanguageFile", "File") ?? "—";
            cells = ["At launch", "Read the language from a settings file", "—", file, "Unchanged", "—", RiskText(status)];
        }
        else if (CapabilityCatalog.IsPatternSection(section, out var prefix, out _) && prefix == "FileWrite")
        {
            var type = _profile.Launcher.GetValue(section, "Type") ?? "INI";
            var file = _profile.Launcher.GetValue(section, "File") ?? "—";
            cells = ["At launch", $"Update the settings file ({type})", "—", file,
                "The portable copy is updated in place", "Updated file stays with the portable data",
                type.TrimStart().StartsWith("XML", StringComparison.OrdinalIgnoreCase) ? "Needs XML support turned on" : RiskText(CapabilityStatus.Supported)];
        }
        else if (string.Equals(section, "Launch", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(entry.Key, "ProgramExecutable", StringComparison.OrdinalIgnoreCase)
                || entry.Key.StartsWith("ProgramExecutable", StringComparison.OrdinalIgnoreCase))
                cells = ["At launch", "Run this program", "—", entry.Value, "Unchanged", "—", RiskText(status)];
            else if (string.Equals(entry.Key, "CommandLineArguments", StringComparison.OrdinalIgnoreCase))
                cells = ["At launch", "Pass these arguments to the program", "—", "—", "Unchanged", "—", RiskText(status)];
            else if (string.Equals(entry.Key, "CloseEXE", StringComparison.OrdinalIgnoreCase))
                cells = ["Before upgrading", "Close this running program first", entry.Value, "—", "Unchanged", "—", RiskText(status)];
            else if (string.Equals(entry.Key, "WaitForProgram", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Key, "WaitForOtherInstances", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Key, "CleanTemp", StringComparison.OrdinalIgnoreCase))
                cells = ["At exit", $"Set {entry.Key}", "—", "—", "Unchanged", "Cleanup runs after the program exits", RiskText(status)];
        }
        return cells;
    }

    private static string[] InstallerCells(IniEntryNode entry, bool isBlocked)
    {
        string[] cells = ["—", "App\\AppInfo\\installer.ini", "—", "—", "None"];
        var section = entry.Section!;
        string DataImpact(string path)
        {
            var normalized = path.Trim().TrimStart('\\', '/');
            return normalized.Equals("Data", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)
                ? "User data" : "App files only";
        }
        if (string.Equals(section, "DirectoriesToPreserve", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "FilesToPreserve", StringComparison.OrdinalIgnoreCase))
            cells = ["Kept across the upgrade", entry.Value, "Preserve", "—", DataImpact(entry.Value)];
        else if (string.Equals(section, "DirectoriesToRemove", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "FilesToRemove", StringComparison.OrdinalIgnoreCase))
            cells = ["Removed during the upgrade", entry.Value, "Remove", "—", DataImpact(entry.Value)];
        else if (string.Equals(section, "MainDirectories", StringComparison.OrdinalIgnoreCase))
            cells = ["Decided at upgrade", "—", "—", "—", "Data is always preserved; only App/Other can be removed"];
        else if (string.Equals(section, "OptionalComponents", StringComparison.OrdinalIgnoreCase))
            cells = InstallerRules.NumberSuffix(entry.Key, "OptionalDirectory") is not null
                || InstallerRules.NumberSuffix(entry.Key, "OptionalFile") is not null
                ? ["Installed with the optional section", entry.Value, "Optional", "Only when the optional section is chosen", "None"]
                : ["Shapes the optional section", "—", "—", "—", "None"];
        else if (string.Equals(section, "Languages", StringComparison.OrdinalIgnoreCase))
            cells = [InstallerRules.ParseBool(entry.Value) == true ? "Offered in the installer" : "Not offered", "—", "—", "—", "None"];
        else if (string.Equals(section, "CheckRunning", StringComparison.OrdinalIgnoreCase))
            cells = ["Checked before upgrading", entry.Value, "—", "The running app is closed first", "None"];
        if (isBlocked)
            cells = ["Blocked — see the note below", cells[1], cells[2], "Blocked by the upgrade-guard order", "Not built"];
        return cells;
    }

    private static string[] MetadataCells(bool isBlocked) => isBlocked
        ? ["Blocked — see the note below", "App\\AppInfo\\appinfo.ini", "—", "Blocked by the upgrade-guard order", "Not built"]
        : ["—", "App\\AppInfo\\appinfo.ini", "—", "—", "None — package information only"];

    // ----- Editing -----

    private (PortableDocumentKind Kind, IniEntryNode? Entry) Resolve(string rowId)
    {
        var parts = rowId.Split('\u001f');
        if (parts.Length != 3 || !parts[0].Contains(':')) return (PortableDocumentKind.Launcher, null);
        var head = parts[0].Split(':');
        if (!int.TryParse(head[0], out var kindNumber) || !Enum.IsDefined(typeof(PortableDocumentKind), kindNumber)) return (PortableDocumentKind.Launcher, null);
        var kind = (PortableDocumentKind)kindNumber;
        var section = head.Length > 1 ? string.Join(':', head.Skip(1)) : "";
        if (!int.TryParse(parts[2], out var occurrence)) return (kind, null);
        var matches = DocumentFor(kind).AllEntries
            .Where(e => string.Equals(e.Section, section, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(e.Key, parts[1], StringComparison.OrdinalIgnoreCase)).ToList();
        return (kind, occurrence >= 0 && occurrence < matches.Count ? matches[occurrence] : null);
    }

    private string RowIdFor(PortableDocumentKind kind, IniEntryNode entry)
    {
        var occurrence = 0;
        foreach (var candidate in DocumentFor(kind).AllEntries)
        {
            if (ReferenceEquals(candidate, entry)) break;
            if (string.Equals(candidate.Section, entry.Section, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Key, entry.Key, StringComparison.OrdinalIgnoreCase)) occurrence++;
        }
        return RowId(kind, entry.Section!, entry.Key, occurrence);
    }

    private static void SetEntryValue(IniEntryNode entry, string rawValue)
    {
        entry.RawValue = rawValue;
        entry.RawText = entry.RawKey + "=" + rawValue;
    }

    /// <summary>Updates the value of the entry behind a row. The text is stored exactly as typed.</summary>
    public bool UpdateRowValue(string rowId, string value)
    {
        var (_, entry) = Resolve(rowId);
        if (entry is null) return false;
        SetEntryValue(entry, value);
        return true;
    }

    /// <summary>
    /// Renames the key of the entry behind a row (arbitrary-key sections only) and
    /// returns the row's new id. Throws <see cref="UserInputException"/> for a key
    /// that cannot appear in an INI entry.
    /// </summary>
    public string UpdateRowKey(string rowId, string newKey)
    {
        var (kind, entry) = Resolve(rowId);
        if (entry is null || entry.Section is null) return rowId;
        if (!KeyEditable(kind, entry.Section, entry.Key)) return rowId;
        var key = newKey.Trim();
        if (key.Length == 0 || key.Length > 260 || key.Contains('=') || key.Any(char.IsControl))
            throw new UserInputException("Enter a key name without '=' or control characters.");
        entry.RawKey = key;
        entry.Key = key;
        entry.RawText = key + "=" + entry.RawValue;
        return RowIdFor(kind, entry);
    }

    /// <summary>
    /// Removes the rule behind a row. Sequential lists and numbered installer
    /// lists close the gap by shifting later values up, so PAL never stops
    /// reading at a missing number.
    /// </summary>
    public bool RemoveRow(string rowId)
    {
        if (rowId.StartsWith("ext:", StringComparison.Ordinal))
            return int.TryParse(rowId.AsSpan(4), out var index) && RemoveExtension(index);
        var (kind, entry) = Resolve(rowId);
        if (entry is null) return false;
        var doc = DocumentFor(kind);
        var group = SequentialGroup(kind, doc, entry);
        if (group is not null)
        {
            var index = group.IndexOf(entry);
            for (var i = index; i < group.Count - 1; i++) SetEntryValue(group[i], group[i + 1].RawValue);
            return doc.RemoveEntry(group[^1]);
        }
        return doc.RemoveEntry(entry);
    }

    /// <summary>
    /// Moves a rule up or down (<paramref name="direction"/> -1 or +1). Sequential
    /// and numbered lists swap values so numbering stays gap-free; ordered
    /// sections such as [Environment] swap the entries themselves; FileWrite
    /// rules swap whole sections.
    /// </summary>
    public bool MoveRow(string rowId, int direction)
    {
        if (direction is not (-1 or 1)) return false;
        var (kind, entry) = Resolve(rowId);
        if (entry is null || entry.Section is null) return false;
        var doc = DocumentFor(kind);
        var group = SequentialGroup(kind, doc, entry);
        if (group is not null)
        {
            var index = group.IndexOf(entry);
            var target = index + direction;
            if (target < 0 || target >= group.Count) return false;
            (group[index].RawValue, group[target].RawValue) = (group[target].RawValue, group[index].RawValue);
            foreach (var e in new[] { group[index], group[target] }) e.RawText = e.RawKey + "=" + e.RawValue;
            return true;
        }
        if (kind == PortableDocumentKind.Launcher
            && CapabilityCatalog.IsPatternSection(entry.Section, out var prefix, out _) && prefix == "FileWrite")
            return SwapFileWriteSections(doc, entry.Section!, direction);
        return doc.MoveEntry(entry, direction);
    }

    private static bool SwapFileWriteSections(IniDocument doc, string section, int direction)
    {
        var sections = doc.Sections
            .Where(s => CapabilityCatalog.IsPatternSection(s.Name, out var p, out _) && p == "FileWrite")
            .OrderBy(s => { CapabilityCatalog.IsPatternSection(s.Name, out _, out var n); return n; })
            .Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var index = sections.FindIndex(s => string.Equals(s, section, StringComparison.OrdinalIgnoreCase));
        var target = index + direction;
        if (index < 0 || target < 0 || target >= sections.Count) return false;
        var first = doc.Entries(sections[index]).Select(e => (e.Key, e.RawValue)).ToList();
        var second = doc.Entries(sections[target]).Select(e => (e.Key, e.RawValue)).ToList();
        foreach (var e in doc.Entries(sections[index]).ToList()) doc.RemoveEntry(e);
        foreach (var e in doc.Entries(sections[target]).ToList()) doc.RemoveEntry(e);
        foreach (var (key, value) in second) doc.AppendEntry(sections[index], key, value);
        foreach (var (key, value) in first) doc.AppendEntry(sections[target], key, value);
        return true;
    }

    /// <summary>
    /// Adds an entry and returns its row id. Sequential launcher sections and
    /// numbered installer lists are given the next number automatically when
    /// <paramref name="key"/> is the family name (for example
    /// <c>PreserveDirectory</c>) or the next sequential slot is meant.
    /// </summary>
    public string AddEntry(PortableDocumentKind kind, string section, string key, string value)
    {
        var doc = DocumentFor(kind);
        if (kind == PortableDocumentKind.Launcher && CapabilityCatalog.IsSequentialSection(section))
        {
            var next = doc.Entries(section).Where(e => int.TryParse(e.Key, out _))
                .Select(e => int.Parse(e.Key)).DefaultIfEmpty(0).Max() + 1;
            key = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (kind == PortableDocumentKind.Launcher
            && string.Equals(section, "Launch", StringComparison.OrdinalIgnoreCase)
            && string.Equals(key, "WaitForEXE", StringComparison.OrdinalIgnoreCase))
        {
            var next = doc.Entries("Launch").Where(e => CapabilityCatalog.IsWaitForExeKey(e.Key))
                .Select(e => int.Parse(e.Key.AsSpan("WaitForEXE".Length))).DefaultIfEmpty(0).Max() + 1;
            key = "WaitForEXE" + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (kind == PortableDocumentKind.Installer && NumberedPrefix(section, key) is { } prefix
            && string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase))
        {
            var next = doc.Entries(section).Select(e => InstallerRules.NumberSuffix(e.Key, prefix))
                .Where(n => n is not null).Select(n => n!.Value).DefaultIfEmpty(0).Max() + 1;
            key = prefix + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var entry = doc.AppendEntry(section, key, value);
        return RowIdFor(kind, entry);
    }

    /// <summary>Adds the next [FileWriteN] section with Type/File fields and returns the row id of its Type entry.</summary>
    public string AddFileWriteRule()
    {
        var doc = _profile.Launcher;
        var next = doc.Sections.Where(s => CapabilityCatalog.IsPatternSection(s.Name, out var p, out _) && p == "FileWrite")
            .Select(s => { CapabilityCatalog.IsPatternSection(s.Name, out _, out var n); return n; })
            .DefaultIfEmpty(0).Max() + 1;
        var section = "FileWrite" + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var type = doc.AppendEntry(section, "Type", "INI");
        doc.AppendEntry(section, "File", "");
        return RowIdFor(PortableDocumentKind.Launcher, type);
    }

    /// <summary>Adds a sensible blank rule for a category (used by the workspace's Add button) and returns its row id, or null when the category has no addable rules.</summary>
    public string? AddDefaultRow(ProfileCategory category) => category switch
    {
        ProfileCategory.OverviewLaunch => AddEntry(PortableDocumentKind.Launcher, "Launch", "CommandLineArguments", ""),
        ProfileCategory.DataFilesFolders => AddEntry(PortableDocumentKind.Launcher, "FilesMove", "settings.ini", "%APPDATA%\\"),
        ProfileCategory.Registry => AddEntry(PortableDocumentKind.Launcher, "RegistryKeys", "NewApp", "HKCU\\Software\\"),
        ProfileCategory.ConfigPathUpdates => AddFileWriteRule(),
        ProfileCategory.EnvironmentLanguage => AddEntry(PortableDocumentKind.Launcher, "Environment", "NEW_VARIABLE", ""),
        ProfileCategory.ProcessesCompatibility => AddEntry(PortableDocumentKind.Launcher, "Launch", "WaitForEXE", ""),
        ProfileCategory.InstallUpgradeRules => AddEntry(PortableDocumentKind.Installer, "DirectoriesToPreserve", "PreserveDirectory", "App\\"),
        ProfileCategory.PackageDetails => AddEntry(PortableDocumentKind.Metadata, "Details", "Description", ""),
        _ => null,
    };

    /// <summary>The numbered-list family prefix a key belongs to in a section, or null.</summary>
    private static string? NumberedPrefix(string section, string key)
    {
        foreach (var (candidateSection, prefix) in new[]
        {
            ("DirectoriesToPreserve", "PreserveDirectory"), ("DirectoriesToRemove", "RemoveDirectory"),
            ("FilesToPreserve", "PreserveFile"), ("FilesToRemove", "RemoveFile"),
            ("OptionalComponents", "OptionalDirectory"), ("OptionalComponents", "OptionalFile"),
        })
        {
            if (string.Equals(section, candidateSection, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase)
                    || InstallerRules.NumberSuffix(key, prefix) is not null))
                return prefix;
        }
        return null;
    }

    /// <summary>
    /// The ordered entry group an entry belongs to when it is part of a
    /// sequential or numbered list (values are permuted/renumbered on edit), or
    /// null for ordinary entries (nodes are moved/removed directly).
    /// </summary>
    private static List<IniEntryNode>? SequentialGroup(PortableDocumentKind kind, IniDocument doc, IniEntryNode entry)
    {
        if (entry.Section is null) return null;
        if (kind == PortableDocumentKind.Launcher && CapabilityCatalog.IsSequentialSection(entry.Section))
            return doc.Entries(entry.Section).Where(e => int.TryParse(e.Key, out _)).OrderBy(e => int.Parse(e.Key)).ToList();
        if (kind == PortableDocumentKind.Launcher
            && string.Equals(entry.Section, "Launch", StringComparison.OrdinalIgnoreCase)
            && CapabilityCatalog.IsWaitForExeKey(entry.Key))
            return doc.Entries("Launch").Where(e => CapabilityCatalog.IsWaitForExeKey(e.Key))
                .OrderBy(e => int.Parse(e.Key.AsSpan("WaitForEXE".Length))).ToList();
        if (kind == PortableDocumentKind.Installer && NumberedPrefix(entry.Section, entry.Key) is { } prefix)
            return doc.Entries(entry.Section).Where(e => InstallerRules.NumberSuffix(e.Key, prefix) is not null)
                .OrderBy(e => InstallerRules.NumberSuffix(e.Key, prefix)).ToList();
        return null;
    }

    private static bool KeyEditable(PortableDocumentKind kind, string section, string key)
    {
        if (kind != PortableDocumentKind.Launcher) return false;
        if (CapabilityCatalog.IsSequentialSection(section) || CapabilityCatalog.IsWaitForExeKey(key)) return false;
        if (CapabilityCatalog.IsPatternSection(section, out _, out _)) return false;
        return CapabilityCatalog.IsArbitrarySection(section);
    }

    private static bool Reorderable(PortableDocumentKind kind, string section, string key)
    {
        if (kind == PortableDocumentKind.Launcher)
        {
            if (CapabilityCatalog.IsSequentialSection(section) || CapabilityCatalog.IsWaitForExeKey(key)) return true;
            if (CapabilityCatalog.IsPatternSection(section, out var prefix, out _)) return prefix == "FileWrite";
            return CapabilityCatalog.IsArbitrarySection(section);
        }
        return kind == PortableDocumentKind.Installer && NumberedPrefix(section, key) is not null;
    }

    // ----- Trusted extensions -----

    public IReadOnlyList<TrustedExtension> Extensions => _profile.TrustedExtensions;

    /// <summary>
    /// Records a custom source extension as data. It starts untrusted; only
    /// <see cref="SetExtensionTrusted"/> — an explicit in-product action — can
    /// trust it, and only for the exact path and hash recorded here.
    /// </summary>
    public void AddExtension(string relativePath, string sha256, string purpose)
    {
        var path = relativePath.Trim();
        var hash = sha256.Trim();
        if (path.Length == 0 || path.Length > 260 || path.Any(char.IsControl))
            throw new UserInputException("Enter the extension's path inside the profile folder.");
        if (hash.Length != 64 || !hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            throw new UserInputException("Enter the extension file's 64-character hexadecimal SHA-256.");
        _profile = _profile with
        {
            TrustedExtensions = [.. _profile.TrustedExtensions, new TrustedExtension(path, hash, purpose.Trim(), false)],
        };
    }

    public bool RemoveExtension(int index)
    {
        if (index < 0 || index >= _profile.TrustedExtensions.Count) return false;
        _profile = _profile with { TrustedExtensions = _profile.TrustedExtensions.Where((_, i) => i != index).ToList() };
        return true;
    }

    /// <summary>The only operation that can mark an extension trusted; the trust binds the recorded path and hash.</summary>
    public bool SetExtensionTrusted(int index, bool trusted)
    {
        if (index < 0 || index >= _profile.TrustedExtensions.Count) return false;
        _profile = _profile with
        {
            TrustedExtensions = _profile.TrustedExtensions.Select((e, i) => i == index ? e with { Trusted = trusted } : e).ToList(),
        };
        return true;
    }
}
