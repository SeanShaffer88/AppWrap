using AppWrap.Core.TestHarness;

namespace AppWrap.Core.Audit;

/// <summary>
/// Converts a runtime observation (a test-harness report with registry
/// observation, or a conversion runtime-audit run) into audit findings in
/// the same shape the static audit produces, so both passes feed one
/// report and one draft-profile synthesis. Profile file leakage becomes
/// host data-location findings (AppData\Roaming\… / AppData\Local\…
/// relative paths), registry creations/modifications become registry-write
/// findings carrying the exact value where the snapshot recorded one.
/// </summary>
public static class RuntimeFindings
{
    public static IReadOnlyList<AuditFinding> FromTestReport(TestReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var findings = new List<AuditFinding>();
        foreach (var write in report.DetectedWrites.Where(w => w.Root == WatchedRootKind.Profile))
            findings.Add(new AuditFinding(AuditFindingKind.FileDataLocation, write.Path.Replace('/', '\\'),
                $"At runtime the app {write.Change.ToString().ToLowerInvariant()} this file under the user profile — data it keeps outside the package.",
                "runtime audit: test-harness profile snapshot diff", AuditConfidence.High));
        if (report.RegistryObserved)
            findings.AddRange(FromRegistryChanges(report.RegistryChangeList));
        return findings;
    }

    public static IReadOnlyList<AuditFinding> FromRegistryChanges(IEnumerable<DetectedRegistryChange> changes)
    {
        var findings = new List<AuditFinding>();
        foreach (var change in changes)
        {
            if (change.Change is RegistryChangeKind.KeyDeleted or RegistryChangeKind.ValueDeleted) continue;
            if (change.Category == RegistryChangeCategory.LauncherBackup) continue;
            var subject = change.ValueName is null ? change.KeyPath : change.KeyPath + "\\" + (change.ValueName.Length == 0 ? "(default)" : change.ValueName);
            findings.Add(new AuditFinding(AuditFindingKind.RegistryWrite, subject,
                $"At runtime the app {change.Change.ToString().ToLowerInvariant()} this registry value on the host.",
                "runtime audit: registry snapshot diff", AuditConfidence.High,
                RegistryKeyPath: change.KeyPath,
                RegistryValueName: change.ValueName,
                RegistryValueKind: change.ValueKind,
                RegistryValueData: change.NewData));
        }
        return findings;
    }

    /// <summary>Appends runtime findings to a static report, keeping the static report's identity and payload.</summary>
    public static AuditReport Merge(AuditReport report, IEnumerable<AuditFinding> runtimeFindings) =>
        report with { Findings = report.Findings.Concat(runtimeFindings).ToList() };
}
