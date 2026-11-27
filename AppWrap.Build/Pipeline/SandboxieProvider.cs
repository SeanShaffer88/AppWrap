using System.Diagnostics;
using AppWrap.Build.Audit;
using AppWrap.Core;
using AppWrap.Core.Pipeline;
using AppWrap.Core.TestHarness;
using Microsoft.Win32;

namespace AppWrap.Build.Pipeline;

/// <summary>
/// Finds Sandboxie-Plus on this machine, using exactly the facts the
/// pinned lock records: the Inno Setup uninstall key the official
/// installer writes (AppId "Sandboxie-Plus"), the SbieSvc service key,
/// and Start.exe in the install folder. Windows-only at runtime.
/// </summary>
public static class SandboxieLocator
{
    public static SandboxieDetection Detect()
    {
        if (!OperatingSystem.IsWindows()) return SandboxieDetection.Absent;
        var pin = SandboxieLock.Pinned;
        string? installPath = null;
        string? version = null;
        var uninstallPresent = false;
        try
        {
            using var uninstall = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Sandboxie-Plus_is1");
            if (uninstall is not null)
            {
                uninstallPresent = true;
                version = uninstall.GetValue("DisplayVersion") as string;
                installPath = uninstall.GetValue("Inno Setup: App Path") as string;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        var servicePresent = false;
        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SbieSvc");
            servicePresent = service is not null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        installPath ??= pin.DefaultInstallDirectory;
        var startPresent = File.Exists(Path.Combine(installPath, "Start.exe"));
        return new SandboxieDetection(uninstallPresent, servicePresent, startPresent, version, installPath);
    }

    /// <summary>The install folder when a working install was detected, else null.</summary>
    public static string? InstallPath()
    {
        var detection = Detect();
        return detection.LooksInstalled ? detection.InstallPath : null;
    }
}

/// <summary>
/// The production <see cref="ISandboxProvider"/>: drives the installed
/// Sandboxie-Plus through its Start.exe launcher. Box configuration is
/// installed with SbieIni.exe when present (the supported route), else
/// by appending the generated section to the Sandboxie.ini in the
/// Windows directory; the box file root is a scratch folder the caller
/// owns. Registry harvest loads the box hive (RegHive) under a temporary
/// HKLM key with reg.exe, snapshots it through the ordinary registry
/// provider, and unloads it again.
///
/// Windows-only at runtime; every part of this provider is Unverified —
/// the pure pieces it relies on (config text, path mapping, hive
/// normalization) are the check-covered ones in AppWrap.Core.
/// </summary>
public sealed class SandboxieProvider : ISandboxProvider
{
    private readonly string? installPath;

    public SandboxieProvider() => installPath = SandboxieLocator.InstallPath();

    public bool IsAvailable => installPath is not null;

    private string StartExe => Path.Combine(installPath!, "Start.exe");

    public async Task<SandboxRunOutcome> RunAsync(SandboxieBoxSpec spec, string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token, string? workingDirectory = null)
    {
        if (!IsAvailable) throw new BuildFailureException("Sandboxie-Plus is not installed, so the sandboxed route cannot run.", "Sandboxie provider used while unavailable.");
        EnsureBox(spec);
        Directory.CreateDirectory(spec.FileRootPath);
        var args = new List<string> { "/box:" + spec.BoxName, "/wait", executable };
        args.AddRange(arguments);
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await ProcessRunner.RunAsync(StartExe, args, workingDirectory ?? installPath!, token, timeout).ConfigureAwait(false);
            watch.Stop();
            return new SandboxRunOutcome(result.ExitCode, false, watch.Elapsed);
        }
        catch (TimeoutException)
        {
            watch.Stop();
            return new SandboxRunOutcome(null, true, watch.Elapsed);
        }
    }

    public IReadOnlyList<string> HarvestFiles(SandboxieBoxSpec spec)
    {
        if (!Directory.Exists(spec.FileRootPath)) return [];
        return Directory.EnumerateFiles(spec.FileRootPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(spec.FileRootPath, path))
            .ToList();
    }

    /// <summary>
    /// Snapshots the box's registry hive: loads RegHive under a scratch
    /// HKLM key, captures it with the ordinary registry provider, and
    /// unloads. Returns an empty snapshot when the box wrote no hive.
    /// </summary>
    public RegistrySnapshot CaptureBoxRegistry(SandboxieBoxSpec spec, CancellationToken token)
    {
        var hive = Path.Combine(spec.FileRootPath, "RegHive");
        // Sandboxie creates RegHive only when something wrote to the
        // registry. A genuinely missing file means no changes; an
        // inaccessible or malformed path must not masquerade as empty.
        FileAttributes hiveAttributes;
        try { hiveAttributes = File.GetAttributes(hive); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return RegistrySnapshot.Empty; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new BuildFailureException("AppWrap couldn't inspect Sandboxie's registry hive; the audit stopped instead of claiming there were no registry changes.",
                $"Registry hive inspection failed: {error.GetType().Name}: {error.Message}");
        }
        if (hiveAttributes.HasFlag(FileAttributes.Directory))
            throw new BuildFailureException("AppWrap found an invalid Sandboxie registry hive path; the audit stopped instead of claiming there were no registry changes.",
                $"Expected a hive file at '{hive}', but the path is a directory.");
        var loadRoot = SandboxieHarvest.LoadRootFor(spec);
        var loaded = false;
        try
        {
            RunChecked("reg.exe", $"load \"{loadRoot.Replace("HKLM", "HKEY_LOCAL_MACHINE")}\" \"{hive}\"", "load the Sandboxie registry hive");
            loaded = true;
            return new WindowsRegistrySnapshotProvider().Capture([loadRoot], token);
        }
        finally
        {
            if (loaded)
                RunChecked("reg.exe", $"unload \"{loadRoot.Replace("HKLM", "HKEY_LOCAL_MACHINE")}\"", "unload the temporary Sandboxie registry hive");
        }
    }

    private void EnsureBox(SandboxieBoxSpec spec)
    {
        var sbieIni = Path.Combine(installPath!, "SbieIni.exe");
        if (File.Exists(sbieIni))
        {
            void Set(string name, string value) =>
                RunChecked(sbieIni, $"set \"{spec.BoxName}\" \"{name}\" \"{value}\"", $"configure the Sandboxie box ({name})");
            Set("Enabled", "y");
            Set("FileRootPath", spec.FileRootPath.Replace('/', '\\'));
            Set("AutoDelete", "n");
            Set("AutoRecover", "n");
            if (spec.BlockNetworkFiles) Set("BlockNetworkFiles", "y");
            RunChecked(sbieIni, "reload", "reload Sandboxie configuration");
            return;
        }
        // Fallback: replace this AppWrap box's section in the global config.
        var iniPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sandboxie.ini");
        var existing = File.Exists(iniPath) ? File.ReadAllText(iniPath) : "";
        File.WriteAllText(iniPath, SandboxieBoxConfig.UpsertSection(existing, spec));
    }

    private static void RunChecked(string executable, string arguments, string operation)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            throw new BuildFailureException($"AppWrap couldn't {operation}; the Sandboxie audit stopped instead of claiming a clean result.",
                $"{operation} failed to start: {error.GetType().Name}: {error.Message}");
        }
        if (process is null)
            throw new BuildFailureException($"AppWrap couldn't {operation}; the Sandboxie audit stopped instead of claiming a clean result.",
                $"{operation} returned no process.");
        using (process)
        {
            try
            {
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(entireProcessTree: true); process.WaitForExit(); }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    EnsureSuccessfulExit(operation, null, timedOut: true);
                }
                EnsureSuccessfulExit(operation, process.ExitCode, timedOut: false);
            }
            catch (BuildFailureException) { throw; }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                throw new BuildFailureException($"AppWrap couldn't {operation}; the Sandboxie audit stopped instead of claiming a clean result.",
                    $"{operation} failed: {error.GetType().Name}: {error.Message}");
            }
        }
    }

    internal static void EnsureSuccessfulExit(string operation, int? exitCode, bool timedOut)
    {
        if (timedOut)
            throw new BuildFailureException($"AppWrap couldn't {operation} before the timeout; the Sandboxie audit stopped instead of claiming a clean result.",
                $"{operation} exceeded 30 seconds.");
        if (exitCode is not 0)
            throw new BuildFailureException($"AppWrap couldn't {operation}; the Sandboxie audit stopped instead of claiming a clean result.",
                $"{operation} exited with code {exitCode?.ToString() ?? "unknown"}.");
    }
}
