using AppWrap.Core.Audit;

namespace AppWrap.Core.Pipeline;

/// <summary>One planned extraction: run this engine over the source into the scratch payload folder.</summary>
public sealed record ExtractionStep(ExtractionEngineKind Engine, string Purpose);

/// <summary>
/// The stage-1 plan for one source: the engine to run, or — when no
/// prepared engine fits — a null engine plus the limitation to record,
/// exactly the honest-degradation rule the static audit already follows.
/// </summary>
public sealed record ExtractionPlan(ExtractionEngineKind? Engine, IReadOnlyList<ExtractionStep> Steps, string? Limitation)
{
    public bool HasEngine => Engine is not null;
}

/// <summary>
/// Decides which prepared extraction engine (if any) applies to a source.
/// The engines are complements, not replacements: 7-Zip opens NSIS and
/// archive-shaped payloads, innoextract unpacks Inno Setup (7-Zip cannot),
/// and lessMSI extracts an MSI's payload alongside the table reader.
/// </summary>
public static class EnginePlanner
{
    public static ExtractionPlan PlanExe(StaticAuditor.InstallerFamily family, IReadOnlyCollection<ExtractionEngineKind> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return family switch
        {
            StaticAuditor.InstallerFamily.InnoSetup => available.Contains(ExtractionEngineKind.InnoExtract)
                ? new(ExtractionEngineKind.InnoExtract, [new(ExtractionEngineKind.InnoExtract, "Unpack the Inno Setup payload without running it.")], null)
                : new(null, [], "innoextract is not prepared, so this Inno Setup payload cannot be unpacked statically."),
            StaticAuditor.InstallerFamily.Nsis => available.Contains(ExtractionEngineKind.SevenZip)
                ? new(ExtractionEngineKind.SevenZip, [new(ExtractionEngineKind.SevenZip, "Extract the NSIS payload archive without running it.")], null)
                : new(null, [], "7-Zip is not prepared, so this NSIS payload cannot be unpacked statically."),
            StaticAuditor.InstallerFamily.MsiWrapper => available.Contains(ExtractionEngineKind.SevenZip)
                ? new(ExtractionEngineKind.SevenZip, [new(ExtractionEngineKind.SevenZip, "Open the wrapper archive to reach the embedded MSI payload.")], null)
                : new(null, [], "7-Zip is not prepared, so this MSI wrapper cannot be opened statically."),
            _ => new(null, [], "The installer family is unknown, so no extraction engine applies."),
        };
    }

    public static ExtractionPlan PlanMsi(IReadOnlyCollection<ExtractionEngineKind> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return available.Contains(ExtractionEngineKind.LessMsi)
            ? new(ExtractionEngineKind.LessMsi, [new(ExtractionEngineKind.LessMsi, "Extract the MSI payload files to complement the table inventory.")], null)
            : new(null, [], "lessMSI is not prepared; the MSI table inventory stands alone.");
    }
}

/// <summary>
/// The engine execution seam. The production implementation
/// (AppWrap.Build) runs the prepared engine process; checks substitute a
/// fake that materializes a fixture payload. Returns false when the
/// engine ran but could not extract the source (unsupported variant,
/// corrupt file) — the caller degrades to the stated audit limit.
/// </summary>
public interface IExtractionRunner
{
    Task<bool> ExtractAsync(ExtractionEngineKind engine, string executablePath, string sourcePath, string destinationDirectory, CancellationToken token);
}

/// <summary>Which prepared engines exist on this machine, and where their executables are.</summary>
public sealed record EngineAvailability(IReadOnlyDictionary<ExtractionEngineKind, string> Executables)
{
    public static EngineAvailability None { get; } = new(new Dictionary<ExtractionEngineKind, string>());
    public IReadOnlyCollection<ExtractionEngineKind> Kinds => [.. Executables.Keys];
    public bool Has(ExtractionEngineKind kind) => Executables.ContainsKey(kind);
    public string? PathFor(ExtractionEngineKind kind) => Executables.TryGetValue(kind, out var path) ? path : null;
}
