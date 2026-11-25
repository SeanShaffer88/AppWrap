using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AppWrap.Build;
using AppWrap.Build.Audit;
using AppWrap.Build.Pipeline;
using AppWrap.Core;
using AppWrap.Core.Audit;
using AppWrap.Core.Pipeline;
using AppWrap.Core.PortableProfiles;
using AppWrap.Core.Projects;
using AppWrap.Core.TestHarness;

namespace AppWrap.Cli;

/// <summary>
/// Where the CLI runs: the base directory the packaging backends are resolved
/// from and the state directory builds stage in — both exactly the GUI's
/// (the executable's folder, and its Data subfolder). Tests substitute
/// temporary directories.
/// </summary>
public sealed record CliEnvironment(string BaseDirectory, string StateDirectory, string Version, Action<string>? Verbose = null, bool HostedInGui = false)
{
    public static CliEnvironment Default(string version) =>
        new(AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "Data"), version);
}

/// <summary>A validator diagnostic in the CLI's unified shape (PAF and profile diagnostics both map onto it).</summary>
public sealed record CliDiagnostic(string Severity, string Code, string? File, string? Section, string? Key, string Message, string? Hint);

/// <summary>
/// One command's outcome: the exit code, the human lines for stdout, the
/// structured diagnostics, and the JSON payload. <see cref="ToJson"/> renders
/// the single-object envelope every command produces under --json:
/// { ok, command, exitCode, result, diagnostics, error }.
/// </summary>
public sealed class CliResult
{
    public required string Command { get; init; }
    public int ExitCode { get; set; }
    public List<string> Lines { get; } = [];
    public List<CliDiagnostic> Diagnostics { get; } = [];
    public JsonObject? Data { get; set; }
    public string? Error { get; set; }
    public bool Ok => ExitCode == 0;

    public string ToJson()
    {
        var diagnostics = new JsonArray();
        foreach (var d in Diagnostics)
            diagnostics.Add(new JsonObject
            {
                ["severity"] = d.Severity, ["code"] = d.Code, ["file"] = d.File,
                ["section"] = d.Section, ["key"] = d.Key, ["message"] = d.Message, ["hint"] = d.Hint,
            });
        var envelope = new JsonObject
        {
            ["ok"] = Ok,
            ["command"] = Command,
            ["exitCode"] = ExitCode,
            ["result"] = Data,
            ["diagnostics"] = diagnostics,
            ["error"] = Error,
        };
        return envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>
/// Executes parsed invocations against the same Core/Build operations the
/// wizard calls — no packaging, validation or harness logic is duplicated
/// here. This layer performs no console I/O: output is returned as a
/// <see cref="CliResult"/> and progress is reported through the
/// environment's verbose callback, so checks drive it directly.
///
/// Exit codes: 0 success; 2 usage error; 3 validation failed (diagnostics
/// present, nothing written); 4 build/test failure. Expected failures are
/// returned, never thrown; the existing typed exceptions are mapped onto
/// the codes (UserInputException → 3, BuildFailureException → 4).
/// </summary>
public static class CommandRouter
{
    public static async Task<CliResult> ExecuteAsync(CliInvocation invocation, CliEnvironment environment, CancellationToken token)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        try
        {
            if (invocation.Version)
            {
                result.Lines.Add("AppWrap " + environment.Version);
                result.Data = new JsonObject { ["version"] = environment.Version };
                return result;
            }
            if (invocation.Help || invocation.CommandKey.Length == 0)
            {
                var help = invocation.CommandKey.Length == 0 ? CliHelp.TopLevel() : CliHelp.For(invocation.CommandKey);
                result.Lines.AddRange(help);
                if (invocation.CommandKey.Length == 0 && !invocation.Help)
                {
                    result.ExitCode = 2;
                    result.Error = "No command given.";
                }
                return result;
            }
            return invocation.CommandKey switch
            {
                "build" => await BuildAsync(invocation, environment, token).ConfigureAwait(false),
                "project validate" => ProjectValidate(invocation),
                "project info" => ProjectInfo(invocation),
                "project create" => ProjectCreate(invocation),
                "config list" => ConfigList(invocation),
                "config get" => ConfigGet(invocation),
                "config set" => ConfigSet(invocation),
                "profile validate" => ProfileValidate(invocation),
                "test" => await TestAsync(invocation, environment, token).ConfigureAwait(false),
                "audit" => await AuditAsync(invocation, environment, token).ConfigureAwait(false),
                "gui" => Gui(environment),
                _ => throw new CliUsageException($"Unknown command '{invocation.CommandKey}'."),
            };
        }
        catch (CliUsageException ex)
        {
            result.ExitCode = 2;
            result.Error = ex.Message;
            result.Lines.AddRange(CliHelp.For(ex.Command ?? invocation.CommandKey));
            return result;
        }
        catch (UserInputException ex) { result.ExitCode = 3; result.Error = ex.Message; return result; }
        catch (BuildFailureException ex)
        {
            result.ExitCode = 4;
            result.Error = ex.Message;
            environment.Verbose?.Invoke(ex.Details);
            return result;
        }
        catch (OperationCanceledException) { result.ExitCode = 4; result.Error = "Cancelled."; return result; }
        catch (Exception ex) { result.ExitCode = 4; result.Error = ex.Message; return result; }
    }

    // ----- build -----------------------------------------------------------

    private static async Task<CliResult> BuildAsync(CliInvocation invocation, CliEnvironment environment, CancellationToken token)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var options = BuildCommandOptions.FromInvocation(invocation);
        if (options.Source is null) throw new CliUsageException("build requires --source <folder>.", "build");
        var source = Path.GetFullPath(options.Source);

        PafProject? project = null;
        string? projectRoot = null;
        if (options.Project is not null)
        {
            var projectPath = Path.GetFullPath(options.Project);
            if (File.Exists(projectPath) && !ProjectStore.IsProjectFileName(Path.GetFileName(projectPath)))
                throw new UserInputException("--project must name appwrap.project.json or the folder containing it.");
            projectRoot = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath)
                ?? throw new CliUsageException("--project must name appwrap.project.json or its folder.", "build");
            project = ProjectStore.Load(projectRoot)
                ?? throw new UserInputException("No appwrap.project.json was found at " + projectRoot + ".");
        }
        var projectStyle = project is not null && project.UserChoices.TryGetValue("namingStyle", out var saved) ? saved : null;
        // The project's image properties are stored project-relative (or absolute
        // for out-of-project files); resolve them against the project root so the
        // precedence rule — flag > project > none — composes with flag paths.
        var projectIcon = project?.IconPath is { } iconPath ? ProjectStore.ResolveStoredPath(projectRoot!, iconPath) : null;
        var projectSidebar = project?.SidebarImagePath is { } sidebarPath ? ProjectStore.ResolveStoredPath(projectRoot!, sidebarPath) : null;
        var effective = options.WithProjectDefaults(project?.Name, projectStyle, projectIcon, projectSidebar);

