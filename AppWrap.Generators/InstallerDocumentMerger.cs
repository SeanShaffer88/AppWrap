using AppWrap.Core.PortableProfiles;

namespace AppWrap.Generators;

/// <summary>
/// Merges a profile's installer document over the trusted upgrade guard's
/// installer settings (<see cref="UpgradeGuardGenerator.InstallerSettings"/>).
/// The guard always wins: every key present in the guard text is guard-owned and
/// profile entries for those keys are skipped (the profile validator also errors
/// on such attempts — the merger stays safe even when used on its own).
/// Guard sections keep their order first; profile sections are appended in
/// profile order. The merge is deterministic: same inputs, same text.
/// </summary>
public static class InstallerDocumentMerger
{
    public static string Merge(string guardSettings, IniDocument profileInstaller)
    {
        var guard = IniDocument.Parse(guardSettings);
        var guardOwned = new HashSet<string>(
            guard.AllEntries
                .Where(e => e.Section is not null)
                .Select(e => e.Section + "\0" + e.Key),
            StringComparer.OrdinalIgnoreCase);

        var result = IniDocument.Parse(guardSettings);
        foreach (var section in profileInstaller.Sections.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var entry in profileInstaller.Entries(section))
            {
                if (guardOwned.Contains(section + "\0" + entry.Key)) continue;
                result.SetValue(section, entry.Key, entry.Value);
            }
        }
        return result.Serialize();
    }
}
