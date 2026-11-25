using AppWrap.Core;
using AppWrap.Core.PortableProfiles;
using AppWrap.Core.Projects;
using AppWrap.Generators;

namespace AppWrap.Build;

public sealed record BuildResult(string OutputPath, string Sha256, long Bytes, string? CleanupWarning = null);

public sealed partial class BuildService(string baseDirectory, string stateDirectory)
{
    public Task<BuildResult> BuildAsync(AppWrapInput input, CancellationToken cancellationToken) => BuildAsync(input, cancellationToken, null);

    public async Task<BuildResult> BuildAsync(AppWrapInput input, CancellationToken cancellationToken, OutputIdentity? expectedOutput)
    {
        ValidateInput(input);
        if (input.Mode == OutputMode.PafPackage)
        {
            // Folder outputs carry their own identity discipline (FolderOutputIdentity);
            // a file identity is only ever checked for the destination it names.
            if (expectedOutput != null && !string.Equals(Path.GetFullPath(expectedOutput.Path), Path.GetFullPath(input.OutputPath), StringComparison.OrdinalIgnoreCase)) throw new UserInputException("The selected destination changed. Choose the save location again.");
            return await BuildPafPackageAsync(input, cancellationToken).ConfigureAwait(false);
        }
        if (expectedOutput != null)
        {
            if (!string.Equals(Path.GetFullPath(expectedOutput.Path), Path.GetFullPath(input.OutputPath), StringComparison.OrdinalIgnoreCase)) throw new UserInputException("The selected destination changed. Choose the save location again.");
            expectedOutput.Verify();
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
            var compiled = await PrepareAsync(input, payload, stage, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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
            expectedOutput?.Verify();
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

    /// <summary>
    /// The PAF Package build: same snapshot → prepare → publish discipline as the
    /// file outputs, except preparation yields a staged folder and publication is
    /// the transactional folder commit in FolderArtifactPublisher. The result's
    /// hash is the folder's deterministic tree hash (SourceSnapshot).
    /// </summary>
    private async Task<BuildResult> BuildPafPackageAsync(AppWrapInput input, CancellationToken cancellationToken)
    {
        var original = FolderOutputIdentity.Capture(input.OutputPath);
        var workRoot = Path.Combine(stateDirectory, "Builds");
        InputRules.SafePath(workRoot);
        Directory.CreateDirectory(workRoot);
        var stage = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        BuildResult? completed = null;
        Exception? failure = null;
        bool retainedStage = false;
        try
        {
            var payload = Path.Combine(stage, "payload");
            await Task.Run(() => Snapshot(input.SourceFolder, payload, cancellationToken), cancellationToken).ConfigureAwait(false);
            var staged = await PrepareAsync(input, payload, stage, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = SourceSnapshot.Compute(staged, cancellationToken);
            if (snapshot.Truncated || snapshot.Sha256 is null) throw new BuildFailureException("The PAF package is too large to verify as a folder. Try the portable package format instead.", "Staged PAF tree exceeded the snapshot bounds.");
            InputRules.SafePath(input.OutputPath);
            original.Verify();
            var published = await FolderArtifactPublisher.PublishAsync(staged, snapshot.Sha256, snapshot.TotalBytes, input.OutputPath, original, cancellationToken).ConfigureAwait(false);
            completed = published.Result;
        }
        catch (Exception ex) { failure = ex; }
        finally { retainedStage = !TryDeleteStage(stage, workRoot); }
        if (failure != null)
        {
            if (retainedStage) throw new BuildFailureException("AppWrap stopped, but temporary source copies could not be removed. See details for recovery paths.", (failure is BuildFailureException b ? b.Details : failure.ToString()) + "\nRetained staging: " + stage);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return completed! with { CleanupWarning = string.Join('\n', new[] { completed!.CleanupWarning, retainedStage ? "Temporary source copies remain at: " + stage : null }.Where(s => s != null)) is var warning && warning.Length != 0 ? warning : null };
    }

    private void ValidateInput(AppWrapInput input)
    {
        InputRules.Validate(input);
        if (input.Mode is not (OutputMode.Standard or OutputMode.PortableApps or OutputMode.PafPackage)) throw new UserInputException("Choose a supported output format.");
        if (input.Profile is { ExecutionKind: PortableExecutionKind.Pal } && input.Mode == OutputMode.Standard)
            throw new UserInputException("Choose the portable package format to use a portable profile.");
        if (input.Mode != OutputMode.PafPackage && Directory.Exists(input.OutputPath)) throw new UserInputException("Choose a file name, rather than a folder, for the package.");
        var sourcePath = Path.GetFullPath(input.SourceFolder);
        if (input.Mode == OutputMode.PafPackage)
        {
            // Committing a folder replaces it wholesale: it must not contain the
            // application folder or AppWrap's own folders.
            var outputPath = Path.GetFullPath(input.OutputPath);
            if (InputRules.IsWithin(sourcePath, outputPath)) throw new UserInputException("Save the PAF package in a folder that does not contain the application folder.");
            foreach (var boundary in new[] { baseDirectory, stateDirectory })
                if (InputRules.IsWithin(Path.GetFullPath(boundary), outputPath)) throw new UserInputException("Save the PAF package in a folder that does not contain AppWrap's own folder.");
        }
        foreach (var boundary in new[] { baseDirectory, stateDirectory })
        {
            var owned = Path.GetFullPath(boundary);
            if (InputRules.IsWithin(owned, sourcePath) || InputRules.IsWithin(sourcePath, owned) || string.Equals(owned, sourcePath, StringComparison.OrdinalIgnoreCase))
                throw new UserInputException("Choose an application folder outside AppWrap's own folder.");
            if (InputRules.IsWithin(input.OutputPath, owned)) throw new UserInputException("Save the package outside AppWrap's own folder.");
        }
    }

    private async Task<string> PrepareAsync(AppWrapInput input, string payload, string stage, CancellationToken cancellationToken)
    {
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
            else if (input.Mode == OutputMode.PafPackage) compiled = await PortableBuild.BuildPafPackageAsync(input, payload, stage, baseDirectory, cancellationToken).ConfigureAwait(false);
            else compiled = await PortableBuild.BuildAsync(input, payload, stage, baseDirectory, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Mode == OutputMode.PafPackage)
            {
                if (!Directory.Exists(compiled)) throw new BuildFailureException("The packaging engine did not produce a package. Try again.", "Expected output folder missing: " + compiled);
            }
            else if (!File.Exists(compiled) || new FileInfo(compiled).Length < 2) throw new BuildFailureException("The packaging engine did not produce a package. Try again.", "Expected output missing: " + compiled);
            return compiled;
    }

    internal static void Snapshot(string source, string destination, CancellationToken token)
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
                // The AppWrap project file at the source root is tooling state, not
                // payload: it records the source snapshot hash and build history and
                // must never be swept into a package. Only the root file is excluded;
                // nested files with the same name remain ordinary payload.
                if (depth == 0 && !Directory.Exists(entry) && ProjectStore.IsProjectFileName(Path.GetFileName(entry))) continue;
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
