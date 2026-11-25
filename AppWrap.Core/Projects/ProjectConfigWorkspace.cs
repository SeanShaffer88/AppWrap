using System.Text;
using AppWrap.Core.PortableProfiles;

namespace AppWrap.Core.Projects;

/// <summary>The three INI documents of a PAF project that the Phase 2 editors cover.</summary>
public enum ProjectDocumentKind { AppInfo, Launcher, Installer }

/// <summary>The two custom-code files Advanced mode can unlock for direct text editing.</summary>
public enum CustomCodeKind { Launcher, Installer }

/// <summary>How a guided field is edited: free text, or a true/false switch.</summary>
public enum ConfigFieldKind { Text, Boolean }

/// <summary>
/// One guided editor field: a single INI key in one project document.
/// <see cref="Required"/> marks the fields <see cref="PafValidator"/> treats as
/// errors when missing; the editor shows those with a required marker.
/// </summary>
public sealed record ConfigField(
    ProjectDocumentKind Document, string Section, string Key, string Label, ConfigFieldKind Kind, bool Required, string Hint);

/// <summary>A field's current value plus the validator diagnostics attached to exactly that field.</summary>
public sealed record ConfigFieldState(ConfigField Field, string? Value, bool Present, IReadOnlyList<PafDiagnostic> Diagnostics);

/// <summary>
/// A custom-code file as the editor sees it. <see cref="Editable"/> is true only
/// while Advanced mode is on; the text is the file's current content (empty when
/// the file does not exist yet).
/// </summary>
public sealed record CustomCodeDocument(CustomCodeKind Kind, string DisplayPath, bool Exists, string Text, bool Editable);

public enum CustomCodeWriteStatus { Written, RefusedAdvancedModeOff }

/// <summary>
/// The typed outcome of a custom-code write attempt. A refusal is a normal result,
/// not an exception: with Advanced mode off the file is left exactly as it was.
/// </summary>
public sealed record CustomCodeWriteOutcome(CustomCodeWriteStatus Status, string Message)
{
    public bool Written => Status == CustomCodeWriteStatus.Written;
}

/// <summary>
/// Testable state and editing logic behind the wizard's project configuration
/// panel (Development Suite Phase 2). The single source of truth is the project's
/// INI files on disk, parsed through the lossless <see cref="IniDocument"/>: the
/// workspace holds the parsed documents in memory, field and text edits mutate
/// those documents, and <see cref="Save"/> writes the changed documents back,
/// refreshes the project snapshot and persists the project file. Unknown
/// sections, keys, comments and ordering survive every edit because nothing is
/// ever re-serialized from a typed model.
///
/// Validation reuses <see cref="PafValidator"/> unchanged: diagnostics are
/// computed with the in-memory documents overlaid on the folder, so unsaved
/// edits validate with exactly the rules the saved project will face.
///
/// Advanced mode is a per-project choice persisted in the project file's user
/// choices (<c>advancedMode</c>), off by default and off for inferred projects.
/// It gates only direct text editing of the two custom-code files inside the
/// project; it changes no packaging behaviour.
/// </summary>
public sealed class ProjectConfigWorkspace
{
    /// <summary>The user-choice key under which the Advanced mode flag persists in appwrap.project.json.</summary>
    public const string AdvancedModeChoiceKey = "advancedMode";

    /// <summary>Bound for custom-code files, matching the project file's own size bound.</summary>
    public const int MaxCustomCodeBytes = ProjectStore.MaxBytes;

    private readonly Dictionary<ProjectDocumentKind, IniDocument> _documents = new();
    private readonly Dictionary<ProjectDocumentKind, string> _originalTexts = new();
    private readonly Dictionary<ProjectDocumentKind, string> _displayPaths = new();

    private ProjectConfigWorkspace(string root, PafProject project, bool inferred)
    {
        Root = root;
        Project = project;
        ProjectWasInferred = inferred;
        Reload();
    }

    public string Root { get; }
    public PafProject Project { get; }

    /// <summary>True when the project was inferred from a bare folder rather than read from appwrap.project.json.</summary>
    public bool ProjectWasInferred { get; }

