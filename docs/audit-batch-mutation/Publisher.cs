using System.Security.Cryptography;
using AppWrap.Core;

namespace AppWrap.Build;

public sealed record OutputIdentity(string Path, bool Exists, string? Hash, long Bytes)
{
    public static OutputIdentity Capture(string path, bool allowExisting = true)
    {
        path = System.IO.Path.GetFullPath(path); InputRules.SafePath(path);
        if (Directory.Exists(path)) throw new UserInputException("A package file name is already used by a folder. Choose another destination.");
        var exists = File.Exists(path);
        if (exists && !allowExisting) throw new UserInputException("A package with this name already exists. Confirm replacement or choose another folder.");
        return new(path, exists, exists ? HashFile(path) : null, exists ? new FileInfo(path).Length : 0);
    }
    internal void Verify()
    {
        var current = Capture(Path, true);
        if (current.Exists != Exists || current.Hash != Hash || current.Bytes != Bytes) throw new UserInputException("A destination file changed while the packages were being built. Review the destination and build again.");
    }
    internal static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}

internal sealed record PreparedArtifact(string Path, string Destination, string Hash, long Bytes);

internal static class BatchArtifactPublisher
{
    internal static async Task<(BuildResult[] Results, string? Warning)> PublishAsync(
        PreparedArtifact[] artifacts, OutputIdentity[] original, CancellationToken token,
        Action<int>? beforeReplace = null, Action<int>? beforeRollback = null)
    {
        if (artifacts.Length != 2 || original.Length != 2 || artifacts.Select(a => System.IO.Path.GetFullPath(a.Destination)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            throw new UserInputException("Choose two different package file names.");
        var pending = new string?[2]; var backups = new string?[2]; var committed = new bool[2]; var pendingCreated = new bool[2];
        var retained = new List<string>(); bool succeeded = false;
        Exception? failure = null;
        try
        {
            for (int i = 0; i < 2; i++)
            {
                InputRules.SafePath(artifacts[i].Destination);
                pending[i] = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(artifacts[i].Destination)!, ".appwrap-" + Guid.NewGuid().ToString("N") + ".tmp");
                await using (var source = File.OpenRead(artifacts[i].Path))
                {
                    await using var target = new FileStream(pending[i]!, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    pendingCreated[i] = true;
                    await source.CopyToAsync(target, token).ConfigureAwait(false); await target.FlushAsync(token).ConfigureAwait(false);
                }
                if (OutputIdentity.HashFile(pending[i]!) != artifacts[i].Hash) throw new IOException("Prepared package copy did not match its hash.");
            }
            token.ThrowIfCancellationRequested();
            foreach (var identity in original) identity.Verify();
            // Cancellation is deferred from here until commit or rollback completes.
            try
            {
                for (int i = 0; i < 2; i++)
                    if (original[i].Exists)
                    {
                        original[i].Verify();
                        backups[i] = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(artifacts[i].Destination)!, ".appwrap-" + Guid.NewGuid().ToString("N") + ".backup");
                        File.Move(artifacts[i].Destination, backups[i]!);
                    }
                for (int i = 0; i < 2; i++)
                {
                    beforeReplace?.Invoke(i);
                    InputRules.SafePath(artifacts[i].Destination);
                    File.Move(pending[i]!, artifacts[i].Destination); // Never overwrite a newly appeared file.
                    pending[i] = null; committed[i] = true;
                }
                succeeded = true;
            }
            catch (Exception commitFailure)
            {
                var recovery = new List<string>();
                for (int i = 1; i >= 0; i--)
                {
                    try
                    {
                        beforeRollback?.Invoke(i);
                        if (committed[i] && File.Exists(artifacts[i].Destination))
                        {
                            InputRules.SafePath(artifacts[i].Destination);
                            if (OutputIdentity.HashFile(artifacts[i].Destination) != artifacts[i].Hash) throw new IOException("The new destination changed; automatic rollback will not remove it.");
                            File.Delete(artifacts[i].Destination);
                        }
                        if (backups[i] != null && File.Exists(backups[i]))
                        {
                            InputRules.SafePath(artifacts[i].Destination);
                            if (OutputIdentity.HashFile(backups[i]!) != original[i].Hash) throw new IOException("The recovery backup changed.");
                            File.Move(backups[i]!, artifacts[i].Destination);
                            backups[i] = null;
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or UserInputException)
                    { recovery.Add($"Destination: {artifacts[i].Destination}\nRecovery backup: {backups[i] ?? "none (original destination did not exist)"}\n{error.Message}"); }
                }
                throw new BuildFailureException(recovery.Count == 0 ? "AppWrap couldn’t save both packages. Your previous files were restored. Try another destination." : "AppWrap couldn’t finish saving both packages. Some files need recovery; see details before building again.", commitFailure + (recovery.Count == 0 ? "\nRollback completed." : "\nRollback incomplete:\n" + string.Join('\n', recovery)));
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            for (int i = 0; i < 2; i++)
            {
                if (pendingCreated[i] && pending[i] != null && !DeletePending(pending[i]!)) retained.Add(pending[i]!);
                if (succeeded && backups[i] != null && !DeleteOwned(backups[i]!, original[i].Hash!)) retained.Add(backups[i]!);
            }
        }
        if (failure != null)
        {
            if (retained.Count != 0) throw new BuildFailureException("AppWrap stopped, but some temporary output files could not be removed. See details for recovery paths.", (failure is BuildFailureException b ? b.Details : failure.ToString()) + "\nRetained temporary files:\n" + string.Join('\n', retained));
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        var warning = retained.Count == 0 ? null : "Some owned temporary files could not be removed:\n" + string.Join('\n', retained);
        return (artifacts.Select(a => new BuildResult(a.Destination, a.Hash, a.Bytes, warning)).ToArray(), warning);
    }

    private static bool DeletePending(string path)
    {
        try { InputRules.SafePath(path); File.Delete(path); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or UserInputException) { return false; }
    }

    private static bool DeleteOwned(string path, string hash)
    {
        try { if (!File.Exists(path)) return true; InputRules.SafePath(path); if (OutputIdentity.HashFile(path) != hash) return false; File.Delete(path); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or UserInputException) { return false; }
    }
}
