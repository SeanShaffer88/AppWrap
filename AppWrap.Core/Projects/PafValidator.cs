using AppWrap.Core.PortableProfiles;

namespace AppWrap.Core.Projects;

public enum PafDiagnosticSeverity { Info, Warning, Error }

/// <summary>
/// One structured, field-level PAF diagnostic. <see cref="FilePath"/> is relative to
/// the project root in forward-slash form; <see cref="Section"/> and <see cref="Key"/>
/// locate the INI field when the diagnostic is about one; <see cref="Hint"/> always
/// carries a concrete remediation step.
/// </summary>
public sealed record PafDiagnostic(
    PafDiagnosticSeverity Severity, string Code, string FilePath, string? Section, string? Key, string Message, string Hint);

/// <summary>
/// Validates a project folder against the PAF specification version the pinned
/// toolchain implements (see <see cref="PafLayout.DefaultPafSpecVersion"/>). Files that
/// exist are validated field-by-field; canonical files that are absent are reported
/// at Info/Warning level because AppWrap generates them during packaging — absence is
/// normal for a plain application folder, broken content is not. Diagnostics are
/// deterministic in order: layout, appinfo.ini, installer.ini, launcher, custom code.
/// </summary>
public static class PafValidator
{
    private static readonly string[] Categories =
        ["Accessibility", "Development", "Education", "Games", "Graphics & Pictures", "Internet", "Music & Video", "Office", "Security", "Utilities"];

