using AppWrap.Core;
using AppWrap.Core.Audit;
using AppWrap.Core.TestHarness;

namespace AppWrap.Build.Audit;

/// <summary>One conversion runtime-audit run: install this source, then launch the installed app once.</summary>
public sealed record RuntimeAuditRequest(
    string SourcePath,
    string? LaunchExecutable = null,
    IReadOnlyList<string>? SilentArguments = null,
    IReadOnlyList<string>? RegistryRoots = null,
    TimeSpan? InstallerTimeout = null,
    TimeSpan? AppTimeout = null);

public sealed record RuntimeAuditResult(
    AuditReport Report,
    IReadOnlyList<DetectedRegistryChange> RegistryChanges,
    string InstallDirectory,
    int? InstallerExitCode,
    int? AppExitCode,
    bool AppTimedOut,
    string? CleanupWarning);

/// <summary>
/// The runtime half of the portable-conversion audit. This is an explicit,
/// user-invoked operation in the test harness's discipline — no build path
/// references it, and it runs only when the user asks for the runtime pass:
///
/// 1. The registry roots (default: the harness's HKCU/HKLM Software roots)
///    are snapshotted.
/// 2. The vendor installer runs silently (family silent switches, or the
///    caller's explicit arguments) with its install target inside a
///    throwaway scratch tree under the system temp folder.
/// 3. The installed folder is inventoried with the static auditor, and the
///    installed app is launched once from the scratch copy.
/// 4. The registry is snapshotted again; the diff becomes registry
///    findings merged into the static report.
///
/// Honest limits: the installer runs on this machine (its registry writes
/// land in the real registry — observing them is the point of the pass —
/// and AppWrap does not uninstall it afterwards; the install *files* go to
/// the scratch tree, which is deleted afterwards). An installer that needs
/// administrator rights, a reboot or interaction will fail or time out,
/// and that outcome is reported, not hidden. Windows-only at runtime.
/// </summary>
public static class RuntimeAuditor
{
    public static readonly TimeSpan DefaultInstallerTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultAppTimeout = TimeSpan.FromSeconds(60);

    public static Task<RuntimeAuditResult> RunAsync(RuntimeAuditRequest request, CancellationToken token) =>
        RunAsync(request, new WindowsRegistrySnapshotProvider(), new WindowsMsiReader(), token);

    public static async Task<RuntimeAuditResult> RunAsync(RuntimeAuditRequest request,
        IRegistrySnapshotProvider registryProvider, IMsiReader msiReader, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(registryProvider);
        if (!OperatingSystem.IsWindows())
            throw new BuildFailureException("The runtime audit needs Windows: it runs the vendor installer and observes the registry. The static audit works on this machine.", "Runtime audit requested on a non-Windows host.");
        InputRules.SafePath(request.SourcePath);
        if (!File.Exists(request.SourcePath)) throw new UserInputException("Choose an installer file that still exists.");
        var extension = Path.GetExtension(request.SourcePath).ToLowerInvariant();
        if (extension is not (".exe" or ".msi")) throw new UserInputException("The runtime audit takes a setup .exe or .msi file.");

        var roots = request.RegistryRoots ?? TestHarness.DefaultRegistryRoots;
        var staticReport = extension == ".msi"
            ? StaticAuditor.AuditMsi(request.SourcePath, msiReader)
            : StaticAuditor.AuditExe(request.SourcePath);
        var workRoot = Path.Combine(Path.GetTempPath(), "appwrap-audit-" + Guid.NewGuid().ToString("N"));
        var installDir = Path.Combine(workRoot, "install");
        Directory.CreateDirectory(installDir);
        RuntimeAuditResult? result = null;
        try
        {
            var (executable, arguments) = InstallCommand(request, installDir);
            var registryBefore = registryProvider.Capture(roots, token);
            var installer = await ProcessRunner.RunAsync(executable, arguments, workRoot, token, request.InstallerTimeout ?? DefaultInstallerTimeout).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var findings = new List<AuditFinding>(staticReport.Findings);
            AuditReport? inventory = null;
            if (Directory.EnumerateFileSystemEntries(installDir).Any())
            {
                inventory = StaticAuditor.AuditFolder(installDir, request.SourcePath, AuditSourceKind.ExtractedFolder, token);
                findings.AddRange(inventory.Findings);
            }
            else
            {
                findings.Add(new AuditFinding(AuditFindingKind.StaticAuditLimited, Path.GetFileName(request.SourcePath),
                    "The installer reported success but left nothing in its scratch install folder — it may have installed to a default location instead. Registry findings below still describe what it did.",
                    "runtime audit: install folder inventory", AuditConfidence.Medium));
            }

            int? appExitCode = null;
            var appTimedOut = false;
            var launch = request.LaunchExecutable ?? inventory?.MainExecutable;
            if (launch is not null)
            {
                var launchPath = Path.Combine(installDir, launch.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(launchPath))
                {
                    try
                    {
                        var app = await ProcessRunner.RunAsync(launchPath, [], installDir, token, request.AppTimeout ?? DefaultAppTimeout).ConfigureAwait(false);
                        appExitCode = app.ExitCode;
                    }
                    catch (TimeoutException) { appTimedOut = true; }
                }
            }
            var registryChanges = registryBefore.Diff(registryProvider.Capture(roots, token));
            findings.AddRange(RuntimeFindings.FromRegistryChanges(registryChanges));
            var merged = staticReport with { Findings = findings, PayloadFolder = installDir, MainExecutable = launch ?? staticReport.MainExecutable };
            result = new RuntimeAuditResult(merged, registryChanges, installDir, installer.ExitCode, appExitCode, appTimedOut, null);
        }
        finally
        {
            var warning = TryDeleteTree(workRoot);
            if (result is not null && warning is not null) result = result with { CleanupWarning = warning };
        }
        return result!;
    }

    /// <summary>The silent install command for a source: caller arguments win; otherwise the family switches.</summary>
    internal static (string Executable, IReadOnlyList<string> Arguments) InstallCommand(RuntimeAuditRequest request, string installDir)
    {
        if (request.SilentArguments is { Count: > 0 }) return (request.SourcePath, request.SilentArguments);
        if (Path.GetExtension(request.SourcePath).Equals(".msi", StringComparison.OrdinalIgnoreCase))
            return (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"),
                ["/i", request.SourcePath, "/qn", "/norestart", "INSTALLDIR=" + installDir]);
        return StaticAuditor.DetectExeFamily(request.SourcePath) switch
        {
            StaticAuditor.InstallerFamily.Nsis => (request.SourcePath, ["/S", "/D=" + installDir]),
            StaticAuditor.InstallerFamily.InnoSetup => (request.SourcePath, ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=" + installDir]),
            StaticAuditor.InstallerFamily.MsiWrapper => (request.SourcePath, ["/quiet", "/norestart"]),
            _ => throw new UserInputException("AppWrap does not know this installer's silent switches, so it will not run it unattended. Pass the silent arguments explicitly (--silent-args) if you know them."),
        };
    }

    private static string? TryDeleteTree(string root)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                return null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2) return "The runtime-audit scratch folder could not be fully removed; left-over files remain at: " + root;
                Thread.Sleep(200);
            }
        }
        return null;
    }
}
