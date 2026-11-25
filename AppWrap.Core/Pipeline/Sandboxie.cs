using System.Text;
using AppWrap.Core.Audit;
using AppWrap.Core.TestHarness;

namespace AppWrap.Core.Pipeline;

/// <summary>One dedicated AppWrap box: its name, where its file root lives (a scratch folder), and its network posture.</summary>
public sealed record SandboxieBoxSpec(string BoxName, string FileRootPath, bool BlockNetworkFiles = true)
{
    /// <summary>The profiling box: vendor installers and audited apps run here.</summary>
    public static SandboxieBoxSpec Profiling(string fileRootPath) => new("AppWrapProfile", fileRootPath);

    /// <summary>The leak-verification box: the packaged app runs here against a clean host.</summary>
    public static SandboxieBoxSpec LeakTest(string fileRootPath) => new("AppWrapLeakTest", fileRootPath);
}

/// <summary>
/// Generates the Sandboxie.ini section for a dedicated AppWrap box. The
/// box root is a scratch folder the caller owns (so harvest and cleanup
/// never touch the user's own boxes), the box never auto-recovers files
/// into the host profile, and network *file* access is blocked —
/// profiling needs no network. Pure text generation: check-covered.
/// Installing the section into the live Sandboxie configuration is the
/// Build provider's platform work.
/// </summary>
public static class SandboxieBoxConfig
{
    public static string Generate(SandboxieBoxSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (string.IsNullOrWhiteSpace(spec.BoxName) || spec.BoxName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new UserInputException("Choose a sandbox name with letters, digits, '-' or '_' only.");
        // The file root is a Windows path for the box, not a path this
        // process touches: it is validated for shape only, never resolved.
        if (string.IsNullOrWhiteSpace(spec.FileRootPath) || spec.FileRootPath.Any(char.IsControl))
            throw new UserInputException("Choose a sandbox file root folder.");
        var builder = new StringBuilder();
        builder.Append('[').Append(spec.BoxName).AppendLine("]");
        builder.AppendLine("Enabled=y");
        builder.Append("FileRootPath=").AppendLine(spec.FileRootPath.Replace('/', '\\'));
        builder.AppendLine("AutoDelete=n");
        builder.AppendLine("AutoRecover=n");
        builder.AppendLine("NotifyDirectAccessDenied=n");
        if (spec.BlockNetworkFiles) builder.AppendLine("BlockNetworkFiles=y");
        return builder.ToString();
    }
}

/// <summary>How a sandboxed run ended (the Sandboxie Start.exe launcher reports the boxed program's exit code).</summary>
public sealed record SandboxRunOutcome(int? ExitCode, bool TimedOut, TimeSpan Duration);

/// <summary>
/// The Sandboxie execution seam. The production implementation
/// (AppWrap.Build) drives the installed Sandboxie-Plus Start.exe;
/// checks substitute a fake. When no implementation reports itself
/// available, callers fall back to the host observation route and say so.
/// </summary>
public interface ISandboxProvider
{
    bool IsAvailable { get; }

    /// <summary>Runs one executable inside the box described by <paramref name="spec"/> and waits for it.</summary>
    Task<SandboxRunOutcome> RunAsync(SandboxieBoxSpec spec, string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token);

    /// <summary>Box-root-relative paths of every file the box currently holds (the harvest source).</summary>
    IReadOnlyList<string> HarvestFiles(SandboxieBoxSpec spec);

    /// <summary>
    /// A snapshot of the box's own registry hive (loaded under
    /// <see cref="SandboxieHarvest.LoadRootFor"/>), or an empty snapshot
    /// when the box wrote no registry state. Convert it with
    /// SandboxieHarvest.FromLoadedHive.
    /// </summary>
    RegistrySnapshot CaptureBoxRegistry(SandboxieBoxSpec spec, CancellationToken token);
}

/// <summary>
/// Turns a Sandboxie harvest into audit findings in the same shape the
/// host runtime pass produces, so both observation backends feed one
/// report and one draft synthesis. Box file paths are mapped back to
/// host paths and then to PAL tokens; AppData/ProgramData app folders
/// become data-location findings (grouped per app folder — the folder is
/// the portable unit), driver files stay hard warnings.
/// </summary>
public static class SandboxieHarvest
{
    /// <summary>The scratch HKLM key the Build provider loads a box hive under; one per box name.</summary>
    public static string LoadRootFor(SandboxieBoxSpec spec) => @"HKLM\AppWrapSandboxHive-" + spec.BoxName;

