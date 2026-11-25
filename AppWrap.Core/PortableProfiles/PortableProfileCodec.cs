namespace AppWrap.Core.PortableProfiles;

/// <summary>
/// Parses/imports and serializes portable profiles. Import is inert data only: no scripts
/// execute and no trust is granted — in particular a <c>trusted:true</c> string in imported
/// text never produces a <see cref="TrustedExtension"/> with <c>Trusted == true</c>;
/// imported profiles carry no trusted extensions at all.
/// </summary>
public static class PortableProfileCodec
{
    public static PortableProfile FromIniTexts(string? launcher, string? installer, string? metadata, string? userOverrides, PortableExecutionKind kind) => new(
        PortableProfile.CurrentSchemaVersion,
        kind,
        EngineLock.Pinned,
        IniDocument.Parse(launcher ?? ""),
        IniDocument.Parse(installer ?? ""),
        metadata is null ? null : IniDocument.Parse(metadata),
        userOverrides is null ? null : IniDocument.Parse(userOverrides),
        [],
        [],
        false);

    public static string LauncherText(PortableProfile profile) => profile.Launcher?.Serialize() ?? "";
    public static string InstallerText(PortableProfile profile) => profile.Installer?.Serialize() ?? "";
    public static string? MetadataText(PortableProfile profile) => profile.Metadata?.Serialize();
    public static string? UserOverridesText(PortableProfile profile) => profile.UserOverrides?.Serialize();
}