    /// <param name="inMemoryDocuments">
    /// Optional overlay of document texts (project-relative display path → text)
    /// that take precedence over the files on disk. The Phase 2 configuration
    /// editors pass their unsaved documents here so inline validation applies
    /// exactly these rules to exactly what will be saved; no rule changes.
    /// </param>
    public static IReadOnlyList<PafDiagnostic> Validate(string root, PafProject? project = null, IReadOnlyDictionary<string, string>? inMemoryDocuments = null)
    {
        var fullRoot = Path.GetFullPath(root);
        InputRules.SafePath(fullRoot);
        if (!Directory.Exists(fullRoot)) throw new UserInputException("Choose an application folder that still exists.");
        var diagnostics = new List<PafDiagnostic>();

        void Add(PafDiagnosticSeverity severity, string code, string file, string? section, string? key, string message, string hint) =>
            diagnostics.Add(new PafDiagnostic(severity, code, file, section, key, message, hint));

        // --- Canonical layout -------------------------------------------------
        if (!Directory.Exists(Path.Combine(fullRoot, "App")))
            Add(PafDiagnosticSeverity.Warning, "MissingAppDirectory", PafLayout.AppDirectory, null, null,
                "The project has no App folder.", "Add an App folder, or let AppWrap generate the package layout when packaging.");
        if (!Directory.Exists(Path.Combine(fullRoot, "Data")))
            Add(PafDiagnosticSeverity.Info, "MissingDataDirectory", PafLayout.DataDirectory, null, null,
                "The project has no Data folder.", "Portable apps create Data on first run; add the folder only if the app ships default data.");
        if (!Directory.Exists(Path.Combine(fullRoot, "Other")))
            Add(PafDiagnosticSeverity.Info, "MissingOtherDirectory", PafLayout.OtherDirectory, null, null,
                "The project has no Other folder.", "Other is optional; it holds source and license material when present.");

        // --- appinfo.ini ------------------------------------------------------
        string? appId = null;
        var appInfo = ReadIni(fullRoot, PafLayout.AppInfoPath, diagnostics, inMemoryDocuments);
        if (appInfo is null && !File.Exists(Path.Combine(fullRoot, "App", "AppInfo", "appinfo.ini"))
            && !(inMemoryDocuments?.ContainsKey(PafLayout.AppInfoPath) ?? false))
            Add(PafDiagnosticSeverity.Warning, "MissingAppInfo", PafLayout.AppInfoPath, null, null,
                "The project has no appinfo.ini.", "AppWrap generates appinfo.ini from the wizard answers when packaging; add one to control the package metadata directly.");
        if (appInfo is not null)
        {
            var formatType = appInfo.GetValue("Format", "Type");
            if (formatType is null)
                Add(PafDiagnosticSeverity.Warning, "MissingFormatType", PafLayout.AppInfoPath, "Format", "Type",
                    "appinfo.ini does not declare its format type.", "Set [Format] Type=PortableApps.comFormat.");
            else if (!string.Equals(formatType, "PortableApps.comFormat", StringComparison.Ordinal))
                Add(PafDiagnosticSeverity.Error, "WrongFormatType", PafLayout.AppInfoPath, "Format", "Type",
                    $"appinfo.ini declares format type '{formatType}', which the PortableApps.com toolchain does not read.", "Set [Format] Type=PortableApps.comFormat.");
            var formatVersion = appInfo.GetValue("Format", "Version");
            if (formatVersion is not null && project is not null && !string.Equals(formatVersion, project.PafSpecVersion, StringComparison.Ordinal))
                Add(PafDiagnosticSeverity.Info, "FormatVersionDiffersFromProject", PafLayout.AppInfoPath, "Format", "Version",
                    $"appinfo.ini targets PAF {formatVersion} but the project targets PAF {project.PafSpecVersion}.", "Align [Format] Version with the project's targeted PAF version, or update the project.");

            Require(appInfo, "Details", "Name", "MissingName", "appinfo.ini does not name the application.", "Set [Details] Name to the application's display name.");
            appId = appInfo.GetValue("Details", "AppID");
            if (string.IsNullOrWhiteSpace(appId))
                Add(PafDiagnosticSeverity.Error, "MissingAppId", PafLayout.AppInfoPath, "Details", "AppID",
                    "appinfo.ini does not declare an AppID.", "Set [Details] AppID to the package identity (letters and digits, ending in 'Portable' by convention).");
            else if (appId.Any(c => !char.IsAsciiLetterOrDigit(c)))
                Add(PafDiagnosticSeverity.Warning, "UnusualAppId", PafLayout.AppInfoPath, "Details", "AppID",
                    $"AppID '{appId}' contains characters other than letters and digits.", "Use letters and digits only; the launcher and installer derive file names from the AppID.");
            if (string.IsNullOrWhiteSpace(appInfo.GetValue("Details", "Publisher")))
                Add(PafDiagnosticSeverity.Warning, "MissingPublisher", PafLayout.AppInfoPath, "Details", "Publisher",
                    "appinfo.ini does not name a publisher.", "Set [Details] Publisher to the person or organisation publishing the package.");
            var category = appInfo.GetValue("Details", "Category");
            if (string.IsNullOrWhiteSpace(category))
                Add(PafDiagnosticSeverity.Warning, "MissingCategory", PafLayout.AppInfoPath, "Details", "Category",
                    "appinfo.ini does not declare a category.", "Set [Details] Category to one of: " + string.Join(", ", Categories) + ".");
            else if (!Categories.Contains(category))
                Add(PafDiagnosticSeverity.Warning, "UnknownCategory", PafLayout.AppInfoPath, "Details", "Category",
                    $"Category '{category}' is not a PortableApps.com category.", "Use one of: " + string.Join(", ", Categories) + ".");
            if (string.IsNullOrWhiteSpace(appInfo.GetValue("Details", "Description")))
                Add(PafDiagnosticSeverity.Warning, "MissingDescription", PafLayout.AppInfoPath, "Details", "Description",
                    "appinfo.ini does not describe the application.", "Set [Details] Description to a one-line summary of what the app does.");
            if (string.IsNullOrWhiteSpace(appInfo.GetValue("Details", "Homepage")))
                Add(PafDiagnosticSeverity.Info, "MissingHomepage", PafLayout.AppInfoPath, "Details", "Homepage",
                    "appinfo.ini does not give a homepage.", "Set [Details] Homepage to the application's website, if it has one.");

            var packageVersion = appInfo.GetValue("Version", "PackageVersion");
            if (string.IsNullOrWhiteSpace(packageVersion))
                Add(PafDiagnosticSeverity.Error, "MissingPackageVersion", PafLayout.AppInfoPath, "Version", "PackageVersion",
                    "appinfo.ini does not declare a package version.", "Set [Version] PackageVersion to a numeric version such as 1.2.0.0.");
            else
            {
                try { InputRules.PackageVersion(packageVersion); }
                catch (UserInputException)
                {
                    Add(PafDiagnosticSeverity.Error, "InvalidPackageVersion", PafLayout.AppInfoPath, "Version", "PackageVersion",
                        $"PackageVersion '{packageVersion}' is not a numeric version.", "Use one to four numbers separated by dots, each from 0 to 65535 (for example 1.2.0.0).");
                }
            }
            if (string.IsNullOrWhiteSpace(appInfo.GetValue("Version", "DisplayVersion")))
                Add(PafDiagnosticSeverity.Warning, "MissingDisplayVersion", PafLayout.AppInfoPath, "Version", "DisplayVersion",
                    "appinfo.ini does not declare a display version.", "Set [Version] DisplayVersion to the version text shown to users.");

            var start = appInfo.GetValue("Control", "Start");
            if (string.IsNullOrWhiteSpace(start))
                Add(PafDiagnosticSeverity.Error, "MissingStart", PafLayout.AppInfoPath, "Control", "Start",
                    "appinfo.ini does not say which executable the platform launches.", "Set [Control] Start to the package's launcher executable (usually <AppID>.exe).");
            else if (!start.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                Add(PafDiagnosticSeverity.Warning, "UnusualStart", PafLayout.AppInfoPath, "Control", "Start",
                    $"[Control] Start is '{start}', which is not an .exe file.", "Point Start at the package's launcher executable.");
            if (string.IsNullOrWhiteSpace(appInfo.GetValue("Control", "Icons")))
                Add(PafDiagnosticSeverity.Info, "MissingIcons", PafLayout.AppInfoPath, "Control", "Icons",
                    "appinfo.ini does not declare how many icons the package exposes.", "Set [Control] Icons=1 for a single launcher.");

            foreach (var key in new[] { "Shareable", "OpenSource", "Freeware", "CommercialUse" })
            {
                var value = appInfo.GetValue("License", key);
                if (value is not null && value is not ("true" or "false"))
                    Add(PafDiagnosticSeverity.Warning, "InvalidLicenseValue", PafLayout.AppInfoPath, "License", key,
                        $"[License] {key} is '{value}'; the format expects true or false.", $"Set [License] {key} to true or false, or remove the key to leave it unknown.");
            }

            void Require(IniDocument doc, string section, string key, string code, string message, string hint)
            {
                if (string.IsNullOrWhiteSpace(doc.GetValue(section, key)))
                    Add(PafDiagnosticSeverity.Error, code, PafLayout.AppInfoPath, section, key, message, hint);
            }
        }

        // --- installer.ini ----------------------------------------------------
        var installer = ReadIni(fullRoot, PafLayout.InstallerIniPath, diagnostics, inMemoryDocuments);
        if (installer is not null && installer.GetValue("CheckRunning", "CloseEXE") is not null)
            Add(PafDiagnosticSeverity.Info, "GuardOwnedInstallerKey", PafLayout.InstallerIniPath, "CheckRunning", "CloseEXE",
                "installer.ini sets [CheckRunning] CloseEXE.", "AppWrap's upgrade guard owns this setting in generated packages; the generated value wins at build time.");

        // --- Launcher ---------------------------------------------------------
        var launcherDir = Path.Combine(fullRoot, "App", "AppInfo", "Launcher");
        var launcherDisplays = new List<string>();
        if (Directory.Exists(launcherDir))
            launcherDisplays.AddRange(Directory.EnumerateFiles(launcherDir, "*.ini").Take(16)
                .Select(f => PafLayout.ToDisplayPath(Path.GetRelativePath(fullRoot, f))));
        if (inMemoryDocuments is not null)
            foreach (var key in inMemoryDocuments.Keys)
                if (key.StartsWith(PafLayout.LauncherDirectory + "/", StringComparison.OrdinalIgnoreCase)
                    && key.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                    && !launcherDisplays.Contains(key, StringComparer.OrdinalIgnoreCase))
                    launcherDisplays.Add(key);
        if (!Directory.Exists(launcherDir) && launcherDisplays.Count == 0)
            Add(PafDiagnosticSeverity.Info, "NoLauncherConfig", PafLayout.LauncherDirectory, null, null,
                "The project has no launcher configuration.", "AppWrap generates launcher configuration from the portable profile when packaging; add App/AppInfo/Launcher/<AppID>.ini to control it directly.");
        else
        {
            if (launcherDisplays.Count == 0)
                Add(PafDiagnosticSeverity.Warning, "EmptyLauncherDirectory", PafLayout.LauncherDirectory, null, null,
                    "The Launcher folder contains no launcher INI.", "Add <AppID>.ini with a [Launch] section, or remove the empty folder.");
            foreach (var display in launcherDisplays)
            {
                var launcher = ReadIni(fullRoot, display, diagnostics, inMemoryDocuments);
                if (launcher is null) continue;
                if (string.IsNullOrWhiteSpace(launcher.GetValue("Launch", "ProgramExecutable")))
                    Add(PafDiagnosticSeverity.Error, "MissingProgramExecutable", display, "Launch", "ProgramExecutable",
                        "The launcher does not say which program to run.", "Set [Launch] ProgramExecutable to the application executable, relative to the package's App folder.");
                var wait = launcher.GetValue("Launch", "WaitForProgram");
                if (wait is not null && wait is not ("true" or "false"))
                    Add(PafDiagnosticSeverity.Warning, "InvalidWaitForProgram", display, "Launch", "WaitForProgram",
                        $"WaitForProgram is '{wait}'; the launcher expects true or false.", "Set [Launch] WaitForProgram to true or false.");
            }
            if (appId is not null && launcherDisplays.Count != 0 &&
                !launcherDisplays.Any(f => string.Equals(Path.GetFileName(f), appId + ".ini", StringComparison.OrdinalIgnoreCase)))
                Add(PafDiagnosticSeverity.Warning, "MissingLauncherIni", PafLayout.LauncherDirectory, null, null,
                    $"No launcher INI is named after the AppID ({appId}.ini).", $"Rename the launcher INI to {appId}.ini so the compiled launcher reads it.");
        }

        // --- Custom code ------------------------------------------------------
        if (File.Exists(Path.Combine(fullRoot, "App", "AppInfo", "Launcher", "Custom.nsh")))
            Add(PafDiagnosticSeverity.Info, "CustomLauncherCode", PafLayout.LauncherCustomPath, null, null,
                "Custom launcher code (Custom.nsh) is present in the project.", "AppWrap never compiles source-supplied launcher code into a package; carry custom behaviour through the portable profile's trusted extensions instead.");
        var sourceDir = Path.Combine(fullRoot, "Other", "Source");
        if (Directory.Exists(sourceDir))
        {
            foreach (var hook in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories).Take(64))
            {
                var ext = Path.GetExtension(hook);
                if (ext.Equals(".nsh", StringComparison.OrdinalIgnoreCase) || ext.Equals(".nsi", StringComparison.OrdinalIgnoreCase))
                    Add(PafDiagnosticSeverity.Warning, "SourceInstallerHook", PafLayout.ToDisplayPath(Path.GetRelativePath(fullRoot, hook)), null, null,
                        "A source-supplied build script is present under Other/Source.", "Remove it before packaging: AppWrap rejects payloads containing source build scripts and generates its own first-party installer hook.");
            }
        }

        // --- Project-level ----------------------------------------------------
        if (project is not null && !string.Equals(project.PafSpecVersion, PafLayout.DefaultPafSpecVersion, StringComparison.Ordinal))
            Add(PafDiagnosticSeverity.Warning, "SpecVersionMismatch", ProjectStore.FileName, null, null,
                $"The project targets PAF {project.PafSpecVersion}, but the pinned toolchain implements PAF {PafLayout.DefaultPafSpecVersion}.",
                "Align the project's targeted PAF version with the toolchain, or expect fields from the newer specification to be ignored.");

        return diagnostics;
    }

