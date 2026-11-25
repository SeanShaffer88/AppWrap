using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppWrap.Core.Audit;

/// <summary>The kind of a conversion source AppWrap can audit.</summary>
public enum AuditSourceKind { PortableZip, MsiPackage, ExeInstaller, ExtractedFolder }

/// <summary>
/// What one audit finding says about the source. Services and drivers are
/// always hard warnings: PAL can save/restore files and registry keys, but it
/// cannot make a service or driver portable, so a package built from such a
/// source will still touch the host in ways the launcher cannot clean up.
/// </summary>
public enum AuditFindingKind
{
    FileDataLocation,
    RegistryWrite,
    RegistryRead,
    HardcodedPath,
    Shortcut,
    ServiceOrDriver,
    StaticAuditLimited,
    PayloadFile,
    Executable,
}

/// <summary>How sure the audit is of a finding, from its evidence.</summary>
public enum AuditConfidence { Low, Medium, High }

/// <summary>
/// One typed audit finding. <see cref="Evidence"/> names the provenance —
/// a zip entry, an extracted file, an MSI table and row key, a .reg file
/// line, or a registry snapshot diff — so a reviewer can check the claim
/// instead of trusting the audit blindly.
/// </summary>
public sealed record AuditFinding(
    AuditFindingKind Kind,
    string Subject,
    string Detail,
    string Evidence,
    AuditConfidence Confidence,
    bool HardWarning = false,
    string? RegistryKeyPath = null,
    string? RegistryValueName = null,
    string? RegistryValueKind = null,
    string? RegistryValueData = null,
    bool SubjectIsDirectory = false);

/// <summary>
/// The result of a static (and optionally runtime) audit of one conversion
/// source: the inventoried payload folder (for zip/folder sources), the main
/// executable when one could be identified, and every finding in evidence
/// order. Serialized as audit-report.json by the CLI and the wizard.
/// </summary>
public sealed record AuditReport(
    int SchemaVersion,
    string SourcePath,
    AuditSourceKind SourceKind,
    string? PayloadFolder,
    string? MainExecutable,
    IReadOnlyList<AuditFinding> Findings)
{
    public const int CurrentSchemaVersion = 1;

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AuditReport FromJson(string json) =>
        JsonSerializer.Deserialize<AuditReport>(json, JsonOptions)
        ?? throw new UserInputException("The audit report could not be read.");

    public IEnumerable<AuditFinding> OfKind(AuditFindingKind kind) => Findings.Where(f => f.Kind == kind);
    public bool HasHardWarnings => Findings.Any(f => f.HardWarning);
}
