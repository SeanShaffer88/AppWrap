# AppWrap.Build

Windows .NET 10 packaging orchestration. Depends on `AppWrap.Core`, `AppWrap.Generators`, and `AppWrap.Imaging`; original AppWrap code uses the repository's MIT license. Compiler distributions are separate, versioned tools with their own licenses and provenance. They are not embedded in this NuGet package.

Public API: `BuildService(baseDirectory, stateDirectory).BuildAsync(input, cancellationToken)`, `BuildResult`, `BuildFailureException`, `ProcessRunner.RunAsync(...)`, and `ProcessResult`. Construction/import has no background or filesystem side effects. An explicit build snapshots the selected source, creates temporary staging beneath the supplied state boundary, invokes the selected trusted backend, and commits output only after success. Cancellation/failure preserves existing output. The caller must supply writable, app-owned state/tool boundaries and clean up retained state after use. Payload applications are never launched by the service.

```csharp
using AppWrap.Build;
var service = new BuildService(bundledToolsRoot, appOwnedStateRoot);
BuildResult result = await service.BuildAsync(input, cancellationToken);
```

`ProcessRunner` is a low-level trusted-tool adapter, not a sandbox. It uses argument arrays, bounded logs and deadlines, and Windows Job Object cleanup. Its immediate post-start job assignment has a documented startup race; do not launch untrusted programs through it. Build error details are intended for an explicit diagnostic disclosure.

Run `tools/Test-PackedModules.ps1` for package-only consumption and cancellation/output-preservation smoke checks in an independent project. The full application harness additionally verifies actual compilers. Installer execution, upgrade behavior, and arbitrary payload portability require separate qualification.
