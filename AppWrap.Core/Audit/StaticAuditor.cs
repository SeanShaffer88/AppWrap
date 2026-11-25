using System.IO.Compression;
using System.Text.RegularExpressions;

namespace AppWrap.Core.Audit;

/// <summary>
/// The static half of the portable-conversion audit. It reads a conversion
/// source — a portable .zip, an .msi, an installer .exe, or an already
/// extracted folder — and reports where the app keeps data and what it
/// writes, without executing anything. Executables are identified by
/// signature; the engine-backed overloads (StaticAuditor.Engines.cs)
/// additionally unpack installer payloads with the pipeline's prepared
/// extraction engines. A family no prepared engine covers is recorded
/// as an explicit <see cref="AuditFindingKind.StaticAuditLimited"/>
/// finding, never guessed around.
/// </summary>
public static partial class StaticAuditor
{
    public const int MaxEntries = 50000;
    public const long MaxBytes = 20L * 1024 * 1024 * 1024;
    private const int MaxConfigScanBytes = 1_000_000;

    private static readonly string[] DataNames = ["data", "settings", "setting", "profile", "profiles", "config", "configuration", "userdata", "user data", "storage", "database"];
    private static readonly string[] ConfigExtensions = [".ini", ".json", ".xml", ".config", ".cfg", ".conf", ".yaml", ".yml", ".toml"];
    private static readonly string[] DataExtensions = [".db", ".sqlite", ".sqlite3", ".mdb", ".dat"];

    // ----- zip / folder ----------------------------------------------------

    /// <summary>
    /// Extracts a portable zip into <paramref name="scratchDirectory"/> with
    /// the build's own safety discipline (no traversal, no links, bounded
    /// size) and audits the extracted payload.
    /// </summary>
    public static AuditReport AuditZip(string zipPath, string scratchDirectory, CancellationToken token = default)
    {
        InputRules.SafePath(zipPath);
        if (!File.Exists(zipPath)) throw new UserInputException("Choose a portable zip file that still exists.");
        var scratch = Path.GetFullPath(scratchDirectory);
        Directory.CreateDirectory(scratch);
        ExtractZip(zipPath, scratch, token);
        var report = AuditFolder(scratch, zipPath, AuditSourceKind.PortableZip, token);
        return report;
    }

