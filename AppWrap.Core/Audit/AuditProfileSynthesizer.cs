using AppWrap.Core.Pipeline;
using AppWrap.Core.PortableProfiles;

namespace AppWrap.Core.Audit;

/// <summary>The outcome of draft synthesis: the draft profile, what was mapped, and what was not.</summary>
public sealed record DraftProfileResult(
    PortableProfile Profile,
    IReadOnlyList<string> MappedNotes,
    IReadOnlyList<AuditFinding> UnmappedFindings);

/// <summary>
/// Turns audit findings into a DRAFT <see cref="PortableProfile"/> over the
/// existing profile model:
///
/// - Registry writes become [RegistryKeys] save/restore rules (one per
///   distinct key), with [Activate] Registry=true. A runtime finding that
///   carries an exact value also becomes a [RegistryValueWrite] under its
///   covering key, so the planner's coverage rule is satisfied by
///   construction.
/// - Host data locations (AppData-shaped paths from MSI tables or runtime
///   profile leakage) become [DirectoriesMove] rules; payload-side data
///   files/folders become [FilesMove] / [DirectoriesMove] rules targeting
///   the app folder, which is PAL's settings-in-app-folder pattern.
/// - Leftover vendor folders become [DirectoriesCleanupIfEmpty] rules:
///   the (now-empty) vendor parent of a redirected AppData folder, and
///   ProgramData vendor folders — ProgramData cannot be redirected by a
///   per-user portable, so empty-cleanup plus a note is the honest rule.
/// - A hardcoded absolute path inside a payload config file becomes a
///   [FileWrite] Replace rule when the path tokenizes to a PAL variable
///   form (%APPDATA%, %USERPROFILE%, …): at launch PAL rewrites the
///   machine-specific path in the file to the portable equivalent.
/// - Hardcoded paths that do not tokenize, shortcuts, services/drivers
///   and audit limits have no honest PAL equivalent: they are returned
///   as unmapped findings, never silently dropped or faked into rules.
///
/// The draft is data only. Like every import it grants no trust
/// (<see cref="PortableProfile.TrustedExtensions"/> stays empty and
/// force-cleanup stays unacknowledged); it becomes a real profile only when
/// the user reviews and saves it in the profile workspace, and the normal
/// validator/planner validate it like any other profile.
/// </summary>
public static class AuditProfileSynthesizer
{
    public static DraftProfileResult Synthesize(AuditReport report, string? mainExecutable = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var notes = new List<string>();
        var unmapped = new List<AuditFinding>();
        var launcher = IniDocument.Parse("");
        var exe = (mainExecutable ?? report.MainExecutable)?.Replace('/', '\\');
        if (exe is not null)
        {
            launcher.SetValue("Launch", "ProgramExecutable", exe);
            notes.Add($"Launch executable set to {exe} from the audit.");
        }

        // --- registry -------------------------------------------------------
        var registryKeys = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase); // storage name -> key path
        var seenKeyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedStorage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valueWrites = new List<(string KeyPath, string ValueName, string Kind, string Data)>();
        foreach (var finding in report.Findings.Where(f => f.Kind == AuditFindingKind.RegistryWrite))
        {
            var keyPath = finding.RegistryKeyPath is not null
                ? RegistrySnapshotNormalize(finding.RegistryKeyPath)
                : RegistrySnapshotNormalize(finding.Subject);
            if (!IsAppSpecificKey(keyPath)) { unmapped.Add(finding); continue; }
            if (seenKeyPaths.Add(keyPath))
            {
                var storage = StorageName(keyPath, usedStorage);
                registryKeys[storage] = keyPath;
            }
            // Only string kinds become [RegistryValueWrite] entries: their
            // PAL data form is the literal string. Numeric/binary data stays
            // covered by the [RegistryKeys] save/restore rule alone, because
            // its exact PAL encoding is not proven by the audit evidence.
            if (finding.RegistryValueName is not null && finding.RegistryValueData is not null
                && finding.RegistryValueKind is "REG_SZ" or "REG_EXPAND_SZ")
                valueWrites.Add((keyPath, finding.RegistryValueName, finding.RegistryValueKind, finding.RegistryValueData));
        }
        if (registryKeys.Count > 0)
        {
            launcher.SetValue("Activate", "Registry", "true");
            foreach (var (storage, keyPath) in registryKeys)
                launcher.AppendEntry("RegistryKeys", storage, keyPath);
            notes.Add($"{registryKeys.Count} registry key(s) will be saved with the portable data and restored on the host while the app runs.");
            foreach (var write in valueWrites.Distinct())
            {
                var fullKey = write.ValueName.Length == 0 ? write.KeyPath + "\\" : write.KeyPath + "\\" + write.ValueName;
                launcher.AppendEntry("RegistryValueWrite", fullKey, write.Kind + ":" + write.Data);
            }
            if (valueWrites.Count > 0) notes.Add($"{valueWrites.Distinct().Count()} exact registry value write(s) will be re-applied at launch under their covering keys.");
        }

