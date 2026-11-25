using System.ComponentModel;
using System.Diagnostics;
using AppWrap.Core;
using AppWrap.Core.Pipeline;
using AppWrap.Core.PortableProfiles;
using AppWrap.Core.TestHarness;

namespace AppWrap.Build;

/// <summary>Everything the process runner needs for one test launch.</summary>
public sealed record TestProcessStart(string Executable, string WorkingDirectory, IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// How one test launch ended. <see cref="ExitCode"/> is null when the process
/// was killed after the timeout (or never produced one); <see cref="TimedOut"/>
/// distinguishes that from a crash, which keeps its real exit code.
/// </summary>
public sealed record TestProcessOutcome(int? ExitCode, bool TimedOut, TimeSpan Duration);

/// <summary>
/// The launch seam of the test harness. The production implementation is
/// <see cref="ProcessTestRunner"/>; checks substitute a stub so the copy →
/// sandbox → snapshot → classify → report pipeline is exercised without
/// launching a real application.
/// </summary>
public interface ITestProcessRunner
{
    Task<TestProcessOutcome> RunAsync(TestProcessStart start, TimeSpan timeout, CancellationToken token);
}

/// <summary>
/// Launches the packaged app directly (never through a shell), with the
/// harness's redirected environment. On timeout or cancellation it first asks
/// the app's window to close, waits briefly, then terminates the whole process
/// tree — a test run never leaves the tested app running behind it.
/// </summary>
public sealed class ProcessTestRunner : ITestProcessRunner
{
    public async Task<TestProcessOutcome> RunAsync(TestProcessStart start, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(start.Executable)
            {
                WorkingDirectory = start.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var pair in start.Environment) process.StartInfo.Environment[pair.Key] = pair.Value;
        if (!process.Start()) throw new IOException("The test app could not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            watch.Stop();
            return new(process.ExitCode, false, watch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            try { process.CloseMainWindow(); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            if (!await ExitedWithin(process, TimeSpan.FromSeconds(2)).ConfigureAwait(false))
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException) { }
                await ExitedWithin(process, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            watch.Stop();
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            int? exitCode = null;
            try { if (process.HasExited) exitCode = process.ExitCode; }
            catch (InvalidOperationException) { }
            return new(exitCode, true, watch.Elapsed);
        }
    }

    private static async Task<bool> ExitedWithin(Process process, TimeSpan limit)
    {
        try { await process.WaitForExitAsync().WaitAsync(limit).ConfigureAwait(false); return true; }
        catch (Exception error) when (error is TimeoutException or InvalidOperationException) { return false; }
    }
}

/// <summary>
/// One harness run: test the package folder at <see cref="PackageFolder"/>
/// (a built PAF Package folder, or an extracted portable app folder) and write
/// the report to <see cref="ReportPath"/>. <see cref="LauncherFileName"/> is
/// the package-root file to launch; when null it comes from the package's
/// appinfo.ini [Control] Start, falling back to the folder's only root .exe.
/// </summary>
public sealed record TestRunRequest(
    string AppName,
    TestRunMode Mode,
    string PackageFolder,
    string ReportPath,
    string? LauncherFileName = null,
    TimeSpan? Timeout = null);

public sealed record TestRunResult(TestReport Report, string ReportPath, string? CleanupWarning);

/// <summary>
/// The runtime test harness (Development Suite Phase 4). This is a separate,
/// explicit user action: no build path references it, and builds never execute
/// a packaged app. A run copies the package into a throwaway tree (bounded by
/// the build's own source limits), creates a sandbox profile and temp folder,
/// redirects USERPROFILE / APPDATA / LOCALAPPDATA / TEMP / TMP for the child
/// process, snapshots the package copy, the profile and the temp folder,
/// launches the app's root launcher, snapshots again, and classifies the diff
/// into a <see cref="TestReport"/> (see WriteClassifier for the rules). The
/// throwaway tree is deleted in all exit paths — success, failure, timeout,
/// crash and cancellation; a cleanup failure is reported, never hidden.
/// </summary>
public static class TestHarness
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The registry roots observed when a registry provider takes part: the per-user and machine Software trees.</summary>
    public static readonly IReadOnlyList<string> DefaultRegistryRoots = [@"HKCU\Software", @"HKLM\Software"];

    public static Task<TestRunResult> RunAsync(TestRunRequest request, CancellationToken token) =>
        RunAsync(request, new ProcessTestRunner(), token);

    public static Task<TestRunResult> RunAsync(TestRunRequest request, ITestProcessRunner runner, CancellationToken token) =>
        RunAsync(request, runner, token, null, null);

    /// <summary>
    /// Stage 4 leak verification: the same run, but the packaged app runs
    /// inside a clean Sandboxie box (a dedicated box whose file root is
    /// inside the harness's throwaway tree) instead of on the host. The
    /// watched roots are unchanged — they are all *outside* the box, so
    /// any write the harness observes there escaped the sandbox, which is
    /// exactly the host contamination the verdict must catch. The report
    /// records the "sandboxie" observation backend.
    /// </summary>
    public static Task<TestRunResult> RunSandboxieAsync(TestRunRequest request, ISandboxProvider sandbox, CancellationToken token,
        IRegistrySnapshotProvider? registryProvider = null, IReadOnlyList<string>? registryRoots = null) =>
        RunAsync(request, new ProcessTestRunner(), token, registryProvider, registryRoots, sandbox);

    /// <summary>
    /// The same run with registry observation: when
    /// <paramref name="registryProvider"/> is supplied, the registry roots
    /// (default <see cref="DefaultRegistryRoots"/>) are snapshotted before
    /// launch and after exit and the diff joins the report — file and
    /// registry observation share the one explicit Test action, the same
    /// sandbox and the same report. The registry itself is the host's (it
    /// cannot be redirected like the profile folders), which is exactly why
    /// a write there is classified as leakage.
    /// </summary>
    public static async Task<TestRunResult> RunAsync(TestRunRequest request, ITestProcessRunner runner, CancellationToken token,
        IRegistrySnapshotProvider? registryProvider, IReadOnlyList<string>? registryRoots, ISandboxProvider? sandbox = null)
    {
        InputRules.Text(request.AppName, "app name");
        if (!Enum.IsDefined(request.Mode)) throw new UserInputException("Choose a supported test mode.");
        InputRules.SafePath(request.PackageFolder);
        if (!Directory.Exists(request.PackageFolder)) throw new UserInputException("Choose a package folder that still exists.");
        InputRules.SafePath(request.ReportPath);
        if (request.LauncherFileName is { Length: > 0 } launcherName &&
            (Path.IsPathRooted(launcherName) || !string.Equals(Path.GetFileName(launcherName), launcherName, StringComparison.Ordinal)))
            throw new UserInputException("The launch file must be a file at the package root.");

        var workRoot = Path.Combine(Path.GetTempPath(), "appwrap-test-" + Guid.NewGuid().ToString("N"));
        TestRunResult? result = null;
        try
        {
            var packageDir = Path.Combine(workRoot, "package");
            var profileDir = Path.Combine(workRoot, "profile");
            var tempDir = Path.Combine(workRoot, "temp");
            var roaming = Path.Combine(profileDir, "AppData", "Roaming");
            var local = Path.Combine(profileDir, "AppData", "Local");
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(roaming);
            Directory.CreateDirectory(local);
            Directory.CreateDirectory(Path.Combine(profileDir, "Documents"));
            // The build's own bounded snapshot copy: same entry/depth/byte
            // limits as a build, and the project file is likewise excluded.
            await Task.Run(() => BuildService.Snapshot(request.PackageFolder, packageDir, token), token).ConfigureAwait(false);
            // PAF convention: the package carries a Data folder the app writes
            // its portable settings into; an extracted folder may lack an empty one.
            Directory.CreateDirectory(Path.Combine(packageDir, "Data"));

            var launcher = ResolveLauncher(request, packageDir);
            var executable = Path.Combine(packageDir, launcher);
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["USERPROFILE"] = profileDir,
                ["APPDATA"] = roaming,
                ["LOCALAPPDATA"] = local,
                ["TEMP"] = tempDir,
                ["TMP"] = tempDir,
            };
            var profileRootPart = Path.GetPathRoot(profileDir) ?? "";
            environment["HOMEDRIVE"] = profileRootPart.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var homePath = profileDir[profileRootPart.Length..];
            environment["HOMEPATH"] = homePath.Length == 0 ? "\\" : "\\" + homePath;

            var roots = new (WatchedRootKind Kind, string Directory)[]
            {
                (WatchedRootKind.Package, packageDir),
                (WatchedRootKind.Profile, profileDir),
                (WatchedRootKind.Temp, tempDir),
            };
            var before = roots.Select(r => TreeSnapshot.Capture(r.Directory, token)).ToArray();
            var registryBefore = registryProvider?.Capture(registryRoots ?? DefaultRegistryRoots, token);
            var startedUtc = DateTimeOffset.UtcNow;
            TestProcessOutcome outcome;
            if (sandbox is null)
            {
                outcome = await runner.RunAsync(new TestProcessStart(executable, packageDir, environment), request.Timeout ?? DefaultTimeout, token).ConfigureAwait(false);
            }
            else
            {
                // The box root lives inside the throwaway tree: harvest and
                // cleanup are the harness's own, and the watched roots stay
                // outside the box, so anything observed there escaped.
                var boxSpec = SandboxieBoxSpec.LeakTest(Path.Combine(workRoot, "box"));
                Directory.CreateDirectory(boxSpec.FileRootPath);
                var boxed = await sandbox.RunAsync(boxSpec, executable, [], request.Timeout ?? DefaultTimeout, token).ConfigureAwait(false);
                outcome = new TestProcessOutcome(boxed.ExitCode, boxed.TimedOut, boxed.Duration);
            }
            var writes = new List<DetectedWrite>();
            for (var i = 0; i < roots.Length; i++)
                foreach (var (path, change) in before[i].Diff(TreeSnapshot.Capture(roots[i].Directory, token)))
                    writes.Add(WriteClassifier.Describe(roots[i].Kind, path, change));
            var violations = writes.Where(w => w.Violation).ToList();
            var registryChanges = registryBefore is null
                ? null
                : registryBefore.Diff(registryProvider!.Capture(registryRoots ?? DefaultRegistryRoots, token));
            var report = new TestReport(
                TestReport.CurrentSchemaVersion,
                request.AppName,
                request.Mode,
                "\"" + executable + "\"",
                startedUtc,
                outcome.Duration.TotalSeconds,
                outcome.ExitCode,
                outcome.TimedOut,
                writes,
                violations,
                registryChanges is null
                    ? WriteClassifier.Verdict(writes, outcome.TimedOut, outcome.ExitCode)
                    : WriteClassifier.Verdict(writes, registryChanges, outcome.TimedOut, outcome.ExitCode),
                registryChanges is not null,
                registryChanges,
                registryChanges?.Where(c => c.Violation).ToList(),
                sandbox is null ? "host" : "sandboxie");
            WriteReport(request.ReportPath, report);
            result = new TestRunResult(report, Path.GetFullPath(request.ReportPath), null);
        }
        finally
        {
            var cleanupWarning = TryDeleteTree(workRoot);
            if (result is not null && cleanupWarning is not null) result = result with { CleanupWarning = cleanupWarning };
        }
        return result!;
    }

    private static string ResolveLauncher(TestRunRequest request, string packageDir)
    {
        if (request.LauncherFileName is { Length: > 0 } named)
        {
            if (!File.Exists(Path.Combine(packageDir, named))) throw new UserInputException("The package does not contain the launch file " + named + ".");
            return named;
        }
        var fromAppInfo = ReadAppInfoStart(packageDir);
        if (fromAppInfo is not null)
        {
            if (!File.Exists(Path.Combine(packageDir, fromAppInfo))) throw new UserInputException("The package details name a launch file that is not in the package: " + fromAppInfo + ".");
            return fromAppInfo;
        }
        var rootExecutables = Directory.EnumerateFiles(packageDir, "*.exe", SearchOption.TopDirectoryOnly).ToList();
        if (rootExecutables.Count == 1) return Path.GetFileName(rootExecutables[0]);
        throw new UserInputException(rootExecutables.Count == 0
            ? "This package has no launchable file at its root. A PAF Package folder only gains one when the official PortableApps.com Installer builds it, or when it was packaged with a compiled launcher."
            : "This package has more than one launchable file at its root and its package details do not name one. Choose the launch file explicitly.");
    }

    private static string? ReadAppInfoStart(string packageDir)
    {
        var path = Path.Combine(packageDir, "App", "AppInfo", "appinfo.ini");
        if (!File.Exists(path) || new FileInfo(path).Length > IniDocument.MaxBytes) return null;
        var start = IniDocument.Parse(File.ReadAllText(path)).GetValue("Control", "Start");
        if (string.IsNullOrWhiteSpace(start)) return null;
        // [Control] Start names a file at the package root; anything else
        // (a path into the tree, a rooted path) is not a launcher to run.
        return string.Equals(Path.GetFileName(start), start, StringComparison.Ordinal) && start.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? start : null;
    }

    private static void WriteReport(string reportPath, TestReport report)
    {
        var full = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, report.ToJson());
        File.Move(temporary, full, overwrite: true);
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
                if (attempt == 2) return "The test sandbox could not be fully removed; left-over test files remain at: " + root;
                Thread.Sleep(200);
            }
        }
        return null;
    }
}
