using AppWrap.Core;

namespace AppWrap.Build;

public sealed record BatchBuildResult(BuildResult Standard, BuildResult Portable, string? CleanupWarning = null);
public enum BatchBuildPhase { SnapshotCaptured, StandardPrepared, PortablePrepared }

public sealed partial class BuildService
{
    public async Task<BatchBuildResult> BuildBothAsync(AppWrapInput standardInput, AppWrapInput portableInput,
        CancellationToken cancellationToken, bool overwriteExisting = false, IProgress<BatchBuildPhase>? progress = null,
        IReadOnlyList<OutputIdentity>? expectedOutputs = null)
    {
        ValidateInput(standardInput); ValidateInput(portableInput);
        if (standardInput.Mode != OutputMode.Standard || portableInput.Mode != OutputMode.PortableApps ||
            !string.Equals(Path.GetFullPath(standardInput.SourceFolder), Path.GetFullPath(portableInput.SourceFolder), StringComparison.OrdinalIgnoreCase) ||
            standardInput.AppName != portableInput.AppName || standardInput.AppVersion != portableInput.AppVersion ||
            standardInput.MainExecutable != portableInput.MainExecutable || standardInput.CreateStartMenuShortcut != portableInput.CreateStartMenuShortcut || standardInput.CreateDesktopShortcut != portableInput.CreateDesktopShortcut)
            throw new UserInputException("Both packages must use the same application and details, with one of each output format.");
        var standardPath = Path.GetFullPath(standardInput.OutputPath); var portablePath = Path.GetFullPath(portableInput.OutputPath);
        if (string.Equals(standardPath, portablePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(standardPath), Path.GetDirectoryName(portablePath), StringComparison.OrdinalIgnoreCase))
            throw new UserInputException("Save both packages with different names in the same destination folder.");
        OutputIdentity[] identities;
        if (expectedOutputs != null)
        {
            if (expectedOutputs.Count != 2 || !string.Equals(Path.GetFullPath(expectedOutputs[0].Path), standardPath, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFullPath(expectedOutputs[1].Path), portablePath, StringComparison.OrdinalIgnoreCase))
                throw new UserInputException("The selected destinations changed. Review the package locations and build again.");
            identities = expectedOutputs.ToArray();
            foreach (var identity in identities) { identity.Verify(); if (identity.Exists && !overwriteExisting) throw new UserInputException("Confirm replacement of the existing packages before building."); }
        }
        else identities = [OutputIdentity.Capture(standardPath, overwriteExisting), OutputIdentity.Capture(portablePath, overwriteExisting)];
        var workRoot = Path.Combine(stateDirectory, "Builds"); InputRules.SafePath(workRoot); Directory.CreateDirectory(workRoot);
        var stage = Path.Combine(workRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        BatchBuildResult? completed = null; Exception? failure = null; bool retainedStage = false;
        try
        {
            var payload = Path.Combine(stage, "payload");
            await Task.Run(() => Snapshot(standardInput.SourceFolder, payload, cancellationToken), cancellationToken).ConfigureAwait(false);
            var captured = CaptureImages(portableInput, stage, payload, cancellationToken);
            progress?.Report(BatchBuildPhase.SnapshotCaptured);
            var standard = await PrepareAsync(standardInput, payload, stage, cancellationToken).ConfigureAwait(false);
            progress?.Report(BatchBuildPhase.StandardPrepared);
            var portable = await PrepareAsync(captured, payload, stage, cancellationToken).ConfigureAwait(false);
            progress?.Report(BatchBuildPhase.PortablePrepared);
            var prepared = new[] { PrepareArtifact(standard, standardPath), PrepareArtifact(portable, portablePath) };
            var published = await BatchArtifactPublisher.PublishAsync(prepared, identities, cancellationToken).ConfigureAwait(false);
            completed = new(published.Results[0], published.Results[1], published.Warning);
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

    private static PreparedArtifact PrepareArtifact(string path, string destination)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 2) throw new BuildFailureException("A packaging component did not produce its package. Try again.", "Missing prepared file: " + path);
        return new(path, destination, OutputIdentity.HashFile(path), new FileInfo(path).Length);
    }
    private static AppWrapInput CaptureImages(AppWrapInput input, string stage, string payload, CancellationToken token)
    {
        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? Capture(string? path, string role)
        {
            if (path == null) return null; token.ThrowIfCancellationRequested(); InputRules.SafePath(path);
            var original = Path.GetFullPath(path);
            if (captured.TryGetValue(original, out var prior)) return prior;
            var source = InputRules.IsWithin(original, input.SourceFolder) ? Path.Combine(payload, Path.GetRelativePath(input.SourceFolder, original)) : original;
            if (new FileInfo(source).Length > 32 * 1024 * 1024) throw new UserInputException("Choose an image smaller than 32 MB.");
            var copy = Path.Combine(stage, role + Path.GetExtension(path)); File.Copy(source, copy); captured.Add(original, copy); return copy;
        }
        return input with { LauncherIconPath = Capture(input.LauncherIconPath, "icon"), SidebarImagePath = Capture(input.SidebarImagePath, "sidebar") };
    }
}