        // --- data locations ---------------------------------------------------
        var directoryMoves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // portable key -> host target
        var fileMoves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in report.Findings.Where(f => f.Kind == AuditFindingKind.FileDataLocation))
        {
            var subject = finding.Subject.Replace('/', '\\');
            if (TryHostDataTarget(subject, out var hostTarget, out var appSegment))
            {
                var key = UniqueKey(directoryMoves, appSegment);
                directoryMoves[key] = hostTarget;
                continue;
            }
            if (report.PayloadFolder is not null || report.SourceKind is AuditSourceKind.PortableZip or AuditSourceKind.ExtractedFolder)
            {
                // Payload-side location: PAL's pattern is Data-side storage
                // moved into the app folder at launch.
                var directory = Path.GetDirectoryName(subject);
                var target = "%PAL:AppDir%" + (string.IsNullOrEmpty(directory) ? "" : "\\" + directory);
                if (finding.SubjectIsDirectory)
                {
                    var key = UniqueKey(directoryMoves, Path.GetFileName(subject));
                    directoryMoves[key] = target + "\\" + Path.GetFileName(subject);
                }
                else
                {
                    var name = Path.GetFileName(subject);
                    if (!fileMoves.ContainsKey(name)) fileMoves[name] = target;
                }
                continue;
            }
            unmapped.Add(finding);
        }
        // The planner rejects overlapping move targets (one target inside
        // another), because PAL's backup/restore would collide. Payload
        // drafts hit this when a root config file (target: the app folder)
        // coexists with a data folder inside the app folder (target: a
        // subfolder of it). Directory moves carry the user's data, so they
        // win; a conflicting file move is left unmapped for a human
        // decision instead of emitting a draft the planner must reject.
        var demoted = new List<string>();
        foreach (var (name, target) in fileMoves.ToList())
        {
            if (directoryMoves.Values.Any(dir => IsSameOrNested(dir, target) || IsSameOrNested(target, dir)))
            {
                fileMoves.Remove(name);
                demoted.Add(name);
            }
        }
        if (demoted.Count > 0)
            notes.Add($"File(s) {string.Join(", ", demoted)} were not mapped to a move rule: their app-folder target overlaps a data-folder move, which the planner forbids. Decide in the profile workspace whether the file or the folder move should win.");
        foreach (var (key, target) in directoryMoves) launcher.AppendEntry("DirectoriesMove", key, target);
        foreach (var (key, target) in fileMoves) launcher.AppendEntry("FilesMove", key, target);
        if (directoryMoves.Count > 0) notes.Add($"{directoryMoves.Count} data folder(s) will be redirected into the portable Data folder.");
        if (fileMoves.Count > 0) notes.Add($"{fileMoves.Count} data/config file(s) will be kept with the portable data and placed for the app at launch.");

        // --- leftover vendor folders ---------------------------------------
        // Redirecting %APPDATA%\Vendor\App leaves the empty %APPDATA%\Vendor
        // shell behind on the host; PAL can remove it when it is empty.
        // ProgramData folders cannot be redirected at all (machine-wide,
        // per-user portable), so their vendor folders get the same
        // cleanup-if-empty treatment plus a note, never a fake move.
        var cleanup = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in directoryMoves.Values)
        {
            var segments = target.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3 && segments[0] is "%APPDATA%" or "%LOCALAPPDATA%" or "%PROGRAMDATA%")
                cleanup.Add(segments[0] + "\\" + segments[1]);
        }
        var programDataSeen = false;
        foreach (var finding in report.Findings.Where(f => f.Kind == AuditFindingKind.FileDataLocation))
        {
            var tokenized = PalPathMapper.TokenizeKnownRoots(finding.Subject.Replace('/', '\\'));
            if (!tokenized.StartsWith("%PROGRAMDATA%\\", StringComparison.OrdinalIgnoreCase)) continue;
            var segments = tokenized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2) cleanup.Add(segments[0] + "\\" + segments[1]);
            if (segments.Length >= 3) cleanup.Add(segments[0] + "\\" + segments[1] + "\\" + segments[2]);
            programDataSeen = true;
        }
        var cleanupIndex = 0;
        foreach (var path in cleanup) launcher.AppendEntry("DirectoriesCleanupIfEmpty", (++cleanupIndex).ToString(), path);
        if (cleanup.Count > 0)
            notes.Add($"{cleanup.Count} vendor folder(s) the app leaves behind will be removed at exit when empty (DirectoriesCleanupIfEmpty).");
        if (programDataSeen)
            notes.Add("The app creates folders under ProgramData, which a portable package cannot redirect; the draft only removes them when left empty.");

        // --- hardcoded paths in payload config files -------------------------
        // PAL's [FileWrite] Replace is the real mechanism for a config file
        // that hardcodes a machine path: at launch the literal path in the
        // file is rewritten to its PAL-token form. Only findings whose
        // evidence is a payload config file and whose path tokenizes are
        // mapped; the rest stay unmapped below.
        var fileWriteMapped = new HashSet<AuditFinding>(ReferenceEqualityComparer.Instance);
        if (report.PayloadFolder is not null)
        {
            var writeIndex = 0;
            foreach (var finding in report.Findings.Where(f => f.Kind == AuditFindingKind.HardcodedPath))
            {
                var tokenized = PalPathMapper.TokenizeKnownRoots(finding.Subject);
                if (string.Equals(tokenized, finding.Subject, StringComparison.Ordinal)) continue;
                writeIndex++;
                var section = "FileWrite" + writeIndex;
                launcher.SetValue(section, "Type", "Replace");
                launcher.SetValue(section, "File", "%PAL:AppDir%\\" + finding.Evidence.Replace('/', '\\'));
                launcher.SetValue(section, "Find", finding.Subject);
                launcher.SetValue(section, "Replace", tokenized);
                fileWriteMapped.Add(finding);
            }
            if (writeIndex > 0)
                notes.Add($"{writeIndex} hardcoded path(s) in payload config files will be rewritten to portable form at launch (FileWrite Replace).");
        }

        foreach (var finding in report.Findings)
            if (finding.Kind is AuditFindingKind.Shortcut or AuditFindingKind.ServiceOrDriver
                or AuditFindingKind.StaticAuditLimited
                || (finding.Kind == AuditFindingKind.HardcodedPath && !fileWriteMapped.Contains(finding)))
                unmapped.Add(finding);
        if (unmapped.Count > 0)
            notes.Add($"{unmapped.Count} finding(s) have no portable-profile rule (hardcoded paths, shortcuts, services/drivers, audit limits) and need a human decision.");

        var profile = PortableProfileCodec.FromIniTexts(launcher.Serialize(), "", null, null, PortableExecutionKind.Pal);
        return new DraftProfileResult(profile, notes, unmapped);
    }

    private static bool IsSameOrNested(string ancestor, string path)
    {
        var a = ancestor.TrimEnd('\\', '/');
        var p = path.TrimEnd('\\', '/');
        return string.Equals(a, p, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string RegistrySnapshotNormalize(string path) =>
        TestHarness.RegistrySnapshot.NormalizeKeyPath(path);

    /// <summary>Only app-specific keys become rules: a hive, or hive\Software / hive\Classes with nothing under it, is too broad to save/restore.</summary>
    private static bool IsAppSpecificKey(string keyPath)
    {
        var parts = keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (parts.Length == 2 && (parts[1].Equals("Software", StringComparison.OrdinalIgnoreCase) || parts[1].Equals("Classes", StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    private static string StorageName(string keyPath, HashSet<string> used)
    {
        var last = keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries)[^1];
        var clean = new string(last.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        if (clean.Length == 0) clean = "AppKey";
        var name = clean;
        var suffix = 2;
        while (!used.Add(name)) name = clean + suffix++;
        return name;
    }

    private static string UniqueKey<T>(Dictionary<string, T> map, string wanted)
    {
        var clean = new string(wanted.Where(c => c != '\\' && c != '/').ToArray());
        if (clean.Length == 0) clean = "Data";
        var name = clean;
        var suffix = 2;
        while (map.ContainsKey(name)) name = clean + suffix++;
        return name;
    }

    /// <summary>
    /// Recognizes AppData-shaped host locations in a finding subject:
    /// anything under an "AppData\Roaming" or "AppData\Local" segment, or a
    /// leading %APPDATA% / %LOCALAPPDATA% token, maps to the matching PAL
    /// token target with the first app segment as the portable key.
    /// </summary>
    private static bool TryHostDataTarget(string subject, out string hostTarget, out string appSegment)
    {
        hostTarget = ""; appSegment = "";
        var segments = subject.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < segments.Length; i++)
        {
            if (!segments[i].Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;
            var flavour = segments[i + 1];
            if (!flavour.Equals("Roaming", StringComparison.OrdinalIgnoreCase) && !flavour.Equals("Local", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 2 >= segments.Length) return false;
            var rest = segments.Skip(i + 2).ToList();
            // A trailing file name (settings.db) names the file inside the
            // data folder, not the folder itself; the folder is the target.
            if (rest.Count > 1 && rest[^1].Contains('.')) rest.RemoveAt(rest.Count - 1);
            if (rest.Count == 1 && rest[0].Contains('.')) return false;
            appSegment = rest[0];
            var token = flavour.Equals("Roaming", StringComparison.OrdinalIgnoreCase) ? "%APPDATA%" : "%LOCALAPPDATA%";
            hostTarget = token + "\\" + string.Join("\\", rest);
            return true;
        }
        foreach (var (token, _) in new[] { ("%APPDATA%", 0), ("%LOCALAPPDATA%", 0) })
        {
            if (!subject.StartsWith(token + "\\", StringComparison.OrdinalIgnoreCase)) continue;
            var rest = subject[(token.Length + 1)..];
            appSegment = rest.Split('\\')[0];
            hostTarget = subject;
            return appSegment.Length > 0;
        }
        return false;
    }
}
