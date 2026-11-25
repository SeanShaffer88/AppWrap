using AppWrap.Core;
using AppWrap.Core.PortableProfiles;
using AppWrap.Core.Projects;
using AppWrap.Generators;

/// <summary>
/// Phase 6 compliance regression checks. The lists below mirror the support
/// matrices in docs/PAF-COMPLIANCE.md; the assertions run against the actual
/// data sources — PortableAppsGenerator output, the ProjectConfigWorkspace
/// field catalogue, and CapabilityCatalog — so removing or renaming a
/// supported field in code fails here even if the document is not updated.
/// (Doc-vs-code drift in the other direction is a review concern, not this
/// check's.)
/// </summary>
internal static class ComplianceChecks
{
    // docs/PAF-COMPLIANCE.md, appinfo.ini table: keys the generator always emits.
    private static readonly (string Section, string Key)[] EmittedAppInfoKeys =
    [
        ("Format", "Type"), ("Format", "Version"),
        ("Details", "Name"), ("Details", "AppID"), ("Details", "Publisher"),
        ("Details", "Homepage"), ("Details", "Category"), ("Details", "Description"),
        ("Details", "Language"),
        ("License", "Shareable"), ("License", "OpenSource"), ("License", "Freeware"),
        ("License", "CommercialUse"),
        ("Version", "PackageVersion"), ("Version", "DisplayVersion"),
        ("Control", "Icons"), ("Control", "Start"), ("Control", "BaseAppID"),
    ];

    // docs/PAF-COMPLIANCE.md, surface matrix: keys editable in the project editors.
    private static readonly (string Section, string Key)[] EditableAppInfoKeys =
    [
        ("Format", "Type"), ("Format", "Version"),
        ("Details", "Name"), ("Details", "AppID"), ("Details", "Publisher"),
        ("Details", "Homepage"), ("Details", "Category"), ("Details", "Description"),
        ("Details", "Language"), ("Details", "Trademarks"), ("Details", "Donate"),
        ("Details", "InstallType"),
        ("License", "Shareable"), ("License", "OpenSource"), ("License", "Freeware"),
        ("License", "CommercialUse"), ("License", "EULAVersion"),
        ("Version", "PackageVersion"), ("Version", "DisplayVersion"),
        ("Control", "Icons"), ("Control", "Start"), ("Control", "ExtractIcon"),
        ("Control", "ExtractName"), ("Control", "BaseAppID"), ("Control", "BaseAppID64"),
        ("Control", "BaseAppIDARM64"), ("Control", "ExitEXE"), ("Control", "ExitParameters"),
        ("Dependencies", "UsesJava"), ("Dependencies", "UsesDotNetVersion"),
        ("Dependencies", "UsesGhostscript"), ("Dependencies", "RequiresPortableApp"),
        ("Dependencies", "Requires64bitOS"), ("Dependencies", "RequiresAdmin"),
        ("SpecialPaths", "Plugins"),
    ];

    private static readonly (string Section, string Key)[] EditableLauncherKeys =
    [
        ("Launch", "ProgramExecutable"), ("Launch", "ProgramExecutable64"),
        ("Launch", "ProgramExecutableWhenParameters"), ("Launch", "ProgramExecutableWhenParameters64"),
        ("Launch", "CommandLineArguments"), ("Launch", "WorkingDirectory"),
        ("Launch", "MinOS"), ("Launch", "MaxOS"), ("Launch", "CloseEXE"),
        ("Launch", "SplashTime"), ("Launch", "WaitForProgram"),
        ("Launch", "WaitForOtherInstances"), ("Launch", "SinglePortableAppInstance"),
        ("Launch", "SingleAppInstance"), ("Launch", "CleanTemp"),
        ("Launch", "LaunchAppAfterSplash"), ("Launch", "RefreshShellIcons"),
        ("Launch", "HideCommandLineWindow"), ("Launch", "DirectoryMoveOK"),
        ("Launch", "NoSpacesInPath"), ("Launch", "SupportsUNC"),
        ("Activate", "Registry"), ("Activate", "Java"), ("Activate", "XML"),
        ("LiveMode", "CopyApp"),
        ("Language", "Base"), ("Language", "Default"), ("Language", "CheckIfExists"),
        ("Language", "DefaultIfNotExists"),
    ];

