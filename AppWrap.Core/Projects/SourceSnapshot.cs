using System.Security.Cryptography;
using System.Text;

namespace AppWrap.Core.Projects;

/// <summary>
/// The result of a bounded source-tree snapshot. <see cref="Sha256"/> is null exactly
/// when <see cref="Truncated"/> is true: the tree exceeded the snapshot bounds, so no
/// hash is claimed rather than hashing a partial walk.
/// </summary>
public sealed record SourceSnapshot(string? Sha256, bool Truncated, int FileCount, long TotalBytes)
{
    /// <summary>
    /// Computes a deterministic hash over the source tree: entries are visited in
    /// ordinal relative-path order and each file contributes its relative path, byte
    /// size and content SHA-256. The walk is bounded by the same limits the build
    /// layer enforces for a source folder (50,000 entries, 40 nested folders, 20 GB —
    /// see BuildService.Snapshot); exceeding any bound stops the walk and returns a
    /// truncated marker. The project file itself (appwrap.project.json at the root)
    /// is excluded: it records this hash, so it can never be an input to it.
    /// Linked/reparse entries are rejected via InputRules.SafePath, matching the build.
    /// </summary>
    public static SourceSnapshot Compute(string root, CancellationToken token = default)
    {
        const int maxEntries = 50000;
        const int maxDepth = 40;
        const long maxBytes = 20L * 1024 * 1024 * 1024;
        var fullRoot = Path.GetFullPath(root);
        InputRules.SafePath(fullRoot);
        if (!Directory.Exists(fullRoot)) throw new UserInputException("Choose an application folder that still exists.");
        var files = new List<(string Relative, string Full)>();
        var entries = 0;
        var truncated = false;
        Walk(fullRoot, 0);
        void Walk(string directory, int depth)
        {
            if (truncated) return;
            if (depth > maxDepth) { truncated = true; return; }
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (truncated) return;
                InputRules.SafePath(entry);
                if (++entries > maxEntries) { truncated = true; return; }
                if (depth == 0 && !Directory.Exists(entry) && ProjectStore.IsProjectFileName(Path.GetFileName(entry))) continue;
                if (Directory.Exists(entry)) Walk(entry, depth + 1);
                else files.Add((PafLayout.ToDisplayPath(Path.GetRelativePath(fullRoot, entry)), entry));
            }
        }
        if (truncated) return new SourceSnapshot(null, true, 0, 0);
        files.Sort((a, b) => string.CompareOrdinal(a.Relative, b.Relative));
        long totalBytes = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var line = new StringBuilder();
        foreach (var (relative, full) in files)
        {
            token.ThrowIfCancellationRequested();
            var info = new FileInfo(full);
            totalBytes += info.Length;
            if (totalBytes > maxBytes) return new SourceSnapshot(null, true, 0, 0);
            string contentHash;
            using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                contentHash = Convert.ToHexString(SHA256.HashData(stream));
            line.Clear();
            line.Append(relative).Append('\t').Append(info.Length).Append('\t').Append(contentHash).Append('\n');
            hash.AppendData(Encoding.UTF8.GetBytes(line.ToString()));
        }
        return new SourceSnapshot(Convert.ToHexString(hash.GetHashAndReset()), false, files.Count, totalBytes);
    }
}
