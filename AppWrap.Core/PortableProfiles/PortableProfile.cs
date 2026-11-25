namespace AppWrap.Core.PortableProfiles;

public enum PortableExecutionKind { PackagingOnly, Pal }

/// <summary>Exact pinned engine identity for profile (PAL) packages.</summary>
public sealed record EngineLock(string PalVersion, string PalSha256, string NsisVersion, string NsisSha256)
{
    public static EngineLock Pinned { get; } = new(
        "2.2.9",
        "7bc93e47b885d5e953fa2082a2e65169ae98128026ac4d747f774a23bd01345b",
        "2.46.5-Unicode",
        "47cd625d2d90f0cfdff411ba18c39c88ab7e9b28730fc81e5e14ed66ead6fb21");
}

public sealed record ProfileAsset(string RelativePath, string Sha256, string Purpose);

/// <summary>
/// A trusted source extension. <see cref="Trusted"/> defaults to false and is only ever set
/// by an explicit in-product user action; the codec never sets it from file content.
/// </summary>
public sealed record TrustedExtension(string RelativePath, string Sha256, string Purpose, bool Trusted = false);

/// <summary>Versioned typed portable profile (schema 1). P0 is configuration-only: no PAL compilation.</summary>
public sealed record PortableProfile(
    int SchemaVersion,
    PortableExecutionKind ExecutionKind,
    EngineLock Engine,
    IniDocument Launcher,
    IniDocument Installer,
    IniDocument? Metadata,
    IniDocument? UserOverrides,
    IReadOnlyList<ProfileAsset> Assets,
    IReadOnlyList<TrustedExtension> TrustedExtensions,
    bool ForceCleanupAcknowledged)
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Existing packaging-only behaviour: empty documents, no assets or extensions.</summary>
    public static PortableProfile PackagingOnly() => new(
        CurrentSchemaVersion,
        PortableExecutionKind.PackagingOnly,
        EngineLock.Pinned,
        IniDocument.Parse(""),
        IniDocument.Parse(""),
        null,
        null,
        [],
        [],
        false);
}