    /// <summary>Loads the folder's project (inferring one for bare folders) and its configuration documents.</summary>
    public static ProjectConfigWorkspace Load(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var project = ProjectStore.LoadOrInfer(fullRoot, out var inferred);
        return new ProjectConfigWorkspace(fullRoot, project, inferred);
    }

    /// <summary>
    /// Opens the workspace over a project the caller already loaded, so the wizard
    /// and the editors share one project instance and can never diverge.
    /// </summary>
    public static ProjectConfigWorkspace Open(string root, PafProject project, bool inferred) =>
        new(Path.GetFullPath(root), project, inferred);

    /// <summary>
    /// Advanced mode: direct text editing of the project's custom-code files.
    /// Off by default; persisted per project in the project file's user choices.
    /// </summary>
    public bool AdvancedMode
    {
        get => Project.UserChoices.TryGetValue(AdvancedModeChoiceKey, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        set => Project.UserChoices[AdvancedModeChoiceKey] = value ? "true" : "false";
    }

    /// <summary>The AppID declared by the current (in-memory) appinfo document, when one is set.</summary>
    public string? AppId => DocumentFor(ProjectDocumentKind.AppInfo).GetValue("Details", "AppID") is { Length: > 0 } id ? id : null;

    /// <summary>The project-relative path (forward slashes) of a configuration document.</summary>
    public string DisplayPathFor(ProjectDocumentKind kind) => _displayPaths[kind];

    /// <summary>The parsed document behind an editor tab. The document instance is stable until <see cref="Reload"/>.</summary>
    public IniDocument DocumentFor(ProjectDocumentKind kind) => _documents[kind];

    public string GetDocumentText(ProjectDocumentKind kind) => _documents[kind].Serialize();

    /// <summary>
    /// Replaces a document from edited text, re-parsed through
    /// <see cref="IniDocument"/>: content problems never throw here — they are
    /// retained as raw lines and surface as diagnostics — so an invalid draft is
    /// kept, not silently discarded, until it is corrected.
    /// </summary>
    public void SetDocumentText(ProjectDocumentKind kind, string text) => _documents[kind] = IniDocument.Parse(text ?? "");

    public string? GetField(ProjectDocumentKind kind, string section, string key) =>
        _documents[kind].GetValue(section, key);

    public string? GetField(ConfigField field) => GetField(field.Document, field.Section, field.Key);

    /// <summary>
    /// Sets one field through the document (an in-place edit when the key exists,
    /// an append to its section otherwise). A null value removes the key's entries
    /// entirely; unknown content around the edit is never touched.
    /// </summary>
    public void SetField(ProjectDocumentKind kind, string section, string key, string? value)
    {
        var document = _documents[kind];
        if (value is null)
        {
            foreach (var entry in document.Entries(section)
                         .Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).ToList())
                document.RemoveEntry(entry);
            return;
        }
        document.SetValue(section, key, value);
    }

    public void SetField(ConfigField field, string? value) => SetField(field.Document, field.Section, field.Key, value);

    /// <summary>The guided fields of one document, in editor order.</summary>
    public static IReadOnlyList<ConfigField> FieldsFor(ProjectDocumentKind kind) =>
        Catalog.Where(f => f.Document == kind).ToList();

    /// <summary>Every field of a document with its current value and its attached diagnostics.</summary>
    public IReadOnlyList<ConfigFieldState> FieldStates(ProjectDocumentKind kind)
    {
        var diagnostics = DiagnosticsFor(kind);
        return FieldsFor(kind).Select(field => new ConfigFieldState(
            field,
            GetField(field),
            GetField(field) is not null,
            diagnostics.Where(d =>
                string.Equals(d.Section, field.Section, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.Key, field.Key, StringComparison.OrdinalIgnoreCase)).ToList())).ToList();
    }

    /// <summary>True when a document's current text differs from what is on disk (or would create its file).</summary>
    public bool IsDirty(ProjectDocumentKind kind) => !string.Equals(GetDocumentText(kind), _originalTexts[kind], StringComparison.Ordinal);

    public bool HasUnsavedChanges => Enum.GetValues<ProjectDocumentKind>().Any(IsDirty);

