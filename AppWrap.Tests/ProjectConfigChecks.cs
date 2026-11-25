using System.IO;
using AppWrap.Core.Projects;

/// <summary>
/// Phase 2 checks: the project configuration workspace behind the wizard's
/// configuration panel — lossless INI round-trips through the editors, field
/// edits that preserve unknown content, validator diagnostics attached to the
/// right fields, the [LiveMode] surface of the pinned PAL, Advanced mode
/// gating of custom-code writes (typed refusal when off, persistence per
/// project, off by default), and save behaviour that creates nothing the user
/// did not edit. Pure configuration logic over fixture folders.
/// </summary>
internal static class ProjectConfigChecks
{
    private const string AppInfoText =
        "; Meadow package details\r\n" +
        "[Format]\r\nType=PortableApps.comFormat\r\nVersion=3.9\r\n" +
        "[Details]\r\nName=Meadow Portable\r\nAppID=MeadowPortable\r\nPublisher=Meadow Team\r\nCategory=Utilities\r\n" +
        "Description=A meadow app\r\nHomepage=https://example.invalid/\r\nLanguage=English\r\nFutureDetailKey=keep me\r\n" +
        "[UnknownSection]\r\nWhatever=1\r\n" +
        "[License]\r\nShareable=false\r\n" +
        "[Version]\r\nPackageVersion=1.2.0.0\r\nDisplayVersion=1.2\r\n" +
        "[Control]\r\nIcons=1\r\nStart=MeadowPortable.exe\r\n";

    private const string LauncherText =
        "; launcher notes\n" +
        "[Launch]\nProgramExecutable=Meadow.exe\nWaitForProgram=true\n" +
        "[Future]\nX=1\n";

    private const string InstallerText =
        "[Source]\nIncludeInstallerSource=false\n" +
        "[DirectoriesToPreserve]\nPreserveDirectory1=App\\Docs\n; a comment to keep\n";

