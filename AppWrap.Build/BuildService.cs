using AppWrap.Core;
using AppWrap.Generators;

namespace AppWrap.Build;

public sealed class BuildFailureException(string message, string details) : Exception(message)
{
    public string Details { get; } = details;
}

public sealed record BuildResult(string OutputPath, string Sha256, long Bytes, string? CleanupWarning = null);

public sealed class BuildService(string baseDirectory, string stateDirectory)
{
    public async Task<BuildResult> BuildAsync(AppWrapInput input, CancellationToken cancellationToken)
    {
        InputRules.Validate(input);
        var sourcePath = Path.GetFullPath(input.SourceFolder);
        foreach (var boundary in new[] { baseDirectory, stateDirectory })
        {
            var owned = Path.GetFullPath(boundary);
            if (InputRules.IsWithin(owned, sourcePath) || InputRules.IsWithin(sourcePath, owned) || string.Equals(owned, sourcePath, StringComparison.OrdinalIgnoreCase))
                throw new UserInputException("Choose an application folder outside AppWrap's own folder.");
            if (InputRules.IsWithin(input.OutputPath, owned)) throw new UserInputException("Save the package outside AppWrap's own folder.");
        }
        var workRoot = Path.Combine(stateDirectory, "Builds");
        InputRules.SafePath(workRoot);
        Directory.CreateDirectory(workRoot);
        var stage = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string? pending = null;
        BuildResult? completed = null;
        Exception? failure = null;
        var retained = new List<string>();
        try
        {
            var payload = Path.Combine(stage, "payload");
            await Task.Run(() => Snapshot(input.SourceFolder, payload, cancellationToken), cancellationToken).ConfigureAwait(false);
            var output = Path.Combine(stage, "output");
            Directory.CreateDirectory(output);
            string compiled;
            if (input.Mode == OutputMode.Standard)
            {
                var compiler = Path.Combine(baseDirectory, "third_party", "innosetup", "ISCC.exe");
                if (!File.Exists(compiler)) throw new BuildFailureException("A packaging component is missing. Download a complete copy of AppWrap and try again.", "Missing bundled compiler: " + compiler);
                var script = Path.Combine(stage, "package.iss");
                await File.WriteAllTextAsync(script, InnoSetupGenerator.Generate(input, payload, output), new System.Text.UTF8Encoding(true), cancellationToken).ConfigureAwait(false);
                var result = await ProcessRunner.RunAsync(compiler, ["--quiet", "--no-ide-signtools", "--no-signing", script], stage, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0) throw new BuildFailureException("AppWrap couldn’t build the installer. Try closing running copies of the app, check the app folder, and build again.", $"Compiler exit code: {result.ExitCode}\n{result.Log}");
                compiled = Path.Combine(output, "package.exe");
            }
            else compiled = await PortableBuild.BuildAsync(input, payload, stage, baseDirectory, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(compiled) || new FileInfo(compiled).Length < 2) throw new BuildFailureException("The packaging engine did not produce a package. Try again.", "Expected output missing: " + compiled);
            InputRules.SafePath(input.OutputPath);
            pending = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input.OutputPath))!, ".appwrap-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var source = File.OpenRead(compiled))
            await using (var target = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string hash;
            await using (var hashStream = File.OpenRead(pending))
                hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(hashStream, cancellationToken).ConfigureAwait(false));
            var bytes = new FileInfo(pending).Length;
            InputRules.SafePath(input.OutputPath);
            File.Move(pending, input.OutputPath, overwrite: true);
            pending = null;
            completed = new(input.OutputPath, hash, bytes);
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            if (pending != null && !TryDeleteFile(pending)) retained.Add(pending);
            if (!TryDeleteStage(stage, workRoot)) retained.Add(stage);
        }
        if (failure != null)
        {
            if (retained.Count != 0) throw new BuildFailureException("AppWrap stopped, but some temporary files could not be removed. Close any apps using them, then remove the files listed in details.", failure + "\nRetained owned paths:\n" + string.Join('\n', retained));
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return completed! with { CleanupWarning = retained.Count == 0 ? null : "Your package is ready, but temporary source copies remain at:\n" + string.Join('\n', retained) };
    }

    private static void Snapshot(string source, string destination, CancellationToken token)
    {
        long bytes = 0; int entries = 0;
        Copy(source, destination, 0);
        void Copy(string from, string to, int depth)
        {
            if (depth > 40) throw new UserInputException("This app has too many nested folders.");
            InputRules.SafePath(from); Directory.CreateDirectory(to);
            foreach (var entry in Directory.EnumerateFileSystemEntries(from))
            {
                token.ThrowIfCancellationRequested();
                InputRules.SafePath(entry);
                if (++entries > 50000) throw new UserInputException("Choose a smaller app folder (up to 50,000 files and folders).");
                var target = Path.Combine(to, Path.GetFileName(entry));
                if (Directory.Exists(entry)) Copy(entry, target, depth + 1);
                else
                {
                    bytes += new FileInfo(entry).Length;
                    if (bytes > 20L * 1024 * 1024 * 1024) throw new UserInputException("Choose an app folder smaller than 20 GB.");
                    using var reader = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var writer = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var buffer = new byte[81920]; int count;
                    while ((count = reader.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); writer.Write(buffer, 0, count); }
                }
            }
        }
    }

    private static bool TryDeleteFile(string file) { try { InputRules.SafePath(file); File.Delete(file); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } catch (UserInputException) { return false; } }
    private static bool TryDeleteStage(string stage, string root)
    {
        try
        {
            if (!InputRules.IsWithin(stage, root)) return false;
            InputRules.SafePath(stage);
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            return true;
        }
        catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } catch (UserInputException) { return false; }
    }
}
