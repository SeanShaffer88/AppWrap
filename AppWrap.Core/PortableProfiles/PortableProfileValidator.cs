namespace AppWrap.Core.PortableProfiles;

public enum ProfileSeverity { Information, Warning, Error }

public sealed record ProfileDiagnostic(string Code, string DocumentPath, ProfileSeverity Severity, CapabilityStatus Status, string Message);

/// <summary>Strict P0 validation of the launcher document against the pinned capability catalog. Configuration-only: no host access.</summary>
public static class PortableProfileValidator
{
    private static readonly string[] CleanupDependentSections =
        ["RegistryKeys", "RegistryValueWrite", "RegistryValueBackupDelete", "RegistryCleanupIfEmpty", "RegistryCleanupForce",
         "QtKeysCleanup", "FilesMove", "DirectoriesMove", "DirectoriesCleanupIfEmpty", "DirectoriesCleanupForce"];

    private static string PathFor(IniEntryNode e) => $"Launcher/[{e.Section}] {e.Key} line {e.LineStart}";
    private static string PathFor(IniSectionNode s) => $"Launcher/[{s.Name}] line {s.LineStart}";

    public static IReadOnlyList<ProfileDiagnostic> Validate(PortableProfile profile)
    {
        var result = new List<ProfileDiagnostic>();
        if (profile.ExecutionKind == PortableExecutionKind.PackagingOnly) return result;
        var doc = profile.Launcher;
        if (doc is null) return result;

        foreach (var issue in doc.ParseIssues)
            result.Add(new ProfileDiagnostic("ControlCharacters", $"Launcher line {issue.Line}", ProfileSeverity.Error, CapabilityStatus.Unrecognized, issue.Message));

        // Section-level checks.
        var seenSections = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in doc.Sections)
        {
            if (!seenSections.Add(section.Name)) continue;
            if (!CapabilityCatalog.IsKnownSection(section.Name))
            {
                result.Add(new ProfileDiagnostic("UnknownSection", PathFor(section), ProfileSeverity.Error, CapabilityStatus.Unrecognized,
                    $"Section [{section.Name}] is not part of the pinned launcher surface."));
            }
            else if (CapabilityCatalog.EnumeratedSections.Any(c => string.Equals(c, section.Name, StringComparison.OrdinalIgnoreCase))
                     && CapabilityCatalog.TryGetCanonicalSection(section.Name, out var canonical)
                     && !string.Equals(canonical, section.Name, StringComparison.Ordinal))
            {
                result.Add(new ProfileDiagnostic("SectionCase", PathFor(section), ProfileSeverity.Warning, CapabilityCatalog.Classify(section.Name, "1") == CapabilityStatus.Unrecognized ? CapabilityStatus.Supported : CapabilityCatalog.Classify(section.Name, "1"),
                    $"Section [{section.Name}] differs in case from canonical [{canonical}]; PAL enumerates raw section headers case-sensitively."));
            }
            else if (CapabilityCatalog.IsPatternSection(section.Name, out var prefix, out var n)
                     && !string.Equals(section.Name, prefix + n.ToString(), StringComparison.Ordinal))
            {
                result.Add(new ProfileDiagnostic("SectionCase", PathFor(section), ProfileSeverity.Warning, CapabilityStatus.Supported,
                    $"Section [{section.Name}] differs in case from canonical [{prefix}{n}]; PAL enumerates raw section headers case-sensitively."));
            }
        }

        var entries = doc.AllEntries.ToList();

