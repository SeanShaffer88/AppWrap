using System.IO;
using AppWrap.Core;
using AppWrap.Core.PortableProfiles;
using AppWrap.Core.Projects;

internal static class ScaffoldChecks
{
    public static void Run(string evidence, Action<string, bool> check, Action<string, Action> reject)
    {
        var root = Path.Combine(evidence, "scaffolds");
        Directory.CreateDirectory(root);

        // --- Blank scaffold: tree shape and project file contents. ---
        var blankDir = Path.Combine(root, "blank");
        var blank = ProjectScaffolder.CreateBlank(blankDir, "Meadow");
        check("scaffold derives a conventional AppID", blank.AppId == "MeadowPortable");
        check("scaffold creates the canonical layout", Directory.Exists(Path.Combine(blankDir, "App", "AppInfo", "Launcher"))
            && Directory.Exists(Path.Combine(blankDir, "Data")) && Directory.Exists(Path.Combine(blankDir, "Other")));
        check("scaffold writes the three INI documents", File.Exists(Path.Combine(blankDir, "App", "AppInfo", "appinfo.ini"))
            && File.Exists(Path.Combine(blankDir, "App", "AppInfo", "installer.ini"))
            && File.Exists(Path.Combine(blankDir, "App", "AppInfo", "Launcher", "MeadowPortable.ini")));
        check("scaffold writes the project file", File.Exists(Path.Combine(blankDir, "appwrap.project.json")));
        check("scaffold leaves no staging folder behind", !Directory.EnumerateDirectories(root).Any(d => Path.GetFileName(d).Contains(".appwrap-scaffold-", StringComparison.Ordinal)));
        var blankProject = ProjectStore.Load(blankDir);
        check("scaffold project records name, spec and template", blankProject is not null && blankProject.Name == "Meadow"
            && blankProject.PafSpecVersion == PafLayout.DefaultPafSpecVersion && blankProject.SchemaVersion == PafProject.CurrentSchemaVersion
            && blankProject.UserChoices.TryGetValue(ProjectScaffolder.TemplateChoiceKey, out var blankTemplate) && blankTemplate == ProjectScaffolder.BlankTemplateName
            && blankProject.SourceSnapshotHash is not null);
        check("scaffold project records Advanced mode off", blankProject is not null
            && blankProject.UserChoices.TryGetValue(ProjectConfigWorkspace.AdvancedModeChoiceKey, out var blankAdvanced) && blankAdvanced == "false");

        // --- Fresh blank scaffold: zero errors, TODO warnings name the fields. ---
        check("scaffold validates with zero errors", blank.Diagnostics.All(d => d.Severity != PafDiagnosticSeverity.Error));
        PafDiagnostic? Todo(string file, string? key) => blank.Diagnostics.FirstOrDefault(d =>
            d.Code == "TodoPlaceholder" && d.Severity == PafDiagnosticSeverity.Warning && d.FilePath == file && d.Key == key);
        check("scaffold TODO warning on Description", Todo("App/AppInfo/appinfo.ini", "Description") is { Section: "Details" });
        check("scaffold TODO warning on DisplayVersion", Todo("App/AppInfo/appinfo.ini", "DisplayVersion") is { Section: "Version" });
        check("scaffold TODO warning on ProgramExecutable", Todo("App/AppInfo/Launcher/MeadowPortable.ini", "ProgramExecutable") is { Section: "Launch" });
        var revalidated = PafValidator.Validate(blankDir, blankProject);
        check("scaffold revalidation also has zero errors", revalidated.All(d => d.Severity != PafDiagnosticSeverity.Error)
            && revalidated.Any(d => d.Code == "TodoPlaceholder"));

        // --- Scaffolded INIs round-trip through IniDocument unchanged. ---
        foreach (var relative in new[] { "App/AppInfo/appinfo.ini", "App/AppInfo/installer.ini", "App/AppInfo/Launcher/MeadowPortable.ini" })
        {
            var text = File.ReadAllText(Path.Combine(blankDir, relative.Replace('/', Path.DirectorySeparatorChar)));
            check($"scaffold INI round-trips losslessly: {relative}", IniDocument.Parse(text).Serialize() == text && IniDocument.Parse(text).ParseIssues.Count == 0);
        }

        // --- The Phase 2 editors open a scaffolded project cleanly. ---
        var workspace = ProjectConfigWorkspace.Load(blankDir);
        check("scaffold opens in the configuration workspace", workspace.AppId == "MeadowPortable" && !workspace.AdvancedMode && !workspace.ProjectWasInferred);
        var workspaceDiagnostics = workspace.ValidateAll();
        check("workspace validation of scaffold has no errors", workspaceDiagnostics.All(d => d.Severity != PafDiagnosticSeverity.Error));
        check("workspace attaches TODO diagnostics to the appinfo document", workspace.DiagnosticsFor(ProjectDocumentKind.AppInfo).Any(d => d.Code == "TodoPlaceholder"));

        // --- Advanced off (the default) scaffolds no custom code. ---
        check("scaffold without Advanced mode writes no custom code", !File.Exists(Path.Combine(blankDir, "App", "AppInfo", "Launcher", "Custom.nsh"))
            && !File.Exists(Path.Combine(blankDir, "Other", "Source", "PortableApps.comInstallerCustom.nsh")));

        // --- Advanced on scaffolds both documented starters, still error-free. ---
        var advancedDir = Path.Combine(root, "advanced");
        var advanced = ProjectScaffolder.CreateBlank(advancedDir, "Fern", advancedMode: true);
        var launcherStarter = Path.Combine(advancedDir, "App", "AppInfo", "Launcher", "Custom.nsh");
        var installerStarter = Path.Combine(advancedDir, "Other", "Source", "PortableApps.comInstallerCustom.nsh");
        check("scaffold with Advanced mode writes both starters", File.Exists(launcherStarter) && File.Exists(installerStarter));
        check("launcher starter documents how it is consumed", File.ReadAllText(launcherStarter).Contains("includes this", StringComparison.Ordinal)
            && File.ReadAllText(launcherStarter).Contains("comment-only (inert)", StringComparison.Ordinal));
        check("installer starter documents the AppWrap rejection honestly", File.ReadAllText(installerStarter).Contains("does NOT consume", StringComparison.Ordinal)
            && File.ReadAllText(installerStarter).Contains("comment-only (inert)", StringComparison.Ordinal));
        check("scaffold with Advanced mode records the flag", advanced.Project.UserChoices.TryGetValue(ProjectConfigWorkspace.AdvancedModeChoiceKey, out var adv) && adv == "true"
            && ProjectConfigWorkspace.Load(advancedDir).AdvancedMode);
        check("scaffold with starters still validates with zero errors", advanced.Diagnostics.All(d => d.Severity != PafDiagnosticSeverity.Error));

        // --- Spec version is selectable and recorded as chosen. ---
        var specDir = Path.Combine(root, "spec");
        var spec = ProjectScaffolder.CreateBlank(specDir, "Ivy", pafSpecVersion: "3.5");
        var specAppInfo = IniDocument.Parse(File.ReadAllText(Path.Combine(specDir, "App", "AppInfo", "appinfo.ini")));
        check("scaffold records the chosen spec version", spec.Project.PafSpecVersion == "3.5" && specAppInfo.GetValue("Format", "Version") == "3.5");
        check("scaffold with a non-target spec still has zero errors", spec.Diagnostics.All(d => d.Severity != PafDiagnosticSeverity.Error));

        // --- Destination discipline: non-empty refused, empty accepted. ---
        var occupiedDir = Path.Combine(root, "occupied"); Directory.CreateDirectory(occupiedDir);
        File.WriteAllText(Path.Combine(occupiedDir, "keep.txt"), "not yours");
        reject("scaffold refuses a non-empty destination", () => ProjectScaffolder.CreateBlank(occupiedDir, "Meadow"));
        check("refused scaffold leaves the folder untouched", File.ReadAllText(Path.Combine(occupiedDir, "keep.txt")) == "not yours"
            && !File.Exists(Path.Combine(occupiedDir, "appwrap.project.json")));
        var fileDestination = Path.Combine(root, "afile"); File.WriteAllText(fileDestination, "file");
        reject("scaffold refuses a file as destination", () => ProjectScaffolder.CreateBlank(fileDestination, "Meadow"));
        var emptyDir = Path.Combine(root, "empty"); Directory.CreateDirectory(emptyDir);
        var intoEmpty = ProjectScaffolder.CreateBlank(emptyDir, "Meadow");
        check("scaffold accepts an existing empty folder", intoEmpty.Project.Name == "Meadow" && File.Exists(Path.Combine(emptyDir, "appwrap.project.json")));
        reject("scaffold refuses an empty app name", () => ProjectScaffolder.CreateBlank(Path.Combine(root, "noname"), "  "));

        // --- Wrapped binary: the inference path records the template choice. ---
        var wrapDir = Path.Combine(root, "wrapped"); Directory.CreateDirectory(wrapDir);
        File.WriteAllText(Path.Combine(wrapDir, "Wrapped.exe"), "fixture");
        var wrapped = ProjectStore.LoadOrInfer(wrapDir, out var wrapInferred, ProjectScaffolder.WrappedBinaryTemplateName);
        check("wrapped-binary inference records the template", wrapInferred
            && wrapped.UserChoices.TryGetValue(ProjectScaffolder.TemplateChoiceKey, out var wrapTemplate) && wrapTemplate == ProjectScaffolder.WrappedBinaryTemplateName);
        ProjectStore.Save(wrapDir, wrapped);
        var wrappedReloaded = ProjectStore.LoadOrInfer(wrapDir, out var wrapReinferred, ProjectScaffolder.BlankTemplateName);
        check("recorded template survives reload and is not overwritten", !wrapReinferred
            && wrappedReloaded.UserChoices.TryGetValue(ProjectScaffolder.TemplateChoiceKey, out var kept) && kept == ProjectScaffolder.WrappedBinaryTemplateName);
        var plainDir = Path.Combine(root, "plain"); Directory.CreateDirectory(plainDir);
        var plain = ProjectStore.LoadOrInfer(plainDir, out _);
        check("plain inference records no template", !plain.UserChoices.ContainsKey(ProjectScaffolder.TemplateChoiceKey));
    }
}
