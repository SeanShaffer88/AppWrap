using AppWrap.Core;
using AppWrap.Core.Audit;
using AppWrap.Core.Pipeline;
using AppWrap.Core.TestHarness;

namespace AppWrap.Build.Audit;

/// <summary>One conversion runtime-audit run: install this source, then launch the installed app once.</summary>
public sealed record RuntimeAuditRequest(
    string SourcePath,
    string? LaunchExecutable = null,
    IReadOnlyList<string>? SilentArguments = null,
    IReadOnlyList<string>? RegistryRoots = null,
    TimeSpan? InstallerTimeout = null,
    TimeSpan? AppTimeout = null,
    string? PayloadOutputDirectory = null);

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

    /// <summary>
    /// The sandboxed route (pipeline stage 2): setup installers and their
    /// installed apps, or a selected executable from a portable zip, run
    /// inside a dedicated Sandboxie box. Files and the box registry hive
    /// become findings through the same classification as the host route.
    /// A missing provider or failed hive capture is an error, never a host
    /// fallback. Zip payloads are retained at the requested output folder.
    /// Windows-only at runtime; Unverified.
    /// </summary>
    public static async Task<RuntimeAuditResult> RunSandboxedAsync(RuntimeAuditRequest request,
        ISandboxProvider sandbox, IMsiReader msiReader, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sandbox);
        if (!sandbox.IsAvailable)
            throw new BuildFailureException("Sandboxie-Plus is not installed; the sandboxed audit was not run. AppWrap will not fall back to running the source on the host.", "Sandboxed runtime audit requested without Sandboxie.");
        InputRules.SafePath(request.SourcePath);
        if (!File.Exists(request.SourcePath)) throw new UserInputException("Choose an installer file or portable zip that still exists.");
        var extension = Path.GetExtension(request.SourcePath).ToLowerInvariant();
        var isZip = extension == ".zip";
        if (extension is not (".exe" or ".msi" or ".zip"))
            throw new UserInputException("The sandboxed runtime audit takes a setup .exe/.msi or a portable .zip file.");
        var payloadOutputDirectory = isZip
            ? Path.GetFullPath(request.PayloadOutputDirectory ?? throw new UserInputException("Choose a folder where AppWrap can keep the extracted zip payload."))
            : null;
        if (payloadOutputDirectory is not null)
        {
            InputRules.SafePath(payloadOutputDirectory);
            if (Directory.Exists(payloadOutputDirectory) || File.Exists(payloadOutputDirectory))
                throw new UserInputException("Choose a new, empty location for the extracted zip payload.");
        }

        var workRoot = Path.Combine(Path.GetTempPath(), "appwrap-audit-box-" + Guid.NewGuid().ToString("N"));
        var installDir = Path.Combine(workRoot, "install");
        Directory.CreateDirectory(installDir);
        var spec = SandboxieBoxSpec.Profiling(Path.Combine(workRoot, "box"));
        Directory.CreateDirectory(spec.FileRootPath);
        var extractedPayload = payloadOutputDirectory;
        var preservePayload = false;
        RuntimeAuditResult? result = null;
        try
        {
            AuditReport staticReport;
            SandboxRunOutcome? installer = null;
            if (isZip)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(extractedPayload!)!);
                staticReport = StaticAuditor.AuditZip(request.SourcePath, extractedPayload!, token);
            }
            else
            {
                staticReport = extension == ".msi"
                    ? StaticAuditor.AuditMsi(request.SourcePath, msiReader)
                    : StaticAuditor.AuditExe(request.SourcePath);
                var (executable, arguments) = InstallCommand(request, installDir);
                installer = await sandbox.RunAsync(spec, executable, arguments, request.InstallerTimeout ?? DefaultInstallerTimeout, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (installer.TimedOut)
                    throw new BuildFailureException("The installer timed out inside Sandboxie; AppWrap stopped the audit instead of reporting partial results.", "Sandboxed installer exceeded its timeout.");
                if (installer.ExitCode is not (0 or 3010))
                    throw new BuildFailureException("The installer did not finish successfully inside Sandboxie; AppWrap stopped the audit.", $"Sandboxed installer exit code: {installer.ExitCode?.ToString() ?? "unknown"}.");
            }

            var context = HostContext() with { AppDirectory = isZip ? extractedPayload : installDir };
            var boxFiles = sandbox.HarvestFiles(spec);
            var findings = new List<AuditFinding>(staticReport.Findings);
            findings.AddRange(SandboxieHarvest.FromBoxFiles(boxFiles, spec, context));

            int? appExitCode = null;
            var appTimedOut = false;
            string? launch;
            string? executablePath;
            string? workingDirectory = null;
            if (isZip)
            {
                launch = request.LaunchExecutable ?? staticReport.MainExecutable;
                if (string.IsNullOrWhiteSpace(launch))
                    throw new UserInputException("AppWrap could not choose one main executable in this zip. Select an executable with --launch and run the sandboxed audit again.");
                executablePath = ResolveZipExecutable(extractedPayload!, launch);
                workingDirectory = extractedPayload;
            }
            else
            {
                launch = request.LaunchExecutable;
                if (launch is null)
                {
                    // The installed app's host path, mirrored back from the box:
                    // the executable the harvest saw under the install folder.
                    var installPrefix = installDir.Replace('/', '\\').TrimEnd('\\') + "\\";
                    executablePath = boxFiles
                        .Select(f => PalPathMapper.BoxPathToHostPath(f, context))
                        .Where(p => p is not null && p.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase)
                            && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => p!.Length)
                        .FirstOrDefault();
                    launch = executablePath is null ? null : Path.GetFileName(executablePath);
                }
                else
                {
                    executablePath = Path.Combine(installDir, launch.Replace('/', Path.DirectorySeparatorChar));
                }
            }
            if (executablePath is not null)
            {
                var app = await sandbox.RunAsync(spec, executablePath, [], request.AppTimeout ?? DefaultAppTimeout, token, workingDirectory).ConfigureAwait(false);
                appExitCode = app.ExitCode;
                appTimedOut = app.TimedOut;
                // Re-harvest: the app's own writes join the findings too.
                findings.AddRange(SandboxieHarvest.FromBoxFiles(sandbox.HarvestFiles(spec), spec, context));
            }
            var registryChanges = SandboxieHarvest.FromLoadedHive(sandbox.CaptureBoxRegistry(spec, token), SandboxieHarvest.LoadRootFor(spec));
            findings.AddRange(RuntimeFindings.FromRegistryChanges(registryChanges));

            var merged = staticReport with
            {
                Findings = findings.GroupBy(f => (f.Kind, f.Subject)).Select(g => g.First()).ToList(),
                PayloadFolder = isZip ? payloadOutputDirectory : spec.FileRootPath,
                MainExecutable = launch ?? staticReport.MainExecutable,
            };
            result = new RuntimeAuditResult(merged, registryChanges, isZip ? payloadOutputDirectory! : installDir,
                installer?.ExitCode, appExitCode, appTimedOut, null);
            preservePayload = isZip;
        }
        finally
        {
            if (isZip && !preservePayload && extractedPayload is not null)
                TryDeleteTree(extractedPayload);
            var warning = TryDeleteTree(workRoot);
            if (result is not null && warning is not null) result = result with { CleanupWarning = warning };
        }
        return result!;
    }

    private static string ResolveZipExecutable(string payloadDirectory, string relativePath)
    {
        var fullRoot = Path.GetFullPath(payloadDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!InputRules.IsWithin(fullPath, fullRoot) || !File.Exists(fullPath))
            throw new UserInputException("Choose an executable inside the extracted zip payload.");
        return fullPath;
    }

    private static PalPathContext HostContext() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        null, null);

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
