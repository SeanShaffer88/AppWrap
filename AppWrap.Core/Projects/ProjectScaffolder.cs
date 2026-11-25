using System.Text;

namespace AppWrap.Core.Projects;

/// <summary>The starting points offered by the Phase 5 "New Project from Template" flow.</summary>
public enum ProjectTemplateKind { BlankPafApp, WrappedBinary }

/// <summary>
/// The outcome of a scaffold: the created project, where it lives, and the
/// validator's diagnostics over the fresh tree (TODO placeholders appear as
/// warnings; a blank scaffold has no errors by construction).
/// </summary>
public sealed record ScaffoldResult(
    PafProject Project, string Root, string AppId, bool AdvancedMode, IReadOnlyList<PafDiagnostic> Diagnostics);

/// <summary>
/// Creates new PAF projects from templates (Development Suite Phase 5). The
/// blank template writes a minimal, valid PortableApps.com Format skeleton —
/// canonical layout, placeholder INI documents whose unfinished values carry
/// TODO markers, and an appwrap.project.json recording the template origin and
/// the targeted PAF spec version — without running any build. The "Wrapped
/// binary" template is not a tree written here: it is the long-standing
/// wrap-a-folder flow, and its only footprint is the template name recorded in
/// the project file when a project is inferred through it (see
/// <see cref="ProjectStore.LoadOrInfer(string, out bool, string?)"/>).
///
/// Creation follows the repository's transactional habits: the tree is staged
/// in a sibling temporary folder and moved into place only when complete, so
/// a failed scaffold never leaves a half-written project behind. The
/// destination must be new or empty — scaffolding never merges into, or
/// overwrites, an existing folder's content.
/// </summary>
public static class ProjectScaffolder
{
    /// <summary>The user-choice key under which a project's template origin is recorded in appwrap.project.json.</summary>
    public const string TemplateChoiceKey = "template";

    /// <summary>Recorded template name for projects created by <see cref="CreateBlank"/>.</summary>
    public const string BlankTemplateName = "BlankPafApp";

    /// <summary>Recorded template name for projects created by wrapping an existing folder.</summary>
    public const string WrappedBinaryTemplateName = "WrappedBinary";

    public static string TemplateName(ProjectTemplateKind kind) => kind switch
    {
        ProjectTemplateKind.BlankPafApp => BlankTemplateName,
        ProjectTemplateKind.WrappedBinary => WrappedBinaryTemplateName,
        _ => throw new UserInputException("Unknown project template."),
    };

