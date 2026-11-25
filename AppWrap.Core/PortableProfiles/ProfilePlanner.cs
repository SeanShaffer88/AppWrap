namespace AppWrap.Core.PortableProfiles;

public enum PlannedOperationKind
{
    LaunchProgram,
    SetEnvironment,
    MoveFile,
    MoveDirectory,
    BackupRegistryKey,
    DiscardRegistryKey,
    WriteRegistryValue,
    BackupDeleteRegistryValue,
    CleanupRegistryIfEmpty,
    ForceCleanupRegistry,
    CleanupDirectoryIfEmpty,
    ForceCleanupDirectory,
    WriteConfigFile,
    ApplyLanguageFile,
}

/// <summary>One planned runtime operation, in planned lifecycle order, with its source line and a plain-language summary.</summary>
public sealed record PlannedOperation(PlannedOperationKind Kind, int Line, string Summary, string Source);

/// <summary>
/// Immutable operation plan for a valid profile. Planning is pure configuration work:
/// nothing here touches the host. Runtime execution remains the pinned PAL engine's job,
/// so a plan is evidence of "Configuration validated", never of runtime portability.
/// </summary>
public sealed record PreparedPlan(
    IReadOnlyList<PlannedOperation> Operations,
    IReadOnlyList<string> RequiredDataParents,
    IReadOnlyList<string> HostBackupLocations,
    bool RequiresWaitForProgram,
    bool RequiresXmlCompile,
    bool RequiresAdminCompileForce);