        // Per-entry checks.
        var seenSingletons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (e.Section is null) continue;
            var knownSection = CapabilityCatalog.IsKnownSection(e.Section);
            var status = CapabilityCatalog.Classify(e.Section, e.Key);
            if (knownSection && status == CapabilityStatus.Unrecognized)
                result.Add(new ProfileDiagnostic("UnknownKey", PathFor(e), ProfileSeverity.Error, status,
                    $"Key '{e.Key}' in [{e.Section}] is not recognized by the pinned launcher."));
            if (status == CapabilityStatus.Deprecated)
                result.Add(new ProfileDiagnostic("DeprecatedKey", PathFor(e), ProfileSeverity.Warning, status,
                    $"Key '{e.Key}' in [{e.Section}] is deprecated."));
            if (status == CapabilityStatus.Unsupported)
                result.Add(new ProfileDiagnostic("UnsupportedKey", PathFor(e), ProfileSeverity.Error, status,
                    $"Key '{e.Key}' in [{e.Section}] is disabled/unsupported in the pinned engine."));
            if (CapabilityCatalog.IsSingleton(e.Section, e.Key) && !seenSingletons.Add(e.Section + "\0" + e.Key))
                result.Add(new ProfileDiagnostic("DuplicateSingleton", PathFor(e), ProfileSeverity.Error, status,
                    $"Singleton key '{e.Key}' in [{e.Section}] appears more than once."));
        }

        ValidateSequential(result, doc, entries);
        ValidateRegistryValueWriteCoverage(result, entries);
        ValidateWaitForProgram(result, doc, entries);
        if (!profile.ForceCleanupAcknowledged)
        {
            foreach (var name in new[] { "RegistryCleanupForce", "DirectoriesCleanupForce" })
                foreach (var e in entries.Where(x => string.Equals(x.Section, name, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new ProfileDiagnostic("ForceCleanupUnacknowledged", PathFor(e), ProfileSeverity.Error, CapabilityCatalog.Classify(name, e.Key),
                        $"Force cleanup in [{name}] requires explicit acknowledgment in the profile."));
        }
        ValidateBroadRegistryTargets(result, entries);
        ValidateBroadDirectoryTargets(result, entries);
        ValidateTokens(result, doc, entries);
        return result;
    }

    private static void ValidateSequential(List<ProfileDiagnostic> result, IniDocument doc, List<IniEntryNode> entries)
    {
        foreach (var group in entries.Where(e => e.Section is not null && CapabilityCatalog.IsSequentialSection(e.Section))
                                     .GroupBy(e => e.Section!, StringComparer.OrdinalIgnoreCase))
        {
            var numbers = group.Select(e => int.TryParse(e.Key, out var n) ? n : -1).Where(n => n >= 1).Distinct().OrderBy(n => n).ToList();
            AddGapDiagnostics(result, numbers, $"Launcher/[{group.Key}]", "sequential keys");
        }
        // WaitForEXE1..N key suffixes inside [Launch].
        var waitNumbers = entries.Where(e => string.Equals(e.Section, "Launch", StringComparison.OrdinalIgnoreCase) && CapabilityCatalog.IsWaitForExeKey(e.Key))
            .Select(e => int.Parse(e.Key.AsSpan("WaitForEXE".Length))).Distinct().OrderBy(n => n).ToList();
        AddGapDiagnostics(result, waitNumbers, "Launcher/[Launch] WaitForEXE", "WaitForEXE keys");
        // FileWrite1..N sections.
        var fileWriteNumbers = doc.Sections.Where(s => CapabilityCatalog.IsPatternSection(s.Name, out var p, out _) && p == "FileWrite")
            .Select(s => { CapabilityCatalog.IsPatternSection(s.Name, out _, out var n); return n; }).Distinct().OrderBy(n => n).ToList();
        AddGapDiagnostics(result, fileWriteNumbers, "Launcher/[FileWriteN]", "FileWrite sections");
    }

    private static void AddGapDiagnostics(List<ProfileDiagnostic> result, List<int> numbers, string path, string label)
    {
        if (numbers.Count == 0) return;
        for (var expected = 1; expected <= numbers[^1]; expected++)
        {
            if (!numbers.Contains(expected))
                result.Add(new ProfileDiagnostic("SequentialGap", path, ProfileSeverity.Error, CapabilityStatus.Supported,
                    $"Sequential {label} are missing {expected}; PAL stops reading at the first gap."));
        }
    }

    private static void ValidateRegistryValueWriteCoverage(List<ProfileDiagnostic> result, List<IniEntryNode> entries)
    {
        var writes = entries.Where(e => string.Equals(e.Section, "RegistryValueWrite", StringComparison.OrdinalIgnoreCase)).ToList();
        if (writes.Count == 0) return;
        var coveringKeys = entries.Where(e => string.Equals(e.Section, "RegistryKeys", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value).Where(v => v.Length > 0).ToList();
        var backupDeletes = entries.Where(e => string.Equals(e.Section, "RegistryValueBackupDelete", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value).ToList();
        foreach (var write in writes)
        {
            var target = write.Key;
            var coveredByKey = coveringKeys.Any(k => target.Equals(k, StringComparison.OrdinalIgnoreCase) || target.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase));
            var coveredByBackup = backupDeletes.Any(b => string.Equals(b, target, StringComparison.OrdinalIgnoreCase));
            if (!coveredByKey && !coveredByBackup)
                result.Add(new ProfileDiagnostic("RegistryValueWriteUncovered", PathFor(write), ProfileSeverity.Error, CapabilityStatus.Expert,
                    $"RegistryValueWrite '{target}' has no covering [RegistryKeys] rule or [RegistryValueBackupDelete] entry; the prior host value would not be preserved."));
        }
    }

    private static void ValidateWaitForProgram(List<ProfileDiagnostic> result, IniDocument doc, List<IniEntryNode> entries)
    {
        var wait = doc.GetValue("Launch", "WaitForProgram");
        if (wait is null || !string.Equals(wait, "false", StringComparison.OrdinalIgnoreCase)) return;
        var hasDependent = entries.Any(e => e.Section is not null &&
            (CleanupDependentSections.Any(s => string.Equals(s, e.Section, StringComparison.OrdinalIgnoreCase))
             || CapabilityCatalog.IsPatternSection(e.Section, out var p, out _) && p == "FileWrite"));
        if (hasDependent)
        {
            var entry = entries.First(e => string.Equals(e.Section, "Launch", StringComparison.OrdinalIgnoreCase) && string.Equals(e.Key, "WaitForProgram", StringComparison.OrdinalIgnoreCase));
            result.Add(new ProfileDiagnostic("WaitForProgramUnsafe", PathFor(entry), ProfileSeverity.Error, CapabilityStatus.Supported,
                "WaitForProgram=false while move/registry/cleanup rules exist; the launcher must stay alive until cleanup completes."));
        }
    }

    private static void ValidateBroadRegistryTargets(List<ProfileDiagnostic> result, List<IniEntryNode> entries)
    {
        foreach (var e in entries.Where(x => string.Equals(x.Section, "RegistryKeys", StringComparison.OrdinalIgnoreCase)))
        {
            var value = e.Value.Trim();
            if (value.Length == 0) continue;
            var parts = value.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            bool broad;
            if (parts.Length == 0) broad = true;
            else if (IsHive(parts[0]))
            {
                var rest = parts.Skip(1).ToArray();
                broad = rest.Length == 0 || (rest.Length == 1 && (rest[0].Equals("Software", StringComparison.OrdinalIgnoreCase) || rest[0].Equals("Classes", StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                broad = parts.Length == 1 && (parts[0].Equals("Software", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("Classes", StringComparison.OrdinalIgnoreCase));
            }
            if (broad)
                result.Add(new ProfileDiagnostic("BroadRegistryTarget", PathFor(e), ProfileSeverity.Error, CapabilityStatus.Expert,
                    $"RegistryKeys target '{value}' is a whole hive or a Software/Classes root with no app-specific subkey."));
        }
    }

    private static bool IsHive(string part) => part.ToUpperInvariant() is "HKCU" or "HKLM" or "HKCR" or "HKU" or "HKCC"
        or "HKEY_CURRENT_USER" or "HKEY_LOCAL_MACHINE" or "HKEY_CLASSES_ROOT" or "HKEY_USERS" or "HKEY_CURRENT_CONFIG";

    private static void ValidateBroadDirectoryTargets(List<ProfileDiagnostic> result, List<IniEntryNode> entries)
    {
        foreach (var e in entries.Where(x => string.Equals(x.Section, "DirectoriesCleanupForce", StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(x.Section, "DirectoriesMove", StringComparison.OrdinalIgnoreCase)))
        {
            var target = e.Value.Trim();
            var broad = false;
            if (target.Length >= 3 && char.IsAsciiLetter(target[0]) && target[1] == ':' && (target.Length == 2 || target[2] is '\\' or '/') && target.TrimEnd('\\', '/').Length == 2)
                broad = true;
            else if (System.Text.RegularExpressions.Regex.IsMatch(target, @"^%[^%]+%[\\/]?$"))
                broad = true;
            else if (target is "\\" or "/")
                broad = true;
            if (broad)
                result.Add(new ProfileDiagnostic("BroadDirectoryTarget", PathFor(e), ProfileSeverity.Error, CapabilityStatus.Expert,
                    $"Directory target '{target}' is a bare token or drive root with no app-specific suffix."));
        }
    }

    private static void ValidateTokens(List<ProfileDiagnostic> result, IniDocument doc, List<IniEntryNode> entries)
    {
        var envDefinitions = entries.Where(e => string.Equals(e.Section, "Environment", StringComparison.OrdinalIgnoreCase))
            .Select(e => (Name: e.Key, e.LineStart)).ToList();
        foreach (var e in entries)
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(e.Value, @"%([^%\s][^%]*)%"))
            {
                var token = m.Groups[1].Value;
                if (CapabilityCatalog.DocumentedTokens.Contains(token)) continue;
                var defined = envDefinitions.FirstOrDefault(d => string.Equals(d.Name, token, StringComparison.OrdinalIgnoreCase));
                if (defined.Name is not null && defined.LineStart < e.LineStart) continue;
                var forward = defined.Name is not null;
                result.Add(new ProfileDiagnostic("UnknownToken", PathFor(e), ProfileSeverity.Warning, CapabilityStatus.Unrecognized,
                    forward
                        ? $"Token '%{token}%' is a forward reference: it is only defined later in [Environment], and PAL expands values in order."
                        : $"Token '%{token}%' is not a documented token and is not defined earlier in [Environment]."));
            }
        }
    }
}