    /// <summary>
    /// The AppID a scaffold derives from an app name, following the generator's
    /// convention (letters and digits, ending in "Portable"): a valid,
    /// non-placeholder identity the launcher INI can be named after immediately.
    /// </summary>
    public static string DeriveAppId(string appName)
    {
        var letters = new string(appName.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (letters.Length == 0) letters = "Application";
        if (letters.Length > 40) letters = letters[..40];
        return letters + "Portable";
    }

    /// <summary>
    /// Scaffolds a blank PAF project into <paramref name="destination"/>, which
    /// must not exist yet or must be an empty folder. Placeholder values that
    /// still need the author's input contain TODO markers, which
    /// <see cref="PafValidator"/> reports as TodoPlaceholder warnings; required
    /// fields are filled with valid placeholders, so the fresh project
    /// validates with zero errors. Custom-code starter files are written only
    /// when <paramref name="advancedMode"/> is on, matching the Phase 2 gate.
    /// </summary>
    public static ScaffoldResult CreateBlank(string destination, string appName, string? pafSpecVersion = null,
        bool advancedMode = false, CancellationToken token = default)
    {
        InputRules.Text(appName, "app name");
        var name = appName.Trim();
        var spec = string.IsNullOrWhiteSpace(pafSpecVersion) ? PafLayout.DefaultPafSpecVersion : pafSpecVersion.Trim();
        var appId = DeriveAppId(name);
        var fullDestination = Path.GetFullPath(destination);
        InputRules.SafePath(fullDestination);
        if (File.Exists(fullDestination))
            throw new UserInputException("Choose a new folder for the project; that path is an existing file.");
        if (Directory.Exists(fullDestination) && Directory.EnumerateFileSystemEntries(fullDestination).Any())
            throw new UserInputException("The destination folder is not empty. Choose a new, empty folder for the scaffolded project.");
        var parent = Path.GetDirectoryName(fullDestination)
            ?? throw new UserInputException("Choose a destination folder with a parent folder.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".appwrap-scaffold-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(staging);
            WriteBlankTree(staging, name, appId, spec, advancedMode);
            var project = new PafProject
            {
                Name = name,
                PafSpecVersion = spec,
                CreatedUtc = DateTimeOffset.UtcNow,
            };
            project.UserChoices[TemplateChoiceKey] = BlankTemplateName;
            project.UserChoices[ProjectConfigWorkspace.AdvancedModeChoiceKey] = advancedMode ? "true" : "false";
            var snapshot = SourceSnapshot.Compute(staging, token);
            project.SourceSnapshotHash = snapshot.Sha256;
            project.SourceSnapshotTruncated = snapshot.Truncated;
            ProjectStore.Save(staging, project);
            if (Directory.Exists(fullDestination))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(staging))
                    MoveEntry(entry, Path.Combine(fullDestination, Path.GetFileName(entry)));
                Directory.Delete(staging);
            }
            else Directory.Move(staging, fullDestination);
            var diagnostics = PafValidator.Validate(fullDestination, project);
            return new ScaffoldResult(project, fullDestination, appId, advancedMode, diagnostics);
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { /* best-effort cleanup of our own staging folder */ }
            throw;
        }
    }

    private static void MoveEntry(string source, string target)
    {
        if (Directory.Exists(source)) Directory.Move(source, target);
        else File.Move(source, target);
    }

    private static void WriteBlankTree(string root, string name, string appId, string spec, bool advancedMode)
    {
        var appInfoDir = Path.Combine(root, "App", "AppInfo");
        var launcherDir = Path.Combine(appInfoDir, "Launcher");
        Directory.CreateDirectory(launcherDir);
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        Directory.CreateDirectory(Path.Combine(root, "Other"));

        WriteText(Path.Combine(appInfoDir, "appinfo.ini"), Ini(
            $"; {name} — PortableApps.com Format package metadata.",
            "; Created by AppWrap's blank PAF template. Values containing TODO are",
            "; placeholders: replace them before publishing (AppWrap lists them as warnings).",
            "[Format]",
            "Type=PortableApps.comFormat",
            $"Version={spec}",
            "[Details]",
            $"Name={name}",
            $"AppID={appId}",
            "Publisher=TODO: publisher name",
            "Homepage=TODO: application homepage",
            "Category=Utilities",
            $"Description=TODO: describe what {name} does",
            "Language=English",
            "[License]",
            "Shareable=false",
            "OpenSource=false",
            "Freeware=false",
            "CommercialUse=false",
            "[Version]",
            "PackageVersion=1.0.0.0",
            "DisplayVersion=TODO: 1.0",
            "[Control]",
            "Icons=1",
            $"Start={appId}.exe"));

        WriteText(Path.Combine(appInfoDir, "installer.ini"), Ini(
            $"; {name} — PortableApps.com Installer settings.",
            "; AppWrap writes installer.ini itself when it packages a project; this",
            "; file records the installer settings a project controls directly. The",
            "; upgrade-guard keys in generated packages are owned by AppWrap and are",
            "; not set here.",
            "[Source]",
            "IncludeInstallerSource=false"));

        WriteText(Path.Combine(launcherDir, appId + ".ini"), Ini(
            $"; {name} — PortableApps.com Launcher configuration.",
            "; Created by AppWrap's blank PAF template. ProgramExecutable is a",
            "; placeholder: replace TODO.exe with the application's executable,",
            "; relative to the package's App folder.",
            "[Launch]",
            "ProgramExecutable=TODO.exe",
            "WaitForProgram=true"));

        if (!advancedMode) return;
        // Advanced mode on: documented, comment-only (inert) starter files for
        // both custom-code locations. The headers state plainly how each file
        // is — and is not — consumed, so the starters never overclaim.
        WriteText(Path.Combine(launcherDir, "Custom.nsh"), Ini(
            $"; {name} — custom launcher code.",
            "; Starter created by AppWrap's blank PAF template because this project",
            "; has Advanced mode on. The PortableApps.com Launcher includes this",
            "; file (App/AppInfo/Launcher/Custom.nsh) at compile time when a launcher",
            "; for this project is compiled from a portable profile, so code placed",
            "; here is live for those builds. AppWrap's basic packaging path does",
            "; not compile it, and AppWrap does not check custom code. This starter",
            "; is intentionally comment-only (inert) until you add your own code."));
        var sourceDir = Path.Combine(root, "Other", "Source");
        Directory.CreateDirectory(sourceDir);
        WriteText(Path.Combine(sourceDir, "PortableApps.comInstallerCustom.nsh"), Ini(
            $"; {name} — custom installer code.",
            "; Starter created by AppWrap's blank PAF template because this project",
            "; has Advanced mode on. In the official PortableApps.com toolchain this",
            "; file (Other/Source/PortableApps.comInstallerCustom.nsh) is where",
            "; installer custom code lives. AppWrap's own packaging does NOT consume",
            "; a source-supplied copy: it rejects payloads that contain one and",
            "; writes its own first-party upgrade-guard hook to this path in",
            "; generated packages. Keep this starter for use with the official",
            "; installer, and remove it from any folder you hand to AppWrap for",
            "; packaging. This starter is intentionally comment-only (inert)."));
    }

    /// <summary>Joins INI lines with CRLF and a trailing CRLF, matching the line endings the generators emit.</summary>
    private static string Ini(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    /// <summary>Writes a text file as UTF-8 with BOM, matching the encoding PortableBuild writes package metadata in.</summary>
    private static void WriteText(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(true));
}