/// <summary>
/// Builds a <see cref="PreparedPlan"/> from a profile. The P0 validator runs first;
/// planner-level cross-rule checks are added with their own codes. A plan is produced
/// only when the combined diagnostics contain no errors.
/// </summary>
public static class ProfilePlanner
{
    public static (PreparedPlan? Plan, IReadOnlyList<ProfileDiagnostic> Diagnostics) Prepare(PortableProfile profile, string appId)
    {
        var diagnostics = new List<ProfileDiagnostic>(PortableProfileValidator.Validate(profile));
        if (profile.ExecutionKind == PortableExecutionKind.PackagingOnly)
            return (new PreparedPlan([], [], [], false, false, false), diagnostics);

        var rules = ProfileRules.From(profile);
        var doc = rules.Document;

        ValidateMoveSources(diagnostics, rules);
        ValidateOverlappingMoveTargets(diagnostics, rules);
        ValidateCleanupOverlaps(diagnostics, rules, doc);
        ValidateEnvironmentCycles(diagnostics, rules);
        ValidateFileWrites(diagnostics, rules);

        if (diagnostics.Any(d => d.Severity == ProfileSeverity.Error))
            return (null, diagnostics);

        var operations = new List<PlannedOperation>();
        var backups = new List<string>();

        foreach (var env in rules.Environment)
            operations.Add(new PlannedOperation(PlannedOperationKind.SetEnvironment, env.Line,
                $"Set environment variable {env.Name} to {env.Value} for the launched program only",
                $"[Environment] {env.Name}"));

        if (rules.Language.FileFields.Count > 0)
        {
            var file = rules.Language.FileField("File") ?? "the program's configuration file";
            var line = doc.Entries("LanguageFile").FirstOrDefault()?.LineStart ?? 0;
            operations.Add(new PlannedOperation(PlannedOperationKind.ApplyLanguageFile, line,
                $"Read the program's language setting from {file} to choose the portable language",
                "[LanguageFile]"));
        }

        foreach (var key in rules.RegistryKeys)
        {
            if (key.IsDiscard)
            {
                operations.Add(new PlannedOperation(PlannedOperationKind.DiscardRegistryKey, key.Line,
                    $"Back up host registry key {key.HiveKey} and discard portable changes to it on exit",
                    $"[RegistryKeys] -"));
            }
            else
            {
                operations.Add(new PlannedOperation(PlannedOperationKind.BackupRegistryKey, key.Line,
                    $"Back up host registry key {key.HiveKey}, save portable changes to Data\\settings\\{key.StorageName}.reg, and restore the host key on exit",
                    $"[RegistryKeys] {key.StorageName}"));
            }
        }
        if (rules.RegistryKeys.Count > 0)
            backups.Add(@"HKCU\Software\PortableApps.com\Keys (host registry key backups; portable copies are stored in Data\settings)");

        foreach (var entry in ProfileRules.SequentialEntries(doc, "RegistryValueBackupDelete"))
            operations.Add(new PlannedOperation(PlannedOperationKind.BackupDeleteRegistryValue, entry.LineStart,
                $"Back up host registry value {entry.Value}, delete the value the program sets, and restore the host value on exit",
                $"[RegistryValueBackupDelete] {entry.Key}"));
        if (rules.RegistryValueBackupDelete.Count > 0)
            backups.Add(@"HKCU\Software\PortableApps.com\Values (host registry value backups)");

        foreach (var write in rules.RegistryValueWrites)
        {
            var shown = write.ValueName.Length == 0 ? write.KeyPath + @"\ (default value)" : write.KeyPath + "\\" + write.ValueName;
            operations.Add(new PlannedOperation(PlannedOperationKind.WriteRegistryValue, write.Line,
                $"Write registry value {shown} as {write.Type} for the program run",
                $"[RegistryValueWrite] {write.KeyPath}"));
        }

        foreach (var move in rules.FileMoves)
        {
            operations.Add(new PlannedOperation(PlannedOperationKind.MoveFile, move.Line,
                move.IsDiscard
                    ? $"Keep the host files in {move.HostTarget} safe and discard portable changes there"
                    : $"Save portable file {move.PortablePath} to {move.HostTarget} and restore the host copy on exit",
                $"[FilesMove] {move.PortablePath}"));
            if (!move.IsDiscard)
            {
                var fileName = move.PortablePath.Split('\\', '/')[^1];
                backups.Add($"{move.HostTarget}\\{fileName}-BackupBy{appId} (host file backup)");
            }
        }

        foreach (var move in rules.DirectoryMoves)
        {
            operations.Add(new PlannedOperation(PlannedOperationKind.MoveDirectory, move.Line,
                move.IsDiscard
                    ? $"Keep the host folder {move.HostTarget} safe and discard changes the program makes there"
                    : $"Save portable folder {move.PortablePath} to {move.HostTarget} and restore the host copy on exit",
                $"[DirectoriesMove] {move.PortablePath}"));
            backups.Add($"{move.HostTarget}-BackupBy{appId} (host folder backup)");
        }

        foreach (var write in rules.FileWrites)
        {
            var type = write.Get("Type") ?? "INI";
            var file = write.Get("File") ?? "";
            var line = doc.Entries($"FileWrite{write.Index}").FirstOrDefault()?.LineStart ?? 0;
            operations.Add(new PlannedOperation(PlannedOperationKind.WriteConfigFile, line,
                $"Update configuration file {file} ({type}) when the program launches",
                $"[FileWrite{write.Index}]"));
        }

        var launch = rules.Launch;
        var executable = launch.ProgramExecutable ?? "(no ProgramExecutable set)";
        var launchLine = doc.Entries("Launch")
            .FirstOrDefault(e => string.Equals(e.Key, "ProgramExecutable", StringComparison.OrdinalIgnoreCase))?.LineStart ?? 0;
        operations.Add(new PlannedOperation(PlannedOperationKind.LaunchProgram, launchLine,
            $"Run {executable} from the package and wait for it to exit",
            "[Launch] ProgramExecutable"));

        foreach (var entry in ProfileRules.SequentialEntries(doc, "RegistryCleanupIfEmpty"))
            operations.Add(new PlannedOperation(PlannedOperationKind.CleanupRegistryIfEmpty, entry.LineStart,
                $"Remove registry key {entry.Value} on exit if it is empty",
                $"[RegistryCleanupIfEmpty] {entry.Key}"));
        foreach (var entry in ProfileRules.SequentialEntries(doc, "RegistryCleanupForce"))
            operations.Add(new PlannedOperation(PlannedOperationKind.ForceCleanupRegistry, entry.LineStart,
                $"Remove registry key {entry.Value} on exit even if it is not empty",
                $"[RegistryCleanupForce] {entry.Key}"));
        foreach (var entry in ProfileRules.SequentialEntries(doc, "DirectoriesCleanupIfEmpty"))
            operations.Add(new PlannedOperation(PlannedOperationKind.CleanupDirectoryIfEmpty, entry.LineStart,
                $"Remove folder {entry.Value} on exit if it is empty",
                $"[DirectoriesCleanupIfEmpty] {entry.Key}"));
        foreach (var entry in ProfileRules.SequentialEntries(doc, "DirectoriesCleanupForce"))
            operations.Add(new PlannedOperation(PlannedOperationKind.ForceCleanupDirectory, entry.LineStart,
                $"Remove folder {entry.Value} on exit even if it is not empty",
                $"[DirectoriesCleanupForce] {entry.Key}"));

        var requiredParents = new List<string>();
        foreach (var move in rules.FileMoves.Concat(rules.DirectoryMoves))
        {
            if (move.IsDiscard || move.PortablePath.Length == 0) continue;
            var parent = ParentOf(move.PortablePath);
            if (parent.Length > 0 && !requiredParents.Contains(parent, StringComparer.OrdinalIgnoreCase))
                requiredParents.Add(parent);
        }

        var requiresWait = rules.FileMoves.Count > 0 || rules.DirectoryMoves.Count > 0
            || rules.RegistryKeys.Count > 0 || rules.RegistryValueWrites.Count > 0
            || rules.RegistryValueBackupDelete.Count > 0 || rules.RegistryCleanupIfEmpty.Count > 0
            || rules.RegistryCleanupForce.Count > 0 || rules.QtKeysCleanup.Count > 0
            || rules.DirectoriesCleanupIfEmpty.Count > 0 || rules.DirectoriesCleanupForce.Count > 0
            || rules.FileWrites.Count > 0;

        var requiresXml = rules.Activate.Xml == true
            || rules.FileWrites.Any(w => IsXmlType(w.Get("Type")))
            || IsXmlType(rules.Language.FileField("Type"));

        var requiresAdminCompileForce = doc.Entries("Launch").Any(e =>
            e.Key.StartsWith("RunAsAdmin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Value, "compileforce", StringComparison.OrdinalIgnoreCase));

        var plan = new PreparedPlan(
            operations,
            requiredParents,
            backups.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            requiresWait,
            requiresXml,
            requiresAdminCompileForce);
        return (plan, diagnostics);
    }

    private static bool IsXmlType(string? type) =>
        type is not null && type.TrimStart().StartsWith("XML", StringComparison.OrdinalIgnoreCase);

    private static string ParentOf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        return cut < 0 ? "" : trimmed[..cut];
    }

    private static string NormalizePath(string path) => path.Trim().TrimEnd('\\', '/');

    private static bool IsSameOrAncestor(string ancestor, string path) =>
        string.Equals(ancestor, path, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(ancestor + "\\", StringComparison.OrdinalIgnoreCase);

    private static void ValidateMoveSources(List<ProfileDiagnostic> diagnostics, LauncherRules rules)
    {
        foreach (var move in rules.DirectoryMoves)
        {
            if (move.IsDiscard || move.PortablePath.Length == 0) continue;
            var first = move.PortablePath.TrimStart('\\', '/').Split('\\', '/')[0];
            if (string.Equals(first, "settings", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(new ProfileDiagnostic("MoveSourceIsSettingsDir",
                    $"Launcher/[DirectoriesMove] {move.PortablePath} line {move.Line}", ProfileSeverity.Error, CapabilityStatus.Expert,
                    "DirectoriesMove cannot move Data\\settings: PAL stores registry data there, and moving it loses data. Choose a different Data folder for this rule."));
        }
    }

    private static void ValidateOverlappingMoveTargets(List<ProfileDiagnostic> diagnostics, LauncherRules rules)
    {
        var moves = rules.FileMoves.Concat(rules.DirectoryMoves)
            .Where(m => m.HostTarget.Trim().Length > 0).ToList();
        for (var i = 0; i < moves.Count; i++)
        {
            for (var j = i + 1; j < moves.Count; j++)
            {
                var a = NormalizePath(moves[i].HostTarget);
                var b = NormalizePath(moves[j].HostTarget);
                if (IsSameOrAncestor(a, b) || IsSameOrAncestor(b, a))
                    diagnostics.Add(new ProfileDiagnostic("OverlappingMoveTargets",
                        $"Launcher/[{moves[j].Section}] line {moves[j].Line}", ProfileSeverity.Error, CapabilityStatus.Supported,
                        $"Move targets '{moves[i].HostTarget}' and '{moves[j].HostTarget}' overlap: one contains the other, so backup and restore would collide."));
            }
        }
    }

    private static void ValidateCleanupOverlaps(List<ProfileDiagnostic> diagnostics, LauncherRules rules, IniDocument doc)
    {
        foreach (var entry in ProfileRules.SequentialEntries(doc, "DirectoriesCleanupForce"))
        {
            var target = NormalizePath(entry.Value);
            if (target.Length == 0) continue;
            foreach (var move in rules.FileMoves.Concat(rules.DirectoryMoves))
            {
                var host = NormalizePath(move.HostTarget);
                if (host.Length > 0 && (IsSameOrAncestor(target, host) || IsSameOrAncestor(host, target)))
                    diagnostics.Add(new ProfileDiagnostic("CleanupOverlapsRestore",
                        $"Launcher/[DirectoriesCleanupForce] {entry.Key} line {entry.LineStart}", ProfileSeverity.Error, CapabilityStatus.Expert,
                        $"Force cleanup of '{entry.Value}' overlaps the move target '{move.HostTarget}', whose host data must be restored on exit."));
            }
        }
        foreach (var entry in ProfileRules.SequentialEntries(doc, "RegistryCleanupForce"))
        {
            var target = NormalizePath(entry.Value);
            if (target.Length == 0) continue;
            foreach (var key in rules.RegistryKeys)
            {
                var host = NormalizePath(key.HiveKey);
                if (host.Length > 0 && (IsSameOrAncestor(target, host) || IsSameOrAncestor(host, target)))
                    diagnostics.Add(new ProfileDiagnostic("CleanupOverlapsRestore",
                        $"Launcher/[RegistryCleanupForce] {entry.Key} line {entry.LineStart}", ProfileSeverity.Error, CapabilityStatus.Expert,
                        $"Force cleanup of '{entry.Value}' overlaps registry key '{key.HiveKey}', whose host data must be restored on exit."));
            }
            foreach (var write in rules.RegistryValueWrites)
            {
                var host = NormalizePath(write.KeyPath);
                if (host.Length > 0 && (IsSameOrAncestor(target, host) || IsSameOrAncestor(host, target)))
                    diagnostics.Add(new ProfileDiagnostic("CleanupOverlapsRestore",
                        $"Launcher/[RegistryCleanupForce] {entry.Key} line {entry.LineStart}", ProfileSeverity.Error, CapabilityStatus.Expert,
                        $"Force cleanup of '{entry.Value}' overlaps registry key '{write.KeyPath}', which a value write depends on."));
            }
        }
    }

    private static void ValidateEnvironmentCycles(List<ProfileDiagnostic> diagnostics, LauncherRules rules)
    {
        var names = new HashSet<string>(rules.Environment.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var env in rules.Environment)
        {
            var targets = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(env.Value, @"%([^%\s][^%]*)%"))
            {
                var token = m.Groups[1].Value;
                // A self-reference (e.g. PATH=%PATH%;…) expands the pre-existing host
                // value in PAL semantics; it is not a cycle.
                if (names.Contains(token) && !string.Equals(token, env.Name, StringComparison.OrdinalIgnoreCase)
                    && !targets.Contains(token, StringComparer.OrdinalIgnoreCase))
                    targets.Add(token);
            }
            edges[env.Name] = targets;
        }

        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        string? cycle = null;

        bool Visit(string name)
        {
            state[name] = 1;
            stack.Add(name);
            foreach (var next in edges.GetValueOrDefault(name) ?? [])
            {
                if (cycle is not null) return true;
                if (!state.TryGetValue(next, out var s))
                {
                    if (Visit(next)) return true;
                }
                else if (s == 1)
                {
                    var start = stack.FindIndex(x => string.Equals(x, next, StringComparison.OrdinalIgnoreCase));
                    cycle = string.Join(" -> ", stack.Skip(start).Append(next));
                    return true;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            state[name] = 2;
            return false;
        }

        foreach (var env in rules.Environment)
        {
            if (cycle is not null) break;
            if (!state.ContainsKey(env.Name)) Visit(env.Name);
        }

        if (cycle is not null)
        {
            var line = rules.Environment.FirstOrDefault()?.Line ?? 0;
            diagnostics.Add(new ProfileDiagnostic("EnvironmentCycle",
                $"Launcher/[Environment] line {line}", ProfileSeverity.Error, CapabilityStatus.Supported,
                $"Environment variables reference each other in a cycle ({cycle}); PAL expands values in order, so the cycle can never resolve."));
        }
    }

    private static void ValidateFileWrites(List<ProfileDiagnostic> diagnostics, LauncherRules rules)
    {
        foreach (var write in rules.FileWrites)
        {
            var line = rules.Document.Entries($"FileWrite{write.Index}").FirstOrDefault()?.LineStart ?? 0;
            var file = write.Get("File");
            if (string.IsNullOrWhiteSpace(file))
                diagnostics.Add(new ProfileDiagnostic("FileWriteMissingFile",
                    $"Launcher/[FileWrite{write.Index}] line {line}", ProfileSeverity.Error, CapabilityStatus.Supported,
                    $"FileWrite{write.Index} has no File field; PAL would have no file to update, so the rule cannot be planned."));
            var type = write.Get("Type");
            if (IsXmlType(type) && rules.Activate.Xml != true)
                diagnostics.Add(new ProfileDiagnostic("FileWriteXmlNeedsActivate",
                    $"Launcher/[FileWrite{write.Index}] line {line}", ProfileSeverity.Error, CapabilityStatus.Supported,
                    $"FileWrite{write.Index} uses Type '{type}', which needs the XML plugin: set [Activate] XML=true so the launcher is compiled with XML support."));
        }
        var languageType = rules.Language.FileField("Type");
        if (rules.Language.FileFields.Count > 0 && IsXmlType(languageType) && rules.Activate.Xml != true)
        {
            var line = rules.Document.Entries("LanguageFile").FirstOrDefault()?.LineStart ?? 0;
            diagnostics.Add(new ProfileDiagnostic("LanguageFileXmlNeedsActivate",
                $"Launcher/[LanguageFile] line {line}", ProfileSeverity.Error, CapabilityStatus.Supported,
                $"LanguageFile uses Type '{languageType}', which needs the XML plugin: set [Activate] XML=true so the launcher is compiled with XML support."));
        }
    }
}