    /// <summary>
    /// Reads and parses one INI file with the build layer's own size bound (64 KB,
    /// matching PortableBuild's appinfo guard). Returns null when the file is absent,
    /// oversized, or unparsable — each case is reported as a diagnostic first. An
    /// in-memory overlay text for the path takes precedence over the file on disk.
    /// </summary>
    private static IniDocument? ReadIni(string fullRoot, string displayPath, List<PafDiagnostic> diagnostics, IReadOnlyDictionary<string, string>? inMemoryDocuments = null)
    {
        if (inMemoryDocuments is not null && inMemoryDocuments.TryGetValue(displayPath, out var overlay))
        {
            if (System.Text.Encoding.UTF8.GetByteCount(overlay) > 65536)
            {
                diagnostics.Add(new PafDiagnostic(PafDiagnosticSeverity.Error, "IniTooLarge", displayPath, null, null,
                    $"{displayPath} is larger than 64 KB.", "Reduce the file below 64 KB; the packaging toolchain refuses larger metadata files."));
                return null;
            }
            var overlaid = IniDocument.Parse(overlay);
            foreach (var issue in overlaid.ParseIssues)
                diagnostics.Add(new PafDiagnostic(PafDiagnosticSeverity.Error, "IniParseIssue", displayPath, null, null,
                    $"{displayPath} line {issue.Line}: {issue.Message}", "Fix the line so it is a [section] header, a key=value entry, a comment, or a blank line."));
            AddTodoDiagnostics(overlaid, displayPath, diagnostics);
            return overlaid;
        }
        var path = Path.Combine(fullRoot, displayPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 65536)
        {
            diagnostics.Add(new PafDiagnostic(PafDiagnosticSeverity.Error, "IniTooLarge", displayPath, null, null,
                $"{displayPath} is larger than 64 KB.", "Reduce the file below 64 KB; the packaging toolchain refuses larger metadata files."));
            return null;
        }
        var document = IniDocument.Parse(File.ReadAllText(path));
        foreach (var issue in document.ParseIssues)
            diagnostics.Add(new PafDiagnostic(PafDiagnosticSeverity.Error, "IniParseIssue", displayPath, null, null,
                $"{displayPath} line {issue.Line}: {issue.Message}", "Fix the line so it is a [section] header, a key=value entry, a comment, or a blank line."));
        AddTodoDiagnostics(document, displayPath, diagnostics);
        return document;
    }

    /// <summary>
    /// Additive Phase 5 rule: any entry value still containing a TODO marker is a
    /// placeholder (the template scaffolder writes several on purpose) and is
    /// reported as a warning naming the exact field. It never changes the
    /// severity of any other rule; values without the marker are unaffected.
    /// </summary>
    private static void AddTodoDiagnostics(IniDocument document, string displayPath, List<PafDiagnostic> diagnostics)
    {
        foreach (var entry in document.AllEntries)
        {
            if (entry.Value.Contains("TODO", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(new PafDiagnostic(PafDiagnosticSeverity.Warning, "TodoPlaceholder", displayPath, entry.Section, entry.Key,
                    $"{displayPath} still has a TODO placeholder in [{entry.Section}] {entry.Key}.",
                    "Replace the TODO placeholder with the real value before publishing the package."));
        }
    }
}
