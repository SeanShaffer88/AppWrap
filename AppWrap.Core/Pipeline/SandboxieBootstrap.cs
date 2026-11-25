namespace AppWrap.Core.Pipeline;

/// <summary>
/// What a Sandboxie detection pass found on this machine. Detection is
/// platform work (registry + files, in AppWrap.Build); the decision over
/// it is pure and lives here so the installer bootstrap and the profiler
/// agree on what "ready" means.
/// </summary>
public sealed record SandboxieDetection(
    bool UninstallKeyPresent,
    bool ServicePresent,
    bool StartExePresent,
    string? InstalledVersion,
    string? InstallPath)
{
    /// <summary>Nothing that looks like Sandboxie-Plus was found.</summary>
    public static SandboxieDetection Absent { get; } = new(false, false, false, null, null);

    /// <summary>The service and the Start.exe launcher both exist: a working install, whatever its paperwork.</summary>
    public bool LooksInstalled => ServicePresent && StartExePresent;
}

public enum SandboxieBootstrapAction
{
    /// <summary>A working install at or above the pinned floor: nothing to do.</summary>
    Ready,
    /// <summary>No working install: download and silently install the pinned release.</summary>
    Install,
    /// <summary>A working install below the pinned floor (or a partial install): install the pinned release over it.</summary>
    Upgrade,
}

/// <summary>The bootstrap decision and the plain-language reason, fit for an install log.</summary>
public sealed record SandboxieBootstrapDecision(SandboxieBootstrapAction Action, string Reason);

/// <summary>
/// The installer bootstrap's decision logic (stage 0 of the pipeline):
/// missing or outdated Sandboxie-Plus is installed from the pinned
/// release; a current one is left alone. An install whose version cannot
/// be read is NOT treated as outdated — AppWrap cannot prove it is old,
/// so it is left as-is and the profiler simply uses it.
/// </summary>
public static class SandboxieBootstrapPlanner
{
    public static SandboxieBootstrapDecision Decide(SandboxieDetection detection, SandboxieLock? pin = null)
    {
        ArgumentNullException.ThrowIfNull(detection);
        pin ??= SandboxieLock.Pinned;
        if (!detection.LooksInstalled)
            return new(SandboxieBootstrapAction.Install,
                detection.UninstallKeyPresent || detection.ServicePresent || detection.StartExePresent
                    ? "Sandboxie-Plus is only partly installed (service or launcher missing); installing the pinned release to repair it."
                    : "Sandboxie-Plus is not installed; installing the pinned release.");
        if (detection.InstalledVersion is null)
            return new(SandboxieBootstrapAction.Ready,
                "Sandboxie-Plus is installed; its version could not be read, so it is left as-is.");
        return CompareVersions(detection.InstalledVersion, pin.MinimumVersion) < 0
            ? new(SandboxieBootstrapAction.Upgrade,
                $"Sandboxie-Plus {detection.InstalledVersion} is older than the pinned {pin.MinimumVersion}; upgrading.")
            : new(SandboxieBootstrapAction.Ready,
                $"Sandboxie-Plus {detection.InstalledVersion} meets the pinned minimum {pin.MinimumVersion}.");
    }

    /// <summary>Numeric dotted-version comparison ("1.18.8" &lt; "1.19"); non-numeric suffixes are ignored.</summary>
    public static int CompareVersions(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] Parts(string version) =>
        version.Split('.', '-', '+').Select(part =>
        {
            var digits = new string(part.TakeWhile(char.IsAsciiDigit).ToArray());
            return digits.Length > 0 && int.TryParse(digits, out var value) ? value : 0;
        }).ToArray();
}