    /// <summary>
    /// Runs <see cref="PafValidator"/> over the whole project with the current
    /// in-memory documents overlaid, so unsaved edits are validated by the same
    /// rules as the saved project. Documents that exist neither on disk nor as
    /// edits are left out, preserving the validator's absent-file severities.
    /// </summary>
    public IReadOnlyList<PafDiagnostic> ValidateAll()
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in Enum.GetValues<ProjectDocumentKind>())
        {
            var path = _displayPaths[kind];
            if (File.Exists(FullPath(path)) || IsDirty(kind) || _documents[kind].Nodes.Count > 0)
                overrides[path] = GetDocumentText(kind);
        }
        return PafValidator.Validate(Root, Project, overrides);
    }

    /// <summary>The current whole-project diagnostics belonging to one document.</summary>
    public IReadOnlyList<PafDiagnostic> DiagnosticsFor(ProjectDocumentKind kind) =>
        ValidateAll().Where(d => string.Equals(d.FilePath, _displayPaths[kind], StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Writes every changed document to its file (creating folders as needed),
    /// refreshes the project snapshot from the resulting tree, and persists the
    /// project file. Documents that were never edited and do not exist are not
    /// created — AppWrap still generates them during packaging.
    /// </summary>
    public void Save()
    {
        foreach (var kind in Enum.GetValues<ProjectDocumentKind>())
        {
            if (!IsDirty(kind)) continue;
            var fullPath = FullPath(_displayPaths[kind]);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            WriteAtomically(fullPath, GetDocumentText(kind));
            _originalTexts[kind] = GetDocumentText(kind);
        }
        var snapshot = SourceSnapshot.Compute(Root);
        Project.SourceSnapshotHash = snapshot.Sha256;
        Project.SourceSnapshotTruncated = snapshot.Truncated;
        ProjectStore.Save(Root, Project);
    }

    /// <summary>Discards in-memory edits and re-reads every document from disk.</summary>
    public void Reload()
    {
        _displayPaths[ProjectDocumentKind.AppInfo] = PafLayout.AppInfoPath;
        _displayPaths[ProjectDocumentKind.Installer] = PafLayout.InstallerIniPath;
        var appInfo = ReadDocument(PafLayout.AppInfoPath);
        _documents[ProjectDocumentKind.AppInfo] = appInfo;
        _originalTexts[ProjectDocumentKind.AppInfo] = appInfo.Serialize();
        _displayPaths[ProjectDocumentKind.Launcher] = ResolveLauncherPath(appInfo);
        foreach (var kind in new[] { ProjectDocumentKind.Launcher, ProjectDocumentKind.Installer })
        {
            var document = ReadDocument(_displayPaths[kind]);
            _documents[kind] = document;
            _originalTexts[kind] = document.Serialize();
        }
    }

    /// <summary>Reads a custom-code file for display. Reading is always allowed; <see cref="CustomCodeDocument.Editable"/> carries the gate.</summary>
    public CustomCodeDocument ReadCustomCode(CustomCodeKind kind)
    {
        var displayPath = DisplayPathFor(kind);
        var fullPath = FullPath(displayPath);
        var exists = File.Exists(fullPath);
        var text = "";
        if (exists)
        {
            if (new FileInfo(fullPath).Length > MaxCustomCodeBytes)
                throw new UserInputException($"The custom code file ({displayPath}) is too large to edit (up to 1 MB).");
            text = File.ReadAllText(fullPath);
        }
        return new CustomCodeDocument(kind, displayPath, exists, text, AdvancedMode);
    }

    /// <summary>
    /// Writes a custom-code file — an explicit editor action, persisted
    /// immediately. With Advanced mode off the write is refused as a typed
    /// outcome and the file is left exactly as it was; no packaging behaviour
    /// is affected either way.
    /// </summary>
    public CustomCodeWriteOutcome WriteCustomCode(CustomCodeKind kind, string text)
    {
        var displayPath = DisplayPathFor(kind);
        if (!AdvancedMode)
            return new CustomCodeWriteOutcome(CustomCodeWriteStatus.RefusedAdvancedModeOff,
                $"Advanced mode is off, so {displayPath} was not changed. Turn Advanced mode on to edit custom code.");
        ArgumentNullException.ThrowIfNull(text);
        if (Encoding.UTF8.GetByteCount(text) > MaxCustomCodeBytes)
            throw new UserInputException($"The custom code file ({displayPath}) is too large to save (up to 1 MB).");
        var fullPath = FullPath(displayPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        WriteAtomically(fullPath, text);
        return new CustomCodeWriteOutcome(CustomCodeWriteStatus.Written, $"{displayPath} saved.");
    }

    public static string DisplayPathFor(CustomCodeKind kind) => kind switch
    {
        CustomCodeKind.Launcher => PafLayout.LauncherCustomPath,
        CustomCodeKind.Installer => PafLayout.InstallerCustomPath,
        _ => throw new UserInputException("Unknown custom code kind."),
    };

    private string FullPath(string displayPath) =>
        Path.Combine(Root, displayPath.Replace('/', Path.DirectorySeparatorChar));

    private IniDocument ReadDocument(string displayPath)
    {
        var fullPath = FullPath(displayPath);
        if (!File.Exists(fullPath)) return IniDocument.Parse("");
        if (new FileInfo(fullPath).Length > IniDocument.MaxBytes)
            throw new UserInputException($"The configuration file ({displayPath}) is too large (up to 1 MB).");
        return IniDocument.Parse(File.ReadAllText(fullPath));
    }

    /// <summary>
    /// The launcher INI the editor binds to, resolved once per load: the AppID's
    /// own file when appinfo declares one, else the single existing launcher INI,
    /// else the path packaging will use for the wizard-derived identity of the
    /// project name (the same derivation PortableAppsGenerator applies).
    /// </summary>
    private string ResolveLauncherPath(IniDocument appInfo)
    {
        var appId = appInfo.GetValue("Details", "AppID");
        if (!string.IsNullOrWhiteSpace(appId)) return PafLayout.LauncherIniPath(appId.Trim());
        var launcherDir = Path.Combine(Root, "App", "AppInfo", "Launcher");
        if (Directory.Exists(launcherDir))
        {
            var existing = Directory.EnumerateFiles(launcherDir, "*.ini")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            if (existing.Count > 0)
                return PafLayout.ToDisplayPath(Path.GetRelativePath(Root, existing[0]));
        }
        var derived = InputRules.SafeId(Project.Name).Replace("-", "", StringComparison.Ordinal);
        return PafLayout.LauncherIniPath((derived.Length > 0 ? derived : "App") + "Portable");
    }

    private static void WriteAtomically(string fullPath, string text)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, ".appwrap-config-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, text);
        File.Move(temporary, fullPath, overwrite: true);
    }

    // ----- Guided field catalogues, grounded in what AppWrap emits and validates -----

    private static ConfigField Text(ProjectDocumentKind document, string section, string key, string label, bool required = false, string hint = "") =>
        new(document, section, key, label, ConfigFieldKind.Text, required, hint);

    private static ConfigField Bool(ProjectDocumentKind document, string section, string key, string label, string hint = "") =>
        new(document, section, key, label, ConfigFieldKind.Boolean, false, hint);

    /// <summary>
    /// Every guided field. AppInfo follows PortableAppsGenerator's emitted
    /// document plus the metadata surface PafValidator and InstallerRules model;
    /// Launcher follows the pinned PAL 2.2.9 key catalogue (CapabilityCatalog),
    /// including [LiveMode] CopyApp, which the pinned RunLocally segment reads —
    /// CopyData is omitted because PAL 2.2.9 removed it; Installer follows the
    /// scalar keys of InstallerRules (the numbered preserve/remove and optional
    /// payload lists stay editable through the document text view).
    /// </summary>
    public static IReadOnlyList<ConfigField> Catalog { get; } =
    [
        // appinfo.ini — [Format] and [Details]
        Text(ProjectDocumentKind.AppInfo, "Format", "Type", "Format type", hint: "PortableApps.comFormat for PAF packages."),
        Text(ProjectDocumentKind.AppInfo, "Format", "Version", "Format version", hint: "The PAF version this package targets (3.9)."),
        Text(ProjectDocumentKind.AppInfo, "Details", "Name", "Name", required: true),
        Text(ProjectDocumentKind.AppInfo, "Details", "AppID", "AppID", required: true, hint: "Package identity; letters and digits, ending in Portable by convention."),
        Text(ProjectDocumentKind.AppInfo, "Details", "Publisher", "Publisher"),
        Text(ProjectDocumentKind.AppInfo, "Details", "Homepage", "Homepage"),
        Text(ProjectDocumentKind.AppInfo, "Details", "Category", "Category", hint: "A PortableApps.com category, for example Utilities."),
        Text(ProjectDocumentKind.AppInfo, "Details", "Description", "Description"),
        Text(ProjectDocumentKind.AppInfo, "Details", "Language", "Language"),
        Text(ProjectDocumentKind.AppInfo, "Details", "Trademarks", "Trademarks"),
        Text(ProjectDocumentKind.AppInfo, "Details", "Donate", "Donate page"),
        Text(ProjectDocumentKind.AppInfo, "Details", "InstallType", "Install type"),
        // appinfo.ini — [License]
        Bool(ProjectDocumentKind.AppInfo, "License", "Shareable", "Shareable"),
        Bool(ProjectDocumentKind.AppInfo, "License", "OpenSource", "Open source"),
        Bool(ProjectDocumentKind.AppInfo, "License", "Freeware", "Freeware"),
        Bool(ProjectDocumentKind.AppInfo, "License", "CommercialUse", "Commercial use"),
        Text(ProjectDocumentKind.AppInfo, "License", "EULAVersion", "EULA version"),
        // appinfo.ini — [Version]
        Text(ProjectDocumentKind.AppInfo, "Version", "PackageVersion", "Package version", required: true, hint: "One to four numbers separated by dots, for example 1.2.0.0."),
        Text(ProjectDocumentKind.AppInfo, "Version", "DisplayVersion", "Display version"),
        // appinfo.ini — [Control]
        Text(ProjectDocumentKind.AppInfo, "Control", "Icons", "Icons", hint: "How many icons the package exposes (usually 1)."),
        Text(ProjectDocumentKind.AppInfo, "Control", "Start", "Start", required: true, hint: "The launcher executable the platform starts, usually <AppID>.exe."),
        Text(ProjectDocumentKind.AppInfo, "Control", "ExtractIcon", "Extract icon"),
        Text(ProjectDocumentKind.AppInfo, "Control", "ExtractName", "Extract name"),
        Text(ProjectDocumentKind.AppInfo, "Control", "BaseAppID", "Base AppID"),
        Text(ProjectDocumentKind.AppInfo, "Control", "BaseAppID64", "Base AppID (64-bit)"),
        Text(ProjectDocumentKind.AppInfo, "Control", "BaseAppIDARM64", "Base AppID (ARM64)"),
        Text(ProjectDocumentKind.AppInfo, "Control", "ExitEXE", "Exit executable"),
        Text(ProjectDocumentKind.AppInfo, "Control", "ExitParameters", "Exit parameters"),
        // appinfo.ini — [Dependencies] and [SpecialPaths]
        Text(ProjectDocumentKind.AppInfo, "Dependencies", "UsesJava", "Uses Java"),
        Text(ProjectDocumentKind.AppInfo, "Dependencies", "UsesDotNetVersion", "Uses .NET version"),
        Text(ProjectDocumentKind.AppInfo, "Dependencies", "UsesGhostscript", "Uses Ghostscript"),
        Text(ProjectDocumentKind.AppInfo, "Dependencies", "RequiresPortableApp", "Requires portable app"),
        Bool(ProjectDocumentKind.AppInfo, "Dependencies", "Requires64bitOS", "Requires 64-bit Windows"),
        Bool(ProjectDocumentKind.AppInfo, "Dependencies", "RequiresAdmin", "Requires administrator"),
        Text(ProjectDocumentKind.AppInfo, "SpecialPaths", "Plugins", "Plugins path"),

        // Launcher INI — [Launch]
        Text(ProjectDocumentKind.Launcher, "Launch", "ProgramExecutable", "Program executable", required: true, hint: "The application executable, relative to the package's App folder."),
        Text(ProjectDocumentKind.Launcher, "Launch", "ProgramExecutable64", "Program executable (64-bit)"),
        Text(ProjectDocumentKind.Launcher, "Launch", "ProgramExecutableWhenParameters", "Executable when parameters"),
        Text(ProjectDocumentKind.Launcher, "Launch", "ProgramExecutableWhenParameters64", "Executable when parameters (64-bit)"),
        Text(ProjectDocumentKind.Launcher, "Launch", "CommandLineArguments", "Command-line arguments"),
        Text(ProjectDocumentKind.Launcher, "Launch", "WorkingDirectory", "Working directory"),
        Text(ProjectDocumentKind.Launcher, "Launch", "MinOS", "Minimum Windows version"),
        Text(ProjectDocumentKind.Launcher, "Launch", "MaxOS", "Maximum Windows version"),
        Text(ProjectDocumentKind.Launcher, "Launch", "CloseEXE", "Close executable"),
        Text(ProjectDocumentKind.Launcher, "Launch", "SplashTime", "Splash time (ms)"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "WaitForProgram", "Wait for program"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "WaitForOtherInstances", "Wait for other instances"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "SinglePortableAppInstance", "Single portable instance"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "SingleAppInstance", "Single app instance"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "CleanTemp", "Clean temp on exit"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "LaunchAppAfterSplash", "Launch app after splash"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "RefreshShellIcons", "Refresh shell icons"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "HideCommandLineWindow", "Hide command-line window"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "DirectoryMoveOK", "Directory move OK"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "NoSpacesInPath", "No spaces in path"),
        Bool(ProjectDocumentKind.Launcher, "Launch", "SupportsUNC", "Supports UNC paths"),
        // Launcher INI — [Activate], [LiveMode], [Language]
        Bool(ProjectDocumentKind.Launcher, "Activate", "Registry", "Activate registry"),
        Bool(ProjectDocumentKind.Launcher, "Activate", "Java", "Activate Java"),
        Bool(ProjectDocumentKind.Launcher, "Activate", "XML", "Activate XML"),
        Bool(ProjectDocumentKind.Launcher, "LiveMode", "CopyApp", "Copy app when running locally", hint: "Live mode: copy the app to a writable location before running."),
        Text(ProjectDocumentKind.Launcher, "Language", "Base", "Language base"),
        Text(ProjectDocumentKind.Launcher, "Language", "Default", "Default language"),
        Text(ProjectDocumentKind.Launcher, "Language", "CheckIfExists", "Check language exists"),
        Text(ProjectDocumentKind.Launcher, "Language", "DefaultIfNotExists", "Default if language missing"),

        // installer.ini — [Source], [CheckRunning], [MainDirectories]
        Bool(ProjectDocumentKind.Installer, "Source", "IncludeInstallerSource", "Include installer source"),
        Text(ProjectDocumentKind.Installer, "CheckRunning", "CloseEXE", "Close executable", hint: "AppWrap's upgrade guard owns this value in generated packages; the generated value wins."),
        Text(ProjectDocumentKind.Installer, "CheckRunning", "CloseName", "Close name"),
        Bool(ProjectDocumentKind.Installer, "MainDirectories", "RemoveAppDirectory", "Remove App directory on uninstall"),
        Bool(ProjectDocumentKind.Installer, "MainDirectories", "RemoveOtherDirectory", "Remove Other directory on uninstall"),
        // installer.ini — [OptionalComponents] scalars
        Bool(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalComponents", "Optional components"),
        Text(ProjectDocumentKind.Installer, "OptionalComponents", "MainSectionTitle", "Main section title"),
        Text(ProjectDocumentKind.Installer, "OptionalComponents", "MainSectionDescription", "Main section description"),
        Text(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalSectionTitle", "Optional section title"),
        Text(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalSectionDescription", "Optional section description"),
        Text(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalSectionSelectedInstallType", "Selected install type"),
        Text(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalSectionNotSelectedInstallType", "Not-selected install type"),
        Bool(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalSectionPreSelectedIfNonEnglishInstall", "Pre-select if non-English install"),
        Bool(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalSectionPreSelectedIfWindows32Bit", "Pre-select on 32-bit Windows"),
        Text(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalSectionPreSelectedIfLessThanWindows", "Pre-select below Windows version"),
        Bool(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalSectionInstalledWhenSilent", "Install when silent"),
        Bool(ProjectDocumentKind.Installer, "OptionalComponents", "OptionalSectionInstalledWhenSilentOn32Bit", "Install when silent (32-bit)"),
        // installer.ini — [Languages]
        Bool(ProjectDocumentKind.Installer, "Languages", "ENGLISH", "Offer English", hint: "A [Languages] section must keep ENGLISH=true."),
    ];
}
