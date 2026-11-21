using System.IO;
using System.Text.Json;
using AppWrap.Build;
using AppWrap.Core;
using AppWrap.Generators;

internal static class BatchChecks
{
    private sealed class InlineProgress(Action<BatchBuildPhase> report) : IProgress<BatchBuildPhase>
    { public void Report(BatchBuildPhase value) => report(value); }

    public static async Task Run(string root, string evidence, AppWrapInput input, BuildService service,
        string? sevenZip, Action<string, bool> check)
    {
        var batchRoot = Path.Combine(evidence, "batch"); Directory.CreateDirectory(batchRoot);
        var standard = input with { Mode = OutputMode.Standard, OutputPath = Path.Combine(batchRoot, "paired-Setup.exe"), StandardOptions = new(false, true, true, "http://127.0.0.1:18765/both-only") };
        var portable = input with { Mode = OutputMode.PortableApps, OutputPath = Path.Combine(batchRoot, "paired-Portable.paf.exe"), StandardOptions = new(OfferDeveloperWebsite: true, DeveloperWebsite: "ignored invalid portable option") };
        var suppliedIcon = Path.Combine(batchRoot, "selected-icon.png");
        var suppliedSidebar = Path.Combine(batchRoot, "selected-sidebar.bmp");
        File.Copy(Path.Combine(evidence, "images", "appicon_128.png"), suppliedIcon);
        File.Copy(Path.Combine(evidence, "images", "sidebar.bmp"), suppliedSidebar);
        portable = portable with { LauncherIconPath = suppliedIcon, SidebarImagePath = suppliedSidebar };
        var originalIcon = File.ReadAllBytes(suppliedIcon); var originalSidebar = File.ReadAllBytes(suppliedSidebar);
        var marker = Path.Combine(input.SourceFolder, "batch-marker.txt");
        var originalMarker = File.Exists(marker) ? File.ReadAllBytes(marker) : null;
        const string capturedMarker = "Captured original bytes before standard and portable compilation.";
        var phases = new List<BatchBuildPhase>();
        try
        {
            File.WriteAllText(marker, capturedMarker);
            var result = await service.BuildBothAsync(standard, portable, CancellationToken.None, progress: new InlineProgress(phase =>
            {
                phases.Add(phase);
                if (phase == BatchBuildPhase.SnapshotCaptured)
                {
                    File.WriteAllText(marker, "Changed live source after capture; must not be packaged.");
                    File.WriteAllText(suppliedIcon, "Changed after selected image capture.");
                    File.WriteAllText(suppliedSidebar, "Changed after selected image capture.");
                }
                if (phase == BatchBuildPhase.StandardPrepared)
                {
                    var stage = Directory.GetDirectories(Path.Combine(evidence, "state", "Builds")).Single();
                    var stagedMarker = Path.Combine(stage, "payload", "batch-marker.txt");
                    check("batch standard prepared from captured bytes", File.ReadAllText(stagedMarker) == capturedMarker);
                    check("batch standard script uses owned snapshot", File.ReadAllText(Path.Combine(stage, "package.iss")).Contains(Path.Combine(stage, "payload"), StringComparison.Ordinal));
                    var generated = File.ReadAllText(Path.Combine(stage, "package.iss"));
                    check("batch preserves standard-only finish options independently of portable options", generated.Contains("StartupOffered = True", StringComparison.Ordinal) && generated.Contains("127.0.0.1:18765/both-only", StringComparison.Ordinal) && !generated.Contains("Flags: nowait postinstall", StringComparison.Ordinal));
                }
            }));
            check("batch actual standard artifact", result.Standard.OutputPath == standard.OutputPath && result.Standard.Bytes > 10000 && File.Exists(standard.OutputPath));
            check("batch actual portable artifact", result.Portable.OutputPath == portable.OutputPath && result.Portable.Bytes > 10000 && File.Exists(portable.OutputPath));
            check("batch hashes describe published files", OutputIdentity.Capture(standard.OutputPath).Hash == result.Standard.Sha256 && OutputIdentity.Capture(portable.OutputPath).Hash == result.Portable.Sha256);
            check("batch source captured once with ordered preparation", phases.SequenceEqual(new[] { BatchBuildPhase.SnapshotCaptured, BatchBuildPhase.StandardPrepared, BatchBuildPhase.PortablePrepared }));
            check("batch live source mutation occurred", File.ReadAllText(marker) != capturedMarker);
            check("batch selected images captured before mutation", !File.ReadAllBytes(suppliedIcon).SequenceEqual(originalIcon) && !File.ReadAllBytes(suppliedSidebar).SequenceEqual(originalSidebar));
            if (!string.IsNullOrWhiteSpace(sevenZip))
            {
                var extracted = Path.Combine(batchRoot, "extracted");
                var extraction = await ProcessRunner.RunAsync(sevenZip, ["x", "-y", "-o" + extracted, portable.OutputPath], batchRoot, CancellationToken.None);
                check("batch portable static extraction", extraction.ExitCode == 0);
                check("batch portable uses same captured original bytes", File.ReadAllText(Path.Combine(extracted, "App", "Payload", "batch-marker.txt")) == capturedMarker);
                check("batch portable selected executable retained", File.Exists(Path.Combine(extracted, "App", "Payload", input.MainExecutable)));
                check("batch portable root launcher retained", File.Exists(Path.Combine(extracted, PortableAppsGenerator.PackageId(input) + ".exe")));
                var portableMetadata = File.ReadAllText(Path.Combine(extracted, "App", "AppInfo", "appinfo.ini"));
                check("batch portable metadata excludes standard browser startup settings", !portableMetadata.Contains("both-only", StringComparison.Ordinal) && !portableMetadata.Contains("StartWithWindows", StringComparison.Ordinal));
            }
            File.WriteAllText(Path.Combine(batchRoot, "artifacts.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (originalMarker != null) File.WriteAllBytes(marker, originalMarker);
            else File.Delete(marker);
            File.WriteAllBytes(suppliedIcon, originalIcon); File.WriteAllBytes(suppliedSidebar, originalSidebar);
        }

        // The same selected file may be both images and may live inside the app source.
        // CaptureImages must reuse the owned payload snapshot, not reopen the live file.
        var sourceImage = Path.Combine(input.SourceFolder, "batch-selected-image.png");
        var priorSourceImage = File.Exists(sourceImage) ? File.ReadAllBytes(sourceImage) : null;
        var sourceImageBytes = File.ReadAllBytes(Path.Combine(evidence, "images", "appicon_128.png"));
        var insideStandard = standard with { OutputPath = Path.Combine(batchRoot, "inside-image-Setup.exe") };
        var insidePortable = portable with { OutputPath = Path.Combine(batchRoot, "inside-image-Portable.paf.exe"), LauncherIconPath = sourceImage, SidebarImagePath = sourceImage };
        int imageSnapshotCount = 0;
        try
        {
            File.WriteAllBytes(sourceImage, sourceImageBytes);
            var insideResult = await service.BuildBothAsync(insideStandard, insidePortable, CancellationToken.None,
                progress: new InlineProgress(phase =>
                {
                    if (phase == BatchBuildPhase.SnapshotCaptured)
                    {
                        imageSnapshotCount++;
                        File.WriteAllText(sourceImage, "Live image changed after shared source and image capture.");
                    }
                    if (phase == BatchBuildPhase.StandardPrepared)
                    {
                        var stage = Directory.GetDirectories(Path.Combine(evidence, "state", "Builds")).Single();
                        check("batch source-contained image standard bytes frozen", File.ReadAllBytes(Path.Combine(stage, "payload", "batch-selected-image.png")).SequenceEqual(sourceImageBytes));
                    }
                }));
            check("batch duplicate source-contained image selection compiles both", imageSnapshotCount == 1 && insideResult.Standard.Bytes > 10000 && insideResult.Portable.Bytes > 10000);
            check("batch duplicate selected image live mutation occurred", !File.ReadAllBytes(sourceImage).SequenceEqual(sourceImageBytes));
            if (!string.IsNullOrWhiteSpace(sevenZip))
            {
                var extracted = Path.Combine(batchRoot, "inside-image-extracted");
                var extraction = await ProcessRunner.RunAsync(sevenZip, ["x", "-y", "-o" + extracted, insidePortable.OutputPath], batchRoot, CancellationToken.None);
                check("batch source-contained image portable extraction", extraction.ExitCode == 0);
                check("batch source-contained image portable bytes frozen", File.ReadAllBytes(Path.Combine(extracted, "App", "Payload", "batch-selected-image.png")).SequenceEqual(sourceImageBytes));
            }
            File.WriteAllText(Path.Combine(batchRoot, "inside-image-artifacts.json"), JsonSerializer.Serialize(insideResult, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (priorSourceImage != null) File.WriteAllBytes(sourceImage, priorSourceImage);
            else File.Delete(sourceImage);
        }

        var protectedStandard = standard with { OutputPath = Path.Combine(batchRoot, "protected-standard.exe") };
        var protectedPortable = portable with { OutputPath = Path.Combine(batchRoot, "protected-portable.paf.exe") };
        File.WriteAllText(protectedStandard.OutputPath, "old standard"); File.WriteAllText(protectedPortable.OutputPath, "old portable");
        bool OriginalsIntact() => File.ReadAllText(protectedStandard.OutputPath) == "old standard" && File.ReadAllText(protectedPortable.OutputPath) == "old portable";
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); bool stopped = false;
            try { await service.BuildBothAsync(protectedStandard, protectedPortable, cancelled.Token, overwriteExisting: true); }
            catch (OperationCanceledException) { stopped = true; }
            check("batch pre-cancelled operation preserves both old outputs", stopped && OriginalsIntact());
        }

        var incompleteBackend = Path.Combine(batchRoot, "incomplete-tools");
        CopyTree(Path.Combine(root, "third_party", "innosetup"), Path.Combine(incompleteBackend, "third_party", "innosetup"));
        var missingPortableService = new BuildService(incompleteBackend, Path.Combine(batchRoot, "failure-state"));
        bool firstPrepared = false, preparationFailed = false;
        try
        {
            await missingPortableService.BuildBothAsync(protectedStandard, protectedPortable, CancellationToken.None, overwriteExisting: true,
                progress: new InlineProgress(phase => { if (phase == BatchBuildPhase.StandardPrepared) firstPrepared = true; }));
        }
        catch (BuildFailureException error) { preparationFailed = error.Details.Contains("Missing portable packaging backend", StringComparison.Ordinal); }
        check("batch second preparation failure occurs after real standard compile", firstPrepared && preparationFailed);
        check("batch second preparation failure preserves both old outputs", OriginalsIntact());

        using (var cancellation = new CancellationTokenSource())
        {
            bool stopped = false;
            try
            {
                await service.BuildBothAsync(protectedStandard, protectedPortable, cancellation.Token, overwriteExisting: true,
                    progress: new InlineProgress(phase => { if (phase == BatchBuildPhase.StandardPrepared) cancellation.Cancel(); }));
            }
            catch (OperationCanceledException) { stopped = true; }
            check("batch cancellation after first preparation preserves both outputs", stopped && OriginalsIntact());
        }
        using (var cancellation = new CancellationTokenSource())
        {
            bool stopped = false, bothPrepared = false;
            try
            {
                await service.BuildBothAsync(protectedStandard, protectedPortable, cancellation.Token, overwriteExisting: true,
                    progress: new InlineProgress(phase => { if (phase == BatchBuildPhase.PortablePrepared) { bothPrepared = true; cancellation.Cancel(); } }));
            }
            catch (OperationCanceledException) { stopped = true; }
            check("batch cancellation after both preparations preserves outputs before commit", bothPrepared && stopped && OriginalsIntact());
        }

        var lateApproval = new[] { OutputIdentity.Capture(protectedStandard.OutputPath), OutputIdentity.Capture(protectedPortable.OutputPath) };
        bool lateMutationReached = false, lateMutationRejected = false;
        try
        {
            try
            {
                await service.BuildBothAsync(protectedStandard, protectedPortable, CancellationToken.None, overwriteExisting: true,
                    expectedOutputs: lateApproval, progress: new InlineProgress(phase =>
                    {
                        if (phase != BatchBuildPhase.PortablePrepared) return;
                        lateMutationReached = true;
                        File.WriteAllText(protectedStandard.OutputPath, "new external content");
                    }));
            }
            catch (UserInputException) { lateMutationRejected = true; }
            check("batch final precommit identity check protects late external change", lateMutationReached && lateMutationRejected &&
                File.ReadAllText(protectedStandard.OutputPath) == "new external content" && File.ReadAllText(protectedPortable.OutputPath) == "old portable");
        }
        finally { File.WriteAllText(protectedStandard.OutputPath, "old standard"); }

        await ExpectInput("batch wrong output-mode pair rejected", () => service.BuildBothAsync(protectedStandard, protectedPortable with { Mode = OutputMode.Standard }, CancellationToken.None, true));
        await ExpectInput("single build invalid mode rejected before backend launch", () => service.BuildAsync(protectedStandard with { Mode = (OutputMode)999 }, CancellationToken.None));
        await ExpectInput("batch reversed output modes rejected", () => service.BuildBothAsync(protectedStandard with { Mode = OutputMode.PortableApps }, protectedPortable with { Mode = OutputMode.Standard }, CancellationToken.None, true));
        await ExpectInput("batch duplicate portable modes rejected", () => service.BuildBothAsync(protectedStandard with { Mode = OutputMode.PortableApps }, protectedPortable, CancellationToken.None, true));
        await ExpectInput("batch mismatched app details rejected", () => service.BuildBothAsync(protectedStandard, protectedPortable with { AppVersion = "9.0" }, CancellationToken.None, true));
        await ExpectInput("batch duplicate canonical outputs rejected", () => service.BuildBothAsync(protectedStandard, protectedPortable with { OutputPath = protectedStandard.OutputPath.ToUpperInvariant() }, CancellationToken.None, true));
        await ExpectInput("batch unconfirmed existing outputs rejected", () => service.BuildBothAsync(protectedStandard, protectedPortable, CancellationToken.None));
        var approved = new[] { OutputIdentity.Capture(protectedStandard.OutputPath), OutputIdentity.Capture(protectedPortable.OutputPath) };
        File.WriteAllText(protectedStandard.OutputPath, "changed since confirmation");
        await ExpectInput("batch changed existing target invalidates consent", () => service.BuildBothAsync(protectedStandard, protectedPortable, CancellationToken.None, true, expectedOutputs: approved));
        check("batch stale consent never overwrites changed target", File.ReadAllText(protectedStandard.OutputPath) == "changed since confirmation");
        var absentStandard = standard with { OutputPath = Path.Combine(batchRoot, "appeared-standard.exe") };
        var absentPortable = portable with { OutputPath = Path.Combine(batchRoot, "appeared-portable.paf.exe") };
        var absentApproval = new[] { OutputIdentity.Capture(absentStandard.OutputPath), OutputIdentity.Capture(absentPortable.OutputPath) };
        File.WriteAllText(absentPortable.OutputPath, "new unrelated file");
        await ExpectInput("batch newly appeared target invalidates consent", () => service.BuildBothAsync(absentStandard, absentPortable, CancellationToken.None, true, expectedOutputs: absentApproval));
        check("batch appeared target retained", File.ReadAllText(absentPortable.OutputPath) == "new unrelated file" && !File.Exists(absentStandard.OutputPath));
        await ExpectInput("batch wrong consent arity rejected", () => service.BuildBothAsync(protectedStandard, protectedPortable, CancellationToken.None, true, expectedOutputs: [approved[0]]));
        await ExpectInput("batch consent paths must match destinations", () => service.BuildBothAsync(protectedStandard, protectedPortable, CancellationToken.None, true, expectedOutputs: [approved[1], approved[0]]));

        await PublisherChecks(batchRoot, check);

        async Task ExpectInput(string name, Func<Task> action)
        {
            try { await action(); }
            catch (UserInputException) { check(name, true); return; }
            throw new Exception("FAIL: " + name);
        }
    }

    private static async Task PublisherChecks(string batchRoot, Action<string, bool> check)
    {
        async Task FaultCase(string name, bool originalsExist, bool rollbackFails)
        {
            var directory = Path.Combine(batchRoot, name); Directory.CreateDirectory(directory);
            var destinations = new[] { Path.Combine(directory, "standard.exe"), Path.Combine(directory, "portable.paf.exe") };
            if (originalsExist) { File.WriteAllText(destinations[0], "original zero"); File.WriteAllText(destinations[1], "original one"); }
            var original = destinations.Select(p => OutputIdentity.Capture(p)).ToArray();
            var artifacts = new PreparedArtifact[2];
            for (int i = 0; i < 2; i++)
            {
                var prepared = Path.Combine(directory, "prepared-" + i); File.WriteAllText(prepared, "new artifact " + i);
                artifacts[i] = new(prepared, destinations[i], OutputIdentity.HashFile(prepared), new FileInfo(prepared).Length);
            }
            BuildFailureException? error = null;
            try
            {
                await BatchArtifactPublisher.PublishAsync(artifacts, original, CancellationToken.None,
                    beforeReplace: index => { if (index == 1) throw new IOException("Owned forced second publish failure"); },
                    beforeRollback: rollbackFails ? index => { if (index == 0) throw new IOException("Owned forced first rollback failure"); } : null);
            }
            catch (BuildFailureException failure) { error = failure; }
            check("batch publisher reports forced failure " + name, error != null);
            if (rollbackFails)
            {
                var backups = Directory.GetFiles(directory, ".appwrap-*.backup");
                check("batch failed rollback retains old backup", backups.Length == 1 && File.ReadAllText(backups[0]) == "original zero");
                check("batch failed rollback reports exact recovery path", error!.Details.Contains("Rollback incomplete", StringComparison.Ordinal) && error.Details.Contains(backups[0], StringComparison.Ordinal));
                check("batch failed rollback preserves second original", File.ReadAllText(destinations[1]) == "original one");
            }
            else if (originalsExist)
                check("batch handled second publish failure restores originals byte-identically", File.ReadAllText(destinations[0]) == "original zero" && File.ReadAllText(destinations[1]) == "original one");
            else check("batch handled publish failure removes only newly committed absent targets", destinations.All(p => !File.Exists(p)));
            check("batch publisher cleans pending files " + name, Directory.GetFiles(directory, ".appwrap-*.tmp").Length == 0);
            if (!rollbackFails) check("batch successful rollback cleans backups " + name, Directory.GetFiles(directory, ".appwrap-*.backup").Length == 0);
        }
        await FaultCase("rollback-existing", true, false);
        await FaultCase("rollback-absent", false, false);
        await FaultCase("rollback-incomplete", true, true);

        var foreignDirectory = Path.Combine(batchRoot, "rollback-foreign"); Directory.CreateDirectory(foreignDirectory);
        var foreignDestinations = new[] { Path.Combine(foreignDirectory, "standard.exe"), Path.Combine(foreignDirectory, "portable.paf.exe") };
        File.WriteAllText(foreignDestinations[0], "old foreign zero"); File.WriteAllText(foreignDestinations[1], "old foreign one");
        var foreignOriginal = foreignDestinations.Select(p => OutputIdentity.Capture(p)).ToArray();
        var foreignArtifacts = CreateArtifacts(foreignDirectory, foreignDestinations);
        BuildFailureException? foreignError = null;
        try
        {
            await BatchArtifactPublisher.PublishAsync(foreignArtifacts, foreignOriginal, CancellationToken.None,
                beforeReplace: index => { if (index == 1) { File.WriteAllText(foreignDestinations[0], "foreign replacement"); throw new IOException("Owned simulated concurrent replacement"); } });
        }
        catch (BuildFailureException error) { foreignError = error; }
        var foreignBackups = Directory.GetFiles(foreignDirectory, ".appwrap-*.backup");
        check("batch rollback never removes concurrently replaced foreign target", foreignError != null && File.ReadAllText(foreignDestinations[0]) == "foreign replacement");
        check("batch foreign replacement retains original recovery backup", foreignBackups.Length == 1 && File.ReadAllText(foreignBackups[0]) == "old foreign zero" && foreignError!.Details.Contains(foreignBackups[0], StringComparison.Ordinal));
        check("batch foreign replacement leaves second original intact", File.ReadAllText(foreignDestinations[1]) == "old foreign one");

        var deferredDirectory = Path.Combine(batchRoot, "cancel-during-commit"); Directory.CreateDirectory(deferredDirectory);
        var deferredDestinations = new[] { Path.Combine(deferredDirectory, "standard.exe"), Path.Combine(deferredDirectory, "portable.paf.exe") };
        File.WriteAllText(deferredDestinations[0], "old deferred zero"); File.WriteAllText(deferredDestinations[1], "old deferred one");
        var deferredOriginal = deferredDestinations.Select(p => OutputIdentity.Capture(p)).ToArray();
        var deferredArtifacts = CreateArtifacts(deferredDirectory, deferredDestinations);
        using var deferredCancellation = new CancellationTokenSource();
        var deferredResult = await BatchArtifactPublisher.PublishAsync(deferredArtifacts, deferredOriginal, deferredCancellation.Token,
            beforeReplace: index => { if (index == 0) deferredCancellation.Cancel(); });
        check("batch cancellation deferred through both replacements", deferredCancellation.IsCancellationRequested && deferredResult.Results.Length == 2 && deferredDestinations.Select(File.ReadAllText).SequenceEqual(new[] { "new deferred 0", "new deferred 1" }));
        check("batch deferred cancellation success cleans backups and temporary files", Directory.GetFiles(deferredDirectory, ".appwrap-*").Length == 0);

        static PreparedArtifact[] CreateArtifacts(string directory, string[] destinations)
        {
            return Enumerable.Range(0, 2).Select(index =>
            {
                var path = Path.Combine(directory, "prepared-" + index); File.WriteAllText(path, "new deferred " + index);
                return new PreparedArtifact(path, destinations[index], OutputIdentity.HashFile(path), new FileInfo(path).Length);
            }).ToArray();
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyTree(entry, target); else File.Copy(entry, target);
        }
    }
}