        if (effective.Profile is not null && effective.AuditDraft is not null)
            throw new CliUsageException("Use either --profile or --audit-draft, not both.", "build");
        PortableProfile? profile = null;
        if (effective.Profile is not null || effective.AuditDraft is not null)
        {
            var (loaded, _) = LoadProfileFile(effective.Profile ?? effective.AuditDraft!);
            profile = loaded;
            var diagnostics = PortableProfileValidator.Validate(loaded).Select(FromProfileDiagnostic).ToList();
            result.Diagnostics.AddRange(diagnostics);
            if (diagnostics.Any(d => d.Severity == "error"))
            {
                result.ExitCode = 3;
                result.Error = "The portable profile has validation errors; nothing was built.";
                AddDiagnosticLines(result);
                return result;
            }
        }

        var name = effective.Name ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        var version = effective.Version ?? "1.0";
        var style = effective.Style;
        var launch = effective.Launch ?? DiscoverLaunch(source);
        var desktop = DefaultOutputFolder();
        var outputRoot = effective.Output is not null ? Path.GetFullPath(effective.Output) : desktop;

        AppWrapInput MakeInput(OutputMode mode, string outputPath) => new(
            source, name, version, launch,
            effective.StartMenu ?? true, effective.Desktop ?? false,
            outputPath, mode,
            mode is OutputMode.PortableApps or OutputMode.PafPackage ? effective.Icon : null,
            mode == OutputMode.PortableApps ? effective.Sidebar : null,
            mode == OutputMode.Standard
                ? StandardInstallerOptions.Normalize(new StandardInstallerOptions(effective.RunAfter ?? true, effective.Startup ?? false, effective.Website is not null, effective.Website))
                : null,
            mode is OutputMode.PortableApps or OutputMode.PafPackage ? profile : null);

        var inputs = new List<AppWrapInput>();
        switch (effective.Mode)
        {
            case "installer":
                inputs.Add(MakeInput(OutputMode.Standard, effective.Output is not null ? outputRoot : Path.Combine(desktop, PackageNaming.GetFileName(name, version, OutputMode.Standard, style))));
                break;
            case "portable":
                inputs.Add(MakeInput(OutputMode.PortableApps, effective.Output is not null ? outputRoot : Path.Combine(desktop, PackageNaming.GetFileName(name, version, OutputMode.PortableApps, style))));
                break;
            case "both":
                inputs.Add(MakeInput(OutputMode.Standard, Path.Combine(outputRoot, PackageNaming.GetFileName(name, version, OutputMode.Standard, style))));
                inputs.Add(MakeInput(OutputMode.PortableApps, Path.Combine(outputRoot, PackageNaming.GetFileName(name, version, OutputMode.PortableApps, style))));
                break;
            case "paf":
                inputs.Add(MakeInput(OutputMode.PafPackage, Path.Combine(outputRoot, PackageNaming.GetFolderName(name))));
                break;
        }

        var outputs = new JsonArray();
        foreach (var input in inputs)
            outputs.Add(new JsonObject { ["mode"] = ModeWord(input.Mode), ["path"] = Path.GetFullPath(input.OutputPath) });
        result.Data = new JsonObject
        {
            ["mode"] = effective.Mode, ["name"] = name, ["version"] = version, ["launch"] = launch,
            ["source"] = source, ["dryRun"] = invocation.DryRun, ["outputs"] = outputs,
        };

        if (invocation.DryRun)
        {
            foreach (var input in inputs) InputRules.Validate(input);
            result.Lines.Add("Dry run — validated; nothing will be written.");
            result.Lines.Add($"Source: {source}");
            result.Lines.Add($"App: {name} {version} · launch: {launch}");
            foreach (var input in inputs) result.Lines.Add($"{ModeLabel(input.Mode)}: {input.OutputPath}");
            AddDiagnosticLines(result);
            return result;
        }

        foreach (var input in inputs)
        {
            if (input.Mode == OutputMode.PafPackage)
            {
                if (Directory.Exists(input.OutputPath) && !effective.Overwrite)
                    throw new UserInputException("The PAF package folder already exists: " + input.OutputPath + ". Pass --overwrite to replace it.");
            }
            else if (File.Exists(input.OutputPath) && !effective.Overwrite)
                throw new UserInputException("The output file already exists: " + input.OutputPath + ". Pass --overwrite to replace it.");
        }

        var service = new BuildService(environment.BaseDirectory, environment.StateDirectory);
        var built = new List<(OutputMode Mode, BuildResult Result)>();
        if (effective.Mode == "both")
        {
            IProgress<BatchBuildPhase>? progress = environment.Verbose is null
                ? null
                : new SynchronousProgress<BatchBuildPhase>(phase => environment.Verbose("build: " + phase));
            var batch = await service.BuildBothAsync(inputs[0], inputs[1], token, overwriteExisting: true, progress: progress).ConfigureAwait(false);
            built.Add((OutputMode.Standard, batch.Standard));
            built.Add((OutputMode.PortableApps, batch.Portable));
            if (batch.CleanupWarning is not null) result.Lines.Add("Warning: " + batch.CleanupWarning);
        }
        else
        {
            var single = await service.BuildAsync(inputs[0], token).ConfigureAwait(false);
            built.Add((inputs[0].Mode, single));
            if (single.CleanupWarning is not null) result.Lines.Add("Warning: " + single.CleanupWarning);
        }

