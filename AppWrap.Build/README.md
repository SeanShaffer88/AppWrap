# AppWrap.Build

Windows .NET 10 packaging orchestration. Depends on `AppWrap.Core`, `AppWrap.Generators`, and `AppWrap.Imaging`; original AppWrap code uses the repository's MIT license. Compiler distributions are separate, versioned tools with their own licenses and provenance. They are not embedded in this NuGet package.

Public API: `BuildService(baseDirectory, stateDirectory).BuildAsync(input, cancellationToken)`, `BuildResult`, `BuildFailureException`, `ProcessRunner.RunAsync(...)`, and `ProcessResult`. Construction/import has no background or filesystem side effects. An explicit build snapshots the selected source, creates temporary staging beneath the supplied state boundary, invokes the selected trusted backend, and commits output only after success. Cancellation/failure preserves existing output. The caller must supply writable, app-owned state/tool boundaries and clean up retained state after use. Payload applications are never launched by the service.

```csharp
using AppWrap.Build;
var service = new BuildService(bundledToolsRoot, appOwnedStateRoot);
BuildResult result = await service.BuildAsync(input, cancellationToken);
```

`BuildBothAsync(standardInput, portableInput, token, overwriteExisting: false, progress: null, expectedOutputs: null)` validates a matching pair and captures the original application once. Both destinations must be distinct files in the same existing folder. Optional pictures are captured once; source-contained pictures come from the same payload snapshot. `BatchBuildResult` contains both ordinary results and cleanup warnings. Generators still consume the two existing `OutputMode` values.

For an interactive host, call the synchronous hash helper off the UI thread, then obtain user replacement consent, and pass the immutable identities into the build:

```csharp
OutputIdentity[] expected = await Task.Run(() => new[] {
    OutputIdentity.Capture(standardInput.OutputPath),
    OutputIdentity.Capture(portableInput.OutputPath)
});
// Host confirms replacement of existing targets here, before authorizing true.
BatchBuildResult both = await service.BuildBothAsync(standardInput, portableInput,
    cancellationToken, overwriteExisting: true, expectedOutputs: expected);
```

The default refuses existing targets. The trusted-host `overwriteExisting: true` fallback without explicit identities captures them at API entry; an interactive caller should bind pre-confirmation identities as shown. Identities are checked at entry and before publishing. The optional three-argument single-output `BuildAsync(input, token, expectedOutput)` provides the same stale-consent check without removing the original two-argument API.

Publication uses two unique sibling pending files and backups. Handled errors restore previous files when possible, preserving unrelated replacements and recovery backups on rollback failure. Cancellation is deferred during commit/rollback. This does not promise atomic paired publication after process crash/power loss or defeat an adversary concurrently modifying the same files. Failures and warnings disclose retained paths; do not automatically delete recovery backups.

`ProcessRunner` is a low-level trusted-tool adapter, not a sandbox. It uses argument arrays, bounded logs and deadlines, and Windows Job Object cleanup. Its immediate post-start job assignment has a documented startup race; do not launch untrusted programs through it. Build error details are intended for an explicit diagnostic disclosure.

Run `tools/Test-PackedModules.ps1` for package-only consumption and cancellation/output-preservation smoke checks in an independent project. The full application harness additionally verifies actual compilers. Installer execution, upgrade behavior, and arbitrary payload portability require separate qualification.
