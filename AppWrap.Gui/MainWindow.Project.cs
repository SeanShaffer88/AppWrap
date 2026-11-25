using System.IO;
using AppWrap.Core;
using AppWrap.Core.Projects;

namespace AppWrap.Gui;

public partial class MainWindow
{
    // Phase 1 project model wiring: choosing a source folder reopens its
    // appwrap.project.json (or infers one for folders that predate project files),
    // the project is persisted before a build, and each completed build is recorded
    // in its history. Project persistence is bookkeeping only — a folder that cannot
    // accept the project file still packages exactly as before, with the failure
    // reported alongside the build result instead of blocking it.
    private sealed record LoadedProject(PafProject Project, bool Inferred);
    private LoadedProject? loadedProject;

    private static LoadedProject LoadProject(string folder, string? template = null)
    {
        var project = ProjectStore.LoadOrInfer(folder, out var inferred, template);
        return new LoadedProject(project, inferred);
    }

    private void ApplyLoadedProject(LoadedProject loaded)
    {
        loadedProject = loaded;
        if (loaded.Inferred) return;
        // Reopening a project restores the choices recorded with it.
        if (!string.IsNullOrWhiteSpace(loaded.Project.Name)) { name = loaded.Project.Name; InvalidateOutputName(); }
        if (loaded.Project.UserChoices.TryGetValue("namingStyle", out var styleName)
            && Enum.TryParse<PackageNamingStyle>(styleName, out var style) && Enum.IsDefined(style))
            namingStyle = style;
    }

    private async Task<string?> SaveProjectBeforeBuildAsync()
    {
        var loaded = loadedProject;
        if (loaded is null || string.IsNullOrEmpty(source)) return null;
        var project = loaded.Project;
        project.Name = name.Trim();
        project.UserChoices["namingStyle"] = namingStyle.ToString();
        try
        {
            await Task.Run(() =>
            {
                var snapshot = SourceSnapshot.Compute(source);
                project.SourceSnapshotHash = snapshot.Sha256;
                project.SourceSnapshotTruncated = snapshot.Truncated;
                ProjectStore.Save(source, project);
            });
            loadedProject = loaded with { Inferred = false };
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The project file (appwrap.project.json) could not be saved: " + ex.Message;
        }
    }

    private async Task<string?> RecordProjectBuildAsync(IEnumerable<OutputMode> modes)
    {
        var loaded = loadedProject;
        if (loaded is null || string.IsNullOrEmpty(source)) return null;
        try
        {
            await Task.Run(() => { foreach (var mode in modes) ProjectStore.RecordBuild(source, loaded.Project, mode); });
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The build was not recorded in the project file (appwrap.project.json): " + ex.Message;
        }
    }
}