    public static void Run(string evidence, Action<string, bool> check, Action<string, Action> reject)
    {
        var root = Path.Combine(evidence, "config");
        Directory.CreateDirectory(root);

        // --- Fixture project with comments, unknown content and mixed ordering. ---
        var dir = Path.Combine(root, "editor");
        Directory.CreateDirectory(Path.Combine(dir, "App", "AppInfo", "Launcher"));
        File.WriteAllText(Path.Combine(dir, "App", "AppInfo", "appinfo.ini"), AppInfoText);
        File.WriteAllText(Path.Combine(dir, "App", "AppInfo", "Launcher", "MeadowPortable.ini"), LauncherText);
        File.WriteAllText(Path.Combine(dir, "App", "AppInfo", "installer.ini"), InstallerText);

        var workspace = ProjectConfigWorkspace.Load(dir);
        check("config workspace resolves AppID and launcher path", workspace.AppId == "MeadowPortable"
            && workspace.DisplayPathFor(ProjectDocumentKind.Launcher) == "App/AppInfo/Launcher/MeadowPortable.ini");
        check("config round-trip appinfo byte-identical", workspace.GetDocumentText(ProjectDocumentKind.AppInfo) == AppInfoText);
        check("config round-trip launcher byte-identical", workspace.GetDocumentText(ProjectDocumentKind.Launcher) == LauncherText);
        check("config round-trip installer byte-identical", workspace.GetDocumentText(ProjectDocumentKind.Installer) == InstallerText);
        check("config untouched workspace is clean", !workspace.HasUnsavedChanges);
        workspace.Save();
        check("config unedited save leaves files byte-identical",
            File.ReadAllText(Path.Combine(dir, "App", "AppInfo", "appinfo.ini")) == AppInfoText
            && File.ReadAllText(Path.Combine(dir, "App", "AppInfo", "Launcher", "MeadowPortable.ini")) == LauncherText
            && File.ReadAllText(Path.Combine(dir, "App", "AppInfo", "installer.ini")) == InstallerText);

        // --- Field edits go through the documents; unknown content survives. ---
        workspace.SetField(ProjectDocumentKind.AppInfo, "Details", "Publisher", "New Publisher");
        var edited = workspace.GetDocumentText(ProjectDocumentKind.AppInfo);
        check("config field edit lands in document", edited.Contains("Publisher=New Publisher", StringComparison.Ordinal));
        check("config field edit preserves unknown content", edited.Contains("[UnknownSection]", StringComparison.Ordinal)
            && edited.Contains("FutureDetailKey=keep me", StringComparison.Ordinal)
            && edited.Contains("; Meadow package details", StringComparison.Ordinal));
        check("config edit marks only that document dirty", workspace.IsDirty(ProjectDocumentKind.AppInfo)
            && !workspace.IsDirty(ProjectDocumentKind.Launcher) && !workspace.IsDirty(ProjectDocumentKind.Installer));
        var openSource = ProjectConfigWorkspace.Catalog.Single(f =>
            f.Document == ProjectDocumentKind.AppInfo && f.Section == "License" && f.Key == "OpenSource");
        workspace.SetField(openSource, "true");
        check("config boolean field edit appends to its section", workspace.GetField(openSource) == "true"
            && workspace.GetDocumentText(ProjectDocumentKind.AppInfo).Contains("OpenSource=true", StringComparison.Ordinal));
        workspace.SetField(ProjectDocumentKind.AppInfo, "Details", "Homepage", null);
        check("config null field edit removes the key", workspace.GetField(ProjectDocumentKind.AppInfo, "Details", "Homepage") is null
            && !workspace.GetDocumentText(ProjectDocumentKind.AppInfo).Contains("Homepage=", StringComparison.Ordinal));
        workspace.Save();
        var reloaded = ProjectConfigWorkspace.Load(dir);
        check("config edits persist to disk", reloaded.GetField(ProjectDocumentKind.AppInfo, "Details", "Publisher") == "New Publisher"
            && reloaded.GetField(openSource) == "true");
        check("config save refreshes the project snapshot", reloaded.Project.SourceSnapshotHash is not null
            && reloaded.Project.SourceSnapshotHash == SourceSnapshot.Compute(dir).Sha256);

        // --- Text edits bind to the same documents. ---
        reloaded.SetDocumentText(ProjectDocumentKind.Installer,
            InstallerText + "\n[Languages]\nENGLISH=true\n");
        check("config text edit reflects in fields", reloaded.GetField(ProjectDocumentKind.Installer, "Languages", "ENGLISH") == "true");
        reloaded.Save();
        check("config text edit persists", File.ReadAllText(Path.Combine(dir, "App", "AppInfo", "installer.ini"))
            .Contains("[Languages]", StringComparison.Ordinal));

        // --- [LiveMode]: supported by the pinned PAL, CopyApp only. ---
        var copyApp = ProjectConfigWorkspace.Catalog.Single(f =>
            f.Document == ProjectDocumentKind.Launcher && f.Section == "LiveMode" && f.Key == "CopyApp");
        check("config LiveMode CopyApp is a boolean field", copyApp.Kind == ConfigFieldKind.Boolean);
        check("config catalog omits removed LiveMode CopyData", !ProjectConfigWorkspace.Catalog.Any(f =>
            f.Section == "LiveMode" && f.Key == "CopyData"));
        reloaded.SetField(copyApp, "true");
        check("config LiveMode edit lands in launcher document", reloaded.GetDocumentText(ProjectDocumentKind.Launcher)
            .Contains("[LiveMode]", StringComparison.Ordinal) && reloaded.GetField(copyApp) == "true");

        // --- Required-field markers match the validator's error rules. ---
        check("config required fields marked", ProjectConfigWorkspace.FieldsFor(ProjectDocumentKind.AppInfo)
            .Where(f => f.Required).Select(f => f.Key).OrderBy(k => k).SequenceEqual(["AppID", "Name", "PackageVersion", "Start"])
            && ProjectConfigWorkspace.FieldsFor(ProjectDocumentKind.Launcher).Single(f => f.Key == "ProgramExecutable").Required);

        // --- Diagnostics attach to the right fields of a broken fixture. ---
        var brokenDir = Path.Combine(root, "broken");
        Directory.CreateDirectory(Path.Combine(brokenDir, "App", "AppInfo", "Launcher"));
        File.WriteAllText(Path.Combine(brokenDir, "App", "AppInfo", "appinfo.ini"),
            "[Format]\nType=PortableApps.comFormat\nVersion=3.9\n" +
            "[Details]\nName=Broken App\nPublisher=Someone\nCategory=Utilities\nDescription=Broken fixture\n" +
            "[Version]\nPackageVersion=1.x\nDisplayVersion=1.x\n" +
            "[Control]\nIcons=1\n");
        File.WriteAllText(Path.Combine(brokenDir, "App", "AppInfo", "Launcher", "Broken.ini"),
            "[Launch]\nWaitForProgram=true\n");
        var broken = ProjectConfigWorkspace.Load(brokenDir);
        var all = broken.ValidateAll();
        check("config validate-all surfaces validator errors", all.Any(d => d.Code == "MissingAppId" && d.Severity == PafDiagnosticSeverity.Error)
            && all.Any(d => d.Code == "InvalidPackageVersion") && all.Any(d => d.Code == "MissingStart")
            && all.Any(d => d.Code == "MissingProgramExecutable"));
        var appInfoStates = broken.FieldStates(ProjectDocumentKind.AppInfo);
        ConfigFieldState State(string key) => appInfoStates.Single(s => s.Field.Key == key);
        check("config diagnostic attaches to AppID field", State("AppID").Diagnostics.Any(d => d.Code == "MissingAppId"));
        check("config diagnostic attaches to PackageVersion field", State("PackageVersion").Diagnostics.Any(d => d.Code == "InvalidPackageVersion"));
        check("config diagnostic attaches to Start field", State("Start").Diagnostics.Any(d => d.Code == "MissingStart"));
        check("config clean field carries no diagnostics", State("Name").Diagnostics.Count == 0);
        var launcherDiagnostics = broken.DiagnosticsFor(ProjectDocumentKind.Launcher);
        check("config launcher diagnostics scoped to launcher file", launcherDiagnostics.Count > 0
            && launcherDiagnostics.All(d => d.FilePath == "App/AppInfo/Launcher/Broken.ini")
            && launcherDiagnostics.Any(d => d.Code == "MissingProgramExecutable"));

        // --- Advanced mode: off by default, typed refusal, persistence. ---
        var advanced = ProjectConfigWorkspace.Load(dir);
        check("config advanced mode defaults off", !advanced.AdvancedMode);
        var refused = advanced.WriteCustomCode(CustomCodeKind.Launcher, "!macro CustomCode\n!macroend\n");
        check("config custom code write refused while advanced off", !refused.Written
            && refused.Status == CustomCodeWriteStatus.RefusedAdvancedModeOff
            && !File.Exists(Path.Combine(dir, "App", "AppInfo", "Launcher", "Custom.nsh")));
        check("config custom code read reports the gate", !advanced.ReadCustomCode(CustomCodeKind.Launcher).Editable
            && !advanced.ReadCustomCode(CustomCodeKind.Launcher).Exists);
        advanced.AdvancedMode = true;
        advanced.Save();
        var persistedProject = ProjectStore.Load(dir);
        check("config advanced mode persists in project choices", persistedProject is not null
            && persistedProject.UserChoices.TryGetValue(ProjectConfigWorkspace.AdvancedModeChoiceKey, out var flag) && flag == "true");
        var advancedReloaded = ProjectConfigWorkspace.Load(dir);
        check("config advanced mode restored on load", advancedReloaded.AdvancedMode);
        var written = advancedReloaded.WriteCustomCode(CustomCodeKind.Launcher, "!macro CustomCode\n!macroend\n");
        check("config custom code write succeeds while advanced on", written.Written
            && File.ReadAllText(Path.Combine(dir, "App", "AppInfo", "Launcher", "Custom.nsh")) == "!macro CustomCode\n!macroend\n");
        var installerWritten = advancedReloaded.WriteCustomCode(CustomCodeKind.Installer, "!macro CustomCodePreInstall\n!macroend\n");
        check("config installer custom code writes to Other/Source", installerWritten.Written
            && File.Exists(Path.Combine(dir, "Other", "Source", "PortableApps.comInstallerCustom.nsh")));
        var readBack = advancedReloaded.ReadCustomCode(CustomCodeKind.Launcher);
        check("config custom code read back editable", readBack.Exists && readBack.Editable && readBack.Text.Contains("!macro CustomCode", StringComparison.Ordinal));

        // --- Inferred projects: advanced off, and an untouched save creates no INI files. ---
        var bareDir = Path.Combine(root, "bare");
        Directory.CreateDirectory(bareDir);
        File.WriteAllText(Path.Combine(bareDir, "Bare.exe"), "fixture");
        var bare = ProjectConfigWorkspace.Load(bareDir);
        check("config inferred project advanced off", bare.ProjectWasInferred && !bare.AdvancedMode);
        bare.Save();
        check("config untouched save creates no configuration files",
            !File.Exists(Path.Combine(bareDir, "App", "AppInfo", "appinfo.ini"))
            && !File.Exists(Path.Combine(bareDir, "App", "AppInfo", "installer.ini"))
            && !Directory.Exists(Path.Combine(bareDir, "App", "AppInfo", "Launcher")));
        bare.SetField(ProjectDocumentKind.AppInfo, "Details", "Name", "Bare Portable");
        bare.Save();
        check("config first edit creates only the edited file",
            File.Exists(Path.Combine(bareDir, "App", "AppInfo", "appinfo.ini"))
            && !File.Exists(Path.Combine(bareDir, "App", "AppInfo", "installer.ini")));

        // --- Project images: first-class properties on the same workspace. ---
        var imageDir = Path.Combine(root, "images");
        Directory.CreateDirectory(Path.Combine(imageDir, "App", "AppInfo"));
        Directory.CreateDirectory(Path.Combine(imageDir, "pics"));
        File.WriteAllText(Path.Combine(imageDir, "App", "AppInfo", "appicon.ico"), "fixture icon bytes");
        File.WriteAllText(Path.Combine(imageDir, "pics", "side.bmp"), "fixture bmp bytes");
        var images = ProjectConfigWorkspace.Load(imageDir);
        check("config workspace infers conventional icon", images.IconPath == ProjectStore.ConventionalIconPath
            && images.SidebarImagePath is null && !images.ImagesDirty);
        images.SidebarImagePath = Path.Combine(imageDir, "pics", "side.bmp");
        check("config sidebar set stores project-relative", images.SidebarImagePath == "pics/side.bmp" && images.ImagesDirty
            && images.HasUnsavedChanges);
        images.Save();
        check("config image properties persist in project file", ProjectStore.Load(imageDir) is { } savedImages
            && savedImages.IconPath == ProjectStore.ConventionalIconPath && savedImages.SidebarImagePath == "pics/side.bmp");
        var imagesReloaded = ProjectConfigWorkspace.Load(imageDir);
        var states = imagesReloaded.ImageFieldStates();
        check("config image field states resolve and exist", states.Count == 2
            && states[0].Key == ProjectConfigWorkspace.IconPathKey && states[0].Exists && states[0].Diagnostics.Count == 0
            && states[1].Key == ProjectConfigWorkspace.SidebarImagePathKey && states[1].Exists
            && states[1].ResolvedPath == Path.GetFullPath(Path.Combine(imageDir, "pics", "side.bmp")));
        imagesReloaded.IconPath = "gone.png";
        var brokenStates = imagesReloaded.ImageFieldStates();
        check("config missing image diagnostic attaches to its field", brokenStates[0].Diagnostics.Any(d =>
            d.Code == "ProjectImageMissing" && d.Severity == PafDiagnosticSeverity.Error)
            && brokenStates[1].Diagnostics.Count == 0);
        imagesReloaded.IconPath = null;
        check("config image property clears", imagesReloaded.IconPath is null);
    }
}
