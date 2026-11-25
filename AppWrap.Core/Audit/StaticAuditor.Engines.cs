using AppWrap.Core.Pipeline;

namespace AppWrap.Core.Audit;

/// <summary>
/// Stage 1 of the pipeline: the static audit with prepared extraction
/// engines. When a fitting engine is prepared (7-Zip, innoextract,
/// lessMSI — see <see cref="ExtractionEngineLock"/>), an installer exe's
/// payload is extracted into scratch and inventoried with the ordinary
/// folder classification, and an MSI's payload extraction complements
/// its table inventory. When no engine fits, or the engine cannot open
/// the source, the result is exactly the pre-engine behavior: family
/// identification plus an explicit <see cref="AuditFindingKind.StaticAuditLimited"/>
/// finding. A missing engine is never an error.
/// </summary>
public static partial class StaticAuditor
{
    /// <summary>Static-audits an installer .exe, extracting its payload with a prepared engine when one fits.</summary>
    public static async Task<AuditReport> AuditExeWithEnginesAsync(
        string exePath, string scratchDirectory, IExtractionRunner runner, EngineAvailability availability, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(availability);
        InputRules.SafePath(exePath);
        if (!File.Exists(exePath)) throw new UserInputException("Choose an installer .exe file that still exists.");
        var family = DetectExeFamily(exePath);
        var plan = EnginePlanner.PlanExe(family, availability.Kinds);
        if (!plan.HasEngine) return AuditExe(exePath);
        var pin = ExtractionEngineLock.Pin(plan.Engine!.Value);
        var payloadDir = Path.Combine(Path.GetFullPath(scratchDirectory), "payload");
        Directory.CreateDirectory(payloadDir);
        var extracted = await runner.ExtractAsync(plan.Engine.Value, availability.PathFor(plan.Engine.Value)!, exePath, payloadDir, token).ConfigureAwait(false);
        if (!extracted || !Directory.EnumerateFileSystemEntries(payloadDir).Any())
        {
            var fallback = AuditExe(exePath);
            var findings = new List<AuditFinding>(fallback.Findings)
            {
                new(AuditFindingKind.StaticAuditLimited, Path.GetFileName(exePath),
                    $"{pin.Kind} {pin.Version} was prepared but could not extract this installer (an unsupported variant or a corrupt file), so the payload stays unobserved statically.",
                    "extraction engine attempt", AuditConfidence.High),
            };
            return fallback with { Findings = findings };
        }
        var report = AuditFolder(payloadDir, exePath, AuditSourceKind.ExeInstaller, token);
        var merged = new List<AuditFinding>
        {
            new(AuditFindingKind.PayloadFile, Path.GetFileName(exePath),
                $"The installer payload was extracted without running it, using {pin.Kind} {pin.Version} (pinned engine), and inventoried below.",
                $"extraction engine: {pin.Kind} {pin.Version}", AuditConfidence.High),
        };
        merged.AddRange(report.Findings);
        return report with { Findings = merged };
    }

    /// <summary>Static-audits an .msi: the table inventory, complemented by a lessMSI payload extraction when prepared.</summary>
    public static async Task<AuditReport> AuditMsiWithEnginesAsync(
        string msiPath, IMsiReader reader, string scratchDirectory, IExtractionRunner runner, EngineAvailability availability, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(availability);
        var report = AuditMsi(msiPath, reader);
        var plan = EnginePlanner.PlanMsi(availability.Kinds);
        if (!plan.HasEngine) return report;
        var pin = ExtractionEngineLock.Pin(plan.Engine!.Value);
        var payloadDir = Path.Combine(Path.GetFullPath(scratchDirectory), "msi-payload");
        Directory.CreateDirectory(payloadDir);
        var extracted = await runner.ExtractAsync(plan.Engine.Value, availability.PathFor(plan.Engine.Value)!, msiPath, payloadDir, token).ConfigureAwait(false);
        if (!extracted || !Directory.EnumerateFileSystemEntries(payloadDir).Any()) return report;
        var inventory = AuditFolder(payloadDir, msiPath, AuditSourceKind.MsiPackage, token);
        var findings = new List<AuditFinding>(report.Findings)
        {
            new(AuditFindingKind.PayloadFile, Path.GetFileName(msiPath),
                $"The MSI payload files were also extracted with {pin.Kind} {pin.Version} (pinned engine); payload-side findings from that inventory follow.",
                $"extraction engine: {pin.Kind} {pin.Version}", AuditConfidence.High),
        };
        findings.AddRange(inventory.Findings);
        return report with { Findings = findings, PayloadFolder = payloadDir };
    }
}