    private static readonly (string Section, string Key)[] EditableInstallerKeys =
    [
        ("Source", "IncludeInstallerSource"),
        ("CheckRunning", "CloseEXE"), ("CheckRunning", "CloseName"),
        ("MainDirectories", "RemoveAppDirectory"), ("MainDirectories", "RemoveOtherDirectory"),
        ("OptionalComponents", "OptionalComponents"), ("OptionalComponents", "MainSectionTitle"),
        ("OptionalComponents", "MainSectionDescription"), ("OptionalComponents", "OptionalSectionTitle"),
        ("OptionalComponents", "OptionalSectionDescription"),
        ("OptionalComponents", "OptionalSectionSelectedInstallType"),
        ("OptionalComponents", "OptionalSectionNotSelectedInstallType"),
        ("OptionalComponents", "OptionalSectionPreSelectedIfNonEnglishInstall"),
        ("OptionalComponents", "OptionalSectionPreSelectedIfWindows32Bit"),
        ("OptionalComponents", "OptionalSectionPreSelectedIfLessThanWindows"),
        ("OptionalComponents", "OptionalSectionInstalledWhenSilent"),
        ("OptionalComponents", "OptionalSectionInstalledWhenSilentOn32Bit"),
        ("Languages", "ENGLISH"),
    ];

    public static void Run(AppWrapInput input, Action<string, bool> check)
    {
        // --- Emitted appinfo surface (PortableAppsGenerator) ------------------
        var appInfo = IniDocument.Parse(PortableAppsGenerator.Generate(input));
        foreach (var (section, key) in EmittedAppInfoKeys)
            check($"compliance emitted appinfo [{section}] {key}",
                appInfo.GetValue(section, key) is { Length: > 0 });
        check("compliance appinfo format type pinned",
            appInfo.GetValue("Format", "Type") == "PortableApps.comFormat");
        check("compliance appinfo format version is targeted spec",
            appInfo.GetValue("Format", "Version") == PafLayout.DefaultPafSpecVersion);
        check("compliance appinfo start names the root launcher",
            appInfo.GetValue("Control", "Start") == PortableAppsGenerator.PackageId(input) + ".exe");

        // --- Editable surface (ProjectConfigWorkspace catalogue) --------------
        static HashSet<string> Exposed(ProjectDocumentKind kind) =>
            ProjectConfigWorkspace.FieldsFor(kind).Select(f => f.Section + "/" + f.Key).ToHashSet(StringComparer.Ordinal);
        var appInfoFields = Exposed(ProjectDocumentKind.AppInfo);
        foreach (var (section, key) in EditableAppInfoKeys)
            check($"compliance editable appinfo [{section}] {key}", appInfoFields.Contains(section + "/" + key));
        var launcherFields = Exposed(ProjectDocumentKind.Launcher);
        foreach (var (section, key) in EditableLauncherKeys)
            check($"compliance editable launcher [{section}] {key}", launcherFields.Contains(section + "/" + key));
        var installerFields = Exposed(ProjectDocumentKind.Installer);
        foreach (var (section, key) in EditableInstallerKeys)
            check($"compliance editable installer [{section}] {key}", installerFields.Contains(section + "/" + key));

        // Every emitted key is also editable, so a generated document can be
        // opened in the project editors without losing generator surface.
        foreach (var (section, key) in EmittedAppInfoKeys)
            check($"compliance emitted key editable [{section}] {key}", appInfoFields.Contains(section + "/" + key));

        // --- Launcher catalogue vs the pinned PAL capability catalog ----------
        foreach (var (section, key) in EditableLauncherKeys)
            check($"compliance launcher key classified [{section}] {key}",
                CapabilityCatalog.Classify(PortableDocumentKind.Launcher, section, key) != CapabilityStatus.Unrecognized);
        check("compliance LiveMode CopyData stays unsupported",
            CapabilityCatalog.Classify(PortableDocumentKind.Launcher, "LiveMode", "CopyData") == CapabilityStatus.Unsupported);
    }
}