        var builtOutputs = new JsonArray();
        foreach (var (mode, build) in built)
        {
            builtOutputs.Add(new JsonObject
            {
                ["mode"] = ModeWord(mode), ["path"] = build.OutputPath, ["sha256"] = build.Sha256, ["bytes"] = build.Bytes,
            });
            result.Lines.Add($"Built {ModeLabel(mode)}: {build.OutputPath} ({build.Bytes} bytes, sha256 {build.Sha256})");
        }
        result.Data["outputs"] = builtOutputs;

        if (project is not null && projectRoot is not null)
        {
            try
            {
                project.Name = name;
                project.UserChoices["namingStyle"] = style.ToString();
                // The effective image picks are recorded as project properties,
                // mirroring the wizard; stored project-relative when the file
                // lives under the project root.
                project.IconPath = effective.Icon is null ? null : ProjectStore.ToStoredPath(projectRoot, Path.GetFullPath(effective.Icon));
                project.SidebarImagePath = effective.Sidebar is null ? null : ProjectStore.ToStoredPath(projectRoot, Path.GetFullPath(effective.Sidebar));
                foreach (var (mode, _) in built) ProjectStore.RecordBuild(projectRoot, project, mode, token);
                result.Lines.Add("Recorded in project: " + ProjectStore.PathFor(projectRoot));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Lines.Add("Warning: the build was not recorded in the project file (appwrap.project.json): " + ex.Message);
            }
        }
        AddDiagnosticLines(result);
        return result;
    }

    private static string DiscoverLaunch(string source)
    {
        var executables = InputRules.Executables(source);
        return executables.Count switch
        {
            1 => executables[0],
            0 => throw new UserInputException("No application .exe was found in the source folder. Choose the folder containing the app's .exe."),
            _ => throw new UserInputException("The source folder has more than one .exe; pass --launch <relative path>. Found: " + string.Join(", ", executables)),
        };
    }

    private static string DefaultOutputFolder()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return desktop.Length != 0 ? desktop : Directory.GetCurrentDirectory();
    }

    // ----- project ---------------------------------------------------------

    private static string ResolveProjectRoot(string target)
    {
        var full = Path.GetFullPath(target);
        if (File.Exists(full))
        {
            if (!ProjectStore.IsProjectFileName(Path.GetFileName(full)))
                throw new UserInputException("Expected a project folder or its appwrap.project.json file.");
            return Path.GetDirectoryName(full) ?? throw new UserInputException("The project file has no containing folder.");
        }
        return full;
    }

    private static CliResult ProjectValidate(CliInvocation invocation)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var root = ResolveProjectRoot(invocation.Positionals[0]);
        var project = ProjectStore.Load(root);
        var diagnostics = PafValidator.Validate(root, project).Select(FromPafDiagnostic).ToList();
        result.Diagnostics.AddRange(diagnostics);
        result.Data = new JsonObject
        {
            ["root"] = root,
            ["projectFile"] = project is not null,
            ["name"] = project?.Name,
            ["pafSpecVersion"] = project?.PafSpecVersion,
        };
        AddDiagnosticLines(result);
        result.Lines.Add(diagnostics.Any(d => d.Severity == "error")
            ? "Validation failed: errors above."
            : "Validation passed (warnings and notes above, if any).");
        if (diagnostics.Any(d => d.Severity == "error")) result.ExitCode = 3;
        return result;
    }

    private static CliResult ProjectInfo(CliInvocation invocation)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var root = ResolveProjectRoot(invocation.Positionals[0]);
        var project = ProjectStore.LoadOrInfer(root, out var inferred);
        var history = new JsonArray();
        foreach (var record in project.BuildModeHistory)
            history.Add(new JsonObject { ["mode"] = record.Mode, ["completedUtc"] = record.CompletedUtc.ToString("o") });
        var choices = new JsonObject();
        foreach (var pair in project.UserChoices) choices[pair.Key] = pair.Value;
        JsonObject ImageInfo(string? stored)
        {
            var resolved = stored is null ? null : ProjectStore.ResolveStoredPath(root, stored);
            return new JsonObject
            {
                ["path"] = stored,
                ["resolved"] = resolved,
                ["exists"] = resolved is not null && File.Exists(resolved),
            };
        }
        result.Data = new JsonObject
        {
            ["root"] = root,
            ["inferred"] = inferred,
            ["name"] = project.Name,
            ["pafSpecVersion"] = project.PafSpecVersion,
            ["schemaVersion"] = project.SchemaVersion,
            ["createdUtc"] = project.CreatedUtc.ToString("o"),
            ["sourceSnapshotHash"] = project.SourceSnapshotHash,
            ["sourceSnapshotTruncated"] = project.SourceSnapshotTruncated,
            ["icon"] = ImageInfo(project.IconPath),
            ["sidebar"] = ImageInfo(project.SidebarImagePath),
            ["buildHistory"] = history,
            ["userChoices"] = choices,
        };
        result.Lines.Add("Name: " + project.Name);
        result.Lines.Add("PAF spec: " + project.PafSpecVersion + " · project schema: " + project.SchemaVersion + (inferred ? " · inferred (no project file yet)" : " · from appwrap.project.json"));
        result.Lines.Add("Source snapshot: " + (project.SourceSnapshotTruncated ? "truncated (tree beyond snapshot bounds)" : project.SourceSnapshotHash ?? "none"));
        result.Lines.Add("Icon: " + (project.IconPath ?? "(default)"));
        result.Lines.Add("Sidebar image: " + (project.SidebarImagePath ?? "(default)"));
        result.Lines.Add("Builds recorded: " + project.BuildModeHistory.Count);
        return result;
    }

    private static CliResult ProjectCreate(CliInvocation invocation)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var folder = Path.GetFullPath(invocation.Positionals[0]);
        var template = (invocation.Value("template") ?? "blank").ToLowerInvariant();
        if (template is not ("blank" or "wrapped"))
            throw new CliUsageException($"--template must be blank or wrapped; got '{invocation.Value("template")}'.", "project create");
        var name = invocation.Value("name") ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        if (string.IsNullOrWhiteSpace(name)) throw new CliUsageException("Pass --name for the new project.", "project create");
        if (template == "blank")
        {
            var appId = ProjectScaffolder.DeriveAppId(name);
            if (invocation.Value("app-id") is { } given && !string.Equals(given, appId, StringComparison.Ordinal))
                throw new CliUsageException($"--app-id must be {appId} for the name '{name}': AppWrap derives the AppID from the app name.", "project create");
            var scaffold = ProjectScaffolder.CreateBlank(folder, name, advancedMode: invocation.Has("advanced"));
            result.Diagnostics.AddRange(scaffold.Diagnostics.Select(FromPafDiagnostic));
            result.Data = new JsonObject
            {
                ["root"] = scaffold.Root, ["template"] = "blank", ["name"] = name,
                ["appId"] = scaffold.AppId, ["advancedMode"] = scaffold.AdvancedMode,
            };
            result.Lines.Add($"Created blank PAF project '{name}' at {scaffold.Root} (AppID {scaffold.AppId}).");
            AddDiagnosticLines(result);
            if (result.Diagnostics.Any(d => d.Severity == "error")) result.ExitCode = 3;
            return result;
        }
        var project = ProjectStore.LoadOrInfer(folder, out var inferred, ProjectScaffolder.WrappedBinaryTemplateName);
        ProjectStore.Save(folder, project);
        result.Data = new JsonObject
        {
            ["root"] = folder, ["template"] = "wrapped", ["name"] = project.Name, ["inferred"] = inferred,
        };
        result.Lines.Add($"Recorded the wrapped-binary project '{project.Name}' at {ProjectStore.PathFor(folder)}.");
        return result;
    }

    // ----- config ----------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, ProjectDocumentKind> DocumentWords =
        new Dictionary<string, ProjectDocumentKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["appinfo"] = ProjectDocumentKind.AppInfo,
            ["launcher"] = ProjectDocumentKind.Launcher,
            ["installer"] = ProjectDocumentKind.Installer,
        };

    private static ProjectDocumentKind ParseDocument(string word, string command) =>
        DocumentWords.TryGetValue(word, out var kind)
            ? kind
            : throw new CliUsageException($"Unknown document '{word}'. Documents: appinfo, launcher, installer, project.", command);

    /// <summary>The config surface's fourth document: the project's own properties in appwrap.project.json.</summary>
    private static bool IsProjectDocument(string word) => string.Equals(word, "project", StringComparison.OrdinalIgnoreCase);

    /// <summary>The project document's fields: section Images, the two image property keys.</summary>
    private static readonly (string Key, string Label)[] ProjectImageFields =
        [(ProjectConfigWorkspace.IconPathKey, "App icon"), (ProjectConfigWorkspace.SidebarImagePathKey, "Sidebar image")];

    private static string? ProjectImageValue(ProjectConfigWorkspace workspace, string key) =>
        string.Equals(key, ProjectConfigWorkspace.IconPathKey, StringComparison.OrdinalIgnoreCase) ? workspace.IconPath
        : string.Equals(key, ProjectConfigWorkspace.SidebarImagePathKey, StringComparison.OrdinalIgnoreCase) ? workspace.SidebarImagePath
        : null;

    private static void RequireProjectImageField(string section, string key, string command)
    {
        if (!string.Equals(section, "Images", StringComparison.OrdinalIgnoreCase)
            || !ProjectImageFields.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)))
            throw new CliUsageException(
                $"The project document has one section, Images, with keys: {string.Join(", ", ProjectImageFields.Select(f => f.Key))}. Got [{section}] {key}.",
                command);
    }

    private static CliResult ConfigList(CliInvocation invocation)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var workspace = ProjectConfigWorkspace.Load(Path.GetFullPath(invocation.Positionals[0]));
        if (invocation.Positionals.Count == 1)
        {
            var documents = new JsonArray();
            foreach (var kind in Enum.GetValues<ProjectDocumentKind>())
            {
                var display = workspace.DisplayPathFor(kind);
                var exists = File.Exists(Path.Combine(workspace.Root, display.Replace('/', Path.DirectorySeparatorChar)));
                documents.Add(new JsonObject
                {
                    ["document"] = DocumentWord(kind), ["path"] = display,
                    ["fields"] = ProjectConfigWorkspace.FieldsFor(kind).Count, ["exists"] = exists,
                });
                result.Lines.Add($"{DocumentWord(kind)} — {display} ({ProjectConfigWorkspace.FieldsFor(kind).Count} fields{(exists ? "" : ", not created yet")})");
            }
            documents.Add(new JsonObject
            {
                ["document"] = "project", ["path"] = ProjectStore.FileName,
                ["fields"] = ProjectImageFields.Length, ["exists"] = File.Exists(ProjectStore.PathFor(workspace.Root)),
            });
            result.Lines.Add($"project — {ProjectStore.FileName} ({ProjectImageFields.Length} fields)");
            result.Data = new JsonObject { ["root"] = workspace.Root, ["documents"] = documents };
            return result;
        }
        if (IsProjectDocument(invocation.Positionals[1]))
        {
            var imageFields = new JsonArray();
            foreach (var state in workspace.ImageFieldStates())
            {
                imageFields.Add(new JsonObject
                {
                    ["section"] = "Images", ["key"] = state.Key, ["label"] = state.Label,
                    ["kind"] = "path", ["required"] = false,
                    ["present"] = state.StoredPath is not null, ["value"] = state.StoredPath,
                    ["resolved"] = state.ResolvedPath, ["exists"] = state.Exists,
                });
                result.Lines.Add($"project [Images] {state.Key} = {(state.StoredPath ?? "(not set)")}");
            }
            result.Data = new JsonObject
            {
                ["root"] = workspace.Root, ["document"] = "project",
                ["path"] = ProjectStore.FileName, ["fields"] = imageFields,
            };
            return result;
        }
        var documentKind = ParseDocument(invocation.Positionals[1], invocation.CommandKey);
        var fields = new JsonArray();
        foreach (var field in ProjectConfigWorkspace.FieldsFor(documentKind))
        {
            var value = workspace.GetField(field);
            fields.Add(new JsonObject
            {
                ["section"] = field.Section, ["key"] = field.Key, ["label"] = field.Label,
                ["kind"] = field.Kind == ConfigFieldKind.Boolean ? "boolean" : "text",
                ["required"] = field.Required, ["present"] = value is not null, ["value"] = value,
            });
            result.Lines.Add($"{DocumentWord(documentKind)} [{field.Section}] {field.Key} = {(value ?? "(not set)")}");
        }
        result.Data = new JsonObject
        {
            ["root"] = workspace.Root, ["document"] = DocumentWord(documentKind),
            ["path"] = workspace.DisplayPathFor(documentKind), ["fields"] = fields,
        };
        return result;
    }

    private static CliResult ConfigGet(CliInvocation invocation)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var workspace = ProjectConfigWorkspace.Load(Path.GetFullPath(invocation.Positionals[0]));
        if (IsProjectDocument(invocation.Positionals[1]))
        {
            var imageSection = invocation.Positionals[2];
            var imageKey = invocation.Positionals[3];
            RequireProjectImageField(imageSection, imageKey, invocation.CommandKey);
            var stored = ProjectImageValue(workspace, imageKey);
            var canonicalKey = ProjectImageFields.First(f => string.Equals(f.Key, imageKey, StringComparison.OrdinalIgnoreCase)).Key;
            result.Data = new JsonObject
            {
                ["document"] = "project", ["section"] = "Images", ["key"] = canonicalKey,
                ["present"] = stored is not null, ["value"] = stored,
                ["resolved"] = stored is null ? null : ProjectStore.ResolveStoredPath(workspace.Root, stored),
            };
            result.Lines.Add(stored ?? "(not set)");
            return result;
        }
        var kind = ParseDocument(invocation.Positionals[1], invocation.CommandKey);
        var section = invocation.Positionals[2];
        var key = invocation.Positionals[3];
        var value = workspace.GetField(kind, section, key);
        if (value is null && FindField(workspace, kind, section, key) is null)
            throw UnknownField(workspace, kind, section, key, invocation.CommandKey);
        result.Data = new JsonObject
        {
            ["document"] = DocumentWord(kind), ["section"] = section, ["key"] = key,
            ["present"] = value is not null, ["value"] = value,
        };
        result.Lines.Add(value ?? "(not set)");
        return result;
    }

    private static CliResult ConfigSet(CliInvocation invocation)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var workspace = ProjectConfigWorkspace.Load(Path.GetFullPath(invocation.Positionals[0]));
        if (IsProjectDocument(invocation.Positionals[1]))
        {
            var imageSection = invocation.Positionals[2];
            var imageKey = invocation.Positionals[3];
            var imageValue = invocation.Positionals[4];
            RequireProjectImageField(imageSection, imageKey, invocation.CommandKey);
            var canonicalKey = ProjectImageFields.First(f => string.Equals(f.Key, imageKey, StringComparison.OrdinalIgnoreCase)).Key;
            // The workspace normalizes the stored form (project-relative when the
            // file lives in the project) and Save persists the project file.
            if (string.Equals(canonicalKey, ProjectConfigWorkspace.IconPathKey, StringComparison.Ordinal))
                workspace.IconPath = imageValue;
            else
                workspace.SidebarImagePath = imageValue;
            workspace.Save();
            var storedBack = ProjectImageValue(workspace, canonicalKey);
            result.Data = new JsonObject
            {
                ["document"] = "project", ["section"] = "Images", ["key"] = canonicalKey,
                ["value"] = storedBack, ["path"] = ProjectStore.FileName,
            };
            result.Lines.Add($"Set project [Images] {canonicalKey} = {storedBack} (saved to {ProjectStore.FileName}).");
            return result;
        }
        var kind = ParseDocument(invocation.Positionals[1], invocation.CommandKey);
        var section = invocation.Positionals[2];
        var key = invocation.Positionals[3];
        var value = invocation.Positionals[4];
        var field = FindField(workspace, kind, section, key)
            ?? throw UnknownField(workspace, kind, section, key, invocation.CommandKey);
        if (field.Kind == ConfigFieldKind.Boolean && value is not ("true" or "false") && !bool.TryParse(value, out _))
            throw new UserInputException($"[{section}] {key} is a true/false field; use true or false.");
        workspace.SetField(kind, section, key, value);
        workspace.Save();
        result.Data = new JsonObject
        {
            ["document"] = DocumentWord(kind), ["section"] = section, ["key"] = key,
            ["value"] = value, ["path"] = workspace.DisplayPathFor(kind),
        };
        result.Lines.Add($"Set {DocumentWord(kind)} [{section}] {key} = {value} (saved to {workspace.DisplayPathFor(kind)}).");
        return result;
    }

    /// <summary>
    /// The catalog field for a section/key, synthesizing one for a key that
    /// exists in the document but not the guided catalog (real data the
    /// editor's text view would show). Null when the field is unknown to both.
    /// </summary>
    private static ConfigField? FindField(ProjectConfigWorkspace workspace, ProjectDocumentKind kind, string section, string key)
    {
        var catalog = ProjectConfigWorkspace.FieldsFor(kind).FirstOrDefault(f =>
            string.Equals(f.Section, section, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        if (catalog is not null) return catalog;
        var present = workspace.DocumentFor(kind).Entries(section)
            .Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        return present ? new ConfigField(kind, section, key, key, ConfigFieldKind.Text, false, "") : null;
    }

    private static CliUsageException UnknownField(ProjectConfigWorkspace workspace, ProjectDocumentKind kind, string section, string key, string command)
    {
        var fields = ProjectConfigWorkspace.FieldsFor(kind);
        var sections = fields.Select(f => f.Section).Distinct(StringComparer.OrdinalIgnoreCase);
        var keys = fields.Where(f => string.Equals(f.Section, section, StringComparison.OrdinalIgnoreCase)).Select(f => f.Key).ToList();
        if (keys.Count == 0)
            keys.AddRange(workspace.DocumentFor(kind).Entries(section).Select(e => e.Key));
        return new CliUsageException(
            $"[{section}] {key} is not a known field of {DocumentWord(kind)}. Sections: {string.Join(", ", sections)}. " +
            (keys.Count != 0 ? $"Keys in [{section}]: {string.Join(", ", keys)}." : $"There is no [{section}] section; add it through the document's known sections."),
            command);
    }

    private static string DocumentWord(ProjectDocumentKind kind) => kind switch
    {
        ProjectDocumentKind.AppInfo => "appinfo",
        ProjectDocumentKind.Launcher => "launcher",
        _ => "installer",
    };

    // ----- profile ---------------------------------------------------------

    /// <summary>
    /// Loads a profile file with the wizard's trust rules: a .json profile is
    /// imported through the workspace codec (imports never grant trust and
    /// force-cleanup starts unacknowledged); any other file is treated as a
    /// launcher INI and built through the profile codec as a PAL profile.
    /// </summary>
    internal static (PortableProfile Profile, string Format) LoadProfileFile(string path)
    {
        var full = Path.GetFullPath(path);
        InputRules.SafePath(full);
        if (!File.Exists(full)) throw new UserInputException("Choose a portable profile file that still exists.");
        if (new FileInfo(full).Length > IniDocument.MaxBytes) throw new UserInputException("The portable profile file is too large (up to 1 MB).");
        var text = File.ReadAllText(full);
        if (text.TrimStart().StartsWith('{'))
        {
            var state = new ProfileWorkspaceState();
            state.ImportProfileText(text);
            return (state.WorkingProfile, "json");
        }
        return (PortableProfileCodec.FromIniTexts(text, null, null, null, PortableExecutionKind.Pal), "ini");
    }

    private static CliResult ProfileValidate(CliInvocation invocation)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var (profile, format) = LoadProfileFile(invocation.Positionals[0]);
        var diagnostics = PortableProfileValidator.Validate(profile).Select(FromProfileDiagnostic).ToList();
        result.Diagnostics.AddRange(diagnostics);
        result.Data = new JsonObject
        {
            ["file"] = Path.GetFullPath(invocation.Positionals[0]),
            ["format"] = format,
            ["executionKind"] = profile.ExecutionKind.ToString(),
        };
        AddDiagnosticLines(result);
        result.Lines.Add(diagnostics.Any(d => d.Severity == "error") ? "Profile has errors." : "Profile is valid.");
        if (diagnostics.Any(d => d.Severity == "error")) result.ExitCode = 3;
        return result;
    }

    // ----- test ------------------------------------------------------------

    private static async Task<CliResult> TestAsync(CliInvocation invocation, CliEnvironment environment, CancellationToken token)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var folder = Path.GetFullPath(invocation.Positionals[0]);
        var modeWord = (invocation.Value("mode") ?? "portable").ToLowerInvariant();
        var mode = modeWord switch
        {
            "portable" => TestRunMode.Portable,
            "paf" => TestRunMode.PafPackage,
            _ => throw new CliUsageException($"--mode must be portable or paf; got '{invocation.Value("mode")}'.", "test"),
        };
        var timeout = TestHarness.DefaultTimeout;
        if (invocation.Value("timeout") is { } timeoutText)
        {
            if (!int.TryParse(timeoutText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds < 1 || seconds > 3600)
                throw new CliUsageException("--timeout must be a whole number of seconds from 1 to 3600.", "test");
            timeout = TimeSpan.FromSeconds(seconds);
        }
        var reportPath = invocation.Value("report") is { } given
            ? Path.GetFullPath(given)
            : Path.Combine(environment.StateDirectory, "TestReports", "test-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        var appName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var request = new TestRunRequest(appName, mode, folder, reportPath, invocation.Value("launch"), timeout);
        TestRunResult run;
        if (invocation.Has("sandboxie"))
        {
            var sandbox = new SandboxieProvider();
            if (!sandbox.IsAvailable)
            {
                result.ExitCode = 4;
                result.Error = "Sandboxie-Plus is not installed, so the Sandboxie leak-verification mode cannot run. Install Sandboxie-Plus (AppWrap Setup bootstraps the pinned release) or run the test without --sandboxie.";
                result.Lines.Add(result.Error);
                return result;
            }
            run = await AppWrap.Build.TestHarness.RunSandboxieAsync(request, sandbox, token,
                invocation.Has("registry") ? new WindowsRegistrySnapshotProvider() : null, null).ConfigureAwait(false);
        }
        else if (invocation.Has("registry"))
        {
            run = await AppWrap.Build.TestHarness.RunAsync(request, new ProcessTestRunner(), token, new WindowsRegistrySnapshotProvider(), null).ConfigureAwait(false);
        }
        else
        {
            run = await AppWrap.Build.TestHarness.RunAsync(request, token).ConfigureAwait(false);
        }
        var report = run.Report;
        var violations = new JsonArray();
        foreach (var write in report.Violations)
            violations.Add(new JsonObject
            {
                ["root"] = write.Root.ToString(), ["path"] = write.Path,
                ["change"] = write.Change.ToString(), ["category"] = write.Category.ToString(),
            });
        result.Data = new JsonObject
        {
            ["verdict"] = report.Verdict.ToString(),
            ["reportPath"] = run.ReportPath,
            ["appName"] = report.AppName,
            ["mode"] = modeWord,
            ["appExitCode"] = report.ExitCode is { } appExitCode ? JsonValue.Create(appExitCode) : null,
            ["timedOut"] = report.TimedOut,
            ["durationSeconds"] = report.DurationSeconds,
            ["detectedWrites"] = report.DetectedWrites.Count,
            ["violations"] = violations,
            ["registryObserved"] = report.RegistryObserved,
            ["observationBackend"] = report.ObservationBackend,
            ["registryChanges"] = report.RegistryChangeList.Count,
            ["registryViolations"] = report.RegistryViolationList.Count,
            ["cleanupWarning"] = run.CleanupWarning,
        };
        result.Lines.Add("Verdict: " + report.Verdict);
        foreach (var write in report.Violations)
            result.Lines.Add($"Violation: {write.Root} {write.Path} ({write.Change}, {write.Category})");
        result.Lines.Add("Report: " + run.ReportPath);
        if (run.CleanupWarning is not null) result.Lines.Add("Warning: " + run.CleanupWarning);
        if (report.Verdict != TestVerdict.Pass) result.ExitCode = 4;
        return result;
    }

    // ----- audit -----------------------------------------------------------

    /// <summary>
    /// The conversion audit: static by default (zip inventory, MSI tables,
    /// .reg parsing, exe family identification — nothing executed), with
    /// --runtime as the explicit second pass through the runtime auditor.
    /// --draft synthesizes a draft portable profile from the findings and
    /// saves it in the wizard's profile .json shape; like every import it
    /// is untrusted until reviewed and saved in the profile workspace.
    /// For a zip source the payload is extracted into a scratch folder
    /// under the state directory and kept (its path is in the result), so
    /// `build --source &lt;payloadFolder&gt;` can package it next.
    /// </summary>
    private static async Task<CliResult> AuditAsync(CliInvocation invocation, CliEnvironment environment, CancellationToken token)
    {
        var result = new CliResult { Command = invocation.CommandKey };
        var source = Path.GetFullPath(invocation.Positionals[0]);
        InputRules.SafePath(source);
        if (!File.Exists(source)) throw new UserInputException("Choose a setup .exe/.msi or portable .zip file that still exists.");
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".zip" or ".msi" or ".exe"))
            throw new CliUsageException($"audit takes a .zip, .msi or .exe source; got '{extension}'.", "audit");

        AppWrap.Core.Audit.AuditReport report;
        string? payloadFolder = null;
        string? runtimeBackend = null;
        if (invocation.Has("runtime"))
        {
            if (extension == ".zip")
                throw new CliUsageException("--runtime applies to a setup .exe/.msi. Audit the zip statically, then build and use 'test' on the package.", "audit");
            var silentArgs = invocation.Value("silent-args")?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            RuntimeAuditResult runtime;
            var sandbox = invocation.Has("sandboxie") ? new SandboxieProvider() : null;
            if (sandbox is not null && sandbox.IsAvailable)
            {
                runtime = await RuntimeAuditor.RunSandboxedAsync(new RuntimeAuditRequest(
                    source, invocation.Value("launch"), silentArgs), sandbox, new WindowsMsiReader(), token).ConfigureAwait(false);
                runtimeBackend = "sandboxie";
                result.Lines.Add("Runtime pass backend: Sandboxie (the installer and app ran inside a dedicated box; the host was not touched).");
            }
            else
            {
                if (sandbox is not null)
                    result.Lines.Add("Sandboxie-Plus is not installed, so the runtime pass ran on the host instead: the installer's registry writes land in the real registry (observing them is the point of the host route).");
                runtime = await RuntimeAuditor.RunAsync(new RuntimeAuditRequest(
                    source, invocation.Value("launch"), silentArgs), token).ConfigureAwait(false);
                runtimeBackend = "host";
            }
            report = runtime.Report;
            result.Lines.Add($"Runtime pass: installer exit {(runtime.InstallerExitCode?.ToString() ?? "unknown")}, app exit {(runtime.AppExitCode?.ToString() ?? "not launched")}{(runtime.AppTimedOut ? " (timed out)" : "")}, {runtime.RegistryChanges.Count} registry change(s).");
            if (runtime.CleanupWarning is not null) result.Lines.Add("Warning: " + runtime.CleanupWarning);
        }
        else if (extension == ".zip")
        {
            var scratch = Path.Combine(environment.StateDirectory, "Audit", Path.GetFileNameWithoutExtension(source) + "-" + Guid.NewGuid().ToString("N")[..8]);
            report = await Task.Run(() => StaticAuditor.AuditZip(source, scratch, token), token).ConfigureAwait(false);
            payloadFolder = scratch;
        }
        else if (extension == ".msi")
        {
            IMsiReader reader = invocation.Value("msi-fixture") is { } fixture
                ? FixtureMsiReader.FromJson(File.ReadAllText(Path.GetFullPath(fixture)))
                : new WindowsMsiReader();
            if (invocation.Has("engines"))
            {
                var availability = EngineBackend.Locate(environment.BaseDirectory);
                var scratch = Path.Combine(environment.StateDirectory, "Audit", Path.GetFileNameWithoutExtension(source) + "-" + Guid.NewGuid().ToString("N")[..8]);
                report = await StaticAuditor.AuditMsiWithEnginesAsync(source, reader, scratch, new ProcessExtractionRunner(), availability, token).ConfigureAwait(false);
                result.Lines.Add(availability.Has(ExtractionEngineKind.LessMsi)
                    ? "Static engines: lessMSI payload extraction complemented the table inventory."
                    : "Static engines: lessMSI is not prepared beside AppWrap; the table inventory stands alone.");
            }
            else
            {
                report = await Task.Run(() => StaticAuditor.AuditMsi(source, reader), token).ConfigureAwait(false);
            }
        }
        else if (invocation.Has("engines"))
        {
            var availability = EngineBackend.Locate(environment.BaseDirectory);
            var scratch = Path.Combine(environment.StateDirectory, "Audit", Path.GetFileNameWithoutExtension(source) + "-" + Guid.NewGuid().ToString("N")[..8]);
            report = await StaticAuditor.AuditExeWithEnginesAsync(source, scratch, new ProcessExtractionRunner(), availability, token).ConfigureAwait(false);
            payloadFolder = report.PayloadFolder;
            result.Lines.Add(availability.Kinds.Count > 0
                ? $"Static engines: prepared beside AppWrap: {string.Join(", ", availability.Kinds)}."
                : "Static engines: none prepared beside AppWrap; the audit states its limits instead of unpacking.");
        }
        else
        {
            report = await Task.Run(() => StaticAuditor.AuditExe(source), token).ConfigureAwait(false);
        }

        var findings = new JsonArray();
        foreach (var finding in report.Findings)
        {
            findings.Add(new JsonObject
            {
                ["kind"] = finding.Kind.ToString(), ["subject"] = finding.Subject, ["detail"] = finding.Detail,
                ["evidence"] = finding.Evidence, ["confidence"] = finding.Confidence.ToString(), ["hardWarning"] = finding.HardWarning,
            });
            result.Lines.Add($"{finding.Kind}: {finding.Subject} — {finding.Detail} [{finding.Evidence}; confidence {finding.Confidence}{(finding.HardWarning ? "; HARD WARNING" : "")}]");
        }
        result.Data = new JsonObject
        {
            ["source"] = report.SourcePath, ["sourceKind"] = report.SourceKind.ToString(),
            ["payloadFolder"] = payloadFolder ?? report.PayloadFolder, ["mainExecutable"] = report.MainExecutable,
            ["findings"] = findings, ["hardWarnings"] = report.Findings.Count(f => f.HardWarning),
            ["runtimeBackend"] = runtimeBackend,
        };
        result.Lines.Add($"Audited {report.SourceKind} source: {report.Findings.Count} finding(s), {report.Findings.Count(f => f.HardWarning)} hard warning(s).");

        if (invocation.Value("report") is { } reportPath)
        {
            var full = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, report.ToJson());
            result.Data["reportPath"] = full;
            result.Lines.Add("Report: " + full);
        }

        if (invocation.Value("draft") is { } draftPath)
        {
            var draft = AuditProfileSynthesizer.Synthesize(report);
            var diagnostics = PortableProfileValidator.Validate(draft.Profile).Select(FromProfileDiagnostic).ToList();
            result.Diagnostics.AddRange(diagnostics);
            var state = new ProfileWorkspaceState();
            state.LoadProfile(draft.Profile);
            var full = Path.GetFullPath(draftPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, state.ExportProfileText());
            var notes = new JsonArray();
            foreach (var note in draft.MappedNotes) notes.Add(note);
            result.Data["draftProfile"] = full;
            result.Data["draftNotes"] = notes;
            result.Data["draftUnmapped"] = draft.UnmappedFindings.Count;
            result.Lines.Add("Draft profile (untrusted — review and save it in the profile workspace before building): " + full);
            foreach (var note in draft.MappedNotes) result.Lines.Add("Draft: " + note);
            AddDiagnosticLines(result);
            if (diagnostics.Any(d => d.Severity == "error"))
            {
                result.ExitCode = 3;
                result.Error = "The synthesized draft profile has validation errors; fix them in the profile workspace before building.";
            }
        }
        return result;
    }

    // ----- gui -------------------------------------------------------------

    private static CliResult Gui(CliEnvironment environment)
    {
        var result = new CliResult { Command = "gui" };
        var exe = Path.Combine(environment.BaseDirectory, "AppWrap.exe");
        // Dual-mode hosting: when the CLI is executing inside AppWrap.exe
        // itself there is no sibling to launch — the GUI is this executable,
        // opened by running it without a command. The same applies whenever
        // the sibling path turns out to be the current process.
        var currentProcess = Environment.ProcessPath;
        var isCurrentProcess = currentProcess is not null
            && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(currentProcess), StringComparison.OrdinalIgnoreCase);
        if (environment.HostedInGui || isCurrentProcess)
        {
            result.Data = new JsonObject { ["hosted"] = true, ["executable"] = exe };
            result.Lines.Add("The AppWrap GUI is this executable (" + exe + "). Run AppWrap.exe without a command to open it.");
            return result;
        }
        if (!File.Exists(exe))
        {
            result.ExitCode = 4;
            result.Error = "AppWrap.exe was not found beside the CLI at " + exe + ". Run the CLI from the AppWrap distribution folder.";
            return result;
        }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        result.Data = new JsonObject { ["launched"] = exe };
        result.Lines.Add("Launched AppWrap: " + exe);
        return result;
    }

    // ----- shared ----------------------------------------------------------

    private static CliDiagnostic FromPafDiagnostic(PafDiagnostic d) => new(
        d.Severity switch { PafDiagnosticSeverity.Error => "error", PafDiagnosticSeverity.Warning => "warning", _ => "info" },
        d.Code, d.FilePath, d.Section, d.Key, d.Message, d.Hint);

    private static CliDiagnostic FromProfileDiagnostic(ProfileDiagnostic d) => new(
        d.Severity switch { ProfileSeverity.Error => "error", ProfileSeverity.Warning => "warning", _ => "info" },
        d.Code, d.DocumentPath, null, null, d.Message, null);

    private static void AddDiagnosticLines(CliResult result)
    {
        foreach (var d in result.Diagnostics)
        {
            var location = d.File ?? "";
            if (d.Section is not null) location += $" [{d.Section}] {d.Key}";
            result.Lines.Add($"{d.Severity}: {d.Code} {location} — {d.Message}" + (d.Hint is not null ? $" Hint: {d.Hint}" : ""));
        }
    }

    private static string ModeWord(OutputMode mode) => mode switch
    {
        OutputMode.Standard => "installer",
        OutputMode.PortableApps => "portable",
        _ => "paf",
    };

    private static string ModeLabel(OutputMode mode) => mode switch
    {
        OutputMode.Standard => "Installer",
        OutputMode.PortableApps => "Portable package",
        _ => "PAF package folder",
    };

    /// <summary>Reports progress synchronously, so verbose lines stay ordered with the build.</summary>
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
