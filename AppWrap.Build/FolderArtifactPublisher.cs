using AppWrap.Core;
using AppWrap.Core.Projects;

namespace AppWrap.Build;

/// <summary>
/// Existence and content identity of a folder destination, the folder analogue of
/// <see cref="OutputIdentity"/>. Content identity is the deterministic tree hash
/// from <see cref="SourceSnapshot"/>; an existing folder too large to hash is not
/// replaceable, because its contents could not be verified before or after.
/// </summary>
public sealed record FolderOutputIdentity(string Path, bool Exists, string? Hash)
{
    public static FolderOutputIdentity Capture(string path)
    {
        path = System.IO.Path.GetFullPath(path); InputRules.SafePath(path);
        if (File.Exists(path)) throw new UserInputException("A file already uses the package folder name. Choose another destination.");
        if (!Directory.Exists(path)) return new(path, false, null);
        var snapshot = SourceSnapshot.Compute(path);
        if (snapshot.Truncated || snapshot.Sha256 is null) throw new UserInputException("The existing package folder is too large to verify before replacing. Choose a new folder name.");
        return new(path, true, snapshot.Sha256);
    }

    internal void Verify()
    {
        var current = Capture(Path);
        if (current.Exists != Exists || current.Hash != Hash) throw new UserInputException("The destination folder changed while the package was being built. Review the destination and build again.");
    }
}

/// <summary>
/// Transactional publication of one prepared folder, mirroring
/// BatchArtifactPublisher's discipline for files: the staged tree is copied to an
/// owned <c>.appwrap-*.tmp</c> folder beside the destination and hash-verified, the
/// previous destination (if any) is moved to an owned <c>.appwrap-*.backup</c>
/// folder, the pending folder is moved into place, and any failure rolls the
/// original back or reports the exact recovery paths. Cancellation is deferred
/// during the short commit/rollback window, as with file publication.
/// </summary>
internal static class FolderArtifactPublisher
{
    internal static async Task<(BuildResult Result, string? Warning)> PublishAsync(
        string stagedFolder, string stagedHash, long stagedBytes, string destination,
        FolderOutputIdentity original, CancellationToken token,
        Action? beforeReplace = null, Action? beforeRollback = null)
    {
        InputRules.SafePath(destination);
        var parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destination))!;
        string? pending = System.IO.Path.Combine(parent, ".appwrap-" + Guid.NewGuid().ToString("N") + ".tmp");
        string? backup = null; bool committed = false; bool pendingCreated = false;
        var retained = new List<string>(); bool succeeded = false;
        Exception? failure = null;
        try
        {
            var pendingPath = pending;
            await Task.Run(() => BuildService.Snapshot(stagedFolder, pendingPath, token), token).ConfigureAwait(false);
            pendingCreated = true;
            var copy = SourceSnapshot.Compute(pending, token);
            if (copy.Sha256 != stagedHash || copy.TotalBytes != stagedBytes) throw new IOException("Prepared package folder copy did not match its hash.");
            token.ThrowIfCancellationRequested();
            original.Verify();
            // Cancellation is deferred from here until commit or rollback completes.
            try
            {
                if (original.Exists)
                {
                    original.Verify();
                    backup = System.IO.Path.Combine(parent, ".appwrap-" + Guid.NewGuid().ToString("N") + ".backup");
                    Directory.Move(destination, backup);
                }
                beforeReplace?.Invoke();
                InputRules.SafePath(destination);
                Directory.Move(pending, destination); // Never overwrite a newly appeared folder.
                pending = null; committed = true;
                succeeded = true;
            }
            catch (Exception commitFailure)
            {
                var recovery = new List<string>();
                try
                {
                    beforeRollback?.Invoke();
                    if (committed && Directory.Exists(destination))
                    {
                        InputRules.SafePath(destination);
                        var placed = SourceSnapshot.Compute(destination);
                        if (placed.Sha256 != stagedHash) throw new IOException("The new destination changed; automatic rollback will not remove it.");
                        Directory.Delete(destination, recursive: true);
                    }
                    if (backup != null && Directory.Exists(backup))
                    {
                        InputRules.SafePath(destination);
                        var kept = SourceSnapshot.Compute(backup);
                        if (kept.Sha256 != original.Hash) throw new IOException("The recovery backup changed.");
                        Directory.Move(backup, destination);
                        backup = null;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or UserInputException)
                { recovery.Add($"Destination: {destination}\nRecovery backup: {backup ?? "none (original destination did not exist)"}\n{error.Message}"); }
                throw new BuildFailureException(recovery.Count == 0 ? "AppWrap couldn’t save the package folder. Your previous folder was restored. Try another destination." : "AppWrap couldn’t finish saving the package folder. The folder needs recovery; see details before building again.", commitFailure + (recovery.Count == 0 ? "\nRollback completed." : "\nRollback incomplete:\n" + string.Join('\n', recovery)));
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (pendingCreated && pending != null && !DeleteTree(pending)) retained.Add(pending);
            if (succeeded && backup != null && !DeleteOwnedFolder(backup, original.Hash!)) retained.Add(backup);
        }
        if (failure != null)
        {
            if (retained.Count != 0) throw new BuildFailureException("AppWrap stopped, but some temporary folders could not be removed. See details for recovery paths.", (failure is BuildFailureException b ? b.Details : failure.ToString()) + "\nRetained temporary folders:\n" + string.Join('\n', retained));
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        var warning = retained.Count == 0 ? null : "Some owned temporary folders could not be removed:\n" + string.Join('\n', retained);
        return (new BuildResult(destination, stagedHash, stagedBytes, warning), warning);
    }

    private static bool DeleteTree(string path)
    {
        try { InputRules.SafePath(path); if (Directory.Exists(path)) Directory.Delete(path, recursive: true); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or UserInputException) { return false; }
    }

    private static bool DeleteOwnedFolder(string path, string hash)
    {
        try
        {
            if (!Directory.Exists(path)) return true;
            InputRules.SafePath(path);
            var snapshot = SourceSnapshot.Compute(path);
            if (snapshot.Truncated || snapshot.Sha256 != hash) return false;
            Directory.Delete(path, recursive: true); return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or UserInputException) { return false; }
    }
}
