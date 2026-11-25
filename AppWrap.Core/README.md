# AppWrap.Core

`PackageNaming.GetFileName(name, version, mode, style)` is the canonical pure naming API. `PackageNamingStyle` offers `NameVersionSetup` (default), `NameOnly` and `NameSetupVersion`; `Labels` supplies readable UI choices and `ValidateName` applies the shared Windows filename rules. An unknown saved style falls back to the current default. The entered valid version is retained in the output filename, while `InputRules.PackageVersion` separately produces four-part metadata.

Windows .NET 10 input contracts and filesystem validation, shared by the wizard and packaging services. Original AppWrap code uses the repository's MIT license; there are no third-party package dependencies.

Public API: immutable `AppWrapInput`, `OutputMode`, `UserInputException`, and `InputRules` (`Validate`, `Executables`, `PackageVersion`, `SafeId`, `SafePath`, `IsWithin`, `Text`). Validation and executable discovery read the selected local filesystem; they do not run applications, write settings, start processes, or use the network. Importing the assembly has no background side effects. Exceptions describe rejected paths and inputs.

```csharp
using AppWrap.Core;
var input = new AppWrapInput(source, "My App", "1.2", "MyApp.exe", true, false, output);
InputRules.Validate(input);
var fourPartVersion = InputRules.PackageVersion(input.AppVersion); // 1.2.0.0
```

The final optional positional argument `StandardOptions` accepts `StandardInstallerOptions(OfferLaunchAfterInstall = true, OfferStartWithWindows = false, OfferDeveloperWebsite = false, DeveloperWebsite = null)`. Existing calls retain these defaults. `StandardInstallerOptions.Normalize(null)` returns those defaults; when the website offer is enabled it returns a normalized absolute HTTP/HTTPS URL or throws `UserInputException`. `ValidateWebsite` rejects credentials, raw quotes/backticks, control characters, whitespace, backslashes, relative/custom schemes and values over 2048 characters. These two methods perform no filesystem or network I/O. Portable inputs ignore this standard-only option, including when paired with a standard input carrying different options.

```csharp
var standard = input with { StandardOptions = new(false, true, true, "https://example.org/app") };
InputRules.Validate(standard);
```

## PortableProfiles (P0–P1)

`AppWrap.Core.PortableProfiles` began as the configuration-only first slice of portable profiles; P1 adds the real PAL path on top of it. The final optional positional argument `Profile` on `AppWrapInput` accepts a `PortableProfile`; `null` (the default) retains the existing packaging-only behaviour, and a `Pal` profile is only meaningful for portable packages (the standard build rejects it). `PortableLauncherGenerator` (in `AppWrap.Generators`) serializes the profile into the launcher INI — defaulting `ProgramExecutable` to the payload executable under `Payload\` and refusing rooted, parent-escaping or non-payload targets — and derives the pinned compiler's conditional defines, and `PalCompiler` (in `AppWrap.Build`) compiles a real root PAL launcher with the hash-pinned backend (see the `AppWrap.Build` README). Evidence level: **Configuration validated; PAL compilation requires the pinned Windows backend and is Unverified on non-Windows hosts** — the bundled `makensis.exe` is a Windows binary, so no PAL compile is claimed from non-Windows evidence. `IniDocument` is a lossless ordered INI AST that preserves comments, blank lines, repeated keys (including repeated `-` discard rows) and the original line endings, round-tripping unedited text byte-for-byte while collecting parse issues instead of throwing on content. `PortableProfile` (schema version 1) carries the execution kind, the pinned `EngineLock` (PAL 2.2.9 / NSIS 2.46.5-Unicode, hash-pinned), launcher/installer/metadata/user-override documents, assets and trusted extensions; `PortableProfile.PackagingOnly()` retains the existing packaging-only behaviour. `PortableProfileCodec` imports/serializes INI text inertly — imported content never grants trust (`TrustedExtension.Trusted` stays false unless set by an explicit in-product action). `CapabilityCatalog` is the versioned pinned-PAL key/section/token knowledge, and `PortableProfileValidator.Validate` returns ordered `ProfileDiagnostic` values (unknown/deprecated/unsupported keys, duplicate singletons, sequential gaps, section-case, uncovered registry writes, unsafe wait/force-cleanup combinations, broad registry/directory targets, unknown or forward-referenced tokens, and control characters).

Use `tools/Test-PackedModules.ps1` from the repository for an independent consumer restored from the actual local NuGet artifacts. The application regression harness covers additional malicious paths and input cases. These are local APIs for trusted host applications; validation cannot eliminate all filesystem change races.
