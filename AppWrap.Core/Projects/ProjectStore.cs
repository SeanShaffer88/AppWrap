using AppWrap.Core.PortableProfiles;

namespace AppWrap.Core.Projects;

/// <summary>
/// Reads, writes and infers <see cref="PafProject"/> documents (appwrap.project.json
/// at the project root). All file access is bounded and local: no network, no execution.
/// </summary>
public static class ProjectStore
{
    public const string FileName = "appwrap.project.json";

    /// <summary>Maximum project file size accepted on load (the file is small by design).</summary>
    public const int MaxBytes = 1_000_000;

    /// <summary>True when a file name is the project file name (comparison matches Windows file-name semantics).</summary>
    public static bool IsProjectFileName(string fileName) =>
        string.Equals(fileName, FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when <paramref name="entryPath"/> is the project file at the root of
    /// <paramref name="root"/> — the one entry payload assembly must never sweep up.
    /// Nested files with the same name are ordinary payload files and return false.
    /// </summary>
    public static bool IsRootProjectFile(string root, string entryPath)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullEntry = Path.GetFullPath(entryPath);
        return string.Equals(Path.GetDirectoryName(fullEntry), fullRoot, StringComparison.OrdinalIgnoreCase)
            && IsProjectFileName(Path.GetFileName(fullEntry));
    }

    public static string PathFor(string root) => Path.Combine(Path.GetFullPath(root), FileName);

    /// <summary>
    /// The conventional in-project location of a package icon: the file the
    /// packaging flow itself writes into every staged package's App/AppInfo.
    /// A folder already carrying one is treated as having chosen it.
    /// </summary>
    public const string ConventionalIconPath = "App/AppInfo/appicon.ico";

    /// <summary>
    /// Converts a chosen file path to the stored form used by the project's
    /// path properties (icon, sidebar): project-relative in forward slashes
    /// when the file lives inside the project root, the absolute path when it
    /// lives outside — the record stays truthful instead of silently copying
    /// user files into the project.
    /// </summary>
    public static string ToStoredPath(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path);
        return InputRules.IsWithin(full, fullRoot)
            ? PafLayout.ToDisplayPath(Path.GetRelativePath(fullRoot, full))
            : full;
    }

    /// <summary>Resolves a stored project path (see <see cref="ToStoredPath"/>) back to an absolute file path.</summary>
    public static string ResolveStoredPath(string root, string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return Path.IsPathRooted(stored)
            ? Path.GetFullPath(stored)
            : Path.GetFullPath(Path.Combine(Path.GetFullPath(root),
                stored.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Loads the project at <paramref name="root"/>, or null when the folder has no project file yet.</summary>
    public static PafProject? Load(string root)
    {
        var path = PathFor(root);
        InputRules.SafePath(path);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaxBytes) throw new UserInputException("The project file (appwrap.project.json) is too large.");
        return PafProject.FromJson(File.ReadAllText(path));
    }

    /// <summary>
    /// Loads the folder's project when one exists; otherwise infers a new project from
    /// the folder (see <see cref="Infer"/>) and reports <paramref name="inferred"/> so
    /// the caller can persist it on first save. Backwards compatibility: folders that
    /// predate project files keep working unchanged.
    /// </summary>
    /// <param name="template">
    /// Optional template name (see <see cref="ProjectScaffolder.TemplateChoiceKey"/>)
    /// recorded in the user choices of a newly inferred project — this is how the
    /// Phase 5 "Wrapped binary" template, which is this inference path, records its
    /// origin. An existing project file is never rewritten by it: the template a
    /// project was created from is a fact about its creation, not a setting.
    /// </param>
    public static PafProject LoadOrInfer(string root, out bool inferred, string? template = null)
    {
        var existing = Load(root);
        if (existing is not null) { inferred = false; return existing; }
        inferred = true;
        return Infer(root, template);
    }

    /// <summary>
    /// Infers a project from a bare folder: the name and targeted PAF version come from
    /// an existing App/AppInfo/appinfo.ini when the folder already carries one (its
    /// [Details] Name and [Format] Version), otherwise from the folder name and the
    /// toolchain default. A fresh source snapshot is taken when the tree is in bounds.
    /// </summary>
    public static PafProject Infer(string root, string? template = null, CancellationToken token = default)
    {
        var fullRoot = Path.GetFullPath(root);
        InputRules.SafePath(fullRoot);
        if (!Directory.Exists(fullRoot)) throw new UserInputException("Choose an application folder that still exists.");
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullRoot));
        var spec = PafLayout.DefaultPafSpecVersion;
        var appInfoPath = Path.Combine(fullRoot, "App", "AppInfo", "appinfo.ini");
        if (File.Exists(appInfoPath) && new FileInfo(appInfoPath).Length <= 65536)
        {
            var document = IniDocument.Parse(File.ReadAllText(appInfoPath));
            var existingName = document.GetValue("Details", "Name");
            if (!string.IsNullOrWhiteSpace(existingName)) name = existingName.Trim();
            var existingSpec = document.GetValue("Format", "Version");
            if (!string.IsNullOrWhiteSpace(existingSpec)) spec = existingSpec.Trim();
        }
        var snapshot = SourceSnapshot.Compute(fullRoot, token);
        var project = new PafProject
        {
            Name = name,
            PafSpecVersion = spec,
            CreatedUtc = DateTimeOffset.UtcNow,
            SourceSnapshotHash = snapshot.Sha256,
            SourceSnapshotTruncated = snapshot.Truncated,
            // The icon has a conventional in-package location (ConventionalIconPath);
            // the sidebar has none — the official installer keeps its sidebar bitmap
            // in its own installation, never in the package (docs/PAF-COMPLIANCE.md) —
            // so only the icon is ever inferred, and only when the file is there.
            IconPath = File.Exists(Path.Combine(fullRoot, "App", "AppInfo", "appicon.ico"))
                ? ConventionalIconPath
                : null,
        };
        if (template is not null) project.UserChoices[ProjectScaffolder.TemplateChoiceKey] = template;
        return project;
    }

    /// <summary>
    /// Persists the project at the current schema version via a same-folder temporary
    /// file moved into place, so an interrupted save never leaves a torn document.
    /// </summary>
    public static void Save(string root, PafProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var path = PathFor(root);
        InputRules.SafePath(path);
        Directory.CreateDirectory(Path.GetFullPath(root));
        var temporary = Path.Combine(Path.GetFullPath(root), ".appwrap-project-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, project.ToJson());
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Records a completed build mode and refreshes the source snapshot, then saves.</summary>
    public static void RecordBuild(string root, PafProject project, OutputMode mode, CancellationToken token = default)
    {
        var snapshot = SourceSnapshot.Compute(root, token);
        project.SourceSnapshotHash = snapshot.Sha256;
        project.SourceSnapshotTruncated = snapshot.Truncated;
        project.BuildModeHistory.Add(new BuildModeRecord(mode.ToString(), DateTimeOffset.UtcNow));
        Save(root, project);
    }
}