    public static IReadOnlyList<AuditFinding> FromBoxFiles(IEnumerable<string> boxRelativePaths, SandboxieBoxSpec spec, PalPathContext context)
    {
        ArgumentNullException.ThrowIfNull(boxRelativePaths);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(context);
        var findings = new List<AuditFinding>();
        var dataFolders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in boxRelativePaths)
        {
            var host = PalPathMapper.BoxPathToHostPath(relative, context);
            if (host is null) continue;
            var tokenized = PalPathMapper.ToPalPath(host, context);
            var name = Path.GetFileName(host);
            if (name.EndsWith(".sys", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new AuditFinding(AuditFindingKind.ServiceOrDriver, tokenized,
                    "A driver file was written inside the sandbox. PAL cannot make drivers portable; the package will still install the driver on the host.",
                    "sandboxie box harvest: " + relative.Replace('\\', '/'), AuditConfidence.High, HardWarning: true));
                continue;
            }
            if (TryAppFolder(tokenized, out var folder)) { dataFolders.Add(folder); continue; }
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                findings.Add(new AuditFinding(AuditFindingKind.Executable, tokenized,
                    "Executable written inside the sandbox.", "sandboxie box harvest: " + relative.Replace('\\', '/'), AuditConfidence.High));
        }
        foreach (var folder in dataFolders)
            findings.Add(new AuditFinding(AuditFindingKind.FileDataLocation, folder,
                "The sandboxed app keeps data in this host location; the draft profile redirects it into the portable Data folder.",
                "sandboxie box harvest", AuditConfidence.High, SubjectIsDirectory: true));
        return findings;
    }

    /// <summary>The app folder (token + first segment) of an AppData/ProgramData-shaped tokenized path.</summary>
    private static bool TryAppFolder(string tokenized, out string folder)
    {
        folder = "";
        foreach (var token in new[] { "%APPDATA%", "%LOCALAPPDATA%", "%PROGRAMDATA%" })
        {
            if (!tokenized.StartsWith(token + "\\", StringComparison.OrdinalIgnoreCase)) continue;
            var first = tokenized[(token.Length + 1)..].Split('\\')[0];
            if (first.Length == 0) return false;
            folder = token + "\\" + first;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Converts a snapshot of a box's registry hive (loaded under
    /// <paramref name="loadRoot"/> by the Build provider) into detected
    /// changes. A fresh box's hive holds only what boxed programs wrote,
    /// so every value is a creation relative to the clean host. Hive
    /// layout prefixes are normalized: a MACHINE subtree becomes HKLM,
    /// a USER\&lt;sid&gt; subtree becomes HKCU; anything unrecognized is
    /// skipped rather than misattributed. The changes then flow through
    /// the audit's runtime-findings conversion like any other runtime
    /// observation.
    /// </summary>
    public static IReadOnlyList<DetectedRegistryChange> FromLoadedHive(RegistrySnapshot boxSnapshot, string loadRoot)
    {
        ArgumentNullException.ThrowIfNull(boxSnapshot);
        var prefix = RegistrySnapshot.NormalizeKeyPath(loadRoot).TrimEnd('\\') + "\\";
        var changes = new List<DetectedRegistryChange>();
        foreach (var (key, values) in boxSnapshot.Keys)
        {
            var normalized = RegistrySnapshot.NormalizeKeyPath(key);
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = normalized[prefix.Length..];
            var segments = rest.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;
            string mapped;
            if (segments[0].Equals("MACHINE", StringComparison.OrdinalIgnoreCase))
                mapped = "HKLM" + (segments.Length > 1 ? "\\" + string.Join("\\", segments.Skip(1)) : "");
            else if (segments[0].Equals("USER", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
                mapped = "HKCU" + (segments.Length > 2 ? "\\" + string.Join("\\", segments.Skip(2)) : "");
            else if (segments[0] is "HKCU" or "HKLM" or "HKCR" or "HKU" or "HKCC")
                mapped = rest;
            else continue;
            foreach (var value in values.Values)
                changes.Add(RegistryClassifier.Describe(mapped, value.Name, RegistryChangeKind.ValueCreated, value.Data, value.Kind));
        }
        return changes;
    }
}
