using System.Security.Cryptography;

namespace AppWrap.Core.TestHarness;

/// <summary>One file in a <see cref="TreeSnapshot"/>: byte size and content SHA-256.</summary>
public sealed record TreeFileEntry(long Size, string Sha256);

/// <summary>
/// A deterministic file-list + content-hash snapshot of one watched root, and
/// the diff between two snapshots. This is the harness's write detection:
/// snapshot before launch and after exit, and the diff is the detected writes.
/// It is deliberately coarse (see docs/TEST-HARNESS.md): a file created and
/// deleted again inside the run leaves no trace, and a file rewritten with
/// identical content is not a change. The walk is bounded by the same limits
/// the build layer enforces for a source folder (50,000 entries, 40 nested
/// folders, 20 GB); exceeding one is an actionable error, not a partial result.
/// Linked/reparse entries are rejected via InputRules.SafePath, matching the build.
/// </summary>
public sealed class TreeSnapshot
{
    public const int MaxEntries = 50000;
    public const int MaxDepth = 40;
    public const long MaxBytes = 20L * 1024 * 1024 * 1024;

    private TreeSnapshot(IReadOnlyDictionary<string, TreeFileEntry> files) => Files = files;

    /// <summary>Files by display relative path (forward slashes), ordinal order.</summary>
    public IReadOnlyDictionary<string, TreeFileEntry> Files { get; }

    public static TreeSnapshot Capture(string root, CancellationToken token = default)
    {
        var fullRoot = Path.GetFullPath(root);
        InputRules.SafePath(fullRoot);
        if (!Directory.Exists(fullRoot)) throw new UserInputException("Choose a folder that still exists.");
        var files = new SortedDictionary<string, TreeFileEntry>(StringComparer.Ordinal);
        var entries = 0;
        long bytes = 0;
        Walk(fullRoot, 0);
        void Walk(string directory, int depth)
        {
            if (depth > MaxDepth) throw new UserInputException("This folder has too many nested folders to test.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                InputRules.SafePath(entry);
                if (++entries > MaxEntries) throw new UserInputException("Choose a smaller package to test (up to 50,000 files and folders).");
                if (Directory.Exists(entry)) Walk(entry, depth + 1);
                else
                {
                    var info = new FileInfo(entry);
                    bytes += info.Length;
                    if (bytes > MaxBytes) throw new UserInputException("Choose a package smaller than 20 GB to test.");
                    string hash;
                    using (var stream = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read))
                        hash = Convert.ToHexString(SHA256.HashData(stream));
                    files[DisplayPath(Path.GetRelativePath(fullRoot, entry))] = new(info.Length, hash);
                }
            }
        }
        return new TreeSnapshot(files);
    }

    /// <summary>The changes that turn this (before) snapshot into <paramref name="after"/>, in ordinal path order.</summary>
    public IReadOnlyList<(string Path, WriteChange Change)> Diff(TreeSnapshot after)
    {
        var changes = new List<(string Path, WriteChange Change)>();
        foreach (var (path, entry) in after.Files)
        {
            if (!Files.TryGetValue(path, out var before)) changes.Add((path, WriteChange.Created));
            else if (before != entry) changes.Add((path, WriteChange.Modified));
        }
        foreach (var path in Files.Keys)
            if (!after.Files.ContainsKey(path)) changes.Add((path, WriteChange.Deleted));
        changes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return changes;
    }

    internal static string DisplayPath(string relativeOsPath) => relativeOsPath.Replace('\\', '/');
}
