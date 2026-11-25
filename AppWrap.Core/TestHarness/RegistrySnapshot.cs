namespace AppWrap.Core.TestHarness;

/// <summary>One registry value in a snapshot: its kind (REG_SZ, REG_DWORD, …) and a stable string form of its data.</summary>
public sealed record RegistryValueSnapshot(string Name, string Kind, string Data);

/// <summary>How a registry value changed across a run.</summary>
public enum RegistryChangeKind { KeyCreated, KeyDeleted, ValueCreated, ValueModified, ValueDeleted }

/// <summary>
/// Where a registry change landed. <see cref="HostLeakage"/> (an ordinary
/// HKCU key) and <see cref="MachineWide"/> (HKLM and other machine roots)
/// are portability violations for a packaged app: the harness redirects the
/// file profile into its sandbox, but the registry is the real host
/// registry, so a write there is exactly the leak a portable package must
/// not have. PAL's own backup area under
/// HKCU\Software\PortableApps.com is where a launcher legitimately stages
/// host backups, so changes there are noted, not violations.
/// </summary>
public enum RegistryChangeCategory { LauncherBackup, HostLeakage, MachineWide }

/// <summary>One detected registry change, with the key path in PAL display form (HKCU\Software\…).</summary>
public sealed record DetectedRegistryChange(
    string KeyPath, string? ValueName, RegistryChangeKind Change, RegistryChangeCategory Category, bool Violation, string? NewData,
    string? ValueKind = null);

/// <summary>
/// A snapshot of registry values under a set of roots: key path → value name
/// → value. Key paths are normalized (short hive, backslashes, no trailing
/// separator) so snapshots from different providers compare cleanly. The
/// capture itself is platform work behind <see cref="IRegistrySnapshotProvider"/>;
/// everything here — the model, the diff and the classification — is pure.
/// </summary>
public sealed class RegistrySnapshot
{
    public RegistrySnapshot(IReadOnlyDictionary<string, IReadOnlyDictionary<string, RegistryValueSnapshot>> keys) => Keys = keys;

    /// <summary>Values by normalized key path, then by value name ("" is the key's default value).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, RegistryValueSnapshot>> Keys { get; }

    public static RegistrySnapshot Empty { get; } = new(new Dictionary<string, IReadOnlyDictionary<string, RegistryValueSnapshot>>());

    public static string NormalizeKeyPath(string path)
    {
        var trimmed = path.Trim().TrimEnd('\\', '/').Replace('/', '\\');
        var cut = trimmed.IndexOf('\\');
        var hive = cut < 0 ? trimmed : trimmed[..cut];
        var shortHive = hive.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => "HKCU",
            "HKEY_LOCAL_MACHINE" => "HKLM",
            "HKEY_CLASSES_ROOT" => "HKCR",
            "HKEY_USERS" => "HKU",
            "HKEY_CURRENT_CONFIG" => "HKCC",
            var other => other,
        };
        return cut < 0 ? shortHive : shortHive + trimmed[cut..];
    }

    /// <summary>The changes that turn this (before) snapshot into <paramref name="after"/>, in key/value order.</summary>
    public IReadOnlyList<DetectedRegistryChange> Diff(RegistrySnapshot after)
    {
        var changes = new List<DetectedRegistryChange>();
        foreach (var (key, values) in after.Keys)
        {
            if (!Keys.TryGetValue(key, out var beforeValues))
            {
                changes.Add(RegistryClassifier.Describe(key, null, RegistryChangeKind.KeyCreated, null));
                foreach (var value in values.Values)
                    changes.Add(RegistryClassifier.Describe(key, value.Name, RegistryChangeKind.ValueCreated, value.Data, value.Kind));
                continue;
            }
            foreach (var value in values.Values)
            {
                if (!beforeValues.TryGetValue(value.Name, out var before))
                    changes.Add(RegistryClassifier.Describe(key, value.Name, RegistryChangeKind.ValueCreated, value.Data, value.Kind));
                else if (before != value)
                    changes.Add(RegistryClassifier.Describe(key, value.Name, RegistryChangeKind.ValueModified, value.Data, value.Kind));
            }
            foreach (var name in beforeValues.Keys)
                if (!values.ContainsKey(name))
                    changes.Add(RegistryClassifier.Describe(key, name, RegistryChangeKind.ValueDeleted, null));
        }
        foreach (var key in Keys.Keys)
            if (!after.Keys.ContainsKey(key))
                changes.Add(RegistryClassifier.Describe(key, null, RegistryChangeKind.KeyDeleted, null));
        changes.Sort((a, b) =>
        {
            var byKey = string.Compare(a.KeyPath, b.KeyPath, StringComparison.OrdinalIgnoreCase);
            return byKey != 0 ? byKey : string.Compare(a.ValueName ?? "", b.ValueName ?? "", StringComparison.OrdinalIgnoreCase);
        });
        return changes;
    }
}

/// <summary>The harness's registry portability rules, as pure functions over a detected change.</summary>
public static class RegistryClassifier
{
    public static (RegistryChangeCategory Category, bool Violation) Classify(string keyPath)
    {
        var normalized = RegistrySnapshot.NormalizeKeyPath(keyPath);
        if (normalized.StartsWith(@"HKCU\Software\PortableApps.com", StringComparison.OrdinalIgnoreCase))
            return (RegistryChangeCategory.LauncherBackup, false);
        if (normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "HKCU", StringComparison.OrdinalIgnoreCase))
            return (RegistryChangeCategory.HostLeakage, true);
        return (RegistryChangeCategory.MachineWide, true);
    }

    public static DetectedRegistryChange Describe(string keyPath, string? valueName, RegistryChangeKind change, string? newData, string? valueKind = null)
    {
        var normalized = RegistrySnapshot.NormalizeKeyPath(keyPath);
        var (category, violation) = Classify(normalized);
        return new DetectedRegistryChange(normalized, valueName, change, category, violation, newData, valueKind);
    }
}

/// <summary>
/// The registry capture seam. The production implementation
/// (AppWrap.Build) reads the real registry on Windows through advapi32;
/// checks substitute an in-memory provider. Roots are key paths such as
/// <c>HKCU\Software</c>; a provider returns every key and value under them.
/// </summary>
public interface IRegistrySnapshotProvider
{
    RegistrySnapshot Capture(IReadOnlyList<string> roots, CancellationToken token);
}