    internal static void ExtractZip(string zipPath, string scratch, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxEntries) throw new UserInputException("Choose a smaller zip to audit (up to 50,000 entries).");
        long bytes = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            if (name.Length == 0) continue;
            // Unix symlink entries carry the link type in the high attributes.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new UserInputException("The zip contains a linked file, which AppWrap does not extract: " + name);
            var target = Path.GetFullPath(Path.Combine(scratch, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!InputRules.IsWithin(target, scratch) && !string.Equals(target, scratch, StringComparison.Ordinal))
                throw new UserInputException("The zip contains a path outside its folder: " + name);
            if (name.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            bytes += entry.Length;
            if (bytes > MaxBytes) throw new UserInputException("Choose a zip smaller than 20 GB to audit.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open();
            using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(file);
        }
    }

    /// <summary>Audits an already-extracted payload folder (the zip path's second half, and folder sources directly).</summary>
    public static AuditReport AuditFolder(string payloadFolder, string sourcePath, AuditSourceKind kind, CancellationToken token = default)
    {
        var root = Path.GetFullPath(payloadFolder);
        InputRules.SafePath(root);
        if (!Directory.Exists(root)) throw new UserInputException("Choose a payload folder that still exists.");
        var findings = new List<AuditFinding>();
        var files = new List<string>();
        Walk(root, root, 0, files, token);
        string? mainExecutable = null;
        var executables = files.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var relative in files)
        {
            var display = relative.Replace('\\', '/');
            var name = Path.GetFileName(relative);
            var stem = Path.GetFileNameWithoutExtension(relative);
            var extension = Path.GetExtension(relative).ToLowerInvariant();
            var depth = display.Count(c => c == '/');
            if (extension == ".exe")
                findings.Add(new AuditFinding(AuditFindingKind.Executable, display, "Executable in the payload.", "payload file", AuditConfidence.High));
            if (extension == ".sys")
                findings.Add(new AuditFinding(AuditFindingKind.ServiceOrDriver, display,
                    "A driver file ships in the payload. PAL cannot make drivers portable; the package will still install the driver on the host.",
                    "payload file", AuditConfidence.High, HardWarning: true));
            if (extension == ".lnk")
                findings.Add(new AuditFinding(AuditFindingKind.Shortcut, display, "A shortcut file ships in the payload.", "payload file", AuditConfidence.High));
            if (IsDataName(stem) || DataNames.Any(d => string.Equals(name, d, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new AuditFinding(AuditFindingKind.FileDataLocation, display,
                    "The name matches a data/settings location: the app likely keeps user data here.",
                    "payload file name", AuditConfidence.High));
            else if (depth <= 1 && DataExtensions.Contains(extension))
                findings.Add(new AuditFinding(AuditFindingKind.FileDataLocation, display,
                    "A database/data file at the app root: the app likely keeps user data here.",
                    "payload file name", AuditConfidence.Medium));
            else if (depth <= 1 && ConfigExtensions.Contains(extension))
                findings.Add(new AuditFinding(AuditFindingKind.FileDataLocation, display,
                    "A configuration file at the app root: the app likely reads (and may rewrite) its settings here.",
                    "payload file name", AuditConfidence.Medium));
            if (extension == ".reg")
                AuditRegFile(Path.Combine(root, relative), display, findings);
            if (ConfigExtensions.Contains(extension))
                AuditConfigFile(Path.Combine(root, relative), display, findings);
        }
        // Directory names are data-location evidence too (a Settings folder
        // full of binaries is still where settings live).
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (DataNames.Any(d => string.Equals(name, d, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new AuditFinding(AuditFindingKind.FileDataLocation,
                    Path.GetRelativePath(root, directory).Replace('\\', '/'),
                    "A folder named like a data/settings location.", "payload folder name", AuditConfidence.High,
                    SubjectIsDirectory: true));
        }
        if (executables.Count == 1) mainExecutable = executables[0].Replace('\\', '/');
        else if (executables.Count > 1)
        {
            var folderNamed = executables.FirstOrDefault(e => string.Equals(Path.GetFileNameWithoutExtension(e), Path.GetFileName(root), StringComparison.OrdinalIgnoreCase));
            var rootLevel = executables.Where(e => !e.Contains('\\') && !e.Contains('/')).ToList();
            mainExecutable = (folderNamed ?? (rootLevel.Count == 1 ? rootLevel[0] : null))?.Replace('\\', '/');
        }
        return new AuditReport(AuditReport.CurrentSchemaVersion, Path.GetFullPath(sourcePath), kind, root, mainExecutable, findings);
    }

    private static void Walk(string root, string directory, int depth, List<string> files, CancellationToken token)
    {
        if (depth > 40) throw new UserInputException("This source has too many nested folders to audit.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            token.ThrowIfCancellationRequested();
            InputRules.SafePath(entry);
            if (files.Count > MaxEntries) throw new UserInputException("Choose a smaller source to audit (up to 50,000 files).");
            if (Directory.Exists(entry)) Walk(root, entry, depth + 1, files, token);
            else files.Add(Path.GetRelativePath(root, entry));
        }
    }

    private static bool IsDataName(string stem) =>
        DataNames.Any(d => stem.Equals(d, StringComparison.OrdinalIgnoreCase)
            || stem.StartsWith(d + ".", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));

    private static void AuditRegFile(string path, string display, List<AuditFinding> findings)
    {
        if (new FileInfo(path).Length > RegFileParser.MaxBytes) return;
        var parsed = RegFileParser.Parse(File.ReadAllText(path));
        foreach (var group in parsed.Values.GroupBy(v => v.KeyPath, StringComparer.OrdinalIgnoreCase))
            findings.Add(new AuditFinding(AuditFindingKind.RegistryWrite, group.Key,
                $".reg file writes {group.Count()} value(s) under this key (for example '{group.First().ValueName}').",
                $"{display} line {group.First().Line}", AuditConfidence.High, RegistryKeyPath: group.Key));
        foreach (var deletion in parsed.Deletions)
            findings.Add(new AuditFinding(AuditFindingKind.RegistryWrite, deletion.KeyPath,
                ".reg file deletes this key.", $"{display} line {deletion.Line}", AuditConfidence.High));
    }

    private static void AuditConfigFile(string path, string display, List<AuditFinding> findings)
    {
        var info = new FileInfo(path);
        if (info.Length == 0 || info.Length > MaxConfigScanBytes) return;
        var text = File.ReadAllText(path);
        foreach (Match match in AbsolutePath().Matches(text))
            findings.Add(new AuditFinding(AuditFindingKind.HardcodedPath, match.Value,
                "An embedded configuration file hardcodes an absolute path; on another machine this path may not exist or may point outside the portable folder.",
                display, AuditConfidence.Medium));
    }

    // ----- MSI ---------------------------------------------------------------

    /// <summary>Classifies the tables an <see cref="IMsiReader"/> read from an MSI into findings.</summary>
    public static AuditReport AuditMsi(string msiPath, IMsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        InputRules.SafePath(msiPath);
        if (!File.Exists(msiPath)) throw new UserInputException("Choose an .msi file that still exists.");
        var tables = reader.Read(msiPath);
        var findings = new List<AuditFinding>();
        var directories = tables.Directories.ToDictionary(d => d.Directory, StringComparer.OrdinalIgnoreCase);
        string DirectoryPath(string? key)
        {
            var parts = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = key;
            while (current is not null && directories.TryGetValue(current, out var row) && seen.Add(current))
            {
                parts.Add(row.DefaultDir.Split(':')[^1]);
                current = row.Parent;
            }
            parts.Reverse();
            return string.Join("\\", parts);
        }
        var fileDirectories = tables.Files.ToDictionary(f => f.FileKey, f => DirectoryPath(f.Directory), StringComparer.OrdinalIgnoreCase);
        foreach (var file in tables.Files)
        {
            var fileName = file.FileName.Split('|')[^1];
            var directory = fileDirectories[file.FileKey];
            var subject = directory.Length == 0 ? fileName : directory + "\\" + fileName;
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                findings.Add(new AuditFinding(AuditFindingKind.Executable, subject, "Executable installed by the MSI.", $"File table row {file.FileKey} (component {file.Component})", AuditConfidence.High));
            if (fileName.EndsWith(".sys", StringComparison.OrdinalIgnoreCase))
                findings.Add(new AuditFinding(AuditFindingKind.ServiceOrDriver, subject,
                    "The MSI installs a driver file. PAL cannot make drivers portable.", $"File table row {file.FileKey} (component {file.Component})", AuditConfidence.High, HardWarning: true));
            if (directory.Contains("AppData", StringComparison.OrdinalIgnoreCase) || directory.Contains("LocalAppData", StringComparison.OrdinalIgnoreCase))
                findings.Add(new AuditFinding(AuditFindingKind.FileDataLocation, subject,
                    "The MSI installs this file into a per-user AppData location: the app keeps data outside its program folder.",
                    $"File table row {file.FileKey} (component {file.Component})", AuditConfidence.High));
        }
        foreach (var row in tables.Registry)
        {
            var hive = row.Root switch { 0 => "HKCR", 1 => "HKCU", 2 => "HKLM", 3 => "HKU", _ => "HKCU/HKLM (per-user or per-machine)" };
            var keyPath = hive + "\\" + row.Key.Replace('/', '\\');
            var valueName = string.IsNullOrEmpty(row.Name) ? "(default)" : row.Name;
            findings.Add(new AuditFinding(AuditFindingKind.RegistryWrite, keyPath + "\\" + valueName,
                $"The MSI writes registry value '{valueName}'" + (row.Value is null ? "." : $" = '{Truncate(row.Value)}'."),
                $"Registry table row {row.RegistryKey} (component {row.Component})", AuditConfidence.High,
                RegistryKeyPath: keyPath, RegistryValueName: string.IsNullOrEmpty(row.Name) ? "" : row.Name,
                RegistryValueData: row.Value));
        }
        foreach (var shortcut in tables.Shortcuts)
            findings.Add(new AuditFinding(AuditFindingKind.Shortcut, shortcut.Name.Split('|')[^1],
                "The MSI creates a shortcut" + (shortcut.Target is null ? "." : $" to '{shortcut.Target}'."),
                $"Shortcut table row {shortcut.Shortcut} (component {shortcut.Component})", AuditConfidence.High));
        foreach (var service in tables.Services)
            findings.Add(new AuditFinding(AuditFindingKind.ServiceOrDriver, service.Name,
                $"The MSI installs the service '{service.DisplayName ?? service.Name}'. PAL cannot make services portable; the converted package will still install and remove this service outside the launcher's cleanup.",
                $"ServiceInstall table row {service.ServiceKey} (component {service.Component})", AuditConfidence.High, HardWarning: true));
        var mainExecutable = tables.Files
            .Select(f => f.FileName.Split('|')[^1])
            .FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        return new AuditReport(AuditReport.CurrentSchemaVersion, Path.GetFullPath(msiPath), AuditSourceKind.MsiPackage, null, mainExecutable, findings);
    }

    // ----- EXE installers ----------------------------------------------------

    /// <summary>The installer family an .exe belongs to, from byte signatures only.</summary>
    public enum InstallerFamily { Unknown, Nsis, InnoSetup, MsiWrapper }

    public static InstallerFamily DetectExeFamily(string exePath)
    {
        var bytes = File.ReadAllBytes(exePath);
        bool Contains(string text)
        {
            var needle = System.Text.Encoding.ASCII.GetBytes(text);
            var wide = System.Text.Encoding.Unicode.GetBytes(text);
            return IndexOf(bytes, needle) >= 0 || IndexOf(bytes, wide) >= 0;
        }
        if (Contains("Inno Setup")) return InstallerFamily.InnoSetup;
        if (Contains("Nullsoft") || Contains("NSIS Error")) return InstallerFamily.Nsis;
        if (Contains("Windows Installer") && Contains(".msi")) return InstallerFamily.MsiWrapper;
        return InstallerFamily.Unknown;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++) if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    /// <summary>
    /// Audits an installer .exe without engines: identifies the family
    /// and records the limit explicitly; the engine-backed overload
    /// (AuditExeWithEnginesAsync) unpacks the payload when a prepared
    /// engine fits, and the runtime audit observes what such an
    /// installer actually does.
    /// </summary>
    public static AuditReport AuditExe(string exePath)
    {
        InputRules.SafePath(exePath);
        if (!File.Exists(exePath)) throw new UserInputException("Choose an installer .exe file that still exists.");
        var family = DetectExeFamily(exePath);
        var familyName = family switch
        {
            InstallerFamily.Nsis => "NSIS",
            InstallerFamily.InnoSetup => "Inno Setup",
            InstallerFamily.MsiWrapper => "an MSI wrapper",
            _ => "an unknown family",
        };
        var findings = new List<AuditFinding>
        {
            new(AuditFindingKind.StaticAuditLimited, Path.GetFileName(exePath),
                $"This installer is {familyName}. AppWrap does not silently unpack {familyName} installers, so the static audit cannot see its file or registry payload. Run the runtime audit to observe the install, or audit the installed folder afterwards.",
                "installer signature scan", family == InstallerFamily.Unknown ? AuditConfidence.Low : AuditConfidence.High),
        };
        return new AuditReport(AuditReport.CurrentSchemaVersion, Path.GetFullPath(exePath), AuditSourceKind.ExeInstaller, null, null, findings);
    }

    private static string Truncate(string value) => value.Length <= 80 ? value : value[..77] + "...";

    [GeneratedRegex(@"[A-Za-z]:\\(?:[^\\""'<>\s|]+\\)*[^\\""'<>\s|]*")]
    private static partial Regex AbsolutePath();
}
