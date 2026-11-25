# AppWrap.Generators

Deterministic text generation for standard installer scripts and PortableApps.com metadata. Targets Windows .NET 10 and depends only on `AppWrap.Core`. Original code uses the repository's MIT license. Generated metadata does not grant redistribution rights to the packaged application.

Public API: `InnoSetupGenerator.Generate(input, payloadDirectory, outputDirectory)`, `InnoSetupGenerator.Literal(value)`, `PortableAppsGenerator.Generate(input, previousMetadata)`, and `PortableAppsGenerator.PackageId(input)`. Call `InputRules.Validate` before invoking generators. Generator calls return strings; importing or calling them does not write files, launch compilers, execute payloads, or use the network. Unsafe literal/configuration inputs throw `UserInputException`.

```csharp
using AppWrap.Core;
using AppWrap.Generators;
InputRules.Validate(input);
string script = InnoSetupGenerator.Generate(input, stagedPayload, stagedOutput);
string metadata = PortableAppsGenerator.Generate(input);
```

The application owns staging and compilation through `AppWrap.Build`. Portable metadata is not a portability transformation or a guarantee of official distribution eligibility. Backend contracts and source references are recorded in `docs/architecture.md`. Run `tools/Test-PackedModules.ps1` to verify consuming the actual packed artifacts independently.

Standard shortcut booleans specify the checked defaults of stable optional `startmenuicon`/`desktopicon` Tasks; both choices remain available to the installing user. `StandardOptions` controls offered final-page app launch, Start with Windows and developer website actions. Website is unchecked and native shell execution is restricted to an explicit interactive Finish; all native actions skip silent setup.

Startup uses an exact current-user `AppWrap-<stable-id>.lnk`, verified target, empty arguments and executable-parent working directory. Existing links must match the current or validated previous installed tuple, with linked paths rejected. Fresh startup is unchecked; only Finish changes the preference. Silent upgrades and later builds without the offer migrate only an already-owned existing link. Previous-data metadata retains the last-owned tuple and uninstall caches it before registry removal; exact cleanup preserves foreign/malformed replacements. Generated script ownership checks reduce accidental interference, but do not provide a sandbox against concurrent same-user filesystem changes.
