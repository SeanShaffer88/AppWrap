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
