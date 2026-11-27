namespace AppWrap.Core.Pipeline;

/// <summary>An optional extraction engine AppWrap can prepare as a pinned backend.</summary>
public enum ExtractionEngineKind { SevenZip, InnoExtract, LessMsi }

/// <summary>
/// The pinned Sandboxie-Plus release the AppWrap installer bootstraps and
/// the sandboxed profiler expects. Mirrors the PAL EngineLock
/// (AppWrap.Core.PortableProfiles.EngineLock): one lock, in code, with
/// the download provenance stated honestly.
///
/// Pinned release: Sandboxie-Plus 1.18.8 (Classic core 5.73.8), released
/// 2026-10-17, from the official sandboxie-plus/Sandboxie GitHub releases
/// (tag v1.18.8). AppWrap supports Windows x64 only and pins the x64 asset.
/// The installer is Inno Setup; its silent form is the standard Inno one
/// below.
///
/// Hash status: the x64 SHA-256 recorded here is the value multiple
/// independent mirrors compute for the file served from the official
/// release, but the Sandboxie project does not publish hashes on a source
/// this lock could cite, so <see cref="SandboxieAsset.Sha256Verified"/>
/// is false and the value is Unverified (see docs/PIPELINE.md). The
/// bootstrap still compares the download against the pinned value and
/// refuses a mismatch.
/// </summary>
public sealed record SandboxieAsset(string Architecture, string FileName, string DownloadUrl, string? Sha256, bool Sha256Verified);

/// <summary>The Sandboxie-Plus pin: version floor, per-architecture installers, detection and silent-install facts.</summary>
public sealed record SandboxieLock(
    string Version,
    string ReleaseTag,
    string ReleasePageUrl,
    IReadOnlyList<SandboxieAsset> Assets,
    string SilentArguments,
    string UninstallKeyPath,
    string ServiceKeyPath,
    string DriverServiceName,
    string DefaultInstallDirectory)
{
    public static SandboxieLock Pinned { get; } = new(
        "1.18.8",
        "v1.18.8",
        "https://github.com/sandboxie-plus/Sandboxie/releases/tag/v1.18.8",
        [
            new SandboxieAsset("x64", "Sandboxie-Plus-x64-v1.18.8.exe",
                "https://github.com/sandboxie-plus/Sandboxie/releases/download/v1.18.8/Sandboxie-Plus-x64-v1.18.8.exe",
                "f27078ff74a07906fd51476079e1a7a8f5e20c8bb75a9d2ae86169d47179ae76", Sha256Verified: false),
        ],
        "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Sandboxie-Plus_is1",
        @"HKLM\SYSTEM\CurrentControlSet\Services\SbieSvc",
        "SbieDrv",
        @"C:\Program Files\Sandboxie-Plus");

    /// <summary>The minimum version the profiler accepts; older installs are upgraded to the pin.</summary>
    public string MinimumVersion => Version;

    public SandboxieAsset? AssetFor(string architecture) =>
        Assets.FirstOrDefault(a => string.Equals(a.Architecture, architecture, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One pinned extraction-engine release: where the official archive lives,
/// what it is called, the executable inside it (backend-relative), and its
/// hash status. A null <see cref="Sha256"/> means no SHA-256 could be
/// verified from an official source — the pin is version + URL only and
/// the hash is Unverified (see docs/PIPELINE.md); the prepare script then
/// records the hash it actually saw into the backend provenance instead of
/// the lock pretending to know it.
/// </summary>
public sealed record ExtractionEnginePin(
    ExtractionEngineKind Kind,
    string Version,
    string ArchiveFileName,
    string DownloadUrl,
    string? Sha256,
    bool Sha256Verified,
    string? Md5,
    string ExecutablePath,
    string Notes);

/// <summary>The pinned extraction-engine releases (stage 1 of the pipeline).</summary>
public static class ExtractionEngineLock
{
    public static IReadOnlyList<ExtractionEnginePin> Pinned { get; } =
    [
        // 7-Zip 26.06 (2026-11-06), the "Extra" archive: standalone console
        // 7za.exe + 7z DLL. Official download page: 7-zip.org; releases are
        // served from the project's GitHub (ip7z/7zip). No SHA-256 for this
        // archive was published on a source this lock could cite.
        new ExtractionEnginePin(ExtractionEngineKind.SevenZip, "26.06", "7z2606-extra.7z",
            "https://github.com/ip7z/7zip/releases/download/26.06/7z2606-extra.7z",
            null, false, null, "7za.exe",
            "Extracts NSIS and most archive-shaped payloads (7z l/x). Cannot unpack Inno Setup."),
        // innoextract 1.9 (2020-08-09), the last upstream release. The
        // official site (constexpr.org/innoextract) publishes an MD5 and a
        // signature for the Windows binaries, no SHA-256.
        new ExtractionEnginePin(ExtractionEngineKind.InnoExtract, "1.9", "innoextract-1.9-windows.zip",
            "https://constexpr.org/innoextract/files/innoextract-1.9-windows.zip",
            null, false, "72d0d0dd874b6236eaa44411f4470ee1", "innoextract.exe",
            "Unpacks Inno Setup installers (upstream 1.9 covers Inno Setup up to 6.1.x); newer Inno formats fall back to the runtime pass."),
        // lessMSI 2.12.9 (2026-04-02) from activescott/lessmsi releases;
        // asset name follows the project's lessmsi-v{version}.zip pattern.
        // No SHA-256 published on a source this lock could cite.
        new ExtractionEnginePin(ExtractionEngineKind.LessMsi, "2.12.9", "lessmsi-v2.12.9.zip",
            "https://github.com/activescott/lessmsi/releases/download/v2.12.9/lessmsi-v2.12.9.zip",
            null, false, null, "lessmsi.exe",
            "Extracts MSI payloads (lessmsi x) as a complement to the MSI table reader."),
    ];

    public static ExtractionEnginePin Pin(ExtractionEngineKind kind) => Pinned.Single(p => p.Kind == kind);
}
